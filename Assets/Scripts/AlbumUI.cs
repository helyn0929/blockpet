using UnityEngine;
using UnityEngine.UI;

public class AlbumUI : MonoBehaviour
{
    public static AlbumUI Instance;
    public Transform content;
    public GameObject photoItemPrefab;

    void Awake()
    {
        Instance = this;
    }

    // 當 FeedController 餵食成功時呼叫
    public void AddPhotoItem(Texture2D photo)
    {
        if (photoItemPrefab == null || content == null) return;

        GameObject item = Instantiate(photoItemPrefab, content);
        RawImage ri = item.GetComponent<RawImage>();
        if (ri != null) ri.texture = photo;
        
        Debug.Log("[AlbumUI] 成功在 UI 生成一張照片");
    }

    public void Refresh()
    {
        foreach (Transform child in content) Destroy(child.gameObject);
        foreach (var data in AlbumManager.Instance.photos)
        {
            AddPhotoItem(data.photo);
        }
    }
}