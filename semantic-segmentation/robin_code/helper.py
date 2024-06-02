import numpy as np
import cv2
import os
import constants as const
import shutil
from pathlib import Path


##############################
# Globals
##############################


timestamp = None


##############################
# Dir Functions
##############################


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


def delete_images_in_folder(path):
    files = os.listdir(path)
    for file in files:
        if file.endswith('.jpg'):
            os.remove(os.path.join(path, file))


# Set up output folders
def setup_output_folders(tstamp):
    global timestamp
    timestamp = tstamp
    print("Setting up output folders...")
    os.makedirs(f"{const.PREFIX}output/{timestamp}", exist_ok=True)
    for folder in const.OUTPUT_FOLDERS:
        os.makedirs(f"{const.PREFIX}output/{timestamp}/{folder}", exist_ok=True)


##############################
# Image Processing Functions
##############################


# Create a binary mask from the segmented image
def create_binary_mask(image_info):
    # image is of PIL type
    gray_image = cv2.cvtColor(np.array(image_info[0]), cv2.COLOR_RGB2GRAY)
    _, binary_mask = cv2.threshold(gray_image, const.WHITE_THRESHOLD, 255, cv2.THRESH_BINARY)
    return binary_mask


# Calculate center of mass from binary mask
def calculate_center_of_mass(image_info, save_output):
    binary_mask = create_binary_mask(image_info)

    # Calculate arrow starting position (bottom center)
    start_x = const.IMG_WIDTH // 2
    start_y = const.IMG_HEIGHT - 1

    # Find contours in binary mask
    contours, _ = cv2.findContours(binary_mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

    if contours:
        largest_contour = max(contours, key=cv2.contourArea)

        # Calculate moments to find centroid
        moments = cv2.moments(largest_contour)

        # Calculate centroid coordinates
        x = int(moments['m10'] / (moments['m00'] + 1e-5))
        y = int(moments['m01'] / (moments['m00'] + 1e-5))

        # Save output
        # Draw an arrow from the bottom center to the specified coordinate
        if save_output:
            arrow_image = binary_mask.copy()
            arrow_image = cv2.arrowedLine(arrow_image, (start_x, start_y), (x, y), (150, 150, 150), 2)
            cv2.imwrite(f"{const.PREFIX}output/{timestamp}/{const.OUTPUT_FOLDERS[2]}/{image_info[1]}.jpg", arrow_image)

        return x, y

    else:
        print("No centroid found.")
        if save_output:
            cv2.imwrite(f"output/{timestamp}/{const.OUTPUT_FOLDERS[2]}/{image_info[1]}.jpg", const.BLACK_IMAGE)
        return None


##############################
# Utilities
##############################


# Create side by side video of camera-semseg-binary
def create_output_video(folder=None):
    if not folder:
        folder = timestamp
    prefix = f"{const.PREFIX}output/{folder}/"
    sorted_files = sorted(Path(f"{prefix}{const.OUTPUT_FOLDERS[0]}").glob('*.*'), key=lambda f: os.path.getctime(f))
    filenames = [file.name for file in sorted_files]

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
