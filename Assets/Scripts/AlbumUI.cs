using UnityEngine;
using UnityEngine.UI;

public class AlbumUI : MonoBehaviour
{
    public Transform content;
    public GameObject photoItemPrefab;

    void OnEnable()
    {
        ReloadFromSave();
        SaveManager.OnSaveDataChanged += ReloadFromSave;
    }

    void OnDisable()
    {
        SaveManager.OnSaveDataChanged -= ReloadFromSave;
    }

    void ReloadFromSave()
    {
        Debug.Log("[AlbumUI] Reload start");
        
        // 1. 安全檢查必須放在最前面
        if (SaveManager.Instance == null || SaveManager.Instance.data == null || SaveManager.Instance.data.photos == null)
        {
            Debug.LogWarning("[AlbumUI] SaveManager 尚未準備好，取消本次載入。");
            return;
        }

        // 2. 現在可以安全地印 Log 了
        Debug.Log("[AlbumUI] metas count = " + SaveManager.Instance.data.photos.Count);

        // 清理舊的 UI
        foreach (Transform c in content)
            Destroy(c.gameObject);

        // 生成照片列表
        foreach (var meta in SaveManager.Instance.data.photos)
        {
            Texture2D photo = SaveManager.Instance.LoadPhoto(meta);
            if (photo == null) continue;

            GameObject item = Instantiate(photoItemPrefab, content);
            RawImage ri = item.GetComponent<RawImage>();
            if (ri != null) ri.texture = photo;
        }
        
        // 在 ReloadFromSave 的 foreach 結束後
        Canvas.ForceUpdateCanvases();
        // 強制讓 Content 物件重新計算 layout
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(content.GetComponent<RectTransform>());
       
    }
}

// 未來 Phase 3
// TMP_Text timeText = item.GetComponentInChildren<TMP_Text>();
// timeText.text = meta.timestamp.Substring(11, 5);
