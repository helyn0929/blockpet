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
    [Tooltip("Album panel to hide when opening camera (avoids Album showing over Camera).")]
    public GameObject albumPanel;
    [Tooltip("Camera preview panel to show when opening camera; optional.")]
    public GameObject cameraPreviewPanel;

    [Header("Settings")]
    public FeedController feedController;
    public PetHealthManager healthManager;

    WebCamTexture webcamTexture;
    Texture2D capturedPhoto;
    CameraState state = CameraState.Idle;

    void Awake()
    {
        if (albumPanel == null)
        {
            var go = GameObject.Find("AlbumPanel");
            if (go != null) albumPanel = go;
        }
        if (cameraPreviewPanel == null && cameraPreview != null && cameraPreview.gameObject != null)
            cameraPreviewPanel = cameraPreview.gameObject;
    }

    void Start()
    {
        ClearOtherPanelsBeforeCamera();
        if (cameraPreview != null && cameraPreview.gameObject != null)
            cameraPreview.gameObject.SetActive(false);
        if (buttonText != null) buttonText.text = "Camera";
    }

    void OnDisable()
    {
        ReleaseCamera();
    }

    void OnDestroy()
    {
        ReleaseCamera();
    }

    void ReleaseCamera()
    {
        if (webcamTexture != null)
        {
            webcamTexture.Stop();
            webcamTexture = null;
        }
        ResetFlow();
    }

    /// <summary>Hides album and other panels so they don't overlap the camera. Call before opening camera.</summary>
    public void ClearOtherPanelsBeforeCamera()
    {
        if (albumPanel != null) albumPanel.SetActive(false);
        if (cameraPreviewPanel != null) cameraPreviewPanel.SetActive(false);
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
        if (buttonText == null || mainButton == null) return;

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
        ClearOtherPanelsBeforeCamera();
        if (albumPanel != null) albumPanel.SetActive(false);
        if (cameraPreviewPanel != null) cameraPreviewPanel.SetActive(true);
        if (cameraPreview != null && cameraPreview.transform != null)
            cameraPreview.transform.SetAsLastSibling();

        webcamTexture = new WebCamTexture();
        if (cameraPreview != null) cameraPreview.texture = webcamTexture;
        webcamTexture.Play();
        if (cameraPreview != null && cameraPreview.gameObject != null)
            cameraPreview.gameObject.SetActive(true);
        state = CameraState.Preview;
        if (buttonText != null) buttonText.text = "Shot";
    }

    void TakePhoto()
    {
        if (webcamTexture == null) return;
        capturedPhoto = new Texture2D(webcamTexture.width, webcamTexture.height);
        capturedPhoto.SetPixels(webcamTexture.GetPixels());
        capturedPhoto.Apply();
        if (cameraPreview != null) cameraPreview.texture = capturedPhoto;
        state = CameraState.Frozen;
        if (buttonText != null) buttonText.text = "Feed";
    }

    void FeedPhoto()
    {
        if (feedController != null)
            feedController.FeedWithPhoto(capturedPhoto);

        if (SaveManager.Instance != null && SaveManager.Instance.data != null)
        {
            SaveManager.Instance.data.lastCaptureTime = DateTime.Now.ToString();
            SaveManager.Instance.Save();
        }

        if (healthManager != null) healthManager.AddHealth();
        else FindObjectOfType<PetHealthManager>()?.AddHealth();

        if (cameraPreview != null && cameraPreview.gameObject != null)
            cameraPreview.gameObject.SetActive(false);
        if (mainButton != null) mainButton.gameObject.SetActive(true);
        if (mainButton != null) mainButton.interactable = true;
        if (buttonText != null) buttonText.text = "Camera";
        ResetFlow();
    }

    public void ResetFlow()
    {
        if (webcamTexture != null) { webcamTexture.Stop(); webcamTexture = null; }
        if (cameraPreview != null && cameraPreview.gameObject != null)
            cameraPreview.gameObject.SetActive(false);
        state = CameraState.Idle;
    }

    /// <summary>True when camera is in Preview or Frozen (album should not open).</summary>
    public bool IsCameraActive()
    {
        return state == CameraState.Preview || state == CameraState.Frozen;
    }
}