using System.Collections.Generic;
using BlockPet.Decoration;
using BlockPet.Hand;
using BlockPet.UI.Home;
using BlockPet.UI.Room;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlockPet.UI.Market
{
    /// <summary>
    /// 商店（設計稿「商店 A 營地試擺」MarketA.dc.html、A-2 寵物蛋、A-3 裝飾）：
    /// - 上方 330：營地預覽。家具／環境放進虛線框「試擺」（可拖曳），寵物在旁邊說喜不喜歡；
    ///   裝飾戴在寵物頭上看看；寵物蛋放在窩裡搖。
    /// - 頂部：返回、「商店」、空間金幣（全空間共用）。
    /// - 下方面板：家具／裝飾／環境／寵物蛋／道具五個分頁，三欄商品卡；最下方購買列。
    ///   道具（復活藥水）可以一直買，用掉就少一瓶；寵物昏倒時在這裡或復活確認窗喝。
    /// - 全部用空間金幣買（FirebaseManager.SpendRoomCoins），買過的記在 Rooms/{roomId}/shop。
    ///   家具買過後「去擺放」→ 回首頁進佈置模式直接開始擺。裝飾不用買（寵物從照片撿，等照片辨識接上才有進度）。
    /// 介面在執行時以 390×772 設計座標建立：預覽與頂部貼齊上方，面板延伸到底部，依螢幕寬度等比縮放。
    /// 由「BlockPet/Build Market Screen」掛到 MarketPage，並關掉舊的商店頁。
    /// </summary>
    public class MarketScreenController : MonoBehaviour
    {
        const float DesignW = 390f;
        const float PreviewH = 330f;
        const float PanelTop = 300f;
        const float BuyBarH = 88f;
        const float GridGap = 10f;
        const float PadX = 16f;
        const float CardW = (DesignW - PadX * 2f - GridGap * 2f) / 3f;

        static readonly Color Ink = Hex(0x3A2E22);
        static readonly Color Paper = Hex(0xFFF8EC);
        static readonly Color Card = Hex(0xFFFCF4);
        static readonly Color Beige = Hex(0xF2E9D8);
        static readonly Color Line = Hex(0xE2D6C0);
        static readonly Color Divider = Hex(0xEFE6D4);
        static readonly Color Muted = Hex(0x6B5D4E);
        static readonly Color Muted2 = Hex(0x8A6A4E);
        static readonly Color Yellow = Hex(0xF6C945);
        static readonly Color YellowEdge = Hex(0xD9A92C);
        static readonly Color Green = Hex(0x6FA45F);
        static readonly Color GreenEdge = Hex(0x4A7A3F);
        static readonly Color Coral = Hex(0xE86B4F);
        static readonly Color CoralEdge = Hex(0xB44A33);
        static readonly Color Grass = Hex(0x8FA868);

        [Header("Style")]
        [SerializeField] HandSkin skin;
        [Tooltip("營地圖（預覽背景）")]
        [SerializeField] Texture background;
        [SerializeField] Sprite[] petSprites;
        [Tooltip("Assets/Art/UI/Market/Sprites 全部（依檔名找）")]
        [SerializeField] List<Sprite> sprites = new List<Sprite>();
        [SerializeField] int sortingOrder = 32;

        [Header("Links")]
        [SerializeField] PageManager pageManager;
        [Tooltip("商店家具的 DecorationItem（itemId 與商品 id 相同）")]
        [SerializeField] DecorationDatabase decorationDatabase;
        [SerializeField] RoomEditorController roomEditor;

        class CardView
        {
            public MarketEntry entry;
            public RectTransform rt;
            public Image ring;
        }

        // 狀態
        MarketTab _tab = MarketTab.Furniture;
        readonly Dictionary<MarketTab, int> _sel = new Dictionary<MarketTab, int>
        {
            { MarketTab.Furniture, 0 }, { MarketTab.Decor, 0 }, { MarketTab.Environment, 0 }, { MarketTab.Egg, 0 }, { MarketTab.Item, 0 },
        };
        Dictionary<string, int> _owned = new Dictionary<string, int>();
        bool _busy;
        int _coins = int.MinValue;
        float _nextPoll;
        float _popT;

        // 版面
        Canvas _canvas;
        RectTransform _root, _top, _panel, _viewport, _content, _buyBar;
        float _lastW = -1f, _lastH = -1f;
        Rect _lastSafe;
        bool _dirty = true;
        float _k = 1f;

        // 預覽
        RectTransform _placeLayer, _decorLayer, _eggLayer;
        RectTransform _placePet, _decorPet, _eggImgRt;
        GameObject _eggNest;
        Image _placePetImg, _decorPetImg, _tryImg, _decorHeadImg, _eggImg;
        RectTransform _tryBox;
        Image _tryDash;
        TMP_Text _tryLabel;
        RectTransform _bubble;
        TMP_Text _bubbleText;
        TMP_Text _previewNote;

        // 頂部
        TMP_Text _coinText;
        RectTransform _coinPill;
        TMP_Text _sharedNote;

        // 面板
        readonly List<GameObject> _tabOn = new List<GameObject>();
        readonly List<TMP_Text> _tabTexts = new List<TMP_Text>();
        TMP_Text _section, _sectionRight;
        RectTransform _grid;
        RectTransform _footNote;
        TMP_Text _footText;
        readonly List<CardView> _cards = new List<CardView>();

        // 購買列
        TMP_Text _buyName, _buyDesc;
        RectTransform _btnMain, _btnSecond;
        Image _btnMainBg, _btnMainEdge, _btnSecondBg, _btnSecondBorder;
        TMP_Text _btnMainText, _btnSecondText;
        Image _btnMainCoin, _btnSecondCoin;
        Button _btnMainButton, _btnSecondButton;

        // 提示
        RectTransform _toast;
        CanvasGroup _toastGroup;
        TMP_Text _toastText;
        float _toastUntil;

        // ─── Lifecycle ─────────────────────────────────────────────────

        void Awake()
        {
            if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
            if (roomEditor == null) roomEditor = FindObjectOfType<RoomEditorController>(true);
            HideOldMarket();
            Build();
        }

        /// <summary>
        /// 舊商店頁的介面（TopBar、PetPreviewPanel、CategoryTabBar、ShopScrollView…）還掛在 MarketPage 底下，
        /// 舊的腳本雖然關掉了，畫面元件還是開著，新版沒蓋滿時就會露出來。新版是自己的 Canvas，不在這底下，全部關掉即可（不刪除）。
        /// </summary>
        void HideOldMarket()
        {
            foreach (Transform child in transform)
                if (child.gameObject.activeSelf) child.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(true);
            _busy = false;
            _coins = int.MinValue;
            _toastGroup.alpha = 0f;
            RefreshSharedNote();
            ApplyPendingItem();
            LoadOwned();
            ShowTab(_tab);
        }

        void OnDisable()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            Relayout();

            if (Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + 0.25f;
                int coins = EconomyManager.Instance != null ? EconomyManager.Instance.CurrentMoney : 0;
                if (coins != _coins)
                {
                    _coins = coins;
                    _coinText.text = coins.ToString("N0");
                    float w = _coinText.GetPreferredValues(_coinText.text).x + 6f + 16f + 5f + 10f;
                    _coinPill.sizeDelta = new Vector2(w, 30f);
                    _coinPill.anchoredPosition = new Vector2(DesignW - 12f - w, _coinPill.anchoredPosition.y);
                }
            }

            // 寵物上下跳（mBob 2.4s 8px）、蛋左右搖（mWob ±6°）、新選的東西彈出（mPop 0.3s）、虛線框閃（mDash 1.6s）
            float t = Time.unscaledTime;
            float bob = 8f * (0.5f - 0.5f * Mathf.Cos(t / 2.4f * Mathf.PI * 2f));
            _placePet.anchoredPosition = new Vector2(52f, -128f + bob);
            _decorPet.anchoredPosition = new Vector2(116f, -70f + bob);
            _eggImgRt.localRotation = Quaternion.Euler(0f, 0f, 6f * Mathf.Sin(t / 2.4f * Mathf.PI * 2f));
            _popT = Mathf.Min(1f, _popT + dt / 0.3f);
            float pop = Mathf.Lerp(0.6f, 1f, 1f - (1f - _popT) * (1f - _popT));
            _tryImg.rectTransform.localScale = Vector3.one * pop;
            _decorHeadImg.rectTransform.localScale = Vector3.one * pop;
            _bubble.localScale = Vector3.one * pop;
            _tryDash.color = new Color(1f, 1f, 1f, Mathf.Lerp(1f, 0.45f, 0.5f - 0.5f * Mathf.Cos(t / 1.6f * Mathf.PI * 2f)));

            _toastGroup.alpha = Mathf.MoveTowards(_toastGroup.alpha, Time.unscaledTime < _toastUntil ? 1f : 0f, dt / 0.2f);
        }

        // ─── 資料 ──────────────────────────────────────────────────────

        void LoadOwned()
        {
            var fb = FirebaseManager.Instance;
            if (fb == null) return;
            fb.GetRoomShop(owned =>
            {
                if (this == null) return;
                _owned = owned ?? new Dictionary<string, int>();
                ShowTab(_tab);
            });
        }

        void RefreshSharedNote()
        {
            var fb = FirebaseManager.Instance;
            _sharedNote.text = "金幣是這個空間共用的";
            if (fb == null || string.IsNullOrEmpty(fb.RoomId)) return;
            fb.GetRoomMeta(fb.RoomId, meta =>
            {
                if (this == null || meta == null || string.IsNullOrEmpty(meta.name)) return;
                _sharedNote.text = "金幣是「" + meta.name + "」共用的";
            });
        }

        bool Owned(MarketEntry e) => e != null && _owned.TryGetValue(e.id, out var n) && n > 0;
        int OwnedCount(string id) => _owned.TryGetValue(id, out var n) ? n : 0;
        bool Locked(MarketEntry e) => e != null && !string.IsNullOrEmpty(e.requires) && !_owned.ContainsKey(e.requires);

        /// <summary>裝飾的進度：要等照片辨識（第 3 步）記錄每一類餵了幾張，目前都是 0。</summary>
        static int DecorHave(MarketEntry e) => 0;

        MarketEntry Current
        {
            get
            {
                var list = MarketCatalog.InTab(_tab);
                if (list.Count == 0) return null;
                return list[Mathf.Clamp(_sel[_tab], 0, list.Count - 1)];
            }
        }

        // ─── 分頁與商品 ────────────────────────────────────────────────

        void ShowTab(MarketTab tab)
        {
            _tab = tab;
            for (int i = 0; i < _tabOn.Count; i++)
            {
                bool on = i == (int)tab;
                _tabOn[i].SetActive(on);
                _tabTexts[i].color = on ? Ink : Muted2;
            }

            foreach (var c in _cards) Destroy(c.rt.gameObject);
            _cards.Clear();
            var list = MarketCatalog.InTab(tab);
            for (int i = 0; i < list.Count; i++) _cards.Add(BuildCard(list[i], i));

            int slots = MarketCatalog.Slots(_owned);
            int placed = DecorationManager.Instance != null ? DecorationManager.Instance.PlacedCount : 0;
            switch (tab)
            {
                case MarketTab.Furniture:
                    _section.text = "營地家具";
                    _sectionRight.text = $"營地空位 {Mathf.Max(0, slots - placed)} / {slots}";
                    break;
                case MarketTab.Decor:
                    int got = 0;
                    foreach (var e in list) if (DecorHave(e) >= e.need) got++;
                    _section.text = PetName() + " 撿到的裝飾";
                    _sectionRight.text = $"已撿到 {got} / {list.Count}";
                    break;
                case MarketTab.Environment:
                    _section.text = "營地環境";
                    _sectionRight.text = "新環境可以放更多家具";
                    break;
                case MarketTab.Item:
                    _section.text = "道具";
                    _sectionRight.text = $"這個空間有 {OwnedCount(MarketCatalog.RevivePotion)} 瓶復活藥水";
                    break;
                default:
                    _section.text = "寵物蛋";
                    _sectionRight.text = $"1 顆 {list[0].price:N0} 金幣";
                    break;
            }

            if (tab == MarketTab.Decor)
            {
                _footText.text = "裝飾不用買：寵物會從你餵的照片裡「撿」東西戴在頭上。餵越多同一種照片，越快撿到。";
                _footNote.gameObject.SetActive(true);
            }
            else if (tab == MarketTab.Egg)
            {
                var have = new List<string>();
                foreach (var e in list) if (OwnedCount(e.id) > 0) have.Add($"{e.name} {OwnedCount(e.id)} 顆");
                _footText.text = "・買到的蛋要餵照片才會孵化，進化方向看你餵什麼\n・這個空間有：" + (have.Count > 0 ? string.Join("、", have) : "還沒有蛋");
                _footNote.gameObject.SetActive(true);
            }
            else if (tab == MarketTab.Item)
            {
                _footText.text = "・寵物愛心歸零會昏倒，喝一瓶復活藥水就醒來、愛心回到 5 顆滿\n・藥水是這個空間共用的，誰都可以讓寵物喝";
                _footNote.gameObject.SetActive(true);
            }
            else _footNote.gameObject.SetActive(false);

            _dirty = true;
            Select(_sel[tab]);
        }

        void Select(int index)
        {
            var list = MarketCatalog.InTab(_tab);
            if (list.Count == 0) return;
            index = Mathf.Clamp(index, 0, list.Count - 1);
            _sel[_tab] = index;
            for (int i = 0; i < _cards.Count; i++) _cards[i].ring.enabled = i == index;
            var e = list[index];
            _popT = 0f;
            UpdatePreview(e);
            UpdateBuyBar(e);
        }

        void UpdatePreview(MarketEntry e)
        {
            bool place = _tab == MarketTab.Furniture || _tab == MarketTab.Environment;
            _placeLayer.gameObject.SetActive(place);
            _decorLayer.gameObject.SetActive(_tab == MarketTab.Decor);
            bool item = _tab == MarketTab.Item;
            _eggLayer.gameObject.SetActive(_tab == MarketTab.Egg || item);
            _eggNest.SetActive(!item); // 道具不放在蛋窩裡
            _bubble.gameObject.SetActive(_tab != MarketTab.Egg && !item);
            var sprite = SpriteOf(e);
            string pet = PetName();

            if (place)
            {
                _tryImg.sprite = sprite;
                _tryLabel.text = _tab == MarketTab.Environment ? "環境預覽" : (Owned(e) ? "已擁有" : "試擺");
                float lw = _tryLabel.GetPreferredValues(_tryLabel.text).x + 16f;
                ((RectTransform)_tryLabel.transform.parent).sizeDelta = new Vector2(lw, 18f);
                _bubbleText.text = e.petLikes ? pet + "：好喜歡！" : (Locked(e) ? pet + "：那是什麼？" : pet + "：嗯…還好");
                SetBubble(34f, 86f, 56f);
                _previewNote.gameObject.SetActive(false);
            }
            else if (_tab == MarketTab.Decor)
            {
                bool got = DecorHave(e) >= e.need;
                _decorHeadImg.sprite = sprite;
                _decorHeadImg.color = new Color(1f, 1f, 1f, got ? 1f : 0.45f);
                _bubbleText.text = got ? "要戴這個嗎？" : "好想要那個…";
                SetBubble(262f, 92f, 14f);
                _previewNote.text = e.desc;
                _previewNote.gameObject.SetActive(true);
            }
            else if (item)
            {
                _eggImg.sprite = sprite;
                _previewNote.text = PetDead() ? PetName() + " 昏倒了，喝一瓶就會醒來" : "寵物昏倒時才用得到，先備著";
                _previewNote.gameObject.SetActive(true);
            }
            else
            {
                _eggImg.sprite = sprite;
                _previewNote.text = "孵出來的樣子每次都不一樣，之後靠餵照片決定進化";
                _previewNote.gameObject.SetActive(true);
            }
        }

        void SetBubble(float x, float y, float tailX)
        {
            float w = _bubbleText.GetPreferredValues(_bubbleText.text).x + 22f;
            _bubble.sizeDelta = new Vector2(w, 28f);
            _bubble.anchoredPosition = new Vector2(x + w / 2f, -(y + 14f));
            var tail = (RectTransform)_bubble.Find("Tail");
            tail.anchoredPosition = new Vector2(-w / 2f + tailX + 6f, -14f);
        }

        void UpdateBuyBar(MarketEntry e)
        {
            _buyName.text = e.name;
            _buyDesc.text = e.desc;
            _btnSecond.gameObject.SetActive(false);
            _btnMainCoin.gameObject.SetActive(false);

            if (_tab == MarketTab.Decor)
            {
                bool got = DecorHave(e) >= e.need;
                // 撿到之後才能戴；目前照片辨識還沒接上，先都是「去拍照」
                if (got) SetMain("戴上", Yellow, YellowEdge, Ink, true);
                else SetMain("去拍照", Coral, CoralEdge, Color.white, true);
            }
            else if (_tab == MarketTab.Egg)
            {
                SetMain("買 10 顆", Yellow, YellowEdge, Ink, true, MarketCatalog.EggTenPrice);
                _btnSecond.gameObject.SetActive(true);
                _btnSecondText.text = "買 1 顆";
                _btnSecondCoin.gameObject.SetActive(true);
                SetPriceText(_btnSecondText, "買 1 顆", e.price);
            }
            else if (_tab == MarketTab.Item)
            {
                // 寵物昏倒、空間有藥水：主按鈕「喝一瓶」，旁邊還是可以買
                int have = OwnedCount(e.id);
                if (PetDead() && have > 0)
                {
                    SetMain($"喝一瓶（有 {have} 瓶）", Coral, CoralEdge, Color.white, true);
                    _btnSecond.gameObject.SetActive(true);
                    _btnSecondCoin.gameObject.SetActive(true);
                    SetPriceText(_btnSecondText, "買 1 瓶", e.price);
                }
                else SetMain("買 1 瓶", Yellow, YellowEdge, Ink, true, e.price);
            }
            else if (Locked(e)) SetMain("還沒解鎖", Line, Line, Muted2, false);
            else if (Owned(e))
            {
                if (_tab == MarketTab.Environment) SetMain("已擁有", Line, Line, Muted2, false);
                else SetMain("去擺放", Green, GreenEdge, Color.white, true);
            }
            else SetMain("買下", Yellow, YellowEdge, Ink, true, e.price);
            LayoutBuyButtons();
        }

        void SetMain(string label, Color bg, Color edge, Color fg, bool interactable, int price = -1)
        {
            _btnMainBg.color = bg;
            _btnMainEdge.color = edge;
            _btnMainEdge.enabled = bg != Line;
            _btnMainText.color = fg;
            _btnMainButton.interactable = interactable;
            _btnMainCoin.gameObject.SetActive(price >= 0);
            if (price >= 0) SetPriceText(_btnMainText, label, price);
            else _btnMainText.text = label;
        }

        static void SetPriceText(TMP_Text t, string label, int price) =>
            t.text = label + "  <space=20>" + price.ToString("N0");

        void LayoutBuyButtons()
        {
            // 主按鈕靠右，寬度隨文字（至少 128）；蛋的「買 1 顆」在主按鈕左邊
            float mw = Mathf.Max(128f, _btnMainText.GetPreferredValues(_btnMainText.text).x + 36f);
            _btnMain.sizeDelta = new Vector2(mw, 50f);
            _btnMain.anchoredPosition = new Vector2(DesignW - PadX - mw, -12f);
            PlaceCoin(_btnMainCoin, _btnMainText, mw);
            float right = DesignW - PadX - mw;
            if (_btnSecond.gameObject.activeSelf)
            {
                float sw = _btnSecondText.GetPreferredValues(_btnSecondText.text).x + 28f;
                _btnSecond.sizeDelta = new Vector2(sw, 50f);
                _btnSecond.anchoredPosition = new Vector2(right - 8f - sw, -12f);
                PlaceCoin(_btnSecondCoin, _btnSecondText, sw);
                right -= 8f + sw;
            }
            float textW = right - PadX - 12f;
            _buyName.rectTransform.sizeDelta = new Vector2(textW, 20f);
            _buyDesc.rectTransform.sizeDelta = new Vector2(textW, 16f);
        }

        static void PlaceCoin(Image coin, TMP_Text text, float buttonW)
        {
            if (!coin.gameObject.activeSelf) return;
            // 金幣圖放在「<space>」留出的空位上
            string label = text.text.Substring(0, text.text.IndexOf("  <space"));
            float full = text.GetPreferredValues(text.text).x;
            float before = text.GetPreferredValues(label).x;
            float x = (buttonW - full) / 2f + before + 4f;
            coin.rectTransform.anchoredPosition = new Vector2(x, -17f);
        }

        // ─── 操作 ──────────────────────────────────────────────────────

        void OnMain()
        {
            var e = Current;
            if (e == null || _busy) return;
            if (_tab == MarketTab.Decor)
            {
                if (DecorHave(e) < e.need) GoTakePhoto();
                return;
            }
            if (_tab == MarketTab.Egg) { Buy(e, 10, MarketCatalog.EggTenPrice); return; }
            if (_tab == MarketTab.Item)
            {
                if (PetDead() && OwnedCount(e.id) > 0) DrinkPotion();
                else Buy(e, 1, e.price);
                return;
            }
            if (Locked(e)) return;
            if (Owned(e))
            {
                if (_tab == MarketTab.Furniture) GoPlace(e);
                return;
            }
            Buy(e, 1, e.price);
        }

        void OnSecond()
        {
            var e = Current;
            if (e == null || _busy || (_tab != MarketTab.Egg && _tab != MarketTab.Item)) return;
            Buy(e, 1, e.price);
        }

        void Buy(MarketEntry e, int count, int price)
        {
            var fb = FirebaseManager.Instance;
            if (fb == null || string.IsNullOrEmpty(fb.RoomId)) { Toast("先進入一個空間再來買"); return; }
            if (_coins != int.MinValue && _coins < price) { Toast("金幣不夠，再多餵幾張照片吧"); return; }
            _busy = true;
            fb.SpendRoomCoins(price, "shop:" + e.id + (count > 1 ? "x" + count : ""), ok =>
            {
                if (this == null) return;
                if (!ok) { _busy = false; Toast("金幣不夠或連線不穩，沒有扣款"); return; }
                fb.AddRoomShopItem(e.id, count, ok2 =>
                {
                    _busy = false;
                    if (this == null) return;
                    if (!ok2)
                    {
                        // 記錄失敗：把錢退回去
                        fb.AddRoomCoins(price, "refund:" + e.id);
                        Toast("購買失敗，金幣已退回");
                        return;
                    }
                    _owned[e.id] = OwnedCount(e.id) + count;
                    Toast(e.tab == MarketTab.Egg ? $"買到 {count} 顆{e.name}！"
                        : e.tab == MarketTab.Item ? $"買到 {count} 瓶{e.name}！"
                        : (e.tab == MarketTab.Environment ? "新環境解鎖了！" : "買好了！可以去擺放"));
                    ShowTab(_tab);
                });
            });
        }

        static bool PetDead()
        {
            var health = FindObjectOfType<PetHealthManager>(true);
            return health != null && health.IsDead;
        }

        void DrinkPotion()
        {
            var health = FindObjectOfType<PetHealthManager>(true);
            if (health == null) return;
            _busy = true;
            health.Revive((ok, reason) =>
            {
                _busy = false;
                if (this == null) return;
                if (ok) { _owned[MarketCatalog.RevivePotion] = Mathf.Max(0, OwnedCount(MarketCatalog.RevivePotion) - 1); Toast(PetName() + " 醒來了！愛心全滿"); }
                else Toast(reason == "alive" ? "已經有人讓牠醒來了" : reason == "potion" ? "藥水剛好被用完了" : "復活失敗，請再試一次");
                LoadOwned();
            });
        }

        /// <summary>打開商店並選好某個商品（例如復活確認窗的「去商店買」）。</summary>
        public static void OpenAt(string itemId)
        {
            _pendingItem = itemId;
            var pm = FindObjectOfType<PageManager>(true);
            pm?.ShowMarketPage();
        }

        static string _pendingItem;

        void ApplyPendingItem()
        {
            if (string.IsNullOrEmpty(_pendingItem)) return;
            var e = MarketCatalog.Get(_pendingItem);
            _pendingItem = null;
            if (e == null) return;
            _sel[e.tab] = Mathf.Max(0, MarketCatalog.InTab(e.tab).IndexOf(e));
            _tab = e.tab;
        }

        void GoPlace(MarketEntry e)
        {
            var item = decorationDatabase != null ? decorationDatabase.GetById(e.id) : null;
            if (item == null) { Toast("這件家具還沒有擺放用的圖，請重新執行 Build Market Screen"); return; }
            if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
            pageManager?.ShowHomePage();
            if (roomEditor == null) roomEditor = FindObjectOfType<RoomEditorController>(true);
            roomEditor?.EnterEditModeAndPlace(item);
        }

        void GoTakePhoto()
        {
            if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
            pageManager?.ShowHomePage();
            FindObjectOfType<CameraUIManager>(true)?.OpenCameraFromHand();
        }

        void Back()
        {
            if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
            pageManager?.ShowHomePage();
        }

        void Toast(string msg)
        {
            _toastText.text = msg;
            float w = Mathf.Min(DesignW - 40f, _toastText.GetPreferredValues(msg).x + 32f);
            _toast.sizeDelta = new Vector2(w, 40f);
            _toast.anchoredPosition = new Vector2((DesignW - w) / 2f, _toast.anchoredPosition.y);
            _toastUntil = Time.unscaledTime + 2.4f;
        }

        Sprite SpriteOf(MarketEntry e) => e == null ? null : SpriteByName(e.sprite);

        Sprite SpriteByName(string n)
        {
            foreach (var s in sprites) if (s != null && s.name == n) return s;
            return null;
        }

        Sprite PetSprite()
        {
            if (petSprites == null || petSprites.Length == 0) return null;
            int i = PetCollectionManager.Instance != null ? PetCollectionManager.Instance.CurrentPetIndex : 0;
            return petSprites[Mathf.Abs(i) % petSprites.Length];
        }

        static string PetName()
        {
            string n = PetCollectionManager.Instance != null ? PetCollectionManager.Instance.GetPetDisplayName() : null;
            return string.IsNullOrEmpty(n) ? "寵物" : n;
        }

        // ─── 版面 ──────────────────────────────────────────────────────

        void Relayout()
        {
            float w = _root.rect.width, h = _root.rect.height;
            var safe = Screen.safeArea;
            if (w <= 0f || h <= 0f) return;
            if (!_dirty && Mathf.Approximately(w, _lastW) && Mathf.Approximately(h, _lastH) && safe == _lastSafe) return;
            _dirty = false;
            _lastW = w; _lastH = h; _lastSafe = safe;

            _k = w / DesignW;
            float scale = _canvas.rootCanvas.scaleFactor;
            float px = scale > 0f ? 1f / scale : 1f;
            float topInset = (Screen.height - safe.yMax) * px / _k;
            float bottomInset = safe.yMin * px / _k;
            float screenH = h / _k;

            _top.localScale = Vector3.one * _k;
            _top.sizeDelta = new Vector2(DesignW, screenH);

            // 頂部按鈕避開瀏海；預覽圖本身貼齊螢幕上緣
            ((RectTransform)_top.Find("Header")).anchoredPosition = new Vector2(0f, -topInset);

            float panelH = screenH - PanelTop;
            _panel.sizeDelta = new Vector2(DesignW, panelH);
            float bar = BuyBarH + bottomInset;
            _buyBar.sizeDelta = new Vector2(DesignW, bar);
            _viewport.sizeDelta = new Vector2(DesignW, panelH - 76f);

            // 商品格：三欄、間距 10；最後留出購買列的高度
            float rowH = _tab == MarketTab.Decor ? 139f : 128f;
            int rows = Mathf.CeilToInt(_cards.Count / 3f);
            for (int i = 0; i < _cards.Count; i++)
            {
                int r = i / 3, c = i % 3;
                _cards[i].rt.anchoredPosition = new Vector2(c * (CardW + GridGap), -r * (rowH + GridGap));
                _cards[i].rt.sizeDelta = new Vector2(CardW, rowH);
            }
            float gridH = rows * rowH + Mathf.Max(0, rows - 1) * GridGap;
            _grid.sizeDelta = new Vector2(DesignW - PadX * 2f, gridH);
            float y = 4f + 24f + gridH;
            if (_footNote.gameObject.activeSelf)
            {
                float fh = Mathf.Ceil(_footText.GetPreferredValues(_footText.text, _footText.rectTransform.rect.width, 0f).y) + 24f;
                _footNote.sizeDelta = new Vector2(DesignW - PadX * 2f, fh);
                _footNote.anchoredPosition = new Vector2(PadX, -(y + 14f));
                y += 14f + fh;
            }
            _content.sizeDelta = new Vector2(DesignW, y + bar + 16f);

            _toast.anchoredPosition = new Vector2(_toast.anchoredPosition.x, -(screenH - bar - 52f));
        }

        // ─── 建立介面 ──────────────────────────────────────────────────

        void Build()
        {
            // 獨立的最上層 Canvas（不受頁面上層 CanvasGroup 影響），顯示／隱藏由 OnEnable／OnDisable 同步
            var go = new GameObject("MarketScreen (runtime)", typeof(RectTransform));
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = sortingOrder;
            go.AddComponent<GraphicRaycaster>();
            _root = (RectTransform)go.transform;

            Stretch(_root, "Background").gameObject.AddComponent<Image>().color = Grass;

            _top = new GameObject("Design", typeof(RectTransform)).GetComponent<RectTransform>();
            _top.SetParent(_root, false);
            _top.anchorMin = _top.anchorMax = new Vector2(0.5f, 1f);
            _top.pivot = new Vector2(0.5f, 1f);
            _top.sizeDelta = new Vector2(DesignW, 772f);

            BuildPreview();
            BuildHeader();
            BuildPanel();
            BuildToast();
            go.SetActive(false); // 元件啟用時（OnEnable）才顯示
        }

        void BuildPreview()
        {
            var pv = Place(_top, "Preview", 0, 0, DesignW, PreviewH);
            pv.gameObject.AddComponent<RectMask2D>();
            // 營地圖：等比填滿 390×772，往上移 250（設計稿 top:-250）
            var img = Place(pv, "Camp", 0, -250, DesignW, 772).gameObject.AddComponent<RawImage>();
            img.texture = background;
            img.raycastTarget = false;
            if (background != null) img.uvRect = AspectFill(background.width, background.height, DesignW, 772f);
            var shade = Place(pv, "TopShade", 0, 0, DesignW, 110).gameObject.AddComponent<RawImage>();
            shade.texture = Gradient(new Color(30f / 255f, 40f / 255f, 20f / 255f, 0.45f), new Color(30f / 255f, 40f / 255f, 20f / 255f, 0f));
            shade.raycastTarget = false;

            // 家具／環境：寵物在左邊、試擺的東西在右邊虛線框（可拖曳）
            _placeLayer = Place(pv, "Place", 0, 0, DesignW, PreviewH);
            UIShapes.AddRounded(Place(_placeLayer, "PetShadow", 60, 236, 120, 20).gameObject, new Color(30f / 255f, 40f / 255f, 20f / 255f, 0.28f), 10).raycastTarget = false;
            _placePet = Place(_placeLayer, "Pet", 52, 128, 134, 116);
            _placePetImg = PetImage(_placePet);
            _tryBox = Place(_placeLayer, "Try", 200, 120, 150, 130);
            var hit = _tryBox.gameObject.AddComponent<Image>();
            hit.color = new Color(1, 1, 1, 0);
            _tryDash = UIShapes.AddRing(Place(_tryBox, "Dash", 0, 0, 0, 0).gameObject, Color.white, 14);
            StretchIn(_tryDash.rectTransform);
            var tryImgRt = Place(_tryBox, "Item", 10, 14, 130, 104);
            tryImgRt.pivot = new Vector2(0.5f, 0.5f);
            tryImgRt.anchoredPosition = new Vector2(75f, -66f);
            _tryImg = tryImgRt.gameObject.AddComponent<Image>();
            _tryImg.preserveAspect = true;
            _tryImg.raycastTarget = false;
            var tag = Place(_tryBox, "Tag", 10, -12, 40, 18);
            UIShapes.AddRounded(Place(tag, "Edge", 0, 2, 0, 0).gameObject, YellowEdge, 8).raycastTarget = false;
            StretchIn((RectTransform)tag.GetChild(0), 0, 2, 0, -2);
            StretchIn(UIShapes.AddRounded(Place(tag, "Fill", 0, 0, 0, 0).gameObject, Yellow, 8).rectTransform);
            _tryLabel = Label(tag, 0, 0, 0, 0, "", 11, Ink, TextAlignmentOptions.Center);
            StretchIn(_tryLabel.rectTransform);
            MakeDraggable(_tryBox, new Rect(0f, 70f, DesignW - 150f, PreviewH - 130f - 70f));
            var hint = Label(_placeLayer, 212, 262, 160, 16, "拖曳換位置", 11, Color.white, TextAlignmentOptions.Left, onScene: true);
            hint.enableWordWrapping = false;

            // 裝飾：寵物在中間，頭上戴著選中的東西
            _decorLayer = Place(pv, "Decor", 0, 0, DesignW, PreviewH);
            var glow = Place(_decorLayer, "Glow", 100, 74, 190, 190);
            UIShapes.AddShadow(glow, new Color(1f, 0.925f, 0.667f, 0.4f), 95, 50, 0);
            UIShapes.AddRounded(Place(glow, "Disc", 0, 0, 190, 190).gameObject, new Color(1f, 0.973f, 0.925f, 0.34f), 95).raycastTarget = false;
            UIShapes.AddRounded(Place(_decorLayer, "PetShadow", 130, 240, 130, 22).gameObject, new Color(30f / 255f, 40f / 255f, 20f / 255f, 0.28f), 11).raycastTarget = false;
            _decorPet = Place(_decorLayer, "Pet", 116, 70, 158, 180);
            _decorPetImg = PetImage(Place(_decorPet, "Body", 0, 44, 158, 136));
            var head = Place(_decorPet, "Head", 47, 6, 64, 52);
            head.pivot = new Vector2(0.5f, 0.5f);
            head.anchoredPosition = new Vector2(79f, -32f);
            _decorHeadImg = head.gameObject.AddComponent<Image>();
            _decorHeadImg.preserveAspect = true;
            _decorHeadImg.raycastTarget = false;

            // 寵物蛋：窩裡的蛋左右搖
            _eggLayer = Place(pv, "Egg", 0, 0, DesignW, PreviewH);
            var eglow = Place(_eggLayer, "Glow", 105, 70, 180, 180);
            UIShapes.AddShadow(eglow, new Color(1f, 0.925f, 0.667f, 0.55f), 90, 60, 0);
            UIShapes.AddRounded(Place(eglow, "Disc", 0, 0, 180, 180).gameObject, new Color(1f, 0.973f, 0.925f, 0.4f), 90).raycastTarget = false;
            var nest = Place(_eggLayer, "NestGroup", 0, 0, DesignW, PreviewH);
            _eggNest = nest.gameObject;
            UIShapes.AddRounded(Place(nest, "NestShade", 130, 234, 130, 24).gameObject, Hex(0x6E4A2E), 12).raycastTarget = false;
            UIShapes.AddRounded(Place(nest, "Nest", 130, 228, 130, 24).gameObject, Hex(0x8B5E3C), 12).raycastTarget = false;
            _eggImgRt = Place(_eggLayer, "EggImg", 135, 108, 120, 128);
            _eggImgRt.pivot = new Vector2(0.5f, 0.1f);
            _eggImgRt.anchoredPosition = new Vector2(195f, -108f - 128f * 0.9f);
            _eggImg = _eggImgRt.gameObject.AddComponent<Image>();
            _eggImg.preserveAspect = true;
            _eggImg.raycastTarget = false;

            // 寵物說話的泡泡（家具／環境在左上、裝飾在右上）
            _bubble = Place(pv, "Bubble", 0, 0, 100, 28);
            _bubble.pivot = new Vector2(0.5f, 0.5f);
            UIShapes.AddShadow(_bubble, new Color(0, 0, 0, 0.2f), 14, 10, 4);
            var tail = Place(_bubble, "Tail", 0, 0, 12, 12);
            tail.anchorMin = tail.anchorMax = new Vector2(0.5f, 0.5f);
            tail.pivot = new Vector2(0.5f, 0.5f);
            tail.localRotation = Quaternion.Euler(0, 0, 45);
            tail.gameObject.AddComponent<Image>().color = Card;
            StretchIn(UIShapes.AddRounded(Place(_bubble, "Fill", 0, 0, 0, 0).gameObject, Card, 14).rectTransform);
            _bubbleText = Label(_bubble, 0, 0, 0, 0, "", 12, Ink, TextAlignmentOptions.Center);
            StretchIn(_bubbleText.rectTransform);
            _bubbleText.enableWordWrapping = false;

            _previewNote = Label(pv, 0, 268, DesignW, 18, "", 12, Color.white, TextAlignmentOptions.Center, onScene: true);
        }

        Image PetImage(RectTransform rt)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = PetSprite();
            img.enabled = img.sprite != null;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        void BuildHeader()
        {
            var header = Place(_top, "Header", 0, 0, DesignW, 80);
            var back = Place(header, "Back", 12, 14, 44, 44);
            UIShapes.AddShadow(back, new Color(30f / 255f, 40f / 255f, 20f / 255f, 0.22f), 22, 8, 3);
            UIShapes.AddRounded(Place(back, "Border", 0, 0, 44, 44).gameObject, new Color(1f, 1f, 1f, 0.8f), 22).raycastTarget = false;
            var bg = UIShapes.AddRounded(Place(back, "Fill", 1.5f, 1.5f, 41, 41).gameObject, new Color(1f, 0.973f, 0.925f, 0.72f), 20.5f);
            var chev = AddGraphic<UIPolygon>(Place(back, "Chevron", 0, 0, 44, 44));
            chev.color = Ink;
            chev.raycastTarget = false;
            chev.SetPoints(new Vector2(25f, 14.5f), new Vector2(25f, 29.5f), new Vector2(17.5f, 22f));
            var bb = back.gameObject.AddComponent<Button>();
            bb.targetGraphic = bg;
            bb.onClick.AddListener(Back);

            Label(header, 66, 21, 160, 30, "商店", 22, Color.white, TextAlignmentOptions.Left, onScene: true);

            _coinPill = Place(header, "Coins", DesignW - 12f - 90f, 21, 90, 30);
            UIShapes.AddRounded(_coinPill.gameObject, new Color(1f, 1f, 1f, 0.9f), 15).raycastTarget = false;
            StretchIn(UIShapes.AddRounded(Place(_coinPill, "Fill", 0, 0, 0, 0).gameObject, new Color(1f, 1f, 1f, 0.78f), 13.5f).rectTransform, 1.5f, 1.5f, 1.5f, 1.5f);
            var coin = Place(_coinPill, "Icon", 6, 7, 16, 16).gameObject.AddComponent<Image>();
            coin.sprite = SpriteByName("ui_coin");
            coin.raycastTarget = false;
            _coinText = Label(_coinPill, 27, 0, 80, 30, "", 13, Ink, TextAlignmentOptions.Left);
            _coinText.enableWordWrapping = false;

            _sharedNote = Label(header, 0, 62, DesignW - 16f, 14, "", 10, Color.white, TextAlignmentOptions.Right, onScene: true);
            _sharedNote.enableWordWrapping = false;
        }

        void BuildPanel()
        {
            _panel = Place(_top, "Panel", 0, PanelTop, DesignW, 472);
            UIShapes.AddShadow(_panel, new Color(0, 0, 0, 0.22f), 26, 24, -8);
            var fill = UIShapes.AddRounded(Place(_panel, "Fill", 0, 0, 0, 0).gameObject, Paper, 26);
            StretchIn(fill.rectTransform, 0, 0, 0, -30);
            UIShapes.AddRounded(Place(_panel, "Handle", 175, 8, 40, 5).gameObject, Line, 2.5f).raycastTarget = false;

            // 分頁：家具／裝飾／環境／寵物蛋
            var tabs = Place(_panel, "Tabs", PadX, 22, DesignW - PadX * 2f, 44);
            UIShapes.AddRounded(tabs.gameObject, Beige, 15).raycastTarget = false;
            string[] labels = { "家具", "裝飾", "環境", "寵物蛋", "道具" };
            float tw = (DesignW - PadX * 2f - 8f - 4f * (labels.Length - 1)) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                var t = Place(tabs, labels[i], 4f + i * (tw + 4f), 4, tw, 36);
                var hit = t.gameObject.AddComponent<Image>();
                hit.color = new Color(1, 1, 1, 0);
                var on = Place(t, "On", 0, 0, tw, 36);
                UIShapes.AddShadow(on, new Color(30f / 255f, 30f / 255f, 20f / 255f, 0.12f), 11, 6, 2);
                UIShapes.AddRounded(Place(on, "Fill", 0, 0, tw, 36).gameObject, Card, 11).raycastTarget = false;
                _tabOn.Add(on.gameObject);
                _tabTexts.Add(Label(t, 0, 0, tw, 36, labels[i], 14, Muted2, TextAlignmentOptions.Center));
                var b = t.gameObject.AddComponent<Button>();
                b.targetGraphic = hit;
                b.transition = Selectable.Transition.None;
                var tab = (MarketTab)i;
                b.onClick.AddListener(() => ShowTab(tab));
            }

            // 可捲動的商品區
            _viewport = Place(_panel, "Viewport", 0, 76, DesignW, 400);
            _viewport.gameObject.AddComponent<RectMask2D>();
            var vpImg = _viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(1, 1, 1, 0);
            _content = Place(_viewport, "Content", 0, 0, DesignW, 800);
            var scroll = _viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = _viewport;
            scroll.content = _content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 20f;

            _section = Label(_content, PadX + 4f, 4, 200, 18, "", 12, Muted, TextAlignmentOptions.Left);
            _sectionRight = Label(_content, DesignW / 2f, 4, DesignW / 2f - PadX - 4f, 18, "", 11, Muted2, TextAlignmentOptions.Right);
            _grid = Place(_content, "Grid", PadX, 4 + 24, DesignW - PadX * 2f, 300);

            _footNote = Place(_content, "Foot", PadX, 0, DesignW - PadX * 2f, 60);
            UIShapes.AddShadow(_footNote, new Color(30f / 255f, 30f / 255f, 20f / 255f, 0.08f), 16, 10, 3);
            StretchIn(UIShapes.AddRounded(Place(_footNote, "Fill", 0, 0, 0, 0).gameObject, Card, 16).rectTransform);
            _footText = Label(_footNote, 14, 12, DesignW - PadX * 2f - 28f, 40, "", 12, Muted, TextAlignmentOptions.TopLeft);
            _footText.lineSpacing = 12f;
            var ftr = _footText.rectTransform;
            ftr.anchorMin = Vector2.zero;
            ftr.anchorMax = Vector2.one;
            ftr.offsetMin = new Vector2(14f, 12f);
            ftr.offsetMax = new Vector2(-14f, -12f);

            BuildBuyBar();
        }

        CardView BuildCard(MarketEntry e, int index)
        {
            bool dec = e.tab == MarketTab.Decor;
            var rt = Place(_grid, e.id, 0, 0, CardW, dec ? 139 : 128);
            var view = new CardView { entry = e, rt = rt };
            UIShapes.AddShadow(rt, new Color(30f / 255f, 30f / 255f, 20f / 255f, 0.08f), 16, 10, 3);
            // 選中：3px 黃框
            view.ring = UIShapes.AddRounded(Place(rt, "Ring", 0, 0, 0, 0).gameObject, Yellow, 19);
            StretchIn(view.ring.rectTransform, -3, -3, -3, -3);
            view.ring.raycastTarget = false;
            var bg = UIShapes.AddRounded(Place(rt, "Fill", 0, 0, 0, 0).gameObject, Card, 16);
            StretchIn(bg.rectTransform);

            bool locked = Locked(e) || (dec && DecorHave(e) < e.need);
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.alpha = locked ? 0.62f : 1f;

            float iw = CardW - 14f;
            var imgBox = Place(rt, "ImageBox", 7, 7, iw, 70);
            UIShapes.AddRounded(imgBox.gameObject, e.tone, 12).raycastTarget = false;
            var img = Place(imgBox, "Image", (iw - 72f) / 2f, 6, 72, 58).gameObject.AddComponent<Image>();
            img.sprite = SpriteOf(e);
            img.preserveAspect = true;
            img.raycastTarget = false;
            if (e.petLikes)
            {
                // 寵物喜歡：紅底圓圈裡一顆白色愛心（兩個圓＋轉 45° 的方塊）
                var like = Place(imgBox, "Like", 5, 5, 20, 20);
                UIShapes.AddRounded(like.gameObject, Coral, 10).raycastTarget = false;
                UIShapes.AddRounded(Place(like, "L", 4.5f, 5.5f, 6, 6).gameObject, Color.white, 3).raycastTarget = false;
                UIShapes.AddRounded(Place(like, "R", 9.5f, 5.5f, 6, 6).gameObject, Color.white, 3).raycastTarget = false;
                var tip = Place(like, "Tip", 0, 0, 6.4f, 6.4f);
                tip.pivot = new Vector2(0.5f, 0.5f);
                tip.anchoredPosition = new Vector2(10f, -10.2f);
                tip.localRotation = Quaternion.Euler(0, 0, 45);
                tip.gameObject.AddComponent<Image>().raycastTarget = false;
            }

            var name = Label(rt, 7, 82, iw, 16, e.name, 13, Ink, TextAlignmentOptions.Left);
            name.enableWordWrapping = false;
            name.overflowMode = TextOverflowModes.Ellipsis;

            float y = 103f;
            if (dec)
            {
                var cat = Place(rt, "Cat", 7, y, iw, 12);
                UIShapes.AddRounded(Place(cat, "Dot", 0, 2, 8, 8).gameObject, MarketCatalog.CategoryColor(e.category), 4).raycastTarget = false;
                Label(cat, 12, 0, iw - 12f, 12, e.category, 10, Muted, TextAlignmentOptions.Left).enableWordWrapping = false;
                int have = DecorHave(e);
                var bar = Place(rt, "Progress", 7, y + 17, iw - 40f, 6);
                UIShapes.AddRounded(bar.gameObject, Divider, 3).raycastTarget = false;
                bar.gameObject.AddComponent<RectMask2D>();
                UIShapes.AddRounded(Place(bar, "Fill", 0, 0, (iw - 40f) * Mathf.Clamp01((float)have / Mathf.Max(1, e.need)), 6).gameObject,
                    MarketCatalog.CategoryColor(e.category), 3).raycastTarget = false;
                Label(rt, 7 + iw - 36f, y + 13, 36, 14, have >= e.need ? "撿到了" : $"{have} / {e.need}", 10, Muted2, TextAlignmentOptions.Right)
                    .enableWordWrapping = false;
            }
            else if (Locked(e))
            {
                Label(rt, 7, y, iw, 16, "未解鎖・" + e.lockText, 11, Muted2, TextAlignmentOptions.Left).enableWordWrapping = false;
            }
            else if (Owned(e) && e.tab != MarketTab.Egg && e.tab != MarketTab.Item)
            {
                var chip = Place(rt, "Owned", 7, y, 52, 18);
                UIShapes.AddRounded(chip.gameObject, Hex(0xE3EED9), 8).raycastTarget = false;
                Label(chip, 0, 0, 52, 18, "已擁有", 11, GreenEdge, TextAlignmentOptions.Center);
            }
            else
            {
                var coin = Place(rt, "Coin", 7, y + 1, 14, 14).gameObject.AddComponent<Image>();
                coin.sprite = SpriteByName("ui_coin");
                coin.raycastTarget = false;
                string unit = e.tab == MarketTab.Item ? "瓶" : "顆";
                string price = e.price.ToString("N0") + ((e.tab == MarketTab.Egg || e.tab == MarketTab.Item) && OwnedCount(e.id) > 0 ? $"  有 {OwnedCount(e.id)} {unit}" : "");
                Label(rt, 25, y, iw - 18f, 16, price, 12, Ink, TextAlignmentOptions.Left).enableWordWrapping = false;
            }

            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => Select(index));
            return view;
        }

        void BuildBuyBar()
        {
            _buyBar = Place(_panel, "BuyBar", 0, 0, DesignW, BuyBarH);
            _buyBar.anchorMin = _buyBar.anchorMax = new Vector2(0f, 0f);
            _buyBar.pivot = new Vector2(0f, 0f);
            _buyBar.anchoredPosition = Vector2.zero;
            _buyBar.gameObject.AddComponent<Image>().color = Card;
            var line = Place(_buyBar, "Line", 0, 0, DesignW, 1).gameObject.AddComponent<Image>();
            line.color = Divider;
            line.raycastTarget = false;

            _buyName = Label(_buyBar, PadX, 16, 200, 20, "", 15, Ink, TextAlignmentOptions.Left);
            _buyName.enableWordWrapping = false;
            _buyName.overflowMode = TextOverflowModes.Ellipsis;
            _buyDesc = Label(_buyBar, PadX, 39, 200, 16, "", 11, Muted, TextAlignmentOptions.Left);
            _buyDesc.enableWordWrapping = false;
            _buyDesc.overflowMode = TextOverflowModes.Ellipsis;

            _btnMain = Place(_buyBar, "Main", DesignW - PadX - 128f, 12, 128, 50);
            _btnMainEdge = UIShapes.AddRounded(Place(_btnMain, "Edge", 0, 3, 0, 0).gameObject, YellowEdge, 15);
            StretchIn(_btnMainEdge.rectTransform, 0, 3, 0, -3);
            _btnMainEdge.raycastTarget = false;
            _btnMainBg = UIShapes.AddRounded(Place(_btnMain, "Fill", 0, 0, 0, 0).gameObject, Yellow, 15);
            StretchIn(_btnMainBg.rectTransform);
            _btnMainText = Label(_btnMain, 0, 0, 0, 0, "", 15, Ink, TextAlignmentOptions.Center);
            StretchIn(_btnMainText.rectTransform);
            _btnMainText.enableWordWrapping = false;
            _btnMainCoin = Place(_btnMain, "Coin", 0, 17, 16, 16).gameObject.AddComponent<Image>();
            _btnMainCoin.sprite = SpriteByName("ui_coin");
            _btnMainCoin.raycastTarget = false;
            _btnMainButton = _btnMain.gameObject.AddComponent<Button>();
            _btnMainButton.targetGraphic = _btnMainBg;
            _btnMainButton.transition = Selectable.Transition.None;
            _btnMainButton.onClick.AddListener(OnMain);
            _btnMain.gameObject.AddComponent<ButtonScalePress>();

            _btnSecond = Place(_buyBar, "Second", 0, 12, 100, 50);
            _btnSecondBorder = UIShapes.AddRounded(Place(_btnSecond, "Border", 0, 0, 0, 0).gameObject, Line, 15);
            StretchIn(_btnSecondBorder.rectTransform);
            _btnSecondBorder.raycastTarget = false;
            _btnSecondBg = UIShapes.AddRounded(Place(_btnSecond, "Fill", 0, 0, 0, 0).gameObject, Color.white, 13.5f);
            StretchIn(_btnSecondBg.rectTransform, 1.5f, 1.5f, 1.5f, 1.5f);
            _btnSecondText = Label(_btnSecond, 0, 0, 0, 0, "", 13, Ink, TextAlignmentOptions.Center);
            StretchIn(_btnSecondText.rectTransform);
            _btnSecondText.enableWordWrapping = false;
            _btnSecondCoin = Place(_btnSecond, "Coin", 0, 18, 14, 14).gameObject.AddComponent<Image>();
            _btnSecondCoin.sprite = SpriteByName("ui_coin");
            _btnSecondCoin.raycastTarget = false;
            _btnSecondButton = _btnSecond.gameObject.AddComponent<Button>();
            _btnSecondButton.targetGraphic = _btnSecondBg;
            _btnSecondButton.onClick.AddListener(OnSecond);
            _btnSecond.gameObject.SetActive(false);
        }

        void BuildToast()
        {
            _toast = Place(_top, "Toast", 20, 600, DesignW - 40f, 40);
            _toastGroup = _toast.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.blocksRaycasts = false;
            _toastGroup.alpha = 0f;
            UIShapes.AddRounded(_toast.gameObject, new Color(Ink.r, Ink.g, Ink.b, 0.92f), 20).raycastTarget = false;
            _toastText = Label(_toast, 0, 0, 0, 0, "", 13, Paper, TextAlignmentOptions.Center);
            StretchIn(_toastText.rectTransform, 16, 0, 16, 0);
        }

        /// <summary>試擺框可以在預覽區裡拖來拖去（只是看看，不會真的放進營地）。</summary>
        void MakeDraggable(RectTransform rt, Rect bounds)
        {
            var trigger = rt.gameObject.AddComponent<EventTrigger>();
            Vector2 grab = Vector2.zero;
            void Add(EventTriggerType type, System.Action<PointerEventData> cb)
            {
                var entry = new EventTrigger.Entry { eventID = type };
                entry.callback.AddListener(d => cb((PointerEventData)d));
                trigger.triggers.Add(entry);
            }
            Vector2 Local(PointerEventData e)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)rt.parent, e.position, e.pressEventCamera, out var p);
                return p;
            }
            Add(EventTriggerType.BeginDrag, e => grab = rt.anchoredPosition - Local(e));
            Add(EventTriggerType.Drag, e =>
            {
                var p = Local(e) + grab;
                p.x = Mathf.Clamp(p.x, bounds.xMin, bounds.xMax);
                p.y = Mathf.Clamp(p.y, -bounds.yMax, -bounds.yMin);
                rt.anchoredPosition = p;
            });
        }

        // ─── UI helpers（與其他新版頁面相同的設計座標寫法）────────────

        TMP_Text Label(RectTransform parent, float left, float top, float w, float h, string value, float size,
                       Color color, TextAlignmentOptions align, bool onScene = false)
        {
            var rt = Place(parent, "Text", left, top, w, h);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            var f = skin != null ? skin.font : null;
            if (f != null) t.font = f;
            t.text = value;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = f != null ? FontStyles.Normal : FontStyles.Bold;
            t.alignment = align;
            t.raycastTarget = false;
            t.richText = true;
            t.enableWordWrapping = true;
            if (onScene && skin != null && skin.labelShadowMaterial != null) t.fontSharedMaterial = skin.labelShadowMaterial;
            return t;
        }

        static T AddGraphic<T>(RectTransform rt) where T : Graphic
        {
            if (rt.GetComponent<CanvasRenderer>() == null) rt.gameObject.AddComponent<CanvasRenderer>();
            return rt.gameObject.AddComponent<T>();
        }

        static RectTransform Place(Transform parent, string name, float left, float top, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(left, -top);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        static RectTransform Stretch(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            StretchIn(rt);
            return rt;
        }

        static void StretchIn(RectTransform rt, float l = 0, float t = 0, float r = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
        }

        static Rect AspectFill(float texW, float texH, float boxW, float boxH)
        {
            if (texW <= 0 || texH <= 0) return new Rect(0, 0, 1, 1);
            float texAspect = texW / texH, boxAspect = boxW / boxH;
            if (texAspect > boxAspect)
            {
                float w = boxAspect / texAspect;
                return new Rect((1f - w) / 2f, 0f, w, 1f);
            }
            float h = texAspect / boxAspect;
            return new Rect(0f, (1f - h) / 2f, 1f, h);
        }

        static Texture2D Gradient(Color top, Color bottom)
        {
            const int n = 16;
            var tex = new Texture2D(1, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            for (int i = 0; i < n; i++) tex.SetPixel(0, i, Color.Lerp(bottom, top, i / (n - 1f)));
            tex.Apply(false, true);
            return tex;
        }

        static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
