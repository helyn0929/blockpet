using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System;
using TMPro;

public class AlbumUI : MonoBehaviour
{
    public Transform content;
    public GameObject photoItemPrefab;
    [Header("Date Header")]
    public GameObject dateHeaderPrefab;

    [Header("Album leave / back")]
    [Tooltip("Drag the UI Button that should close the album and return to Home. Optional if you use Button → OnClick → LeaveAlbum instead.")]
    public Button albumLeaveButton;
    [Tooltip("Optional. If empty, AlbumUI finds a PageManager in the scene at runtime.")]
    public PageManager pageManager;

    [Header("Scroll view (fill screen)")]
    [Tooltip("Assign the album Scroll View, or leave empty to use the first ScrollRect under this object.")]
    [SerializeField] ScrollRect albumScrollRect;
    [Tooltip("Stretch ScrollRect to fill its parent (AlbumPage). Turn off if you lay out the scroll area manually.")]
    [SerializeField] bool fitScrollViewToParent = true;
    [Tooltip("Shrink scroll area from album panel edges (canvas units). Use top inset if a title/leave bar sits above the list.")]
    [SerializeField] float scrollInsetLeft;
    [SerializeField] float scrollInsetRight;
    [SerializeField] float scrollInsetTop;
    [SerializeField] float scrollInsetBottom;

    [Header("Grid Layout")]
    [Tooltip("Number of photos per row.")]
    [SerializeField] int columns = 2;
    [Tooltip("Spacing between photos in pixels.")]
    [SerializeField] float spacing = 10f;
    [Tooltip("Padding around the photo grid edges.")]
    [SerializeField] float padding = 10f;

    float contentWidth;

    void Awake()
    {
        if (pageManager == null)
            pageManager = FindObjectOfType<PageManager>();
        if (albumScrollRect == null)
            albumScrollRect = GetComponentInChildren<ScrollRect>(true);
        if (albumLeaveButton != null)
            albumLeaveButton.onClick.AddListener(LeaveAlbum);
    }

    void OnDestroy()
    {
        if (albumLeaveButton != null)
            albumLeaveButton.onClick.RemoveListener(LeaveAlbum);
    }

    /// <summary>Returns to chat or home depending on how album was opened.</summary>
    public void LeaveAlbum()
    {
        if (pageManager != null)
            pageManager.CloseAlbumPage();
    }

    void OnEnable()
    {
        Debug.Log("[AlbumUI] OnEnable called");
        ApplyScrollViewScreenFit();
        EnsureScrollContentWidthStretchesViewport();
        CacheContentWidth();
        ConfigureContentLayout();
        ReloadFromSave();
        SaveManager.OnSaveDataChanged += ReloadFromSave;
    }

    void OnDisable()
    {
        Debug.Log("[AlbumUI] OnDisable called");
        SaveManager.OnSaveDataChanged -= ReloadFromSave;
    }

    /// <summary>
    /// Makes the ScrollRect + Viewport use stretch anchors so the list uses the full album panel (minus optional insets).
    /// </summary>
    void ApplyScrollViewScreenFit()
    {
        if (!fitScrollViewToParent || albumScrollRect == null)
            return;

        var rt = albumScrollRect.transform as RectTransform;
        if (rt == null || rt.parent is not RectTransform)
            return;

        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(scrollInsetLeft, scrollInsetBottom);
        rt.offsetMax = new Vector2(-scrollInsetRight, -scrollInsetTop);

        RectTransform viewport = albumScrollRect.viewport;
        if (viewport != null)
        {
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
        }
    }

    /// <summary>
    /// Scroll content should span the viewport width so <see cref="CacheContentWidth"/> and grids match the device.
    /// </summary>
    void EnsureScrollContentWidthStretchesViewport()
    {
        if (albumScrollRect == null || albumScrollRect.viewport == null || content == null)
            return;
        if (content is not RectTransform contentRect)
            return;

        RectTransform vp = albumScrollRect.viewport;
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = new Vector2(0f, contentRect.offsetMin.y);
        contentRect.offsetMax = new Vector2(0f, contentRect.offsetMax.y);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(vp);
    }

    void CacheContentWidth()
    {
        if (content == null) return;
        RectTransform contentRect = content as RectTransform;
        if (contentRect == null) return;

        contentWidth = contentRect.rect.width;
        if (contentWidth <= 0)
        {
            Canvas.ForceUpdateCanvases();
            if (albumScrollRect != null && albumScrollRect.viewport != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(albumScrollRect.viewport);
            contentWidth = contentRect.rect.width;
        }
        if (contentWidth <= 0f && albumScrollRect != null && albumScrollRect.viewport != null)
            contentWidth = Mathf.Max(1f, albumScrollRect.viewport.rect.width);
        Debug.Log($"[AlbumUI] contentWidth={contentWidth}");
    }

    void ConfigureContentLayout()
    {
        if (content == null) return;

        // Remove GridLayoutGroup if it exists (use Immediate so it's gone before we add VerticalLayoutGroup)
        GridLayoutGroup oldGrid = content.GetComponent<GridLayoutGroup>();
        if (oldGrid != null) DestroyImmediate(oldGrid);

        // Content uses VerticalLayoutGroup so headers and photo grids stack vertically
        VerticalLayoutGroup vLayout = content.GetComponent<VerticalLayoutGroup>();
        if (vLayout == null) vLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vLayout.childAlignment = TextAnchor.UpperLeft;
        vLayout.childControlWidth = true;
        vLayout.childControlHeight = true;
        vLayout.childForceExpandWidth = true;
        vLayout.childForceExpandHeight = false;
        vLayout.spacing = spacing;
        vLayout.padding = new RectOffset((int)padding, (int)padding, (int)padding, (int)padding);

        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    public void ReloadFromSave()
    {
        Debug.Log("[AlbumUI] ReloadFromSave called");

        if (SaveManager.Instance == null || SaveManager.Instance.data == null || SaveManager.Instance.data.photos == null)
        {
            Debug.LogWarning("[AlbumUI] SaveManager 尚未準備好，取消本次載入。");
            return;
        }

        foreach (Transform c in content)
            Destroy(c.gameObject);

        // Group by full date (yyyy-MM-dd) so same-day photos share one header
        var validPhotos = SaveManager.Instance.data.photos
            .Where(p => p != null && !string.IsNullOrEmpty(p.timestamp) && p.timestamp.Length >= 10)
            .ToList();

        var groupedPhotos = validPhotos
            .GroupBy(p => p.timestamp.Substring(0, 10))
            .OrderByDescending(g => g.Key);

        Debug.Log($"[AlbumUI] dateHeaderPrefab={(dateHeaderPrefab != null ? dateHeaderPrefab.name : "NULL")}, groups={groupedPhotos.Count()}, photos={validPhotos.Count}");

        foreach (var group in groupedPhotos)
        {
            // --- A. Date header ---
            if (dateHeaderPrefab != null)
            {
                GameObject header = Instantiate(dateHeaderPrefab, content);
                TMP_Text headerText = header.GetComponentInChildren<TMP_Text>();
                if (headerText != null)
                {
                    headerText.text = FormatDateHeader(group.Key);
                    Debug.Log($"[AlbumUI] Created header: '{headerText.text}', color={headerText.color}, fontSize={headerText.fontSize}");
                }
                else
                {
                    Debug.LogWarning("[AlbumUI] dateHeaderPrefab has no TMP_Text component!");
                }

                LayoutElement headerLayout = header.GetComponent<LayoutElement>();
                if (headerLayout == null) headerLayout = header.AddComponent<LayoutElement>();
                headerLayout.minHeight = 40f;
                headerLayout.preferredHeight = 40f;

                RectTransform headerRect = header.GetComponent<RectTransform>();
                if (headerRect != null)
                    headerRect.sizeDelta = new Vector2(headerRect.sizeDelta.x, 40f);
            }

            // --- B. Photo grid container for this date ---
            GameObject gridContainer = new GameObject("PhotoGrid", typeof(RectTransform));
            gridContainer.transform.SetParent(content, false);

            GridLayoutGroup grid = gridContainer.AddComponent<GridLayoutGroup>();
            float cellWidth = (contentWidth - padding * 2 - spacing * (columns - 1)) / columns;
            if (cellWidth <= 0) cellWidth = 200f;
            grid.cellSize = new Vector2(cellWidth, cellWidth);
            Debug.Log($"[AlbumUI] Grid cellSize={cellWidth}x{cellWidth}, photos in group={group.Count()}");
            grid.spacing = new Vector2(spacing, spacing);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;

            // Grid container sizes itself to fit its children
            ContentSizeFitter gridFitter = gridContainer.AddComponent<ContentSizeFitter>();
            gridFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Tell the parent VerticalLayoutGroup to use preferred height
            LayoutElement gridLayout = gridContainer.AddComponent<LayoutElement>();
            gridLayout.flexibleWidth = 1f;

            // --- C. Spawn photos into the grid (newest first) ---
            foreach (var meta in group.OrderByDescending(p => p.timestamp))
            {
                Texture2D photo = SaveManager.Instance.LoadPhoto(meta);
                if (photo == null) continue;

                GameObject item = Instantiate(photoItemPrefab, gridContainer.transform);

                RawImage userAvatar = item.transform.Find("UserAvatarMask/UserAvatar")?.GetComponent<RawImage>();

                RawImage ri = FindPrimaryThumbnailRawImage(item, userAvatar);
                if (ri != null)
                {
                    ri.texture = photo;
                    ri.raycastTarget = true;
                }

                TMP_Text timeText = item.transform.Find("TimeText")?.GetComponent<TMP_Text>();
                if (timeText != null && meta.timestamp.Length >= 16)
                    timeText.text = meta.timestamp.Substring(11, 5);

                if (userAvatar != null && AvatarManager.Instance != null && AvatarManager.Instance.CurrentAvatar != null)
                    userAvatar.texture = AvatarManager.Instance.CurrentAvatar;

                WirePhotoItemClick(item, meta, ri);
            }
        }

        Canvas.ForceUpdateCanvases();
        if (content.TryGetComponent<RectTransform>(out var rect))
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
    }

    /// <summary>
    /// Prefer named children, then the largest non-avatar RawImage (GetComponentInChildren order often picks the tiny UserAvatar first).
    /// </summary>
    static RawImage FindPrimaryThumbnailRawImage(GameObject item, RawImage userAvatar)
    {
        if (item == null)
            return null;

        Transform t = item.transform.Find("Photo");
        if (t != null && t.TryGetComponent<RawImage>(out var named))
            return named;
        t = item.transform.Find("Thumbnail");
        if (t != null && t.TryGetComponent<RawImage>(out var named2))
            return named2;

        RawImage best = null;
        float bestArea = -1f;
        foreach (RawImage candidate in item.GetComponentsInChildren<RawImage>(true))
        {
            if (candidate == null || candidate == userAvatar)
                continue;
            if (IsUnderUserAvatarHierarchy(candidate.transform))
                continue;

            float area = candidate.rectTransform.rect.width * candidate.rectTransform.rect.height;
            if (area > bestArea)
            {
                bestArea = area;
                best = candidate;
            }
        }

        return best;
    }

    static bool IsUnderUserAvatarHierarchy(Transform tr)
    {
        while (tr != null)
        {
            if (tr.name.IndexOf("UserAvatar", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            tr = tr.parent;
        }
        return false;
    }

    static void WirePhotoItemClick(GameObject item, PhotoMeta meta, RawImage thumbnailGraphic)
    {
        if (item == null || meta == null)
            return;

        PhotoItemClick click = item.GetComponent<PhotoItemClick>();
        if (click == null)
            click = item.AddComponent<PhotoItemClick>();

        Button btn = item.GetComponent<Button>();
        if (thumbnailGraphic != null && btn != null)
        {
            btn.targetGraphic = thumbnailGraphic;
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        click.Initialize(meta);
    }

    /// <summary>Converts "2026-03-28" to "2026, Mar 28".</summary>
    static string FormatDateHeader(string dateKey)
    {
        if (System.DateTime.TryParseExact(dateKey, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out System.DateTime dt))
        {
            return dt.ToString("yyyy, MMM d", CultureInfo.InvariantCulture);
        }
        return dateKey;
    }
}