using System;
using System.Collections.Generic;

/// <summary>Per-room save data. One file per room: save_{roomId}.json</summary>
[Serializable]
public class SaveData
{
    public List<PhotoMeta> photos = new List<PhotoMeta>();

    public float currentHealth = 86400f;
    public string lastUpdateTime;
    public string lastCaptureTime;
}

/// <summary>Global (user-level) save data. One file: save_global.json</summary>
[Serializable]
public class GlobalSaveData
{
    public string avatarFileName;
}