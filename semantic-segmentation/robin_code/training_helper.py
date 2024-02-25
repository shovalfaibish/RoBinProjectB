import subprocess
import numpy as np
import cv2
import os
from PIL import Image
import random
import constants as const
import shutil


##############################
# Training Utilities
##############################


def apply_training():
    cmd_command = f"python {const.TRAIN_SCRIPT_PATH} --cfg {const.CONFIG_FILE_PATH}"
    # Run the command
    try:
        subprocess.run(cmd_command, shell=True, check=True, cwd=os.getcwd())
    except subprocess.CalledProcessError as e:
        print(f"Command failed with return code {e.returncode}")


def replace_colors(image_path):
    # Load the RGB image
    img = Image.open(image_path)
    target_colors = [[255, 255, 255]]
    replacement_colors = [53]  # Path, Earth

    # Convert the image to a NumPy array
    img_array = np.array(img)

    # Iterate over each target color and replace it with the corresponding replacement color
    for target_color, replacement_color in zip(target_colors, replacement_colors):
        # Create a boolean mask for the current target color
        color_mask = np.all(img_array == target_color, axis=-1)

        # Replace pixels matching the current target color with the corresponding replacement color
        img_array[color_mask] = replacement_color

    # Convert the NumPy array back to an image
    result_image = Image.fromarray(img_array)

    # Convert the RGB image to grayscale
    result_image = result_image.convert('L')

    # Save the result as a PNG image
    result_image.save(image_path, format="PNG")


def replace_colors_in_folder(folder_path):
    for filename in os.listdir(folder_path):
        if filename.endswith(".png") or filename.endswith(".jpg"):
            replace_colors(os.path.join(folder_path, filename))


def convert_to_grayscale(image_path):
    # Open the image and convert to grayscale
    image = Image.open(image_path).convert('L')
    return image


# Split dataset into train and validation
def create_train_val_split(dataset_folder, split_ratio=0.8):
    image_folder = os.path.join(dataset_folder, 'images')
    annotation_folder = os.path.join(dataset_folder, 'annotations')

    # Create output folders
    output_folder = "recorded_data/train"
    output_subfolders = [os.path.join(output_folder, 'images', 'training'),
                         os.path.join(output_folder, 'annotations', 'training'),
                         os.path.join(output_folder, 'images', 'validation'),
                         os.path.join(output_folder, 'annotations', 'validation')]

    for folder in output_subfolders:
        os.makedirs(folder, exist_ok=True)

    # List image files
    image_files = os.listdir(image_folder)

    # Shuffle the list of image files
    random.shuffle(image_files)

    # Split the dataset into training and validation sets
    split_index = int(len(image_files) * split_ratio)
    train_files = image_files[:split_index]
    val_files = image_files[split_index:]

    # Copy files to the output folders
    for image_file in train_files:
        shutil.copyfile(os.path.join(image_folder, image_file), os.path.join(output_subfolders[0], image_file))

        annotation_file = image_file.replace('.jpg', '.png')
        annotation_path = os.path.join(annotation_folder, annotation_file)
        grayscale_annotation = convert_to_grayscale(annotation_path)
        grayscale_annotation.save(os.path.join(output_subfolders[1], annotation_file), 'PNG')

    for image_file in val_files:
        shutil.copyfile(os.path.join(image_folder, image_file), os.path.join(output_subfolders[2], image_file))

        annotation_file = image_file.replace('.jpg', '.png')
        annotation_path = os.path.join(annotation_folder, annotation_file)
        grayscale_annotation = convert_to_grayscale(annotation_path)
        grayscale_annotation.save(os.path.join(output_subfolders[3], annotation_file), 'PNG')


def create_training_video(folder_a, folder_b, output_video_path):
    # Get the list of filenames sorted by number
    filenames_a = sorted(os.listdir(folder_a))
    filenames_b = sorted(os.listdir(folder_b))

    # Create a VideoWriter object
    fourcc = cv2.VideoWriter_fourcc(*'mp4v')
    video_writer = cv2.VideoWriter(output_video_path, fourcc, 10, (1280, 360))

    for filename_a, filename_b in zip(filenames_a, filenames_b):
        # Read images
        image_a = cv2.imread(os.path.join(folder_a, filename_a))
        image_b = cv2.imread(os.path.join(folder_b, filename_b))

        # Resize images
        re_image_a = cv2.resize(image_a, (1280, 720))
        re_image_b = cv2.resize(image_b, (1280, 720))

        # Resize images by half
        image_a = cv2.resize(re_image_a, (0, 0), fx=0.5, fy=0.5)
        image_b = cv2.resize(re_image_b, (0, 0), fx=0.5, fy=0.5)

        # Combine images side by side
        combined_image = cv2.hconcat([image_a, image_b])

        # Write the combined frame to the video
        video_writer.write(combined_image)

    # Release the video writer
    video_writer.release()


def rename_and_number_images(folder_path, start_number=586):
    # Get the list of image filenames sorted by name
    image_files = sorted(os.listdir(folder_path))

    # Rename and number the images
    for i, filename in enumerate(image_files):
        # Create the new filename
        new_filename = f"{start_number + i}.jpg"

        # Construct the full paths
        old_path = os.path.join(folder_path, filename)
        new_path = os.path.join(folder_path, new_filename)

        # Rename the file
        os.rename(old_path, new_path)


def delete_images_without_annotation(dataset_folder):
    images_folder = os.path.join(dataset_folder, 'images')
    annotations_folder = os.path.join(dataset_folder, 'annotations')

    # List annotation filenames without extensions
    annotation_filenames = [os.path.splitext(filename)[0] for filename in os.listdir(annotations_folder)]

    # Iterate through image files
    for image_filename in os.listdir(images_folder):
        # Get the filename without extension
        image_name, _ = os.path.splitext(image_filename)

        # Check if the corresponding annotation exists
        if image_name not in annotation_filenames:
            # If annotation doesn't exist, delete the image file
            image_path = os.path.join(images_folder, image_filename)
            os.remove(image_path)
            print(f"Deleted: {image_filename}")
