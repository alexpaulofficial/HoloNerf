using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using TMPro;
using MixedReality.Toolkit.UX;
using System;

public class ExportMeshScript : MonoBehaviour
{
    // Riferimenti UI
    [SerializeField] private PressableButton exportButton;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Slider[] scaleSliders = new Slider[3]; // X,Y,Z
    [SerializeField] private TextMeshProUGUI[] sliderTexts = new TextMeshProUGUI[3];

    // Configurazione richieste
    private const int MaxRetries = 5;
    private const float RetryDelay = 5f;
    private const float ProgressCheckInterval = 20f;
    
    private bool isExporting = false;
    private bool isRequestPending = false;
    private Coroutine exportCoroutine;

    private void Start()
    {
        InitializeUI();
    }

    private void InitializeUI()
    {
        if (exportButton != null)
            exportButton.OnClicked.AddListener(HandleExportButtonClick);

        // Inizializza slider e testi
        for (int i = 0; i < 3; i++)
        {
            int index = i; // Cattura per lambda
            if (scaleSliders[i] != null)
            {
                scaleSliders[i].OnValueUpdated.AddListener(
                    (data) => UpdateSliderText(index, data.NewValue));
                UpdateSliderText(i, scaleSliders[i].Value);
            }
        }
    }

    private void UpdateSliderText(int index, float value)
    {
        if (sliderTexts[index] != null)
            sliderTexts[index].text = $"{(char)('X' + index)}: {value:F2}";
    }

    private void HandleExportButtonClick()
    {
        if (isExporting || isRequestPending)
        {
            UpdateStatus("Esportazione già in corso...");
            return;
        }

        StartExport();
    }

    private void StartExport()
    {
        // Prepara parametri export
        var scaleParams = new
        {
            x = scaleSliders[0].Value,
            y = scaleSliders[1].Value,
            z = scaleSliders[2].Value
        };

        string jsonData = JsonUtility.ToJson(scaleParams);
        StartCoroutine(SendExportRequest(jsonData));
    }

    private IEnumerator SendExportRequest(string jsonData)
    {
        isRequestPending = true;
        UpdateStatus("Avvio esportazione...");

        yield return SendRequestWithRetry(
            $"{StartStopTrainingScript.ServerUrl}/start_export", 
            "POST", 
            OnExportRequestComplete,
            jsonData
        );
    }

    private void OnExportRequestComplete(UnityWebRequest www)
    {
        isRequestPending = false;

        if (!HandleRequestError(www))
        {
            isExporting = true;
            UpdateStatus("Monitoraggio esportazione...");
            exportCoroutine = StartCoroutine(MonitorExportProgress());
        }
    }

    private IEnumerator MonitorExportProgress()
    {
        while (isExporting)
        {
            yield return new WaitForSeconds(ProgressCheckInterval);
            yield return SendRequestWithRetry(
                $"{StartStopTrainingScript.ServerUrl}/export_progress",
                "GET",
                OnProgressReceived
            );
        }
    }

    private void OnProgressReceived(UnityWebRequest www)
    {
        if (www.responseCode == 204)
        {
            CompleteExport();
        }
        else if (www.responseCode != 200)
        {
            HandleExportError("Errore durante l'esportazione");
        }
    }

    private void CompleteExport()
    {
        isExporting = false;
        UpdateStatus("Esportazione completata!");
        if (exportCoroutine != null)
        {
            StopCoroutine(exportCoroutine);
            exportCoroutine = null;
        }
    }

    private void HandleExportError(string message)
    {
        isExporting = false;
        UpdateStatus($"Errore: {message}");
        if (exportCoroutine != null)
        {
            StopCoroutine(exportCoroutine);
            exportCoroutine = null;
        }
    }

    private bool HandleRequestError(UnityWebRequest www)
    {
        if (www.result != UnityWebRequest.Result.Success && 
            www.responseCode != 204)
        {
            string error = string.IsNullOrEmpty(www.error) ? 
                "Errore di rete" : www.error;
            UpdateStatus($"Errore: {error}");
            return true;
        }
        return false;
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
        Debug.Log($"[ExportMesh] {message}");
    }

    private IEnumerator SendRequestWithRetry(
        string url, 
        string method,
        Action<UnityWebRequest> callback,
        string jsonData = ""
    )
    {
        int retries = 0;
        bool success = false;

        while (!success && retries < MaxRetries)
        {
            using (UnityWebRequest www = new UnityWebRequest(url, method))
            {
                SetupRequest(www, jsonData);
                
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success || 
                    www.responseCode == 204)
                {
                    success = true;
                    callback?.Invoke(www);
                }
                else
                {
                    retries++;
                    if (retries < MaxRetries)
                    {
                        UpdateStatus($"Tentativo {retries}/{MaxRetries}...");
                        yield return new WaitForSeconds(RetryDelay);
                    }
                }
            }
        }

        if (!success)
        {
            UpdateStatus("Errore di connessione. Riprovare più tardi.");
        }
    }

    private void SetupRequest(UnityWebRequest www, string jsonData)
    {
        www.downloadHandler = new DownloadHandlerBuffer();
        
        if (!string.IsNullOrEmpty(jsonData))
        {
            byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(jsonData);
            www.uploadHandler = new UploadHandlerRaw(jsonBytes);
            www.SetRequestHeader("Content-Type", "application/json");
        }
    }

    private void OnDestroy()
    {
        if (exportCoroutine != null)
            StopCoroutine(exportCoroutine);
    }
}