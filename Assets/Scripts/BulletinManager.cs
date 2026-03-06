using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class BulletinManager : MonoBehaviour
{
    [Header("UI Slots")]
    public RawImage[] displaySlots; // 在 Inspector 拖入那 7 個 RawImage

    void Start()
    {
        // 訂閱 SaveManager 的更新事件，這樣拍照後公佈欄會自動更新
        SaveManager.OnSaveDataChanged += RefreshBulletin;
        
        // 啟動時先載入一次
        RefreshBulletin();
    }

    void OnDestroy()
    {
        // 養成好習慣，銷毀時取消訂閱
        SaveManager.OnSaveDataChanged -= RefreshBulletin;
    }
    
    public void RefreshBulletin()
    {
        if (SaveManager.Instance == null || SaveManager.Instance.data.photos == null) return;

        List<PhotoMeta> allPhotos = SaveManager.Instance.data.photos;
        int photoCount = allPhotos.Count;

        for (int i = 0; i < displaySlots.Length; i++)
        {
            int targetIndex = photoCount - 1 - i;

            if (targetIndex >= 0)
            {
                Texture2D tex = SaveManager.Instance.LoadPhoto(allPhotos[targetIndex]);
                if (tex != null)
                {
                    displaySlots[i].texture = tex;
                    // 重點：有照片時，要把 Color 設回不透明 (Alpha = 1)
                    displaySlots[i].color = Color.white; 
                }
            }
            else
            {
                displaySlots[i].texture = null;
                // 沒照片時，設為完全透明 (Alpha = 0)
                displaySlots[i].color = new Color(1, 1, 1, 0); 
            }
        }
    }
}