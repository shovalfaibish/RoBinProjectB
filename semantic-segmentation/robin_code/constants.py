import cv2.aruco as aruco
import numpy as np


##############################
# Folder Path Constants
##############################


ROBIN_FOLDER = r"C:/Users/user/AppData/Roaming/GroundStation/"
PREFIX = ""
CAMERA_OUTPUT_PATH = f"{ROBIN_FOLDER}Images"
LOCAL_CAMERA_OUTPUT_PATH = f"{PREFIX}data/infer_kislak"
OUTPUT_FOLDERS = ["camera", "semseg", "binary"]
TRAIN_SCRIPT_PATH = f"{PREFIX}tools/train.py"


##############################
# File Path Constants
##############################


CFG_FILE_PATH = f"{PREFIX}configs/ade20k.yaml"


##############################
# Variable Constants
##############################


TARGET_COLOR = (255, 255, 255)
WHITE_THRESHOLD = 230
IMG_WIDTH, IMG_HEIGHT = 192, 108
VIDEO_WIDTH, VIDEO_HEIGHT = IMG_WIDTH * 3, IMG_HEIGHT
BLACK_IMAGE = np.zeros((IMG_HEIGHT, IMG_WIDTH, 3), np.uint8)

ARUCO_DICT = aruco.getPredefinedDictionary(aruco.DICT_6X6_250)
ARUCO_PARAMS = aruco.DetectorParameters()


##############################
# Movement Constants
##############################


TIME_PAUSE = 1.5
ARUCO_TIME_PAUSE = 5
MOVEMENT_DISTANCE = 80
DEGREES_THRESHOLD = 4
DEGREES_FIX = 0.48 * 0.9


##############################
# Training Constants
##############################
