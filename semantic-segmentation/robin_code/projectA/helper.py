import argparse
import subprocess

import numpy as np
import cv2
import math
import os

from PIL import Image
import random

import constants as const
import gpxpy
import shutil
from datetime import datetime
from geopy.distance import geodesic
from robin import Groundstation
from tools.infer import SemSeg


##############################
# Robot Functions
##############################


def setup_robot():
    station = Groundstation("robin3")
    if not station.connect():  # Connect to robot
        print("Groundstation not available.")
        exit()

    station.start()
    print("Groundstation available.")
    return station


##############################
# Globals
##############################


# sensors_file = open(const.SENSORS_FILE_PATH, 'r')
path_vector = []
gs = setup_robot()
aruco_detector = cv2.aruco.ArucoDetector(const.ARUCO_DICT, const.ARUCO_PARAMS)
sem_seg = SemSeg(const.CFG_FILE_PATH)
timestamp = None


##############################
# Classes
##############################


# Cell data in path_vector
class CellData:
    def __init__(self, gps_start_lat, gps_start_long,
                 gps_end_lat, gps_end_long):
        self.gps_start_lat = gps_start_lat
        self.gps_start_long = gps_start_long
        self.gps_end_lat = gps_end_lat
        self.gps_end_long = gps_end_long


# Location data from robot sensors file
class LocationData:
    def __init__(self, gps_long, gps_lat, altitude, yaw, pitch, roll, compass, corresponding_cell=0):
        self.gps_long = gps_long
        self.gps_lat = gps_lat
        self.altitude = altitude
        self.yaw = yaw
        self.pitch = pitch
        self.roll = roll
        self.compass = compass
        self.corresponding_cell = corresponding_cell


##############################
# Path Data
##############################


# Fill vector with data from recorded GPS (.gpx) file
def create_path_vector():
    # Parse the GPX file and extract track positions (trkpt elements)
    with open(const.GPX_FILE_PATH, "r") as gpx_file:
        gpx = gpxpy.parse(gpx_file)
        track_positions = [point for track in gpx.tracks for segment in track.segments for point in
                           segment.points]

    # Calculate the total distance of the track
    total_distance = sum(
        track_positions[i].distance_3d(track_positions[i + 1]) for i in range(len(track_positions) - 1))

    # Specify the number of segments you want
    num_segments = 10  # Adjust this based on your desired number of segments

    # Calculate the segment size
    segment_size = total_distance / num_segments

    # Initialize variables
    current_distance = 0
    current_segment = 1

    # Iterate over the track positions and create segments
    for i in range(1, len(track_positions)):
        segment_start = track_positions[i - 1]
        segment_end = track_positions[i]
        segment_distance = segment_start.distance_3d(segment_end)

        # Check if the current segment has reached the desired size
        if current_distance + segment_distance >= current_segment * segment_size:
            # Calculate the coordinates of the bounding box for the segment
            cell_data = CellData(gps_start_lat=segment_start.latitude,
                                 gps_start_long=segment_start.longitude,
                                 gps_end_lat=segment_end.latitude,
                                 gps_end_long=segment_end.longitude)

            path_vector.append(cell_data)
            current_segment += 1

        # Update the current distance
        current_distance += segment_distance


##############################
# File Functions
##############################


# Extract values from sensors file
def extract_sensors_values():
    # Read the content of the file
    input_line = sensors_file.readline().strip()

    # Check if the file is empty
    if not input_line:
        print("Error: Sensors file is empty.")
        exit()

    # Extract values
    fields = input_line.split(',')
    values = [float(field.split(':')[1]) if '.' in field.split(':')[1] else int(field.split(':')[1]) for field in
              fields]

    return values


# Delete directory 'src'
def delete_dir(src):
    if os.path.exists(src):
        print(f"Deleting: {src}...")
        shutil.rmtree(src)


# Back up directory 'src' to 'src'_temp
def backup_dir(src):
    dst = f"{src}_temp"
    delete_dir(dst)
    print(f"Backing up: {src}...")
    shutil.copytree(src, dst)


# Delete all files in folder
def delete_folder_content(folder_path):
    # List all files and directories in the folder
    items = os.listdir(folder_path)

    # Remove each file and directory
    for item in items:
        item_path = os.path.join(folder_path, item)
        if os.path.isfile(item_path):
            os.remove(item_path)
        else:
            shutil.rmtree(item_path)


# Set up output folders
def setup_output_folders():
    print("Setting up output folders...")
    global timestamp
    timestamp = datetime.now().strftime("%d-%m_%H:%M:%S")
    os.makedirs(f"output/{timestamp}", exist_ok=True)
    for folder in const.OUTPUT_FOLDERS:
        os.makedirs(f"output/{timestamp}/{folder}", exist_ok=True)


# Resize image to desired 'width' and keep aspect ratio
def resize_image(image, width):
    original_height, original_width = image.shape[:2]
    aspect_ratio = original_width / original_height
    height = int(width / aspect_ratio)
    return cv2.resize(image, (width, height))


# Copy and resize last (or first if 'lab_exp'=true) 'num_images' images
def copy_and_resize_images(src, dst, lab_exp=False, save_output=False, num_images=1, target_width=0.1):
    if lab_exp:
        # Get the first 'num_images' in 'src'
        src = f"{const.LOCAL_CAMERA_OUTPUT_PATH}_temp"
        images = sorted(os.listdir(src))[:num_images]
    else:
        # Get the last 'num_images' in 'src'
        images = sorted(os.listdir(src), reverse=True)[:num_images]

    if len(images) < num_images:
        print("Error: Not enough images in camera directory.")
        if lab_exp:
            delete_dir(src)
        return 1

    for filename in images:
        # Remove prefix
        filename = filename.replace(f"{src}/", '')
        image = cv2.imread(os.path.join(src, filename))

        # Resize image and save to 'dst'
        # resized_image = resize_image(image, int(image.shape[1] * target_width))
        resized_image = image
        cv2.imwrite(os.path.join(dst, filename), resized_image)

        if lab_exp:
            # Delete image
            os.remove(os.path.join(src, filename))

    if save_output:
        # Copy last resized image
        shutil.copy(os.path.join(dst, filename), f"output/{timestamp}/{const.OUTPUT_FOLDERS[0]}")
        return filename
    return None


# Create output video
def create_output_video(folder=None):
    print("Creating video...")
    if not folder:
        folder = timestamp
    prefix = f"output/{folder}/"
    filenames = sorted(os.listdir(f"{prefix}{const.OUTPUT_FOLDERS[0]}"))

    # Create VideoWriter object
    fourcc = cv2.VideoWriter_fourcc(*"mp4v")
    video_writer = cv2.VideoWriter(f"{prefix}{folder}.mp4", fourcc, 3, (const.VIDEO_WIDTH, const.VIDEO_HEIGHT))

    # Iterate and merge images
    for filename in filenames:
        path_camera = os.path.join(f"{prefix}{const.OUTPUT_FOLDERS[0]}", filename)
        path_semseg = os.path.join(f"{prefix}{const.OUTPUT_FOLDERS[1]}", filename)
        path_binary = os.path.join(f"{prefix}{const.OUTPUT_FOLDERS[2]}", filename)

        # Read and resize images
        image_camera = cv2.imread(path_camera)
        try:
            image_semseg = cv2.imread(path_semseg)
            image_binary = cv2.imread(path_binary)
        except FileNotFoundError:
            continue
        except cv2.error:
            continue

        # Stack images vertically
        try:
            merged_image = cv2.hconcat([image_camera, image_semseg, image_binary])
        except cv2.error:
            continue

        # Write the merged frame to the video
        video_writer.write(merged_image)

    # Release the video writer
    video_writer.release()


##############################
# Location Functions
##############################


# Check if location falls within the bounding box of cell
def is_location_in_range(cell_data, location_data):
    return (cell_data.gps_start_lat <= location_data.gps_lat <= cell_data.gps_end_lat and
            cell_data.gps_start_long <= location_data.gps_long <= cell_data.gps_end_long)


# Retrieve current location data
def get_current_location_data():
    location_data = LocationData(*extract_sensors_values())

    # Find corresponding cell on path
    for i, cell_data in enumerate(path_vector):  # Every item in vector is of type CellData
        if is_location_in_range(cell_data, location_data):
            location_data.corresponding_cell = i
            break

    return location_data


# Returns true if the current location is the starting position
def is_starting_position():
    if is_location_in_range(path_vector[0], get_current_location_data()):
        print("Robin is in starting position.")
        return True
    return False


# Returns true if the current location is the ending position
def is_ending_position():
    if is_location_in_range(path_vector[-1], get_current_location_data()):
        print("Robin is in ending position.")
        return True
    return False


##############################
# Image Processing Functions
##############################


# Apply semantic segmentation algorithm on images in folder
def apply_semantic_segmentation(filename=None, save_output=False):
    sem_seg.predict_on_folder()

    if save_output:
        # Copy last segmented image
        shutil.copy(f"{const.SEGMENTATION_OUTPUT_PATH}{filename}", f"output/{timestamp}/{const.OUTPUT_FOLDERS[1]}")


# Create a binary mask from the segmented image
def create_binary_mask(image):
    gray_image = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    _, binary_mask = cv2.threshold(gray_image, const.WHITE_THRESHOLD, 255, cv2.THRESH_BINARY)
    return binary_mask


# Merge segmentation masks
def merge_binary_masks():
    # Create a black image as the initial segmentation mask
    merged_binary_mask = np.zeros((const.IMG_HEIGHT, const.IMG_WIDTH), dtype=np.uint8)

    # Iterate masks
    for filename in os.listdir(const.SEGMENTATION_OUTPUT_PATH):
        # Read image
        file_path = os.path.join(const.SEGMENTATION_OUTPUT_PATH, filename)
        segmented_image = cv2.imread(file_path, cv2.IMREAD_COLOR)

        # Create binary mask
        binary_mask = create_binary_mask(segmented_image)

        # Merge masks
        merged_binary_mask = binary_mask + merged_binary_mask

    return merged_binary_mask


# Calculate center of mass from binary mask
def calculate_center_of_mass(filename, save_output=False):
    # Merge binary masks
    merged_binary_mask = merge_binary_masks()

    # Calculate arrow starting position (bottom center)
    start_x = const.IMG_WIDTH // 2
    start_y = const.IMG_HEIGHT - 1

    # Find contours in binary mask
    contours, _ = cv2.findContours(merged_binary_mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

    if contours:
        largest_contour = max(contours, key=cv2.contourArea)

        # Calculate moments to find centroid
        moments = cv2.moments(largest_contour)

        # Calculate centroid coordinates
        x = int(moments['m10'] / (moments['m00'] + 1e-5))
        y = int(moments['m01'] / (moments['m00'] + 1e-5))

        if save_output:
            # Draw an arrow from the bottom center to the specified coordinate
            arrow_image = merged_binary_mask.copy()
            arrow_image = cv2.arrowedLine(arrow_image, (start_x, start_y), (x, y), (150, 150, 150), 2)
            cv2.imwrite(f"output/{timestamp}/{const.OUTPUT_FOLDERS[2]}/{filename}", arrow_image)

        return x, y

    else:
        print("No centroid found.")
        if save_output:
            cv2.imwrite(f"output/{timestamp}/{const.OUTPUT_FOLDERS[2]}/{filename}", const.BLACK_IMAGE)
        return None


##############################
# Navigation Functions
##############################


# Turn right or left
def turn(degrees):
    degrees = int(degrees * const.DEGREES_FIX)
    if abs(degrees) < const.DEGREES_THRESHOLD:
        print(f"No adjustment: {abs(degrees)} degrees is below threshold")
        return

    if degrees > 0:
        gs.driver.right(degrees)
        print(f"Adjustment: Driver, Right, {degrees} degrees")

    else:
        gs.driver.left(-degrees)
        print(f"Adjustment: Driver, Left, {-degrees} degrees")


# Adjust direction by centroid
def adjust_direction_by_centroid(centroid_x, centroid_y):
    # Calculate image center x value
    image_center_x = const.IMG_WIDTH // 2

    # Calculate the angle in radians
    angle_radians = math.atan2(centroid_x - image_center_x, const.IMG_HEIGHT - centroid_y)
    # Convert angle from radians to degrees
    angle_degrees = math.degrees(angle_radians)

    turn(angle_degrees)


# Calculate bearing using GPS coordinates
# location_data: Current GPS location
def calculate_bearing(location_data, reverse):
    # Current GPS coordinates
    current_gps = (location_data.gps_lat, location_data.gps_long)

    next_cell = location_data.corresponding_cell + (-1 if reverse else 1)

    # Destination GPS coordinates - find center of next cell
    dst_gps = (
        (path_vector[next_cell].gps_start_lat + path_vector[next_cell].gps_end_lat) / 2,
        (path_vector[next_cell].gps_start_long + path_vector[next_cell].gps_end_long) / 2
    )

    bearing = geodesic(current_gps, dst_gps).bearing
    print(f"Bearing: {bearing} degrees")
    return bearing


# Adjust direction by GPS data
# bearing: Direction from current GPS location to desired GPS location
# current_heading: Current compass data
def adjust_direction_by_gps(bearing, current_heading, tolerance=10):
    # Calculate the difference between the current bearing and the wanted direction
    degrees = abs(bearing - current_heading)

    # If the difference is within the tolerance, no adjustment is needed
    if abs(degrees) <= tolerance:
        print("No adjustment by GPS needed.")
        return

    turn(degrees)


# Perform u turn
def u_turn():
    gs.driver.right(180)  # TODO: Calibrate value
    print("U turn completed.")


##############################
# Utilities
##############################


# ArUco Mark Detection
def detect_aruco():
    image = cv2.imread(gs.frame(), cv2.IMREAD_COLOR)
    (corners, ids, _) = aruco_detector.detectMarkers(image)

    if ids is not None:
        print("ArUco markers detected:")
        for i in range(len(ids)):
            print(f"Marker ID: {ids[i]}, Corner Coordinates: {corners[i]}")
        return True
    else:
        print("No ArUco markers detected.")
        return False


# Parse arguments
def parse_args():
    parser = argparse.ArgumentParser()
    parser.add_argument('--disable_gps', action='store_true', help='Disable GPS based navigation.')
    parser.add_argument('--disable_aruco', action='store_true', help='Disable AruCo.')
    parser.add_argument('--lab_exp', action='store_true', help='Enable lab experiment. Adjust LOCAL_CAMERA_OUTPUT_PATH')
    parser.add_argument('--save_output', action='store_true', help='Save output for video.')
    return parser.parse_args()


##############################
# Training
##############################

def apply_training():
    cmd_command = f"python {const.TRAIN_SCRIPT_PATH} --cfg {const.CONFIG_FILE_PATH}"
    # Run the command
    try:
        subprocess.run(cmd_command, shell=True, check=True, cwd=os.getcwd())
    except subprocess.CalledProcessError as e:
        print(f"Command failed with return code {e.returncode}")


def replace_colors(image_path):
    # Load the RGB image
    img = Image.open(image_path)
    target_colors = [[255, 255, 255]]
    replacement_colors = [53]  # Path, Earth

    # Convert the image to a NumPy array
    img_array = np.array(img)

    # Iterate over each target color and replace it with the corresponding replacement color
    for target_color, replacement_color in zip(target_colors, replacement_colors):
        # Create a boolean mask for the current target color
        color_mask = np.all(img_array == target_color, axis=-1)

        # Replace pixels matching the current target color with the corresponding replacement color
        img_array[color_mask] = replacement_color

    # Convert the NumPy array back to an image
    result_image = Image.fromarray(img_array)

    # Convert the RGB image to grayscale
    result_image = result_image.convert('L')

    # Save the result as a PNG image
    result_image.save(image_path, format="PNG")


def replace_colors_in_folder(folder_path):
    for filename in os.listdir(folder_path):
        if filename.endswith(".png") or filename.endswith(".jpg"):
            replace_colors(os.path.join(folder_path, filename))


def convert_to_grayscale(image_path):
    # Open the image and convert to grayscale
    image = Image.open(image_path).convert('L')
    return image


# Split dataset into train and validation
def create_train_val_split(dataset_folder, split_ratio=0.8):
    image_folder = os.path.join(dataset_folder, 'images')
    annotation_folder = os.path.join(dataset_folder, 'annotations')

    # Create output folders
    output_folder = "recorded_data/train"
    output_subfolders = [os.path.join(output_folder, 'images', 'training'),
                         os.path.join(output_folder, 'annotations', 'training'),
                         os.path.join(output_folder, 'images', 'validation'),
                         os.path.join(output_folder, 'annotations', 'validation')]

    for folder in output_subfolders:
        os.makedirs(folder, exist_ok=True)

    # List image files
    image_files = os.listdir(image_folder)

    # Shuffle the list of image files
    random.shuffle(image_files)

    # Split the dataset into training and validation sets
    split_index = int(len(image_files) * split_ratio)
    train_files = image_files[:split_index]
    val_files = image_files[split_index:]

    # Copy files to the output folders
    for image_file in train_files:
        shutil.copyfile(os.path.join(image_folder, image_file), os.path.join(output_subfolders[0], image_file))

        annotation_file = image_file.replace('.jpg', '.png')
        annotation_path = os.path.join(annotation_folder, annotation_file)
        grayscale_annotation = convert_to_grayscale(annotation_path)
        grayscale_annotation.save(os.path.join(output_subfolders[1], annotation_file), 'PNG')

    for image_file in val_files:
        shutil.copyfile(os.path.join(image_folder, image_file), os.path.join(output_subfolders[2], image_file))

        annotation_file = image_file.replace('.jpg', '.png')
        annotation_path = os.path.join(annotation_folder, annotation_file)
        grayscale_annotation = convert_to_grayscale(annotation_path)
        grayscale_annotation.save(os.path.join(output_subfolders[3], annotation_file), 'PNG')


def create_video(folder_a, folder_b, output_video_path):
    # Get the list of filenames sorted by number
    filenames_a = sorted(os.listdir(folder_a))
    filenames_b = sorted(os.listdir(folder_b))

    # Create a VideoWriter object
    fourcc = cv2.VideoWriter_fourcc(*'mp4v')
    video_writer = cv2.VideoWriter(output_video_path, fourcc, 10, (1280, 360))

    for filename_a, filename_b in zip(filenames_a, filenames_b):
        # Read images
        image_a = cv2.imread(os.path.join(folder_a, filename_a))
        image_b = cv2.imread(os.path.join(folder_b, filename_b))


        # Resize images to (1280, 360)
        re_image_a = cv2.resize(image_a, (1280, 720))
        re_image_b = cv2.resize(image_b, (1280, 720))

        # Resize images by half
        image_a = cv2.resize(re_image_a, (0, 0), fx=0.5, fy=0.5)
        image_b = cv2.resize(re_image_b, (0, 0), fx=0.5, fy=0.5)

        # Combine images side by side
        combined_image = cv2.hconcat([image_a, image_b])

        # Write the combined frame to the video
        video_writer.write(combined_image)

    # Release the video writer
    video_writer.release()


def rename_and_number_images(folder_path, start_number=586):
    # Get the list of image filenames sorted by name
    image_files = sorted(os.listdir(folder_path))

    # Rename and number the images
    for i, filename in enumerate(image_files):
        # Create the new filename
        new_filename = f"{start_number + i}.jpg"

        # Construct the full paths
        old_path = os.path.join(folder_path, filename)
        new_path = os.path.join(folder_path, new_filename)

        # Rename the file
        os.rename(old_path, new_path)


def clean_images_folder(dataset_folder):
    images_folder = os.path.join(dataset_folder, 'images')
    annotations_folder = os.path.join(dataset_folder, 'annotations')

    # List annotation filenames without extensions
    annotation_filenames = [os.path.splitext(filename)[0] for filename in os.listdir(annotations_folder)]

    # Iterate through image files
    for image_filename in os.listdir(images_folder):
        # Get the filename without extension
        image_name, _ = os.path.splitext(image_filename)

        # Check if the corresponding annotation exists
        if image_name not in annotation_filenames:
            # If annotation doesn't exist, delete the image file
            image_path = os.path.join(images_folder, image_filename)
            os.remove(image_path)
            print(f"Deleted: {image_filename}")
