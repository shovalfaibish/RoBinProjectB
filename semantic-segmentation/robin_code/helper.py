import numpy as np
import cv2
import os
import constants as const
import shutil


##############################
# Globals
##############################


aruco_detector = cv2.aruco.ArucoDetector(const.ARUCO_DICT, const.ARUCO_PARAMS)
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


# Set up output folders
def setup_output_folders(tstamp):
    global timestamp
    timestamp = tstamp
    print("Setting up output folders...")
    os.makedirs(f"output/{timestamp}", exist_ok=True)
    for folder in const.OUTPUT_FOLDERS:
        os.makedirs(f"output/{timestamp}/{folder}", exist_ok=True)


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
def calculate_center_of_mass(image_info):
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
        arrow_image = binary_mask.copy()
        arrow_image = cv2.arrowedLine(arrow_image, (start_x, start_y), (x, y), (150, 150, 150), 2)
        cv2.imwrite(f"output/{timestamp}/{const.OUTPUT_FOLDERS[2]}/{image_info[1]}.jpg", arrow_image)

        return x, y

    else:
        print("No centroid found.")
        cv2.imwrite(f"output/{timestamp}/{const.OUTPUT_FOLDERS[2]}/{image_info[1]}.jpg", const.BLACK_IMAGE)
        return None


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

#
# # Parse arguments
# def parse_args():
#     parser = argparse.ArgumentParser()
#     parser.add_argument('--disable_aruco', action='store_true', help='Disable AruCo.')
#     parser.add_argument('--lab_exp', action='store_true', help='Enable lab experiment. Adjust LOCAL_CAMERA_OUTPUT_PATH')
#     parser.add_argument('--save_output', action='store_true', help='Save output for video.')
#     return parser.parse_args()


# Create side by side video of camera-semseg-binary
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
