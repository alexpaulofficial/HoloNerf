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
        if (!isProcessing)
        {
            isProcessing = true;
            UpdateStatus("Inizio download e importazione della mesh...");
            StartCoroutine(DownloadAndLoadMeshRoutine());
        }
        else
        {
            UpdateStatus("Elaborazione già in corso, attendere...");
        }
    }

    private IEnumerator DownloadAndLoadMeshRoutine()
    {
        string meshZipPath = Path.Combine(Application.temporaryCachePath, "mesh.zip");
        string extractPath = Path.Combine(Application.temporaryCachePath, "extracted");

        if (Directory.Exists(extractPath))
            Directory.Delete(extractPath, true);
        Directory.CreateDirectory(extractPath);

        UpdateStatus("Download della mesh in corso...");
        
        yield return StartCoroutine(SendRequestWithRetry(
            $"{StartStopTrainingScript.ServerUrl}/get_mesh",
            "GET",
            null,
            meshZipPath
        ));

        UpdateStatus("Estrazione della mesh in corso...");
        
        yield return new WaitForEndOfFrame();
        ExtractZipFile(meshZipPath, extractPath);

        string objPath = Path.Combine(extractPath, "mesh.obj");
        if (!File.Exists(objPath))
        {
            UpdateStatus("Errore: File mesh.obj non trovato.");
            isProcessing = false;
            yield break;
        }

        yield return new WaitForEndOfFrame();
        try
        {
            UpdateStatus("Caricamento e posizionamento della mesh...");
            LoadAndPositionMesh(objPath);
            
            CleanupTemporaryFiles(meshZipPath, extractPath);
            UpdateStatus("Mesh importata con successo!");
        }
        catch (System.Exception e)
        {
            UpdateStatus($"Errore durante l'importazione: {e.Message}");
            Debug.LogError($"Errore durante l'importazione: {e}");
        }
        finally
        {
            isProcessing = false;
        }
    }

    private void ExtractZipFile(string zipPath, string extractPath)
    {
        UpdateStatus("Estrazione del file zip...");
        using (ZipArchive archive = ZipFile.OpenRead(zipPath))
        {
            archive.ExtractToDirectory(extractPath);
        }
    }

    private void LoadAndPositionMesh(string objPath)
    {
        GameObject loadedMesh = new OBJLoader().Load(objPath);
        if (loadedMesh == null) throw new System.Exception("Caricamento mesh fallito");

        var collider = loadedMesh.AddComponent<BoxCollider>();
        var manipulator = loadedMesh.AddComponent<ObjectManipulator>();

        if (mainCamera)
        {
            loadedMesh.transform.position = mainCamera.transform.position + 
                                          mainCamera.transform.forward * MeshPlacementDistance;
            loadedMesh.transform.rotation = Quaternion.LookRotation(mainCamera.transform.forward);
        }

        importedMeshes.Add(loadedMesh);
    }

    private void CleanupTemporaryFiles(string zipPath, string extractPath)
    {
        try
        {
            UpdateStatus("Pulizia dei file temporanei...");
            if (File.Exists(zipPath)) File.Delete(zipPath);
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
        }
        catch (System.Exception e)
        {
            UpdateStatus($"Avviso: Errore durante la pulizia dei file temporanei: {e.Message}");
            Debug.LogWarning($"Errore durante la pulizia dei file temporanei: {e.Message}");
        }
    }

    private void ClearImportedMeshes()
    {
        if (isProcessing)
        {
            UpdateStatus("Impossibile cancellare durante l'elaborazione.");
            return;
        }

        foreach (var mesh in importedMeshes)
        {
            if (mesh) Destroy(mesh);
        }
        
        importedMeshes.Clear();
        UpdateStatus("Oggetti cancellati con successo.");
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

                if (www.responseCode != 0)
                {
                    success = true;
                    callback?.Invoke(www);
                    break;
                }

                retries++;
                UpdateStatus($"Tentativo {retries}/{MaxRetries} fallito. Nuovo tentativo in {RetryDelay} secondi...");
                yield return new WaitForSeconds(RetryDelay);
            }
        }

        if (!success)
        {
            UpdateStatus("Errore: Richiesta fallita dopo tutti i tentativi.");
            isProcessing = false;
            throw new System.Exception("Richiesta fallita dopo tutti i tentativi");
        }
    }
}
