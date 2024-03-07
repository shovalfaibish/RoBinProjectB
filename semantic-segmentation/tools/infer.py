import time
import torch
import yaml
import math
import threading
import os
from torch import Tensor
from torch.nn import functional as F
from pathlib import Path
from torchvision.io.image import read_image
from torchvision import transforms as T
import torchvision.transforms.functional as TF
from semseg.models import *
from semseg.datasets import *
from semseg.utils.utils import timer
from semseg.utils.visualize import draw_text
from robin_code import constants as const


##############################
# Semantic Segmentation
##############################


class SemSeg:
    def __init__(self, cfg_file: str, lab_exp=False) -> None:
        self.logfile = os.path.join(Path.home(), "RoBin_Files/Logs/Latest/Semseg_Logs.txt")

        self._initialize_from_config(cfg_file)
        self._initialize_model()
        self._initialize_preprocessing()
        self._initialize_file_paths()
        self._initialize_state(lab_exp)

    def _initialize_from_config(self, cfg_file: str) -> None:
        with open(cfg_file) as f:
            self.cfg = yaml.load(f, Loader=yaml.SafeLoader)

        self._write_log(f"Model > {self.cfg['MODEL']['NAME']} {self.cfg['MODEL']['BACKBONE']}")
        self._write_log(f"Model > {self.cfg['DATASET']['NAME']}")

        self.device = torch.device(self.cfg['DEVICE'])
        self.palette = eval(self.cfg['DATASET']['NAME']).PALETTE
        self.labels = eval(self.cfg['DATASET']['NAME']).CLASSES
        self.overlay = self.cfg['TEST']['OVERLAY']

    def _initialize_model(self) -> None:
        self.model = eval(self.cfg['MODEL']['NAME'])(self.cfg['MODEL']['BACKBONE'], len(self.palette))
        self.model.load_state_dict(torch.load(self.cfg['TEST']['MODEL_PATH'], map_location='cpu'))
        self.model = self.model.to(self.device)
        self.model.eval()

    def _initialize_preprocessing(self) -> None:
        self.size = self.cfg['TEST']['IMAGE_SIZE']
        self.tf_pipeline = T.Compose([
            T.Lambda(lambda x: x / 255),
            T.Normalize((0.485, 0.456, 0.406), (0.229, 0.224, 0.225)),
            T.Lambda(lambda x: x.unsqueeze(0))
        ])

    def _initialize_file_paths(self) -> None:
        self.test_files = Path(f"{Path.home()}/RoBin_Files/Images/")
        self.save_dir = Path(self.cfg['SAVE_DIR'])
        self.save_dir.mkdir(exist_ok=True)

    def _initialize_state(self, lab_exp) -> None:
        self.alpha = 0.25
        self.seq_pred = None  # Sequential prediction
        self.seg_result = None
        self.curr_filename = None
        self.result_lock = threading.Lock()
        self.stop_thread = False
        self.timestamp = -1
        self.lab_exp = lab_exp

    def _write_log(self, msg):
        # Write to file and flush to stdout
        with open(self.logfile, 'a') as f:
            f.write(msg + '\n')
        print(msg, flush=True)

    def preprocess(self, image: Tensor) -> Tensor:
        H, W = image.shape[1:]
        self._write_log(f"Original Image Size > {H}x{W}")

        # Scale the short side of image to target size, and make divisible by model stride
        scale_factor = self.size[0] / min(H, W)
        nH, nW = round(H * scale_factor), round(W * scale_factor)
        nH, nW = int(math.ceil(nH / 32)) * 32, int(math.ceil(nW / 32)) * 32
        self._write_log(f"Inference Image Size > {nH}x{nW}")

        # Resize image, divide by 255, norm and add batch dim
        image = T.Resize((nH, nW))(image)
        image = self.tf_pipeline(image).to(self.device)
        return image

    def postprocess(self, orig_img: Tensor, seg_map: Tensor, img_fname) -> Tensor:
        # Resize to original image size
        seg_map = F.interpolate(seg_map, size=orig_img.shape[-2:], mode='bilinear', align_corners=True)

        # Get segmentation map (value being 0 to num_classes)
        softmax_output = seg_map.softmax(dim=1)

        # Perform weighted sum - temporal smoothing
        if self.seq_pred is not None:
            pred_tensor = self.alpha * softmax_output + (1 - self.alpha) * self.seq_pred
        else:
            pred_tensor = softmax_output
        seg_map = pred_tensor.argmax(dim=1).cpu().to(int)

        # Update seq_pred for the next iteration
        self.seq_pred = pred_tensor

        # Convert segmentation map to color map
        seg_image = self.palette[seg_map].squeeze()
        # if self.overlay:
        #     seg_image = (orig_img.permute(1, 2, 0) * 0.4) + (seg_image * 0.6)

        # Save segmentation result
        with self.result_lock:
            self.seg_result = draw_text(seg_image, seg_map, self.labels)
            self.curr_filename = img_fname.stem
            if self.timestamp != -1:
                self.seg_result.save(self.save_dir / f"{self.timestamp}/semseg/{str(img_fname.stem)}.jpg")

    @torch.inference_mode()
    @timer
    def model_forward(self, img: Tensor) -> Tensor:
        return self.model(img)

    def predict(self, img_fname) -> Tensor:
        if self.timestamp == -1:
            return

        # Resize and save image
        image = read_image(str(img_fname))
        image = T.Resize((int(image.shape[1] * 0.1), int(image.shape[2] * 0.1)))(image)
        TF.to_pil_image(image).save(self.save_dir / f"{self.timestamp}/camera/{str(img_fname.stem)}.jpg")

        # if self.lab_exp:
        #     os.remove(img_fname)

        # Process
        preprocess_image = self.preprocess(image)
        seg_map = self.model_forward(preprocess_image)
        seg_map = self.postprocess(image, seg_map, img_fname)
        return seg_map

    def _perform_semantic_segmentation(self):
        while not self.stop_thread or self.timestamp == -1:
            # Get oldest / newest image from camera
            if self.lab_exp:
                files = Path(f"{const.LOCAL_CAMERA_OUTPUT_PATH}_temp").glob('*.*')
                image_file = min(files, key=lambda f: f.name, default=None)
            else:
                files = self.test_files.glob('*.*')
                image_file = max(files, key=lambda f: f.name, default=None)

            if image_file is not None:
                self.predict(image_file)
            time.sleep(2) # TODO: Change as needed
        print("STOPSS")

    def get_seg_result(self):
        with self.result_lock:
            return [self.seg_result, self.curr_filename]

    def start(self, timestamp):
        self._write_log("Started semantic segmentation process.")
        self.stop_thread = False
        self.timestamp = timestamp
        threading.Thread(target=self._perform_semantic_segmentation, name="Semseg_th").start()

    def stop(self):
        self._write_log("Stopped semantic segmentation process.")
        self.stop_thread = True
        self.timestamp = -1
        self.seq_pred = None
