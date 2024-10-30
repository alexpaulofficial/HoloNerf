using UnityEngine;
using System.IO;
using MixedReality.Toolkit.UX;
using TMPro;
using UnityEngine.Networking;
using System.Collections;
using Newtonsoft.Json;

public class ClearScreenshotFolder : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private GameObject dialogCanvas;
    [SerializeField] private PressableButton confirmButton;
    [SerializeField] private PressableButton cancelButton;
    
    [Header("Configuration")]
    [SerializeField] private int maxRetries = 5;
    [SerializeField] private float retryDelay = 5f;
    
    private PressableButton clearButton;
    private bool isDeleting;

    // Struttura per deserializzare le risposte del server
    private class ServerResponse
    {
        public string status { get; set; }
        public string message { get; set; }
    }

    private void Start()
    {
        SetupButtons();
        dialogCanvas.SetActive(false);
    }

    private void SetupButtons()
    {
        clearButton = GetComponent<PressableButton>();
        if (!clearButton)
        {
            Debug.LogError("DeleteScript: Componente PressableButton mancante");
            enabled = false;
            return;
        }

        clearButton.OnClicked.AddListener(ShowConfirmationDialog);
        confirmButton?.OnClicked.AddListener(HandleConfirmDelete);
        cancelButton?.OnClicked.AddListener(() => dialogCanvas.SetActive(false));
    }

    private void ShowConfirmationDialog() => dialogCanvas.SetActive(true);

    private void HandleConfirmDelete()
    {
        if (isDeleting) return;
        isDeleting = true;
        StartCoroutine(DeleteDatasetRoutine());
    }

    private IEnumerator DeleteDatasetRoutine()
    {
        // Verifica stato del server prima di procedere
        yield return StartCoroutine(SendServerRequest($"{StartStopTrainingScript.ServerUrl}/delete_server", 
            (response) => {
                if (response.status == "Error")
                {
                    UpdateStatus($"Impossibile eliminare: {response.message}");
                    isDeleting = false;
                    dialogCanvas.SetActive(false);
                    return;
                }
                
                DeleteLocalFolder();
            }));
    }

    private void DeleteLocalFolder()
    {
        string folderPath = Path.Combine(Application.temporaryCachePath, "CAPTURE", "images");
        
        try
        {
            if (Directory.Exists(folderPath))
            {
                Directory.Delete(folderPath, true);
                UpdateStatus("Dataset eliminato con successo");
            }
        }
        catch (IOException ex)
        {
            Debug.LogError($"Errore eliminazione cartella: {ex.Message}");
            UpdateStatus("Errore durante l'eliminazione dei file locali");
        }
        finally
        {
            isDeleting = false;
            dialogCanvas.SetActive(false);
        }
    }

    private IEnumerator SendServerRequest(string url, System.Action<ServerResponse> callback)
    {
        int retries = 0;
        
        while (retries < maxRetries)
        {
            using (UnityWebRequest www = UnityWebRequest.Delete(url))
            {
                www.downloadHandler = new DownloadHandlerBuffer();
                
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var response = JsonConvert.DeserializeObject<ServerResponse>(www.downloadHandler.text);
                        callback(response);
                        yield break;
                    }
                    catch (JsonException)
                    {
                        Debug.LogError("Errore parsing risposta server");
                    }
                }

                retries++;
                if (retries < maxRetries)
                {
                    yield return new WaitForSeconds(retryDelay);
                }
            }
        }

        UpdateStatus("Errore di comunicazione con il server");
    }

    private void UpdateStatus(string message)
    {
        if (statusText) statusText.text = message;
        Debug.Log($"DeleteScript: {message}");
    }

    private void OnDestroy()
    {
        // Cleanup listeners
        clearButton?.OnClicked.RemoveAllListeners();
        confirmButton?.OnClicked.RemoveAllListeners();
        cancelButton?.OnClicked.RemoveAllListeners();
    }
}