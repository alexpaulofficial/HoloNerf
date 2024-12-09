using UnityEngine;
using System.IO;

public class CaptureCameraIntrinsics : MonoBehaviour
{
    private string savePath;

    void Start()
    {
        // Combina il percorso della cache temporanea dell'applicazione con il nome del file
        savePath = Path.Combine(Application.temporaryCachePath, "CAPTURE", "intrinsics.txt");
        SaveIntrinsics();
    }

    void SaveIntrinsics()
    {
        // Parametri della fotocamera
        float fov = 64.69f;
        float imageWidth = 1280;
        float imageHeight = 720;
        float aspect = imageWidth / imageHeight;

        // Calcola le lunghezze focali in pixel
        float focalLengthX = imageWidth / (2 * Mathf.Tan(fov * Mathf.Deg2Rad / 2));
        float focalLengthY = focalLengthX;

        // Assume che il punto principale sia al centro dell'immagine
        float principalPointX = imageWidth / 2.0f;
        float principalPointY = imageHeight / 2.0f;

        // Costruisce la matrice degli intrinseci
        float[] intrinsics = new float[]
        {
            focalLengthX, 0, principalPointX,
            0, focalLengthY, principalPointY,
            0, 0, 1
        };

        // Salva gli intrinseci e la dimensione dell'immagine su file
        Directory.CreateDirectory(Path.GetDirectoryName(savePath)); // Crea la directory se non esiste
        using (StreamWriter writer = new StreamWriter(savePath))
        {
            for (int i = 0; i < intrinsics.Length; i++)
            {
                writer.Write(intrinsics[i] + "\t");
            }

            // Scrive la dimensione dell'immagine
            writer.Write(imageWidth + "\t" + imageHeight);
        }

        Debug.Log("Intrinsics saved to " + savePath);
    }
} 