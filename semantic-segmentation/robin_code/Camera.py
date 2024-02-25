"""!
@brief Version 2.5 of the Camera module. 
Starts capturing images from an assumed connected camera (only 1 camera right now),
when it gets the order to do so by the manager.

This is the main file. It contains a CameraFrames object, that handles the processing of camera frames.
Also, samples the SensorsData table to give realtime per-frame data to the user.
Possible tasks right now:
* Start/Stop capture
* Change FPS (not used currently)
"""

import threading
import time
import os
import subprocess

import mysql.connector
import cv2
import shutil
import datetime

import dateutil.parser as dparser
from pathlib import Path
from multiprocessing import Process

from camera_inner import CameraFrames
module_path = os.path.abspath(os.getcwd())

HISTORY_WRITE = 10
ROBIN_LOGS = "{}/RoBin_Files/Logs/Latest".format(Path.home())
LOG_NAME = "Camera_Logs"
cameraThreads = 5
FPS = 20 # frames per second (for camera)
stop_thread = False # flag to stop the image taking thread
image_list = [] # list of images to send
latest_log_dir = ""
sensors_data = ""
cframes = None
start_TaskID = ""
# create a thread to look for new tasks
def look_for_tasks():
    try:
        global FPS, stop_thread, start_TaskID, cframes
        cnx_tasks = mysql.connector.connect(user='robin', password='robin',
                              host='127.0.0.1',
                              database='RobinDB')

        write_log("Started main task thread and connected it to SQL.")
        cursor_tasks = cnx_tasks.cursor()
        while True:
            # look for a flag in the ModuleJobs table that signals a new task
            cursor_tasks.execute("SELECT ManagerStatus FROM ModuleJobs WHERE Module='Camera'")
            result = cursor_tasks.fetchall()
            if len(result) == 1:
                for (ManagerStatus,) in result:
                    manager_status = ManagerStatus
                    if manager_status == 1:
                        # the new task should be in the CameraTasks table
                        cursor_tasks.execute("SELECT TaskID, Command, Value, Resolution "
                                             "FROM CameraTasks "
                                             "WHERE Status='NEW'")
                        result = cursor_tasks.fetchall()
                        if len(result) > 0:
                            for (TaskID, Type, Value, Resolution) in result:
                                if Type == "SETTINGS":
                                    FPS = Value
                                    # update the different tables to reflect the settings change
                                    cursor_tasks.execute("UPDATE CameraTasks "
                                                         "SET Status='DONE' "
                                                         "WHERE TaskID='{}'".format(TaskID))
                                    # TODO add error catching here?
                                elif Type == "Start":
                                    cframes = CameraFrames()
                                    # start the camera with time limit <value> (unlimited if value < 0)
                                    stop_thread = False
                                    threading.Thread(target=get_latest_sensors).start()
                                    cframes.start(Value, Resolution)
                                    cursor_tasks.execute("UPDATE CameraTasks "
                                                         "SET Status='RUNNING' "
                                                         "WHERE TaskID='{}'".format(TaskID))
                                    start_TaskID = TaskID
                                elif Type == "Stop":
                                    stop_thread = True
                                    cframes.stop()
                                    cframes = None
                                    cursor_tasks.execute("UPDATE CameraTasks "
                                                         "SET Status='DONE' "
                                                         "WHERE TaskID='{}' OR TaskID='{}'".format(TaskID, start_TaskID))
                                    cursor_tasks.execute("UPDATE ModuleJobs "
                                                         "SET ManagerStatus=0, ModuleStatus=1 "
                                                         "WHERE Module='Camera'")
                                    start_TaskID = ""
                            # update Modulejobs to reflect that you went over the tasks
                            cursor_tasks.execute("UPDATE ModuleJobs "
                                                 "SET ManagerStatus=0, ModuleStatus=1 "
                                                 "WHERE Module='Camera'")
            cnx_tasks.commit()
            # sleep so you don't flood the SQL
            time.sleep(0.1)						
    except mysql.connector.Error as err:
        write_log("Something went wrong with MySQL: {}".format(err))
    except Exception as e:
        write_log("Something else went wrong at 'look_for_tasks' :" + str(e))
    finally:
        cnx_tasks.close()


def get_latest_sensors():
    try:
        global sensors_data, stop_thread, cframes
        cnx_sensors = mysql.connector.connect(user='robin', password='robin',
                              host='127.0.0.1',
                              database='RobinDB')

        write_log("Started main task thread and connected it to SQL.")
        cursor_tasks = cnx_sensors.cursor()
        while True:
            if stop_thread:
                write_log("Received an order to stop capturing, aborting sensors data thread")
                break

            # look for a flag in the ModuleJobs table that signals a new task
            cursor_tasks.execute("SELECT GPS_Lon,GPS_Lat,GPS_Alt,Yaw,Pitch,Roll,Compass FROM SensorsData ORDER BY ID DESC LIMIT 1")
            result = cursor_tasks.fetchall()
            if len(result) == 1:
                for (GPS_Lon, GPS_Lat, GPS_Alt, Yaw, Pitch, Roll, Compass,) in result:
                    cframes.sensors_data = "GPS_Lon:{},GPS_Lat:{},GPS_Alt:{},Yaw:{},Pitch:{},Roll:{},Compass:{}".\
                                   format(GPS_Lon, GPS_Lat, GPS_Alt, Yaw, Pitch, Roll, Compass)
            cnx_sensors.commit()
            # sleep so you don't flood the SQL. this is 30 per second (for the equivalent 30 FPS)
            time.sleep(0.03)
    except mysql.connector.Error as err:
        write_log("Something went wrong with MySQL: {}".format(err))
    except Exception as e:
        write_log("Something else went wrong at 'get_latest_sensors' :" + str(e))
    finally:
        cnx_sensors.close()


def write_log(msg):
    global latest_log_dir
    # write to file AND flush to stdout
    logfile = "{}/{}.txt".format(ROBIN_LOGS, LOG_NAME)
    if not os.path.exists(logfile):
        open(logfile, 'x').close()
    with open(logfile, 'a') as f:
        f.write(msg + '\n')
    print (msg, flush=True)


if __name__ == '__main__':
    # set the log dir
    write_log("Camera module starting!")
    write_log("Using the log directory: {}".format(ROBIN_LOGS))

    # start the task thread and wait for it
    th = threading.Thread(target=look_for_tasks)
    th.start()
    th.join() # wait until it finishes
    # TODO should I check if it finished bc it crashed?

    # cnx_main.close()
    write_log("Main closed. Bye!")
    quit()
