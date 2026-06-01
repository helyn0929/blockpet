using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Hosts the React Market UI in a WebView.
/// Sends catalog + currency on ready; handles buy/equip/tryOn from JS.
/// </summary>
[DefaultExecutionOrder(-85)]
public class MarketWebViewBridge : MonoBehaviour
{
    [SerializeField] PageManager pageManager;
    [SerializeField] RectTransform webLayoutTarget;
    [SerializeField] string streamingAssetsRelativePath = "game-ui/index.html";
    [SerializeField] bool updateMarginsEachFrame = true;

    WebViewObject _webView;
    bool _pageReady;
    bool _shouldBeVisible;
    bool _initStarted;

    // Catalog is built fresh from MarketSampleData on each open.
    List<ShopItemData> _catalog;

    void Awake()
    {
        if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
        if (webLayoutTarget == null) webLayoutTarget = transform as RectTransform;
    }

    void OnEnable()
    {
        _shouldBeVisible = true;
        if (_webView != null) _webView.SetVisibility(true);
#if !UNITY_EDITOR
        if (!_initStarted)
        {
            _initStarted = true;
            StartCoroutine(InitWebViewCoroutine());
        }
        else
        {
            // Already loaded — re-send catalog so data is fresh each open.
            if (_pageReady) SendMarketInit();
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

    // ── WebView init ──────────────────────────────────────────────────────────

    IEnumerator InitWebViewCoroutine()
    {
        var go = new GameObject("MarketWebViewObject");
        go.transform.SetParent(null, false);
        _webView = go.AddComponent<WebViewObject>();
        _webView.Init(
            cb:      OnJsFromPage,
            err:     m => Debug.LogWarning("[MarketWebViewBridge] error: " + m),
            httpErr: m => Debug.LogWarning("[MarketWebViewBridge] http error: " + m),
            started: u => Debug.Log("[MarketWebViewBridge] started: " + u),
            hooked:  _ => { },
            cookies: _ => { },
            ld:      u => { Debug.Log("[MarketWebViewBridge] loaded: " + u); OnWebLoaded(); }
        );

        while (!_webView.IsInitialized()) yield return null;

        _webView.SetMargins(0, 0, 0, 0);
        _webView.SetVisibility(_shouldBeVisible);
        yield return StartCoroutine(LoadFromStreamingAssets());
        if (webLayoutTarget != null) ApplyMarginsFromRect(webLayoutTarget);
    }

    void OnWebLoaded()
    {
        const string bridgeJs = @"
window.__chatFromUnityB64 = function(b64) {
  try {
    var binary = atob(b64);
    var bytes = new Uint8Array(binary.length);
    for (var i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    var json = (typeof TextDecoder !== 'undefined')
      ? new TextDecoder('utf-8').decode(bytes)
      : decodeURIComponent(escape(String.fromCharCode.apply(null, bytes)));
    var detail = JSON.parse(json);
    window.dispatchEvent(new CustomEvent('blockpet-chat', { detail: detail }));
  } catch(e) { console.log('__chatFromUnityB64 error', e); }
};
";
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS
        var poly = @"
if (!window.Unity || typeof window.Unity.call !== 'function') {
  window.Unity = { call: function(msg) { window.location = 'unity:' + msg; } };
}
";
#else
        const string poly = "";
#endif
        _webView.EvaluateJS(poly + bridgeJs);
        _pageReady = false;
    }

    // ── Catalog serialisation ─────────────────────────────────────────────────

    [Serializable]
    class MarketItemDto
    {
        public string id;
        public string name;
        public string category;
        public string section;
        public int price;
        public int gemPrice;
        public bool isOwned;
        public bool isEquipped;
        public bool isLocked;
        public string iconBase64; // empty when no sprite assigned
    }

    [Serializable]
    class MarketInitPayload
    {
        public string kind;           // "market"
        public MarketItemDto[] items;
        public int coins;
        public int gems;
        public string equippedPetId;
    }

    void SendMarketInit()
    {
        if (_webView == null) return;

        _catalog = MarketSampleData.CreateSampleCatalog();
        MarketSampleData.ApplyPersistenceFlags(_catalog);

        var dtos = new List<MarketItemDto>();
        foreach (var item in _catalog)
        {
            if (item.category == MarketCategory.Money) continue;
            dtos.Add(new MarketItemDto
            {
                id        = item.id,
                name      = item.itemName,
                category  = item.category.ToString(),
                section   = item.section ?? "",
                price     = item.price,
                gemPrice  = item.gemPrice,
                isOwned   = item.isOwned,
                isEquipped = item.isEquipped,
                isLocked  = item.isLocked,
                iconBase64 = EncodeSprite(item.icon)
            });
        }

        int coins = EconomyManager.Instance != null ? EconomyManager.Instance.CurrentMoney : 0;
        int gems  = MarketWallet.Gems;

        var payload = new MarketInitPayload
        {
            kind          = "market",
            items         = dtos.ToArray(),
            coins         = coins,
            gems          = gems,
            equippedPetId = MarketInventoryStore.GetEquippedPetId()
        };

        DispatchToPage(JsonUtility.ToJson(payload));
    }

    static string EncodeSprite(Sprite sprite)
    {
        if (sprite == null) return "";
        try
        {
            Texture2D tex = sprite.texture;
            if (!tex.isReadable) return "";
            byte[] jpg = tex.EncodeToJPG(75);
            return Convert.ToBase64String(jpg);
        }
        catch { return ""; }
    }

    // ── JS → Unity messages ───────────────────────────────────────────────────

    [Serializable]
    class MarketMessage
    {
        public string type;
        public string itemId;
    }

    void OnJsFromPage(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return;
        MarketMessage dto = null;
        try { dto = JsonUtility.FromJson<MarketMessage>(msg); } catch { }
        if (dto == null || string.IsNullOrEmpty(dto.type)) return;

        switch (dto.type)
        {
            case "ready":
                _pageReady = true;
                SendMarketInit();
                break;

            case "buyItem":
                HandleBuy(dto.itemId);
                break;

            case "equipItem":
                HandleEquip(dto.itemId);
                break;

            case "back":
                if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
                pageManager?.ShowHomePage();
                break;

            case "refreshMarket":
                SendMarketInit();
                break;
        }
    }

    void HandleBuy(string itemId)
    {
        if (_catalog == null || string.IsNullOrEmpty(itemId)) return;
        var item = _catalog.Find(x => x.id == itemId);
        if (item == null || item.isOwned || item.isLocked) return;

        // Deduct currency.
        if (item.price > 0)
        {
            if (EconomyManager.Instance == null || !EconomyManager.Instance.TrySpend(item.price)) return;
        }
        if (item.gemPrice > 0)
        {
            if (!MarketWallet.TrySpendGems(item.gemPrice))
            {
                if (item.price > 0) EconomyManager.Instance?.AddCoins(item.price);
                return;
            }
        }

        MarketInventoryStore.SetOwned(item.id);
        item.isOwned = true;

        // Auto-equip on first purchase.
        DoEquip(item);

        SendMarketInit(); // push updated state back to page
    }

    void HandleEquip(string itemId)
    {
        if (_catalog == null || string.IsNullOrEmpty(itemId)) return;
        var item = _catalog.Find(x => x.id == itemId);
        if (item == null || !item.isOwned) return;
        DoEquip(item);
        SendMarketInit();
    }

    void DoEquip(ShopItemData item)
    {
        switch (item.category)
        {
            case MarketCategory.Pets:        MarketInventoryStore.SetEquippedPet(item.id);        break;
            case MarketCategory.Backgrounds: MarketInventoryStore.SetEquippedBackground(item.id); break;
            case MarketCategory.Spaces:      MarketInventoryStore.SetEquippedSpace(item.id);      break;
            case MarketCategory.Furnitures:  MarketInventoryStore.SetEquippedFurniture(item.id);  break;
            case MarketCategory.Accessories: MarketInventoryStore.AddEquippedAccessory(item.id);  break;
        }
        MarketSampleData.ApplyPersistenceFlags(_catalog);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void DispatchToPage(string json)
    {
        if (_webView == null || string.IsNullOrEmpty(json)) return;
        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        _webView.EvaluateJS("window.__chatFromUnityB64(\"" + b64 + "\");");
    }

    IEnumerator LoadFromStreamingAssets()
    {
        string rel = streamingAssetsRelativePath.Replace(" ", "%20");
        string baseDir = Path.GetDirectoryName(rel)?.Replace("\\", "/") ?? "";
        string indexFileName = Path.GetFileName(rel);
        string dstRoot = Path.Combine(Application.temporaryCachePath, "game-ui-market");
        string dstIndex = Path.Combine(dstRoot, indexFileName);
        const string screenParam = "?screen=market";

        if (Application.streamingAssetsPath.Contains("://", StringComparison.Ordinal))
        {
            yield return StartCoroutine(CopyViaManifestAndroid(baseDir, indexFileName, dstRoot, dstIndex, screenParam));
            yield break;
        }

        string srcDir = Path.Combine(Application.streamingAssetsPath,
            baseDir.TrimEnd('/').Replace("/", Path.DirectorySeparatorChar.ToString()));

        try
        {
            if (Directory.Exists(dstRoot)) Directory.Delete(dstRoot, true);
            Directory.CreateDirectory(dstRoot);
            CopyFolder(srcDir, dstRoot);
        }
        catch (Exception e) { Debug.LogError("[MarketWebViewBridge] Copy failed: " + e.Message); }

        if (File.Exists(dstIndex))
        {
            string url = "file://" + dstIndex.Replace(" ", "%20") + screenParam;
            Debug.Log("[MarketWebViewBridge] LoadURL: " + url);
            _webView.LoadURL(url);
        }
        else Debug.LogError("[MarketWebViewBridge] index.html missing after copy.");
    }

    IEnumerator CopyViaManifestAndroid(string baseDir, string indexFileName, string dstRoot, string dstIndex, string screenParam)
    {
        try { if (Directory.Exists(dstRoot)) Directory.Delete(dstRoot, true); Directory.CreateDirectory(dstRoot); }
        catch (Exception e) { Debug.LogError("[MarketWebViewBridge] " + e.Message); }

        if (!baseDir.EndsWith("/")) baseDir += "/";
        string manifestUrl = CombineStreamingUrl(baseDir + "manifest.txt");

        using (var req = UnityWebRequest.Get(manifestUrl))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) { Debug.LogError("[MarketWebViewBridge] manifest.txt missing."); yield break; }
            foreach (var line in req.downloadHandler.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim().Replace("\\", "/");
                using (var getFile = UnityWebRequest.Get(CombineStreamingUrl(baseDir + trimmed)))
                {
                    yield return getFile.SendWebRequest();
                    if (getFile.result != UnityWebRequest.Result.Success) continue;
                    string dst = Path.Combine(dstRoot, trimmed.Replace("/", Path.DirectorySeparatorChar.ToString()));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst) ?? dstRoot);
                    File.WriteAllBytes(dst, getFile.downloadHandler.data);
                }
            }
        }

        if (File.Exists(dstIndex))
            _webView.LoadURL("file://" + dstIndex.Replace(" ", "%20") + screenParam);
        else
            Debug.LogError("[MarketWebViewBridge] index.html missing after Android copy.");
    }

    static string CombineStreamingUrl(string rel)
    {
        string root = Application.streamingAssetsPath;
        return root.Contains("://", StringComparison.Ordinal)
            ? root + "/" + rel.TrimStart('/')
            : "file://" + Path.Combine(root, rel.Replace("/", Path.DirectorySeparatorChar.ToString())).Replace("\\", "/");
    }

    static void CopyFolder(string srcDir, string dstDir)
    {
        if (!Directory.Exists(srcDir)) return;
        foreach (string file in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            string rel = file.Substring(srcDir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
            string dst = Path.Combine(dstDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst) ?? dstDir);
            File.Copy(file, dst, true);
        }
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
            minX = Mathf.Min(minX, sp.x); maxX = Mathf.Max(maxX, sp.x);
            minY = Mathf.Min(minY, sp.y); maxY = Mathf.Max(maxY, sp.y);
        }
        _webView.SetMargins(
            Mathf.RoundToInt(minX),
            Mathf.RoundToInt(Screen.height - maxY),
            Mathf.RoundToInt(Screen.width - maxX),
            Mathf.RoundToInt(minY));
    }
}
