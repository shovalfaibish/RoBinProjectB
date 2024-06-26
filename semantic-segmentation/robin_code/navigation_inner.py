import time
import helper as h
import constants as const
import math
import os
import threading
import mysql.connector
from mysql.connector import pooling
from datetime import datetime
from pathlib import Path
from tools.infer import SemSeg


class Navigation:
    """
    Instantiate a Navigation object.
    Used to navigate along a trail in a park.
    """

    def __init__(self, lab_exp=False):
        self.__save_output = False
        self.__stop_thread = False
        self.__nav_th = None
        self.__lab_exp = lab_exp

        self.__semseg = SemSeg(const.CFG_FILE_PATH, self.__lab_exp)
        self.__semseg.predict_on_startup()

        self.logfile = os.path.join(Path.home(), "RoBin_Files/Logs/Latest/Navigation_Logs.txt")

        self.start_task_id = ""
        self.project_id = None
        self.request_i = 1
        self.request_task_id = None

        self.db_pool = pooling.MySQLConnectionPool(pool_name="RoBinConn", pool_size=2,
                                                   user='robin', password='robin',
                                                   host='127.0.0.1', database='RobinDB')
        self.r_database, self.requests_cursor = self.create_connection_to_db()

    def create_connection_to_db(self):
        try:
            database = self.db_pool.get_connection()
            cursor = database.cursor()
            return database, cursor

        except mysql.connector.Error as err:
            self.write_log(f"MySQL error: {err}")

    @staticmethod
    def disconnect_from_db(database, cursor):
        cursor.close()
        database.close()

    def write_log(self, msg):
        # Write to file and flush to stdout
        with open(self.logfile, 'a') as f:
            f.write(msg + '\n')
        print(msg, flush=True)

    @staticmethod
    def _create_driver_request(cmd, cmd_val, cmd_speed=-1, cmd_time=-1, cmd_quadrant=None, cmd_radius=None):
        data = f"Driver,{cmd},{cmd_val},{cmd_speed},{cmd_time}"
        if cmd_quadrant:
            data += f",{cmd_quadrant},{cmd_radius}"
        return data

    @staticmethod
    def _create_camera_request(cmd):
        if cmd == "Start":
            return f"Camera,{cmd},0,True,1280x720"
        if cmd == "Stop":
            return f"Camera,{cmd},-1,False,False"

    def _send_request_to_module(self, data):
        self.request_task_id = str(self.project_id) + ".1." + str(self.request_i)

        # Insert new request
        self.requests_cursor.execute("INSERT INTO navigationrequests "
                                     "(TaskID, Data, Status) "
                                     f"VALUES ('{self.request_task_id}', '{data}', 'NEW')")

        # Update ModuleJobs, and set 2nd bit of ModuleStatus to 1
        self.requests_cursor.execute("UPDATE modulejobs "
                                     "SET ManagerStatus=0, ModuleStatus = ModuleStatus | 2 "
                                     "WHERE Module='Navigation'")
        self.r_database.commit()

        self.write_log(f"Sent request {self.request_task_id} Values: {data}")
        self.request_i += 1

    def _get_request_status(self, request_task_id):
        if self.__stop_thread:
            return None

        self.r_database.commit()
        self.requests_cursor.execute("SELECT Status "
                                     "FROM navigationrequests "
                                     f"WHERE TaskID='{request_task_id}'")
        result = self.requests_cursor.fetchone()
        return result[0] if result else None

    def _wait_for_request_done(self, request_task_id):
        iteration = 1  # To solve bug: request is DONE but status isn't updated
        while not self.__stop_thread:
            status = self._get_request_status(request_task_id)
            if status is None:  # Error
                raise "Error in function '_wait_for_request_done': caught Status=None"
            if status == "DONE" or iteration == 4:
                self.write_log(f"Request {request_task_id} DONE.")
                break
            time.sleep(const.TIME_PAUSE)
            iteration += 1

    def _terminate_all_requests(self):
        self.requests_cursor.execute("UPDATE navigationrequests "
                                     "SET Status='DONE' " 
                                     "WHERE Status='NEW' OR Status='RUNNING'")
        self.r_database.commit()

    def _turn(self, degrees):
        degrees = int(degrees * const.DEGREES_FIX)
        if abs(degrees) < const.DEGREES_THRESHOLD:
            self.write_log(f"No adjustment: {abs(degrees)} degrees is below threshold")
            if self.request_i <= 2:
                # Infinity forward
                self._send_request_to_module(self._create_driver_request("Forward", -1, -1))
            return

        # Turn right or left
        if degrees > 0:
            self._send_request_to_module(self._create_driver_request("Right", degrees, 40))

        else:
            self._send_request_to_module(self._create_driver_request("Left", -degrees, 40))

        # Save turn request task id
        turn_request_task_id = self.request_task_id

        # Infinity forward
        self._send_request_to_module(self._create_driver_request("Forward", -1, -1))

        # Wait for turn request status DONE
        self._wait_for_request_done(turn_request_task_id)

    def _adjust_direction_by_centroid(self, centroid_x, centroid_y):
        # Calculate image center x value
        image_center_x = const.IMG_WIDTH // 2

        # Calculate the angle in radians and convert to degrees
        angle_radians = math.atan2(centroid_x - image_center_x, const.IMG_HEIGHT - centroid_y)
        angle_degrees = math.degrees(angle_radians)

        self._turn(angle_degrees)

    def start(self, save_output):
        self.write_log("Started navigation process.")
        self.__save_output = save_output
        self.__stop_thread = False
        self.project_id = datetime.now().strftime("%d%m%y_%H%M%S%f")

        # Send request to start camera, and start navigation thread
        if not self.__lab_exp:
            self._send_request_to_module(self._create_camera_request("Start"))
            self._wait_for_request_done(self.request_task_id)

        self.__nav_th = threading.Thread(target=self.navigate, name="Nav_th")
        self.__nav_th.start()

    def stop(self):
        self.write_log("Stopped navigation process.")

        # Stop navigation thread, terminate requests, and send request to stop camera
        self.__stop_thread = True
        self._terminate_all_requests()
        if not self.__lab_exp:
            self._send_request_to_module(self._create_camera_request("Stop"))

    def navigate(self):
        if self.__lab_exp:
            # Back up local camera dir to temporary dir
            h.backup_dir(const.LOCAL_CAMERA_OUTPUT_PATH)

        # Start semantic segmentation
        timestamp = datetime.now().strftime("%d-%m_%H:%M:%S")
        if self.__save_output:
            h.setup_output_folders(timestamp)
        self.__semseg.start(timestamp, self.__save_output)

        try:
            # Wait for segmentation results
            while not self.__stop_thread and self.__semseg.get_seg_result()[0] is None:
                time.sleep(const.TIME_PAUSE * 2)

            while not self.__stop_thread:
                # Calculate center of mass and adjust direction based on segmentation
                centroid = h.calculate_center_of_mass(self.__semseg.get_seg_result(), self.__save_output)
                if centroid:
                    self._adjust_direction_by_centroid(*centroid)
                time.sleep(const.TIME_PAUSE)  # Allow RoBin to move forward between turns

        except Exception as e:
            self.write_log("Error in function 'navigate': " + str(e))

        finally:
            # Stop semantic segmentation
            self.__semseg.stop()

            if self.__lab_exp:
                h.delete_dir(f"{const.LOCAL_CAMERA_OUTPUT_PATH}_temp")

            if self.__save_output:
                self.write_log("Creating output video...")
                h.create_output_video(timestamp)
                self.write_log("Finished creating output video.")
                pass
