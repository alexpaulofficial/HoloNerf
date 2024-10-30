using UnityEngine;
using System.IO;
using MixedReality.Toolkit.UX;
using TMPro;
using UnityEngine.Networking;
using System.Collections;

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
       using (UnityWebRequest www = UnityWebRequest.Delete($"{StartStopTrainingScript.ServerUrl}/delete_server"))
       {
           www.downloadHandler = new DownloadHandlerBuffer();
           yield return www.SendWebRequest();

           if (www.result != UnityWebRequest.Result.Success)
           {
               UpdateStatus($"Errore server: {www.error}");
               isDeleting = false;
               dialogCanvas.SetActive(false);
               yield break;
           }

           // Parse risposta server
           string responseText = www.downloadHandler.text;
           if (responseText.Contains("\"status\":\"Error\""))
           {
               int startIndex = responseText.IndexOf("\"message\":\"") + "\"message\":\"".Length;
               int endIndex = responseText.IndexOf("\"", startIndex);
               string errorMessage = responseText.Substring(startIndex, endIndex - startIndex);
               
               UpdateStatus($"Impossibile eliminare: {errorMessage}");
               isDeleting = false;
               dialogCanvas.SetActive(false);
               yield break;
           }

           DeleteLocalFolder();
       }
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

   private void UpdateStatus(string message)
   {
       if (statusText) statusText.text = message;
       Debug.Log($"DeleteScript: {message}");
   }

   private void OnDestroy()
   {
       clearButton?.OnClicked.RemoveAllListeners();
       confirmButton?.OnClicked.RemoveAllListeners(); 
       cancelButton?.OnClicked.RemoveAllListeners();
   }
}