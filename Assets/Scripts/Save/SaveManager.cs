using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System;

[DefaultExecutionOrder(-100)]
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance;

    public static System.Action OnSaveDataChanged;
    /// <summary>Fired when a photo is saved successfully (for economy, etc.).</summary>
    public static System.Action OnPhotoSaved;
    /// <summary>Fired when a photo is saved with its metadata (for cloud sync, etc.).</summary>
    public static System.Action<PhotoMeta> OnPhotoSavedMeta;

    [Header("Runtime Data")]
    public SaveData data = new SaveData();

    string jsonPath;
    string photoFolder;

    /// <summary>The last photo meta saved by <see cref="SavePhoto"/> in this session.</summary>
    public PhotoMeta LastSavedPhotoMeta { get; private set; }

    void Awake()
    {
        // Singleton
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (transform.parent != null)
            transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        // Paths
        jsonPath = Path.Combine(Application.persistentDataPath, "save.json");
        photoFolder = Path.Combine(Application.persistentDataPath, "photos");

        // Ensure folder exists
        if (!Directory.Exists(photoFolder))
            Directory.CreateDirectory(photoFolder);

        Load();
    }

    // =========================
    // Save
    // =========================
    public void SavePhoto(Texture2D photo)
    {
        if (photo == null)
        {
            Debug.LogWarning("[SaveManager] SavePhoto called with null photo");
            return;
        }

        try
        {
            // Ensure list exists
            if (data.photos == null)
                data.photos = new List<PhotoMeta>();

            // Save PNG
            string fileName = System.Guid.NewGuid().ToString() + ".png";
            string fullPath = Path.Combine(photoFolder, fileName);

            byte[] bytes = photo.EncodeToPNG();
            File.WriteAllBytes(fullPath, bytes);

            // Record meta with the current user's avatar
            string takerAvatar = data.avatarFileName;
            PhotoMeta meta = new PhotoMeta(fileName, takerAvatar);
            data.photos.Add(meta);
            LastSavedPhotoMeta = meta;

            Save();

            Debug.Log("[SaveManager] Photo saved: " + fileName);
            Debug.Log("[SaveManager] Total photos: " + data.photos.Count);

            OnPhotoSaved?.Invoke();
            OnPhotoSavedMeta?.Invoke(meta);
            OnSaveDataChanged?.Invoke();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[SaveManager] SavePhoto failed: " + e.Message);
        }
    }

    /// <summary>
    /// Imports a photo received from cloud sync into local storage + save data.
    /// Dedupe is by <see cref="PhotoMeta.fileName"/>.
    /// </summary>
    public bool ImportPhotoFromCloud(PhotoMeta meta, byte[] imageBytes)
    {
        if (meta == null || string.IsNullOrEmpty(meta.fileName) || imageBytes == null || imageBytes.Length == 0)
            return false;

        try
        {
            if (data.photos == null)
                data.photos = new List<PhotoMeta>();

            // Already have it.
            if (data.photos.Exists(p => p != null && p.fileName == meta.fileName))
                return false;

            string fullPath = Path.Combine(photoFolder, meta.fileName);
            if (!Directory.Exists(photoFolder))
                Directory.CreateDirectory(photoFolder);

            File.WriteAllBytes(fullPath, imageBytes);
            data.photos.Add(meta);
            Save();

            Debug.Log("[SaveManager] Imported cloud photo: " + meta.fileName);
            OnSaveDataChanged?.Invoke();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[SaveManager] ImportPhotoFromCloud failed: " + e.Message);
            return false;
        }
    }

    public void Save()
    {
        try
        {
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(jsonPath, json);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[SaveManager] Save JSON failed: " + e.Message);
        }
    }

    // =========================
    // Load
    // =========================
    void Load()
    {
        // No save file yet → create clean data
        if (!File.Exists(jsonPath))
        {
            data = new SaveData();
            data.photos = new List<PhotoMeta>();
            Debug.Log("[SaveManager] No save.json found, create new data");
            return;
        }

        try
        {
            string json = File.ReadAllText(jsonPath);
            data = JsonUtility.FromJson<SaveData>(json);

            if (data == null)
                data = new SaveData();

            if (data.photos == null)
                data.photos = new List<PhotoMeta>();

            Debug.Log("[SaveManager] Save loaded, photos: " + data.photos.Count);

            OnSaveDataChanged?.Invoke();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[SaveManager] Load failed: " + e.Message);
            data = new SaveData();
            data.photos = new List<PhotoMeta>();
        }
    }

    // =========================
    // Load Photo
    // =========================
    public Texture2D LoadPhoto(PhotoMeta meta)
    {
        if (meta == null || string.IsNullOrEmpty(meta.fileName))
            return null;

        string path = Path.Combine(photoFolder, meta.fileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning("[SaveManager] Photo file missing: " + meta.fileName);
            return null;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2);
            tex.LoadImage(bytes);
            return tex;
        }
        catch (System.Exception e)
        {
            Debug.LogError("[SaveManager] LoadPhoto failed: " + e.Message);
            return null;
        }
    }
}
