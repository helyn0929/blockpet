using System;

[Serializable]
public class PhotoMeta
{
    public string fileName;
    public string timestamp;
    /// <summary>Optional user caption; shown on photo detail when set.</summary>
    public string caption;
    /// <summary>Avatar file name of the user who took this photo (stored in avatars/ folder).</summary>
    public string takerAvatarFileName;
    /// <summary>誰拍的（Firebase uid／暱稱）；舊照片沒有。相簿的「誰拍的」篩選用。</summary>
    public string takerUserId;
    public string takerName;
    /// <summary>照片辨識的類別（美食／自然／城市／生活人物／寵物）與信心；舊照片沒有。</summary>
    public string category;
    public float categoryConfidence;
    /// <summary>辨識時的類別版本（photo_categories.json 的 version）；和目前版本不同時會重新辨識。</summary>
    public string categoryVersion;

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