using System;

/// <summary>
/// Digestion state for one placed fermentation jar decoration (keyed by its Firebase decorId).
/// Stored locally (per-room save file) — jar placement position is still synced through the
/// existing decoration system, but the uploaded photo and its countdown are per-device only.
/// </summary>
[Serializable]
public class FermentationJarState
{
    /// <summary>Matches DecorationObject.DecorId of the jar holding this photo.</summary>
    public string decorId;
    /// <summary>File name inside the pending-photos folder (persistentDataPath/ferment_pending/).</summary>
    public string pendingPhotoFileName;
    /// <summary>DateTime.Now.ToString() at the moment the photo was placed in the jar.</summary>
    public string digestStartTime;

    public FermentationJarState() { }

    public FermentationJarState(string decorId, string pendingPhotoFileName)
    {
        this.decorId = decorId;
        this.pendingPhotoFileName = pendingPhotoFileName;
        this.digestStartTime = DateTime.Now.ToString();
    }
}
