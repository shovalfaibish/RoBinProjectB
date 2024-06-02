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
from robin_code import helper as h


##############################
# Semantic Segmentation
##############################


class SemSeg:
    def __init__(self, cfg_file: str, lab_exp=False) -> None:
        self.logfile = os.path.join(Path.home(), "RoBin_Files/Logs/Latest/Semseg_Logs.txt")
        self.semseg_th = None
        self.lab_exp = lab_exp
        self.save_output = None

        self._initialize_from_config(cfg_file)
        self._initialize_preprocessing()
        self._initialize_file_paths()

    def _initialize_from_config(self, cfg_file: str) -> None:
        with open(cfg_file) as f:
            self.cfg = yaml.load(f, Loader=yaml.SafeLoader)

        self._write_log(f"Model > {self.cfg['MODEL']['NAME']} {self.cfg['MODEL']['BACKBONE']}")
        self._write_log(f"Model > {self.cfg['DATASET']['NAME']}")

        self.device = torch.device(self.cfg['DEVICE'])
        self.palette = eval(self.cfg['DATASET']['NAME']).PALETTE
        self.labels = eval(self.cfg['DATASET']['NAME']).CLASSES
        self.overlay = self.cfg['TEST']['OVERLAY']

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

    def _initialize_state(self, timestamp, save_output) -> None:
        self.alpha = 0.25
        self.seq_pred = None  # Sequential prediction
        self.seg_result = None
        self.curr_filename = None
        self.result_lock = threading.Lock()
        self.stop_thread = False
        self.timestamp = timestamp
        self.save_output = save_output

    def _write_log(self, msg):
        # Write to file and flush to stdout
        with open(self.logfile, 'a') as f:
            f.write(msg + '\n')
        print(msg, flush=True)

    def _preprocess(self, image: Tensor) -> Tensor:
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

    def _postprocess(self, orig_img: Tensor, seg_map: Tensor, img_fname) -> None:
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

        # Save segmentation result
        with self.result_lock:
            self.seg_result = draw_text(seg_image, seg_map, self.labels)
            self.curr_filename = img_fname.stem
            if self.timestamp != -1 and self.save_output:
                self.seg_result.save(self.save_dir / f"{self.timestamp}/semseg/{str(img_fname.stem)}.jpg")

    @torch.inference_mode()
    @timer
    def _model_forward(self, img: Tensor) -> Tensor:
        return self.model(img)

    def _predict(self, img_fname) -> None:
        # Resize and save image
        try:
            self._write_log(f"Image: {img_fname}")
            image = read_image(str(img_fname))

            image = T.Resize((int(image.shape[1] * 0.1), int(image.shape[2] * 0.1)))(image)
            if self.timestamp != -1:
                if self.save_output:
                    TF.to_pil_image(image).save(self.save_dir / f"{self.timestamp}/camera/{str(img_fname.stem)}.jpg")

                if self.lab_exp:
                    os.remove(img_fname)

                # Process
                preprocess_image = self._preprocess(image)
                seg_map = self._model_forward(preprocess_image)
                self._postprocess(image, seg_map, img_fname)
        except RuntimeError as _:
            pass

    def predict_on_startup(self):
        # Run on startup to reduce initial overhead
        image = read_image(const.EXAMPLE_IMAGE_PATH)
        image = T.Resize((int(image.shape[1] * 0.1), int(image.shape[2] * 0.1)))(image)
        preprocess_image = self._preprocess(image)
        self._model_forward(preprocess_image)

    def _perform_semantic_segmentation(self):
        while not self.stop_thread:
            # Get oldest / newest image from camera
            if self.lab_exp:
                files = Path(f"{const.LOCAL_CAMERA_OUTPUT_PATH}_temp").glob('*.*')
                image_file = min(files, key=lambda f: f.name, default=None)
            else:
                files = self.test_files.glob('*.*')
                image_file = max(files, key=lambda f: os.path.getctime(f), default=None)

            if image_file is not None:
                self._predict(image_file)

            # Constantly empty camera dir to capture new photos
            if not self.lab_exp:
                h.delete_images_in_folder(const.CAMERA_OUTPUT_PATH)

            time.sleep(const.TIME_PAUSE)

    def get_seg_result(self):
        with self.result_lock:
            return [self.seg_result, self.curr_filename]

    def start(self, timestamp, save_output):
        self._write_log("Started semantic segmentation process.")
        self._initialize_state(timestamp, save_output)
        self.semseg_th = threading.Thread(target=self._perform_semantic_segmentation, name="Semseg_th")
        self.semseg_th.start()

    def stop(self):
        self._write_log("Stopped semantic segmentation process.")
        self.stop_thread = True
        self.timestamp = -1
