using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.IO;
using TMPro;
using Dummiesman;
using MixedReality.Toolkit.SpatialManipulation;
using Unity.SharpZipLib.Zip;
using MixedReality.Toolkit.UX;
using System.Collections.Generic; // Aggiungi questa direttiva

public class DownloadImportMeshScript : MonoBehaviour
{
    [SerializeField] private PressableButton downloadButton;
    [SerializeField] private PressableButton clearButton; // Bottone per cancellare le mesh
    [SerializeField] private TextMeshProUGUI statusText;
    private Camera mainCamera;

    // Nuova lista per tracciare le mesh importate
    private List<GameObject> importedMeshes = new List<GameObject>(); 
    private const int MaxRetries = 5;
    private const float RetryDelay = 5f;

    private void Start()
    {
        if (downloadButton != null)
            downloadButton.OnClicked.AddListener(OnDownloadButtonClicked); // Metodo collegato al listener
        
        if (clearButton != null)
            clearButton.OnClicked.AddListener(ClearImportedMeshes); // Collega il bottone di cancellazione
        
        mainCamera = Camera.main;
    }

    private void OnDownloadButtonClicked()
    {
        StartCoroutine(DownloadAndLoadMesh());  // Avvia la coroutine
    }

    private IEnumerator DownloadAndLoadMesh()
    {
        string meshPath = Path.Combine(Application.temporaryCachePath, "mesh.zip");

        // Scarica la mesh con un retry in caso di fallimento
        yield return StartCoroutine(SendRequestWithRetry($"{StartStopTrainingScript.ServerUrl}/get_mesh", "GET", null, meshPath));
        statusText.text = $"Import mesh in corso";
        // Estrai il file ZIP
        FastZip fastZip = new FastZip();
        string extractPath = Application.temporaryCachePath;

        try
        {
            fastZip.ExtractZip(meshPath, extractPath, null);
        }
        catch (System.Exception e)
        {
            statusText.text = $"Errore durante l'estrazione della mesh: {e.Message}";
            yield break; // Interrompi se l'estrazione fallisce
        }

        // Verifica se il file OBJ esiste dopo l'estrazione
        string objPath = Path.Combine(extractPath, "mesh.obj");
        if (File.Exists(objPath))
        {
            GameObject loadedMesh = null;

            try
            {
                // Carica la mesh usando OBJLoader
                loadedMesh = new OBJLoader().Load(objPath);
            }
            catch (System.Exception e)
            {
                statusText.text = $"Errore durante il caricamento della mesh: {e.Message}";
                yield break;
            }

            if (loadedMesh != null)
            {
                // Aggiungi componenti per interazioni e fisica
                loadedMesh.AddComponent<BoxCollider>();
                loadedMesh.AddComponent<ObjectManipulator>();

                // Posiziona la mesh di fronte alla fotocamera
                PositionMeshInFrontOfCamera(loadedMesh);

                // Aggiungi la mesh alla lista
                importedMeshes.Add(loadedMesh);

                // Aggiorna lo stato
                statusText.text = "Mesh importata con successo!";
            }
            else
            {
                statusText.text = "Errore: Caricamento della mesh fallito.";
            }
        }
        else
        {
            statusText.text = "Errore: File mesh.obj non trovato.";
        }
    }

    private void ClearImportedMeshes()
    {
        foreach (GameObject mesh in importedMeshes)
        {
            Destroy(mesh);  // Cancella l'oggetto dalla scena
        }

        // Svuota la lista dopo aver cancellato gli oggetti
        importedMeshes.Clear();
        
        statusText.text = "Tutti gli oggetti importati sono stati cancellati.";
        Debug.Log("Tutti gli oggetti importati sono stati cancellati.");
    }

    private void PositionMeshInFrontOfCamera(GameObject mesh)
    {
        if (mainCamera != null)
        {
            mesh.transform.position = mainCamera.transform.position + mainCamera.transform.forward * 2f;
            mesh.transform.rotation = Quaternion.LookRotation(mainCamera.transform.forward);
        }
    }

    private IEnumerator SendRequestWithRetry(string url, string method, System.Action<UnityWebRequest> callback = null, string downloadPath = null)
    {
        int retries = 0;
        bool success = false;

        while (!success && retries < MaxRetries)
        {
            using (UnityWebRequest www = new UnityWebRequest(url, method))
            {
                if (downloadPath != null)
                    www.downloadHandler = new DownloadHandlerFile(downloadPath);
                else
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


