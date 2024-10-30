using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MeshManager : MonoBehaviour
{
    // Lista per tracciare le mesh importate
    private readonly List<GameObject> importedMeshes = new List<GameObject>();

    // Riferimento al bottone per la cancellazione
    [SerializeField]
    private Button clearButton;

    void Start()
    {
        // Associa l'evento onClick del bottone alla funzione ClearImportedMeshes
        clearButton?.onClick.AddListener(ClearImportedMeshes);
    }

    // Funzione per aggiungere un oggetto importato alla lista
    public void AddImportedMesh(GameObject mesh)
    {
        if (mesh != null)
        {
            importedMeshes.Add(mesh);
        }
    }

    // Funzione per cancellare tutti gli oggetti importati
    private void ClearImportedMeshes()
    {
        // Cancella ogni oggetto dalla scena
        foreach (GameObject mesh in importedMeshes)
        {
            Destroy(mesh);
        }

        // Svuota la lista dopo aver cancellato gli oggetti
        importedMeshes.Clear();

        Debug.Log("Tutti gli oggetti importati sono stati cancellati.");
    }
}
