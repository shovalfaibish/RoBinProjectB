import numpy as np
import os


##############################
# Folder Path Constants
##############################


ROBIN_FOLDER = r"/home/nvidia/RoBin_Files/"
PREFIX = "/home/nvidia/PycharmProjects/RoBinProjectB/semantic-segmentation/"
CAMERA_OUTPUT_PATH = f"{ROBIN_FOLDER}Images"
LOCAL_CAMERA_OUTPUT_PATH = f"{PREFIX}data/new_kislak"
OUTPUT_FOLDERS = ["camera", "semseg", "binary"]
TRAIN_SCRIPT_PATH = f"{PREFIX}tools/train.py"


##############################
# File Path Constants
##############################


CFG_FILE_PATH = f"{PREFIX}configs/ade20k.yaml"
EXAMPLE_IMAGE_PATH = f"{PREFIX}data/example.jpg"


##############################
# Variable Constants
##############################


TARGET_COLOR = (255, 255, 255)
WHITE_THRESHOLD = 230
IMG_WIDTH, IMG_HEIGHT = int(1280 * 0.1), int(720 * 0.1)
VIDEO_WIDTH, VIDEO_HEIGHT = IMG_WIDTH * 3, IMG_HEIGHT
BLACK_IMAGE = np.zeros((IMG_HEIGHT, IMG_WIDTH, 3), np.uint8)


##############################
# Movement Constants
##############################


TIME_PAUSE = 0.5
DEGREES_THRESHOLD = 4
DEGREES_FIX = 0.48 * 0.9
