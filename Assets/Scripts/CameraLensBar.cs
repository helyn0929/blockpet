using BlockPet.Hand;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 相機的鏡頭列（設計文件 5.27 ①；設計稿還沒畫）：快門正上方約 16、置中一排。
/// 半透明深色膠囊 rgba(27,23,18,0.45)、圓角；左到右：魚眼開關｜分隔線｜1×、2×、3×。
/// - 魚眼：圓形鏡頭圖示 32；關＝白色線條；開＝底 #F6C945、圖示 #3A2E22。
/// - 縮放：小圓 32、12 Black 白字；選到的放大到 36、白底、字 #3A2E22；兩指縮放時選到的那顆顯示目前倍率（例「1.6×」）。
/// 只在相機預覽時出現（拍好、送出前的畫面不顯示）。由 CameraController 自動掛上，介面在執行時建立。
/// </summary>
public class CameraLensBar : MonoBehaviour
{
    const float DesignW = 390f;
    const float Pad = 4f, Btn = 32f, BtnOn = 36f, Gap = 8f, ZoomSlot = 38f;
    static readonly float[] Presets = { 1f, 2f, 3f };
    static readonly Color Ink = new Color(0x3A / 255f, 0x2E / 255f, 0x22 / 255f);
    static readonly Color Yellow = new Color(0xF6 / 255f, 0xC9 / 255f, 0x45 / 255f);

    CameraController _cam;
    CameraUIManager _ui;
    Canvas _canvas;
    RectTransform _root, _bar;
    Image _fishBg, _fishRing, _fishDot;
    readonly Image[] _zoomBg = new Image[3];
    readonly TMP_Text[] _zoomText = new TMP_Text[3];
    readonly RectTransform[] _zoomRt = new RectTransform[3];
    TMP_FontAsset _font;
    string _shown;

    void Awake()
    {
        _cam = GetComponent<CameraController>();
        var skin = BlockPet.UI.Polaroid.PolaroidSkin.Load();
        _font = skin != null ? skin.font : null;
        Build();
    }

    void OnDestroy()
    {
        if (_canvas != null) Destroy(_canvas.gameObject);
    }

    void LateUpdate()
    {
        if (_ui == null) _ui = FindObjectOfType<CameraUIManager>(true);
        bool show = _cam != null && _ui != null && _ui.CurrentMode == UIMode.CameraPreview && _cam.CurrentState == CameraState.Preview;
        if (_canvas.gameObject.activeSelf != show) _canvas.gameObject.SetActive(show);
        if (!show) return;
        Place();
        Refresh();
    }

    /// <summary>放在快門正上方 16（設計單位，依螢幕寬等比）。</summary>
    void Place()
    {
        float k = _root.rect.width / DesignW;
        _bar.localScale = Vector3.one * k;
        var shutter = _ui.ShutterRect;
        float bottom = 120f * k; // 找不到快門時的位置
        if (shutter != null)
        {
            var corners = new Vector3[4];
            shutter.GetWorldCorners(corners);
            var sc = shutter.GetComponentInParent<Canvas>()?.rootCanvas;
            var cam = sc != null && sc.renderMode != RenderMode.ScreenSpaceOverlay ? sc.worldCamera : null;
            Vector2 top = RectTransformUtility.WorldToScreenPoint(cam, corners[1]);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, top, null, out var local);
            bottom = local.y - _root.rect.yMin + 16f * k;
        }
        _bar.anchoredPosition = new Vector2(0f, bottom);
    }

    void Refresh()
    {
        float zoom = _cam.Zoom;
        int sel = 0;
        for (int i = 0; i < Presets.Length; i++) if (zoom >= Presets[i] - 0.01f) sel = i;
        bool exact = Mathf.Abs(zoom - Presets[sel]) < 0.05f;
        string key = (_cam.Fisheye ? "F" : "f") + sel + (exact ? "" : zoom.ToString("0.0"));
        if (key == _shown) return;
        _shown = key;

        // 魚眼開關
        bool on = _cam.Fisheye;
        _fishBg.enabled = on;
        _fishRing.color = on ? Ink : Color.white;
        _fishDot.color = on ? Ink : Color.white;

        for (int i = 0; i < 3; i++)
        {
            bool s = i == sel;
            float size = s ? BtnOn : Btn;
            _zoomRt[i].sizeDelta = new Vector2(size, size);
            _zoomBg[i].color = s ? Color.white : new Color(1f, 1f, 1f, 0.14f);
            _zoomText[i].color = s ? Ink : Color.white;
            // 兩指縮放時，選到的那顆改顯示目前倍率
            _zoomText[i].text = s && !exact ? zoom.ToString("0.0") + "×" : Presets[i] + "×";
            _zoomText[i].fontSize = s && !exact ? 11f : 12f;
        }
    }

    // ─── 建立介面 ──────────────────────────────────────────────────

    void Build()
    {
        var go = new GameObject("CameraLensBar (runtime)", typeof(RectTransform));
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 11; // 相機畫面（場景 Canvas 0、狀態欄 10）之上
        go.AddComponent<GraphicRaycaster>();
        _root = (RectTransform)go.transform;

        float w = Pad + Btn + Gap + 1f + Gap + ZoomSlot * 3f + Pad;
        float h = BtnOn + Pad * 2f;
        _bar = new GameObject("Bar", typeof(RectTransform)).GetComponent<RectTransform>();
        _bar.SetParent(_root, false);
        _bar.anchorMin = _bar.anchorMax = new Vector2(0.5f, 0f);
        _bar.pivot = new Vector2(0.5f, 0f);
        _bar.sizeDelta = new Vector2(w, h);
        // 半透明深色膠囊（毛玻璃之後再加）
        UIShapes.AddRounded(Stretch(_bar, "Bg").gameObject, new Color(27f / 255f, 23f / 255f, 18f / 255f, 0.45f), h / 2f).raycastTarget = true;

        // 魚眼開關：圓形鏡頭圖示（外圈＋中間小圓）
        var fish = Box(_bar, "Fisheye", Pad + Btn / 2f, Btn, Btn);
        _fishBg = UIShapes.AddRounded(Stretch(fish, "On").gameObject, Yellow, Btn / 2f);
        _fishRing = UIShapes.AddThinRing(Box(fish, "Lens", 0f, 22f, 22f, centerInParent: true).gameObject, Color.white, 11f);
        _fishDot = UIShapes.AddRounded(Box(fish, "Dot", 0f, 8f, 8f, centerInParent: true).gameObject, Color.white, 4f);
        _fishDot.raycastTarget = false;
        var hit = fish.gameObject.AddComponent<Image>();
        hit.color = new Color(0, 0, 0, 0);
        Button(fish, hit, () => { if (_cam != null) _cam.Fisheye = !_cam.Fisheye; _shown = null; });

        // 分隔線
        var line = Box(_bar, "Divider", Pad + Btn + Gap + 0.5f, 1f, 18f).gameObject.AddComponent<Image>();
        line.color = new Color(1f, 1f, 1f, 0.35f);
        line.raycastTarget = false;

        // 1× 2× 3×
        float x0 = Pad + Btn + Gap + 1f + Gap;
        for (int i = 0; i < 3; i++)
        {
            float z = Presets[i];
            var rt = Box(_bar, "Zoom" + z, x0 + ZoomSlot * i + ZoomSlot / 2f, Btn, Btn);
            _zoomRt[i] = rt;
            _zoomBg[i] = UIShapes.AddRounded(rt.gameObject, Color.white, BtnOn / 2f);
            var t = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            t.rectTransform.SetParent(rt, false);
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
            if (_font != null) t.font = _font;
            t.fontSize = 12f;
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            _zoomText[i] = t;
            Button(rt, _zoomBg[i], () => { if (_cam != null) _cam.SetZoom(z); _shown = null; });
        }
        go.SetActive(false);
    }

    /// <summary>以中心放：cx＝中心 x（左到右），垂直置中在膠囊裡。</summary>
    static RectTransform Box(RectTransform parent, string name, float cx, float w, float h, bool centerInParent = false)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = centerInParent ? new Vector2(0.5f, 0.5f) : new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = centerInParent ? Vector2.zero : new Vector2(cx, 0f);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    static RectTransform Stretch(Transform parent, string name)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    static void Button(RectTransform rt, Graphic target, UnityEngine.Events.UnityAction onClick)
    {
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = target;
        b.transition = Selectable.Transition.None;
        b.onClick.AddListener(onClick);
    }
}
