using System;

[Serializable]
public class PhotoMeta
{
    public string fileName;
    public string timestamp;

    public PhotoMeta(string fileName)
    {
        this.fileName = fileName;
        this.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }
}

// save photos in json as notes