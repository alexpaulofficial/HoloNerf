using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using MixedReality.Toolkit.UX;
using TMPro;
using UnityEngine.Windows.WebCam;
using System.Linq;
using MixedReality.Toolkit.Speech.Windows;

public class ScreenshotHandlerHL : MonoBehaviour
{
    // Cartella per salvare gli screenshot e nome del file per le coordinate
    public string screenshotFolder;
    public string coordinatesFileName = "coordinates.txt";
    public float seconds = 0.1f;  // Intervallo per catturare screenshot

    private Coroutine screenshotCoroutine = null;
    private bool isScreenshotModeActive = false;
    private int screenshotCounter = 0;

    private PressableButton screenshotButton;
    public TextMeshProUGUI screenshotButtonText;
    public PressableButton clearMarkersButton;
    [SerializeField] private TextMeshProUGUI statusText;

    public GameObject markerPrefab;
    private List<GameObject> markers = new List<GameObject>();

    private PhotoCapture photoCaptureObject = null;
    private bool isCapturingPhoto = false;

    private void Awake()
    {
        // Impostazioni della fotocamera
        if (Camera.main != null)
        {
            Camera.main.fieldOfView = 64.69f;
            Camera.main.aspect = 3904f / 2196f;
            Camera.main.nearClipPlane = 0.25f;
            Camera.main.farClipPlane = 20f;
        }

        // Definisci la cartella per gli screenshot
        screenshotFolder = Path.Combine(Application.temporaryCachePath, "CAPTURE", "images");

        // Configura il pulsante per gli screenshot
        screenshotButton = GetComponent<PressableButton>();
        if (screenshotButton != null)
        {
            screenshotButton.OnClicked.AddListener(OnScreenshotButtonClicked);
            if (screenshotButtonText == null)
            {
                statusText.text = "Componente TextMeshPro mancante sul pulsante di screenshot.";
            }
        }
        else
        {
            statusText.text = "Componente PressableButton mancante su questo GameObject.";
        }

        // Assicurati che la cartella per gli screenshot esista
        if (!Directory.Exists(screenshotFolder))
        {
            Directory.CreateDirectory(screenshotFolder);
        }

        // Controlla l'assegnazione del prefab del marker
        if (markerPrefab == null)
        {
            statusText.text = "Prefab del marker non assegnato!";
        }

        // Configura il pulsante per eliminare i marker
        if (clearMarkersButton != null)
        {
            clearMarkersButton.OnClicked.AddListener(ClearMarkers);
        }
        else
        {
            statusText.text = "Pulsante di eliminazione marker non assegnato!";
        }

        // Inizializza il contatore degli screenshot e lo stato del pulsante
        UpdateScreenshotCounter();
        UpdateButtonState();
    }

    private void OnScreenshotButtonClicked()
    {
        // Attiva/disattiva la modalità screenshot
        isScreenshotModeActive = !isScreenshotModeActive;

        if (isScreenshotModeActive)
        {
            StartPhotoCaptureMode();
        }
        else
        {
            StopPhotoCaptureMode();
        }

        // Aggiorna l'interfaccia utente
        UpdateScreenshotCounter();
        UpdateButtonState();
    }

    private void StartPhotoCaptureMode()
    {
        UpdateScreenshotCounter();
        // Avvia il processo di acquisizione foto
        isScreenshotModeActive = true;
        if (photoCaptureObject == null)
        {
            PhotoCapture.CreateAsync(false, OnPhotoCaptureCreated);
        }
        else
        {
            screenshotCoroutine = StartCoroutine(CaptureScreenshotEveryNSeconds(seconds));
        }
    }

    private void StopPhotoCaptureMode()
    {
        // Ferma il processo di acquisizione foto
        isScreenshotModeActive = false;
        if (screenshotCoroutine != null)
        {
            StopCoroutine(screenshotCoroutine);
            screenshotCoroutine = null;
        }
        if (photoCaptureObject != null)
        {
            photoCaptureObject.StopPhotoModeAsync(OnStoppedPhotoMode);
        }
    }

    private void UpdateButtonState()
    {
        // Aggiorna il testo e il colore del pulsante in base alla modalità screenshot
        if (screenshotButtonText != null)
        {
            if (isScreenshotModeActive)
            {
                screenshotButtonText.text = "Ferma Foto";
                screenshotButton.GetComponent<Renderer>().material.color = Color.green;
            }
            else
            {
                screenshotButtonText.text = "Scatta Foto";
                screenshotButton.GetComponent<Renderer>().material.color = Color.white;
            }
        }
    }

    private void OnPhotoCaptureCreated(PhotoCapture captureObject)
    {
        // Configura i parametri di acquisizione foto dopo la creazione dell'oggetto
        photoCaptureObject = captureObject;

        Resolution cameraResolution = PhotoCapture.SupportedResolutions.OrderByDescending((res) => res.width * res.height).Last();

        CameraParameters cameraParameters = new CameraParameters
        {
            hologramOpacity = 0.0f,
            cameraResolutionWidth = cameraResolution.width,
            cameraResolutionHeight = cameraResolution.height,
            pixelFormat = CapturePixelFormat.BGRA32
        };

        statusText.text = "Avvio della modalità foto...";
        photoCaptureObject.StartPhotoModeAsync(cameraParameters, OnPhotoModeStarted);
    }

    private void OnPhotoModeStarted(PhotoCapture.PhotoCaptureResult result)
    {
        // Avvia l'acquisizione di screenshot a intervalli se la modalità foto è stata avviata con successo
        if (result.success)
        {
            statusText.text = "Modalità foto avviata con successo.";
            screenshotCoroutine = StartCoroutine(CaptureScreenshotEveryNSeconds(seconds));
        }
        else
        {
            statusText.text = $"Impossibile avviare la modalità foto. Risultato: {result.resultType}";
        }
    }

    private IEnumerator CaptureScreenshotEveryNSeconds(float seconds)
    {
        // Cattura screenshot a intervalli specificati mentre è attiva la modalità screenshot
        while (isScreenshotModeActive)
        {
            if (!isCapturingPhoto)
            {
                CaptureScreenshot();
            }
            yield return new WaitForSeconds(seconds);
        }
    }

    private void CaptureScreenshot()
    {
        // Cattura un singolo screenshot
        if (photoCaptureObject != null && !isCapturingPhoto)
        {
            isCapturingPhoto = true;
            string filename = $"{screenshotCounter:D6}.jpg";
            string filePath = Path.Combine(screenshotFolder, filename);

            statusText.text = $"Scattando foto: {filename}";
            photoCaptureObject.TakePhotoAsync(filePath, PhotoCaptureFileOutputFormat.JPG, OnCapturedPhotoToDisk);
        }
        else if (photoCaptureObject == null)
        {
            // Riavvia la modalità foto se l'oggetto di acquisizione è nullo
            statusText.text = "Oggetto PhotoCapture nullo. Riavvio della modalità foto...";
            StartPhotoCaptureMode();
        }
    }

    private void OnCapturedPhotoToDisk(PhotoCapture.PhotoCaptureResult result)
    {
        // Gestisci i risultati dell'acquisizione foto e salva le coordinate
        isCapturingPhoto = false;
        if (result.success)
        {
            statusText.text = $"Screenshot {screenshotCounter} salvato con successo";
            screenshotButtonText.text = screenshotCounter.ToString();
            screenshotCounter++;
            SaveCoordinates();
            PlaceMarker();
        }
        else if (!isScreenshotModeActive)
        {
            statusText.text = "Modalità screenshot interrotta";
        }
        else
        {
            statusText.text = $"Impossibile salvare la foto {screenshotCounter}. Risultato: {result.resultType}";
        }
    }

    private void SaveCoordinates()
    {
        // Salva le coordinate della fotocamera in un file
        Vector3 position = Camera.main.transform.position;
        Quaternion rotation = Camera.main.transform.rotation;

        Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, Vector3.one);
        string matrixLine = $"{System.DateTime.Now.Ticks}\t" +
                            $"{matrix.m00}\t{matrix.m01}\t{matrix.m02}\t{matrix.m03}\t" +
                            $"{matrix.m10}\t{matrix.m11}\t{matrix.m12}\t{matrix.m13}\t" +
                            $"{matrix.m20}\t{matrix.m21}\t{matrix.m22}\t{matrix.m23}\t" +
                            $"0\t0\t0\t{matrix.m33}\n";

        string path = Path.Combine(screenshotFolder, coordinatesFileName);
        File.AppendAllText(path, matrixLine);

        statusText.text = $"Coordinate salvate in: {path}";
    }

    private void UpdateScreenshotCounter()
    {
        // Aggiorna il contatore degli screenshot in base alle immagini esistenti nella cartella
        string[] existingScreenshots = Directory.GetFiles(screenshotFolder, "*.jpg");
        screenshotCounter = existingScreenshots.Length;
    }

    private void OnDisable()
    {
        // Ferma la modalità foto quando lo script viene disabilitato
        if (photoCaptureObject != null)
        {
            photoCaptureObject.StopPhotoModeAsync(OnStoppedPhotoMode);
        }
    }

    private void OnStoppedPhotoMode(PhotoCapture.PhotoCaptureResult result)
    {
        // Pulisce dopo aver fermato la modalità foto
        if (photoCaptureObject != null)
        {
            photoCaptureObject.Dispose();
            photoCaptureObject = null;
        }
        statusText.text = "Modalità acquisizione foto interrotta e pulizia effettuata";
    }

    private void PlaceMarker()
    {
        // Posiziona un marker prefab alla posizione della fotocamera
        if (markerPrefab != null)
        {
            Vector3 position = Camera.main.transform.position;
            Quaternion rotation = Camera.main.transform.rotation;

            GameObject marker = Instantiate(markerPrefab, position, rotation);
            marker.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);
            markers.Add(marker);

            statusText.text = "Marker aggiunto: " + marker.name + ". Totale marker: " + markers.Count;

            TextMeshPro markerText = marker.GetComponentInChildren<TextMeshPro>();
            if (markerText != null)
            {
                markerText.text = screenshotCounter.ToString();
            }
        }
        else
        {
            statusText.text = "Prefab del marker è nullo, impossibile posizionare il marker.";
        }
    }

    public void ClearMarkers()
    {
        // Elimina tutti i marker dalla scena
        if (markers.Count > 0)
        {
            foreach (GameObject marker in markers)
            {
                Destroy(marker);  // Distrugge ogni marker nella lista
            }
            markers.Clear();  // Svuota la lista dopo aver distrutto i marker
            screenshotCounter = 0;
            statusText.text = "Tutti i marker sono stati eliminati.";
        }
        else
        {
            statusText.text = "Nessun marker da eliminare.";
        }
    }
}
