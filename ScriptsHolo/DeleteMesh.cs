using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MeshManager : MonoBehaviour
{
    // Lista per tracciare gli oggetti importati
    private List<GameObject> importedMeshes = new List<GameObject>();

    // Riferimento al bottone per la cancellazione
    public Button clearButton;

    void Start()
    {
        // Associa l'evento onClick del bottone alla funzione ClearImportedMeshes
        if (clearButton != null)
        {
            clearButton.onClick.AddListener(ClearImportedMeshes);
        }
    }

    // Funzione per aggiungere un oggetto importato alla lista
    public void AddImportedMesh(GameObject mesh)
    {
        importedMeshes.Add(mesh);
    }

    // Funzione per cancellare tutti gli oggetti importati
    public void ClearImportedMeshes()
    {
        foreach (GameObject mesh in importedMeshes)
        {
            Destroy(mesh);  // Cancella l'oggetto dalla scena
        }

        // Svuota la lista dopo aver cancellato gli oggetti
        importedMeshes.Clear();
        
        Debug.Log("Tutti gli oggetti importati sono stati cancellati.");
    }
}
