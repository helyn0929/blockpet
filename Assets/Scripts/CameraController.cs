using UnityEngine;
using UnityEngine.UI;

public class CameraController : MonoBehaviour
{
    public RawImage cameraPreview;
    public FeedController feedController;

    WebCamTexture webcamTexture;

    void Start()
    {
        webcamTexture = new WebCamTexture();
        cameraPreview.texture = webcamTexture;
        webcamTexture.Play();

        Debug.Log("WebCam started: " + webcamTexture.isPlaying);
    }

    public void TakePhotoAndFeed()
    {
        // 1️拍照
        Texture2D photo = new Texture2D(
            webcamTexture.width,
            webcamTexture.height
        );
        photo.SetPixels(webcamTexture.GetPixels());
        photo.Apply();

        // 2️丟給 FeedController（餵食）
        feedController.FeedWithPhoto(photo);
    }
}

