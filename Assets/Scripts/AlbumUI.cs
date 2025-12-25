using UnityEngine;
using UnityEngine.UI;

public class AlbumUI : MonoBehaviour
{
    public Transform content;
    public GameObject photoItemPrefab;

    void Start()
    {
        Refresh();
    }

    public void Refresh()
    {
        // 清空舊的照片
        foreach (Transform child in content)
        {
            Destroy(child.gameObject);
        }

        // 依序產生照片
        foreach (var data in AlbumManager.Instance.photos)
        {
            GameObject item = Instantiate(photoItemPrefab, content);
            item.GetComponent<RawImage>().texture = data.photo;
        }
    }
}
