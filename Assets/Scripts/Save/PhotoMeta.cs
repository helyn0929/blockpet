using System;

[Serializable]
public class PhotoMeta
{
    public string fileName;
    public string timestamp;
    /// <summary>Avatar file name of the user who took this photo (stored in avatars/ folder).</summary>
    public string takerAvatarFileName;

    public PhotoMeta(string fileName)
    {
        this.fileName = fileName;
        this.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    public PhotoMeta(string fileName, string takerAvatar)
    {
        this.fileName = fileName;
        this.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        this.takerAvatarFileName = takerAvatar;
    }
}