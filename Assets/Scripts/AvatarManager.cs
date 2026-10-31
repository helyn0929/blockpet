using UnityEngine;
using System;
using System.IO;
using System.Collections;

/// <summary>
/// Manages user avatar: pick from gallery, save locally, load on login.
/// Avatar file is stored under Application.persistentDataPath/avatars/.
/// File name is recorded in SaveData.avatarFileName so it persists with the save.
/// </summary>
[DefaultExecutionOrder(-90)]
public class AvatarManager : MonoBehaviour
{
    public static AvatarManager Instance;

    public static event Action OnAvatarChanged;

    /// <summary>Current avatar texture (null = use default placeholder).</summary>
    public Texture2D CurrentAvatar { get; private set; }

    string avatarFolder;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        if (transform.parent != null)
            transform.SetParent(null);

        Instance = this;
        DontDestroyOnLoad(gameObject);

        avatarFolder = Path.Combine(Application.persistentDataPath, "avatars");
        if (!Directory.Exists(avatarFolder))
            Directory.CreateDirectory(avatarFolder);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ================================================================
    //  Load existing avatar from disk (call after login / save loaded)
    // ================================================================

    public void LoadAvatarFromSave()
    {
        CurrentAvatar = null;

        if (SaveManager.Instance == null || SaveManager.Instance.data == null)
        {
            Debug.Log("[AvatarManager] LoadAvatarFromSave: SaveManager not ready, no avatar to load.");
            OnAvatarChanged?.Invoke();
            return;
        }

        string fileName = SaveManager.Instance.AvatarFileName;
        if (string.IsNullOrEmpty(fileName))
        {
            Debug.Log("[AvatarManager] No local avatarFileName — trying Firebase backup.");
            TryRestoreFromFirebase();
            return;
        }

        string path = Path.Combine(avatarFolder, fileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning("[AvatarManager] Local avatar missing — trying Firebase backup.");
            TryRestoreFromFirebase();
            return;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2);
            if (tex.LoadImage(bytes))
            {
                CurrentAvatar = tex;
                SyncThumbOnce(tex);
            }
            else
            {
                Destroy(tex);
                CurrentAvatar = null;
                Debug.LogWarning("[AvatarManager] Avatar file could not be decoded, falling back to default.");
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[AvatarManager] Failed to load avatar: " + e.Message);
            CurrentAvatar = null;
        }

        OnAvatarChanged?.Invoke();
    }

    // ================================================================
    //  Restore avatar from Firebase backup (after reinstall)
    // ================================================================

    void TryRestoreFromFirebase()
    {
        if (FirebaseManager.Instance == null) { OnAvatarChanged?.Invoke(); return; }
        FirebaseManager.Instance.DownloadAvatarBase64(b64 =>
        {
            if (string.IsNullOrEmpty(b64)) { OnAvatarChanged?.Invoke(); return; }
            try
            {
                byte[] bytes = Convert.FromBase64String(b64);
                Texture2D tex = new Texture2D(2, 2);
                if (tex.LoadImage(bytes))
                {
                    // Save locally so future loads skip Firebase.
                    string fileName = "avatar_" + Guid.NewGuid().ToString("N") + ".png";
                    File.WriteAllBytes(Path.Combine(avatarFolder, fileName), bytes);
                    if (SaveManager.Instance != null)
                        SaveManager.Instance.AvatarFileName = fileName;
                    CurrentAvatar = tex;
                    Debug.Log("[AvatarManager] Avatar restored from Firebase.");
                }
                else { Destroy(tex); }
            }
            catch (Exception e) { Debug.LogWarning("[AvatarManager] Firebase avatar restore failed: " + e.Message); }
            OnAvatarChanged?.Invoke();
        });
    }

    // ================================================================
    //  Save a new avatar (from Texture2D, e.g. after gallery pick)
    // ================================================================

    public void SetAvatar(Texture2D texture)
    {
        if (texture == null)
        {
            Debug.LogWarning("[AvatarManager] SetAvatar called with null texture.");
            return;
        }

        try
        {
            string fileName = "avatar_" + Guid.NewGuid().ToString("N") + ".png";
            string fullPath = Path.Combine(avatarFolder, fileName);

            byte[] bytes = texture.EncodeToPNG();
            File.WriteAllBytes(fullPath, bytes);

            DeleteOldAvatar();

            if (SaveManager.Instance != null)
            {
                SaveManager.Instance.AvatarFileName = fileName;
            }

            CurrentAvatar = texture;
            Debug.Log($"[AvatarManager] Avatar saved: {fileName}, CurrentAvatar set ({texture.width}x{texture.height})");
            OnAvatarChanged?.Invoke();

            // Back up to Firebase so avatar survives reinstall.
            if (FirebaseManager.Instance != null)
                FirebaseManager.Instance.UploadAvatarBase64(Convert.ToBase64String(bytes));
            // 其他成員看的小縮圖（相簿、聊天室、成員列表的圓形頭像）
            UploadThumb(texture);
            _thumbSynced = true;
        }
        catch (Exception e)
        {
            Debug.LogError("[AvatarManager] SetAvatar failed: " + e.Message);
        }
    }

    // ================================================================
    //  Clear avatar (e.g. on logout)
    // ================================================================

    public void ClearAvatar()
    {
        CurrentAvatar = null;
        OnAvatarChanged?.Invoke();
    }

    // ================================================================
    //  Pick image from device gallery (iOS / Android)
    // ================================================================

    /// <summary>
    /// Opens the native photo picker. On iOS this requires NSPhotoLibraryUsageDescription
    /// in Info.plist. Uses NativeGallery plugin if available; otherwise falls back to a
    /// stub that logs a warning.
    /// </summary>
    public void PickAvatarFromGallery()
    {
        PickAvatarFromGallery(null);
    }

    /// <summary>
    /// Opens the gallery picker with an optional completion callback.
    /// Callback receives true if an avatar was selected, false if cancelled/failed.
    /// </summary>
    public void PickAvatarFromGallery(Action<bool> onComplete)
    {
#if (UNITY_IOS || UNITY_ANDROID) && NATIVE_GALLERY
        PickImageNative(onComplete);
#else
        Debug.LogWarning("[AvatarManager] Gallery pick requires the NativeGallery plugin and the NATIVE_GALLERY scripting define (iOS/Android only).");
        onComplete?.Invoke(false);
#endif
    }

    /// <summary>True if no avatar has been saved yet (first-time user).</summary>
    public bool HasAvatar
    {
        get
        {
            if (SaveManager.Instance == null) return false;
            return !string.IsNullOrEmpty(SaveManager.Instance.AvatarFileName);
        }
    }

#if (UNITY_IOS || UNITY_ANDROID) && NATIVE_GALLERY
    Action<bool> pendingPickCallback;

    void PickImageNative(Action<bool> onComplete)
    {
        pendingPickCallback = onComplete;

        NativeGallery.GetImageFromGallery((path) =>
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.Log("[AvatarManager] Gallery pick cancelled.");
                pendingPickCallback?.Invoke(false);
                pendingPickCallback = null;
                return;
            }

            StartCoroutine(LoadPickedImage(path));
        }, "Select Avatar");
    }

    IEnumerator LoadPickedImage(string path)
    {
        yield return null; // give UI a frame

        bool success = false;
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2);
            if (tex.LoadImage(bytes))
            {
                Texture2D resized = ResizeToFit(tex, 512);
                SetAvatar(resized);
                if (resized != tex) Destroy(tex);
                success = true;
            }
            else
            {
                Debug.LogError("[AvatarManager] Failed to decode picked image.");
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[AvatarManager] LoadPickedImage failed: " + e.Message);
        }

        pendingPickCallback?.Invoke(success);
        pendingPickCallback = null;
    }
#endif

    // ================================================================
    //  Helpers
    // ================================================================

    void DeleteOldAvatar()
    {
        if (SaveManager.Instance == null) return;
        string old = SaveManager.Instance.AvatarFileName;
        if (string.IsNullOrEmpty(old)) return;

        string oldPath = Path.Combine(avatarFolder, old);
        try { if (File.Exists(oldPath)) File.Delete(oldPath); }
        catch (Exception e) { Debug.LogWarning("[AvatarManager] Could not delete old avatar: " + e.Message); }
    }

    // ================================================================
    //  其他成員看的小縮圖：置中裁成正方形 128px JPG，存在 Users/{uid}/avatarThumb
    // ================================================================

    const int ThumbSize = 128;
    static bool _thumbSynced;

    /// <summary>登入後補傳一次（之前設的頭貼沒有縮圖，其他成員才看得到）。</summary>
    void SyncThumbOnce(Texture2D tex)
    {
        if (_thumbSynced || tex == null) return;
        _thumbSynced = true;
        UploadThumb(tex);
    }

    static void UploadThumb(Texture2D tex)
    {
        if (FirebaseManager.Instance == null || tex == null) return;
        try
        {
            var bytes = MakeThumb(tex);
            if (bytes != null) FirebaseManager.Instance.UploadAvatarThumb(Convert.ToBase64String(bytes));
        }
        catch (Exception e) { Debug.LogWarning("[AvatarManager] Avatar thumbnail failed: " + e.Message); }
    }

    static byte[] MakeThumb(Texture2D source)
    {
        int side = Mathf.Min(source.width, source.height);
        if (side <= 0) return null;
        var scale = new Vector2((float)side / source.width, (float)side / source.height);
        var offset = new Vector2((1f - scale.x) / 2f, (1f - scale.y) / 2f);
        var rt = RenderTexture.GetTemporary(ThumbSize, ThumbSize);
        Graphics.Blit(source, rt, scale, offset);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var thumb = new Texture2D(ThumbSize, ThumbSize, TextureFormat.RGB24, false);
        thumb.ReadPixels(new Rect(0, 0, ThumbSize, ThumbSize), 0, 0);
        thumb.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var bytes = thumb.EncodeToJPG(80);
        Destroy(thumb);
        return bytes;
    }

    /// <summary>Down-scale to maxSize on longest edge while preserving aspect ratio.</summary>
    static Texture2D ResizeToFit(Texture2D source, int maxSize)
    {
        int w = source.width;
        int h = source.height;
        if (w <= maxSize && h <= maxSize) return source;

        float ratio = Mathf.Min((float)maxSize / w, (float)maxSize / h);
        int nw = Mathf.Max(1, Mathf.RoundToInt(w * ratio));
        int nh = Mathf.Max(1, Mathf.RoundToInt(h * ratio));

        RenderTexture rt = RenderTexture.GetTemporary(nw, nh);
        Graphics.Blit(source, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D result = new Texture2D(nw, nh, TextureFormat.RGBA32, false);
        result.ReadPixels(new Rect(0, 0, nw, nh), 0, 0);
        result.Apply();

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return result;
    }
}
