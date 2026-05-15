using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

/// <summary>
/// Lives on the PhotoDetailPage root. Call <see cref="Display"/> before showing the page.
/// Supports left/right swipe to navigate to the next/previous photo.
/// </summary>
public class PhotoDetailUIPage : MonoBehaviour, IBeginDragHandler, IEndDragHandler
{
    [SerializeField] RawImage photoRawImage;
    [SerializeField] TMP_Text dateText;
    [SerializeField] TMP_Text captionText;
    [Tooltip("Hidden when caption is empty.")]
    [SerializeField] GameObject captionContainer;
    [SerializeField] Button backButton;

    [Header("Taker Avatar")]
    [Tooltip("RawImage shown in the bottom-right corner displaying who took this photo.")]
    [SerializeField] RawImage takerAvatarImage;

    [Tooltip("Minimum horizontal swipe distance (pixels) to navigate photos.")]
    [SerializeField] float swipeThreshold = 60f;

    List<PhotoMeta> _photos;
    int _currentIndex;
    Vector2 _dragStart;

    void Awake()
    {
        if (backButton != null)
            backButton.onClick.AddListener(OnBackClicked);
    }

    void OnDestroy()
    {
        if (backButton != null)
            backButton.onClick.RemoveListener(OnBackClicked);
    }

    void OnBackClicked()
    {
        AlbumUIManager mgr = AlbumUIManager.Resolve();
        if (mgr != null)
            mgr.ClosePhotoDetail();
        else
            FindObjectOfType<PageManager>(true)?.ShowAlbumPage();
    }

    /// <summary>
    /// Show a photo with optional swipe context.
    /// </summary>
    /// <param name="texture">Texture to display.</param>
    /// <param name="meta">Metadata for the photo.</param>
    /// <param name="photos">Full sorted list of photos (enables swipe navigation).</param>
    /// <param name="index">Index of <paramref name="meta"/> in <paramref name="photos"/>.</param>
    public void Display(Texture2D texture, PhotoMeta meta, List<PhotoMeta> photos = null, int index = 0)
    {
        _photos = photos;
        _currentIndex = index;
        ShowPhoto(texture, meta);
    }

    // ── Swipe handlers ──────────────────────────────────────────────────

    public void OnBeginDrag(PointerEventData eventData)
    {
        _dragStart = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_photos == null || _photos.Count <= 1) return;

        float dx = eventData.position.x - _dragStart.x;
        if (Mathf.Abs(dx) < swipeThreshold) return;

        if (dx < 0)
            Navigate(_currentIndex + 1); // swipe left → next
        else
            Navigate(_currentIndex - 1); // swipe right → previous
    }

    void Navigate(int newIndex)
    {
        if (_photos == null || newIndex < 0 || newIndex >= _photos.Count) return;
        _currentIndex = newIndex;
        PhotoMeta meta = _photos[_currentIndex];
        Texture2D tex = SaveManager.Instance?.LoadPhoto(meta);
        ShowPhoto(tex, meta);
    }

    // ── Internal display ─────────────────────────────────────────────────

    void ShowPhoto(Texture2D texture, PhotoMeta meta)
    {
        if (photoRawImage != null)
        {
            photoRawImage.texture = texture;
            photoRawImage.enabled = texture != null;
        }

        if (dateText != null)
            dateText.text = meta != null ? FormatDateTime(meta.timestamp) : string.Empty;

        bool hasCaption = meta != null && !string.IsNullOrWhiteSpace(meta.caption);
        if (captionText != null)
        {
            captionText.text = hasCaption ? meta.caption.Trim() : string.Empty;
            captionText.gameObject.SetActive(hasCaption);
        }
        if (captionContainer != null)
            captionContainer.SetActive(hasCaption);

        LoadTakerAvatar(meta?.takerAvatarFileName);
    }

    void LoadTakerAvatar(string avatarFileName)
    {
        if (takerAvatarImage == null) return;

        if (string.IsNullOrEmpty(avatarFileName))
        {
            takerAvatarImage.enabled = false;
            return;
        }

        string path = Path.Combine(Application.persistentDataPath, "avatars", avatarFileName);
        if (!File.Exists(path))
        {
            takerAvatarImage.enabled = false;
            return;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2);
            if (tex.LoadImage(bytes))
            {
                takerAvatarImage.texture = tex;
                takerAvatarImage.enabled = true;
            }
            else
            {
                Destroy(tex);
                takerAvatarImage.enabled = false;
            }
        }
        catch
        {
            takerAvatarImage.enabled = false;
        }
    }

    static string FormatDateTime(string timestamp)
    {
        if (string.IsNullOrEmpty(timestamp))
            return string.Empty;
        if (System.DateTime.TryParse(timestamp, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces, out var dt))
            return dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
        return timestamp;
    }
}
