using UnityEngine;
using UnityEngine.UI;

public class AlbumUI : MonoBehaviour
{
    public Transform content;
    public GameObject photoItemPrefab;

    void OnEnable()
    {
        ReloadFromSave();
    }

    void ReloadFromSave()
    {
        if (SaveManager.Instance == null) return;

        foreach (Transform c in content)
            Destroy(c.gameObject);

        foreach (var meta in SaveManager.Instance.data.photos)
        {
            Texture2D photo = SaveManager.Instance.LoadPhoto(meta);
            if (photo == null) continue;

            GameObject item = Instantiate(photoItemPrefab, content);
            RawImage ri = item.GetComponent<RawImage>();
            if (ri != null) ri.texture = photo;
        }

        Debug.Log("[AlbumUI] Reloaded from SaveManager");
    }
}

// 未來 Phase 3
// TMP_Text timeText = item.GetComponentInChildren<TMP_Text>();
// timeText.text = meta.timestamp.Substring(11, 5);
