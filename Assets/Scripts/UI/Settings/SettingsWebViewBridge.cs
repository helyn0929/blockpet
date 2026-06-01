using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Hosts the Settings screen in a WebView (?screen=settings).
/// Receives actions from React and calls AvatarManager / FirebaseManager.
/// </summary>
public class SettingsWebViewBridge : MonoBehaviour
{
    [SerializeField] PageManager pageManager;
    [SerializeField] RectTransform webLayoutTarget;
    [SerializeField] string streamingAssetsRelativePath = "game-ui/index.html";
    [SerializeField] bool updateMarginsEachFrame = true;

    WebViewObject _webView;
    bool _pageReady;
    bool _shouldBeVisible;
    bool _initStarted;

    void Awake()
    {
        if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
        if (webLayoutTarget == null) webLayoutTarget = transform as RectTransform;
    }

    void OnEnable()
    {
        _shouldBeVisible = true;
        if (_webView != null)
        {
            _webView.SetVisibility(true);
            // Refresh data every time the settings page is opened.
            if (_pageReady) SendSettingsInit();
        }

#if !UNITY_EDITOR
        if (!_initStarted)
        {
            _initStarted = true;
            StartCoroutine(InitWebViewCoroutine());
        }
#endif
    }

    void OnDisable()
    {
        _shouldBeVisible = false;
        if (_webView != null) _webView.SetVisibility(false);
    }

    void OnDestroy()
    {
        if (_webView != null) { Destroy(_webView.gameObject); _webView = null; }
    }

    void LateUpdate()
    {
        if (_webView != null && updateMarginsEachFrame && webLayoutTarget != null)
            ApplyMarginsFromRect(webLayoutTarget);
    }

    IEnumerator InitWebViewCoroutine()
    {
        var go = new GameObject("SettingsWebViewObject");
        go.transform.SetParent(null, false);
        _webView = go.AddComponent<WebViewObject>();
        _webView.Init(
            cb: OnJsFromPage,
            err: m => Debug.LogWarning("[SettingsWebViewBridge] err: " + m),
            httpErr: m => Debug.LogWarning("[SettingsWebViewBridge] httpErr: " + m),
            ld: _ => OnWebLoaded()
        );

        while (!_webView.IsInitialized()) yield return null;

        _webView.SetMargins(0, 0, 0, 0);
        _webView.SetVisibility(_shouldBeVisible);

        yield return StartCoroutine(LoadHtml());

        if (webLayoutTarget != null) ApplyMarginsFromRect(webLayoutTarget);
    }

    void OnWebLoaded()
    {
        const string bridgeJs = @"
window.__settingsFromUnityB64 = function(b64) {
  try {
    var binary = atob(b64);
    var bytes = new Uint8Array(binary.length);
    for (var i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    var json;
    if (typeof TextDecoder !== 'undefined') {
      json = new TextDecoder('utf-8').decode(bytes);
    } else {
      var s = '';
      for (var j = 0; j < bytes.length; j++) s += String.fromCharCode(bytes[j]);
      json = decodeURIComponent(escape(s));
    }
    window.dispatchEvent(new CustomEvent('blockpet-settings', { detail: JSON.parse(json) }));
  } catch(e) { console.log('settingsB64 err', e); }
};";

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS
        const string poly = @"
if (!window.Unity || typeof window.Unity.call !== 'function') {
  window.Unity = { call: function(msg){ window.location = 'unity:' + msg; } };
}";
#else
        const string poly = "";
#endif
        _webView.EvaluateJS(poly + bridgeJs);
        _pageReady = false;
    }

    void DispatchToPage(string json)
    {
        if (_webView == null || string.IsNullOrEmpty(json)) return;
        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        _webView.EvaluateJS($"window.__settingsFromUnityB64(\"{b64}\");");
    }

    void SendSettingsInit()
    {
        var fb = FirebaseManager.Instance;
        string name = fb != null ? fb.GetDisplayName() : "Guest";

        // Send avatar as base64 if available.
        string avatarB64 = null;
        if (AvatarManager.Instance != null && AvatarManager.Instance.CurrentAvatar != null)
            avatarB64 = Convert.ToBase64String(
                AvatarManager.Instance.CurrentAvatar.EncodeToPNG());

        string json = $"{{\"kind\":\"settings\",\"displayName\":\"{EscapeJson(name)}\""
                    + (avatarB64 != null ? $",\"avatarBase64\":\"{avatarB64}\"" : "")
                    + "}";
        DispatchToPage(json);
    }

    [Serializable] class JsMessage { public string type; public string nickname; }

    void OnJsFromPage(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return;
        JsMessage dto = null;
        try { dto = JsonUtility.FromJson<JsMessage>(msg); } catch { }
        if (dto == null) return;

        switch (dto.type)
        {
            case "ready":
                _pageReady = true;
                SendSettingsInit();
                break;

            case "close":
                if (pageManager != null) pageManager.ShowRoomPage();
                break;

            case "changeAvatar":
                if (AvatarManager.Instance != null)
                    AvatarManager.Instance.PickAvatarFromGallery(ok =>
                    {
                        if (!ok || !_pageReady) return;
                        string b64 = AvatarManager.Instance?.CurrentAvatar != null
                            ? Convert.ToBase64String(AvatarManager.Instance.CurrentAvatar.EncodeToPNG())
                            : null;
                        if (b64 != null)
                            DispatchToPage($"{{\"kind\":\"avatarUpdated\",\"avatarBase64\":\"{b64}\"}}");
                    });
                break;

            case "saveNickname":
                if (!string.IsNullOrEmpty(dto.nickname) && FirebaseManager.Instance != null)
                    FirebaseManager.Instance.SetDisplayName(dto.nickname, ok =>
                    {
                        if (!_pageReady) return;
                        string name = FirebaseManager.Instance?.GetDisplayName() ?? dto.nickname;
                        DispatchToPage($"{{\"kind\":\"nicknameUpdated\",\"displayName\":\"{EscapeJson(name)}\"}}");
                    });
                break;

            case "logout":
                FirebaseManager.Instance?.SignOut();
                AvatarManager.Instance?.ClearAvatar();
                UnityEngine.SceneManagement.SceneManager.LoadScene(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
                break;

            case "deleteAccount":
                FirebaseManager.Instance?.DeleteAccount((ok, err) =>
                {
                    if (ok)
                    {
                        AvatarManager.Instance?.ClearAvatar();
                        UnityEngine.SceneManagement.SceneManager.LoadScene(
                            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
                    }
                    else
                    {
                        Debug.LogWarning("[SettingsWebViewBridge] deleteAccount failed: " + err);
                    }
                });
                break;
        }
    }

    static string EscapeJson(string s) =>
        (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

    IEnumerator LoadHtml()
    {
        string rel = streamingAssetsRelativePath.Replace(" ", "%20");
        string baseDir = Path.GetDirectoryName(rel)?.Replace("\\", "/") ?? string.Empty;
        string indexFileName = Path.GetFileName(rel);
        string dstRoot = Path.Combine(Application.temporaryCachePath, "game-ui-settings");
        string dstIndex = Path.Combine(dstRoot, indexFileName);
        const string screenParam = "?screen=settings";

        if (Application.streamingAssetsPath.Contains("://", StringComparison.Ordinal))
        {
            yield return StartCoroutine(CopyViaManifestAndroid(baseDir, indexFileName, dstRoot, dstIndex, screenParam));
            yield break;
        }

        string srcDir = Path.Combine(Application.streamingAssetsPath,
            baseDir.TrimEnd('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
        string absIndex = Path.Combine(srcDir, indexFileName);
        if (!File.Exists(absIndex)) { Debug.LogError("[SettingsWebViewBridge] Missing: " + absIndex); yield break; }

        try
        {
            if (Directory.Exists(dstRoot)) Directory.Delete(dstRoot, true);
            Directory.CreateDirectory(dstRoot);
            CopyFolder(srcDir, dstRoot);
        }
        catch (Exception e) { Debug.LogError("[SettingsWebViewBridge] Copy failed: " + e.Message); }

        if (File.Exists(dstIndex))
            _webView.LoadURL("file://" + dstIndex.Replace(" ", "%20") + screenParam);
    }

    IEnumerator CopyViaManifestAndroid(string baseDir, string indexFileName, string dstRoot, string dstIndex, string screenParam)
    {
        try { if (Directory.Exists(dstRoot)) Directory.Delete(dstRoot, true); Directory.CreateDirectory(dstRoot); }
        catch (Exception e) { Debug.LogError("[SettingsWebViewBridge] " + e.Message); }

        if (!string.IsNullOrEmpty(baseDir) && !baseDir.EndsWith("/")) baseDir += "/";
        string manifestUrl = CombineStreamingUrl(baseDir + "manifest.txt");
        using var req = UnityWebRequest.Get(manifestUrl);
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success) { Debug.LogError("[SettingsWebViewBridge] manifest not found"); yield break; }

        foreach (var line in req.downloadHandler.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim().Replace("\\", "/");
            using var getFile = UnityWebRequest.Get(CombineStreamingUrl(baseDir + trimmed));
            yield return getFile.SendWebRequest();
            if (getFile.result != UnityWebRequest.Result.Success) continue;
            string dst = Path.Combine(dstRoot, trimmed.Replace("/", Path.DirectorySeparatorChar.ToString()));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.WriteAllBytes(dst, getFile.downloadHandler.data);
        }

        if (File.Exists(dstIndex))
            _webView.LoadURL("file://" + dstIndex.Replace(" ", "%20") + screenParam);
    }

    static void CopyFolder(string srcDir, string dstDir)
    {
        if (!Directory.Exists(srcDir)) return;
        foreach (string file in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            string rel = file.Substring(srcDir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
            string dst = Path.Combine(dstDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(file, dst, true);
        }
    }

    static string CombineStreamingUrl(string rel)
    {
        string root = Application.streamingAssetsPath;
        return root.Contains("://", StringComparison.Ordinal)
            ? root + "/" + rel.TrimStart('/')
            : "file://" + Path.Combine(root, rel.Replace("/", Path.DirectorySeparatorChar.ToString())).Replace("\\", "/");
    }

    void ApplyMarginsFromRect(RectTransform rt)
    {
        if (_webView == null || rt == null) return;
        Canvas canvas = rt.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var c in corners)
        {
            Vector2 sp = RectTransformUtility.WorldToScreenPoint(cam, c);
            if (sp.x < minX) minX = sp.x; if (sp.x > maxX) maxX = sp.x;
            if (sp.y < minY) minY = sp.y; if (sp.y > maxY) maxY = sp.y;
        }
        _webView.SetMargins(Mathf.RoundToInt(minX), Mathf.RoundToInt(Screen.height - maxY),
                            Mathf.RoundToInt(Screen.width - maxX), Mathf.RoundToInt(minY));
    }
}
