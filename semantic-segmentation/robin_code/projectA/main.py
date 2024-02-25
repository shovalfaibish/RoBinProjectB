import time
import helper as h
import constants as const
import cv2
import os


##############################
# Globals
##############################


aruco_timer = 0  # To avoid reusing the same AruCo detection


##############################
# Navigation
##############################


def navigate(disable_gps=False, disable_aruco=False, lab_exp=False, save_output=False, reverse=False):
    first_iter = True  # Fix for first iteration (takes longer to run)
    distance_moved = 0
    global aruco_timer

    if not disable_aruco:
        aruco_timer = time.time()

    if lab_exp:
        # Back up local camera dir to temporary dir
        h.backup_dir(const.LOCAL_CAMERA_OUTPUT_PATH)

    while True:
        # Delete folders contents
        h.delete_folder_content(const.IMAGE_TO_SEGMENT_PATH)
        h.delete_folder_content(const.SEGMENTATION_OUTPUT_PATH)

        # Copy new images to segment
        filename = h.copy_and_resize_images(const.CAMERA_OUTPUT_PATH, const.IMAGE_TO_SEGMENT_PATH, lab_exp, save_output)
        if filename == 1:
            # Error
            break

        # Semantic segmentation timer
        semseg_timer = time.time()

        # Perform semantic segmentation
        h.apply_semantic_segmentation(filename, save_output)

    #     print(f"Semantic Segmentation time: {time.time() - semseg_timer} seconds")
    #
    #     # Calculate center of mass and adjust direction based on segmentation
    #     centroid = h.calculate_center_of_mass(filename, save_output)
    #     if centroid:
    #         h.adjust_direction_by_centroid(*centroid)
    #
    #     # Update bearing if moved a certain distance
    #     if not disable_gps and distance_moved >= const.BEARING_UPDATE_DISTANCE:
    #         distance_moved = 0
    #         # Calculate bearing and adjust direction based on GPS
    #         location_data = h.get_current_location_data()
    #         current_compass = location_data.compass
    #         new_bearing = h.calculate_bearing(location_data, reverse)
    #
    #         # Calculate the difference between compass and GPS-based bearing
    #         if abs(new_bearing - current_compass) < const.MAX_COMPASS_DIFF:
    #             h.adjust_direction_by_gps(new_bearing, current_compass)
    #
    #     # Increment distance moved since last bearing update
    #     distance_moved += const.MOVEMENT_DISTANCE
    #     h.gs.driver.forward(const.MOVEMENT_DISTANCE)
    #
    #     # Check navigation completion criteria
    #     if (not disable_aruco and (time.time() - aruco_timer > const.ARUCO_TIME_PAUSE) and h.detect_aruco()) or \
    #             (not disable_gps and (h.is_starting_position() if reverse else h.is_ending_position())):
    #         if not disable_aruco:
    #             aruco_timer = time.time()
    #         break
    #
    #     # Brief pause between movements
    #     # time.sleep(const.TIME_PAUSE)
    #
    #     # if first_iter:
    #     #     time.sleep(2 * const.TIME_PAUSE)
    #     #     first_iter = False
    #
    # print("Finished navigation on path.")


if __name__ == '__main__':
    # Parse arguments
    args = h.parse_args()

    if args.save_output:
        h.setup_output_folders()

    # Check if robot is in starting position
    while not args.disable_gps and not h.is_starting_position():
        print("Incorrect starting position! Adjust Robin.")
        time.sleep(const.TIME_PAUSE)

    # Check if robot detects ArUco mark
    while not args.disable_aruco and not h.detect_aruco():
        time.sleep(const.TIME_PAUSE)

    # Perform navigation
    navigate(args.disable_gps, args.disable_aruco, args.lab_exp, args.save_output)
    if not args.lab_exp:
        h.u_turn()
        navigate(args.disable_gps, args.disable_aruco, save_output=args.save_output, reverse=True)

    print("Completed navigation.")

    if args.save_output:
        h.create_output_video()

    # h.replace_colors_in_folder(const.ANNOTATIONS_PATH)
    # h.create_train_val_split("recorded_data/mid_train")
    # h.apply_training()
    # h.clean_images_folder("/home1/shoval/robinIS/repo/semantic-segmentation/recorded_data/mid_train/")

    h.apply_semantic_segmentation()
    h.create_video("data/infer_kislak/", "output/test_results", "output/vid6.mp4")

