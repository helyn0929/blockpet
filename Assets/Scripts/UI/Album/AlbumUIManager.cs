using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Coordinates album grid → full-screen photo detail. Place on <c>MainGame</c> (or Canvas) and assign references.
/// </summary>
public class AlbumUIManager : MonoBehaviour
{
    public static AlbumUIManager Instance { get; private set; }

    [SerializeField] PageManager pageManager;
    [SerializeField] PhotoDetailUIPage photoDetailUIPage;

    /// <summary>
    /// Use this from UI callbacks. <see cref="Instance"/> is only set after <c>Awake</c>;
    /// if this component sits on an inactive GameObject, <c>Awake</c> has not run yet and <c>Instance</c> is null.
    /// </summary>
    public static AlbumUIManager Resolve()
    {
        if (Instance != null)
            return Instance;
        return FindObjectOfType<AlbumUIManager>(true);
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (pageManager == null)
            pageManager = FindObjectOfType<PageManager>();
        if (photoDetailUIPage == null)
            photoDetailUIPage = FindObjectOfType<PhotoDetailUIPage>(true);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void EnsureReferences()
    {
        if (pageManager == null)
            pageManager = FindObjectOfType<PageManager>(true);
        if (photoDetailUIPage == null)
            photoDetailUIPage = FindObjectOfType<PhotoDetailUIPage>(true);
    }

    public void OpenPhotoDetail(PhotoMeta meta)
    {
        if (meta == null || SaveManager.Instance == null)
            return;

        EnsureReferences();

        if (pageManager == null)
        {
            Debug.LogWarning("[AlbumUIManager] PageManager not found.");
            return;
        }

        if (!pageManager.HasPhotoDetailPage)
        {
            Debug.LogWarning("[AlbumUIManager] PageManager has no Photo Detail Page assigned.");
            return;
        }

        // Build sorted list matching AlbumUI's display order (newest first).
        List<PhotoMeta> allPhotos = SaveManager.Instance.data?.photos
            ?.Where(p => p != null && !string.IsNullOrEmpty(p.fileName))
            .OrderByDescending(p => p.timestamp ?? "")
            .ToList() ?? new List<PhotoMeta>();

        int index = allPhotos.FindIndex(p => p != null && p.fileName == meta.fileName);
        if (index < 0) index = 0;

        Texture2D tex = SaveManager.Instance.LoadPhoto(meta);
        if (tex == null)
        {
            Debug.LogWarning("[AlbumUIManager] Could not load photo for detail view.");
            return;
        }

        if (photoDetailUIPage != null)
            photoDetailUIPage.Display(tex, meta, allPhotos, index);
        else
            Debug.LogWarning("[AlbumUIManager] PhotoDetailUIPage not found.");

        pageManager.ShowPhotoDetailPage();
    }

    /// <summary>Back from photo detail to album grid.</summary>
    public void ClosePhotoDetail()
    {
        EnsureReferences();
        if (pageManager != null)
            pageManager.ShowAlbumPage();
    }
}
