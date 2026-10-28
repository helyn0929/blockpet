using BlockPet.Hand;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.Feed
{
    /// <summary>
    /// 復活確認（愛心 0 顆時點狀態欄的「點我復活」）：顯示空間有幾瓶復活藥水；
    /// 有藥水就喝一瓶，愛心回到 5 顆滿（圖鑑紀錄不撤銷）；沒有就帶去商城的道具分頁買。
    /// 執行時自己建一個最上層的 Screen Space Overlay，390 設計寬度等比縮放。
    /// </summary>
    public class ReviveDialog : MonoBehaviour
    {
        const float DesignW = 390f;
        const float CardW = 320f;
        const float CardH = 268f;

        static readonly Color Ink = Hex(0x3A2E22);
        static readonly Color Cream = Hex(0xFFFCF4);
        static readonly Color Beige = Hex(0xF2E9D8);
        static readonly Color Yellow = Hex(0xF6C945);
        static readonly Color YellowEdge = Hex(0xD9A92C);
        static readonly Color Coral = Hex(0xE86B4F);
        static readonly Color Muted = Hex(0x8A7A66);

        static ReviveDialog _instance;

        TMP_FontAsset _font;
        RectTransform _root, _design;
        CanvasGroup _group;
        TMP_Text _title, _balance, _hint, _confirmText;
        CanvasGroup _confirmGroup;
        bool _busy, _open;
        float _t;
        int _potions = -1; // -1：查詢中
        PetHealthManager _health;

        public static void Show(TMP_FontAsset font, string petName)
        {
            if (_instance == null)
            {
                var go = new GameObject("ReviveDialog (runtime)", typeof(RectTransform));
                _instance = go.AddComponent<ReviveDialog>();
                _instance._font = font;
                _instance.Build();
            }
            _instance.Open(string.IsNullOrEmpty(petName) ? "寵物" : petName);
        }

        void Open(string petName)
        {
            _title.text = petName + " 昏倒了";
            _hint.text = "";
            _busy = false;
            _open = true;
            gameObject.SetActive(true);
            LoadPotions();
            Refresh();
        }

        void LoadPotions()
        {
            _potions = -1;
            var fb = FirebaseManager.Instance;
            if (fb == null) { _potions = 0; return; }
            fb.GetRoomShop(owned =>
            {
                if (this == null) return;
                _potions = owned != null && owned.TryGetValue(PetHearts.PotionId, out var n) ? n : 0;
            });
        }

        static int PotionPrice => BlockPet.UI.Market.MarketCatalog.Get(PetHearts.PotionId)?.price ?? 0;

        void Close() => _open = false;

        void Update()
        {
            float w = _root.rect.width;
            if (w > 0f) _design.localScale = Vector3.one * (w / DesignW);

            _t = Mathf.MoveTowards(_t, _open ? 1f : 0f, Time.unscaledDeltaTime / 0.2f);
            _group.alpha = _t;
            _group.blocksRaycasts = _open;
            if (!_open && _t <= 0f) { gameObject.SetActive(false); return; }

            // 已經被別人復活了就自動關
            if (_health == null) _health = FindObjectOfType<PetHealthManager>();
            if (_open && !_busy && _health != null && !_health.IsDead) Close();
            if (!_busy) Refresh();
        }

        void Refresh()
        {
            if (_potions < 0) _balance.text = "查看藥水中…";
            else if (_potions > 0) _balance.text = $"這個空間有 {_potions} 瓶復活藥水";
            else _balance.text = "這個空間還沒有復活藥水";
            _balance.color = _potions == 0 ? Coral : Muted;
            _confirmGroup.alpha = _busy || _potions < 0 ? 0.45f : 1f;
            _confirmText.text = _busy ? "處理中…" : _potions > 0 ? "喝一瓶藥水" : $"去商城買・{PotionPrice} 金幣";
        }

        void Confirm()
        {
            if (_busy || _potions < 0) return;
            if (_potions == 0)
            {
                // 沒有藥水：去商城的道具分頁
                Close();
                BlockPet.UI.Market.MarketScreenController.OpenAt(PetHearts.PotionId);
                return;
            }
            if (_health == null) _health = FindObjectOfType<PetHealthManager>();
            if (_health == null) return;
            _busy = true;
            Refresh();
            _health.Revive((ok, reason) =>
            {
                _busy = false;
                if (this == null) return;
                if (ok || reason == "alive") { Close(); return; }
                _hint.text = reason == "potion" ? "藥水剛好被用完了" : "復活失敗，請再試一次";
                LoadPotions();
                Refresh();
            });
        }

        // ─── 介面 ──────────────────────────────────────────────────────

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            gameObject.AddComponent<CanvasScaler>();
            gameObject.AddComponent<GraphicRaycaster>();
            _group = gameObject.AddComponent<CanvasGroup>();
            _root = (RectTransform)transform;

            var dim = new GameObject("Dim", typeof(RectTransform)).GetComponent<RectTransform>();
            dim.SetParent(_root, false);
            dim.anchorMin = Vector2.zero;
            dim.anchorMax = Vector2.one;
            dim.offsetMin = dim.offsetMax = Vector2.zero;
            dim.gameObject.AddComponent<Image>().color = new Color(20f / 255f, 24f / 255f, 14f / 255f, 0.55f);
            dim.gameObject.AddComponent<Button>().onClick.AddListener(() => { if (!_busy) Close(); });

            // 卡片置中
            _design = new GameObject("Design", typeof(RectTransform)).GetComponent<RectTransform>();
            _design.SetParent(_root, false);
            _design.anchorMin = _design.anchorMax = new Vector2(0.5f, 0.5f);
            _design.pivot = new Vector2(0.5f, 0.5f);
            _design.sizeDelta = new Vector2(CardW, CardH);

            UIShapes.AddShadow(_design, new Color(0, 0, 0, 0.3f), 24, 30, 10);
            var card = Place(_design, "Card", 0, 0, CardW, CardH);
            UIShapes.AddRounded(card.gameObject, Cream, 24);

            _title = Label(card, 20, 26, CardW - 40f, 30, "", 21, Ink, TextAlignmentOptions.Center);
            Label(card, 24, 66, CardW - 48f, 48, "愛心歸零了。喝一瓶復活藥水就會醒來，愛心回到 5 顆滿。", 14, Ink, TextAlignmentOptions.Center);
            _balance = Label(card, 20, 124, CardW - 40f, 20, "", 13, Muted, TextAlignmentOptions.Center);
            _hint = Label(card, 20, 146, CardW - 40f, 18, "", 11, Coral, TextAlignmentOptions.Center);

            float bw = (CardW - 40f - 10f) / 2f;
            var cancel = Place(card, "Cancel", 20, 186, bw, 54);
            var cbg = UIShapes.AddRounded(cancel.gameObject, Beige, 16);
            Label(cancel, 0, 0, bw, 54, "先不要", 15, Ink, TextAlignmentOptions.Center);
            var cb = cancel.gameObject.AddComponent<Button>();
            cb.targetGraphic = cbg;
            cb.onClick.AddListener(() => { if (!_busy) Close(); });

            var confirm = Place(card, "Confirm", 20 + bw + 10f, 186, bw, 54);
            _confirmGroup = confirm.gameObject.AddComponent<CanvasGroup>();
            UIShapes.AddRounded(Place(confirm, "Edge", 0, 3, bw, 54).gameObject, YellowEdge, 16).raycastTarget = false;
            var ybg = UIShapes.AddRounded(Place(confirm, "Fill", 0, 0, bw, 54).gameObject, Yellow, 16);
            _confirmText = Label(confirm, 0, 0, bw, 54, "", 15, Ink, TextAlignmentOptions.Center);
            var yb = confirm.gameObject.AddComponent<Button>();
            yb.targetGraphic = ybg;
            yb.onClick.AddListener(Confirm);
        }

        static RectTransform Place(Transform parent, string name, float left, float top, float w, float h)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(left, -top);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        TMP_Text Label(RectTransform parent, float left, float top, float w, float h, string value, float size, Color color, TextAlignmentOptions align)
        {
            var t = Place(parent, "Text", left, top, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.text = value;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.enableWordWrapping = true;
            t.raycastTarget = false;
            return t;
        }

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
