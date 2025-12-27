using UnityEngine;
using UnityEngine.UI;
using TMPro;

public enum CameraState
{
    Idle,
    Preview,
    Frozen
}

public class CameraController : MonoBehaviour
{
    [Header("UI")]
    public RawImage cameraPreview;
    public Button mainButton;
    public TMP_Text buttonText;
    
    public FeedController feedController;
    WebCamTexture webcamTexture;
    Texture2D capturedPhoto;

    CameraState state = CameraState.Idle;

    void Start()
    {
        cameraPreview.gameObject.SetActive(false);
        buttonText.text = "Camera";
    }

    public void OnMainButtonPressed()
    {

        Debug.Log("Button pressed in state: " + state);

        switch (state)
        {
            case CameraState.Idle:
                StartCamera();
                break;

            case CameraState.Preview:
                TakePhoto();
                break;

            case CameraState.Frozen:
                FeedPhoto();
                break;
        }
    }

    void StartCamera()
    {
        webcamTexture = new WebCamTexture();
        cameraPreview.texture = webcamTexture;
        webcamTexture.Play();

        cameraPreview.gameObject.SetActive(true);

        state = CameraState.Preview;
        buttonText.text = "Shot";
    }

    void TakePhoto()
    {
        capturedPhoto = new Texture2D(
            webcamTexture.width,
            webcamTexture.height
        );
        capturedPhoto.SetPixels(webcamTexture.GetPixels());
        capturedPhoto.Apply();

        cameraPreview.texture = capturedPhoto;

        state = CameraState.Frozen;
        buttonText.text = "Feed";
    }

    void FeedPhoto()
    {
        Debug.Log("Feed with photo");

        if (feedController != null)
            feedController.FeedWithPhoto(capturedPhoto);

        // ⭐ 重點 1：關掉畫面
        cameraPreview.gameObject.SetActive(false);

        // ⭐ 重點 2：按鈕還活著
        mainButton.gameObject.SetActive(true);

        // ⭐ 重點 3：回到初始狀態
        ResetFlow();
  
    }

    void ResetFlow()
    {
        if (webcamTexture != null)
        {
            webcamTexture.Stop();
            webcamTexture = null;
        }

        cameraPreview.gameObject.SetActive(false);

        state = CameraState.Idle;
        buttonText.text = "Camera";
    }
}
