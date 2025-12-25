using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CameraController : MonoBehaviour
{
    public RawImage cameraPreview;
    public Button captureButton;
    TMP_Text buttonText;
    public FeedController feedController;

    WebCamTexture webcamTexture;
    Texture2D capturedPhoto;

    bool hasPhoto = false;

    void Start()
    {
        webcamTexture = new WebCamTexture();
        cameraPreview.texture = webcamTexture;
        webcamTexture.Play();

        buttonText = captureButton.GetComponentInChildren<TMP_Text>();

        buttonText.text = "Camera";
    }

    public void OnButtonPressed()
    {
        if (!hasPhoto)
        {
            TakePhoto();
        }
        else
        {
            FeedPhoto();
        }
    }

    void TakePhoto()
    {
        capturedPhoto = new Texture2D(
            webcamTexture.width,
            webcamTexture.height
        );
        capturedPhoto.SetPixels(webcamTexture.GetPixels());
        capturedPhoto.Apply();

        AlbumManager.Instance.AddPhoto(capturedPhoto);

        // Freeze 畫面
        cameraPreview.texture = capturedPhoto;

        hasPhoto = true;
        buttonText.text = "Feed";


    }

    void FeedPhoto()
    {
        cameraPreview.gameObject.SetActive(false);
        captureButton.gameObject.SetActive(false);

        feedController.FeedWithPhoto(capturedPhoto);
    }
}

