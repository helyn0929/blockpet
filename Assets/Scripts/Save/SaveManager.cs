using UnityEngine;
using System.IO;

public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance;

    public SaveData data = new SaveData();

    string jsonPath;
    string photoFolder;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        jsonPath = Path.Combine(Application.persistentDataPath, "save.json");
        photoFolder = Path.Combine(Application.persistentDataPath, "photos");

        if (!Directory.Exists(photoFolder))
            Directory.CreateDirectory(photoFolder);

        Load();
    }

    public void SavePhoto(Texture2D photo)
    {
        // 1️⃣ 存 PNG
        string fileName = System.Guid.NewGuid() + ".png";
        string fullPath = Path.Combine(photoFolder, fileName);

        byte[] bytes = photo.EncodeToPNG();
        File.WriteAllBytes(fullPath, bytes);

        // 2️⃣ 記錄 meta
        PhotoMeta meta = new PhotoMeta(fileName);
        data.photos.Add(meta);

        // 3️⃣ 存 JSON
        Save();
    }

    void Save()
    {
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(jsonPath, json);
    }

    void Load()
    {
        if (!File.Exists(jsonPath))
            return;

        string json = File.ReadAllText(jsonPath);
        data = JsonUtility.FromJson<SaveData>(json);
    }

    public Texture2D LoadPhoto(PhotoMeta meta)
    {
        string path = Path.Combine(photoFolder, meta.fileName);
        if (!File.Exists(path)) return null;

        byte[] bytes = File.ReadAllBytes(path);
        Texture2D tex = new Texture2D(2, 2);
        tex.LoadImage(bytes);
        return tex;
    }
}
