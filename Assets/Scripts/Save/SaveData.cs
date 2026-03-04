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
}

//json's root