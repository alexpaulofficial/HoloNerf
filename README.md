# HoloNerf

# Server API

Il Server è un'applicazione basata su Flask che gestisce l'addestramento e l'esportazione di modelli 3D utilizzando la tecnologia Neural Radiance Fields (NeRFs). Il server gestisce il caricamento dei dati, l'addestramento del modello e la funzionalità di esportazione 3D, fungendo da componente backend per il progetto HoloNerf che integra la tecnologia NeRF con la Realtà Mista attraverso HoloLens.

## Funzionalità Principali

- Elaborazione e preparazione dei dati per l'addestramento NeRF
- Ridimensionamento e standardizzazione automatica delle immagini
- Monitoraggio in tempo reale dei progressi dell'addestramento
- Esportazione mesh 3D con parametri personalizzabili
- Trasformazione del sistema di coordinate da Unity (left-handed) a OpenGL (right-handed)
- Elaborazione asincrona utilizzando multiprocessing e threading

## Endpoint API

| Endpoint             | Metodo | Descrizione                                                            | Risposta                         |
| -------------------- | ------ | ---------------------------------------------------------------------- | -------------------------------- |
| `/upload_data`       | POST   | Carica ed estrae file ZIP contenente immagini e parametri della camera | Messaggio di successo/errore     |
| `/start_training`    | GET    | Avvia l'addestramento del modello NeRF                                 | Stato dell'addestramento         |
| `/training_progress` | GET    | Restituisce lo stato attuale dell'addestramento                        | Percentuale di progresso (0-100) |
| `/stop_training`     | GET    | Interrompe il processo di addestramento in corso                       | Messaggio di successo/errore     |
| `/start_export`      | POST   | Inizia l'esportazione del modello 3D con scala personalizzata          | Stato dell'esportazione          |
| `/export_progress`   | GET    | Monitora lo stato del processo di esportazione                         | Stato dell'esportazione          |
| `/get_mesh`          | GET    | Scarica il modello 3D esportato come ZIP                               | File mesh/Messaggio di errore    |

## Trasformazione del Sistema di Coordinate

Il server implementa una trasformazione cruciale tra il sistema di coordinate left-handed di Unity (utilizzato da HoloLens) e il sistema di coordinate right-handed di OpenGL (utilizzato da NeRF). La trasformazione viene eseguita utilizzando la seguente operazione matriciale:

```python
# Matrice di trasformazione per la conversione delle pose
R_z_reflection = np.array([
    [-1, 0, 0],
    [0, -1, 0],
    [0, 0, 1]
])

# Applicata a ogni matrice di posa:
R_new = np.dot(R, R_z_reflection)
t_new = -t
```

## Codici di Risposta e Gestione Errori

| Endpoint             | Codice  | Tipo    | Messaggio                                  | Causa/Significato                    |
| -------------------- | ------- | ------- | ------------------------------------------ | ------------------------------------ |
| `/upload_data`       | 200     | Success | "File caricato ed estratto con successo"   | Upload completato correttamente      |
| `/upload_data`       | 400     | Error   | "Nessun file caricato"                     | Richiesta senza file                 |
| `/upload_data`       | 400     | Error   | "Nessun file selezionato"                  | File vuoto                           |
| `/upload_data`       | 400     | Error   | "Il file deve essere in formato ZIP"       | Formato file non valido              |
| `/upload_data`       | 500     | Error   | "Errore nell'elaborazione del file"        | Errore durante l'estrazione          |
| `/start_training`    | 200     | Success | "Training avviato"                         | Avvio training riuscito              |
| `/start_training`    | 400     | Error   | "Training già in corso"                    | Processo già attivo                  |
| `/training_progress` | **204** | Success | "Training completato"                      | **Training completato con successo** |
| `/training_progress` | 400     | Error   | "Nessun training in corso"                 | Nessun processo attivo               |
| `/stop_training`     | 200     | Success | "Training interrotto"                      | Interruzione riuscita                |
| `/stop_training`     | 201     | Error   | "Nessun training in corso"                 | Nessun processo da fermare           |
| `/start_export`      | 200     | Success | "Esportazione avviata"                     | Avvio export riuscito                |
| `/start_export`      | 401     | Error   | "Esportazione già in corso"                | Processo già attivo                  |
| `/start_export`      | 404     | Error   | "Parametri di scala mancanti"              | Parametri OBB non specificati        |
| `/start_export`      | 404     | Error   | "Parametri non validi (devono essere > 0)" | Valori scala non positivi            |
| `/export_progress`   | **204** | Success | "Esportazione completata"                  | **Export completato con successo**   |
| `/export_progress`   | 400     | Error   | "Nessuna esportazione in corso"            | Nessun processo attivo               |
| `/get_mesh`          | 200     | Success | File mesh.zip                              | Download mesh riuscito               |
| `/get_mesh`          | 404     | Error   | "File mesh non trovato"                    | File di output mancante              |
| `/get_mesh`          | 500     | Error   | "Errore nel recupero del mesh"             | Errore creazione ZIP                 |

I codici 204 sono particolarmente significativi perché:

- Indicano il completamento con successo di processi lunghi
- Permettono al client di sapere quando il processo è terminato
- Non contengono body nella risposta (diversamente dal 200)
- Sono utilizzati come trigger per passare alla fase successiva dell'applicazione

## Struttura transforms.json

Il file transforms.json è fondamentale per l'addestramento NeRF. Ecco la sua struttura con spiegazione dei parametri:

```json
{
    "fl_x": 1234.5,        // Lunghezza focale X della camera
    "fl_y": 1234.5,        // Lunghezza focale Y della camera
    "cx": 640.0,           // Centro ottico X (metà larghezza immagine)
    "cy": 360.0,           // Centro ottico Y (metà altezza immagine)
    "w": 1280,             // Larghezza immagine
    "h": 720,              // Altezza immagine
    "k1": 0.0,            // Coefficiente di distorsione radiale 1
    "k2": 0.0,            // Coefficiente di distorsione radiale 2
    "p1": 0.0,            // Coefficiente di distorsione tangenziale 1
    "p2": 0.0,            // Coefficiente di distorsione tangenziale 2
    "frames": [
        {
            "file_path": "images\\frame_001.jpg",  // Percorso relativo immagine
            "transform_matrix": [                   // Matrice di trasformazione 4x4
                [1.0, 0.0, 0.0, 0.0],
                [0.0, 1.0, 0.0, 0.0],
                [0.0, 0.0, 1.0, 0.0],
                [0.0, 0.0, 0.0, 1.0]
            ],
            "timestamp": 1234567890.123            // Timestamp acquisizione
        }
    ]
}
```

## Gestione File tramite ZIP

Il progetto utilizza il formato ZIP per due scopi principali:

1. **Input Dataset**:
   
   - Compressione delle immagini e parametri di calibrazione
   
   - Struttura attesa:
     
     ```
     dataset.zip
     ├── images/
     │   ├── frame_001.jpg
     │   ├── frame_002.jpg
     │   └── coordinates.txt
     └── intrinsics.txt
     ```

2. **Output Mesh**:
   
   - Compressione del modello 3D esportato
   
   - Contenuto:
     
     ```
     mesh.zip
     ├── mesh.obj
     ├── mesh.mtl
     └── textures/
     ```

## Elementi Caratteristici

1. **Gestione Asincrona**
   
   - Utilizzo di code multiprocesso per la comunicazione
   - Monitoraggio non bloccante dei progressi
   - Sistema di callback per aggiornamenti in tempo reale

2. **Ottimizzazione Immagini**
   
   ```python
   IMAGE_TARGET_SIZE = (1280, 720)  # Risoluzione ottimale per il training
   ```
   
   - Ridimensionamento automatico per bilanciare qualità e performance
   - Mantenimento del rapporto d'aspetto
   - Ottimizzazione dello spazio di archiviazione

3. **Esportazione con Scala Personalizzabile**   
   
   Una caratteristica distintiva del progetto è la possibilità di personalizzare la scala del modello 3D esportato attraverso il bounding box orientato (OBB - Oriented Bounding Box). I parametri vengono passati dal client al server tramite una richiesta POST in formato JSON:
   
   ```json
   {
       "x": 1.5,  // Scala sull'asse X
       "y": 2.0,  // Scala sull'asse Y
       "z": 1.0   // Scala sull'asse Z
   }
   ```
   
   Questo permette di:
   
   - Adattare le dimensioni del modello 3D alle esigenze specifiche
   - Correggere eventuali distorsioni nella scala
   - Ottimizzare il modello per la visualizzazione in HoloLens
   - Effettuare crop mirati di porzioni specifiche della scena
   
   Il comando di esportazione viene costruito dinamicamente includendo i parametri di scala:
   
   ```python
   export_command = (f"ns-export poisson "
                    f"--obb_scale {obb_scaleX} {obb_scaleY} {obb_scaleZ} "
                    f"--obb_center 0.0 0.0 0.0 "
                    f"--obb_rotation 0.0 0.0 0.0")
   ```

4. **Sistema di Logging**
   
   - Logging strutturato con timestamp
   - Tracciamento dettagliato delle operazioni
   - Gestione errori con stack trace completo

5. **Architettura Modulare**

Il server utilizza un'architettura multi-processo per gestire operazioni concorrenti:

- Processo Flask principale per la gestione delle richieste HTTP
- Processo separato per l'addestramento NeRF
- Processo dedicato per l'esportazione mesh
- Monitoraggio dei progressi basato su thread

## Note

* **Prerequisiti**:
  
  - L'ambiente Conda deve essere configurato correttamente
  - Pacchetti Python richiesti: Flask, NumPy, PIL, ecc.

* **Requisiti del Dataset**:
  
  - <u>Minimo 150 immagini raccomandate</u> per risultati ottimali
  - Le immagini devono avere una sovrapposizione sufficiente
  - Condizioni di illuminazione adeguate consigliate

* **Considerazioni Importanti**:
  
  - I file non vengono eliminati automaticamente da HoloLens (rimangono nella memoria del dispositivo), vanno quindi cancellati tramite funzione apposita
  - Per eliminare i file dal server va fatto nuovamente l'upload dei dati, le mesh vengono sovrascritte se generate nuovamente, altrimenti rimangono
  - Utilizzo di Nerfacto invece di Instant-NGP per una maggiore stabilità

* 