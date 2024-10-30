using UnityEngine;
using System.IO;
using MixedReality.Toolkit.UX;
using TMPro;
using UnityEngine.Networking;
using System.Collections;

public class ClearScreenshotFolder : MonoBehaviour
{
    public TextMeshProUGUI statusText; // Riferimento al testo di stato (opzionale)
    public GameObject CanvasDialog; // Pannello con finestra di dialogo
    public PressableButton Positive; // Bottone di conferma eliminazione
    public PressableButton Negative; // Bottone di annullamento eliminazione
    private PressableButton clearButton; // Bottone Delete Dataset
    public const string ServerUrl = "http://172.24.150.157:5000";

    private const int MaxRetries = 5;
    private const float RetryDelay = 5f;

    private void Start()
    {
        clearButton = GetComponent<PressableButton>();
        if (clearButton != null)
        {
            // Imposta il listener per mostrare la finestra di dialogo
            clearButton.OnClicked.AddListener(ShowConfirmationDialog);
        }
        else
        {
            Debug.LogError("ClearScreenshotFolder: PressableButton component not found on this GameObject.");
        }

        // Imposta i listener per i bottoni della finestra di dialogo
        if (Positive != null)
        {
            Positive.OnClicked.AddListener(ConfirmClearFolder);
        }
        if (Negative != null)
        {
            Negative.OnClicked.AddListener(ClearFolder);
        }

        // Nascondi la finestra di dialogo all’avvio
        CanvasDialog.SetActive(false);
    }

    // Mostra la finestra di dialogo di conferma
    private void ShowConfirmationDialog()
    {
        CanvasDialog.SetActive(true); // Mostra la finestra di dialogo
    }

    // Conferma l’eliminazione del dataset
    private void ConfirmClearFolder()
    {
        CanvasDialog.SetActive(false); // Nasconde la finestra di dialogo
        string folderPath = Path.Combine(Application.temporaryCachePath, "CAPTURE", "images");

        try
        {
            if (Directory.Exists(folderPath))
            {
                foreach (string file in Directory.GetFiles(folderPath))
                {
                    File.Delete(file);
                }
                foreach (string subdirectory in Directory.GetDirectories(folderPath))
                {
                    Directory.Delete(subdirectory, true);
                }
                UpdateStatus("Screenshot folder cleared successfully.");
            }
            else
            {
                UpdateStatus("Screenshot folder does not exist.");
            }
        }
        catch (System.Exception e)
        {
            UpdateStatus($"Error clearing folder: {e.Message}");
        }

        // Chiama qui il metodo per eliminare i marker se necessario
        ClearMarkers();
    }

    // Annulla l’eliminazione del dataset
    private IEnumerator CancelServerFolder()
    {
        yield return SendRequestWithRetry($"{ServerUrl}/delete_server", "DELETE");
    }

    private void ClearFolder()
    { 
        CanvasDialog.SetActive(false); // Nasconde la finestra di dialogo
        StartCoroutine(CancelServerFolder());
        UpdateStatus("Dataset deletion canceled.");

    }
    

    private void UpdateStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log($"ClearScreenshotFolder: {message}");
    }

    // Metodo per eliminare i marker
    private void ClearMarkers()
    {
        // Aggiungi qui il codice per cancellare i marker, se necessario
    }


private IEnumerator SendRequestWithRetry(string url, string method, System.Action<UnityWebRequest> callback = null)
{
    int retries = 0;
    bool success = false;

    while (!success && retries < MaxRetries)
    {
        using (UnityWebRequest www = new UnityWebRequest(url, method))
        {
            www.downloadHandler = new DownloadHandlerBuffer();
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success || www.responseCode == 204)
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

}
