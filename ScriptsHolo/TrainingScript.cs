using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.IO;
using TMPro;
using MixedReality.Toolkit.UX;
using System.IO.Compression;

public class StartStopTrainingScript : MonoBehaviour
{
    [SerializeField] private PressableButton trainingButton;
    [SerializeField] private TextMeshProUGUI statusText;
    private TextMeshPro buttonText;
    [SerializeField] public static string ServerUrl = "http://172.24.150.157:5000";
    private bool isTraining = false;
    private const int MaxRetries = 5;
    private const float RetryDelay = 5f;

    private void Start()
    {
        if (trainingButton != null)
            trainingButton.OnClicked.AddListener(ToggleTraining);

        statusText.text = "Ready to train!";
        UpdateButtonText();
    }

    private void ToggleTraining()
    {
        if (isTraining)
            StopTraining();
        else
            StartTraining();
    }

private async void StartTraining()
{
    isTraining = true;
    UpdateButtonText();
    statusText.text = "Preparing data...";
    StartCoroutine(TrainingProcessAsync());
}

private IEnumerator TrainingProcessAsync()
{
    string zipPath = null;
    yield return StartCoroutine(CreateZipFileAsync().AsCoroutine().ContinueWith(task => zipPath = task.Result));

    if (zipPath != null)
    {
        yield return StartCoroutine(UploadZipFile(zipPath));
        if (statusText.text == "Data uploaded. Starting training...")
        {
            yield return StartCoroutine(SendRequestWithRetry($"{ServerUrl}/start_training", "GET", OnStartTrainingComplete));
        }
    }
    else
    {
        statusText.text = "Error: Could not upload file.";
        isTraining = false;
        UpdateButtonText();
    }
}

private IEnumerator CreateZipFileAsync()
{
    string captureFolder = Path.Combine(Application.temporaryCachePath, "CAPTURE");
    string zipPath = Path.Combine(Application.temporaryCachePath, "data.zip");

    if (File.Exists(zipPath))
    {
        File.Delete(zipPath);
    }

    return Task.Run(() =>
    {
        try
        {
            // Create zip file in a background thread
            ZipFile.CreateFromDirectory(captureFolder, zipPath, CompressionLevel.Fastest, false);
            return zipPath;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error creating zip file: {e.Message}");
            return null;
        }
    }).AsCoroutine();
}

// Helper extension method to convert Task to Coroutine
public static class TaskExtensions
{
    public static IEnumerator AsCoroutine(this Task task)
    {
        while (!task.IsCompleted)
        {
            yield return null;
        }
        
        if (task.IsFaulted)
        {
            throw task.Exception;
        }
    }
}

    private void StopTraining()
    {
        isTraining = false;
        UpdateButtonText();
        statusText.text = "Stopping training...";
        StartCoroutine(SendRequestWithRetry($"{ServerUrl}/stop_training", "GET", OnStopTrainingComplete));
    }

    private void OnStopTrainingComplete(UnityWebRequest www)
    {
        statusText.text = "Training stopped.";
    }

    private void UpdateButtonText()
    {
        if (trainingButton != null)
        {
            buttonText = trainingButton.GetComponentInChildren<TextMeshPro>();
            if (buttonText != null)
                buttonText.text = isTraining ? "Stop Training" : "Start Training";
        }
    }

    private IEnumerator UploadZipFile(string zipPath)
    {
        WWWForm form = new WWWForm();
        form.AddBinaryData("file", File.ReadAllBytes(zipPath), "data.zip", "application/zip");
        yield return StartCoroutine(SendRequestWithRetry($"{ServerUrl}/upload_data", "POST", OnUploadComplete, null, form));
    }

    private void OnUploadComplete(UnityWebRequest www)
    {
        if (www.responseCode == 200)
            statusText.text = "Data uploaded. Starting training...";
        else
        {
            statusText.text = "Error: Failed to upload data.";
            isTraining = false;
            UpdateButtonText();
        }
    }

    private void OnStartTrainingComplete(UnityWebRequest www)
    {
        if (www.responseCode == 200)
        {
            statusText.text = "Training started. Monitoring progress...";
            StartCoroutine(MonitorTrainingProgress());
        }
        else
        {
            statusText.text = "Error: Failed to start training.";
            isTraining = false;
            UpdateButtonText();
        }
    }

    private IEnumerator MonitorTrainingProgress()
    {
        while (isTraining)
        {
            yield return StartCoroutine(SendRequestWithRetry($"{ServerUrl}/training_progress", "GET", OnProgressReceived));
            yield return new WaitForSeconds(5f);
        }
    }

    private void OnProgressReceived(UnityWebRequest www)
    {
        if (www.responseCode == 204)
        {
            isTraining = false;
            UpdateButtonText();
            statusText.text = "Training completed!";
        }
        else if (www.responseCode == 200)
        {
            ProgressData progressData = JsonUtility.FromJson<ProgressData>(www.downloadHandler.text);
            statusText.text = $"Training progress: {progressData.progress}%";
        }
    }

    private IEnumerator SendRequestWithRetry(string url, string method, System.Action<UnityWebRequest> callback = null, string downloadPath = null, WWWForm form = null)
    {
        int retries = 0;
        bool success = false;

        while (!success && retries < MaxRetries)
        {
            using (UnityWebRequest www = (form != null) ? UnityWebRequest.Post(url, form) : new UnityWebRequest(url, method))
            {
                if (downloadPath != null)
                    www.downloadHandler = new DownloadHandlerFile(downloadPath);
                else if (form == null)
                    www.downloadHandler = new DownloadHandlerBuffer();

                yield return www.SendWebRequest();

                if (www.responseCode != 0)
                {
                    success = true;
                    if (callback != null)
                        callback(www);
                }
                else
                {
                    retries++;
                    yield return new WaitForSeconds(RetryDelay);
                }
            }
        }

        if (!success)
        {
            statusText.text = "Error: Request failed. Please try again.";
        }
    }

    [System.Serializable]
    private class ProgressData
    {
        public float progress;
    }
}