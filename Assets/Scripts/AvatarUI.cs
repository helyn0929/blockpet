using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays the user avatar in the top-right corner of the main game UI.
/// Subscribes to AvatarManager.OnAvatarChanged so the image updates automatically.
///
/// Inspector setup:
///   avatarImage  – a RawImage inside a circular mask (use avatar_mask00.png as the mask).
///   defaultAvatar – drag in avatar_test00.png or any placeholder texture.
///   changeAvatarButton – (optional) button that opens the gallery picker.
/// </summary>
public class AvatarUI : MonoBehaviour
{
    [Header("Avatar Display")]
    [Tooltip("RawImage that shows the user avatar (should be inside a Mask for circular crop).")]
    [SerializeField] RawImage avatarImage;

    [Tooltip("Default placeholder shown when no avatar is set.")]
    [SerializeField] Texture defaultAvatar;

    [Header("Optional")]
    [Tooltip("Button the user taps to change their avatar (opens gallery).")]
    [SerializeField] Button changeAvatarButton;

    void Start()
    {
        if (changeAvatarButton != null)
            changeAvatarButton.onClick.AddListener(OnChangeAvatarPressed);

        if (avatarImage == null)
            Debug.LogError("[AvatarUI] avatarImage is NOT assigned in the Inspector! Drag your RawImage here.");
        if (defaultAvatar == null)
            Debug.LogWarning("[AvatarUI] defaultAvatar is not assigned; avatar will be blank when no photo is set.");

        RefreshDisplay();
    }

    void OnEnable()
    {
        AvatarManager.OnAvatarChanged += RefreshDisplay;
        RefreshDisplay();
    }

    void OnDisable()
    {
        AvatarManager.OnAvatarChanged -= RefreshDisplay;
    }

    void RefreshDisplay()
    {
        if (avatarImage == null) return;

        Texture tex = null;
        if (AvatarManager.Instance != null)
            tex = AvatarManager.Instance.CurrentAvatar;

        avatarImage.texture = tex != null ? tex : defaultAvatar;
        Debug.Log($"[AvatarUI] RefreshDisplay → texture={(tex != null ? tex.name : "default/null")}");
    }

    void OnChangeAvatarPressed()
    {
        if (AvatarManager.Instance != null)
            AvatarManager.Instance.PickAvatarFromGallery();
        else
            Debug.LogWarning("[AvatarUI] AvatarManager.Instance is null.");
    }
}
