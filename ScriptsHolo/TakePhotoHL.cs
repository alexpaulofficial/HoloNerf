using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using MixedReality.Toolkit.UX;
using TMPro;
using UnityEngine.Windows.WebCam;

public class ScreenshotHandlerHL : MonoBehaviour
{
    // Configurazione
    [SerializeField] private float captureInterval = 0.1f;
    [SerializeField] private string coordinatesFileName = "coordinates.txt";
    [SerializeField] private GameObject markerPrefab;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI screenshotButtonText;
    [SerializeField] private PressableButton clearMarkersButton;

    // Percorso screenshot
    private string screenshotFolder;
    
    // Gestione cattura foto
    private PhotoCapture photoCaptureObject;
    private Coroutine screenshotCoroutine;
    private bool isScreenshotModeActive;
    private bool isCapturingPhoto;
    private int screenshotCounter;
    
    // UI e markers
    private PressableButton screenshotButton;
    private readonly List<GameObject> markers = new List<GameObject>();

    private void Awake()
    {
        InitializeCamera();
        InitializeComponents();
        SetupDirectories();
        UpdateScreenshotCounter();
    }

    // Inizializzazione camera con parametri ottimali
    private void InitializeCamera()
    {
        if (Camera.main != null)
        {
            Camera.main.fieldOfView = 64.69f;
            Camera.main.aspect = 3904f / 2196f;
            Camera.main.nearClipPlane = 0.1f;
            Camera.main.farClipPlane = 20f;
        }
    }

    // Setup componenti UI e listeners
    private void InitializeComponents()
    {
        screenshotButton = GetComponent<PressableButton>();
        
        if (screenshotButton == null)
        {
            LogError("PressableButton mancante");
            return;
        }

        screenshotButton.OnClicked.AddListener(ToggleScreenshotMode);
        
        if (clearMarkersButton != null)
            clearMarkersButton.OnClicked.AddListener(ClearMarkers);
        else
            LogError("ClearMarkersButton mancante");

        if (markerPrefab == null)
            LogError("MarkerPrefab mancante");
    }

    // Creazione directory necessarie
    private void SetupDirectories()
    {
        screenshotFolder = Path.Combine(Application.temporaryCachePath, "CAPTURE", "images");
        Directory.CreateDirectory(screenshotFolder);
    }

    // Toggle modalità screenshot
    private void ToggleScreenshotMode()
    {
        isScreenshotModeActive = !isScreenshotModeActive;
        
        if (isScreenshotModeActive)
            StartPhotoCaptureMode();
        else
            StopPhotoCaptureMode();
            
        UpdateUI();
    }

    // Avvio cattura foto
    private void StartPhotoCaptureMode()
    {
        if (photoCaptureObject == null)
            PhotoCapture.CreateAsync(false, OnPhotoCaptureCreated);
        else
            screenshotCoroutine = StartCoroutine(CaptureScreenshotRoutine());
    }

    // Configurazione parametri cattura
    private void OnPhotoCaptureCreated(PhotoCapture captureObject)
    {
        photoCaptureObject = captureObject;
        var resolution = PhotoCapture.SupportedResolutions
            .OrderByDescending(res => res.width * res.height)
            .Last();

        var parameters = new CameraParameters
        {
            hologramOpacity = 0.0f,
            cameraResolutionWidth = resolution.width,
            cameraResolutionHeight = resolution.height,
            pixelFormat = CapturePixelFormat.BGRA32
        };

        photoCaptureObject.StartPhotoModeAsync(parameters, OnPhotoModeStarted);
    }

    // Routine cattura screenshot periodici
    private IEnumerator CaptureScreenshotRoutine()
    {
        while (isScreenshotModeActive)
        {
            if (!isCapturingPhoto)
                CaptureScreenshot();
            yield return new WaitForSeconds(captureInterval);
        }
    }

    // Salvataggio coordinate camera
    private void SaveCoordinates()
    {
        var matrix = Matrix4x4.TRS(
            Camera.main.transform.position,
            Camera.main.transform.rotation,
            Vector3.one);

        var matrixLine = $"{DateTime.Now.Ticks}\t" +
                        $"{matrix.m00}\t{matrix.m01}\t{matrix.m02}\t{matrix.m03}\t" +
                        $"{matrix.m10}\t{matrix.m11}\t{matrix.m12}\t{matrix.m13}\t" +
                        $"{matrix.m20}\t{matrix.m21}\t{matrix.m22}\t{matrix.m23}\t" +
                        $"0\t0\t0\t{matrix.m33}\n";

        File.AppendAllText(Path.Combine(screenshotFolder, coordinatesFileName), matrixLine);
    }

    // Piazzamento marker visivo
    private void PlaceMarker()
    {
        if (markerPrefab == null) return;

        var marker = Instantiate(
            markerPrefab,
            Camera.main.transform.position,
            Camera.main.transform.rotation);
            
        marker.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);
        markers.Add(marker);

        if (marker.GetComponentInChildren<TextMeshPro>() is TextMeshPro markerText)
            markerText.text = screenshotCounter.ToString();
    }

    // Utility per logging errori
    private void LogError(string message)
    {
        Debug.LogError(message);
        if (statusText != null)
            statusText.text = message;
    }

    // Cleanup risorse
    private void OnDisable()
    {
        StopPhotoCaptureMode();
    }
}