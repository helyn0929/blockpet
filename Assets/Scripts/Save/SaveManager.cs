using UnityEngine;
using System.IO;
using System.Collections.Generic;

public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance;

    public static System.Action OnSaveDataChanged;

    [Header("Runtime Data")]
    public SaveData data = new SaveData();

    string jsonPath;
    string photoFolder;

    void Awake()
    {
        // Singleton
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
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

            // Record meta
            PhotoMeta meta = new PhotoMeta(fileName);
            data.photos.Add(meta);

            Save();

            Debug.Log("[SaveManager] Photo saved: " + fileName);
            Debug.Log("[SaveManager] Total photos: " + data.photos.Count);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[SaveManager] SavePhoto failed: " + e.Message);
        }
    }

    void Save()
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
