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
    [Tooltip("Album page/panel to hide when opening camera (avoids Album showing over Camera).")]
    public GameObject albumPanel;
    [Tooltip("When set, opening the camera returns UI to Home (closes Chat/Album pages).")]
    [SerializeField] PageManager pageManager;
    [Tooltip("Camera preview panel to show when opening camera; optional.")]
    public GameObject cameraPreviewPanel;

    [Header("Settings")]
    public FeedController feedController;
    public PetHealthManager healthManager;

    WebCamTexture webcamTexture;
    Texture2D capturedPhoto;
    CameraState state = CameraState.Idle;
    bool isFrontCamera;

    Quaternion _previewBaseRotation;
    Vector3 _previewBaseScale;

    // 鏡頭（設計文件 5.27）：數位縮放 1×～5×（裁切畫面中間，預覽和拍下的照片一樣）、魚眼（只套在顯示上，照片存原圖＋標記）
    public const float MinZoom = 1f, MaxZoom = 5f;
    float _zoom = 1f;
    bool _fisheye;
    float _pinchDist = -1f;

    /// <summary>目前倍率（1～5）。</summary>
    public float Zoom => _zoom;
    /// <summary>正在兩指縮放（鏡頭列顯示目前倍率用）。</summary>
    public bool IsPinching => _pinchDist > 0f;
    /// <summary>魚眼開關；會記住（PlayerPrefs），下次打開相機維持上次的選擇。</summary>
    public bool Fisheye
    {
        get => _fisheye;
        set
        {
            _fisheye = value;
            PlayerPrefs.SetInt(BlockPet.UI.FisheyeImage.PrefsCameraFisheye, value ? 1 : 0);
            PlayerPrefs.Save();
            ApplyFisheye();
        }
    }

    public void SetZoom(float z)
    {
        _zoom = Mathf.Clamp(z, MinZoom, MaxZoom);
        ApplyZoom();
    }

    /// <summary>預覽只顯示中間 1/倍率 的範圍（拍好的照片已經裁過，顯示整張）。</summary>
    void ApplyZoom()
    {
        if (cameraPreview == null) return;
        if (state == CameraState.Frozen) { cameraPreview.uvRect = new Rect(0f, 0f, 1f, 1f); return; }
        float s = 1f / _zoom;
        cameraPreview.uvRect = new Rect(0.5f - s / 2f, 0.5f - s / 2f, s, s);
    }

    void ApplyFisheye()
    {
        if (cameraPreview != null) BlockPet.UI.FisheyeImage.Set(cameraPreview, _fisheye);
    }

    /// <summary>兩指縮放：兩指距離的比例直接乘上倍率，1～5 連續。</summary>
    void UpdatePinch()
    {
        if (Input.touchCount != 2) { _pinchDist = -1f; return; }
        float d = Vector2.Distance(Input.GetTouch(0).position, Input.GetTouch(1).position);
        if (_pinchDist > 0f && d > 1f) SetZoom(_zoom * d / _pinchDist);
        _pinchDist = d;
    }

    /// <summary>When true, CameraUIManager handles all button labels and cooldown display.</summary>
    [HideInInspector] public bool uiManagedExternally;

    public CameraState CurrentState => state;
    public bool IsFrontCamera => isFrontCamera;

    void Awake()
    {
        if (pageManager == null)
            pageManager = FindObjectOfType<PageManager>();

        if (albumPanel == null)
        {
            var go = GameObject.Find("AlbumPage") ?? GameObject.Find("AlbumPanel");
            if (go != null) albumPanel = go;
        }
        if (cameraPreviewPanel == null && cameraPreview != null && cameraPreview.gameObject != null)
            cameraPreviewPanel = cameraPreview.gameObject;

        if (cameraPreview != null)
        {
            var rt = cameraPreview.rectTransform;
            _previewBaseRotation = rt.localRotation;
            _previewBaseScale = rt.localScale;
        }
        _fisheye = PlayerPrefs.GetInt(BlockPet.UI.FisheyeImage.PrefsCameraFisheye, 0) == 1;

        // 鏡頭列（魚眼開關、1× 2× 3×）
        if (GetComponent<CameraLensBar>() == null) gameObject.AddComponent<CameraLensBar>();
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
        if (pageManager != null)
            pageManager.ShowHomePage();
        else if (albumPanel != null)
            albumPanel.SetActive(false);
        if (cameraPreviewPanel != null) cameraPreviewPanel.SetActive(false);
    }

    void Update()
    {
        // iOS/Android: sensor buffer is often sideways; WebCamTexture reports how much to rotate the preview.
        if (state == CameraState.Preview && webcamTexture != null && webcamTexture.isPlaying)
        {
            ApplyWebcamPreviewOrientation();
            UpdatePinch();
        }
        else _pinchDist = -1f;

        // 只有在 Idle 狀態時，才檢查並顯示冷卻倒數
        if (state == CameraState.Idle && !uiManagedExternally)
        {
            UpdateCooldownDisplay();
        }
    }

    /// <summary>
    /// Aligns RawImage with <see cref="WebCamTexture.videoRotationAngle"/> and <see cref="WebCamTexture.videoVerticallyMirrored"/> (required on most phones, especially iOS).
    /// </summary>
    void ApplyWebcamPreviewOrientation()
    {
        if (cameraPreview == null || webcamTexture == null)
            return;

        var rt = cameraPreview.rectTransform;
        float angle = webcamTexture.videoRotationAngle;
        rt.localRotation = _previewBaseRotation * Quaternion.AngleAxis(-angle, Vector3.forward);

        Vector3 s = _previewBaseScale;
        if (webcamTexture.videoVerticallyMirrored)
            s.y *= -1f;
        rt.localScale = s;
    }

    void ResetPreviewOrientation()
    {
        if (cameraPreview == null)
            return;
        var rt = cameraPreview.rectTransform;
        rt.localRotation = _previewBaseRotation;
        rt.localScale = _previewBaseScale;
    }

    // 冷卻檢查（與手牌共用，見 PhotoCooldown）
    void UpdateCooldownDisplay()
    {
        if (buttonText == null || mainButton == null) return;

        if (PhotoCooldown.IsActive)
        {
            mainButton.interactable = false;
            buttonText.text = PhotoCooldown.RemainingText;
        }
        else
        {
            mainButton.interactable = true;
            buttonText.text = "Camera";
        }
    }

    public void OnMainButtonPressed()
    {
        if (uiManagedExternally) return;

        switch (state)
        {
            case CameraState.Idle: StartCamera(); break;
            case CameraState.Preview: TakePhoto(); break;
            case CameraState.Frozen: FeedPhoto(); break;
        }
    }

    public void StartCamera()
    {
        ClearOtherPanelsBeforeCamera();
        if (pageManager == null && albumPanel != null) albumPanel.SetActive(false);
        if (cameraPreviewPanel != null) cameraPreviewPanel.SetActive(true);
        if (cameraPreview != null && cameraPreview.transform != null)
            cameraPreview.transform.SetAsLastSibling();

        StartWebcam(isFrontCamera);

        if (cameraPreview != null && cameraPreview.gameObject != null)
            cameraPreview.gameObject.SetActive(true);
        state = CameraState.Preview;
        SetZoom(1f);
        ApplyFisheye();
        if (buttonText != null) buttonText.text = "Shot";
    }

    /// <summary>Toggle between front and rear camera while keeping the preview alive.</summary>
    public void SwitchCamera()
    {
        if (state != CameraState.Preview) return;

        isFrontCamera = !isFrontCamera;
        StartWebcam(isFrontCamera);
        SetZoom(1f); // 翻轉鏡頭時倍率回到 1×
    }

    void StartWebcam(bool front)
    {
        if (webcamTexture != null)
        {
            webcamTexture.Stop();
            webcamTexture = null;
        }

        string deviceName = PickDevice(front);
        webcamTexture = string.IsNullOrEmpty(deviceName)
            ? new WebCamTexture()
            : new WebCamTexture(deviceName);

        if (cameraPreview != null)
        {
            cameraPreview.texture = webcamTexture;
            ResetPreviewOrientation();
        }
        webcamTexture.Play();
    }

    static string PickDevice(bool front)
    {
        foreach (var d in WebCamTexture.devices)
            if (d.isFrontFacing == front) return d.name;

        // Fallback: if requested facing isn't available, return first device.
        return WebCamTexture.devices.Length > 0 ? WebCamTexture.devices[0].name : null;
    }

    public void TakePhoto()
    {
        if (webcamTexture == null) return;
        // 拍下的照片＝縮放後看到的範圍：裁切畫面中間 1/倍率（魚眼不套進照片，存原圖）
        int w = webcamTexture.width, h = webcamTexture.height;
        int cw = Mathf.Max(1, Mathf.RoundToInt(w / _zoom)), ch = Mathf.Max(1, Mathf.RoundToInt(h / _zoom));
        capturedPhoto = new Texture2D(cw, ch);
        capturedPhoto.SetPixels(webcamTexture.GetPixels((w - cw) / 2, (h - ch) / 2, cw, ch));
        capturedPhoto.Apply();
        if (cameraPreview != null) cameraPreview.texture = capturedPhoto;
        state = CameraState.Frozen;
        ApplyZoom();
        if (buttonText != null) buttonText.text = "Feed";
    }

    /// <summary>From Frozen → Preview: restore live webcam feed so the user can retake.</summary>
    public void RetakePhoto()
    {
        if (state != CameraState.Frozen) return;

        if (webcamTexture == null || !webcamTexture.isPlaying)
        {
            StartCamera();
            return;
        }

        if (cameraPreview != null) cameraPreview.texture = webcamTexture;
        capturedPhoto = null;
        state = CameraState.Preview;
        ApplyZoom();
        if (buttonText != null) buttonText.text = "Shot";
    }

    public void FeedPhoto()
    {
        if (feedController != null)
            feedController.FeedWithPhoto(capturedPhoto, _fisheye);

        PhotoCooldown.Start();
        // 回心、金幣在照片存好時由 FeedSettlement 結算

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
        ResetPreviewOrientation();
        state = CameraState.Idle;
        _zoom = 1f;
        if (cameraPreview != null) cameraPreview.uvRect = new Rect(0f, 0f, 1f, 1f);
    }

    /// <summary>True when camera is in Preview or Frozen (album should not open).</summary>
    public bool IsCameraActive()
    {
        return state == CameraState.Preview || state == CameraState.Frozen;
    }
}