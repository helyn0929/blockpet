using UnityEngine;
using UnityEngine.UI;

public class AlbumUI : MonoBehaviour
{
    public Transform content;
    public GameObject photoItemPrefab;

    public void AddPhoto(Texture2D photo)
    {
        GameObject item = Instantiate(photoItemPrefab, content);
        item.GetComponent<RawImage>().texture = photo;
    }   

    // 打開相簿時用
    public void Refresh()
    {
        foreach (Transform child in content)
        {
            Destroy(child.gameObject);
        }

        foreach (var data in AlbumManager.Instance.photos)
        {
            AddPhotoItem(data.photo);
        }
    }

    // Feed 新照片時用
    public void AddPhotoItem(Texture2D photo)
    {
        Debug.Log("Adding photo to album UI");
        GameObject item = Instantiate(photoItemPrefab, content);
        item.GetComponent<RawImage>().texture = photo;
    }
}

