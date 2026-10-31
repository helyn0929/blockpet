using System;
using System.Collections.Generic;
using BlockPet.Hand;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 成員頭貼快取：自己的用 AvatarManager（手機裡的那張，換頭貼馬上更新）；
/// 其他成員的從 Firebase 讀小縮圖（Users/{uid}/avatarThumb，舊資料退回 avatarBase64），每次開 App 讀一次、存在記憶體。
/// 沒有頭貼的成員記下來，這次開 App 不再重查。
/// </summary>
public static class AvatarCache
{
    const int MaxSize = 128;

    static readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
    static readonly HashSet<string> _loading = new HashSet<string>();
    static readonly HashSet<string> _none = new HashSet<string>();
    static bool _hooked;

    /// <summary>某個成員的頭貼讀好了／換了（uid）。</summary>
    public static event Action<string> OnChanged;

    static string MyId => FirebaseManager.Instance != null ? FirebaseManager.Instance.GetUserId() : null;

    /// <summary>成員的頭貼；還沒讀到或沒有頭貼回傳 null（讀到時會發 OnChanged）。</summary>
    public static Texture2D Get(string uid)
    {
        Hook();
        if (string.IsNullOrEmpty(uid)) return null;
        if (uid == MyId) return AvatarManager.Instance != null ? AvatarManager.Instance.CurrentAvatar : null;
        if (_textures.TryGetValue(uid, out var tex)) return tex;
        if (_none.Contains(uid) || _loading.Contains(uid)) return null;

        var fb = FirebaseManager.Instance;
        if (fb == null) return null;
        _loading.Add(uid);
        fb.DownloadUserAvatar(uid, b64 =>
        {
            _loading.Remove(uid);
            var t = Decode(b64);
            if (t == null) { _none.Add(uid); return; }
            _textures[uid] = t;
            OnChanged?.Invoke(uid);
        });
        return null;
    }

    static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        AvatarManager.OnAvatarChanged += () => { var me = MyId; if (!string.IsNullOrEmpty(me)) OnChanged?.Invoke(me); };
        // 換空間（可能換帳號）時清掉，下次重讀
        FirebaseManager.OnRoomChanged += () => { _none.Clear(); };
    }

    static Texture2D Decode(string b64)
    {
        if (string.IsNullOrEmpty(b64)) return null;
        try
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(Convert.FromBase64String(b64))) { UnityEngine.Object.Destroy(tex); return null; }
            if (tex.width <= MaxSize && tex.height <= MaxSize) return tex;
            // 舊的 512px 備份：縮小再存，省記憶體
            var small = Downscale(tex, MaxSize);
            UnityEngine.Object.Destroy(tex);
            return small;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[AvatarCache] decode failed: " + e.Message);
            return null;
        }
    }

    static Texture2D Downscale(Texture2D src, int max)
    {
        float r = Mathf.Min((float)max / src.width, (float)max / src.height);
        int w = Mathf.Max(1, Mathf.RoundToInt(src.width * r)), h = Mathf.Max(1, Mathf.RoundToInt(src.height * r));
        var rt = RenderTexture.GetTemporary(w, h);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var dst = new Texture2D(w, h, TextureFormat.RGB24, false);
        dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        dst.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return dst;
    }
}

/// <summary>
/// 圓形頭像蓋上成員的頭貼照片：掛在原本「顏色＋首字」的圓上，有頭貼時整個圓被照片蓋住（裁成圓形），
/// 沒有頭貼時隱藏，維持顏色＋首字。頭貼讀到或換了會自動更新。
/// 用法：建好圓和首字之後呼叫 AvatarPhoto.Attach(圓的 RectTransform, uid)。
/// </summary>
public class AvatarPhoto : MonoBehaviour
{
    string _uid;
    RawImage _img;
    GameObject _mask;

    /// <summary>
    /// 在 circle 上面蓋一層圓形照片：放在 circle 的同一層、跟 circle 一樣的位置大小，排在目前最上面，
    /// 所以要在首字（不管是 circle 的子物件或後面的兄弟物件）都建好之後再呼叫。
    /// </summary>
    public static AvatarPhoto Attach(RectTransform circle, string uid)
    {
        if (circle == null || circle.parent == null) return null;
        var maskRt = new GameObject("AvatarPhoto", typeof(RectTransform)).GetComponent<RectTransform>();
        maskRt.SetParent(circle.parent, false);
        maskRt.anchorMin = circle.anchorMin;
        maskRt.anchorMax = circle.anchorMax;
        maskRt.pivot = circle.pivot;
        maskRt.anchoredPosition = circle.anchoredPosition;
        maskRt.sizeDelta = circle.sizeDelta;
        maskRt.localRotation = circle.localRotation;
        maskRt.localScale = circle.localScale;
        maskRt.SetAsLastSibling();
        float radius = Mathf.Min(circle.sizeDelta.x, circle.sizeDelta.y) / 2f;
        var maskImg = UIShapes.AddRounded(maskRt.gameObject, Color.white, Mathf.Max(1f, radius));
        maskImg.raycastTarget = false;
        maskRt.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        var imgRt = new GameObject("Photo", typeof(RectTransform)).GetComponent<RectTransform>();
        imgRt.SetParent(maskRt, false);
        imgRt.anchorMin = Vector2.zero;
        imgRt.anchorMax = Vector2.one;
        imgRt.offsetMin = imgRt.offsetMax = Vector2.zero;
        var img = imgRt.gameObject.AddComponent<RawImage>();
        img.raycastTarget = false;

        var c = maskRt.gameObject.AddComponent<AvatarPhoto>();
        c._img = img;
        c._mask = maskRt.gameObject;
        c.SetUser(uid);
        return c;
    }

    public void SetUser(string uid)
    {
        _uid = uid;
        Apply();
    }

    void OnEnable()
    {
        AvatarCache.OnChanged += OnChanged;
        Apply();
    }

    void OnDisable() => AvatarCache.OnChanged -= OnChanged;

    void OnChanged(string uid)
    {
        if (uid == _uid) Apply();
    }

    void Apply()
    {
        if (_img == null) return;
        var tex = AvatarCache.Get(_uid);
        _img.texture = tex;
        // 隱藏遮罩本身就好（AvatarPhoto 掛在遮罩上，關掉 Graphic 不會停掉 OnEnable／事件）
        _img.enabled = tex != null;
        var maskImg = _mask.GetComponent<Image>();
        if (maskImg != null) maskImg.enabled = tex != null;
        if (tex == null) return;
        // 置中裁成正方形
        float w = tex.width, h = tex.height;
        _img.uvRect = w > h ? new Rect((1f - h / w) / 2f, 0f, h / w, 1f) : new Rect(0f, (1f - w / h) / 2f, 1f, w / h);
    }
}
