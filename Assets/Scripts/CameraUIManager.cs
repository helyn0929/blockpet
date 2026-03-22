using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

/// <summary>
/// Centralized UI state machine that controls the three bottom buttons
/// (Camera, Chat, Album) and re-maps them during camera mode.
///
/// States:
///   Home          – Camera = enter camera, Chat = toggle chat, Album = toggle album.
///   CameraPreview – Camera = take photo,   Chat = switch front/back cam, Album = exit (Turn Out).
///   PhotoTaken    – Camera = feed pet,     Chat = disabled,              Album = retake (Turn Out).
///
/// "Turn Out" flow:
///   PhotoTaken    → Turn Out → CameraPreview  (retake)
///   CameraPreview → Turn Out → Home           (exit)
/// </summary>
public enum UIMode { Home, CameraPreview, PhotoTaken }

public class CameraUIManager : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] Button cameraButton;
    [SerializeField] Button chatButton;
    [SerializeField] Button albumButton;

    [Header("Button Labels (TMP)")]
    [SerializeField] TMP_Text cameraButtonText;
    [SerializeField] TMP_Text chatButtonText;
    [SerializeField] TMP_Text albumButtonText;

    [Header("Home-State Labels")]
    [SerializeField] string homeCameraLabel = "Camera";
    [SerializeField] string homeChatLabel   = "Chat";
    [SerializeField] string homeAlbumLabel  = "Album";

    [Header("Panels")]
    [SerializeField] GameObject chatPanel;
    [SerializeField] GameObject albumPanel;

    [Header("Dependencies")]
    [SerializeField] CameraController cameraController;

    [Header("Legacy Toggle Scripts (auto-disabled)")]
    [Tooltip("Drag in the AlbumPanelToggle component so this manager can disable it.")]
    [SerializeField] AlbumPanelToggle albumPanelToggle;
    [Tooltip("Drag in the ChatPanelToggle component so this manager can disable it.")]
    [SerializeField] ChatPanelToggle chatPanelToggle;

    UIMode mode = UIMode.Home;
    public UIMode CurrentMode => mode;

    void Start()
    {
        if (cameraController != null)
            cameraController.uiManagedExternally = true;

        if (albumPanelToggle != null) albumPanelToggle.enabled = false;
        if (chatPanelToggle  != null) chatPanelToggle.enabled  = false;

        WireButton(cameraButton, OnCameraButtonPressed);
        WireButton(chatButton,   OnChatButtonPressed);
        WireButton(albumButton,  OnAlbumButtonPressed);

        ApplyState(UIMode.Home);
    }

    void Update()
    {
        if (mode == UIMode.Home)
            UpdateCooldownDisplay();
    }

    // ================================================================
    //  Button handlers — behaviour depends on current UIMode
    // ================================================================

    void OnCameraButtonPressed()
    {
        switch (mode)
        {
            case UIMode.Home:
                EnterCameraMode();
                break;
            case UIMode.CameraPreview:
                TakePhoto();
                break;
            case UIMode.PhotoTaken:
                FeedAndExit();
                break;
        }
    }

    void OnChatButtonPressed()
    {
        switch (mode)
        {
            case UIMode.Home:
                ToggleChatPanel();
                break;
            case UIMode.CameraPreview:
                SwitchCamera();
                break;
        }
    }

    void OnAlbumButtonPressed()
    {
        switch (mode)
        {
            case UIMode.Home:
                ToggleAlbumPanel();
                break;
            case UIMode.CameraPreview:
                ExitCameraMode();
                break;
            case UIMode.PhotoTaken:
                RetakePhoto();
                break;
        }
    }

    // ================================================================
    //  State transitions
    // ================================================================

    void EnterCameraMode()
    {
        if (chatPanel  != null) chatPanel.SetActive(false);
        if (albumPanel != null) albumPanel.SetActive(false);

        if (cameraController != null)
            cameraController.StartCamera();

        ApplyState(UIMode.CameraPreview);
    }

    void TakePhoto()
    {
        if (cameraController != null)
            cameraController.TakePhoto();

        ApplyState(UIMode.PhotoTaken);
    }

    void RetakePhoto()
    {
        if (cameraController != null)
            cameraController.RetakePhoto();

        ApplyState(UIMode.CameraPreview);
    }

    void FeedAndExit()
    {
        if (cameraController != null)
            cameraController.FeedPhoto();

        ApplyState(UIMode.Home);
    }

    void SwitchCamera()
    {
        if (cameraController != null)
            cameraController.SwitchCamera();

        UpdateCameraSwitchLabel();
    }

    void ExitCameraMode()
    {
        if (cameraController != null)
            cameraController.ResetFlow();

        ApplyState(UIMode.Home);
    }

    // ================================================================
    //  Home-state panel helpers (replaces legacy toggle scripts)
    // ================================================================

    void ToggleChatPanel()
    {
        if (chatPanel != null)
            chatPanel.SetActive(!chatPanel.activeSelf);
    }

    void ToggleAlbumPanel()
    {
        if (albumPanel == null) return;

        if (albumPanel.activeSelf)
        {
            albumPanel.SetActive(false);
            return;
        }

        if (cameraController != null && cameraController.IsCameraActive())
            return;

        albumPanel.SetActive(true);
    }

    // ================================================================
    //  UI refresh — single source of truth for button labels
    // ================================================================

    void ApplyState(UIMode newMode)
    {
        mode = newMode;

        switch (mode)
        {
            case UIMode.Home:
                SetLabel(cameraButtonText, homeCameraLabel);
                SetLabel(chatButtonText,   homeChatLabel);
                SetLabel(albumButtonText,  homeAlbumLabel);
                SetInteractable(cameraButton, true);
                SetInteractable(chatButton,   true);
                SetInteractable(albumButton,  true);
                break;

            case UIMode.CameraPreview:
                SetLabel(cameraButtonText, "Shot");
                SetLabel(albumButtonText,  "Turn Out");
                SetInteractable(cameraButton, true);
                SetInteractable(chatButton,   true);
                SetInteractable(albumButton,  true);
                UpdateCameraSwitchLabel();
                break;

            case UIMode.PhotoTaken:
                SetLabel(cameraButtonText, "Feed");
                SetLabel(albumButtonText,  "Turn Out");
                SetInteractable(cameraButton, true);
                SetInteractable(chatButton,   false);
                SetInteractable(albumButton,  true);
                break;
        }
    }

    /// <summary>Sets the chat button label to reflect which camera will be activated next.</summary>
    void UpdateCameraSwitchLabel()
    {
        if (cameraController == null) return;
        SetLabel(chatButtonText, cameraController.IsFrontCamera ? "Back" : "Front");
    }

    // ================================================================
    //  Cooldown display (camera button, Home state only)
    // ================================================================

    void UpdateCooldownDisplay()
    {
        if (cameraButtonText == null || cameraButton == null) return;

        if (SaveManager.Instance == null || SaveManager.Instance.data == null ||
            string.IsNullOrEmpty(SaveManager.Instance.data.lastCaptureTime))
        {
            cameraButtonText.text = homeCameraLabel;
            cameraButton.interactable = true;
            return;
        }

        if (DateTime.TryParse(SaveManager.Instance.data.lastCaptureTime, out DateTime lastTime))
        {
            TimeSpan elapsed = DateTime.Now - lastTime;
            double remaining = 300 - elapsed.TotalSeconds;

            if (remaining > 0)
            {
                cameraButton.interactable = false;
                TimeSpan t = TimeSpan.FromSeconds(remaining);
                cameraButtonText.text = string.Format("{0:D2}:{1:D2}", t.Minutes, t.Seconds);
            }
            else
            {
                cameraButton.interactable = true;
                cameraButtonText.text = homeCameraLabel;
            }
        }
    }

    // ================================================================
    //  Utilities
    // ================================================================

    static void WireButton(Button btn, UnityEngine.Events.UnityAction action)
    {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(action);
    }

    static void SetLabel(TMP_Text text, string label)
    {
        if (text != null) text.text = label;
    }

    static void SetInteractable(Button btn, bool value)
    {
        if (btn != null) btn.interactable = value;
    }
}
