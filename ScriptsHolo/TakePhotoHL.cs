using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using MixedReality.Toolkit.UX;
using TMPro;
using UnityEngine.Windows.WebCam;
using System.Linq;

public class ScreenshotHandlerHL : MonoBehaviour
{
    public string screenshotFolder;
    public string coordinatesFileName = "coordinates.txt";
    public float seconds = 0.1f;

    private Coroutine screenshotCoroutine = null;
    private bool isScreenshotModeActive = false;
    public static int screenshotCounter = 0;

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
        if (Camera.main != null)
        {
            Camera.main.fieldOfView = 64.69f;
            Camera.main.aspect = 3904f / 2196f;
            Camera.main.nearClipPlane = 0.25f;
            Camera.main.farClipPlane = 20f;
        }
        screenshotFolder = Path.Combine(Application.temporaryCachePath, "CAPTURE", "images");
        screenshotButton = GetComponent<PressableButton>();
        if (screenshotButton != null)
        {
            screenshotButton.OnClicked.AddListener(OnScreenshotButtonClicked);
            if (screenshotButtonText == null)
            {
                statusText.text = "No TextMeshPro component found on the screenshot button.";
            }
        }
        else
        {
            statusText.text = "No PressableButton component found on this GameObject.";
        }

        if (!Directory.Exists(screenshotFolder))
        {
            Directory.CreateDirectory(screenshotFolder);
        }

        if (markerPrefab == null)
        {
            statusText.text = "Marker prefab is not assigned!";
        }

        if (clearMarkersButton != null)
        {
            clearMarkersButton.OnClicked.AddListener(ClearMarkers);
        }
        else
        {
            statusText.text = "Clear markers button is not assigned!";
        }

        UpdateScreenshotCounter();
        UpdateButtonState();

    }

    private void OnScreenshotButtonClicked()
    {
        isScreenshotModeActive = !isScreenshotModeActive;

        if (isScreenshotModeActive)
        {
            StartPhotoCaptureMode();
        }
        else
        {
            StopPhotoCaptureMode();
        }
        UpdateScreenshotCounter();
        UpdateButtonState();
    }

    private void StartPhotoCaptureMode()
    {
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
        if (screenshotButtonText != null)
        {
            if (isScreenshotModeActive)
            {
                screenshotButtonText.text = "Stop Photos";
                screenshotButton.GetComponent<Renderer>().material.color = Color.green;
            }
            else
            {
                screenshotButtonText.text = "Take Photos";
                screenshotButton.GetComponent<Renderer>().material.color = Color.white;
            }
        }
    }

    private void OnPhotoCaptureCreated(PhotoCapture captureObject)
    {
        photoCaptureObject = captureObject;

        Resolution cameraResolution = PhotoCapture.SupportedResolutions.OrderByDescending((res) => res.width * res.height).Last();

        CameraParameters cameraParameters = new CameraParameters
        {
            hologramOpacity = 0.0f,
            cameraResolutionWidth = cameraResolution.width,
            cameraResolutionHeight = cameraResolution.height,
            pixelFormat = CapturePixelFormat.BGRA32
        };

        statusText.text = "Starting photo mode...";
        photoCaptureObject.StartPhotoModeAsync(cameraParameters, OnPhotoModeStarted);
    }

    private void OnPhotoModeStarted(PhotoCapture.PhotoCaptureResult result)
    {
        if (result.success)
        {
            statusText.text = "Photo mode started successfully.";
            screenshotCoroutine = StartCoroutine(CaptureScreenshotEveryNSeconds(seconds));
        }
        else
        {
            statusText.text = $"Failed to start photo mode. Result: {result.resultType}";
        }
    }

    private IEnumerator CaptureScreenshotEveryNSeconds(float seconds)
    {
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
        if (photoCaptureObject != null && !isCapturingPhoto)
        {
            isCapturingPhoto = true;
            string filename = $"{screenshotCounter:D6}.jpg";
            string filePath = Path.Combine(screenshotFolder, filename);

            statusText.text = $"Taking photo: {filename}";
            photoCaptureObject.TakePhotoAsync(filePath, PhotoCaptureFileOutputFormat.JPG, OnCapturedPhotoToDisk);
        }
        else if (photoCaptureObject == null)
        {
            statusText.text = "PhotoCapture object is null. Restarting photo mode...";
            StartPhotoCaptureMode();
        }
    }

    private void OnCapturedPhotoToDisk(PhotoCapture.PhotoCaptureResult result)
    {
        isCapturingPhoto = false;
        if (result.success)
        {
            statusText.text = $"Screenshot {screenshotCounter} saved successfully";
            screenshotButtonText.text = screenshotCounter.ToString();
            screenshotCounter++;
            SaveCoordinates();
            PlaceMarker();
        }
        else if (!isScreenshotModeActive)
        {
            statusText.text = "Screenshot mode stopped";
        }
        else
        {
            statusText.text = $"Failed to save photo {screenshotCounter}. Result: {result.resultType}";
        }
    }

    private void SaveCoordinates()
    {
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

        statusText.text = $"Coordinates saved to: {path}";
    }

    private void UpdateScreenshotCounter()
    {
        string[] existingScreenshots = Directory.GetFiles(screenshotFolder, "*.jpg");
        screenshotCounter = existingScreenshots.Length;
    }

    private void OnDisable()
    {
        if (photoCaptureObject != null)
        {
            photoCaptureObject.StopPhotoModeAsync(OnStoppedPhotoMode);
        }
    }

    private void OnStoppedPhotoMode(PhotoCapture.PhotoCaptureResult result)
    {
        if (photoCaptureObject != null)
        {
            photoCaptureObject.Dispose();
            photoCaptureObject = null;
        }
        statusText.text = "Photo capture mode stopped clenup";
    }
    private void PlaceMarker()
    {
        if (markerPrefab != null)
        {
            Vector3 position = Camera.main.transform.position;
            Quaternion rotation = Camera.main.transform.rotation;

            GameObject marker = Instantiate(markerPrefab, position, rotation);
            marker.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);
            markers.Add(marker);

            statusText.text = "Marker added: " + marker.name + ". Total markers: " + markers.Count;

            TextMeshPro markerText = marker.GetComponentInChildren<TextMeshPro>();
            if (markerText != null)
            {
                markerText.text = screenshotCounter.ToString();
            }
        }
        else
        {
            statusText.text = "Marker prefab is null, cannot place marker.";
        }
    }

    public void ClearMarkers()
    {
        if (markers.Count > 0)
        {
            foreach (GameObject marker in markers)
            {
                Destroy(marker);  // Distrugge ogni marker nella lista
            }
            markers.Clear();  // Svuota la lista dopo aver distrutto i marker
            screenshotCounter = 0;
            statusText.text = "All markers have been cleared.";
        }
        else
        {
            statusText.text = "No markers to clear.";
        }
    }



}