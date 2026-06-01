using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Floating avatar button on RoomPage. Tap to open SettingsPanel.
/// Attach to a Button GameObject that also has a RawImage child for the avatar.
/// </summary>
public class SettingsAvatarButton : MonoBehaviour
{
    [SerializeField] Button button;
    [SerializeField] RawImage avatarImage;
    [SerializeField] Texture defaultAvatar;
    [SerializeField] PageManager pageManager;

    void Awake()
    {
        if (button == null) button = GetComponent<Button>();
        if (avatarImage == null) avatarImage = GetComponentInChildren<RawImage>();
        if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
    }

    void OnEnable()
    {
        if (button != null) { button.onClick.RemoveListener(OpenSettings); button.onClick.AddListener(OpenSettings); }
        AvatarManager.OnAvatarChanged += RefreshAvatar;
        RefreshAvatar();
    }

    void OnDisable()
    {
        if (button != null) button.onClick.RemoveListener(OpenSettings);
        AvatarManager.OnAvatarChanged -= RefreshAvatar;
    }

    void RefreshAvatar()
    {
        if (avatarImage == null) return;
        Texture tex = AvatarManager.Instance != null ? AvatarManager.Instance.CurrentAvatar : null;
        avatarImage.texture = tex != null ? tex : defaultAvatar;
    }

    void OpenSettings()
    {
        if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
        if (pageManager != null)
            pageManager.ShowSettingsPage();
        else
            Debug.LogWarning("[SettingsAvatarButton] PageManager not found.");
    }
}
