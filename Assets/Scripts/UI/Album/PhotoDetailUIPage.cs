using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Globalization;

/// <summary>
/// Lives on the PhotoDetailPage root. Call <see cref="Display"/> before showing the page.
/// </summary>
public class PhotoDetailUIPage : MonoBehaviour
{
    [SerializeField] RawImage photoRawImage;
    [SerializeField] TMP_Text dateText;
    [SerializeField] TMP_Text captionText;
    [Tooltip("Hidden when caption is empty.")]
    [SerializeField] GameObject captionContainer;
    [SerializeField] Button backButton;

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

    public void Display(Texture2D texture, PhotoMeta meta)
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
