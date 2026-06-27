using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System;

[DefaultExecutionOrder(-100)]
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance;

    public static Action OnSaveDataChanged;
    public static Action OnPhotoSaved;
    public static Action<PhotoMeta> OnPhotoSavedMeta;
    /// <summary>Fired just before the room switches so subscribers can persist their current state.</summary>
    public static Action OnBeforeRoomSwitch;
    /// <summary>Fired after the room data has loaded so subscribers can reload their state.</summary>
    public static Action OnRoomSwitched;

    [Header("Runtime Data")]
    public SaveData data = new SaveData();

    GlobalSaveData _globalData = new GlobalSaveData();
    string _currentRoomId = "";
    string _globalJsonPath;
    string _roomJsonPath;
    string _photoFolder;

    public PhotoMeta LastSavedPhotoMeta { get; private set; }
    public string CurrentRoomId => _currentRoomId;

    // Avatar lives in the global save (not per-room)
    public string AvatarFileName
    {
        get => _globalData.avatarFileName;
        set { _globalData.avatarFileName = value; SaveGlobal(); }
    }

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        if (transform.parent != null) transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        _globalJsonPath = Path.Combine(Application.persistentDataPath, "save_global.json");
        _photoFolder = Path.Combine(Application.persistentDataPath, "photos");
        if (!Directory.Exists(_photoFolder)) Directory.CreateDirectory(_photoFolder);

        LoadGlobal();
        data = new SaveData();
        data.photos = new System.Collections.Generic.List<PhotoMeta>();
    }

    // ─── Room switching ───────────────────────────────────────────────

    public void SwitchRoom(string roomId)
    {
        roomId = roomId?.Trim() ?? "";
        if (_currentRoomId == roomId) return;

        OnBeforeRoomSwitch?.Invoke();
        SaveRoomData();
        LoadRoomData(roomId);
        OnRoomSwitched?.Invoke();
        OnSaveDataChanged?.Invoke();
    }

    // ─── Global save (avatar) ─────────────────────────────────────────

    void LoadGlobal()
    {
        if (File.Exists(_globalJsonPath))
        {
            try { _globalData = JsonUtility.FromJson<GlobalSaveData>(File.ReadAllText(_globalJsonPath)) ?? new GlobalSaveData(); }
            catch { _globalData = new GlobalSaveData(); }
        }
        else
        {
            // Migrate avatar from old monolithic save.json if it exists
            string oldPath = Path.Combine(Application.persistentDataPath, "save.json");
            if (File.Exists(oldPath))
            {
                try
                {
                    var old = JsonUtility.FromJson<SaveData>(File.ReadAllText(oldPath));
                    // SaveData no longer has avatarFileName; migration is a no-op for new builds
                    _ = old;
                }
                catch { }
            }
            _globalData = new GlobalSaveData();
        }
    }

    void SaveGlobal()
    {
        try { File.WriteAllText(_globalJsonPath, JsonUtility.ToJson(_globalData, true)); }
        catch (Exception e) { Debug.LogError("[SaveManager] SaveGlobal failed: " + e.Message); }
    }

    // ─── Per-room save ────────────────────────────────────────────────

    void LoadRoomData(string roomId)
    {
        _currentRoomId = roomId;
        string safe = roomId.Replace("/", "_").Replace("\\", "_").Replace("..", "_");
        _roomJsonPath = Path.Combine(Application.persistentDataPath, $"save_{safe}.json");

        if (File.Exists(_roomJsonPath))
        {
            try
            {
                data = JsonUtility.FromJson<SaveData>(File.ReadAllText(_roomJsonPath)) ?? new SaveData();
                if (data.photos == null) data.photos = new List<PhotoMeta>();
                Debug.Log($"[SaveManager] Loaded room '{roomId}', photos: {data.photos.Count}");
            }
            catch (Exception e)
            {
                Debug.LogError("[SaveManager] LoadRoomData failed: " + e.Message);
                data = new SaveData();
                data.photos = new List<PhotoMeta>();
            }
        }
        else
        {
            data = new SaveData();
            data.photos = new List<PhotoMeta>();
            Debug.Log($"[SaveManager] New save for room '{roomId}'");
        }

    }

    void SaveRoomData()
    {
        if (string.IsNullOrEmpty(_roomJsonPath)) return;
        try { File.WriteAllText(_roomJsonPath, JsonUtility.ToJson(data, true)); }
        catch (Exception e) { Debug.LogError("[SaveManager] SaveRoomData failed: " + e.Message); }
    }

    public void Save() => SaveRoomData();

    // ─── Photo ───────────────────────────────────────────────────────

    public void SavePhoto(Texture2D photo)
    {
        if (photo == null) { Debug.LogWarning("[SaveManager] SavePhoto called with null photo"); return; }
        try
        {
            if (data.photos == null) data.photos = new List<PhotoMeta>();
            string fileName = Guid.NewGuid().ToString() + ".png";
            string fullPath = Path.Combine(_photoFolder, fileName);
            File.WriteAllBytes(fullPath, photo.EncodeToPNG());

            PhotoMeta meta = new PhotoMeta(fileName, _globalData.avatarFileName);
            data.photos.Add(meta);
            LastSavedPhotoMeta = meta;
            Save();

            Debug.Log($"[SaveManager] Photo saved: {fileName}, room: {_currentRoomId}, total: {data.photos.Count}");
            OnPhotoSaved?.Invoke();
            OnPhotoSavedMeta?.Invoke(meta);
            OnSaveDataChanged?.Invoke();
        }
        catch (Exception e) { Debug.LogError("[SaveManager] SavePhoto failed: " + e.Message); }
    }

    public bool ImportPhotoFromCloud(PhotoMeta meta, byte[] imageBytes)
    {
        if (meta == null || string.IsNullOrEmpty(meta.fileName) || imageBytes == null || imageBytes.Length == 0)
            return false;
        try
        {
            if (data.photos == null) data.photos = new List<PhotoMeta>();
            if (data.photos.Exists(p => p != null && p.fileName == meta.fileName)) return false;

            string fullPath = Path.Combine(_photoFolder, meta.fileName);
            if (!Directory.Exists(_photoFolder)) Directory.CreateDirectory(_photoFolder);
            File.WriteAllBytes(fullPath, imageBytes);
            data.photos.Add(meta);
            Save();

            Debug.Log("[SaveManager] Imported cloud photo: " + meta.fileName);
            OnSaveDataChanged?.Invoke();
            return true;
        }
        catch (Exception e) { Debug.LogError("[SaveManager] ImportPhotoFromCloud failed: " + e.Message); return false; }
    }

    public Texture2D LoadPhoto(PhotoMeta meta)
    {
        if (meta == null || string.IsNullOrEmpty(meta.fileName)) return null;
        string path = Path.Combine(_photoFolder, meta.fileName);
        if (!File.Exists(path)) { Debug.LogWarning("[SaveManager] Photo file missing: " + meta.fileName); return null; }
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2);
            tex.LoadImage(bytes);
            return tex;
        }
        catch (Exception e) { Debug.LogError("[SaveManager] LoadPhoto failed: " + e.Message); return null; }
    }

    /// <summary>Returns the latest photo texture for any room without switching the active room.</summary>
    public Texture2D GetLatestPhotoTextureForRoom(string roomId)
    {
        if (string.IsNullOrEmpty(roomId)) return null;

        // Use already-loaded data for the current room
        if (roomId == _currentRoomId)
        {
            if (data?.photos == null || data.photos.Count == 0) return null;
            return LoadPhoto(data.photos[data.photos.Count - 1]);
        }

        // Read the save file for a different room without touching global state
        string safe = roomId.Replace("/", "_").Replace("\\", "_").Replace("..", "_");
        string jsonPath = Path.Combine(Application.persistentDataPath, $"save_{safe}.json");
        if (!File.Exists(jsonPath)) return null;
        try
        {
            var sd = JsonUtility.FromJson<SaveData>(File.ReadAllText(jsonPath));
            if (sd?.photos == null || sd.photos.Count == 0) return null;
            return LoadPhoto(sd.photos[sd.photos.Count - 1]);
        }
        catch { return null; }
    }
}
