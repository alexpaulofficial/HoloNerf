using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using TMPro;
using MixedReality.Toolkit.UX;
using System.IO.Compression;

public class StartStopTrainingScript : MonoBehaviour
{
   [SerializeField] private PressableButton trainingButton;
   [SerializeField] private TextMeshProUGUI statusText;
   private TextMeshPro buttonText;
   
   [SerializeField] public static string ServerUrl = "http://172.24.150.157:5000";
   private const int MaxRetries = 5;
   private const float RetryDelay = 5f;
   private const float ProgressCheckInterval = 5f;

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
           bool success = await SendRequestAsync($"{ServerUrl}/stop_training", "GET");
           
           if (success)
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
                   System.IO.Compression.CompressionLevel.Fastest,
                   false
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

           return await SendRequestAsync($"{ServerUrl}/upload_data", "POST", form);
       }
       catch (Exception e)
       {
           throw new Exception($"Upload failed: {e.Message}");
       }
   }

   private async Task<bool> SendRequestAsync(string url, string method, WWWForm form = null)
   {
       UnityWebRequest request;
       if (form != null)
           request = UnityWebRequest.Post(url, form);
       else
           request = new UnityWebRequest(url, method);

       request.downloadHandler = new DownloadHandlerBuffer();
       
       for (int i = 0; i < MaxRetries; i++)
       {
           try {
               UnityWebRequestAsyncOperation operation = request.SendWebRequest();
               while (!operation.isDone)
                   await Task.Yield();

               if (request.result == UnityWebRequest.Result.Success)
                   return true;
           }
           catch {
               if (i == MaxRetries - 1) throw;
           }
           await Task.Delay((int)(RetryDelay * 1000));
       }
       
       return false;
   }

   private IEnumerator MonitorTrainingProgress()
   {
       while (isTraining)
       {
           UnityWebRequest www = UnityWebRequest.Get($"{ServerUrl}/training_progress");
           yield return www.SendWebRequest();

           try
           {
               if (www.result == UnityWebRequest.Result.Success)
               {
                   ProcessProgressResponse(www);
               }
               else if (www.responseCode == 204)
               {
                   CompleteTraining();
                   www.Dispose();
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
           finally
           {
               www.Dispose();
           }

           yield return new WaitForSeconds(ProgressCheckInterval);
       }
   }

   private async Task<bool> StartTrainingProcessAsync()
   {
       try {
           return await SendRequestAsync($"{ServerUrl}/start_training", "GET");
       }
       catch (Exception e) {
           throw new Exception($"Failed to start training: {e.Message}");
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