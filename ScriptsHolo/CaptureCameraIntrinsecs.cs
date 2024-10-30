using UnityEngine;
using System.IO;

public class CaptureCameraIntrinsics : MonoBehaviour
{
    public string savePath;

    void Start()
    {
        savePath = Path.Combine(Application.temporaryCachePath, "CAPTURE", "intrinsics.txt");
        SaveIntrinsics();
    }

    void SaveIntrinsics()
    {
        float fov = 64.69f;
        float imageWidth = 1280;
        float imageHeight = 720;
        float aspect = imageWidth/imageHeight;

        // Calculate the focal lengths in pixels
        float focalLengthX = imageWidth / (2 * Mathf.Tan(fov * Mathf.Deg2Rad / 2));
        float focalLengthY = focalLengthX;

        // Assume the principal point is at the center of the image
        float principalPointX = imageWidth / 2.0f;
        float principalPointY = imageHeight / 2.0f;

        // Construct the intrinsics matrix
        float[] intrinsics = new float[]
        {
            focalLengthX, 0, principalPointX,
            0, focalLengthY, principalPointY,
            0, 0, 1
        };

        // Save intrinsics and image size to file
        using (StreamWriter writer = new StreamWriter(savePath))
        {
            for (int i = 0; i < intrinsics.Length; i++)
            {
                writer.Write(intrinsics[i] + "\t");
            }

            // Write the image size
            writer.Write(imageWidth + "\t" + imageHeight);
        }

        Debug.Log("Intrinsics saved to " + savePath);
    }
} 





