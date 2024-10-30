from ntpath import join
import queue
from flask import Flask, json, jsonify, request, send_file
import subprocess
import multiprocessing
import os
import sys
import io
import locale
import threading
import glob
import zipfile
import logging
import numpy as np
from werkzeug.utils import secure_filename
from PIL import Image

app = Flask(__name__)
logging.basicConfig(level=logging.INFO)

# Variabili globali per il monitoraggio dei processi
training_process = None
training_progress = 0
is_training = False
is_completed = False
export_process = None
is_exporting = False
export_completed = False

# Variabili di configurazione
CONDA_ENV = "nerfstudio"
NERFSTUDIO_TRAIN_COMMAND = "ns-train nerfacto --data DATA"
EXPORT_FOLDER = "exports/mesh"
TEMP_FOLDER = "temp"
DATA_FOLDER = "DATA"

# Fuznione per sostituire le virgole con i punti nei file di testo
def replace_commas_with_dots(file_path):
    with open(file_path, 'r', encoding='utf-8') as file:
        content = file.read().replace(',', '.')
    with open(file_path, 'w', encoding='utf-8') as file:
        file.write(content)

# Funzione per ridimensionare le immagini a 720p in modo da agevolare il training
def resize_images_to_720p(folder_path):    
    target_size = (1280, 720) 
    for root, _, files in os.walk(folder_path):
        for file in files:
            if file.lower().endswith(('.png','.jpg','.jpeg')):
                file_path = os.path.join(root,file)                
                with Image.open(file_path) as img:
                    if img.size != target_size:
                        img = img.resize(target_size, Image.LANCZOS)
                        img.save(file_path)

# Funzione per caricare e processare i dati a partire dalle immagini RGB, dai file di intrinseci ed estrinseci
def load_and_process_data(rgb_dir, intrinsics_path, extrinsics_path):
    replace_commas_with_dots(intrinsics_path)
    replace_commas_with_dots(extrinsics_path)
    
    image_paths = glob.glob(rgb_dir)
    
    image_paths = [path.replace("DATA\\", "") for path in image_paths]

    img_idxs = [int(path.split("\\")[-1].split(".")[0]) for path in image_paths]
    img_idxs = np.array(img_idxs) - 1

    intrinsic_txt = np.loadtxt(intrinsics_path)
    W, H = map(int, intrinsic_txt[-2:])
    fl_x, fl_y, cx, cy = intrinsic_txt[0], intrinsic_txt[4], intrinsic_txt[2], intrinsic_txt[5]

    extrinsics = np.loadtxt(extrinsics_path)
    poses = extrinsics[:, 1:].reshape(-1, 4, 4)[img_idxs]
    timestamps = extrinsics[:, 0][img_idxs]

    return image_paths, poses, timestamps, fl_x, fl_y, cx, cy, W, H

# Funzione per trasformare una posa dalla convenzione di Unity a quella di OpenGL (NerfStudio)
def transform_pose(pose):
    """
    Trasforma una posa da Unity (left-handed, Y-up) a OpenGL (right-handed, Y-up).
    La trasformazione tiene conto che i due sistemi differiscono per:
    - direzione degli assi X e Y (riflessi)
    - inversione della traslazione per il cambio di posizione della camera
    """
    # Estrae rotazione (3x3) e traslazione (3x1) dalla matrice di posa
    R = pose[:3, :3]  
    t = pose[:3, 3]   

    # Crea una copia della rotazione che verrà modificata
    R_new = R.copy()

    # Inverte la traslazione per gestire il cambio di direzione della camera
    # In Unity la camera guarda verso -Z, in OpenGL verso +Z
    t_new = -t.copy()

    # Matrice di riflessione che:
    # - Inverte X e Y (primi due elementi della diagonale -1)
    # - Mantiene Z invariato (ultimo elemento della diagonale 1)
    # Questo perché la camera in Unity è ruotata di 180° attorno all'asse Z 
    # rispetto alla camera in OpenGL
    R_z_reflection = np.array([
        [-1, 0, 0],
        [0, -1, 0],
        [0, 0, 1]
    ])

    # Applica la riflessione alla rotazione
    # La moltiplicazione a destra (R * R_z_reflection) è corretta perché 
    # stiamo trasformando dal sistema di coordinate locale della camera
    R_new = np.dot(R, R_z_reflection)

    # Ricostruisce la nuova matrice di posa 4x4
    pose_new = np.eye(4)
    pose_new[:3, :3] = R_new  # Inserisce la rotazione trasformata
    pose_new[:3, 3] = t_new   # Inserisce la traslazione invertita

    return pose_new

# Funzione per convertire tutte le pose da Unity a OpenGL (la funzione precedente si riguarda solo una posa)
def convert_poses(poses):
    converted_poses = []
    for pose in poses:        
        # Applica la trasformazione della posa
        converted_pose = transform_pose(pose)
        converted_poses.append(converted_pose)
    return converted_poses

# Funzione per creare il dizionario dei trasformi da esportare in formato JSON
def create_transforms_dict(image_paths, converted_poses, timestamps, fl_x, fl_y, cx, cy, W, H):
    frames = [
        {
            "file_path": f"{path.split('/')[-1]}",
            "transform_matrix": pose.tolist(),
            "timestamp": timestamp
        }
        for path, pose, timestamp in zip(image_paths, converted_poses, timestamps)
    ]

    return {
        "fl_x": fl_x, "fl_y": fl_y, "cx": cx, "cy": cy,
        "w": W, "h": H,
        "k1": 0.0, "k2": 0.0, "p1": 0.0, "p2": 0.0,
        "frames": frames
    }

def convertToOpenGL(rgb_dir, intrinsics_path, extrinsics_path, output_path):
    image_paths, poses, timestamps, fl_x, fl_y, cx, cy, W, H = load_and_process_data(rgb_dir, intrinsics_path, extrinsics_path)
    
    converted_poses = convert_poses(poses)
    
    transforms_dict = create_transforms_dict(image_paths, converted_poses, timestamps, fl_x, fl_y, cx, cy, W, H)

    with open(join(output_path, "transforms.json"), 'w') as outfile:
        json.dump(transforms_dict, outfile, indent=4)

    print(f"Processed {len(image_paths)} images")
    
def create_zip_file(folder_path, output_folder, zip_name):
    zip_file = os.path.join(output_folder, zip_name)
    with zipfile.ZipFile(zip_file, "w", zipfile.ZIP_DEFLATED) as zip_ref:
        for root, _, files in os.walk(folder_path):
            for file in files:
                file_path = os.path.join(root, file)
                zip_ref.write(file_path, os.path.relpath(file_path, folder_path), compress_type=zipfile.ZIP_DEFLATED)
    return zip_file

def get_latest_output_folder():
    output_folders = glob.glob("outputs/DATA/nerfacto/*")
    if not output_folders:
        raise ValueError("No output folder found")
    return max(output_folders, key=os.path.getmtime)

def get_export_command():
    latest_folder = get_latest_output_folder()
    return (f"ns-export poisson --load-config {latest_folder}/config.yml "
            f"--output-dir {EXPORT_FOLDER} --target-num-faces 50000 "
            "--num-pixels-per-side 2048 --num-points 1000000 --remove-outliers True "
            "--normal-method open3d --obb_center 0.0000000000 0.0000000000 0.0000000000 "
            "--obb_rotation 0.0000000000 0.0000000000 0.0000000000 "
            "--obb_scale 0.8000000000 0.8000000000 0.8000000000")

def run_command_in_conda_env(command, output_queue):
    activate_cmd = (f"call conda activate {CONDA_ENV} && "
                    if sys.platform == "win32"
                    else f"source activate {CONDA_ENV} && ")
    full_command = activate_cmd + command

    try:
        my_env = os.environ.copy()
        my_env["PYTHONIOENCODING"] = "utf-8"

        with subprocess.Popen(full_command, shell=True, stdout=subprocess.PIPE,
                              stderr=subprocess.STDOUT, text=True, bufsize=1,
                              universal_newlines=True, env=my_env, encoding="utf-8",
                              errors="replace") as process:
            for line in iter(process.stdout.readline, ""):
                output_queue.put(line)

            return_code = process.wait()
            if return_code:
                raise subprocess.CalledProcessError(return_code, command)
    except Exception as e:
        output_queue.put(f"Error: {str(e)}")

def run_training(output_queue):
    global is_training, is_completed, training_progress, training_process
    try:
        # Execute the data transformation
        convertToOpenGL(rgb_dir=os.path.join(DATA_FOLDER, "images", "*.jpg"),
                        intrinsics_path=os.path.join(DATA_FOLDER, "intrinsics.txt"),
                        extrinsics_path=os.path.join(DATA_FOLDER, "images", "coordinates.txt"),
                        output_path=DATA_FOLDER)
        
        # Then run the training command
        run_command_in_conda_env(NERFSTUDIO_TRAIN_COMMAND, output_queue)
        training_progress = 100
    except Exception as e:
        output_queue.put(f"Error in training process: {str(e)}")
        logging.error(f"Error in training process: {str(e)}", exc_info=True)
    finally:
        is_training = False
        is_completed = True
        while not output_queue.empty():
            try:
                output_queue.get_nowait()
            except queue.Empty:
                break
        output_queue.put(None)
        if training_process and training_process.is_alive():
            training_process.terminate()
            training_process.join()

def run_export(output_queue):
    try:
        export_command = get_export_command()
        run_command_in_conda_env(export_command, output_queue)
    except Exception as e:
        output_queue.put(f"Error in export process: {str(e)}")
    finally:
        output_queue.put(None)  # Signal the end of the process

def execute_script_in_conda_env(script_name, output_queue):
    try:
        run_command_in_conda_env(f"python {script_name}", output_queue)
    except Exception as e:
        output_queue.put(f"Error in script execution: {str(e)}")
    finally:
        output_queue.put(None)  # Signal the end of the process

@app.route("/upload_data", methods=["POST"])
def upload_data():
    if "file" not in request.files:
        return jsonify({"status": "Error", "message": "No file uploaded"}), 400

    file = request.files["file"]
    if file.filename == "":
        return jsonify({"status": "Error", "message": "No file selected"}), 400

    if file.filename.endswith(".zip"):
        filename = secure_filename(file.filename)
        temp_zip_path = os.path.join(TEMP_FOLDER, filename)
        os.makedirs(TEMP_FOLDER, exist_ok=True)
        file.save(temp_zip_path)
        try:
            with zipfile.ZipFile(temp_zip_path, "r") as zip_ref:
                zip_ref.extractall(DATA_FOLDER)
            os.remove(temp_zip_path)
            resize_images_to_720p(os.path.join(DATA_FOLDER, "images"))
            return jsonify({"status": "Success", "message": "File uploaded and extracted successfully"})
        except Exception as e:
            logging.error(f"Error extracting zip file: {str(e)}")
            return jsonify({"status": "Error", "message": "Error extracting zip file"}), 500
    else:
        return jsonify({"status": "Error", "message": "File must be a zip file"}), 400

@app.route("/start_training")
def start_training():
    global training_process, is_training, training_progress, is_completed
    if not is_training:
        is_training = True
        training_progress = 0
        is_completed = False
        output_queue = multiprocessing.Queue()
        training_process = multiprocessing.Process(target=run_training, args=(output_queue,))
        training_process.start()

        def print_output():
            global training_progress, is_training, is_completed
            while is_training or not is_completed:
                try:
                    line = output_queue.get(timeout=1)
                    if line is None:
                        break
                    logging.info(line.strip())
                    if "%" in line and "Loading" not in line:
                        percentage = line.split("%")[0].split()[-1].strip("(")
                        try:
                            training_progress = float(percentage)
                        except ValueError:
                            pass
                except multiprocessing.queues.Empty:
                    pass
            
            # Assicurati che il processo sia terminato
            if training_process and training_process.is_alive():
                training_process.terminate()
                training_process.join()

        output_thread = threading.Thread(target=print_output)
        output_thread.start()

        return jsonify({"status": "Success", "message": "Training started"})
    return jsonify({"status": "Error", "message": "Training already in progress"}), 400

@app.route("/start_export")
def start_export():
    global export_process, is_exporting, export_completed
    
    if is_exporting:
        return jsonify({"status": "Error", "message": "Export already in progress"}), 400
    
    is_exporting = True
    export_completed = False
    output_queue = multiprocessing.Queue()
    export_process = multiprocessing.Process(target=run_export, args=(output_queue,))
    export_process.start()

    def monitor_export():
        global is_exporting, export_completed
        while is_exporting:
            try:
                line = output_queue.get(timeout=1)
                if line is None:
                    break
                logging.info(line.strip())
            except multiprocessing.queues.Empty:
                pass
        
        is_exporting = False
        export_completed = True
        if export_process and export_process.is_alive():
            export_process.terminate()
            export_process.join()

    monitor_thread = threading.Thread(target=monitor_export)
    monitor_thread.start()

    return jsonify({"status": "Success", "message": "Export started"})

@app.route("/export_progress")
def get_export_progress():
    global is_exporting, export_completed
    if export_completed:
        return jsonify({"status": "Success", "message": "Export completed"}), 204
    elif is_exporting:
        return jsonify({"status": "In Progress", "message": "Export in progress"})
    else:
        return jsonify({"status": "Error", "message": "No export in progress"}), 400

@app.route("/stop_training")
def stop_training():
    global is_training, training_process, is_completed
    if is_training or training_process:
        is_training = False
        is_completed = False
        if training_process:
            try:
                training_process.terminate()
                training_process.join()
            except Exception as e:
                logging.error(f"Error stopping training: {str(e)}")
                return jsonify({"status": "Error", "message": "Error stopping training"}), 500
        return jsonify({"status": "Success", "message": "Training stopped"})
    return jsonify({"status": "Error", "message": "No training in progress"}), 201

@app.route("/training_progress")
def get_training_progress():
    global training_progress, is_training, is_completed, training_process
    logging.info(f"Progress: {training_progress}, In training: {is_training}, Completed: {is_completed}")
    if training_progress == 100 or is_completed:
        is_training = False
        is_completed = True
        if training_process and training_process.is_alive():
            training_process.terminate()
            training_process.join()
        return jsonify({"status": "Success", "progress": 100, "message": "Training completed"}), 204
    if not is_training:
        return jsonify({"status": "Error", "message": "No training in progress"}), 400
    return jsonify({"status": "In Progress", "progress": training_progress})

@app.route("/get_mesh")
def get_mesh():
    os.makedirs(TEMP_FOLDER, exist_ok=True)
    file_path = create_zip_file(EXPORT_FOLDER, TEMP_FOLDER, "mesh.zip")
    if os.path.exists(file_path):
        return send_file(file_path, as_attachment=True), 200
    else:
        return jsonify({"status": "Error", "message": "Mesh file not found"}), 404

# Rotta per eliminare tutti i dati caricati sul server
@app.route("/delete_server", methods=["DELETE"])
def delete_server():
    global is_training, is_exporting

    if is_training or is_exporting:
        return jsonify({"status": "Error", "message": "Non è possibile al momento eliminare il server. Training o export in corso."}), 400

    try:
        for root, dirs, files in os.walk(DATA_FOLDER, topdown=False):
            for file in files:
                os.remove(os.path.join(root, file))
            for dir in dirs:
                os.rmdir(os.path.join(root, dir))
        return jsonify({"status": "Success", "message": "All data deleted"}), 200
    except Exception as e:
        logging.error(f"Error deleting data: {str(e)}")
        return jsonify({"status": "Error", "message": "Error deleting data"}), 500

if __name__ == "__main__":
    # Encoding configuration
    if sys.platform == "win32":
        os.system("chcp 65001")

    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8", errors="replace")

    try:
        locale.setlocale(locale.LC_ALL, "en_US.UTF-8")
    except locale.Error:
        try:
            locale.setlocale(locale.LC_ALL, "C.UTF-8")
        except locale.Error:
            logging.warning("Unable to set UTF-8 locale. Some characters may not display correctly.")

    multiprocessing.set_start_method("spawn", force=True)
    app.run(debug=True, host="0.0.0.0", port=5000)