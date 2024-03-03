import time
import helper as h
import constants as const
import math
import os
import threading
from datetime import datetime
from pathlib import Path
from tools.infer import SemSeg


class Navigation:
    """
    Instantiate a Navigation object.
    Used to navigate along a trail in a park.
    """

    def __init__(self, lab_exp=False):
        self.__stop_thread = False
        self.__logfile = os.path.join(Path.home(), "RoBin_Files/Logs/Latest/Navigation_Logs.txt")
        self.__lab_exp = lab_exp
        self.__semseg = SemSeg(const.CFG_FILE_PATH, self.__lab_exp)

    def _write_log(self, msg):
        # Write to file and flush to stdout
        with open(self.__logfile, 'a') as f:
            f.write(msg + '\n')
        print(msg, flush=True)

    @staticmethod
    def _turn(degrees):
        # TODO: Send command to driver through SQL
        degrees = int(degrees * const.DEGREES_FIX)
        if abs(degrees) < const.DEGREES_THRESHOLD:
            print(f"No adjustment: {abs(degrees)} degrees is below threshold")
            return

        if degrees > 0:
            # gs.driver.right(degrees)
            print(f"Adjustment: Driver, Right, {degrees} degrees")

        else:
            # gs.driver.left(-degrees)
            print(f"Adjustment: Driver, Left, {-degrees} degrees")

    def _adjust_direction_by_centroid(self, centroid_x, centroid_y):
        # Calculate image center x value
        image_center_x = const.IMG_WIDTH // 2

        # Calculate the angle in radians and convert to degrees
        angle_radians = math.atan2(centroid_x - image_center_x, const.IMG_HEIGHT - centroid_y)
        angle_degrees = math.degrees(angle_radians)

        self._turn(angle_degrees)

    def start(self):
        self._write_log("Started navigation process.")
        self.__stop_thread = False
        threading.Thread(target=self.navigate).start()

    def stop(self):
        self._write_log("Stopped navigation process.")
        self.__stop_thread = True

    def navigate(self):
        if self.__lab_exp:
            # Back up local camera dir to temporary dir
            h.backup_dir(const.LOCAL_CAMERA_OUTPUT_PATH)

        # Start semantic segmentation
        timestamp = datetime.now().strftime("%d-%m_%H:%M:%S")
        h.setup_output_folders(timestamp)
        self.__semseg.start(timestamp)

        # TODO: TIME SLEEP TO MAKE SURE THERE ARE SEMSEG RESULTS?
        print("time sleep nav")
        while self.__semseg.get_seg_result()[0] is None:
            time.sleep(1)

        while not self.__stop_thread:
            # Calculate center of mass and adjust direction based on segmentation
            centroid = h.calculate_center_of_mass(self.__semseg.get_seg_result())
            if centroid:
                self._adjust_direction_by_centroid(*centroid)

            # Change to infinite
            # h.gs.driver.forward(const.MOVEMENT_DISTANCE)
            print("Driver, Forward")

            # Brief pause between movements
            time.sleep(const.TIME_PAUSE)

        # Stop semantic segmentation
        self.__semseg.stop()

        if self.__lab_exp:
            h.delete_dir(f"{const.LOCAL_CAMERA_OUTPUT_PATH}_temp")
        # TODO: AUTOMATICALLY CREATE VIDEO?
