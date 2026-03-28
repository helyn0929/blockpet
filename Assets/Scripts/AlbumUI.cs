using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TMPro;

public class AlbumUI : MonoBehaviour
{
    public Transform content;
    public GameObject photoItemPrefab;
    [Header("Date Header")]
    public GameObject dateHeaderPrefab;

    [Header("Grid Layout")]
    [Tooltip("Number of photos per row.")]
    [SerializeField] int columns = 2;
    [Tooltip("Spacing between photos in pixels.")]
    [SerializeField] float spacing = 10f;
    [Tooltip("Padding around the photo grid edges.")]
    [SerializeField] float padding = 10f;

    float contentWidth;

    void OnEnable()
    {
        Debug.Log("[AlbumUI] OnEnable called");
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

    void CacheContentWidth()
    {
        if (content == null) return;
        RectTransform contentRect = content as RectTransform;
        if (contentRect == null) return;

        contentWidth = contentRect.rect.width;
        if (contentWidth <= 0)
        {
            Canvas.ForceUpdateCanvases();
            contentWidth = contentRect.rect.width;
        }
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

                RawImage ri = item.GetComponentInChildren<RawImage>();
                if (ri != null) ri.texture = photo;

                TMP_Text timeText = item.transform.Find("TimeText")?.GetComponent<TMP_Text>();
                if (timeText != null && meta.timestamp.Length >= 16)
                    timeText.text = meta.timestamp.Substring(11, 5);

                RawImage userAvatar = item.transform.Find("UserAvatarMask/UserAvatar")?.GetComponent<RawImage>();
                if (userAvatar != null && AvatarManager.Instance != null && AvatarManager.Instance.CurrentAvatar != null)
                    userAvatar.texture = AvatarManager.Instance.CurrentAvatar;
            }
        }

        Canvas.ForceUpdateCanvases();
        if (content.TryGetComponent<RectTransform>(out var rect))
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
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