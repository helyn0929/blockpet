using System;
using System.Collections.Generic;

[Serializable]
public class SaveData
{
    public List<PhotoMeta> photos = new List<PhotoMeta>();

    //for petlife
    public float currentHealth = 86400f; //24 hours in seconds
    public string lastUpdateTime; 

    public string lastCaptureTime;

    /// <summary>File name of the user's avatar image (stored in persistentDataPath/avatars/).</summary>
    public string avatarFileName;
}

//json's root