import time
import os
import datetime
import threading
import subprocess

import cv2
from pathlib import Path
import shutil


class CameraFrames:
    """
    Instantiate a CameraFrames object.
    Used to capture and save frames from a connected camera, and save outside data related to a frame.

    :param __pipe_path: The path to the groundstation's named pipe.
    :type __pipe_path: string
    """

    def __init__(self):
        self.__frames = {}
        self.__sensors_data = "NA"
        self.__sensors_data_list = []
        self.__capture_length = -1
        self.__stop_thread = False
        self.__stop_thread_capture = False
        self.__cap_width = 1280
        self.__cap_height = 720
        self.ROBIN_LOGS = "{}/RoBin_Files/Logs/".format(Path.home())
        self.IMAGE_DIR = "{}/RoBin_Files/Images/".format(Path.home())
        self.TMP_IMAGE_DIR = "{}/Tmp_write/".format(self.IMAGE_DIR)

    @property
    def sensors_data(self):
        return self.__sensors_data

    @sensors_data.setter
    def sensors_data(self, value):
        self.__sensors_data = value

    def write_log(self, msg):
        # write to file AND flush to stdout
        logfile = "{}/Latest/Camera_Logs.txt".format(self.ROBIN_LOGS)
        if not os.path.exists(logfile):
            open(logfile, 'x').close()
        with open(logfile, 'a') as f:
            f.write(msg + '\n')
        print(msg, flush=True)

    def start(self, capture_length, resolution):
        self.write_log("Started Camera image capture process for {} seconds!".format(capture_length))
        self.__capture_length = capture_length
        self.__cap_width = int(resolution.split("x")[0])
        self.__cap_height = int(resolution.split("x")[1])
        Path(self.TMP_IMAGE_DIR).mkdir(parents=True, exist_ok=True)
        th_capture = threading.Thread(target=self.capture)
        th_capture.start()
        th_save = threading.Thread(target=self.save)
        th_save.start()

    def stop(self):
        self.write_log("Stopped Camera image capture process.")
        self.__stop_thread_capture = True
        while self.__stop_thread:
            time.sleep(0.1)
        with open("{}/sensors_data.txt".format(self.IMAGE_DIR), 'a') as f:
            f.writelines(self.__sensors_data_list)
        
        shutil.rmtree(self.TMP_IMAGE_DIR, ignore_errors=True)

    def capture(self):
        lines = subprocess.check_output(['v4l2-ctl', '--list-devices']).decode().split('\n')
        next = False
        video_device = "/dev/video0"
        for line in lines:
            if next:
                video_device = line.strip()
                break
            if "Camera" in line:
                next = True

        # connect to the camera
        print(self.__cap_height, self.__cap_width);
        cap = cv2.VideoCapture(int(video_device[len(video_device) - 1]))
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, self.__cap_height)
        cap.set(cv2.CAP_PROP_FRAME_WIDTH, self.__cap_width)
        width = cap.get(3)  # float `width`
        height = cap.get(4)  # float `height`
        print(self.__cap_height, self.__cap_width);
        print(height, width)
        self.__encode_param = [int(cv2.IMWRITE_JPEG_QUALITY), 100]

        ID = 0
        self.write_log("Using dimensions {}x{} and JPEG quality of {}.".format(width, height, self.__encode_param[1]))
        self.write_log("Camera connected on {}, starting capture!".format(video_device))

        # start capturing images
        t_start = time.time()
        t_current = time.time()
        capture_attempts = 0
        max_capture_attempts = 3  # allow 3 consecutive failures max
        total_frames = 0
        sensors_data_list = []
        while cap.isOpened():
            # a stop signal can be received for any reason:
            # (1) user initiated (2) manager initiated (for example if determined we disconnected from ground)
            if capture_attempts == max_capture_attempts:
                self.write_log("Couldn't capture images with the camera, aborting.")
                break
            if self.__stop_thread_capture:
                self.__stop_thread_capture = False
                self.__stop_thread = True
                self.write_log("Received an order to stop capturing, aborting.")
                self.write_log("Camera operations ended.")
                self.write_log("=============================================")
                break
            # if we have a time limit, check if its over
            if self.__capture_length > 0 and t_current > t_start + self.__capture_length:
                self.write_log("Ended the timed capture ({} seconds, {} total_frames) successfully".format(self.__capture_length, total_frames))
                stop_thread = True  # you finished capturing the images, notify the threads
                break
            ret, frame = cap.read()
            if ret == True:
                total_frames += 1
                # set the frame name
                capture_attempts = 0  # a successful frame capture resets the retry counter
                ID += 1
                timestamp = int(time.time() * 100)  # *100 to include 2 digits of MS
                filename = "{}_{}.jpg".format(ID, timestamp)
                if len(self.__frames.keys()) > 50:
                    self.__frames.clear()
                self.__frames[filename] = frame
                # give the comm module just one image at any given moment. the camera threads grab it quickly enough.
                # save the rest on the side, in case the user requested it

    def save(self):
        # try to write the image - give it 3 attempts
        while not self.__stop_thread:
            number_of_files = len(list(Path(self.IMAGE_DIR).glob('*')))
            if number_of_files > 10:
                continue
            if len(self.__frames) == 0:
                continue
            try_write = 0
            filename, frame = self.__frames.popitem()
            #filename = list(self.__frames.keys())[-1]
            #frame = self.__frames[filename]
            while try_write < 3:
                cv2.imwrite(self.TMP_IMAGE_DIR + filename, frame, self.__encode_param)
                if not os.path.exists(self.TMP_IMAGE_DIR + filename):
                    try_write += 1
                else:
                    break
            # if we finished a failed frame write attempt, restart the bigger loop and return the ID back by 1
            if try_write == 3:
                continue
            # move the file from the write dir to the main image dir
            shutil.move(self.TMP_IMAGE_DIR + filename, self.IMAGE_DIR + filename)
            # add the most recent sensors data to the JPEG metadata
            ID = filename.split("_")[0]
            self.__sensors_data_list.append("{},{}\n".format(ID, self.__sensors_data))
        self.write_log("Received an order to stop saving images, aborting.")
        self.__stop_thread = False
