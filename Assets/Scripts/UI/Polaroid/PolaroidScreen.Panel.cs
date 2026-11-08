using System.Collections;
using System.Collections.Generic;
using BlockPet.Hand;
using BlockPet.Polaroid;
using BlockPet.UI.Album;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Polaroid
{
    /// <summary>
    /// 拍立得分割畫面的下方面板（設計文件 5.26 ②③④，2026-11-07 晚上改版）：(14, 497) 362 × 199，兩頁左右滑切換（超過 40）。
    /// - 寵物頁（預設）：縮小的營地＋左上狀態膠囊（5 顆愛心、進化條）；照片是牠想吃的類別就跳，不是就歪頭，不說話。
    /// - 裝飾頁：「貼紙／畫框／濾鏡」小分頁＋工具區（在工具區上滑是捲動，不換頁）＋短滑動條。
    /// 下方 y 699 兩個頁數小點；卡片右下的編輯鈕也能切換。另外是提示、下載與分享。
    /// </summary>
    public partial class PolaroidScreen
    {
        enum Tab { Stickers, Frames, Filters }
        enum Page { Pet, Deco }

        const float PanelX = 14f, PanelY = 497f, PanelW = 362f, PanelH = 199f;
        const float TrayTop = 52f, Cell = 52f, Gap = 8f, TrayPad = 12f;
        const float ThumbW = 84f, ThumbH = 72f;

        static readonly Color CellBg = PUI.Hex(0xF4ECDD);

        Tab _tab;
        Page _page;
        float _pageT;                 // 0＝寵物頁、1＝裝飾頁（動畫用）
        RectTransform _panel, _pages, _decoPage;
        Image _dotA, _dotB;

        // 寵物頁
        RectTransform _petBody;
        readonly List<Image> _hearts = new List<Image>();
        RectTransform _evoFill;
        bool _petHit;
        float _petT;

        // 裝飾頁
        readonly RectTransform[] _tabs = new RectTransform[3];
        readonly Image[] _tabBgs = new Image[3];
        readonly TMP_Text[] _tabTexts = new TMP_Text[3];
        TMP_Text _tabNote;
        RectTransform _trayView, _trayContent, _scrollTrack, _scrollThumb;
        ScrollRect _trayScroll;
        CanvasGroup _trayGroup;
        bool _exporting;

        // 提示：深色小標（「只有小美能換畫框」）、存好的 toast
        RectTransform _hint, _toast;
        TMP_Text _hintText, _toastText;
        CanvasGroup _hintGroup, _toastGroup;
        Image _toastCheck;
        float _hintUntil, _toastUntil;

        static readonly string[] TabNames = { "貼紙", "畫框", "濾鏡" };

        // ─── 面板、換頁 ────────────────────────────────────────────────

        void BuildPanel()
        {
            _panel = PUI.Place(_design, "Panel", PanelX, PanelY, PanelW, PanelH);
            PUI.Hit(_panel);
            _panel.gameObject.AddComponent<RectMask2D>().softness = Vector2Int.zero;
            // 寵物頁的陰影會被裁掉一點，外面另外墊一層
            _pages = PUI.Place(_panel, "Pages", 0, 0, PanelW * 2f, PanelH);
            BuildPetPage(PUI.Place(_pages, "Pet", 0, 0, PanelW, PanelH));
            _decoPage = PUI.Place(_pages, "Deco", PanelW, 0, PanelW, PanelH);
            BuildDecoPage(_decoPage);

            // 左右滑超過 40 換頁（工具區的捲動列自己會吃掉拖曳，不會換頁）
            var swipe = _panel.gameObject.AddComponent<HandSwipe>();
            swipe.space = _design;
            swipe.canSwipe = () => !_input.isFocused;
            swipe.onEnd = dx => { if (dx < -40f) SetPage(Page.Deco, false); else if (dx > 40f) SetPage(Page.Pet, false); };

            // 頁數小點：置中、y 699、間距 6；現在那頁 16 × 5 長條，另一頁 5 × 5 圓點（25%）
            _dotA = UIShapes.AddRounded(PUI.Place(_design, "PageDotPet", 0, 699, 16, 5).gameObject, Ink, 2.5f);
            _dotB = UIShapes.AddRounded(PUI.Place(_design, "PageDotDeco", 0, 699, 5, 5).gameObject, Ink, 2.5f);
            _dotA.raycastTarget = _dotB.raycastTarget = false;
        }

        void SetPage(Page page, bool instant)
        {
            if (page == Page.Pet) Deselect();
            _page = page;
            if (instant) _pageT = page == Page.Deco ? 1f : 0f;
            if (_editDot != null) _editDot.enabled = page == Page.Deco;
            ApplyPage();
            SyncCaptionMode();
        }

        void ApplyPage()
        {
            float e = _pageT * _pageT * (3f - 2f * _pageT);
            _pages.anchoredPosition = new Vector2(-PanelW * e, 0f);
            // 頁數小點：長條 16 ↔ 圓點 5，0.2 秒
            float wa = Mathf.Lerp(16f, 5f, _pageT), wb = Mathf.Lerp(5f, 16f, _pageT);
            float x = (DesignW - (wa + 6f + wb)) / 2f;
            var on = Ink;
            var off = new Color(Ink.r, Ink.g, Ink.b, 0.25f);
            _dotA.rectTransform.anchoredPosition = new Vector2(x, -699f);
            _dotA.rectTransform.sizeDelta = new Vector2(wa, 5f);
            _dotA.color = Color.Lerp(on, off, _pageT);
            _dotB.rectTransform.anchoredPosition = new Vector2(x + wa + 6f, -699f);
            _dotB.rectTransform.sizeDelta = new Vector2(wb, 5f);
            _dotB.color = Color.Lerp(off, on, _pageT);
        }

        void UpdatePanel(float dt)
        {
            float target = _page == Page.Deco ? 1f : 0f;
            if (!Mathf.Approximately(_pageT, target))
            {
                _pageT = Mathf.MoveTowards(_pageT, target, dt / 0.2f);
                ApplyPage();
            }
            AnimatePet(dt);

            // 滑動條小點跟著捲動比例在 0～26 移動
            bool scrollable = _trayContent.sizeDelta.x > PanelW + 1f;
            _scrollTrack.gameObject.SetActive(scrollable);
            if (scrollable)
                _scrollThumb.anchoredPosition = new Vector2(26f * Mathf.Clamp01(_trayScroll.horizontalNormalizedPosition), 0f);

            _hintGroup.alpha = Mathf.MoveTowards(_hintGroup.alpha, Time.unscaledTime < _hintUntil ? 1f : 0f, dt / 0.15f);
            _toastGroup.alpha = Mathf.MoveTowards(_toastGroup.alpha, Time.unscaledTime < _toastUntil ? 1f : 0f, dt / 0.2f);
        }

        // ─── 寵物頁 ────────────────────────────────────────────────────

        void BuildPetPage(RectTransform page)
        {
            // 邊 3 #FFFCF4、圓角 20、陰影 0 8 20；裡面是營地底圖（356 × 704，往上偏移 372，看到地面）
            var inner = PUI.Place(page, "Mask", 3, 3, PanelW - 6f, PanelH - 6f);
            var maskImg = PUI.Fill(inner, _skin?.panelMask, true, Color.white, 17);
            inner.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var camp = PUI.Place(inner, "Camp", 0, -372f, 356, 704).gameObject.AddComponent<RawImage>();
            camp.texture = _skin != null ? _skin.camp : null;
            camp.raycastTarget = false;
            if (camp.texture != null) camp.uvRect = ChatAspectFill(camp.texture.width, camp.texture.height, 356f, 704f);
            else camp.color = PUI.Hex(0x8FA868);
            var frame = PUI.Fill(PUI.Stretch(page, "Frame"), _skin?.petFrame, true, new Color(0, 0, 0, 0), 20);
            frame.raycastTarget = false;
            if (_skin == null || _skin.petFrame == null) UIShapes.AddThinRing(PUI.Stretch(page, "Edge").gameObject, Cream, 20).raycastTarget = false;

            // 寵物：(128, 80) 100 × 88，腳下陰影 72 × 11
            UIShapes.AddRounded(PUI.Place(page, "Shadow", 128f + 14f, 80f + 88f - 8f, 72, 11).gameObject, new Color(30f / 255f, 45f / 255f, 20f / 255f, 0.3f), 5.5f).raycastTarget = false;
            _petBody = PUI.Place(page, "Pet", 128, 80, 100, 88);
            _petBody.pivot = new Vector2(0.5f, 0f); // 以腳底為軸
            _petBody.anchoredPosition = new Vector2(128f + 50f, -(80f + 88f));
            var pet = PUI.Stretch(_petBody, "Img").gameObject.AddComponent<Image>();
            pet.sprite = _skin != null ? _skin.pet : null;
            pet.preserveAspect = true;
            pet.raycastTarget = false;
            if (pet.sprite == null) pet.color = PUI.Hex(0xF6C945);

            // 左上 (8, 8) 狀態膠囊：只有 5 顆愛心（12 × 11）和進化條（84 × 7）；內距 5 10、間距 8
            const float heartsW = 5f * 12f + 4f * 1f;
            float pillW = 10f + heartsW + 8f + 84f + 10f;
            var pill = PUI.Place(page, "Status", 8, 8, pillW, 21);
            PUI.Fill(PUI.Stretch(pill, "Bg"), _skin?.statusPill, true, new Color(1f, 248f / 255f, 236f / 255f, 0.75f), 10.5f).raycastTarget = false;
            _hearts.Clear();
            for (int i = 0; i < 5; i++)
            {
                var h = PUI.Place(pill, "Heart", 10f + i * 13f, 5, 12, 11).gameObject.AddComponent<Image>();
                h.preserveAspect = true;
                h.raycastTarget = false;
                _hearts.Add(h);
            }
            var track = PUI.Place(pill, "Evo", 10f + heartsW + 8f, 7, 84, 7);
            UIShapes.AddRounded(track.gameObject, new Color(1f, 1f, 1f, 0.55f), 3.5f).raycastTarget = false;
            track.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            _evoFill = PUI.Stretch(track, "Segments");
        }

        /// <summary>寵物頁的資料：愛心、進化條色段、牠想不想吃這張照片（跳／歪頭）。</summary>
        void RefreshPetPage()
        {
            var cfg = _skin != null ? _skin.topBar : null;
            float hearts = _captureHearts >= 0f ? _captureHearts : (FindObjectOfType<PetHealthManager>() is PetHealthManager hm ? hm.Hearts : 5f);
            for (int i = 0; i < _hearts.Count; i++)
            {
                float v = hearts - i;
                var sp = cfg == null ? null : (v >= 1f ? cfg.heartFull : v >= 0.5f ? cfg.heartHalf : cfg.heartEmpty);
                _hearts[i].sprite = sp;
                _hearts[i].color = sp != null ? Color.white : (v >= 0.5f ? PUI.Hex(0xF06B5B) : PUI.Hex(0xE6D9C3));
            }

            // 進化條：五色分段（同 5.5），段與段緊貼不留白（2026-11-08 改）
            for (int i = _evoFill.childCount - 1; i >= 0; i--) Kill(_evoFill.GetChild(i).gameObject);
            if (cfg != null && cfg.segments.Count > 0)
            {
                var cats = cfg.segments.ConvertAll(x => x.category);
                float[] e = _captureEnergy ?? (PetCollectionManager.Instance != null ? PetCollectionManager.Instance.EnergyByCategory(cats) : null);
                float x = 0f;
                for (int i = 0; e != null && i < e.Length && i < cats.Count; i++)
                {
                    float w = 84f * Mathf.Clamp01(e[i] / 100f);
                    if (w < 0.5f) continue;
                    var seg = PUI.Place(_evoFill, "Seg", x, 0, w, 7).gameObject.AddComponent<Image>();
                    seg.color = cfg.segments[i].color;
                    seg.raycastTarget = false;
                    x += w;
                }
            }

            // 照片是牠「現在想吃」的類別 → 跳；其他 → 歪頭（只做動作、不說話）
            var m = Current;
            string want = _captureWant ?? BlockPet.Feed.TodayWant.Current();
            _petHit = m != null && !string.IsNullOrEmpty(m.category) && m.category == want;
            _petT = 0f;
        }

        void AnimatePet(float dt)
        {
            if (_petBody == null) return;
            _petT += dt;
            if (_petHit)
            {
                // 跳起：1 秒循環（35% 最高 −20，60% 落地稍微壓扁）
                float p = Mathf.Repeat(_petT, 1f), y = 0f, sx = 1f, sy = 1f;
                if (p < 0.35f) y = 20f * Mathf.Sin(p / 0.35f * Mathf.PI * 0.5f);
                else if (p < 0.6f) { y = 20f * Mathf.Cos((p - 0.35f) / 0.25f * Mathf.PI * 0.5f); float q = (p - 0.35f) / 0.25f; sx = Mathf.Lerp(1f, 1.06f, q); sy = Mathf.Lerp(1f, 0.94f, q); }
                else { float q = Mathf.Clamp01((p - 0.6f) / 0.15f); sx = Mathf.Lerp(1.06f, 1f, q); sy = Mathf.Lerp(0.94f, 1f, q); }
                _petBody.anchoredPosition = new Vector2(178f, -168f + y);
                _petBody.localScale = new Vector3(sx, sy, 1f);
                _petBody.localRotation = Quaternion.identity;
            }
            else
            {
                // 歪頭：2 秒循環 0° → 30% −8° → 60% +6° → 0°（CSS 角度，Unity 要變號）
                float p = Mathf.Repeat(_petT / 2f, 1f), css;
                if (p < 0.3f) css = Mathf.Lerp(0f, -8f, Mathf.SmoothStep(0f, 1f, p / 0.3f));
                else if (p < 0.6f) css = Mathf.Lerp(-8f, 6f, Mathf.SmoothStep(0f, 1f, (p - 0.3f) / 0.3f));
                else css = Mathf.Lerp(6f, 0f, Mathf.SmoothStep(0f, 1f, (p - 0.6f) / 0.4f));
                _petBody.anchoredPosition = new Vector2(178f, -168f);
                _petBody.localScale = Vector3.one;
                _petBody.localRotation = Quaternion.Euler(0f, 0f, -css);
            }
        }

        static Rect ChatAspectFill(float texW, float texH, float boxW, float boxH) =>
            BlockPet.UI.Chat.ChatScreenController.AspectFill(texW, texH, boxW, boxH);

        // ─── 裝飾頁 ────────────────────────────────────────────────────

        void BuildDecoPage(RectTransform page)
        {
            PUI.Fill(PUI.Stretch(page, "Bg"), _skin?.panelMask, true, Cream, 20).color = Cream;

            // 小分頁 (10, 8)：高 28、圓角 14、內距 0 12、13 Black
            float x = 10f;
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                var t = PUI.Place(page, "Tab" + TabNames[i], x, 8, 60, 28);
                _tabBgs[i] = PUI.Fill(PUI.Stretch(t, "Bg"), _skin?.dtabOff, true, Cream, 14);
                _tabTexts[i] = PUI.Label(_font, t, 12, 0, 40, 28, TabNames[i], 13, Sub, TextAlignmentOptions.Center);
                float tw = Mathf.Ceil(_tabTexts[i].GetPreferredValues(TabNames[i]).x);
                _tabTexts[i].rectTransform.sizeDelta = new Vector2(tw, 28f);
                t.sizeDelta = new Vector2(tw + 24f, 28f);
                PUI.MakeButton(t.gameObject, _tabBgs[i], () => { _tab = (Tab)k; BuildTray(true); });
                _tabs[i] = t;
                x += tw + 24f + 6f;
            }
            _tabNote = PUI.Label(_font, page, x + 2f, 8, PanelW - x - 12f, 28, "", 11, Note, TextAlignmentOptions.Left);

            // 工具區：左右滑（ScrollRect 自己吃掉拖曳，不會換頁）
            _trayView = PUI.Place(page, "Tray", 0, TrayTop - 4f, PanelW, PanelH - TrayTop - 12f);
            _trayView.gameObject.AddComponent<RectMask2D>();
            PUI.Hit(_trayView);
            _trayGroup = _trayView.gameObject.AddComponent<CanvasGroup>();
            _trayContent = PUI.Place(_trayView, "Content", 0, 0, PanelW, _trayView.sizeDelta.y);
            _trayScroll = _trayView.gameObject.AddComponent<ScrollRect>();
            _trayScroll.content = _trayContent;
            _trayScroll.viewport = _trayView;
            _trayScroll.vertical = false;
            _trayScroll.movementType = ScrollRect.MovementType.Elastic;
            _trayScroll.scrollSensitivity = 20f;

            // 最下方置中短滑動條：軌道 44 × 4、小點 18 × 4 在 0～26 移動（下 6）
            _scrollTrack = PUI.Place(page, "ScrollTrack", PanelW / 2f - 22f, PanelH - 6f - 4f, 44, 4);
            PUI.Fill(_scrollTrack, _skin?.scrollTrack, false, new Color(Ink.r, Ink.g, Ink.b, 0.12f), 2).raycastTarget = false;
            _scrollThumb = PUI.Place(_scrollTrack, "Thumb", 0, 0, 18, 4);
            PUI.Fill(_scrollThumb, _skin?.scrollThumb, false, new Color(Ink.r, Ink.g, Ink.b, 0.45f), 2).raycastTarget = false;
        }

        void RefreshTabs()
        {
            for (int i = 0; i < 3; i++)
            {
                bool on = (int)_tab == i;
                var sp = on ? _skin?.dtabOn : _skin?.dtabOff;
                if (sp != null) { _tabBgs[i].sprite = sp; _tabBgs[i].color = Color.white; }
                else _tabBgs[i].color = on ? Ink : new Color(Cream.r, Cream.g, Cream.b, 0.9f);
                _tabTexts[i].color = on ? PUI.Hex(0xFFF8EC) : Sub;
            }
            if (_tab == Tab.Stickers) _tabNote.text = $"還能貼 {PolaroidRules.StickersLeft(_stickers, Me)} 個";
            else _tabNote.text = "大家都會看到";
        }

        /// <summary>重建工具區；resetScroll＝回到最左邊（換分頁、換張時）。</summary>
        void BuildTray(bool resetScroll = false)
        {
            if (_trayContent == null) return;
            float keep = _trayScroll != null ? _trayScroll.horizontalNormalizedPosition : 0f;
            for (int i = _trayContent.childCount - 1; i >= 0; i--) Kill(_trayContent.GetChild(i).gameObject);
            RefreshTabs();
            _trayGroup.alpha = 1f;
            float w;
            switch (_tab)
            {
                case Tab.Frames: w = BuildFrameCells(); break;
                case Tab.Filters: w = BuildFilterCells(); break;
                default: w = BuildStickerCells(); break;
            }
            _trayContent.sizeDelta = new Vector2(Mathf.Max(PanelW, w), _trayContent.sizeDelta.y);
            _trayScroll.horizontalNormalizedPosition = resetScroll ? 0f : keep;
        }

        /// <summary>貼紙：兩排、每格 52（圓角 14、間距 8）；上排寵物貼紙（底 #FCEFC8），下排手畫貼紙（底 #F4ECDD）。貼滿整排變淡 40%。</summary>
        float BuildStickerCells()
        {
            bool full = !PolaroidRules.CanAddSticker(_stickers, Me);
            _trayGroup.alpha = full ? 0.4f : 1f;
            int cols = PolaroidRules.Tray.Length / 2;
            for (int i = 0; i < PolaroidRules.Tray.Length; i++)
            {
                string kind = PolaroidRules.Tray[i];
                bool pet = PolaroidRules.IsPetSticker(kind);
                int row = i / cols, col = i % cols;
                var c = PUI.Place(_trayContent, "Cell", TrayPad + col * (Cell + Gap), 4f + row * (Cell + Gap), Cell, Cell);
                var bg = PUI.Fill(PUI.Stretch(c, "Bg"), pet ? _skin?.cellPet : _skin?.cellAlt, true, pet ? PUI.Hex(0xFCEFC8) : CellBg, 14);
                var art = PolaroidStickerArt.Create(c, kind, pet ? 44f : 36f, _skin, out _);
                art.anchoredPosition = new Vector2((Cell - art.sizeDelta.x) / 2f, -(Cell - art.sizeDelta.y) / 2f);
                PUI.MakeButton(c.gameObject, bg, () => AddSticker(kind));
            }
            return TrayPad + cols * Cell + (cols - 1) * Gap + TrayPad;
        }

        /// <summary>畫框：10 種，兩排、格子 52（底 #F4ECDD）；小拍立得預覽 25 × 30＋名字 9 Black；選到外框 3 #F6C945。營地成員都能換。</summary>
        float BuildFrameCells()
        {
            bool owner = CanFrameFilter;
            int cols = (PolaroidRules.Frames.Length + 1) / 2;
            string cur = PolaroidRules.Frame(_style.frame).key;
            for (int i = 0; i < PolaroidRules.Frames.Length; i++)
            {
                var f = PolaroidRules.Frames[i];
                int row = i / cols, col = i % cols;
                var c = PUI.Place(_trayContent, "Frame_" + f.key, TrayPad + col * (Cell + Gap), 4f + row * (Cell + Gap), Cell, Cell);
                c.gameObject.AddComponent<CanvasGroup>().alpha = owner ? 1f : 0.5f;
                var bg = PUI.Fill(PUI.Stretch(c, "Bg"), _skin?.cellAlt, true, CellBg, 14);
                var mini = PUI.Place(c, "Mini", (Cell - 25f) / 2f, 5, 25, 30);
                var mb = mini.gameObject.AddComponent<Image>();
                mb.color = f.bg;
                mb.raycastTarget = false;
                var tile = _skin != null && f.tile != null ? _skin.FrameTile(f.tile) : null;
                if (tile != null)
                {
                    var ti = PUI.Stretch(mini, "Pattern").gameObject.AddComponent<Image>();
                    ti.sprite = tile; ti.type = Image.Type.Tiled; ti.raycastTarget = false;
                    ti.pixelsPerUnitMultiplier = 2f; // 縮小的花樣
                }
                var ph = PUI.Place(mini, "Photo", 2.5f, 2.5f, 20, 20).gameObject.AddComponent<Image>();
                ph.color = PUI.Hex(0xC9BFAF);
                ph.raycastTarget = false;
                if (mb.color.grayscale > 0.95f && tile == null) UIShapes.AddThinRing(PUI.Stretch(mini, "Edge").gameObject, PUI.Hex(0xE2D6C0), 1).raycastTarget = false;
                PUI.Label(_font, c, 0, 36, Cell, 14, f.name, 9, Ink, TextAlignmentOptions.Center);
                if (f.key == cur) Ring(c, Cell, Cell, 14f);
                string key = f.key;
                PUI.MakeButton(c.gameObject, bg, () => PickFrame(key));
            }
            return TrayPad + cols * Cell + (cols - 1) * Gap + TrayPad;
        }

        /// <summary>濾鏡：3 種，一排 84 × 72（用這張照片套濾鏡、圓角 13）＋名字 11 Black；選到外框 3 #F6C945；再點一次取消。</summary>
        float BuildFilterCells()
        {
            bool owner = CanFrameFilter;
            string cur = _style.filter;
            for (int i = 0; i < PolaroidRules.Filters.Length; i++)
            {
                var f = PolaroidRules.Filters[i];
                var c = PUI.Place(_trayContent, "Filter_" + f.key, TrayPad + i * (ThumbW + Gap), 56f - TrayTop + 4f, ThumbW, ThumbH + 20f);
                c.gameObject.AddComponent<CanvasGroup>().alpha = owner ? 1f : 0.5f;
                var holder = PUI.Place(c, "Thumb", 0, 0, ThumbW, ThumbH);
                var mask = UIShapes.AddRounded(holder.gameObject, PUI.Hex(0xE9E2D6), 13);
                holder.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                var raw = PUI.Stretch(holder, "Img").gameObject.AddComponent<RawImage>();
                raw.raycastTarget = false;
                raw.texture = _filterThumbs.TryGetValue(f.key, out var tex) ? tex : null;
                raw.enabled = raw.texture != null;
                FisheyeImage.Set(raw, raw.enabled && Current != null && Current.fisheye);
                if (tex != null) raw.uvRect = ChatAspectFill(tex.width, tex.height, ThumbW, ThumbH);
                var name = PUI.Label(_font, c, -6, ThumbH + 4f, ThumbW + 12f, 16, f.name, 11, Ink, TextAlignmentOptions.Center);
                name.overflowMode = TextOverflowModes.Overflow;
                if (f.key == cur) Ring(c, ThumbW, ThumbH, 13f);
                string key = f.key;
                PUI.MakeButton(c.gameObject, mask, () => PickFilter(key));
            }
            return TrayPad + PolaroidRules.Filters.Length * (ThumbW + Gap) - Gap + TrayPad;
        }

        /// <summary>選到的外框 3 #F6C945（外擴 3）。</summary>
        void Ring(RectTransform cell, float w, float h, float radius)
        {
            var r = PUI.Place(cell, "Ring", -3, -3, w + 6f, h + 6f);
            if (_skin != null && _skin.cellRing != null)
            {
                var img = r.gameObject.AddComponent<Image>();
                img.sprite = _skin.cellRing; img.type = Image.Type.Sliced; img.raycastTarget = false;
            }
            else UIShapes.AddRing(r.gameObject, Yellow, radius + 3f).raycastTarget = false;
        }

        void PickFrame(string key)
        {
            if (!CanFrameFilter) { ShowLocked("畫框"); return; }
            var m = Current;
            if (m == null || _style.frame == key) return;
            _style.frame = key;
            ApplyStyle();
            BuildTray();
            FirebaseManager.Instance?.SetPolaroidStyle(m.fileName, Copy(_style));
        }

        /// <summary>點濾鏡：換成這個；再點一次已選的就取消（回到沒有濾鏡）。</summary>
        void PickFilter(string key)
        {
            if (!CanFrameFilter) { ShowLocked("濾鏡"); return; }
            var m = Current;
            if (m == null) return;
            _style.filter = PolaroidRules.ToggleFilter(_style.filter, key);
            RefreshFiltered();
            ApplyStyle();
            BuildTray();
            FirebaseManager.Instance?.SetPolaroidStyle(m.fileName, Copy(_style));
        }

        void ShowLocked(string what) => ShowHint($"登入後才能換{what}", 1.6f);

        /// <summary>畫框、濾鏡：營地成員都能換（2026-11-08 使用者決定）。</summary>
        bool CanFrameFilter => Current != null && PolaroidRules.CanFrameFilter(Me);

        // ─── 提示 ──────────────────────────────────────────────────────

        void BuildToasts()
        {
            // 深色小標（面板中間）
            _hint = PUI.Place(_design, "Hint", 0, PanelY + 90f, 160, 30);
            _hintGroup = _hint.gameObject.AddComponent<CanvasGroup>();
            _hintGroup.alpha = 0f;
            _hintGroup.blocksRaycasts = false;
            PUI.Fill(PUI.Stretch(_hint, "Bg"), _skin?.tagDark, true, new Color(Ink.r, Ink.g, Ink.b, 0.9f), 10).raycastTarget = false;
            _hintText = PUI.Label(_font, _hint, 12, 0, 136, 30, "", 13, PUI.Hex(0xFFF8EC), TextAlignmentOptions.Center);

            // 「✓ 已存到手機相簿」：y 250，內距 11 18，15 Black 白
            _toast = PUI.Place(_design, "Toast", 0, 250, 220, 44);
            _toastGroup = _toast.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.alpha = 0f;
            _toastGroup.blocksRaycasts = false;
            PUI.Fill(PUI.Stretch(_toast, "Bg"), _skin?.toast, true, new Color(Ink.r, Ink.g, Ink.b, 0.9f), 18).raycastTarget = false;
            _toastCheck = PUI.Icon(_toast, _skin?.iconCheck, 18, 13, 18, Color.white);
            _toastText = PUI.Label(_font, _toast, 18, 0, 180, 44, "", 15, Color.white, TextAlignmentOptions.Left);
        }

        void ShowHint(string text, float seconds)
        {
            _hintText.text = text;
            float tw = Mathf.Ceil(_hintText.GetPreferredValues(text).x);
            _hintText.rectTransform.sizeDelta = new Vector2(tw, 30f);
            _hint.sizeDelta = new Vector2(tw + 24f, 30f);
            _hint.anchoredPosition = new Vector2((DesignW - tw - 24f) / 2f, _hint.anchoredPosition.y);
            _hintUntil = Time.unscaledTime + seconds;
        }

        void ShowToast(string text, bool check)
        {
            _toastText.text = text;
            _toastCheck.enabled = check && _toastCheck.sprite != null;
            float iconW = _toastCheck.enabled ? 18f + 6f : 0f;
            float tw = Mathf.Ceil(_toastText.GetPreferredValues(text).x);
            _toastText.rectTransform.anchoredPosition = new Vector2(18f + iconW, 0f);
            _toastText.rectTransform.sizeDelta = new Vector2(tw, 44f);
            float w = 18f + iconW + tw + 18f;
            _toast.sizeDelta = new Vector2(w, 44f);
            _toast.anchoredPosition = new Vector2((DesignW - w) / 2f, -250f);
            _toastUntil = Time.unscaledTime + 2.6f;
        }

        // ─── 下載、分享 ────────────────────────────────────────────────

        void Export(bool share)
        {
            if (_exporting || Current == null) return;
            if (_filtered == null) { ShowToast("照片還在讀取，等一下再試", false); return; }
            Deselect();
            StartCoroutine(ExportRoutine(share));
        }

        IEnumerator ExportRoutine(bool share)
        {
            _exporting = true;
            yield return null;
            byte[] jpg = null;
            try { jpg = PolaroidExport.Render(_filtered, _style, _stickers, _skin, _font, PhotoTime(Current), Current.fisheye); }
            catch (System.Exception e) { Debug.LogWarning("[Polaroid] 畫下載圖失敗：" + e.Message); }
            System.Action<bool, string> done = (ok, msg) =>
            {
                _exporting = false;
                if (ok && !share) { _flashT = 0f; _flash.enabled = true; }
                if (!string.IsNullOrEmpty(msg)) ShowToast(msg, ok && !share);
            };
            if (share) PhotoExport.ShareImage(jpg, done);
            else PhotoExport.SaveImage(jpg, done);
        }
    }
}
