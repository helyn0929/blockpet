using System.Collections.Generic;
using UnityEngine;

public class AlbumManager : MonoBehaviour
{
    public static AlbumManager Instance;

    public List<PhotoData> photos = new List<PhotoData>();

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    public void AddPhoto(Texture2D photo)
    {
        PhotoData data = new PhotoData(photo);
        photos.Add(data);

        Debug.Log("Photo added to album. Total: " + photos.Count);
    }
}

