using UnityEngine;

[System.Serializable]
public class PhotoData
{
    public Texture2D photo;
    public string timestamp;

    public PhotoData(Texture2D photo)
    {
        this.photo = photo;
        this.timestamp = System.DateTime.Now.ToString("HH:mm:ss");
    }
}