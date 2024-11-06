"""
Server Flask di HoloNerf.
Gestisce il caricamento dati, training ed esportazione di modelli 3D.

Autori: Alessio Paolucci, Marco Proietti
Versione: 1.0
"""

import os
import subprocess
import sys
import io
import queue
import glob
import logging
import zipfile
import threading
import multiprocessing
from typing import Optional
from dataclasses import dataclass
from functools import wraps
import time

import numpy as np
from PIL import Image
from flask import Flask, json, jsonify, request, send_file
from werkzeug.utils import secure_filename

# Configurazione logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

@dataclass
class Config:
    """Configurazione del server e dei parametri di elaborazione."""
    CONDA_ENV: str = "nerfstudio"
    NERFSTUDIO_TRAIN_COMMAND: str = "ns-train nerfacto --data DATA"
    EXPORT_FOLDER: str = "exports/mesh"
    DATA_FOLDER: str = "DATA"
    IMAGE_TARGET_SIZE: tuple[int, int] = (1280, 720)
    SUPPORTED_IMAGE_FORMATS: tuple[str, ...] = ('.jpg')

class ProcessState:
    """Gestisce lo stato dei processi di training ed export."""
    def __init__(self):
        self.training_process: Optional[multiprocessing.Process] = None
        self.training_progress: float = 0
        self.is_training: bool = False
        self.is_completed: bool = False
        self.export_process: Optional[multiprocessing.Process] = None
        self.is_exporting: bool = False
        self.export_completed: bool = False

config = Config()
state = ProcessState()
app = Flask(__name__)

def cleanup_temp_files():
    """Rimuove i file ZIP temporanei più vecchi di 1 ora"""
    temp_dir = config.DATA_FOLDER
    threshold = time.time() - 3600
   
    for f in os.listdir(temp_dir):
        if f.endswith('.zip'):
            path = os.path.join(temp_dir, f)
            if os.path.getctime(path) < threshold:
                try:
                    os.remove(path)
                except OSError:
                    pass
           
def retry_operation(max_attempts=5, delay=5):
    """Decorator per retry delle operazioni critiche"""
    def decorator(func):
        @wraps(func)
        def wrapper(*args, **kwargs):
            attempts = 0
            while attempts < max_attempts:
                try:
                    return func(*args, **kwargs)
                except (IOError, subprocess.CalledProcessError):
                    attempts += 1
                    if attempts == max_attempts:
                        raise
                    time.sleep(delay)
            return None
        return wrapper
    return decorator
# Serve nel file coordinates.txt dato che le coordinate sono con la virgola come separatore decimale
def replace_commas_with_dots(file_path: str) -> None:
    """
    Sostituisce le virgole con i punti nei file di testo per standardizzare i separatori decimali.
    
    Args:
        file_path: Percorso del file da modificare
    """
    try:
        with open(file_path, 'r', encoding='utf-8') as file:
            content = file.read().replace(',', '.')
        with open(file_path, 'w', encoding='utf-8') as file:
            file.write(content)
    except IOError as e:
        logger.error("Errore nella modifica del file %s: %s", file_path, e)
        raise

# Aggiungi cleanup periodico
def start_cleanup_thread():
    def periodic_cleanup():
        while True:
            cleanup_temp_files()
            time.sleep(3600)
    
    cleanup_thread = threading.Thread(target=periodic_cleanup, daemon=True)
    cleanup_thread.start()
# Serve per alleggerire il training, ridimensiona le immagini
def resize_images(folder_path: str) -> None:
    """
    Ridimensiona le immagini nella cartella specificata a risoluzione 720p.
    
    Args:
        folder_path: Percorso della cartella contenente le immagini
    """
    for root, _, files in os.walk(folder_path):
        for file in files:
            if file.lower().endswith(config.SUPPORTED_IMAGE_FORMATS):
                try:
                    file_path = os.path.join(root, file)
                    with Image.open(file_path) as img:
                        if img.size != config.IMAGE_TARGET_SIZE:
                            img = img.resize(config.IMAGE_TARGET_SIZE, Image.Resampling.LANCZOS)
                            img.save(file_path)
                except (IOError, ValueError) as e:
                    logger.error("Errore nel processare l'immagine %s: %s", file_path, e)

def load_and_process_data(rgb_dir: str, intrinsics_path: str, extrinsics_path: str):
    """
    Carica e processa i dati dalle immagini RGB e dai file di parametri intrinseci ed estrinseci.
    
    Args:
        rgb_dir: Percorso delle immagini RGB
        intrinsics_path: Percorso del file dei parametri intrinseci
        extrinsics_path: Percorso del file dei parametri estrinseci
    
    Returns:
        tuple: Tuple contenente paths delle immagini, pose, timestamps e parametri della camera
    """
    replace_commas_with_dots(intrinsics_path)
    replace_commas_with_dots(extrinsics_path)
    
    # Trova i paths delle immagini e processa gli indici
    image_paths = glob.glob(rgb_dir)
    image_paths = [path.replace(f"{config.DATA_FOLDER}/", "") for path in image_paths]
    img_idxs = np.array([int(path.split("\\")[-1].split(".")[0]) for path in image_paths], dtype=int) - 1
    # Carica i parametri intrinseci
    intrinsic_txt = np.loadtxt(intrinsics_path)
    W, H = map(int, intrinsic_txt[-2:])
    fl_x, fl_y = intrinsic_txt[0], intrinsic_txt[4]
    cx, cy = intrinsic_txt[2], intrinsic_txt[5]

    # Carica i parametri estrinseci
    extrinsics = np.loadtxt(extrinsics_path)
    poses = extrinsics[:, 1:].reshape(-1, 4, 4)[img_idxs]
    timestamps = extrinsics[:, 0][img_idxs]

    return image_paths, poses, timestamps, fl_x, fl_y, cx, cy, W, H

def transform_pose(pose: np.ndarray) -> np.ndarray:
    """
    Trasforma una posa da Unity (left-handed, Y-up) a OpenGL (right-handed, Y-up).
    
    Args:
        pose: Matrice di posa 4x4 nel formato Unity
    
    Returns:
        np.ndarray: Matrice di posa 4x4 nel formato OpenGL
    """
    # Estrae rotazione e traslazione
    R = pose[:3, :3]
    t = pose[:3, 3]

    # Crea nuova rotazione e traslazione
    R_new = R.copy()
    t_new = -t.copy()

    # Matrice di riflessione per inversione assi X e Y
    R_z_reflection = np.array([
        [-1, 0, 0],
        [0, -1, 0],
        [0, 0, 1]
    ])

    # Applica la riflessione alla rotazione
    R_new = np.dot(R, R_z_reflection)

    # Ricostruisce la matrice di posa
    pose_new = np.eye(4)
    pose_new[:3, :3] = R_new
    pose_new[:3, 3] = t_new

    return pose_new

def convert_poses(poses: np.ndarray) -> list[np.ndarray]:
    """
    Converte un array di pose da Unity a OpenGL.
    
    Args:
        poses: Array di matrici di posa nel formato Unity
    
    Returns:
        list[np.ndarray]: Lista di matrici di posa nel formato OpenGL
    """
    return [transform_pose(pose) for pose in poses]

def create_transforms_dict(image_paths: list[str], 
                         converted_poses: list[np.ndarray], 
                         timestamps: np.ndarray,
                         fl_x: float, fl_y: float, 
                         cx: float, cy: float, 
                         W: int, H: int) -> dict:
    """
    Crea il dizionario dei trasformi per il formato JSON di NerfStudio.
    
    Args:
        image_paths: Lista dei percorsi delle immagini
        converted_poses: Lista delle pose convertite
        timestamps: Array dei timestamps
        fl_x, fl_y: Parametri focali
        cx, cy: Centro ottico
        W, H: Dimensioni dell'immagine
    
    Returns:
        dict: Dizionario del transforms.json nel formato NerfStudio
    """
    frames = [
        {
            "file_path": "images\\{}".format(path.split('\\')[-1]),
            "transform_matrix": pose.tolist(),
            "timestamp": timestamp
        }
        for path, pose, timestamp in zip(image_paths, converted_poses, timestamps)
    ]

    return {
        "fl_x": fl_x, "fl_y": fl_y, 
        "cx": cx, "cy": cy,
        "w": W, "h": H,
        "k1": 0.0, "k2": 0.0, 
        "p1": 0.0, "p2": 0.0,
        "frames": frames
    }

@retry_operation()
def create_transforms_json(rgb_dir: str, 
                         intrinsics_path: str, 
                         extrinsics_path: str, 
                         output_path: str) -> None:
    """
    Crea il file transforms.json combinando tutti i dati processati.
    
    Args:
        rgb_dir: Percorso delle immagini RGB
        intrinsics_path: Percorso del file dei parametri intrinseci
        extrinsics_path: Percorso del file dei parametri estrinseci
        output_path: Percorso di output per il file JSON
    """
    try:
        # Carica e processa i dati
        data = load_and_process_data(rgb_dir, intrinsics_path, extrinsics_path)
        image_paths, poses, timestamps, fl_x, fl_y, cx, cy, W, H = data
        
        # Converte le pose e crea il dizionario
        converted_poses = convert_poses(poses)
        transforms_dict = create_transforms_dict(
            image_paths, converted_poses, timestamps,
            fl_x, fl_y, cx, cy, W, H
        )

        # Salva il file JSON
        json_path = os.path.join(output_path, "transforms.json")
        with open(json_path, 'w', encoding='utf-8') as outfile:
            json.dump(transforms_dict, outfile, indent=4)
        
        logger.info("Processate %d immagini", len(image_paths))
    except (IOError, ValueError) as e:
        logger.error("Errore nella creazione del file transforms.json: %s", e)
        raise

def create_zip_file(folder_path: str, output_folder: str, zip_name: str) -> str:
    """
    Crea un file ZIP del contenuto di una cartella.
    
    Args:
        folder_path: Percorso della cartella da comprimere
        output_folder: Cartella di destinazione del file ZIP
        zip_name: Nome del file ZIP
    
    Returns:
        str: Percorso completo del file ZIP creato
    """
    zip_file = os.path.join(output_folder, zip_name)
    try:
        with zipfile.ZipFile(zip_file, "w", zipfile.ZIP_DEFLATED) as zip_ref:
            for root, _, files in os.walk(folder_path):
                for file in files:
                    file_path = os.path.join(root, file)
                    arc_path = os.path.relpath(file_path, folder_path)
                    zip_ref.write(file_path, arc_path)
        return zip_file
    except (subprocess.CalledProcessError, OSError) as e:
        logger.error("Errore nella creazione del file ZIP: %s", e)
        raise

def get_latest_output_folder() -> str:
    """
    Trova la cartella di output più recente.
    
    Returns:
        str: Percorso della cartella più recente
    
    Raises:
        ValueError: Se non viene trovata nessuna cartella di output
    """
    output_folders = glob.glob(f"outputs/{config.DATA_FOLDER}/nerfacto/*")
    if not output_folders:
        raise ValueError("Nessuna cartella di output trovata")
    return max(output_folders, key=os.path.getmtime)

def get_export_command(obb_scaleX: float, obb_scaleY: float, obb_scaleZ: float) -> str:
    """
    Genera il comando per esportare il modello NeRF.
    
    Args:
        obb_scaleX/Y/Z: Fattori di scala per il bounding box
    
    Returns:
        str: Comando di esportazione completo
    """
    latest_folder = get_latest_output_folder()
    return (f"ns-export poisson --load-config {latest_folder}/config.yml "
            f"--output-dir {config.EXPORT_FOLDER} --target-num-faces 50000 "
            "--num-pixels-per-side 2048 --num-points 1000000 --remove-outliers True "
            "--normal-method open3d --obb_center 0.0000000000 0.0000000000 0.0000000000 "
            "--obb_rotation 0.0000000000 0.0000000000 0.0000000000 "
            f"--obb_scale {obb_scaleX} {obb_scaleY} {obb_scaleZ}")

@retry_operation()
def run_command_in_conda_env(command: str, output_queue: multiprocessing.Queue) -> None:
    """
    Esegue un comando nell'ambiente Conda specificato.
    
    Args:
        command: Comando da eseguire
        output_queue: Coda per l'output del comando
    """
    activate_cmd = f"call conda activate {config.CONDA_ENV} && " if sys.platform == "win32" else f"source activate {config.CONDA_ENV} && "
    full_command = activate_cmd + command

    try:
        my_env = os.environ.copy()
        my_env["PYTHONIOENCODING"] = "utf-8"

        with subprocess.Popen(
            full_command, 
            shell=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            bufsize=1,
            universal_newlines=True,
            env=my_env,
            encoding="utf-8",
            errors="replace"
        ) as process:
            for line in iter(process.stdout.readline, ""):
                output_queue.put(line)

            if process.wait() != 0:
                raise subprocess.CalledProcessError(process.returncode, command)
    except (subprocess.CalledProcessError, OSError) as e:
        output_queue.put(f"Errore: {str(e)}")
        logger.error("Errore nell'esecuzione del comando: %s", e)

def run_training(output_queue: multiprocessing.Queue) -> None:
    """
    Esegue il processo di training del modello NeRF.
    """
    try:
        # Crea il file transforms.json
        create_transforms_json(
            rgb_dir=os.path.join(config.DATA_FOLDER, "images", "*.jpg"),
            intrinsics_path=os.path.join(config.DATA_FOLDER, "intrinsics.txt"),
            extrinsics_path=os.path.join(config.DATA_FOLDER, "images", "coordinates.txt"),
            output_path=config.DATA_FOLDER
        )
        
        # Esegue il comando di training
        run_command_in_conda_env(config.NERFSTUDIO_TRAIN_COMMAND, output_queue)
        state.training_progress = 100
    
    except Exception as e:
        output_queue.put(f"Errore nel processo di training: {str(e)}")
        logger.error("Errore nel processo di training: %s", e, exc_info=True)
    finally:
        state.is_training = False
        state.is_completed = True
        # Svuota la coda
        while not output_queue.empty():
            try:
                output_queue.get_nowait()
            except queue.Empty:
                break
        output_queue.put(None)
        if state.training_process and state.training_process.is_alive():
            state.training_process.terminate()
            state.training_process.join()

# Route Flask
@app.route("/upload_data", methods=["POST"])
def upload_data():
    """Gestisce l'upload dei dati tramite file ZIP."""
    if "file" not in request.files:
        return jsonify({"status": "Error", "message": "Nessun file caricato"}), 400

    file = request.files["file"]
    if file.filename == "":
        return jsonify({"status": "Error", "message": "Nessun file selezionato"}), 400

    if not file.filename.endswith(".zip"):
        return jsonify({"status": "Error", "message": "Il file deve essere in formato ZIP"}), 400

    # Se la cartella data è già presente, la elimina e ricrea
    if os.path.exists(config.DATA_FOLDER):
        try:
            for root, _, files in os.walk(config.DATA_FOLDER):
                for file in files:
                    os.remove(os.path.join(root, file))
            os.rmdir(config.DATA_FOLDER)
        except OSError as e:
            logger.error("Errore nella rimozione della cartella data: %s", e)
            return jsonify({"status": "Error", "message": "Errore nella rimozione della cartella data"}), 500
    
    os.makedirs(config.DATA_FOLDER)

    try:
        filename = secure_filename(file.filename)
        temp_zip_path = os.path.join(config.DATA_FOLDER, filename)
        file.save(temp_zip_path)

        with zipfile.ZipFile(temp_zip_path, "r") as zip_ref:
            zip_ref.extractall(config.DATA_FOLDER)
        os.remove(temp_zip_path)
        
        # Ridimensiona le immagini a 720p
        resize_images(os.path.join(config.DATA_FOLDER, "images"))
        time.sleep(10)
        return jsonify({"status": "Success", "message": "File caricato ed estratto con successo"})
    except (IOError, ValueError) as e:
        logger.error("Errore nell'upload del file: %s", e)
        return jsonify({"status": "Error", "message": "Errore nell'elaborazione del file"}), 500

@app.route("/start_training")
def start_training():
    """Avvia il processo di training."""
    if not state.is_training:
        state.is_training = True
        state.training_progress = 0
        state.is_completed = False
        manager = multiprocessing.Manager()
        output_queue = manager.Queue()
        state.training_process = multiprocessing.Process(
            target=run_training, 
            args=(output_queue,)
        )
        state.training_process.start()

        def monitor_output():
            while state.is_training or not state.is_completed:
                try:
                    line = output_queue.get(timeout=1)
                    if line is None:
                        break
                    logger.info(line.strip())
                    if "%" in line and "Loading" not in line:
                        percentage = line.split("%")[0].split()[-1].strip("(")
                        try:
                            state.training_progress = float(percentage)
                        except ValueError:
                            pass
                except queue.Empty:
                    pass
            
            if state.training_process and state.training_process.is_alive():
                state.training_process.terminate()
                state.training_process.join()

        threading.Thread(target=monitor_output).start()
        return jsonify({"status": "Success", "message": "Training avviato"})
    return jsonify({"status": "Error", "message": "Training già in corso"}), 400

def run_export(output_queue: multiprocessing.Queue, obb_scaleX: float, obb_scaleY: float, obb_scaleZ: float) -> None:
    """
    Esegue il processo di esportazione del modello.
    """
    try:
        export_command = get_export_command(obb_scaleX, obb_scaleY, obb_scaleZ)
        run_command_in_conda_env(export_command, output_queue)
    except Exception as e:
        output_queue.put(f"Errore nel processo di esportazione: {str(e)}")
        logger.error("Errore nel processo di esportazione: %s", e)
    finally:
        output_queue.put(None)

@app.route("/start_export", methods=["POST"])
def start_export():
    """Avvia il processo di esportazione."""
    if state.is_exporting:
        return jsonify({"status": "Error", "message": "Esportazione già in corso"}), 401

    try:
        data = request.get_json()
        obb_scale_x = data.get("x")
        obb_scale_y = data.get("y")
        obb_scale_z = data.get("z")
        
        if any(x is None for x in [obb_scale_x, obb_scale_y, obb_scale_z]):
            return jsonify({"status": "Error", "message": "Parametri di scala mancanti"}), 404
        else: 
            if obb_scale_x <= 0 or obb_scale_y <= 0 or obb_scale_z <= 0:
                return jsonify({"status": "Error", "message": "Parametri non validi (devono essere > 0)"}), 404

    except (ValueError, TypeError):
        return jsonify({"status": "Error", "message": "Parametri di scala non validi"}), 404

    state.is_exporting = True
    state.export_completed = False
    output_queue = multiprocessing.Queue()
    state.export_process = multiprocessing.Process(
        target=run_export, 
        args=(output_queue, obb_scale_x, obb_scale_y, obb_scale_z)
    )
    state.export_process.start()

    def monitor_export():
        while state.is_exporting:
            try:
                line = output_queue.get(timeout=1)
                if line is None:
                    break
                logger.info(line.strip())
            except queue.Empty:
                pass
        
        state.is_exporting = False
        state.export_completed = True
        if state.export_process and state.export_process.is_alive():
            state.export_process.terminate()
            state.export_process.join()

    threading.Thread(target=monitor_export).start()
    return jsonify({"status": "Success", "message": "Esportazione avviata"})

@app.route("/export_progress")
def get_export_progress():
    """Controlla lo stato dell'esportazione."""
    if state.export_completed:
        return jsonify({"status": "Success", "message": "Esportazione completata"}), 204
    elif state.is_exporting:
        return jsonify({"status": "In Progress", "message": "Esportazione in corso"})
    else:
        return jsonify({"status": "Error", "message": "Nessuna esportazione in corso"}), 400

@app.route("/stop_training")
def stop_training():
    """Interrompe il processo di training."""
    if state.is_training or state.training_process:
        state.is_training = False
        state.is_completed = False
        if state.training_process:
            try:
                state.training_process.terminate()
                state.training_process.join()
            except (OSError, subprocess.CalledProcessError) as e:
                logger.error("Errore nell'interruzione del training: %s", e)
                return jsonify({"status": "Error", "message": "Errore nell'interruzione del training"}), 500
        return jsonify({"status": "Success", "message": "Training interrotto"})
    return jsonify({"status": "Error", "message": "Nessun training in corso"}), 201

@app.route("/training_progress")
def get_training_progress():
    """Controlla lo stato del training."""
    if state.training_progress == 100 or state.is_completed:
        state.is_training = False
        state.is_completed = True
        if state.training_process and state.training_process.is_alive():
            state.training_process.terminate()
            state.training_process.join()
        return jsonify({"status": "Success", "progress": 100, "message": "Training completato"}), 204
    if not state.is_training:
        return jsonify({"status": "Error", "message": "Nessun training in corso"}), 400
    return jsonify({"status": "In Progress", "progress": state.training_progress})

@app.route("/get_mesh")
def get_mesh():
    """Scarica il modello 3D esportato."""
    try:
        file_path = create_zip_file(config.EXPORT_FOLDER, config.DATA_FOLDER, "mesh.zip")
        if os.path.exists(file_path):
            return send_file(file_path, as_attachment=True), 200
        else:
            return jsonify({"status": "Error", "message": "File mesh non trovato"}), 404
    except (OSError, ValueError) as e:
        logger.error("Errore nel recupero del mesh: %s", e)
        return jsonify({"status": "Error", "message": "Errore nel recupero del mesh"}), 500
    
if __name__ == "__main__":
    # Configurazione encoding per Windows
    if sys.platform == "win32":
        os.system("chcp 65001")

    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8", errors="replace")

    # Inizializza il multiprocessing
    multiprocessing.set_start_method("spawn", force=True)

    # Avvia il server
    app.run(debug=False, host="0.0.0.0", port=5000)
    