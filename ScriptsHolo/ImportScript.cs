using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.IO;
using System.IO.Compression;
using TMPro;
using Dummiesman;
using MixedReality.Toolkit.SpatialManipulation;
using MixedReality.Toolkit.UX;
using System.Collections.Generic;

public class DownloadImportMeshScript : MonoBehaviour
{
    [SerializeField] private PressableButton downloadButton;
    [SerializeField] private PressableButton clearButton;
    [SerializeField] private TextMeshProUGUI statusText;
    
    private Camera mainCamera;
    private List<GameObject> importedMeshes = new List<GameObject>();
    private bool isProcessing = false; // Flag per prevenire richieste multiple
    
    private const int MaxRetries = 5;
    private const float RetryDelay = 5f;
    private const float MeshPlacementDistance = 2f;

    private void Start()
    {
        // Inizializzazione con controlli di null safety
        mainCamera = Camera.main;
        if (!mainCamera) Debug.LogError("Camera principale non trovata");
        
        InitializeButtons();
    }

    private void InitializeButtons()
    {
        if (downloadButton) 
            downloadButton.OnClicked.AddListener(HandleDownloadRequest);
        else 
            Debug.LogError("Download button non assegnato");

        if (clearButton)
            clearButton.OnClicked.AddListener(ClearImportedMeshes);
        else 
            Debug.LogError("Clear button non assegnato");
    }

    private void HandleDownloadRequest()
    {
        // Previene richieste multiple durante l'elaborazione
        if (!isProcessing)
        {
            isProcessing = true;
            StartCoroutine(DownloadAndLoadMeshRoutine());
        }
        else
        {
            UpdateStatus("Elaborazione in corso, attendere...");
        }
    }

    private IEnumerator DownloadAndLoadMeshRoutine()
    {
        try
        {
            string meshZipPath = Path.Combine(Application.temporaryCachePath, "mesh.zip");
            string extractPath = Path.Combine(Application.temporaryCachePath, "extracted");

            // Assicura che la directory di estrazione sia pulita
            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);
            Directory.CreateDirectory(extractPath);

            UpdateStatus("Download mesh in corso...");
            
            // Download con retry
            yield return StartCoroutine(SendRequestWithRetry(
                $"{StartStopTrainingScript.ServerUrl}/get_mesh",
                "GET",
                null,
                meshZipPath
            ));

            UpdateStatus("Estrazione mesh in corso...");
            
            // Estrazione usando System.IO.Compression
            yield return new WaitForEndOfFrame();
            ExtractZipFile(meshZipPath, extractPath);

            string objPath = Path.Combine(extractPath, "mesh.obj");
            if (!File.Exists(objPath))
            {
                throw new FileNotFoundException("File mesh.obj non trovato nell'archivio");
            }

            yield return new WaitForEndOfFrame();
            LoadAndPositionMesh(objPath);
            
            // Pulizia file temporanei
            CleanupTemporaryFiles(meshZipPath, extractPath);
        }
        catch (System.Exception e)
        {
            UpdateStatus($"Errore: {e.Message}");
            Debug.LogError($"Errore durante l'importazione: {e}");
        }
        finally
        {
            isProcessing = false;
        }
    }

    private void ExtractZipFile(string zipPath, string extractPath)
    {
        using (ZipArchive archive = ZipFile.OpenRead(zipPath))
        {
            archive.ExtractToDirectory(extractPath);
        }
    }

    private void LoadAndPositionMesh(string objPath)
    {
        GameObject loadedMesh = new OBJLoader().Load(objPath);
        if (loadedMesh == null) throw new System.Exception("Caricamento mesh fallito");

        // Configurazione componenti
        var collider = loadedMesh.AddComponent<BoxCollider>();
        var manipulator = loadedMesh.AddComponent<ObjectManipulator>();

        // Posizionamento
        if (mainCamera)
        {
            loadedMesh.transform.position = mainCamera.transform.position + 
                                          mainCamera.transform.forward * MeshPlacementDistance;
            loadedMesh.transform.rotation = Quaternion.LookRotation(mainCamera.transform.forward);
        }

        importedMeshes.Add(loadedMesh);
        UpdateStatus("Mesh importata con successo!");
    }

    private void CleanupTemporaryFiles(string zipPath, string extractPath)
    {
        try
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Errore durante la pulizia dei file temporanei: {e.Message}");
        }
    }

    private void ClearImportedMeshes()
    {
        if (isProcessing)
        {
            UpdateStatus("Impossibile cancellare durante l'elaborazione");
            return;
        }

        foreach (var mesh in importedMeshes)
        {
            if (mesh) Destroy(mesh);
        }
        
        importedMeshes.Clear();
        UpdateStatus("Oggetti cancellati con successo");
    }

    private void UpdateStatus(string message)
    {
        if (statusText) statusText.text = message;
        Debug.Log(message);
    }

    private IEnumerator SendRequestWithRetry(string url, string method, 
        System.Action<UnityWebRequest> callback = null, string downloadPath = null)
    {
        int retries = 0;
        bool success = false;

        while (!success && retries < MaxRetries)
        {
            using (UnityWebRequest www = new UnityWebRequest(url, method))
            {
                www.downloadHandler = downloadPath != null ? 
                    new DownloadHandlerFile(downloadPath) : 
                    new DownloadHandlerBuffer();

                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success || www.responseCode == 204)
                {
                    success = true;
                    callback?.Invoke(www);
                    break;
                }

                retries++;
                UpdateStatus($"Tentativo {retries}/{MaxRetries} fallito. Nuovo tentativo...");
                yield return new WaitForSeconds(RetryDelay);
            }
        }

        if (!success)
            throw new System.Exception("Richiesta fallita dopo tutti i tentativi");
    }
}