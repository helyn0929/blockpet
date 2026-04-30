using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using System.Reflection;
/// <summary>
/// Hosts the React chat UI in <see cref="WebViewObject"/> (net.gree.unity-webview) and exchanges JSON with the page.
/// Build the web app with <c>npm run build</c> in <c>game-ui/</c>, then run <c>npm run unity:manifest</c> so Android can copy all assets from StreamingAssets.
/// </summary>
[DefaultExecutionOrder(-80)]
public class ChatWebViewBridge : MonoBehaviour
{
    const string ManifestFileName = "manifest.txt";

    [SerializeField] ChatUIHandler chatUIHandler;
    [Tooltip("Screen-area the native WebView should cover (usually the Chat panel RectTransform). Defaults to this object's RectTransform.")]
    [SerializeField] RectTransform webLayoutTarget;
    [Tooltip("Optional: native TMP composer RectTransform to keep clickable when useNativeComposer is true (Editor IME workaround).")]
    [SerializeField] RectTransform nativeComposerRect;
    [Tooltip("Canvas used by gree WebView on macOS Editor (offscreen texture). Usually the root UI Canvas.")]
    [SerializeField] Canvas editorWebViewCanvas;
    [SerializeField] string streamingAssetsRelativePath = "game-ui/index.html";
    [SerializeField] bool updateMarginsEachFrame = true;
    [Header("Editor sizing")]
    [Tooltip("Unity Editor: force WebView fullscreen to avoid RectTransform-to-screen conversion issues.")]
    [SerializeField] bool forceFullscreenInEditor = true;
    [Tooltip("Unity Editor: use a dedicated overlay canvas for WebView so it doesn't conflict with other RawImages (e.g. photo thumbnails).")]
    [SerializeField] bool useDedicatedEditorWebViewCanvas = true;

    WebViewObject _webView;
    bool _pageReady;
    ChatWebInitPayload _pendingInit;
    ChatWebInitPayload _pendingInitWithoutMessages;
    ChatMessage[] _pendingAppendAfterInit;
    string _deferredRoomName;
    int _deferredMemberCount = -1;
    bool _shouldBeVisible;
    bool _useNativeComposer;

    // Some embedded WebViews (notably in Unity Editor) have small EvaluateJS string limits.
    // Large init payloads (e.g. 50+ messages) can be truncated and result in an empty UI.
    const int MaxInitJsonCharsSafe = 14000;

#if UNITY_EDITOR
    int _lastLogLeft = int.MinValue, _lastLogTop = int.MinValue, _lastLogRight = int.MinValue, _lastLogBottom = int.MinValue;
    RectTransform _editorWebViewOverlayRt;
    bool _editorOverlayWatcherStarted;
    float _nextMissingOverlayWarnAt;
    bool _dumpedEditorOverlayHierarchy;
#endif

    void Awake()
    {
        if (chatUIHandler == null)
            chatUIHandler = GetComponent<ChatUIHandler>();
        if (webLayoutTarget == null)
            webLayoutTarget = transform as RectTransform;
        if (nativeComposerRect == null && chatUIHandler != null)
            nativeComposerRect = chatUIHandler.GetNativeComposerRectTransform();

#if UNITY_EDITOR
        // Inspector serialization can override the field default; in Editor we always want fullscreen for stability.
        forceFullscreenInEditor = true;
#endif
    }

    void Start()
    {
        if (chatUIHandler == null)
            chatUIHandler = GetComponent<ChatUIHandler>();
        if (chatUIHandler == null || !chatUIHandler.IsWebViewChatEnabled())
            return;
#if UNITY_EDITOR
        // Editor: force legacy Unity UI (IME + layout stability).
        return;
#endif
        StartCoroutine(InitWebViewCoroutine());
    }

    void OnEnable()
    {
        FirebaseManager.OnRoomChanged += OnRoomChanged;
        // When PageManager toggles pages, the MonoBehaviour stays alive but its GameObject is disabled/enabled.
        // Keep the native WebView visibility in sync so it doesn't overlay other pages as a white screen.
        _shouldBeVisible = true;
        if (_webView != null)
            _webView.SetVisibility(true);
    }

    void OnDisable()
    {
        FirebaseManager.OnRoomChanged -= OnRoomChanged;
        _shouldBeVisible = false;
        if (_webView != null)
            _webView.SetVisibility(false);
    }

    void OnRoomChanged()
    {
        if (_webView == null || !_pageReady) return;
        string json = "{\"kind\":\"clearMessages\"}";
        string b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
        _webView.EvaluateJS("window.__chatFromUnityB64(\"" + b64 + "\");");
    }

    void OnDestroy()
    {
        FirebaseManager.OnRoomChanged -= OnRoomChanged;
        if (_webView != null)
        {
            Destroy(_webView.gameObject);
            _webView = null;
        }
    }

    void LateUpdate()
    {
        if (_webView != null && updateMarginsEachFrame)
        {
#if UNITY_EDITOR
            if (forceFullscreenInEditor)
            {
                _webView.SetMargins(0, 0, 0, 0);
                // Keep the actual web overlay stretched (once we find it). Avoid spamming logs when missing.
                if (_editorWebViewOverlayRt != null)
                {
                    StretchRectTransformFullScreen(_editorWebViewOverlayRt);
                    StretchParentsToCanvasRoot(_editorWebViewOverlayRt);
                }
                else if (!_editorOverlayWatcherStarted && isActiveAndEnabled && gameObject.activeInHierarchy)
                {
                    _editorOverlayWatcherStarted = true;
                    StartCoroutine(EditorOverlayWatcherCoroutine());
                }
                return;
            }
#endif
            if (webLayoutTarget == null || IsClearlyTooSmall(webLayoutTarget))
                EnsureBestLayoutTarget();
        }
        if (_webView != null && updateMarginsEachFrame && webLayoutTarget != null)
            ApplyMarginsFromRect(webLayoutTarget);
    }

    /// <summary>Called by <see cref="ChatUIHandler"/> after history is loaded or when messages change.</summary>
    public void RequestFullSync(IReadOnlyList<ChatMessage> messages, string roomName, string roomId, int memberCount, string localDisplayName, bool mineMessagesOnRight, string animalImageBase64Png, bool useNativeComposer)
    {
        _useNativeComposer = useNativeComposer;
#if UNITY_EDITOR
        // User requested pure Web UI in Editor, so never reserve space for a native composer.
        if (forceFullscreenInEditor)
            _useNativeComposer = false;
#endif
        if (nativeComposerRect == null && chatUIHandler != null)
            nativeComposerRect = chatUIHandler.GetNativeComposerRectTransform();

        var full = new ChatWebInitPayload
        {
            kind = "init",
            messages = ToArray(messages),
            roomName = roomName ?? string.Empty,
            roomId = roomId ?? string.Empty,
            memberCount = memberCount,
            localDisplayName = localDisplayName ?? string.Empty,
            localUserId = FirebaseManager.Instance?.GetUserId() ?? string.Empty,
            mineMessagesOnRight = mineMessagesOnRight,
            animalImageBase64 = animalImageBase64Png,
            useNativeComposer = useNativeComposer
        };

        // If init JSON is too large, send init without messages, then append messages in smaller chunks.
        string fullJson = JsonUtility.ToJson(full);
        if (fullJson != null && fullJson.Length > MaxInitJsonCharsSafe)
        {
            var initNoMessages = new ChatWebInitPayload
            {
                kind = "init",
                messages = Array.Empty<ChatMessage>(),
                roomName = roomName ?? string.Empty,
                roomId = roomId ?? string.Empty,
                memberCount = memberCount,
                localDisplayName = localDisplayName ?? string.Empty,
                localUserId = FirebaseManager.Instance?.GetUserId() ?? string.Empty,
                mineMessagesOnRight = mineMessagesOnRight,
                animalImageBase64 = animalImageBase64Png,
                useNativeComposer = useNativeComposer
            };

            if (!_pageReady)
            {
                _pendingInitWithoutMessages = initNoMessages;
                _pendingAppendAfterInit = ToArray(messages);
                return;
            }

            SendInit(initNoMessages);
            StartCoroutine(AppendMessagesCoroutine(ToArray(messages)));
            return;
        }

        if (!_pageReady)
        {
            _pendingInit = full;
            return;
        }

        SendInit(full);
    }

    public void NotifyMessageAppended(ChatMessage msg)
    {
        if (msg == null || !_pageReady)
            return;
        var payload = new ChatWebAppendPayload { kind = "append", message = msg };
        DispatchToPage(JsonUtility.ToJson(payload));
    }

    public void NotifyHeader(string roomName, int memberCount)
    {
        if (!_pageReady)
        {
            _deferredRoomName = roomName ?? string.Empty;
            _deferredMemberCount = memberCount;
            if (_pendingInit != null)
            {
                _pendingInit.roomName = _deferredRoomName;
                _pendingInit.memberCount = memberCount;
            }
            return;
        }

        var payload = new ChatWebHeaderPayload
        {
            kind = "header",
            roomName = roomName ?? string.Empty,
            memberCount = memberCount
        };
        DispatchToPage(JsonUtility.ToJson(payload));
    }

    public void NotifyReplyCleared()
    {
        if (!_pageReady)
            return;
        var payload = new ChatWebClearReplyPayload { kind = "clearReply" };
        DispatchToPage(JsonUtility.ToJson(payload));
    }

    static ChatMessage[] ToArray(IReadOnlyList<ChatMessage> messages)
    {
        if (messages == null || messages.Count == 0)
            return Array.Empty<ChatMessage>();
        var arr = new ChatMessage[messages.Count];
        for (int i = 0; i < messages.Count; i++)
            arr[i] = messages[i];
        return arr;
    }

    IEnumerator InitWebViewCoroutine()
    {
        var go = new GameObject("WebViewObject");
        go.transform.SetParent(transform, false);
        _webView = go.AddComponent<WebViewObject>();

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        if (editorWebViewCanvas == null)
            editorWebViewCanvas = GetComponentInParent<Canvas>();
        if (editorWebViewCanvas == null)
            editorWebViewCanvas = FindAnyObjectByType<Canvas>();
        if (useDedicatedEditorWebViewCanvas)
            editorWebViewCanvas = EnsureDedicatedEditorCanvas(editorWebViewCanvas);
        _webView.canvas = editorWebViewCanvas != null ? editorWebViewCanvas.gameObject : null;
#endif
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (editorWebViewCanvas == null)
            editorWebViewCanvas = GetComponentInParent<Canvas>();
        if (editorWebViewCanvas == null)
            editorWebViewCanvas = FindAnyObjectByType<Canvas>();
        if (useDedicatedEditorWebViewCanvas)
            editorWebViewCanvas = EnsureDedicatedEditorCanvas(editorWebViewCanvas);
        _webView.canvas = editorWebViewCanvas != null ? editorWebViewCanvas.gameObject : null;
#endif

        _webView.Init(
            cb: OnJsFromPage,
            err: m => Debug.LogWarning("[ChatWebViewBridge] WebView error: " + m),
            httpErr: m => Debug.LogWarning("[ChatWebViewBridge] WebView HTTP error: " + m),
            started: u => Debug.Log("[ChatWebViewBridge] started: " + u),
            hooked: _ => { },
            cookies: _ => { },
            ld: u =>
            {
                Debug.Log("[ChatWebViewBridge] loaded: " + u);
                OnWebLoaded(u);
            }
        );

        while (!_webView.IsInitialized())
            yield return null;

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        _webView.bitmapRefreshCycle = 1;
        _webView.devicePixelRatio = 1;
#endif

        // If sizing is wrong, user will see a tiny white square. Keep a safe fullscreen fallback.
        _webView.SetMargins(0, 0, 0, 0);
        // Wait one frame so layout sizes are valid, then pick the best target.
        yield return null;
        EnsureBestLayoutTarget();
        if (webLayoutTarget != null)
            ApplyMarginsFromRect(webLayoutTarget);

        _webView.SetVisibility(_shouldBeVisible);

        yield return StartCoroutine(LoadStreamingAssetsHtml());

        if (webLayoutTarget != null)
            ApplyMarginsFromRect(webLayoutTarget);

#if UNITY_EDITOR
        // Unity Editor mode renders to a RawImage under the assigned canvas; margins alone may not resize it.
        // Start a watcher that binds to the WebView overlay once it exists.
        if (!_editorOverlayWatcherStarted)
        {
            _editorOverlayWatcherStarted = true;
            StartCoroutine(EditorOverlayWatcherCoroutine());
        }
#endif
    }

#if UNITY_EDITOR
    Canvas EnsureDedicatedEditorCanvas(Canvas existingRoot)
    {
        if (!useDedicatedEditorWebViewCanvas)
            return existingRoot;
        if (existingRoot == null)
            return null;

        Transform rootT = existingRoot.transform;
        Transform existing = rootT.Find("WebViewOverlayCanvas");
        if (existing != null)
        {
            Canvas c = existing.GetComponent<Canvas>();
            if (c != null) return c;
        }

        GameObject go = new GameObject("WebViewOverlayCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(rootT, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10000;
        canvas.overrideSorting = true;

        // Match scaler settings for consistent sizing.
        CanvasScaler src = existingRoot.GetComponent<CanvasScaler>();
        CanvasScaler dst = go.GetComponent<CanvasScaler>();
        if (src != null)
        {
            dst.uiScaleMode = src.uiScaleMode;
            dst.referenceResolution = src.referenceResolution;
            dst.matchWidthOrHeight = src.matchWidthOrHeight;
            dst.screenMatchMode = src.screenMatchMode;
            dst.referencePixelsPerUnit = src.referencePixelsPerUnit;
        }
        else
        {
            dst.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            dst.referenceResolution = new Vector2(1080, 1920);
            dst.matchWidthOrHeight = 0.5f;
        }

        Debug.Log("[ChatWebViewBridge] Created dedicated Editor WebView overlay canvas.");
        return canvas;
    }
#endif

#if UNITY_EDITOR
    IEnumerator EditorOverlayWatcherCoroutine()
    {
        float start = Time.realtimeSinceStartup;
        while (_webView != null && _editorWebViewOverlayRt == null)
        {
            RectTransform pick = FindEditorWebViewOverlayRectTransform();
            if (pick != null)
            {
                _editorWebViewOverlayRt = pick;
                StretchRectTransformFullScreen(_editorWebViewOverlayRt);
                StretchParentsToCanvasRoot(_editorWebViewOverlayRt);
                Debug.Log("[ChatWebViewBridge] Editor overlay bound: " + _editorWebViewOverlayRt.name);
                yield break;
            }

            if (Time.realtimeSinceStartup >= _nextMissingOverlayWarnAt)
            {
                _nextMissingOverlayWarnAt = Time.realtimeSinceStartup + 2f;
                Debug.LogWarning("[ChatWebViewBridge] Editor overlay stretch: still waiting for WebView RawImage... (" +
                                 (Time.realtimeSinceStartup - start).ToString("0.0") + "s)");
            }

            if (!_dumpedEditorOverlayHierarchy && (Time.realtimeSinceStartup - start) > 10f)
            {
                _dumpedEditorOverlayHierarchy = true;
                Debug.LogWarning("[ChatWebViewBridge] Editor overlay diagnostic (canvas graphics):\n" +
                                 DescribeGraphicsUnder(_webView != null ? _webView.canvas : null));
                Debug.LogWarning("[ChatWebViewBridge] Editor overlay diagnostic (webview GO graphics):\n" +
                                 DescribeGraphicsUnder(_webView != null ? _webView.gameObject : null));
            }

            yield return null;
        }
    }

    RectTransform FindEditorWebViewOverlayRectTransform()
    {
        Graphic g = PickBestWebViewGraphic(_webView != null ? _webView.gameObject : null);
        if (g == null)
            g = PickBestWebViewGraphic(_webView != null ? _webView.canvas : null);
        if (g != null)
            return g.rectTransform;

        RawImage ri = TryGetWebViewRawImageViaReflection(_webView);
        if (ri != null)
            return ri.rectTransform;

        return null;
    }

    static void StretchRectTransformFullScreen(RectTransform rt)
    {
        if (rt == null) return;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static RawImage TryGetWebViewRawImageViaReflection(WebViewObject wv)
    {
        if (wv == null) return null;
        try
        {
            // net.gree.unity-webview stores the overlay RawImage in a private field on some platforms.
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            FieldInfo[] fields = wv.GetType().GetFields(flags);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i] == null) continue;
                if (typeof(RawImage).IsAssignableFrom(fields[i].FieldType))
                {
                    var ri = fields[i].GetValue(wv) as RawImage;
                    if (ri != null) return ri;
                }
            }

            // Some versions store a GameObject/Component that has the RawImage.
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i] == null) continue;
                object v = fields[i].GetValue(wv);
                if (v is GameObject go && go != null)
                {
                    var ri = go.GetComponentInChildren<RawImage>(true);
                    if (ri != null) return ri;
                }
                if (v is Component c && c != null)
                {
                    var ri = c.GetComponentInChildren<RawImage>(true);
                    if (ri != null) return ri;
                }
            }
        }
        catch { }
        return null;
    }

    static RawImage PickBestWebViewRawImage(RawImage[] raws)
    {
        if (raws == null || raws.Length == 0) return null;

        RawImage best = null;
        int bestScore = int.MinValue;
        for (int i = 0; i < raws.Length; i++)
        {
            RawImage r = raws[i];
            if (r == null) continue;
            string n = r.name ?? string.Empty;
            int score = 0;
            // Strong signal: RenderTexture means it's probably the webview surface in Editor.
            if (r.texture is RenderTexture) score += 500;
            if (n.IndexOf("webview", StringComparison.OrdinalIgnoreCase) >= 0) score += 100;
            if (n.IndexOf("gree", StringComparison.OrdinalIgnoreCase) >= 0) score += 60;
            if (n.IndexOf("photo", StringComparison.OrdinalIgnoreCase) >= 0) score -= 500;
            if (n.IndexOf("thumb", StringComparison.OrdinalIgnoreCase) >= 0) score -= 200;
            if (n.IndexOf("avatar", StringComparison.OrdinalIgnoreCase) >= 0) score -= 300;
            if (n.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0) score -= 200;
            if (r.texture != null) score += 20;
            if (r.material != null && (r.material.name.IndexOf("webview", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       r.material.shader != null && r.material.shader.name.IndexOf("webview", StringComparison.OrdinalIgnoreCase) >= 0))
                score += 40;

            // Prefer something already close to fullscreen.
            float w = Mathf.Abs(r.rectTransform.rect.width);
            float h = Mathf.Abs(r.rectTransform.rect.height);
            if (w > Screen.width * 0.6f && h > Screen.height * 0.6f) score += 15;

            if (score > bestScore)
            {
                bestScore = score;
                best = r;
            }
        }

        // Diagnostics: print candidates once if we picked something suspicious.
        return best;
    }

    static void StretchParentsToCanvasRoot(RectTransform img)
    {
        if (img == null) return;
        Canvas c = img.GetComponentInParent<Canvas>();
        if (c == null) return;
        Transform stop = c.transform;

        Transform cur = img;
        while (cur != null && cur != stop)
        {
            if (cur is RectTransform rt && rt.parent is RectTransform)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            cur = cur.parent;
        }
    }

    static Graphic PickBestWebViewGraphic(GameObject root)
    {
        if (root == null) return null;
        Graphic[] gs = root.GetComponentsInChildren<Graphic>(true);
        Graphic best = null;
        int bestScore = int.MinValue;
        for (int i = 0; i < gs.Length; i++)
        {
            Graphic g = gs[i];
            if (g == null) continue;
            string n = g.name ?? string.Empty;
            int score = 0;

            Texture tex = null;
            if (g.material != null)
                tex = g.material.mainTexture;
            if (tex == null && g is RawImage rimg)
                tex = rimg.texture;

            if (tex is RenderTexture) score += 800;
            if (n.IndexOf("webview", StringComparison.OrdinalIgnoreCase) >= 0) score += 120;
            if (n.IndexOf("gree", StringComparison.OrdinalIgnoreCase) >= 0) score += 80;
            if (n.IndexOf("photo", StringComparison.OrdinalIgnoreCase) >= 0) score -= 600;
            if (n.IndexOf("avatar", StringComparison.OrdinalIgnoreCase) >= 0) score -= 500;
            if (n.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0) score -= 200;

            float w = Mathf.Abs(g.rectTransform.rect.width);
            float h = Mathf.Abs(g.rectTransform.rect.height);
            if (w > Screen.width * 0.6f && h > Screen.height * 0.6f) score += 25;
            if (tex != null) score += 10;

            if (score > bestScore)
            {
                bestScore = score;
                best = g;
            }
        }
        return best;
    }

    static string DescribeGraphicsUnder(GameObject root)
    {
        if (root == null) return "(null root)";
        var sb = new StringBuilder();
        Graphic[] gs = root.GetComponentsInChildren<Graphic>(true);
        sb.Append("root=").Append(root.name).Append(" graphics=").Append(gs.Length).Append("\n");
        int max = Mathf.Min(gs.Length, 60);
        for (int i = 0; i < max; i++)
        {
            Graphic g = gs[i];
            if (g == null) continue;
            Texture tex = null;
            if (g.material != null)
                tex = g.material.mainTexture;
            if (tex == null && g is RawImage rimg)
                tex = rimg.texture;
            sb.Append("- ").Append(g.name)
              .Append(" type=").Append(g.GetType().Name)
              .Append(" tex=").Append(tex != null ? tex.GetType().Name : "null")
              .Append(" rect=").Append(Mathf.Abs(g.rectTransform.rect.width)).Append("x").Append(Mathf.Abs(g.rectTransform.rect.height))
              .Append("\n");
        }
        if (gs.Length > max)
            sb.Append("... (").Append(gs.Length - max).Append(" more)\n");
        return sb.ToString();
    }

    static string DescribeRawImages(RawImage[] raws)
    {
        if (raws == null) return "(none)";
        var sb = new StringBuilder();
        for (int i = 0; i < raws.Length; i++)
        {
            RawImage r = raws[i];
            if (r == null) continue;
            sb.Append("- ").Append(r.name)
              .Append(" tex=").Append(r.texture != null ? (r.texture.width + "x" + r.texture.height) : "null")
              .Append(" rect=").Append(Mathf.Abs(r.rectTransform.rect.width)).Append("x").Append(Mathf.Abs(r.rectTransform.rect.height))
              .Append("\n");
        }
        return sb.ToString();
    }
#endif

    void EnsureBestLayoutTarget()
    {
        // If the assigned target is missing or too small (common when the bridge object is a tiny RectTransform),
        // fall back to the ChatUIHandler's RectTransform or the nearest parent that looks like a full-screen panel.
        RectTransform candidate = webLayoutTarget;
        if (candidate == null || IsClearlyTooSmall(candidate))
        {
            if (chatUIHandler != null)
            {
                RectTransform hrt = chatUIHandler.transform as RectTransform;
                if (hrt != null && !IsClearlyTooSmall(hrt))
                    candidate = hrt;
                else if (hrt != null)
                {
                    RectTransform p = hrt.parent as RectTransform;
                    while (p != null)
                    {
                        if (!IsClearlyTooSmall(p))
                        {
                            candidate = p;
                            break;
                        }
                        p = p.parent as RectTransform;
                    }
                }
            }

            if (candidate == null)
            {
                RectTransform selfRt = transform as RectTransform;
                RectTransform p = selfRt != null ? selfRt.parent as RectTransform : null;
                while (p != null)
                {
                    if (!IsClearlyTooSmall(p))
                    {
                        candidate = p;
                        break;
                    }
                    p = p.parent as RectTransform;
                }
            }
        }

        webLayoutTarget = candidate;
    }

    static bool IsClearlyTooSmall(RectTransform rt)
    {
        if (rt == null) return true;
        float w = Mathf.Abs(rt.rect.width);
        float h = Mathf.Abs(rt.rect.height);
        if (w < 10f || h < 10f) return true;
        // If it's less than ~65% of the screen in either dimension, it's probably not the full chat panel.
        return w < Screen.width * 0.65f || h < Screen.height * 0.65f;
    }

    void OnWebLoaded(string _)
    {
        const string bridgeJs = @"
window.__chatFromUnityB64 = function(b64) {
  try {
    var binary = atob(b64);
    var bytes = new Uint8Array(binary.length);
    for (var i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    // TextDecoder is not available in some embedded WebViews (notably Unity Editor).
    // Fallback keeps UTF-8 intact for CJK text.
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
if (!(window.webkit && window.webkit.messageHandlers)) {
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
        // IMPORTANT: do NOT assume the React app is ready here. In Unity Editor embedded WebViews,
        // our CustomEvent can fire before React mounts and registers its listener, causing an empty UI.
        // The page will send a `{ type: "ready" }` message once it has mounted.
        _pageReady = false;

#if UNITY_EDITOR
        Debug.Log("[ChatWebViewBridge] JS bridge injected. Waiting for page ready handshake...");
#endif

        _deferredMemberCount = -1;
    }

    IEnumerator AppendMessagesCoroutine(ChatMessage[] msgs)
    {
        if (msgs == null || msgs.Length == 0)
            yield break;
        // Yield one frame so React can mount after init.
        yield return null;
        for (int i = 0; i < msgs.Length; i++)
        {
            if (!_pageReady)
                yield break;
            if (msgs[i] != null)
                NotifyMessageAppended(msgs[i]);
            // Spread out EvaluateJS calls to avoid choking the embedded webview.
            if ((i % 6) == 5)
                yield return null;
        }
    }

    void OnJsFromPage(string msg)
    {
        if (string.IsNullOrEmpty(msg) || chatUIHandler == null)
            return;

        WebViewToUnityMessage dto = null;
        try
        {
            dto = JsonUtility.FromJson<WebViewToUnityMessage>(msg);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ChatWebViewBridge] Invalid JSON from page: " + e.Message);
            return;
        }

        if (dto == null || string.IsNullOrEmpty(dto.type))
            return;

        switch (dto.type)
        {
            case "ready":
                _pageReady = true;
                if (_pendingInit != null)
                {
                    SendInit(_pendingInit);
                    _pendingInit = null;
                }
                else if (_pendingInitWithoutMessages != null)
                {
                    SendInit(_pendingInitWithoutMessages);
                    _pendingInitWithoutMessages = null;
                    if (_pendingAppendAfterInit != null && _pendingAppendAfterInit.Length > 0)
                    {
                        StartCoroutine(AppendMessagesCoroutine(_pendingAppendAfterInit));
                        _pendingAppendAfterInit = null;
                    }
                }
                else
                {
                    chatUIHandler.PushFullStateToWebView();
                }
#if UNITY_EDITOR
                Debug.Log("[ChatWebViewBridge] Page ready handshake received. Init dispatched.");
#endif
                break;
            case "send":
                chatUIHandler.SendFromWebView(dto.text, dto.replyToMessageId, dto.replyToDisplayName, dto.replyToMessagePreview);
                break;
            case "back":
                chatUIHandler.WebViewRequestBack();
                break;
            case "clearReply":
                chatUIHandler.WebViewClearReply();
                break;
            case "openAlbum":
                chatUIHandler.WebViewRequestOpenAlbum();
                break;
            case "leaveChat":
                chatUIHandler.WebViewRequestLeaveChat();
                break;
            case "replySelect":
                chatUIHandler.WebViewSelectReply(
                    dto.selectedMessageId,
                    dto.selectedUserName,
                    dto.selectedDisplayName,
                    dto.selectedMessageBody);
                break;
            case "setRoom":
                chatUIHandler.WebViewSetRoom(dto.roomId);
                break;
            default:
                Debug.Log("[ChatWebViewBridge] Unknown message type: " + dto.type);
                break;
        }
    }

    IEnumerator LoadStreamingAssetsHtml()
    {
        string url = streamingAssetsRelativePath.Replace(" ", "%20");
        Debug.Log("[ChatWebViewBridge] Loading: " + url);
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            _webView.LoadURL(url + "?screen=chat");
            yield break;
        }

#if UNITY_WEBPLAYER || UNITY_WEBGL
        _webView.LoadURL("StreamingAssets/" + url + "?screen=chat");
        yield break;
#else
        if (!url.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError("[ChatWebViewBridge] streamingAssetsRelativePath should end with .html");
            yield break;
        }

        string baseDir = Path.GetDirectoryName(url)?.Replace("\\", "/") ?? string.Empty;
        string streamingRoot = Path.Combine(Application.streamingAssetsPath, baseDir.TrimEnd('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
        string indexFileName = Path.GetFileName(url);

        if (!Application.streamingAssetsPath.Contains("://", StringComparison.Ordinal))
        {
            string absIndex = Path.Combine(streamingRoot, indexFileName);
            if (File.Exists(absIndex))
            {
                // Prefer loading from cache so WKWebView can read relative module assets reliably.
                string dstRoot = Path.Combine(Application.temporaryCachePath, "game-ui");
                try
                {
                    if (Directory.Exists(dstRoot))
                        Directory.Delete(dstRoot, true);
                    Directory.CreateDirectory(dstRoot);
                    CopyStreamingChatUiFolder(streamingRoot, dstRoot);
                }
                catch (Exception e)
                {
                    Debug.LogError("[ChatWebViewBridge] Copy to cache failed: " + e.Message);
                }

                string dstIndex = Path.Combine(dstRoot, indexFileName);
                if (File.Exists(dstIndex))
                {
                    string cached = "file://" + dstIndex.Replace(" ", "%20") + "?screen=chat";
                    Debug.Log("[ChatWebViewBridge] LoadURL cached: " + cached);
                    _webView.LoadURL(cached);
                    yield break;
                }
            }
            else
            {
                Debug.LogError("[ChatWebViewBridge] Missing file at: " + absIndex + " (run: cd game-ui && npm run unity:sync)");
            }
        }

        // Fallback path: copy via manifest (Android) or if cache copy fails.
        if (!Application.streamingAssetsPath.Contains("://", StringComparison.Ordinal))
        {
            string absIndex = Path.Combine(streamingRoot, indexFileName);
            if (File.Exists(absIndex))
            {
                string dstRoot = Path.Combine(Application.temporaryCachePath, "game-ui");
                try
                {
                    if (Directory.Exists(dstRoot))
                        Directory.Delete(dstRoot, true);
                    Directory.CreateDirectory(dstRoot);
                    CopyStreamingChatUiFolder(streamingRoot, dstRoot);
                }
                catch (Exception e)
                {
                    Debug.LogError("[ChatWebViewBridge] Copy to cache failed: " + e.Message);
                }

                string dstIndex = Path.Combine(dstRoot, indexFileName);
                if (File.Exists(dstIndex))
                {
                    _webView.LoadURL("file://" + dstIndex.Replace(" ", "%20") + "?screen=chat");
                    yield break;
                }
            }
        }

        if (!string.IsNullOrEmpty(baseDir) && !baseDir.EndsWith("/"))
            baseDir += "/";
        yield return StartCoroutine(CopyStreamingChatUiAndroidFallback(baseDir, indexFileName));
#endif
    }

    void CopyStreamingChatUiFolder(string srcDir, string dstDir)
    {
        if (!Directory.Exists(srcDir))
            return;
        foreach (string file in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            string rel = file.Substring(srcDir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
            string dst = Path.Combine(dstDir, rel);
            string dstFolder = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(dstFolder) && !Directory.Exists(dstFolder))
                Directory.CreateDirectory(dstFolder);
            File.Copy(file, dst, true);
        }
    }

    IEnumerator CopyStreamingChatUiAndroidFallback(string baseDir, string indexFileName)
    {
        string dstRoot = Path.Combine(Application.temporaryCachePath, "game-ui");
        try
        {
            if (Directory.Exists(dstRoot))
                Directory.Delete(dstRoot, true);
            Directory.CreateDirectory(dstRoot);
        }
        catch (Exception e)
        {
            Debug.LogError("[ChatWebViewBridge] Cache folder: " + e.Message);
        }

        string manifestUrl = CombineStreamingAssetsUrl(Path.Combine(baseDir, ManifestFileName).Replace("\\", "/"));
        using (var req = UnityWebRequest.Get(manifestUrl))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("[ChatWebViewBridge] Missing " + ManifestFileName + " under StreamingAssets/game-ui/. Run: npm run unity:manifest in game-ui/. " + req.error);
                yield break;
            }

            var lines = req.downloadHandler.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rel in lines)
            {
                if (string.IsNullOrWhiteSpace(rel))
                    continue;
                string trimmed = rel.Trim().Replace("\\", "/");
                string fileUrl = CombineStreamingAssetsUrl(Path.Combine(baseDir, trimmed).Replace("\\", "/"));
                using (var getFile = UnityWebRequest.Get(fileUrl))
                {
                    yield return getFile.SendWebRequest();
                    if (getFile.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogWarning("[ChatWebViewBridge] Skip file (download failed): " + trimmed + " — " + getFile.error);
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

        string dstIndex = Path.Combine(dstRoot, indexFileName);
        if (File.Exists(dstIndex))
            _webView.LoadURL("file://" + dstIndex.Replace(" ", "%20") + "?screen=chat");
        else
            Debug.LogError("[ChatWebViewBridge] index.html not found after manifest copy.");
    }

    static string CombineStreamingAssetsUrl(string relativePosixPath)
    {
        string root = Application.streamingAssetsPath;
        if (root.Contains("://", StringComparison.Ordinal))
            return root + "/" + relativePosixPath.TrimStart('/');
        return "file://" + Path.Combine(root, relativePosixPath.Replace("/", Path.DirectorySeparatorChar.ToString())).Replace("\\", "/");
    }

    void ApplyMarginsFromRect(RectTransform rt)
    {
        if (_webView == null || rt == null)
            return;

#if UNITY_EDITOR
        if (forceFullscreenInEditor)
        {
            _webView.SetMargins(0, 0, 0, 0);
            return;
        }
#endif

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

        // If we keep the native TMP composer visible, don't let the WebView cover it.
        if (_useNativeComposer && nativeComposerRect != null)
        {
            Canvas c2 = nativeComposerRect.GetComponentInParent<Canvas>();
            Camera cam2 = c2 != null && c2.renderMode != RenderMode.ScreenSpaceOverlay ? c2.worldCamera : null;
            Vector3[] cc = new Vector3[4];
            nativeComposerRect.GetWorldCorners(cc);
            float composerTopY = 0f;
            for (int i = 0; i < 4; i++)
            {
                Vector2 sp = RectTransformUtility.WorldToScreenPoint(cam2, cc[i]);
                composerTopY = Mathf.Max(composerTopY, sp.y);
            }

            int composerBottomMargin = Mathf.Clamp(Mathf.RoundToInt(composerTopY), 0, Screen.height);
            bottom = Mathf.Max(bottom, composerBottomMargin);
        }

        _webView.SetMargins(left, top, right, bottom);

#if UNITY_EDITOR
        // Avoid log spam: only print when margins change.
        if (left != _lastLogLeft || top != _lastLogTop || right != _lastLogRight || bottom != _lastLogBottom)
        {
            _lastLogLeft = left; _lastLogTop = top; _lastLogRight = right; _lastLogBottom = bottom;
            Debug.Log("[ChatWebViewBridge] Margins: L" + left + " T" + top + " R" + right + " B" + bottom + " target=" + rt.name + " rect=" + rt.rect.width + "x" + rt.rect.height);
        }
#endif
    }

    void SendInit(ChatWebInitPayload payload)
    {
        if (payload == null)
            return;
        if (payload.messages == null)
            payload.messages = Array.Empty<ChatMessage>();
        DispatchToPage(JsonUtility.ToJson(payload));
    }

    void DispatchToPage(string json)
    {
        if (_webView == null || string.IsNullOrEmpty(json))
            return;
        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        _webView.EvaluateJS("window.__chatFromUnityB64(\"" + b64 + "\");");
    }

    [Serializable]
    class ChatWebInitPayload
    {
        public string kind;
        public ChatMessage[] messages;
        public string roomName;
        public string roomId;
        public int memberCount;
        public string localDisplayName;
        public string localUserId;
        public bool mineMessagesOnRight;
        public string animalImageBase64;
        public bool useNativeComposer;
    }

    [Serializable]
    class ChatWebAppendPayload
    {
        public string kind;
        public ChatMessage message;
    }

    [Serializable]
    class ChatWebHeaderPayload
    {
        public string kind;
        public string roomName;
        public int memberCount;
    }

    [Serializable]
    class ChatWebClearReplyPayload
    {
        public string kind;
    }

    [Serializable]
    class WebViewToUnityMessage
    {
        public string type;
        public string text;
        public string replyToMessageId;
        public string replyToDisplayName;
        public string replyToMessagePreview;
        public string selectedMessageId;
        public string selectedUserName;
        public string selectedDisplayName;
        public string selectedMessageBody;
        public string roomId;
    }
}
