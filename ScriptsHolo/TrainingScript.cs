using UnityEngine;
using UnityEngine.Networking;
using System;
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
    
    // Configurazione server
    [SerializeField] public static string ServerUrl = "http://172.24.150.157:5000";
    private const int MaxRetries = 5;
    private const float RetryDelay = 5f;
    private const float ProgressCheckInterval = 5f;

    // Stati del training
    private bool isTraining = false;
    private bool isUploading = false;
    private float currentProgress = 0f;

    private void Start()
    {
        InitializeUI();
    }

    private void InitializeUI()
    {
        if (trainingButton != null)
        {
            trainingButton.OnClicked.AddListener(ToggleTraining);
            buttonText = trainingButton.GetComponentInChildren<TextMeshPro>();
        }
        
        UpdateStatus("Ready to train!");
        UpdateButtonText();
    }

    private void ToggleTraining()
    {
        if (isUploading)
        {
            UpdateStatus("Upload in corso, attendere...");
            return;
        }

        if (isTraining)
            StopTraining();
        else
            StartTraining();
    }

    private async void StartTraining()
    {
        try
        {
            isTraining = true;
            isUploading = true;
            UpdateButtonText();
            UpdateStatus("Preparing data...");

            string zipPath = await CreateZipFileAsync();
            bool uploadSuccess = await UploadZipFileAsync(zipPath);

            if (!uploadSuccess)
            {
                HandleError("Upload failed");
                return;
            }

            bool trainingStarted = await StartTrainingProcessAsync();
            if (trainingStarted)
            {
                StartCoroutine(MonitorTrainingProgress());
            }
        }
        catch (Exception e)
        {
            HandleError($"Training error: {e.Message}");
        }
        finally
        {
            isUploading = false;
        }
    }

    private async void StopTraining()
    {
        try
        {
            UpdateStatus("Stopping training...");
            var response = await SendRequestAsync($"{ServerUrl}/stop_training", "GET");
            
            if (response.responseCode == 200)
            {
                isTraining = false;
                UpdateStatus("Training stopped.");
                UpdateButtonText();
            }
            else
            {
                HandleError("Failed to stop training");
            }
        }
        catch (Exception e)
        {
            HandleError($"Stop training error: {e.Message}");
        }
    }

private async Task<string> CreateZipFileAsync()
{
    string captureFolder = Path.Combine(Application.temporaryCachePath, "CAPTURE");
    string zipPath = Path.Combine(Application.temporaryCachePath, "data.zip");

    try
    {
        await Task.Run(() => {
            if (File.Exists(zipPath))
                File.Delete(zipPath);
            
            ZipFile.CreateFromDirectory(
                captureFolder,
                zipPath,
                CompressionLevel.Fastest,
                false // preserveSourceDateTime
            );
        });

        return zipPath;
    }
    catch (Exception e)
    {
        throw new Exception($"Zip creation failed: {e.Message}");
    }
}
    private async Task<bool> UploadZipFileAsync(string zipPath)
    {
        try
        {
            WWWForm form = new WWWForm();
            byte[] fileData = await File.ReadAllBytesAsync(zipPath);
            form.AddBinaryData("file", fileData, "data.zip", "application/zip");

            var response = await SendRequestAsync($"{ServerUrl}/upload_data", "POST", form);
            return response.responseCode == 200;
        }
        catch (Exception e)
        {
            throw new Exception($"Upload failed: {e.Message}");
        }
    }

    private IEnumerator MonitorTrainingProgress()
    {
        while (isTraining)
        {
            try
            {
                var www = UnityWebRequest.Get($"{ServerUrl}/training_progress");
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    ProcessProgressResponse(www);
                }
                else if (www.responseCode == 204)
                {
                    CompleteTraining();
                    yield break;
                }
                else
                {
                    HandleError($"Progress check failed: {www.error}");
                }
            }
            catch (Exception e)
            {
                HandleError($"Progress monitoring error: {e.Message}");
            }

            yield return new WaitForSeconds(ProgressCheckInterval);
        }
    }

    private void ProcessProgressResponse(UnityWebRequest www)
    {
        var progressData = JsonUtility.FromJson<ProgressData>(www.downloadHandler.text);
        currentProgress = progressData.progress;
        UpdateStatus($"Training progress: {currentProgress}%");
    }

    private void CompleteTraining()
    {
        isTraining = false;
        UpdateStatus("Training completed!");
        UpdateButtonText();
    }

    private void HandleError(string message)
    {
        Debug.LogError(message);
        isTraining = false;
        isUploading = false;
        UpdateStatus($"Error: {message}");
        UpdateButtonText();
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    private void UpdateButtonText()
    {
        if (buttonText != null)
            buttonText.text = isTraining ? "Stop Training" : "Start Training";
    }

    [Serializable]
    private class ProgressData
    {
        public float progress;
    }
}