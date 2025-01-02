# HoloNeRF

<div align="left">
<img widht="500" height="200" src=".github/logo.png">
</div>

## Panoramica del Progetto

HoloNeRF integra la tecnologia dei Campi di Radianza Neurali (NeRF) con la Realtà Mista attraverso HoloLens, consentendo la ricostruzione e visualizzazione 3D in tempo reale. Il sistema è composto da due componenti principali: un'applicazione client HoloLens costruita con Unity e un server basato su Flask che gestisce l'addestramento NeRF e la generazione di modelli 3D.

## Architettura del Sistema

### Lato Client (HoloLens)

L'applicazione HoloLens è costruita con Unity e gestisce diverse funzionalità cruciali attraverso una serie di script C# specializzati:

#### 1. Sistema di Training (TrainingScript.cs)

- **Gestione delle Richieste**
  
  - Implementa un sistema robusto di coroutine sequenziali che si avviano solo dopo il successo della precedente
  - Utilizza il metodo `SendRequestWithRetry` per gestire le comunicazioni con il server
  - Supporta sia richieste GET che POST con gestione automatica dei retry in caso di fallimento

- **Gestione dei Dati**
  
  - Implementa la creazione di file ZIP utilizzando `System.IO.Compression`
  - Gestisce la serializzazione/deserializzazione JSON attraverso `JsonUtility`
  - Monitora il progresso del training con polling regolare al server

#### 2. Sistema di Importazione/Esportazione

- **ImportScript.cs**
  
  - Utilizza la libreria Dummiesman per il caricamento di file OBJ
  - Gestisce il posizionamento delle mesh importate nella scena
  - Implementa controlli di errore e gestione della memoria

- **ExportScript.cs**
  
  - Gestisce i parametri di scala attraverso un sistema di slider UI
  - Genera e invia JSON contenente i parametri di scala al server
  - Monitora il processo di esportazione con feedback in tempo reale

#### 3. Sistema di Gestione Memoria

- **DeleteScript.cs**
  
  - Implementa la cancellazione sicura della cartella di acquisizione
  - Include una finestra di dialogo di conferma per operazioni critiche
  - Gestisce la pulizia delle risorse e la liberazione della memoria

- **DeleteMesh.cs**
  
  - Gestisce la rimozione delle mesh importate dalla scena
  - Mantiene un array delle mesh importate per una gestione efficiente
  - Implementa la pulizia completa della scena quando richiesto

#### 4. Sistema di Acquisizione Immagini (TakePhotoHL.cs)

- Utilizza *Windows.WebCam* per l'accesso diretto alla fotocamera HoloLens

- Durante i test di acquisizione delle immagini, il sistema è stato configurato per catturare approssimativamente una foto al secondo.
  
  **ATTENZIONE!** E' stato anche testato un approccio basato su buffer in memoria che prevedeva l'accumulo delle immagini RAW e il loro salvataggio su disco solo al termine dell'acquisizione. Tuttavia, questo metodo si è rivelato impraticabile a causa dell'elevato consumo di memoria, considerando che ogni immagine RAW occupa circa 32MB. Inoltre, la conversione batch da formato RAW a JPEG, necessaria durante il salvataggio su disco, causava significative interruzioni del sistema, bloccando l'applicazione per diversi secondi anche quando eseguita a intervalli regolari. Dopo varie prove, si è optato per un approccio più lineare: acquisizione e salvataggio immediato di ogni singola immagine. Nonostante questo metodo richieda più tempo per processare ogni foto individualmente, garantisce un'esperienza di acquisizione più fluida e stabile, evitando le interruzioni improvvise che compromettevano l'usabilità del sistema nel metodo precedente.

- La fotocamera scatta in 4k ma poi le immagini vengono downscalate a 720p per ottimizzare il training. Gli intrinseci usati quindi sono:
  
  ```csharp
  // Apertura focale camera esterna Hololens 2 (presa da documentzione ufficiale)
  float fov = 64.69f;
  float imageWidth = 1280;
  float imageHeight = 720;
  
  float aspect = imageWidth / imageHeight;
  float focalLengthX = imageWidth / (2 * Mathf.Tan(fov * Mathf.Deg2Rad / 2));
  float focalLengthY = focalLengthX;
  
  // Assume che il punto principale sia al centro dell'immagine
  float principalPointX = imageWidth / 2.0f;
  float principalPointY = imageHeight / 2.0f;
  ```

- Il sistema permette di gestire in modo flessibile il processo di acquisizione delle immagini, consentendo la messa in pausa temporanea dell'acquisizione e l'avvio della pipeline per una valutazione preliminare dei risultati. In caso di risultati non ottimali, è possibile riprendere l'acquisizione dal punto di interruzione precedente, aggiungendo automaticamente le nuove immagini al dataset esistente. Il sistema mantiene la coerenza dei dati durante queste operazioni e offre anche la possibilità di cancellare completamente il dataset per iniziare una nuova sessione di acquisizione da zero.

- Nel momento esatto in cui viene acquisita un'immagine, il sistema inserisce automaticamente un marcatore visuale nella scena virtuale. Questo marcatore indica sia la posizione precisa da cui è stata scattata la foto sia l'orientamento della fotocamera in quel momento. Questa funzionalità è particolarmente utile quando si riprende l'acquisizione dopo una pausa, poiché permette di visualizzare chiaramente le aree già coperte ed evitare ridondanze, consentendo all'utente di concentrarsi su zone non ancora fotografate o che necessitano di una copertura maggiore per migliorare la qualità della ricostruzione.

### Lato Server (Flask)

Il Server è un'applicazione basata su Flask che gestisce l'addestramento e l'esportazione di modelli 3D utilizzando la tecnologia Neural Radiance Fields (NeRFs). Il server gestisce il caricamento dei dati, l'addestramento del modello e la funzionalità di esportazione 3D, fungendo da componente backend per il progetto HoloNerf che integra la tecnologia NeRF con la Realtà Mista attraverso HoloLens.

#### 1. Funzionalità Core

- Elaborazione e preparazione del dataset
- Ridimensionamento e standardizzazione automatica delle immagini
- Monitoraggio in tempo reale dei progressi dell'addestramento
- Esportazione personalizzabile delle mesh 3D
- Trasformazione del sistema di coordinate

#### 2. Endpoint API

| Endpoint             | Metodo | Descrizione                                                   | Risposta                         |
| -------------------- | ------ | ------------------------------------------------------------- | -------------------------------- |
| `/upload_data`       | POST   | Caricamento ed estrazione dataset                             | Messaggio di successo/errore     |
| `/start_training`    | GET    | Avvia l'addestramento NeRF                                    | Stato dell'addestramento         |
| `/training_progress` | GET    | Restituisce lo stato attuale dell'addestramento               | Percentuale di progresso (0-100) |
| `/stop_training`     | GET    | Interrompe il processo di addestramento                       | Messaggio di successo/errore     |
| `/start_export`      | POST   | Inizia l'esportazione del modello 3D con scala personalizzata | Stato dell'esportazione          |
| `/export_progress`   | GET    | Monitora il processo di esportazione                          | Stato dell'esportazione          |
| `/get_mesh`          | GET    | Scarica il modello 3D esportato                               | File mesh/Messaggio di errore    |

#### Codici di Risposta e Gestione Errori

| Endpoint             | Codice  | Tipo    | Messaggio                                          | Causa/Significato                    |
| -------------------- | ------- | ------- | -------------------------------------------------- | ------------------------------------ |
| `/upload_data`       | 200     | Success | "File caricato ed estratto con successo"           | Upload completato correttamente      |
| `/upload_data`       | 400     | Error   | "Nessun file caricato"                             | Richiesta senza file                 |
| `/upload_data`       | 400     | Error   | "Nessun file selezionato"                          | File vuoto                           |
| `/upload_data`       | 400     | Error   | "Il file deve essere in formato ZIP"               | Formato file non valido              |
| `/upload_data`       | 500     | Error   | "Errore nell'elaborazione del file"                | Errore generico durante l'estrazione |
| `/start_training`    | 200     | Success | "Training avviato"                                 | Avvio training riuscito              |
| `/start_training`    | 400     | Error   | "Training già in corso"                            | Processo già attivo                  |
| `/start_training`    | 400     | Error   | "Numero insufficiente di immagini per il training" | Immagini insufficienti (almeno 50)   |
| `/training_progress` | **204** | Success | "Training completato"                              | **Training completato con successo** |
| `/training_progress` | 400     | Error   | "Nessun training in corso"                         | Nessun processo attivo               |
| `/stop_training`     | 200     | Success | "Training interrotto"                              | Interruzione riuscita                |
| `/stop_training`     | 201     | Success | "Nessun training in corso"                         | Nessun processo da fermare           |
| `/stop_training`     | 500     | Error   | "Errore nell'interruzione del training"            | Errore generico di mancato stop      |
| `/start_export`      | 200     | Success | "Esportazione avviata"                             | Avvio export riuscito                |
| `/start_export`      | 401     | Error   | "Esportazione già in corso"                        | Processo già attivo                  |
| `/start_export`      | 404     | Error   | "Parametri di scala mancanti"                      | Parametri OBB non specificati        |
| `/start_export`      | 404     | Error   | "Parametri non validi (devono essere > 0)"         | Valori scala non positivi            |
| `/export_progress`   | 200     | Success | "Esportazione in corso"                            | Esportazione già avviata             |
| `/export_progress`   | **204** | Success | "Esportazione completata"                          | **Export completato con successo**   |
| `/export_progress`   | 400     | Error   | "Nessuna esportazione in corso"                    | Nessun processo attivo               |
| `/get_mesh`          | 200     | Success | File mesh.zip                                      | Download mesh riuscito               |
| `/get_mesh`          | 404     | Error   | "File mesh non trovato"                            | File di output mancante              |
| `/get_mesh`          | 500     | Error   | "Errore nel recupero della mesh"                   | Errore creazione ZIP                 |

I codici 204 sono particolarmente significativi perché:

- Indicano il completamento con successo di processi lunghi
- Permettono al client di sapere quando il processo è terminato
- Sono utilizzati come trigger per passare alla fase successiva dell'applicazione

## Dettagli Tecnici di Implementazione

### 1. Networking e Comunicazione

- Meccanismo di retry robusto (massimo 5 tentativi)

- Esecuzione sequenziale basata su coroutine

- Gestione degli errori e monitoraggio dello stato
  
  ```csharp
  private IEnumerator SendRequestWithRetry(string url, string method, 
    System.Action<UnityWebRequest> callback)
  {
    int retries = 0;
    bool success = false;
    while (!success && retries < MaxRetries) {
        // Implementazione
    }
  }
  ```

### 2. Pipeline di Elaborazione Dati

#### Elaborazione Lato Client

- Gestione dei parametri intrinseci della fotocamera
- Acquisizione e compressione delle immagini
- Pacchettizzazione del dataset

#### Elaborazione Lato Server

- Standardizzazione delle immagini (risoluzione target 1280x720)

##### Trasformazione del Sistema di Coordinate

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

### 3. Struttura dei File e Formato Dati

#### Dataset di Input (ZIP)

```
dataset.zip
├── images/
│   ├── frame_001.jpg
│   ├── frame_002.jpg
│   └── coordinates.txt
└── intrinsics.txt
```

#### Mesh di Output (ZIP)

```
mesh.zip
├── mesh.obj
├── mesh.mtl
└── textures/
```

**ATTENZIONE!** Quando si crea un file ZIP all'interno dell'Hololens, potrebbero verificarsi temporanei problemi di interfaccia. Per mitigare questo problema, è stata utilizzata una libreria di sistema e un task separato per la compressione ZIP. Sebbene questo approccio abbia ridotto significativamente il problema, può ancora causare un lieve rallentamento. 

### 4. Struttura *transforms.json*

Il file transforms.json è fondamentale per l'addestramento NeRF. Ecco la sua struttura con spiegazione dei parametri:

```json
{
    "fl_x": 1234.5,        // Lunghezza focale X della fotocamera
    "fl_y": 1234.5,        // Lunghezza focale Y della fotocamera
    "cx": 640.0,           // Centro ottico X
    "cy": 360.0,           // Centro ottico Y
    "w": 1280,             // Larghezza immagine
    "h": 720,              // Altezza immagine
    "k1": 0.0,            // Coefficiente di distorsione radiale 1
    "k2": 0.0,            // Coefficiente di distorsione radiale 2
    "p1": 0.0,            // Coefficiente di distorsione tangenziale 1
    "p2": 0.0,            // Coefficiente di distorsione tangenziale 2
    "frames": [
        {
            "file_path": "images\\frame_001.jpg", // Percorso relativo immagine
            "transform_matrix": [[...]],          // Matrice di trasformazione 4x4
            "timestamp": 1234567890.123           // Timestamp acquisizione
        }
    ]
}
```

### 5. Esportazione con Scala Personalizzabile

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

## Note di Setup e Requisiti

## Prerequisiti

### Ambiente di Sviluppo

- L'ambiente Conda deve essere configurato correttamente sul sistema
  - Assicurarsi che tutte le variabili d'ambiente siano impostate appropriatamente
  - Verificare che la versione di Conda sia compatibile con il progetto
- Pacchetti Python richiesti per il funzionamento:
  - Flask: server web
  - NumPy: elaborazione numerica
  - PIL (Python Imaging Library): manipolazione delle immagini
  - Werkzeug: libreria WSGI (Web Server Gateway Interface) utile in questo progetto per gestire in modo sicuro i nomi dei file caricati dagli utenti.

## Requisiti del Dataset

### Linee Guida per la Raccolta Dati

- È raccomandato un <u>minimo di 150 immagini</u> per ottenere risultati ottimali
  - Un numero inferiore potrebbe compromettere la qualità della ricostruzione
  - Dataset più ampi possono migliorare significativamente la precisione, senza esagerare considerando la natura delle NeRF (overfitting)
- Le immagini devono presentare una sovrapposizione sufficiente tra scatti consecutivi
  - Si consiglia una sovrapposizione minima del 30-40% tra immagini adiacenti
  - Evitare gaps significativi nella copertura della scena
- Si raccomandano condizioni di illuminazione adeguate e costanti
  - Evitare forti contrasti di luce
  - Mantenere un'illuminazione uniforme durante l'acquisizione
- I marcatori devono essere orientati correttamente nella scena
  - Verificare l'allineamento prima della raccolta dati
  - Assicurare la visibilità costante dei marcatori

## Considerazioni Importanti

### Gestione della Memoria

- I file non vengono eliminati automaticamente da HoloLens
  - Rimangono nella memoria del dispositivo
  - È necessario utilizzare l'apposita funzione di cancellazione
  - Si consiglia una pulizia periodica per ottimizzare lo spazio

### Gestione dei File sul Server

- Per eliminare i file dal server è necessario effettuare un nuovo upload dei dati
- Le mesh vengono sovrascritte se generate nuovamente
  - In caso contrario, rimangono memorizzate nel sistema
  - Occupano spazio su disco fino alla sovrascrittura

### Configurazione del Sistema

- Utilizzo di Nerfacto invece di Instant-NGP
  - Garantisce una maggiore stabilità del sistema
  - Offre risultati più consistenti con prestazioni simili
- L'<u>indirizzo IP del server richiede modifiche in Unity</u>
  - Necessaria ricompilazione dopo le modifiche
  - In ambiente di produzione, con URL statico, questo non costituisce un problema

### Funzionalità del Sistema

- Possibilità di riscattare foto se necessario
- Gestione dei marcatori con verifica dell'orientamento

### Limitazioni e Prestazioni

- Il sistema termina automaticamente i processi di export mesh che superano i 15 minuti per step
  - Implementato per evitare blocchi del sistema
  - Consigliato ottimizzare i dataset per rispettare questo limite

Questa documentazione fornisce le linee guida essenziali per l'utilizzo del sistema. Si raccomanda di seguire attentamente tutte le indicazioni per ottenere i migliori risultati possibili.