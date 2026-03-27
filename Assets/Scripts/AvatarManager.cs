using UnityEngine;
using System;
using System.IO;
using System.Collections;

/// <summary>
/// Manages user avatar: pick from gallery, save locally, load on login.
/// Avatar file is stored under Application.persistentDataPath/avatars/.
/// File name is recorded in SaveData.avatarFileName so it persists with the save.
/// </summary>
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

        string fileName = SaveManager.Instance.data.avatarFileName;
        if (string.IsNullOrEmpty(fileName))
        {
            Debug.Log("[AvatarManager] LoadAvatarFromSave: No avatarFileName in SaveData (first-time user).");
            OnAvatarChanged?.Invoke();
            return;
        }

        string path = Path.Combine(avatarFolder, fileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning("[AvatarManager] Saved avatar file missing, resetting to default.");
            SaveManager.Instance.data.avatarFileName = null;
            SaveManager.Instance.Save();
            OnAvatarChanged?.Invoke();
            return;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2);
            tex.LoadImage(bytes);
            CurrentAvatar = tex;
        }
        catch (Exception e)
        {
            Debug.LogError("[AvatarManager] Failed to load avatar: " + e.Message);
            CurrentAvatar = null;
        }

        OnAvatarChanged?.Invoke();
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

            if (SaveManager.Instance != null && SaveManager.Instance.data != null)
            {
                SaveManager.Instance.data.avatarFileName = fileName;
                SaveManager.Instance.Save();
            }

            CurrentAvatar = texture;
            Debug.Log($"[AvatarManager] Avatar saved: {fileName}, CurrentAvatar set ({texture.width}x{texture.height})");
            OnAvatarChanged?.Invoke();
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
            if (SaveManager.Instance == null || SaveManager.Instance.data == null) return false;
            return !string.IsNullOrEmpty(SaveManager.Instance.data.avatarFileName);
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
        if (SaveManager.Instance == null || SaveManager.Instance.data == null) return;
        string old = SaveManager.Instance.data.avatarFileName;
        if (string.IsNullOrEmpty(old)) return;

        string oldPath = Path.Combine(avatarFolder, old);
        try { if (File.Exists(oldPath)) File.Delete(oldPath); }
        catch (Exception e) { Debug.LogWarning("[AvatarManager] Could not delete old avatar: " + e.Message); }
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
