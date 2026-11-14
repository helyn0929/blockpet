using System;
using System.Collections.Generic;
using BlockPet.Feed;
using BlockPet.Hand;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Dex
{
    /// <summary>
    /// 圖鑑（設計文件 5.23；設計稿「圖鑑」DexHome／DexDetail／DexLocked／DexNew；docs/dex/dex_spec.json）。
    /// - 主頁：頂部（返回、標題、總進度；2026-11-05 拿掉系列分頁）＋系列卡（品種蛋圖示、進度、說明、一排 3 格：彩虹型、極致型、進化型）。
    /// - 點收到的格子：下方滑出詳細（大圖、第幾代、進化日期、養了幾天、調色盤；收過好幾代可左右切換）。
    /// - 點還沒收到的格子：中間跳出小卡（剪影、怎麼養、知道了）。
    /// - 收到新的一格：全畫面「圖鑑新增！」（光芒旋轉、+金幣、系列進度、看圖鑑）。
    /// 介面在執行時以 390 寬設計座標建立，依螢幕寬度等比縮放；資料是營地共用的 Rooms/{room}/dex（FirebaseManager）。
    /// 入口：首頁右上選單「圖鑑」（MenuToggle）。
    /// </summary>
    public class DexScreen : MonoBehaviour
    {
        const float DesignW = 390f;
        const float DesignH = 772f;
        const float HeaderH = 74f; // 設計文件 5.23 ②：拿掉分頁後頂部 0～74
        const float Side = 14f;
        const float CardW = DesignW - Side * 2f;   // 362
        const float CardInnerW = CardW - 24f;      // 左右內距 12
        const float CellGap = 8f;
        const float CellH = 104f;
        const int Cols = 3; // 一排 3 格（設計文件 5.23 ②，11-05）
        static readonly float CellW = (CardInnerW - CellGap * (Cols - 1)) / Cols;

        static readonly Color Bg = Hex(0xF7F0E2);
        static readonly Color HeaderFill = Hex(0xFFF8EC);
        static readonly Color HeaderLine = Hex(0xEFE6D4);
        static readonly Color Ink = Hex(0x3A2E22);
        static readonly Color Brown = Hex(0x8A6A4E);
        static readonly Color Cream = Hex(0xFFFCF4);
        static readonly Color Paper = Hex(0xFFF8EC);
        static readonly Color Beige = Hex(0xF2E9D8);
        static readonly Color Line = Hex(0xE2D6C0);
        static readonly Color Yellow = Hex(0xF6C945);
        static readonly Color YellowEdge = Hex(0xD9A92C);
        static readonly Color HowText = Hex(0x6B5D4E);
        static readonly Color Coral = Hex(0xE86B4F);
        static readonly Color Silhouette = new Color(0f, 0f, 0f, 0.22f); // 11-05 從 14% 調深（格子沒有底）
        static readonly Color DimColor = new Color(20f / 255f, 24f / 255f, 14f / 255f, 1f);

        /// <summary>調色盤五大類（美食、自然、城市、生活人物、寵物）的顏色，與進化條相同。</summary>
        static readonly string[] PaletteNames = { "美食", "自然", "城市", "生活人物", "寵物" };
        static readonly Color[] PaletteColors = { Hex(0xF29A3A), Hex(0x6FA45F), Hex(0x6F8FE0), Hex(0xD78AD0), Hex(0xE8C33A) };

        /// <summary>各外型的寵物圖還沒畫：先把同一張圖上色區分（設計稿也是 BowBow 改色）。</summary>
        static readonly Dictionary<string, Color> FormTint = new Dictionary<string, Color>
        {
            { DexRules.Rainbow, new Color(1f, 0.9f, 0.95f) },
            { DexRules.Extreme, new Color(1f, 0.82f, 0.62f) },
            { DexRules.Evolved, Color.white },
        };

        static DexScreen _instance;

        DexSkin _skin;
        TMP_FontAsset _font;
        Canvas _canvas;
        RectTransform _root, _design, _topFill;
        Image _topImg, _base;
        float _k = 1f, _lastW = -1f, _lastH = -1f, _screenH = DesignH, _topInset, _bottomInset;
        Rect _lastSafe;

        // 主頁
        TMP_Text _total;
        RectTransform _listView, _listContent;
        ScrollRect _listScroll;
        DexBook _book = new DexBook();
        bool _loading;

        // 詳細（下方滑出）
        RectTransform _sheetLayer, _sheet;
        Image _sheetDim;
        float _sheetT;
        bool _sheetOpen;
        string _detailSeries, _detailForm;
        int _detailIndex;
        RectTransform _detailPet;

        // 還沒收到（中間小卡）
        RectTransform _popLayer, _popDesign, _popCard;
        Image _popDim;
        float _popT;
        bool _popOpen;

        // 收到新的一格
        RectTransform _newLayer, _newDesign, _rays;
        CanvasGroup _newGroup;
        RectTransform[] _newPop;
        float _newT;
        bool _newOpen;

        // ─── 入口 ───────────────────────────────────────────────────────

        /// <summary>打開圖鑑主頁。</summary>
        public static void Open()
        {
            Ensure();
            _instance.ShowHome();
        }

        /// <summary>收到新的一格（進化動畫播完後呼叫）：跳「圖鑑新增！」；收到已經有的格子不跳（設計文件 5.23 ⑥）。</summary>
        public static void ShowNew(string seriesId, string formKey, FirebaseManager.DexRecordResult result)
        {
            if (!result.ok || !result.isNewCell) return;
            Ensure();
            _instance.OpenNew(seriesId, formKey, result);
        }

        static void Ensure()
        {
            if (_instance != null) return;
            var go = new GameObject("DexScreen (runtime)", typeof(RectTransform));
            _instance = go.AddComponent<DexScreen>();
            _instance.Build();
        }

        void OnEnable() => FirebaseManager.OnRoomChanged += OnRoomChanged;
        void OnDisable() => FirebaseManager.OnRoomChanged -= OnRoomChanged;
        void OnDestroy() { if (_instance == this) _instance = null; }

        void OnRoomChanged() => Close();

        void ShowHome()
        {
            gameObject.SetActive(true);
            SetHomeVisible(true);
            CloseSheet(true);
            ClosePop(true);
            Reload();
        }

        void Reload()
        {
            var fb = FirebaseManager.Instance;
            if (fb == null) { RebuildHome(); return; }
            _loading = true;
            RebuildHome();
            fb.LoadRoomDex(book =>
            {
                _loading = false;
                if (this == null) return;
                _book = book ?? new DexBook();
                RebuildHome();
            });
        }

        void Close()
        {
            _newOpen = false;
            if (_newLayer != null) _newLayer.gameObject.SetActive(false);
            CloseSheet(true);
            ClosePop(true);
            gameObject.SetActive(false);
        }

        // ─── 每一格 ─────────────────────────────────────────────────────

        void Update()
        {
            Relayout();
            float dt = Time.unscaledDeltaTime;

            // 詳細：0.3 秒由下滑入；遮罩淡入
            _sheetT = Mathf.MoveTowards(_sheetT, _sheetOpen ? 1f : 0f, dt / 0.3f);
            if (_sheetLayer.gameObject.activeSelf)
            {
                float e = 1f - Mathf.Pow(1f - _sheetT, 3f);
                _sheetDim.color = new Color(DimColor.r, DimColor.g, DimColor.b, 0.5f * Mathf.Clamp01(_sheetT / 0.8f));
                _sheet.anchoredPosition = new Vector2(0f, -_sheet.sizeDelta.y * _k * (1f - e));
                if (!_sheetOpen && _sheetT <= 0f) _sheetLayer.gameObject.SetActive(false);
                if (_detailPet != null) _detailPet.anchoredPosition = new Vector2(97f, -26f - 6f * (0.5f - 0.5f * Mathf.Cos(Time.unscaledTime / 2.4f * Mathf.PI * 2f)));
            }

            // 還沒收到：0.3 秒彈出（0.5 → 1.08 → 1）
            _popT = Mathf.MoveTowards(_popT, _popOpen ? 1f : 0f, dt / (_popOpen ? 0.3f : 0.15f));
            if (_popLayer.gameObject.activeSelf)
            {
                _popDim.color = new Color(DimColor.r, DimColor.g, DimColor.b, 0.5f * _popT);
                if (_popCard != null) _popCard.localScale = Vector3.one * Pop(_popT);
                if (!_popOpen && _popT <= 0f) _popLayer.gameObject.SetActive(false);
            }

            // 收到新的一格：依序彈出；光芒 12 秒一圈
            if (_newOpen)
            {
                _newT += dt;
                _newGroup.alpha = Mathf.Clamp01(_newT / 0.3f);
                if (_rays != null) _rays.localRotation = Quaternion.Euler(0f, 0f, -_newT / 12f * 360f);
                if (_newPop != null)
                    for (int i = 0; i < _newPop.Length; i++)
                        if (_newPop[i] != null) _newPop[i].localScale = Vector3.one * Pop(Mathf.Clamp01((_newT - 0.1f * i) / 0.45f));
            }
        }

        static float Pop(float t)
        {
            // 0 → 0.7：0.5 → 1.08；0.7 → 1：1.08 → 1
            if (t <= 0f) return 0.5f;
            if (t < 0.7f) return Mathf.Lerp(0.5f, 1.08f, t / 0.7f);
            return Mathf.Lerp(1.08f, 1f, (t - 0.7f) / 0.3f);
        }

        void Relayout()
        {
            float w = _root.rect.width, h = _root.rect.height;
            var safe = Screen.safeArea;
            if (w <= 0f || h <= 0f) return;
            if (Mathf.Approximately(w, _lastW) && Mathf.Approximately(h, _lastH) && safe == _lastSafe) return;
            _lastW = w; _lastH = h; _lastSafe = safe;
            _k = w / DesignW;
            float scale = _canvas.rootCanvas.scaleFactor;
            float px = scale > 0f ? 1f / scale : 1f;
            _topInset = (Screen.height - safe.yMax) * px / _k;   // 設計單位
            _bottomInset = safe.yMin * px / _k;
            _screenH = h / _k;

            foreach (var d in new[] { _design, _popDesign, _newDesign })
            {
                d.localScale = Vector3.one * _k;
                d.sizeDelta = new Vector2(DesignW, _screenH - _topInset);
                d.anchoredPosition = new Vector2(0f, -_topInset * _k); // 位移是畫布單位
            }
            // 頂部底色延伸到瀏海後面
            _topFill.sizeDelta = new Vector2(0f, (_topInset + HeaderH) * _k);
            _topImg.pixelsPerUnitMultiplier = 1f / _k; // 頂部底不在設計座標裡：邊界跟著縮放
            _listView.sizeDelta = new Vector2(DesignW, Mathf.Max(100f, _screenH - _topInset - HeaderH));
            _sheet.localScale = Vector3.one * _k;
            LayoutNewButton();
        }

        // ─── 建立介面 ──────────────────────────────────────────────────

        void Build()
        {
            _skin = DexSkin.Load();
            if (_skin == null) Debug.LogWarning("[Dex] 找不到 Resources/UI/DexSkin，先用程式畫的形狀（執行 BlockPet/Dex/Build Dex Skin）");
            _font = _skin != null ? _skin.font : null;

            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 38; // 照片檢視 37 之上
            gameObject.AddComponent<GraphicRaycaster>();
            _root = (RectTransform)transform;

            _base = Stretch(_root, "Base").gameObject.AddComponent<Image>();
            _base.color = Bg;

            _topFill = new GameObject("TopFill", typeof(RectTransform)).GetComponent<RectTransform>();
            _topFill.SetParent(_root, false);
            _topFill.anchorMin = new Vector2(0f, 1f);
            _topFill.anchorMax = new Vector2(1f, 1f);
            _topFill.pivot = new Vector2(0.5f, 1f);
            _topImg = _topFill.gameObject.AddComponent<Image>();
            var topImg = _topImg;
            topImg.raycastTarget = false;
            if (_skin != null && _skin.headerBg != null) { topImg.sprite = _skin.headerBg; topImg.type = Image.Type.Sliced; }
            else
            {
                topImg.color = HeaderFill;
                var line = new GameObject("Line", typeof(RectTransform)).GetComponent<RectTransform>();
                line.SetParent(_topFill, false);
                line.anchorMin = Vector2.zero; line.anchorMax = new Vector2(1f, 0f);
                line.pivot = new Vector2(0.5f, 0f);
                line.sizeDelta = new Vector2(0f, 1f);
                line.gameObject.AddComponent<Image>().color = HeaderLine;
            }

            _design = DesignRoot("Design");
            BuildHeader();
            BuildList();
            BuildSheet();
            BuildPop();
            BuildNew();
            gameObject.SetActive(false);
        }

        RectTransform DesignRoot(string name)
        {
            var d = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            d.SetParent(_root, false);
            d.anchorMin = d.anchorMax = new Vector2(0.5f, 1f);
            d.pivot = new Vector2(0.5f, 1f);
            d.sizeDelta = new Vector2(DesignW, DesignH);
            return d;
        }

        void BuildHeader()
        {
            // 返回 (12, 14) 44 → 回營地
            var back = Place(_design, "Back", 12, 14, 44, 44);
            var backImg = Img(back, _skin?.back, false, Beige, 22);
            if (_skin == null || _skin.back == null) Label(back, 0, 0, 44, 44, "‹", 22, Ink, TextAlignmentOptions.Center);
            var bb = back.gameObject.AddComponent<Button>();
            bb.targetGraphic = backImg;
            backImg.raycastTarget = true; // Img() 預設不接點擊，按鈕的圖要打開
            bb.onClick.AddListener(Close);

            Label(_design, 68, 12, 200, 30, "圖鑑", 24, Ink, TextAlignmentOptions.Left);
            Label(_design, 68, 42, 220, 18, "家人營地・大家一起收集", 12, Brown, TextAlignmentOptions.Left);

            // 右上總進度「4 / 28」＋「已收集」
            _total = Label(_design, DesignW - Side - 140f, 14, 140, 28, "", 22, Ink, TextAlignmentOptions.Right);
            _total.richText = true;
            Label(_design, DesignW - Side - 140f, 42, 140, 14, "已收集", 10, Brown, TextAlignmentOptions.Right);
        }

        void BuildList()
        {
            _listView = Place(_design, "List", 0, HeaderH, DesignW, DesignH - HeaderH);
            _listView.gameObject.AddComponent<RectMask2D>();
            _listView.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            _listContent = Place(_listView, "Content", 0, 0, DesignW, 0);
            _listScroll = _listView.gameObject.AddComponent<ScrollRect>();
            _listScroll.content = _listContent;
            _listScroll.viewport = _listView;
            _listScroll.horizontal = false;
            _listScroll.movementType = ScrollRect.MovementType.Clamped;
            _listScroll.scrollSensitivity = 20f;
        }

        // ─── 主頁內容 ──────────────────────────────────────────────────

        void RebuildHome()
        {
            _total.text = $"{_book.TotalGot()}<size=14><color=#8A6A4E> / {DexBook.TotalMax}</color></size>";

            Clear(_listContent);
            float y = 14f;
            if (_loading && _book.TotalEntries() == 0)
            {
                Label(_listContent, 0, 40, DesignW, 20, "讀取中…", 13, Brown, TextAlignmentOptions.Center);
                _listContent.sizeDelta = new Vector2(DesignW, 100f);
                return;
            }
            foreach (var s in _book.OrderedSeries())
            {
                y += BuildSeriesCard(s, y) + 14f;
            }
            _listContent.sizeDelta = new Vector2(DesignW, y + 16f);
            _listContent.anchoredPosition = Vector2.zero;
        }

        float BuildSeriesCard(DexSeries s, float top)
        {
            bool owned = _book.Owns(s.id); // 營地有這個品種（現在的寵物都算 BowBow）：系列說明用
            bool hasAny = owned || DexRules.ShowCellsForAllSeries; // 放 7 格（目前每個系列都放）
            int got = _book.Got(s.id);
            int rows = (DexRules.Forms.Length + Cols - 1) / Cols;
            float gridH = hasAny ? rows * CellH + (rows - 1) * CellGap : CellH;
            float h = 14f + 40f + 8f + 16f + 10f + gridH + 12f;

            var card = Place(_listContent, s.id, Side, top, CardW, h);
            UIShapes.AddShadow(card, new Color(30f / 255f, 30f / 255f, 20f / 255f, 0.08f), 22, 12, 4);
            Img(Stretch(card, "Bg"), _skin?.seriesCard, true, Cream, 22).raycastTarget = false;

            float ix = 12f + 2f, iy = 14f;
            // 品種蛋圖示 40 × 40
            var icon = Place(card, "Icon", ix, iy, 40, 40);
            Img(icon, _skin?.seriesIconBg, false, HexStr(s.iconBg), 12).color = HexStr(s.iconBg);
            var egg = Place(icon, "Egg", 8, 6.5f, 24, 27);
            if (_skin != null && _skin.egg != null)
            {
                Img(egg, _skin.egg, false, Cream, 0);
                if (_skin.eggZigzag != null) Img(Stretch(egg, "Zigzag"), _skin.eggZigzag, false, HexStr(s.eggLine), 0).color = HexStr(s.eggLine);
            }
            else UIShapes.AddRounded(egg.gameObject, Cream, 11).raycastTarget = false;

            // 名字＋季節限定＋收滿（收滿的系列標題旁一直留著金色「收滿」，設計文件 5.23 ①）
            float nx = ix + 50f, nw = CardW - nx - 14f;
            var name = Label(card, nx, iy - 2f, nw, 22, s.name, 16, Ink, TextAlignmentOptions.Left);
            float tx = nx + Mathf.Ceil(TextWidth(name, s.name)) + 6f;
            if (s.seasonal)
            {
                var tag = Place(card, "Season", tx, iy + 2f, 56, 16);
                Img(tag, _skin?.tagSeason, true, Ink, 8);
                Label(tag, 0, 0, 56, 16, "季節限定", 10, Color.white, TextAlignmentOptions.Center);
                tx += 56f + 6f;
            }
            if (_book.IsComplete(s.id))
            {
                var done = Place(card, "Complete", tx, iy + 2f, 36, 16);
                UIShapes.AddRounded(done.gameObject, Yellow, 8).raycastTarget = false;
                Label(done, 0, 0, 36, 16, "收滿", 10, Ink, TextAlignmentOptions.Center);
            }
            // 進度條＋「3 / 7」
            float barW = nw - 44f;
            var track = Place(card, "Track", nx, iy + 27f, barW, 6);
            Img(track, _skin?.progressTrack, true, HeaderLine, 3);
            if (got > 0)
            {
                var fill = Place(track, "Fill", 0, 0, Mathf.Max(6f, barW * got / DexRules.CellsPerSeries), 6);
                Img(fill, _skin?.progressFill, true, Yellow, 3);
            }
            Label(card, nx + barW + 6f, iy + 20f, 38, 20, $"{got} / {DexRules.CellsPerSeries}", 12, Ink, TextAlignmentOptions.Right);

            // 說明
            Label(card, 14, iy + 40f + 8f, CardInnerW, 16, DexRules.Note(s, owned, _book.IsComplete(s.id)), 11, Brown, TextAlignmentOptions.Left);

            // 格子
            float gy = iy + 40f + 8f + 16f + 10f;
            if (!hasAny) { ShopCell(card, s, 12f + (CardInnerW - CellW) / 2f, gy); return h; } // 置中
            int n = DexRules.Forms.Length;
            for (int i = 0; i < n; i++)
            {
                // 每排 Cols 格；最後一排不滿時置中
                int row = i / Cols, inRow = Mathf.Min(Cols, n - row * Cols);
                float rowW = inRow * CellW + (inRow - 1) * CellGap;
                float cx = 12f + (CardInnerW - rowW) / 2f + (i % Cols) * (CellW + CellGap);
                float cy = gy + row * (CellH + CellGap);
                Cell(card, s, DexRules.Forms[i], cx, cy);
            }
            return h;
        }

        /// <summary>
        /// 一格（設計文件 5.23 ③，11-05 改）：沒有底色、沒有框，只放寵物或剪影；點的範圍還是整格 78 × 104。
        /// 收到：寵物＋名字（稀有型左上「稀有」、收過 2 次以上右上「×N」）；還沒收到：只有剪影，點了才看名字和怎麼養。
        /// </summary>
        void Cell(RectTransform card, DexSeries s, DexForm f, float x, float y)
        {
            int count = _book.Count(s.id, f.key);
            bool has = count > 0;
            var rt = Place(card, f.key, x, y, CellW, CellH);

            // 透明的點擊範圍
            var hit = rt.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);

            // 寵物 66 × 57、名字在下方（沒收到時位置一樣，只是不寫名字，格子才對得齊）
            const float nameTop = 84f;
            var pet = Place(rt, "Pet", (CellW - 66f) / 2f, nameTop - 4f - 57f, 66, 57);
            PetImage(pet, f.key, !has);

            if (has)
            {
                Label(rt, 0, nameTop, CellW, 13, f.name, 11, Ink, TextAlignmentOptions.Center);
            }
            if (count > 1)
            {
                string t = "×" + count;
                var probe = Label(rt, 0, 0, 10, 16, t, 10, Color.white, TextAlignmentOptions.Center);
                float w = Mathf.Ceil(TextWidth(probe, t)) + 12f;
                var badge = Place(rt, "Count", CellW - 6f - w, 6, w, 16);
                Img(badge, _skin?.badgeCount, true, Ink, 8);
                probe.rectTransform.SetParent(badge, false);
                probe.rectTransform.anchoredPosition = Vector2.zero;
                probe.rectTransform.sizeDelta = new Vector2(w, 16f);
            }

            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = hit;
            b.transition = Selectable.Transition.None;
            string sid = s.id, fk = f.key;
            b.onClick.AddListener(() => { if (has) OpenDetail(sid, fk, 0); else OpenLocked(fk); });
        }

        void ShopCell(RectTransform card, DexSeries s, float x, float y)
        {
            var rt = Place(card, "Shop", x, y, CellW, CellH);
            var bg = Img(Stretch(rt, "Bg"), _skin?.shopCell, true, new Color(1, 1, 1, 0), 16);
            if (_skin == null || _skin.shopCell == null) UIShapes.AddRing(Place(rt, "Dash", 0, 0, CellW, CellH).gameObject, Line, 16);
            var bag = Place(rt, "Bag", (CellW - 20f) / 2f, 28, 20, 20);
            if (_skin != null && _skin.shopBag != null) Img(bag, _skin.shopBag, false, Brown, 0);
            var t = Label(rt, 4, 52, CellW - 8f, 30, DexRules.ShopCta(s), 10, Brown, TextAlignmentOptions.Top);
            t.enableWordWrapping = true;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            bg.raycastTarget = true; // Img() 預設不接點擊，按鈕的圖要打開
            b.onClick.AddListener(() =>
            {
                Close();
                BlockPet.UI.Market.MarketScreenController.OpenAt("egg_forest"); // 商店的寵物蛋分頁（5.11）
            });
        }

        void PetImage(RectTransform rt, string formKey, bool silhouette)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            img.sprite = _skin != null ? _skin.petPlaceholder : null;
            if (img.sprite == null) { img.enabled = false; return; }
            // 剪影：寵物圖上成黑色、透明度 22%（設計文件 5.23 ⑦；Image 顏色是乘上去的，乘黑色就全黑，只留形狀）
            img.color = silhouette ? Silhouette : (FormTint.TryGetValue(formKey, out var c) ? c : Color.white);
        }

        // ─── 詳細（下方滑出）──────────────────────────────────────────

        void BuildSheet()
        {
            _sheetLayer = Stretch(_root, "DetailLayer");
            _sheetDim = _sheetLayer.gameObject.AddComponent<Image>();
            _sheetDim.color = new Color(DimColor.r, DimColor.g, DimColor.b, 0f);
            var db = _sheetLayer.gameObject.AddComponent<Button>();
            db.transition = Selectable.Transition.None;
            db.onClick.AddListener(() => CloseSheet(false));

            _sheet = new GameObject("Sheet", typeof(RectTransform)).GetComponent<RectTransform>();
            _sheet.SetParent(_sheetLayer, false);
            _sheet.anchorMin = _sheet.anchorMax = new Vector2(0.5f, 0f);
            _sheet.pivot = new Vector2(0.5f, 0f);
            _sheet.sizeDelta = new Vector2(DesignW, 500f);
            _sheetLayer.gameObject.SetActive(false);
        }

        void OpenDetail(string seriesId, string formKey, int index)
        {
            var list = _book.Entries(seriesId, formKey);
            if (list.Count == 0) return;
            _detailSeries = seriesId;
            _detailForm = formKey;
            _detailIndex = ((index % list.Count) + list.Count) % list.Count;
            bool wasOpen = _sheetOpen && _sheetLayer.gameObject.activeSelf;
            BuildDetailContent();
            _sheetLayer.gameObject.SetActive(true);
            _sheetOpen = true;
            if (!wasOpen) _sheetT = 0f;
        }

        void BuildDetailContent()
        {
            Clear(_sheet);
            _detailPet = null;
            var s = DexRules.FindSeries(_detailSeries);
            var f = DexRules.Form(_detailForm);
            var list = _book.Entries(_detailSeries, _detailForm);
            var e = list[_detailIndex];
            bool multi = list.Count > 1;

            var panel = Stretch(_sheet, "Panel");
            Img(panel, _skin?.sheetPanel, true, Paper, 26).raycastTarget = true; // 擋住，點面板不會關

            const float L = 18f, W = DesignW - 36f;
            var grab = Place(_sheet, "Grabber", (DesignW - 40f) / 2f, 12, 40, 5);
            Img(grab, _skin?.sheetGrabber, false, Line, 2.5f);

            var close = Place(_sheet, "Close", DesignW - 14f - 44f, 12, 44, 44);
            var closeImg = Img(close, _skin?.close, false, Beige, 22);
            if (_skin == null || _skin.close == null) Label(close, 0, 0, 44, 44, "×", 18, Ink, TextAlignmentOptions.Center);
            var cb = close.gameObject.AddComponent<Button>();
            cb.targetGraphic = closeImg;
            closeImg.raycastTarget = true; // Img() 預設不接點擊，按鈕的圖要打開
            cb.onClick.AddListener(() => CloseSheet(false));

            Label(_sheet, L, 23, W - 50f, 17, s.name, 12, Brown, TextAlignmentOptions.Left);
            Label(_sheet, L, 42, W - 50f, 34, f.name + " " + s.shortName, 26, Ink, TextAlignmentOptions.Left);

            // 大圖 354 × 200：營地底圖＋寵物上下輕跳
            float y = 88f;
            var hero = Place(_sheet, "Hero", L, y, W, 200);
            var mask = Img(hero, _skin?.photoMask, true, Hex(0x8FA868), 20);
            mask.color = Hex(0x8FA868);
            hero.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var camp = _skin != null ? _skin.campBackground : null;
            if (camp != null)
            {
                var raw = Stretch(hero, "Camp").gameObject.AddComponent<RawImage>();
                raw.texture = camp;
                raw.raycastTarget = false;
                // 設計稿：底圖寬 354、往上移 400／700，看到下半部的草地
                float scaledH = W * camp.height / Mathf.Max(1f, camp.width);
                float offset = Mathf.Clamp(scaledH * 400f / 700f, 0f, Mathf.Max(0f, scaledH - 200f));
                float vh = Mathf.Min(1f, 200f / scaledH);
                raw.uvRect = new Rect(0f, Mathf.Clamp01(1f - offset / scaledH - vh), 1f, vh);
            }
            var shadow = Place(hero, "Shadow", 117, 168, 120, 18);
            UIShapes.AddRounded(shadow.gameObject, new Color(30f / 255f, 45f / 255f, 20f / 255f, 0.3f), 9).raycastTarget = false;
            _detailPet = Place(hero, "Pet", 97, 26, 160, 148);
            PetImage(_detailPet, f.key, false);

            if (multi)
            {
                Arrow(hero, 8, false, () => OpenDetail(_detailSeries, _detailForm, _detailIndex - 1));
                Arrow(hero, W - 8f - 44f, true, () => OpenDetail(_detailSeries, _detailForm, _detailIndex + 1));
            }
            string gen = $"第 {e.gen} 代";
            var genProbe = Label(hero, 0, 0, 10, 20, gen, 11, Ink, TextAlignmentOptions.Center);
            float gw = Mathf.Ceil(TextWidth(genProbe, gen)) + 18f;
            var pill = Place(hero, "Gen", 10, 10, gw, 20);
            Img(pill, _skin?.genPill, true, new Color(1f, 248f / 255f, 236f / 255f, 0.9f), 10);
            genProbe.rectTransform.SetParent(pill, false);
            genProbe.rectTransform.anchoredPosition = Vector2.zero;
            genProbe.rectTransform.sizeDelta = new Vector2(gw, 20f);

            // 兩格資訊
            y += 200f + 12f;
            float bw = (W - 10f) / 2f;
            string date = e.evolvedAt > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(e.evolvedAt).ToLocalTime().ToString("yyyy-MM-dd") : "—";
            InfoBox(_sheet, L, y, bw, "進化日期", date);
            InfoBox(_sheet, L + bw + 10f, y, bw, "養了幾天才進化", e.days > 0 ? e.days + " 天" : "—");

            // 調色盤
            y += 56f + 10f;
            float palH = Palette(_sheet, L, y, W, e.palette);
            y += palH;

            // 第幾代位置點
            if (multi)
            {
                y += 12f;
                float total = 18f + (list.Count - 1) * 6f + (list.Count - 1) * 6f;
                float x = (DesignW - total) / 2f;
                for (int i = 0; i < list.Count; i++)
                {
                    bool on = i == _detailIndex;
                    var d = Place(_sheet, "Dot", x, y, on ? 18f : 6f, 6f);
                    Img(d, on ? _skin?.pageDotOn : _skin?.pageDot, on, on ? Ink : new Color(Ink.r, Ink.g, Ink.b, 0.25f), 3);
                    x += (on ? 18f : 6f) + 6f;
                }
                y += 6f;
            }
            y += 26f + _bottomInset;
            _sheet.sizeDelta = new Vector2(DesignW, y);
        }

        void Arrow(RectTransform hero, float x, bool next, Action onClick)
        {
            var rt = Place(hero, next ? "Next" : "Prev", x, 78, 44, 44);
            Image img;
            if (_skin != null && _skin.arrow != null)
            {
                img = Img(rt, _skin.arrow, false, Color.white, 0);
                if (next) rt.localScale = new Vector3(-1f, 1f, 1f); // 下一代：水平翻轉
            }
            else
            {
                img = UIShapes.AddRounded(rt.gameObject, new Color(0f, 0f, 0f, 0.25f), 22);
                Label(rt, 0, 0, 44, 44, next ? "›" : "‹", 20, Color.white, TextAlignmentOptions.Center);
            }
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            img.raycastTarget = true; // Img() 預設不接點擊，按鈕的圖要打開
            b.onClick.AddListener(() => onClick());
        }

        void InfoBox(RectTransform parent, float x, float y, float w, string label, string value)
        {
            var rt = Place(parent, "Info", x, y, w, 56);
            Img(rt, _skin?.infoBox, true, Cream, 14);
            Label(rt, 12, 9, w - 24f, 14, label, 10, Brown, TextAlignmentOptions.Left);
            Label(rt, 12, 25, w - 24f, 22, value, 15, Ink, TextAlignmentOptions.Left);
        }

        /// <summary>進化時的調色盤：五色條＋每色圓點和百分比（比例 0 的不顯示）。回傳高度。</summary>
        float Palette(RectTransform parent, float x, float y, float w, int[] pal)
        {
            var box = Place(parent, "Palette", x, y, w, 80);
            Img(box, _skin?.infoBox, true, Cream, 14);
            Label(box, 12, 10, w - 24f, 14, "進化時的調色盤（100 張照片）", 10, Brown, TextAlignmentOptions.Left);

            int sum = 0;
            if (pal != null) foreach (var p in pal) sum += Mathf.Max(0, p);
            float innerW = w - 24f;
            var bar = Place(box, "Bar", 12, 31, innerW, 12);
            var barImg = Img(bar, _skin?.paletteMask, true, Color.white, 6);
            bar.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            float bx = 0f;
            var shown = new List<int>();
            for (int i = 0; i < PaletteColors.Length; i++) if (pal != null && i < pal.Length && pal[i] > 0) shown.Add(i);
            float usable = innerW - Mathf.Max(0, shown.Count - 1); // 色段之間 1px
            foreach (int i in shown)
            {
                float sw = sum > 0 ? usable * pal[i] / sum : 0f;
                var seg = Place(bar, PaletteNames[i], bx, 0, sw, 12).gameObject.AddComponent<Image>();
                seg.color = PaletteColors[i];
                seg.raycastTarget = false;
                bx += sw + 1f;
            }
            if (shown.Count == 0) barImg.color = HeaderLine;

            // 圖例：一行放不下就換行（間距 4／12）
            float lx = 0f, ly = 50f;
            foreach (int i in shown)
            {
                string t = $"{PaletteNames[i]} {pal[i]}%";
                var text = Label(box, 0, 0, 10, 16, t, 11, Ink, TextAlignmentOptions.Left);
                float tw = Mathf.Ceil(TextWidth(text, t));
                float itemW = 8f + 4f + tw;
                if (lx > 0f && lx + itemW > innerW) { lx = 0f; ly += 20f; }
                var dot = Place(box, "Dot", 12f + lx, ly + 4f, 8, 8);
                Img(dot, _skin?.dot, false, PaletteColors[i], 4).color = PaletteColors[i];
                text.rectTransform.anchoredPosition = new Vector2(12f + lx + 12f, -ly);
                text.rectTransform.sizeDelta = new Vector2(tw + 2f, 16f);
                lx += itemW + 12f;
            }
            float h = ly + 16f + 10f;
            box.sizeDelta = new Vector2(w, h);
            return h;
        }

        void CloseSheet(bool instant)
        {
            _sheetOpen = false;
            if (instant && _sheetLayer != null) { _sheetT = 0f; _sheetLayer.gameObject.SetActive(false); }
        }

        // ─── 還沒收到（中間小卡）──────────────────────────────────────

        void BuildPop()
        {
            _popLayer = Stretch(_root, "LockedLayer");
            _popDim = _popLayer.gameObject.AddComponent<Image>();
            _popDim.color = new Color(DimColor.r, DimColor.g, DimColor.b, 0f);
            var db = _popLayer.gameObject.AddComponent<Button>();
            db.transition = Selectable.Transition.None;
            db.onClick.AddListener(() => ClosePop(false));
            _popDesign = DesignRoot("LockedDesign");
            _popDesign.SetParent(_popLayer, false);
            _popLayer.gameObject.SetActive(false);
        }

        void OpenLocked(string formKey)
        {
            var f = DexRules.Form(formKey);
            if (f == null) return;
            _popLayer.gameObject.SetActive(true); // 先打開再建：文字要在啟用狀態才量得到幾行高
            Clear(_popDesign);
            const float W = DesignW - 60f, Inner = W - 36f;
            _popCard = Place(_popDesign, "Card", 30, 230, W, 300);
            UIShapes.AddShadow(_popCard, new Color(0f, 0f, 0f, 0.3f), 24, 40, 16);
            Img(Stretch(_popCard, "Bg"), _skin?.popupCard, true, Paper, 24).raycastTarget = true; // 點卡片不會關

            float y = 20f;
            var pet = Place(_popCard, "Pet", (W - 120f) / 2f, y, 120, 104);
            PetImage(pet, f.key, true);
            y += 104f + 4f;
            Label(_popCard, 18, y, Inner, 17, "還沒收到", 12, Brown, TextAlignmentOptions.Center);
            y += 17f + 2f;
            Label(_popCard, 18, y, Inner, 32, f.name, 24, Ink, TextAlignmentOptions.Center);
            y += 32f + 8f;
            var how = Label(_popCard, 18, y, Inner, 20, f.how, 13, HowText, TextAlignmentOptions.Top);
            how.lineSpacing = 10f;
            float hh = Mathf.Ceil(TextHeight(how, f.how, Inner));
            how.rectTransform.sizeDelta = new Vector2(Inner, hh);
            y += hh + 16f;

            var btn = Place(_popCard, "OK", 18, y, Inner, 48);
            Image bimg;
            if (_skin != null && _skin.buttonPrimary != null)
            {
                // 圖含下緣 3：比按鈕高 3
                var art = Place(btn, "Art", 0, 0, Inner, 51);
                bimg = Img(art, _skin.buttonPrimary, true, Yellow, 15);
            }
            else
            {
                UIShapes.AddRounded(Place(btn, "Edge", 0, 3, Inner, 48).gameObject, YellowEdge, 15).raycastTarget = false;
                bimg = UIShapes.AddRounded(btn.gameObject, Yellow, 15);
            }
            Label(btn, 0, 0, Inner, 48, "知道了", 15, Ink, TextAlignmentOptions.Center);
            var b = btn.gameObject.AddComponent<Button>();
            b.targetGraphic = bimg;
            bimg.raycastTarget = true; // Img() 預設不接點擊，按鈕的圖要打開
            b.onClick.AddListener(() => ClosePop(false));
            y += 48f + 18f;

            _popCard.sizeDelta = new Vector2(W, y);
            _popCard.pivot = new Vector2(0.5f, 0.5f);
            _popCard.anchoredPosition = new Vector2(30f + W / 2f, -(230f + y / 2f));
            _popLayer.gameObject.SetActive(true);
            _popOpen = true;
            _popT = 0f;
        }

        void ClosePop(bool instant)
        {
            _popOpen = false;
            if (instant && _popLayer != null) { _popT = 0f; _popLayer.gameObject.SetActive(false); }
        }

        // ─── 收到新的一格 ──────────────────────────────────────────────

        RectTransform _newButton;

        void BuildNew()
        {
            _newLayer = Stretch(_root, "NewLayer");
            _newGroup = _newLayer.gameObject.AddComponent<CanvasGroup>();
            var dim = _newLayer.gameObject.AddComponent<Image>();
            dim.color = new Color(DimColor.r, DimColor.g, DimColor.b, 0.7f);
            _newDesign = DesignRoot("NewDesign");
            _newDesign.SetParent(_newLayer, false);
            _newLayer.gameObject.SetActive(false);
        }

        void OpenNew(string seriesId, string formKey, FirebaseManager.DexRecordResult r)
        {
            var s = DexRules.FindSeries(seriesId);
            var f = DexRules.Form(formKey);
            if (s == null || f == null) return;
            // 從營地進化後跳出來：只蓋在營地上，不顯示圖鑑主頁；在圖鑑裡收到就蓋在主頁上
            if (!gameObject.activeSelf) SetHomeVisible(false);
            gameObject.SetActive(true);
            // 先打開（透明）再建：文字要在啟用狀態才量得到寬度，不然標籤、金幣膠囊的底框會比字短（2026-11-14 修）
            _newGroup.alpha = 0f;
            _newLayer.gameObject.SetActive(true);
            Clear(_newDesign);

            // 光芒 300 圓，(45, 150)，12 秒一圈
            _rays = Place(_newDesign, "Rays", 45, 150, 300, 300);
            _rays.pivot = new Vector2(0.5f, 0.5f);
            _rays.anchoredPosition = new Vector2(45f + 150f, -(150f + 150f));
            if (_skin != null && _skin.newRays != null) Img(_rays, _skin.newRays, false, Color.white, 0);

            var pop = new List<RectTransform>();

            // 「圖鑑新增！」或「BowBow 系列收滿了！」
            string tagText = r.seriesComplete ? $"{s.name}收滿了！" : "圖鑑新增！";
            var tagProbe = Label(_newDesign, 0, 0, 10, 24, tagText, 13, Color.white, TextAlignmentOptions.Center);
            float tw = Mathf.Ceil(TextWidth(tagProbe, tagText)) + 24f;
            var tag = Centered(_newDesign, "Tag", 96, tw, 26);
            if (_skin != null && _skin.newTag != null) Img(tag, _skin.newTag, true, Coral, 12);
            else UIShapes.AddRounded(tag.gameObject, Coral, 12).raycastTarget = false;
            tagProbe.rectTransform.SetParent(tag, false);
            tagProbe.rectTransform.anchoredPosition = Vector2.zero;
            tagProbe.rectTransform.sizeDelta = new Vector2(tw, 24f);
            pop.Add(tag);

            var pet = Centered(_newDesign, "Pet", 192, 220, 196);
            PetImage(pet, f.key, false);
            pop.Add(pet);

            Label(_newDesign, 0, 422, DesignW, 18, $"{s.name}・第 {r.got} 格", 13, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.Center);
            var name = Label(_newDesign, 0, 440, DesignW, 46, f.name, 34, Color.white, TextAlignmentOptions.Center);

            // 「全營地 +100 金幣」
            string reward = $"全營地 +{EconomyRules.DexNewCellCoins} 金幣";
            var rProbe = Label(_newDesign, 0, 0, 10, 32, reward, 14, Ink, TextAlignmentOptions.Left);
            float rw = 14f + 18f + 6f + Mathf.Ceil(TextWidth(rProbe, reward)) + 14f;
            var pill = Centered(_newDesign, "Reward", 496, rw, 32);
            Img(pill, _skin?.rewardPill, true, new Color(1f, 248f / 255f, 236f / 255f, 0.95f), 16);
            var coin = Place(pill, "Coin", 14, 7, 18, 18);
            if (_skin != null && _skin.coin != null) Img(coin, _skin.coin, false, Yellow, 9);
            else UIShapes.AddRounded(coin.gameObject, Hex(0xFFD54F), 9).raycastTarget = false;
            rProbe.rectTransform.SetParent(pill, false);
            rProbe.rectTransform.anchoredPosition = new Vector2(38f, 0f);
            rProbe.rectTransform.sizeDelta = new Vector2(rw - 38f, 32f);
            pop.Add(pill);

            string line2 = r.seriesComplete
                ? "送你免費抽一次寵物蛋"
                : $"{s.name} {r.got} / {DexRules.CellsPerSeries}・收滿送免費抽一次寵物蛋";
            Label(_newDesign, 0, 536, DesignW, 18, line2, 12, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.Center);

            // 「看圖鑑」左右 40、下 44、高 54
            _newButton = Place(_newDesign, "Go", 40, 0, DesignW - 80f, 54);
            Image bimg;
            if (_skin != null && _skin.buttonPrimaryLarge != null)
                bimg = Img(Place(_newButton, "Art", 0, 0, DesignW - 80f, 58), _skin.buttonPrimaryLarge, true, Yellow, 18);
            else
            {
                UIShapes.AddRounded(Place(_newButton, "Edge", 0, 4, DesignW - 80f, 54).gameObject, YellowEdge, 18).raycastTarget = false;
                bimg = UIShapes.AddRounded(_newButton.gameObject, Yellow, 18);
            }
            // 收滿系列：「去抽蛋」直接進抽 1 次（5.24 ③，不扣金幣）；其他：「看圖鑑」
            Label(_newButton, 0, 0, DesignW - 80f, 54, r.seriesComplete ? "去抽蛋" : "看圖鑑", 17, Ink, TextAlignmentOptions.Center);
            var b = _newButton.gameObject.AddComponent<Button>();
            b.targetGraphic = bimg;
            bimg.raycastTarget = true; // Img() 預設不接點擊，按鈕的圖要打開
            bool goDraw = r.seriesComplete;
            b.onClick.AddListener(() =>
            {
                _newOpen = false;
                _newLayer.gameObject.SetActive(false);
                if (goDraw)
                {
                    // 抽蛋畫面（5.24）還沒做：先開商店的寵物蛋分頁；免費那一次已經記在 Rooms/{room}/shop/free_egg_draw
                    Close();
                    BlockPet.UI.Market.MarketScreenController.OpenAt("egg_forest");
                }
                else ShowHome();
            });
            LayoutNewButton();

            _newPop = pop.ToArray();
            foreach (var p in _newPop) p.localScale = Vector3.one * 0.5f;
            _newT = 0f;
            _newGroup.alpha = 0f;
            _newOpen = true;
            _newLayer.gameObject.SetActive(true);
        }

        void SetHomeVisible(bool on)
        {
            _base.enabled = on;
            _topFill.gameObject.SetActive(on);
            _design.gameObject.SetActive(on);
        }

        void LayoutNewButton()
        {
            if (_newButton == null) return;
            float designH = _screenH - _topInset;
            _newButton.anchoredPosition = new Vector2(40f, -(designH - _bottomInset - 44f - 54f));
        }

        RectTransform Centered(RectTransform parent, string name, float top, float w, float h)
        {
            var rt = Place(parent, name, (DesignW - w) / 2f, top, w, h);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(DesignW / 2f, -(top + h / 2f));
            return rt;
        }

        /// <summary>量字寬：字型是動態的，沒出現過的字要先加進字型，不然量出來太窄、字超出底。</summary>
        static float TextWidth(TMP_Text t, string s)
        {
            if (t.font != null && !string.IsNullOrEmpty(s)) t.font.TryAddCharacters(s);
            return t.GetPreferredValues(s).x;
        }

        /// <summary>
        /// 多行文字的高度：設定寬度、開換行，讓 TextMeshPro 實際排一次版，量第一行頂到最後一行底。
        /// 文字所在的物件一定要已經啟用（未啟用時 TextMeshPro 量不出來，會只剩一行高）。
        /// （2026-11-08 修：原本面板「先建內容、再打開」，量到的都是一行高，下面的東西就壓到第二行以後；圖鑑說明則是沒開換行）
        /// </summary>
        static float TextHeight(TMP_Text t, string s, float width)
        {
            if (t.font != null && !string.IsNullOrEmpty(s)) t.font.TryAddCharacters(s);
            if (!t.gameObject.activeInHierarchy) Debug.LogWarning("[UI] 文字還沒啟用就量高度，會量不準（先 SetActive 再建）：" + s);
            var rt = t.rectTransform;
            var old = rt.sizeDelta;
            t.enableWordWrapping = true;
            t.text = s;
            rt.sizeDelta = new Vector2(width, 4000f);
            t.ForceMeshUpdate(true, true);
            var info = t.textInfo;
            float h = info != null && info.lineCount > 0
                ? info.lineInfo[0].ascender - info.lineInfo[info.lineCount - 1].descender
                : 0f;
            rt.sizeDelta = old;
            if (h <= 0f) h = t.fontSize * 1.3f; // 排不出來（例如字型還沒載入）：至少一行
            return h + 2f;
        }

        // ─── UI helpers（與其他新版頁面相同的設計座標寫法）────────────

        /// <summary>有素材就用素材（可 9-slice），沒有就用程式畫的圓角方塊。</summary>
        Image Img(RectTransform rt, Sprite sprite, bool sliced, Color fallback, float radius)
        {
            if (sprite != null)
            {
                var img = rt.gameObject.AddComponent<Image>();
                img.sprite = sprite;
                img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
                img.raycastTarget = false;
                return img;
            }
            var r = UIShapes.AddRounded(rt.gameObject, fallback, Mathf.Max(0.5f, radius));
            r.raycastTarget = false;
            return r;
        }

        TMP_Text Label(RectTransform parent, float left, float top, float w, float h, string value, float size, Color color, TextAlignmentOptions align)
        {
            var t = Place(parent, "Text", left, top, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.text = value;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.richText = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        static void Clear(RectTransform rt)
        {
            for (int i = rt.childCount - 1; i >= 0; i--) Destroy(rt.GetChild(i).gameObject);
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

        static RectTransform Stretch(Transform parent, string name)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        static Color HexStr(string s) => ColorUtility.TryParseHtmlString(s, out var c) ? c : Color.white;
    }
}
