using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using TMPro;
using MixedReality.Toolkit.UX;

public class ExportMeshScript : MonoBehaviour
{
    [SerializeField] private PressableButton exportButton;
    [SerializeField] private TextMeshProUGUI statusText;

    [SerializeField] private Slider sliderX;
    [SerializeField] private Slider sliderY;
    [SerializeField] private Slider sliderZ;

    [SerializeField] private TextMeshProUGUI sliderXText;
    [SerializeField] private TextMeshProUGUI sliderYText;
    [SerializeField] private TextMeshProUGUI sliderZText;

    private bool isExporting = false;
    private const int MaxRetries = 5;
    private const float RetryDelay = 5f;

    private void Start()
    {
        if (exportButton != null)
            exportButton.OnClicked.AddListener(StartMeshExport);

        if (sliderX != null) sliderX.OnValueUpdated.AddListener(UpdateSliderXText);
        if (sliderY != null) sliderY.OnValueUpdated.AddListener(UpdateSliderYText);
        if (sliderZ != null) sliderZ.OnValueUpdated.AddListener(UpdateSliderZText);

        // Initialize the text with the current slider values
        UpdateSliderXText(new SliderEventData(sliderX.Value, sliderX.Value));
        UpdateSliderYText(new SliderEventData(sliderY.Value, sliderY.Value));
        UpdateSliderZText(new SliderEventData(sliderZ.Value, sliderZ.Value));
    }

    private void UpdateSliderXText(SliderEventData data)
    {
        if (sliderXText != null)
            sliderXText.text = $"X: {data.NewValue:F2}";
    }

    private void UpdateSliderYText(SliderEventData data)
    {
        if (sliderYText != null)
            sliderYText.text = $"Y: {data.NewValue:F2}";
    }

    private void UpdateSliderZText(SliderEventData data)
    {
        if (sliderZText != null)
            sliderZText.text = $"Z: {data.NewValue:F2}";
    }

    private void StartMeshExport()
    {
        float xValue = sliderX.Value;
        float yValue = sliderY.Value;
        float zValue = sliderZ.Value;

        // Create JSON for the x, y, z parameters
        string jsonData = $"{{\"x\":{xValue},\"y\":{yValue},\"z\":{zValue}}}";

        statusText.text = "Starting mesh export...";

        // Use POST with JSON in the body
        StartCoroutine(SendRequestWithRetry($"{StartStopTrainingScript.ServerUrl}/start_export", "POST", OnStartExportComplete, jsonData));
    }

    private void OnStartExportComplete(UnityWebRequest www)
    {
        if (www.responseCode == 200)
        {
            statusText.text = "Mesh export started. Monitoring progress...";
            StartCoroutine(MonitorExportProgress());
        }
        else
        {
            statusText.text = "Error: Failed to start mesh export.";
        }
    }

    private IEnumerator MonitorExportProgress()
    {
        isExporting = true;
        while (isExporting)
        {
            yield return StartCoroutine(SendRequestWithRetry($"{StartStopTrainingScript.ServerUrl}/export_progress", "GET", OnExportProgressReceived));
            yield return new WaitForSeconds(5f);
        }
    }

    private void OnExportProgressReceived(UnityWebRequest www)
    {
        if (www.responseCode == 204)
        {
            isExporting = false;
            statusText.text = "Mesh export completed!";
        }
        else if (www.responseCode == 200)
        {
            statusText.text = "Mesh export in progress...";
        }
        else
        {
            isExporting = false;
            statusText.text = "Error: Mesh export failed.";
        }
    }

    private IEnumerator SendRequestWithRetry(string url, string method, System.Action<UnityWebRequest> callback = null, string jsonData = "")
    {
        int retries = 0;
        bool success = false;

        while (!success && retries < MaxRetries)
        {
            using (UnityWebRequest www = new UnityWebRequest(url, method))
            {
                www.downloadHandler = new DownloadHandlerBuffer();

                if (method == "POST" && !string.IsNullOrEmpty(jsonData))
                {
                    byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(jsonData);
                    www.uploadHandler = new UploadHandlerRaw(jsonToSend);
                    www.SetRequestHeader("Content-Type", "application/json");
                }

                yield return www.SendWebRequest();

                if (www.responseCode != 0)
                {
                    success = true;
                    callback?.Invoke(www);
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
}
