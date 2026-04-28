using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Shows the same React app (<c>StreamingAssets/chat-ui/index.html</c>) but initializes it in "room select" mode.
/// The page will send back <c>{ type: "setRoom", roomId: "..." }</c> which switches the Firebase room and navigates to ChatPage.
/// </summary>
[DefaultExecutionOrder(-90)]
public class RoomWebViewBridge : MonoBehaviour
{
    [SerializeField] PageManager pageManager;
    [Tooltip("Screen-area the native WebView should cover (usually the RoomPage RectTransform). Defaults to this object's RectTransform.")]
    [SerializeField] RectTransform webLayoutTarget;
    [SerializeField] string streamingAssetsRelativePath = "chat-ui/index.html";
    [SerializeField] bool updateMarginsEachFrame = true;

    WebViewObject _webView;
    bool _pageReady;
    bool _shouldBeVisible;
    Firebase.Database.DatabaseReference _userRoomsRef;
    bool _initStarted;

    void Awake()
    {
        Debug.Log("[RoomWebViewBridge] Awake()");
        if (pageManager == null)
            pageManager = FindObjectOfType<PageManager>(true);
        if (webLayoutTarget == null)
            webLayoutTarget = transform as RectTransform;
    }

    void Start()
    {
#if UNITY_EDITOR
        // Editor uses native UI for stability; room select is primarily for devices.
        return;
#endif
        // Prefer OnEnable initialization so it also works when the page starts inactive.
    }

    void OnEnable()
    {
        Debug.Log("[RoomWebViewBridge] OnEnable()");
        _shouldBeVisible = true;
        if (_webView != null)
            _webView.SetVisibility(true);

#if !UNITY_EDITOR
        if (!_initStarted)
        {
            _initStarted = true;
            Debug.Log("[RoomWebViewBridge] Initializing WebView (OnEnable)");
            StartCoroutine(InitWebViewCoroutine());
        }
#endif
    }

    void OnDisable()
    {
        _shouldBeVisible = false;
        if (_webView != null)
            _webView.SetVisibility(false);
        StopUserRoomsListener();
    }

    void OnDestroy()
    {
        StopUserRoomsListener();
        if (_webView != null)
        {
            Destroy(_webView.gameObject);
            _webView = null;
        }
    }

    void LateUpdate()
    {
        if (_webView != null && updateMarginsEachFrame && webLayoutTarget != null)
            ApplyMarginsFromRect(webLayoutTarget);
    }

    System.Collections.IEnumerator InitWebViewCoroutine()
    {
        var go = new GameObject("RoomWebViewObject");
        // IMPORTANT (iOS): the native plugin uses UnitySendMessage(GameObjectName,...)
        // which relies on GameObject.Find and cannot find inactive objects.
        // Keep the WebViewObject at the scene root so it stays active even when pages toggle.
        go.transform.SetParent(null, false);
        _webView = go.AddComponent<WebViewObject>();
        _webView.Init(
            cb: OnJsFromPage,
            err: m => Debug.LogWarning("[RoomWebViewBridge] WebView error: " + m),
            httpErr: m => Debug.LogWarning("[RoomWebViewBridge] WebView HTTP error: " + m),
            started: u => Debug.Log("[RoomWebViewBridge] started: " + u),
            hooked: _ => { },
            cookies: _ => { },
            ld: u =>
            {
                Debug.Log("[RoomWebViewBridge] loaded: " + u);
                OnWebLoaded();
            }
        );

        while (!_webView.IsInitialized())
            yield return null;

        _webView.SetMargins(0, 0, 0, 0);
        _webView.SetVisibility(_shouldBeVisible);

        yield return StartCoroutine(LoadHtmlSingleFileFromStreamingAssets());

        if (webLayoutTarget != null)
            ApplyMarginsFromRect(webLayoutTarget);
    }

    void OnWebLoaded()
    {
        const string bridgeJs = @"
window.__chatFromUnityB64 = function(b64) {
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
    var detail = JSON.parse(json);
    window.dispatchEvent(new CustomEvent('blockpet-chat', { detail: detail }));
  } catch (e) { console.log('__chatFromUnityB64 error', e); }
};
";

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS
        var poly = @"
// Ensure a Unity bridge exists for JS -> C# messaging.
// On iOS, `window.webkit.messageHandlers` may exist even when the WebView plugin does NOT
// provide `window.Unity.call`, so we must polyfill based on `window.Unity`, not on webkit.
if (!window.Unity || typeof window.Unity.call !== 'function') {
  window.Unity = {
    call: function(msg) {
      window.location = 'unity:' + msg;
    }
  };
}
";
#else
        const string poly = "";
#endif

        _webView.EvaluateJS(poly + bridgeJs);
        _pageReady = false;
    }

    [Serializable]
    class RoomInitPayload
    {
        public string kind;
        public string roomId;
        public string localDisplayName;
        public FirebaseManager.RoomSummary[] rooms;
    }

    void SendRoomInit()
    {
        if (_webView == null) return;
        var fb = FirebaseManager.Instance;
        if (fb == null)
            return;

        string currentRoomId = fb.RoomId;

        fb.GetMyRoomSummaries((list) =>
        {
            if (list == null)
                list = new System.Collections.Generic.List<FirebaseManager.RoomSummary>();

            // Always show the room currently stored in PlayerPrefs, even if it
            // was never formally "joined" through Firebase (e.g. the old global room).
            if (!string.IsNullOrEmpty(currentRoomId) &&
                !list.Exists(r => r.roomId == currentRoomId))
            {
                list.Insert(0, new FirebaseManager.RoomSummary
                {
                    roomId = currentRoomId,
                    name = currentRoomId == "global" ? "預設房間 (global)" : currentRoomId,
                });
            }

            var payload = new RoomInitPayload
            {
                kind = "room",
                roomId = currentRoomId,
                localDisplayName = fb.GetDisplayName(),
                rooms = list.ToArray()
            };
            DispatchToPage(JsonUtility.ToJson(payload));
        });
    }

    void StartUserRoomsListener()
    {
        if (_userRoomsRef != null)
            return;
        var fb = FirebaseManager.Instance;
        if (fb == null)
            return;
        _userRoomsRef = fb.GetUserRoomsRef();
        if (_userRoomsRef == null)
            return;
        _userRoomsRef.ValueChanged += HandleUserRoomsChanged;
    }

    void StopUserRoomsListener()
    {
        if (_userRoomsRef != null)
            _userRoomsRef.ValueChanged -= HandleUserRoomsChanged;
        _userRoomsRef = null;
    }

    void HandleUserRoomsChanged(object sender, Firebase.Database.ValueChangedEventArgs args)
    {
        if (!_shouldBeVisible || !_pageReady)
            return;
        if (args != null && args.DatabaseError != null)
        {
            Debug.LogWarning("[RoomWebViewBridge] rooms listener error: " + args.DatabaseError.Message);
            return;
        }
        // Re-fetch summaries (meta + petState) and push to page.
        SendRoomInit();
    }

    void DispatchToPage(string json)
    {
        if (_webView == null || string.IsNullOrEmpty(json))
            return;
        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        _webView.EvaluateJS("window.__chatFromUnityB64(\"" + b64 + "\");");
    }

    [Serializable]
    class WebViewToUnityMessage
    {
        public string type;
        public string roomId;
        public string roomName;
    }

    void OnJsFromPage(string msg)
    {
        if (string.IsNullOrEmpty(msg))
            return;

        WebViewToUnityMessage dto = null;
        try { dto = JsonUtility.FromJson<WebViewToUnityMessage>(msg); }
        catch { dto = null; }
        if (dto == null || string.IsNullOrEmpty(dto.type))
            return;

        switch (dto.type)
        {
            case "ready":
                _pageReady = true;
                SendRoomInit();
                StartUserRoomsListener();
                break;
            case "setRoom":
                if (FirebaseManager.Instance != null)
                    FirebaseManager.Instance.SetRoomId(dto.roomId);
                EnterGameAndGoHome();
                break;
            case "createRoom":
                if (FirebaseManager.Instance != null)
                {
                    // Navigate immediately; write membership to Firebase in the background.
                    string crId = dto.roomId;
                    string crName = dto.roomName;
                    FirebaseManager.Instance.SetRoomId(crId);
                    EnterGameAndGoHome();
                    FirebaseManager.Instance.CreateRoom(crId, crName, (ok, err) =>
                    {
                        if (!ok) Debug.LogWarning("[RoomWebViewBridge] createRoom background write failed: " + err);
                    });
                }
                break;
            case "joinRoom":
                if (FirebaseManager.Instance != null)
                {
                    // Navigate immediately; register membership in Firebase in the background.
                    string jrId = dto.roomId;
                    FirebaseManager.Instance.SetRoomId(jrId);
                    EnterGameAndGoHome();
                    FirebaseManager.Instance.JoinRoom(jrId, (ok, err) =>
                    {
                        if (!ok) Debug.LogWarning("[RoomWebViewBridge] joinRoom background write failed: " + err);
                    });
                }
                break;
            case "refreshRooms":
                SendRoomInit();
                break;
            case "back":
                if (pageManager == null)
                    pageManager = FindObjectOfType<PageManager>(true);
                if (pageManager != null)
                    pageManager.ShowHomePage();
                break;
        }
    }

    void EnterGameAndGoHome()
    {
        var login = FindObjectOfType<LoginUIHandler>(true);
        if (login != null)
            login.EnterMainGame();
        if (pageManager == null)
            pageManager = FindObjectOfType<PageManager>(true);
        if (pageManager != null)
            pageManager.ShowHomePage();
    }

    // Shared with ChatWebViewBridge: copies every file under srcDir into dstDir recursively.
    static void CopyFolder(string srcDir, string dstDir)
    {
        if (!Directory.Exists(srcDir)) return;
        foreach (string file in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            string rel = file.Substring(srcDir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
            string dst = Path.Combine(dstDir, rel);
            string folder = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                Directory.CreateDirectory(folder);
            File.Copy(file, dst, true);
        }
    }

    static string CombineStreamingUrl(string relativePosixPath)
    {
        string root = Application.streamingAssetsPath;
        if (root.Contains("://", StringComparison.Ordinal))
            return root + "/" + relativePosixPath.TrimStart('/');
        return "file://" + Path.Combine(root, relativePosixPath.Replace("/", Path.DirectorySeparatorChar.ToString())).Replace("\\", "/");
    }

    IEnumerator LoadHtmlSingleFileFromStreamingAssets()
    {
        string rel = streamingAssetsRelativePath.Replace(" ", "%20");
        string baseDir = Path.GetDirectoryName(rel)?.Replace("\\", "/") ?? string.Empty;
        string indexFileName = Path.GetFileName(rel);

        string dstRoot = Path.Combine(Application.temporaryCachePath, "chat-ui-room");
        string dstIndex = Path.Combine(dstRoot, indexFileName);
        const string screenParam = "?screen=room";

        // Android: StreamingAssets is a jar:// URL — use UnityWebRequest to copy each file.
        if (Application.streamingAssetsPath.Contains("://", StringComparison.Ordinal))
        {
            yield return StartCoroutine(CopyViaManifestAndroid(baseDir, indexFileName, dstRoot, dstIndex, screenParam));
            yield break;
        }

        // iOS / Desktop: direct file access — copy entire folder so JS/CSS assets load.
        string srcDir = Path.Combine(Application.streamingAssetsPath,
            baseDir.TrimEnd('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
        string absIndex = Path.Combine(srcDir, indexFileName);

        if (!File.Exists(absIndex))
        {
            Debug.LogError("[RoomWebViewBridge] Missing: " + absIndex +
                           " (run: cd chat-ui && npm run build && npm run unity:sync)");
            yield break;
        }

        try
        {
            if (Directory.Exists(dstRoot)) Directory.Delete(dstRoot, true);
            Directory.CreateDirectory(dstRoot);
            CopyFolder(srcDir, dstRoot);
        }
        catch (Exception e)
        {
            Debug.LogError("[RoomWebViewBridge] Copy to cache failed: " + e.Message);
        }

        if (File.Exists(dstIndex))
        {
            string url = "file://" + dstIndex.Replace(" ", "%20") + screenParam;
            Debug.Log("[RoomWebViewBridge] LoadURL: " + url);
            _webView.LoadURL(url);
        }
        else
        {
            Debug.LogError("[RoomWebViewBridge] Cache file missing after copy: " + dstIndex);
        }
    }

    IEnumerator CopyViaManifestAndroid(string baseDir, string indexFileName, string dstRoot, string dstIndex, string screenParam)
    {
        try
        {
            if (Directory.Exists(dstRoot)) Directory.Delete(dstRoot, true);
            Directory.CreateDirectory(dstRoot);
        }
        catch (Exception e)
        {
            Debug.LogError("[RoomWebViewBridge] Cache folder create failed: " + e.Message);
        }

        if (!string.IsNullOrEmpty(baseDir) && !baseDir.EndsWith("/"))
            baseDir += "/";

        string manifestUrl = CombineStreamingUrl(baseDir + "manifest.txt");
        using (var req = UnityWebRequest.Get(manifestUrl))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("[RoomWebViewBridge] manifest.txt not found at: " + manifestUrl +
                               " (run: npm run unity:manifest in chat-ui/)");
                yield break;
            }

            var lines = req.downloadHandler.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string trimmed = line.Trim().Replace("\\", "/");
                string fileUrl = CombineStreamingUrl(baseDir + trimmed);
                using (var getFile = UnityWebRequest.Get(fileUrl))
                {
                    yield return getFile.SendWebRequest();
                    if (getFile.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogWarning("[RoomWebViewBridge] Skip file: " + trimmed + " — " + getFile.error);
                        continue;
                    }

                    string dstPath = Path.Combine(dstRoot, trimmed.Replace("/", Path.DirectorySeparatorChar.ToString()));
                    string folder = Path.GetDirectoryName(dstPath);
                    if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                        Directory.CreateDirectory(folder);
                    File.WriteAllBytes(dstPath, getFile.downloadHandler.data);
                }
            }
        }

        if (File.Exists(dstIndex))
        {
            string url = "file://" + dstIndex.Replace(" ", "%20") + screenParam;
            Debug.Log("[RoomWebViewBridge] LoadURL (Android): " + url);
            _webView.LoadURL(url);
        }
        else
        {
            Debug.LogError("[RoomWebViewBridge] index.html not found after Android manifest copy.");
        }
    }

    void ApplyMarginsFromRect(RectTransform rt)
    {
        if (_webView == null || rt == null)
            return;

        Canvas canvas = rt.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners);

        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            Vector2 sp = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
            minX = Mathf.Min(minX, sp.x);
            maxX = Mathf.Max(maxX, sp.x);
            minY = Mathf.Min(minY, sp.y);
            maxY = Mathf.Max(maxY, sp.y);
        }

        int left = Mathf.RoundToInt(minX);
        int right = Mathf.RoundToInt(Screen.width - maxX);
        int bottom = Mathf.RoundToInt(minY);
        int top = Mathf.RoundToInt(Screen.height - maxY);
        _webView.SetMargins(left, top, right, bottom);
    }
}

