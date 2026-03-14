using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq; // 必須引用，用來處理 GroupBy
using TMPro; // 使用 TextMeshPro

public class AlbumUI : MonoBehaviour
{
    public Transform content;
    public GameObject photoItemPrefab;
    [Header("Phase 3 新增")]
    public GameObject dateHeaderPrefab; // 請在 Inspector 建立一個簡單的文字 Prefab 並拉進來

    void OnEnable()
    {
        ReloadFromSave();
        SaveManager.OnSaveDataChanged += ReloadFromSave;
    }

    void OnDisable()
    {
        SaveManager.OnSaveDataChanged -= ReloadFromSave;
    }

    public void ReloadFromSave()
    {
        // 1. 安全檢查
        if (SaveManager.Instance == null || SaveManager.Instance.data == null || SaveManager.Instance.data.photos == null)
        {
            Debug.LogWarning("[AlbumUI] SaveManager 尚未準備好，取消本次載入。");
            return;
        }

        // 清理舊的 UI
        foreach (Transform c in content)
            Destroy(c.gameObject);

        // 2. 核心邏輯：依照日期 (Month Day) 分組；跳過 timestamp 為 null 或過短的項目，避免 Substring 崩潰
        const int minTimestampLength = 16; // "yyyy-MM-dd HH:mm" 至少 16
        var validPhotos = SaveManager.Instance.data.photos
            .Where(p => p != null && !string.IsNullOrEmpty(p.timestamp) && p.timestamp.Length >= 10)
            .ToList();
        var groupedPhotos = validPhotos
            .GroupBy(p => p.timestamp.Length >= 10 ? p.timestamp.Substring(5, 5) : "00-00")
            .OrderByDescending(g => g.Key);

        foreach (var group in groupedPhotos)
        {
            // --- A. 生成日期標題 (左上角 Month Day) ---
            if (dateHeaderPrefab != null)
            {
                GameObject header = Instantiate(dateHeaderPrefab, content);
                TMP_Text headerText = header.GetComponentInChildren<TMP_Text>();
                if (headerText != null)
                {
                    // 將 "03-02" 轉換為 "03 02" 或妳喜歡的格式
                    headerText.text = group.Key.Replace("-", " "); 
                }
            }

            // --- B. 生成該日期下的所有縮圖 ---
            foreach (var meta in group)
            {
                Texture2D photo = SaveManager.Instance.LoadPhoto(meta);
                if (photo == null) continue;

                GameObject item = Instantiate(photoItemPrefab, content);
                
                // 設定照片
                RawImage ri = item.GetComponent<RawImage>();
                if (ri != null) ri.texture = photo;

                // 設定縮圖右上角時間 (HH:mm)
                // 從 "2026-03-02 10:15:00" 擷取索引 11 開始的 5 個字元得到 "10:15"
                TMP_Text timeText = item.transform.Find("TimeText")?.GetComponent<TMP_Text>();
                if (timeText != null && meta.timestamp.Length >= 16)
                {
                    timeText.text = meta.timestamp.Substring(11, 5);
                }
            }
        }
        
        // 3. 刷新佈局
        Canvas.ForceUpdateCanvases();
        if (content.TryGetComponent<RectTransform>(out var rect))
        {
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
        }
    }
}