using UnityEngine;
using System.IO;
using MixedReality.Toolkit.UX;
using TMPro;
using UnityEngine.Networking;
using System.Collections;
using UnityEngine.Windows.WebCam;

public class ClearScreenshotFolder : MonoBehaviour
{
    public TextMeshProUGUI statusText; // Riferimento al testo di stato (opzionale)
    public GameObject CanvasDialog; // Pannello con finestra di dialogo
    public PressableButton Positive; // Bottone di conferma eliminazione
    public PressableButton Negative; // Bottone di annullamento eliminazione
    private PressableButton clearButton; // Bottone Delete Dataset
    public PhotoCapture photoCapture; // Aggiungi questo come riferimento nello script

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
    /*private void ConfirmClearFolder()
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
    }*/

    // Annulla l’eliminazione del dataset


    private void ConfirmClearFolder()
    {
        CanvasDialog.SetActive(false); // Nasconde la finestra di dialogo
        string folderPath = Path.Combine(Application.temporaryCachePath, "CAPTURE", "images");

        if (photoCapture != null) // Controlla se la modalità fotografica è attiva
        {
            photoCapture.StopPhotoModeAsync((result) =>
            {
                photoCapture.Dispose();
                photoCapture = null;
                Debug.Log("Photo mode stopped. Proceeding to clear folder.");

                // Procedi con l'eliminazione del dataset dopo aver fermato la Photo Mode
                DeleteDataset(folderPath);
            });
        }
        else
        {
            // Se la modalità fotografica non è attiva, elimina direttamente il dataset
            DeleteDataset(folderPath);
        }
    }

    // Funzione per eliminare il dataset
    private void DeleteDataset(string folderPath)
    {
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
    }

    private void ClearFolder()
    {
        CanvasDialog.SetActive(false); // Nasconde la finestra di dialogo
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
}
