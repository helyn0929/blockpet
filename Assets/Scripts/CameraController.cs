using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public enum CameraState { Idle, Preview, Frozen }

public class CameraController : MonoBehaviour
{
    [Header("UI")]
    public RawImage cameraPreview;
    public Button mainButton;
    public TMP_Text buttonText;
    
    [Header("Settings")]
    public FeedController feedController;
    public PetHealthManager healthManager;

    WebCamTexture webcamTexture;
    Texture2D capturedPhoto;
    CameraState state = CameraState.Idle;

    void Start()
    {
        cameraPreview.gameObject.SetActive(false);
        buttonText.text = "Camera";
    }

    void Update()
    {
        // 只有在 Idle 狀態時，才檢查並顯示冷卻倒數
        if (state == CameraState.Idle)
        {
            UpdateCooldownDisplay();
        }
    }

    // 整合後的冷卻檢查邏輯
    void UpdateCooldownDisplay()
    {
        if (SaveManager.Instance == null || SaveManager.Instance.data == null || 
            string.IsNullOrEmpty(SaveManager.Instance.data.lastCaptureTime))
        {
            buttonText.text = "Camera";
            mainButton.interactable = true;
            return;
        }

        if (DateTime.TryParse(SaveManager.Instance.data.lastCaptureTime, out DateTime lastTime))
        {
            TimeSpan elapsed = DateTime.Now - lastTime;
            double remainingSeconds = 300 - elapsed.TotalSeconds; // 5 分鐘冷卻

            if (remainingSeconds > 0)
            {
                mainButton.interactable = false;
                TimeSpan t = TimeSpan.FromSeconds(remainingSeconds);
                buttonText.text = string.Format("{0:D2}:{1:D2}", t.Minutes, t.Seconds);
            }
            else
            {
                mainButton.interactable = true;
                buttonText.text = "Camera";
            }
        }
    }

    public void OnMainButtonPressed()
    {
        switch (state)
        {
            case CameraState.Idle: StartCamera(); break;
            case CameraState.Preview: TakePhoto(); break;
            case CameraState.Frozen: FeedPhoto(); break;
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
        capturedPhoto = new Texture2D(webcamTexture.width, webcamTexture.height);
        capturedPhoto.SetPixels(webcamTexture.GetPixels());
        capturedPhoto.Apply();
        cameraPreview.texture = capturedPhoto;
        state = CameraState.Frozen;
        buttonText.text = "Feed";
    }

    void FeedPhoto()
    {
        if (feedController != null)
            feedController.FeedWithPhoto(capturedPhoto);

        // 1. 紀錄時間並強制存檔
        if (SaveManager.Instance != null && SaveManager.Instance.data != null)
        {
            SaveManager.Instance.data.lastCaptureTime = DateTime.Now.ToString();
            SaveManager.Instance.Save(); 
        }

        // 2. 補血邏輯
        if (healthManager != null) healthManager.AddHealth();
        else FindObjectOfType<PetHealthManager>()?.AddHealth();

        cameraPreview.gameObject.SetActive(false);
        mainButton.gameObject.SetActive(true);
        ResetFlow();
    }

    public void ResetFlow()
    {
        if (webcamTexture != null) { webcamTexture.Stop(); webcamTexture = null; }
        cameraPreview.gameObject.SetActive(false);
        state = CameraState.Idle;
    }
}