using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to each album thumbnail (or add at runtime from <see cref="AlbumUI"/>).
/// Requires a <see cref="Button"/> (added automatically if missing, using the first <see cref="RawImage"/> as target).
/// </summary>
[RequireComponent(typeof(Button))]
public class PhotoItemClick : MonoBehaviour
{
    PhotoMeta _meta;
    Button _button;

    void Awake()
    {
        _button = GetComponent<Button>();
    }

    void OnEnable()
    {
        if (_button != null)
            _button.onClick.AddListener(OnClicked);
    }

    void OnDisable()
    {
        if (_button != null)
            _button.onClick.RemoveListener(OnClicked);
    }

    /// <summary>Called by <see cref="AlbumUI"/> when the cell is spawned.</summary>
    public void Initialize(PhotoMeta meta)
    {
        _meta = meta;
    }

    void OnClicked()
    {
        if (_meta == null)
            return;

        AlbumUIManager mgr = AlbumUIManager.Resolve();
        if (mgr != null)
            mgr.OpenPhotoDetail(_meta);
        else
            Debug.LogWarning("[PhotoItemClick] No AlbumUIManager in the scene. Add one (e.g. on MainGame) and assign PageManager / PhotoDetailUIPage if needed.");
    }
}
