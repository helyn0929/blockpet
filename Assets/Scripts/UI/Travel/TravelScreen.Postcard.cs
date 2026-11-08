using System;
using System.Collections;
using System.Collections.Generic;
using BlockPet.Hand;
using BlockPet.Polaroid;
using BlockPet.Postcard;
using BlockPet.Travel;
using BlockPet.UI.Album;
using BlockPet.UI.Polaroid;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Travel
{
    /// <summary>
    /// 寄明信片（設計文件 5.21 ④⑤；postcard_spec.json pick_sheet／passport；元件 Postcard 的 pick／reply／passport）：
    /// - 選照片面板（pc_sheet 高 640）：只看自己營地的照片，一排 3 張；有人臉的變灰、中間 pc_icon_noface＋「有人臉」、不能選；
    ///   上方提示條「有人臉的照片不能寄」；裝飾過的拍立得右下 pc_badge_deco。
    /// - 選了裝飾過的 → 直接顯示那張拍立得：「用這張拍立得寄出」／「再編輯」；沒裝飾過的 → 打開拍立得畫面（寄出模式）。
    /// - 寄出的是當下那一版（JPG），寵物照原本的路程走過去；回家時帶回一張對方營地的風景明信片（2026-11-08 使用者決定拿掉回寄，各寄各的）。
    /// - 護照：深藍底、內頁；每個好友一排（頭像、營地名、「收到 N 張」），下面是微微歪斜的小拍立得＋小郵戳；
    ///   還沒交換過的好友顯示虛線空格「還沒交換過」。點一張放大看，可以下載、封鎖、檢舉。
    /// </summary>
    public partial class TravelScreen
    {
        enum PickMode { Send }

        const float SheetH = 640f;
        static readonly Color FaceVeil = new Color(240f / 255f, 234f / 255f, 224f / 255f, 0.72f);
        static readonly Color NoteGray = Hex(0x8A7B68);

        PickMode _pickMode;
        FirebaseManager.CampFriend _pickFriend;
        int _pickFriendIdx = -1;
        Dictionary<string, (PolaroidStyle style, List<PolaroidSticker> stickers)> _polaroids;
        RectTransform _pickLayer, _pickPanel, _previewLayer, _viewerLayer;
        Coroutine _thumbLoader;
        readonly List<Texture2D> _pickTextures = new List<Texture2D>();
        Texture2D _previewTex;
        bool _delivering;

        // ─── 入口 ──────────────────────────────────────────────────────

        /// <summary>打開地圖並直接顯示護照（首頁「收進護照」之後）。</summary>
        public static void OpenPassport()
        {
            Open();
            _instance.OpenPassportView();
        }

        // ─── 選照片 ────────────────────────────────────────────────────

        void OpenPick(PickMode mode, int friendIdx, object unused)
        {
            var fb = FirebaseManager.Instance;
            if (_capture || fb == null) return;
            if (friendIdx < 0 || friendIdx >= _friendRecords.Count) return;
            if (Phase(Trip()) != TripPhase.None) { ShowToast($"{PetName()} 正在送明信片，一次只能去一個地方"); return; }
            _pickFriend = _friendRecords[friendIdx];
            _pickMode = mode;
            _pickFriendIdx = friendIdx;
            CloseSheet();
            BuildPick();
            _polaroids = null;
            fb.GetAllPolaroids(map =>
            {
                if (this == null || _pickLayer == null) return;
                _polaroids = map;
                BuildPickGrid(); // 拿到拍立得資料後才知道哪些裝飾過
            });
        }

        void ClosePick()
        {
            if (_thumbLoader != null) { StopCoroutine(_thumbLoader); _thumbLoader = null; }
            foreach (var t in _pickTextures) if (t != null) Destroy(t);
            _pickTextures.Clear();
            if (_pickLayer != null) Destroy(_pickLayer.gameObject);
            _pickLayer = _pickPanel = _pickGrid = null;
            ClosePreview();
        }

        RectTransform _pickGrid;
        float _pickGridH;

        void BuildPick()
        {
            if (_pickLayer != null) Destroy(_pickLayer.gameObject);
            _pickLayer = Stretch(_root, "Pick");
            _pickLayer.SetAsLastSibling();
            var dim = _pickLayer.gameObject.AddComponent<Image>();
            dim.color = new Color(Dim.r, Dim.g, Dim.b, 0.45f);
            var db = _pickLayer.gameObject.AddComponent<Button>();
            db.transition = Selectable.Transition.None;
            db.onClick.AddListener(ClosePick);

            float k = _root.rect.width / DesignW;
            float bottom = BottomInsetDesign();
            float h = Mathf.Min(SheetH, _root.rect.height / Mathf.Max(0.01f, k) - TopInsetCanvas() / Mathf.Max(0.01f, k) - 20f);
            _pickPanel = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
            _pickPanel.SetParent(_pickLayer, false);
            _pickPanel.anchorMin = _pickPanel.anchorMax = new Vector2(0.5f, 0f);
            _pickPanel.pivot = new Vector2(0.5f, 0f);
            _pickPanel.sizeDelta = new Vector2(DesignW, h + bottom);
            _pickPanel.localScale = Vector3.one * k;
            var bg = Stretch(_pickPanel, "Bg");
            UIShapes.AddShadow(bg, new Color(0f, 0f, 0f, 0.25f), 26, 30, -10);
            SkinImage(Stretch(bg, "Fill"), _pc?.sheet, Paper, 26, true).raycastTarget = true;
            var handle = Place(_pickPanel, "Handle", (DesignW - 40f) / 2f, 10, 40, 5);
            SkinImage(handle, _pc?.handle, Line, 2.5f, false);

            const string title = "選一張照片";
            var t = Label(_pickPanel, 18, 24, 260, 30, title, 21, Ink);
            t.alignment = TextAlignmentOptions.Left;
            string to = _pickFriend?.name;
            var st = Label(_pickPanel, 18, 54, 300, 16, $"寄給{(string.IsNullOrEmpty(to) ? "好友營地" : to)}・做成拍立得明信片", 12, SubText);
            st.alignment = TextAlignmentOptions.Left;
            var close = Place(_pickPanel, "Close", DesignW - 18f - 44f, 20, 44, 44);
            var cImg = UIShapes.AddRounded(close.gameObject, Beige, 22);
            Label(close, 0, 0, 44, 44, "×", 20, Ink);
            Button(close, cImg, ClosePick);

            // 提示條「有人臉的照片不能寄」
            var bar = Place(_pickPanel, "NoFace", 18, 80, DesignW - 36f, 34);
            SkinImage(bar, _pc?.nofaceBar, Hex(0xFFF1C9), 12, true);
            if (_pc != null && _pc.iconNoface != null)
            {
                var ic = Place(bar, "Icon", 10, 6, 22, 22).gameObject.AddComponent<Image>();
                ic.sprite = _pc.iconNoface;
                ic.raycastTarget = false;
            }
            var bl = Label(bar, 38, 0, DesignW - 36f - 48f, 34, "有人臉的照片不能寄", 12, Ink);
            bl.alignment = TextAlignmentOptions.Left;

            // 照片格子（一排 3 張、間距 8、圓角 12），上下捲動
            float top = 124f;
            _pickGridH = h - top - 12f;
            var view = Place(_pickPanel, "View", 18, top, DesignW - 36f, _pickGridH);
            view.gameObject.AddComponent<RectMask2D>();
            var hit = view.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            _pickGrid = Place(view, "Grid", 0, 0, DesignW - 36f, 0);
            var sr = view.gameObject.AddComponent<ScrollRect>();
            sr.content = _pickGrid; sr.viewport = view; sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped;
            var loading = Label(_pickGrid, 0, 30, DesignW - 36f, 20, "讀取中…", 12, SubText);
            loading.name = "Loading";
        }

        static List<PhotoMeta> CampPhotosNewestFirst()
        {
            var list = new List<PhotoMeta>();
            var photos = SaveManager.Instance?.data?.photos;
            if (photos != null) foreach (var p in photos) if (p != null && !string.IsNullOrEmpty(p.fileName)) list.Add(p);
            list.Sort((a, b) => string.CompareOrdinal(b.timestamp ?? "", a.timestamp ?? ""));
            return list;
        }

        bool IsDecorated(PhotoMeta m)
        {
            if (_polaroids == null || m == null) return false;
            return _polaroids.TryGetValue(PolaroidRules.PhotoKey(m.fileName), out var d) && PolaroidRules.IsDecorated(d.style, d.stickers.Count);
        }

        class PickCell
        {
            public PhotoMeta meta;
            public RawImage img;
            public RectTransform root, veil, ring;
            public bool known, face, decorated;
        }

        readonly List<PickCell> _pickCells = new List<PickCell>();

        void BuildPickGrid()
        {
            if (_pickGrid == null) return;
            for (int i = _pickGrid.childCount - 1; i >= 0; i--) Destroy(_pickGrid.GetChild(i).gameObject);
            _pickCells.Clear();
            var photos = CampPhotosNewestFirst();
            const float gap = 8f;
            float W = DesignW - 36f, cell = (W - gap * 2f) / 3f;
            if (photos.Count == 0)
            {
                var e = Label(_pickGrid, 0, 30, W, 40, "營地還沒有照片，先餵寵物一張吧", 13, SubText);
                e.enableWordWrapping = true;
            }
            for (int i = 0; i < photos.Count; i++)
            {
                var m = photos[i];
                var c = new PickCell { meta = m, decorated = IsDecorated(m) };
                float x = (i % 3) * (cell + gap), y = (i / 3) * (cell + gap);
                c.root = Place(_pickGrid, "Photo", x, y, cell, cell);
                var mask = UIShapes.AddRounded(c.root.gameObject, Hex(0xE8DFCF), 12);
                c.root.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                c.img = Stretch(c.root, "Img").gameObject.AddComponent<RawImage>();
                c.img.raycastTarget = false;
                c.img.enabled = false;
                // 有人臉：灰色＋淡色遮罩＋圖示＋「有人臉」（偵測完才顯示）
                c.veil = Stretch(c.root, "Face");
                c.veil.gameObject.AddComponent<Image>().color = FaceVeil;
                if (_pc != null && _pc.iconNoface != null)
                {
                    var ic = Place(c.veil, "Icon", (cell - 22f) / 2f, cell / 2f - 22f, 22, 22).gameObject.AddComponent<Image>();
                    ic.sprite = _pc.iconNoface;
                    ic.raycastTarget = false;
                }
                Label(c.veil, 0, cell / 2f + 2f, cell, 16, "有人臉", 11, SubText);
                c.veil.gameObject.SetActive(false);
                if (c.decorated && _pc != null && _pc.badgeDeco != null)
                {
                    var badge = Place(c.root, "Deco", cell - 6f - 20f, cell - 6f - 24f, 20, 24).gameObject.AddComponent<Image>();
                    badge.sprite = _pc.badgeDeco;
                    badge.raycastTarget = false;
                }
                // 選到：黃框 3＋右上勾勾
                c.ring = Stretch(c.root, "Selected");
                UIShapes.AddRing(c.ring.gameObject, Yellow, 12).raycastTarget = false;
                if (_pc != null && _pc.check != null)
                {
                    var ck = Place(c.ring, "Check", cell - 6f - 24f, 6, 24, 24).gameObject.AddComponent<Image>();
                    ck.sprite = _pc.check;
                    ck.raycastTarget = false;
                }
                c.ring.gameObject.SetActive(false);
                var cc = c;
                mask.raycastTarget = true;
                var b = c.root.gameObject.AddComponent<Button>();
                b.targetGraphic = mask;
                b.onClick.AddListener(() => OnPickPhoto(cc));
                _pickCells.Add(c);
            }
            int rows = (photos.Count + 2) / 3;
            _pickGrid.sizeDelta = new Vector2(W, Mathf.Max(_pickGridH, rows * (cell + gap) + 20f));
            if (_thumbLoader != null) StopCoroutine(_thumbLoader);
            _thumbLoader = StartCoroutine(LoadPickThumbs());
        }

        /// <summary>一張一張讀縮圖、查人臉（每幀一張，不卡）。</summary>
        IEnumerator LoadPickThumbs()
        {
            foreach (var c in _pickCells.ToArray())
            {
                if (_pickLayer == null) yield break;
                var tex = PolaroidPhoto.LoadSquare(c.meta, 240);
                if (tex != null) { _pickTextures.Add(tex); c.img.texture = tex; c.img.enabled = true; }
                bool waiting = true;
                FaceDetector.Check(c.meta, has =>
                {
                    waiting = false;
                    c.known = true;
                    c.face = has;
                    if (c.veil == null) return;
                    c.veil.gameObject.SetActive(has);
                    if (has && c.img != null && c.img.texture is Texture2D t2) { var g = Gray(t2); _pickTextures.Add(g); c.img.texture = g; }
                });
                float until = Time.unscaledTime + 12f;
                while (waiting && Time.unscaledTime < until) yield return null;
                yield return null;
            }
            _thumbLoader = null;
        }

        static Texture2D Gray(Texture2D src)
        {
            var px = src.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                byte l = (byte)Mathf.Clamp(Mathf.RoundToInt(px[i].r * 0.299f + px[i].g * 0.587f + px[i].b * 0.114f), 0, 255);
                px[i] = new Color32(l, l, l, px[i].a);
            }
            var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        void OnPickPhoto(PickCell c)
        {
            if (_delivering) return;
            if (!c.known) { ShowToast("還在檢查這張照片，等一下"); return; }
            if (c.face) { ShowToast("有人臉的照片不能寄"); return; }
            foreach (var o in _pickCells) if (o.ring != null) o.ring.gameObject.SetActive(o == c);
            if (!c.decorated) { OpenPolaroidForSend(c.meta, "寄出"); return; }
            // 裝飾過的拍立得：直接顯示那張（含大家的貼紙、畫框、濾鏡、標題）
            _polaroids.TryGetValue(PolaroidRules.PhotoKey(c.meta.fileName), out var d);
            byte[] jpg = null;
            try { jpg = PolaroidScreen.RenderPostcard(c.meta, d.style, d.stickers); }
            catch (Exception e) { Debug.LogWarning("[Postcard] 畫拍立得失敗：" + e.Message); }
            if (jpg == null) { ShowToast("讀不到這張照片"); return; }
            ShowPreview(c.meta, jpg, d.style != null ? d.style.caption : "");
        }

        void OpenPolaroidForSend(PhotoMeta m, string sendText)
        {
            ClosePreview();
            PolaroidScreen.OpenForSend(m, sendText, (jpg, caption) => { if (this != null) Deliver(jpg, caption); });
        }

        void ClosePreview()
        {
            if (_previewLayer != null) Destroy(_previewLayer.gameObject);
            _previewLayer = null;
            if (_previewTex != null) Destroy(_previewTex);
            _previewTex = null;
        }

        /// <summary>裝飾過的拍立得：整張顯示＋「用這張拍立得寄出」（黃）／「再編輯」（白）。</summary>
        void ShowPreview(PhotoMeta m, byte[] jpg, string caption)
        {
            ClosePreview();
            _previewTex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            _previewTex.LoadImage(jpg);
            _previewLayer = OverlayLayer("Preview", 0.6f, out var design, ClosePreview);
            float w = 280f, h = w * PolaroidRules.CardH / PolaroidRules.CardW;
            var card = Place(design, "Card", (DesignW - w) / 2f, 96, w, h);
            UIShapes.AddShadow(card, new Color(0f, 0f, 0f, 0.35f), 4, 30, 12);
            var raw = Stretch(card, "Img").gameObject.AddComponent<RawImage>();
            raw.texture = _previewTex;
            raw.raycastTarget = true;
            float y = 96f + h + 20f;
            const float W = DesignW - 60f;
            const string sendText = "用這張拍立得寄出";
            var yes = Place(design, "Send", 30, y, W, 53);
            UIShapes.AddRounded(Place(yes, "Edge", 0, 3, W, 50).gameObject, YellowEdge, 15).raycastTarget = false;
            var yesImg = SkinImage(Place(yes, "Fill", 0, 0, W, 50), _pc?.btnYes, Yellow, 15, true);
            Label(yes, 0, 0, W, 50, sendText, 15, Ink);
            Button(yes, yesImg, () => Deliver(jpg, caption));
            y += 53f + 10f;
            var no = Place(design, "Edit", 30, y, W, 50);
            var noImg = SkinImage(no, _pc?.btnNo, Color.white, 15, true);
            Label(no, 0, 0, W, 50, "再編輯", 15, Ink);
            Button(no, noImg, () => OpenPolaroidForSend(m, "寄出"));
        }

        /// <summary>明信片做好了（當下那一版 JPG）：寄出（寵物出門）。</summary>
        void Deliver(byte[] jpg, string caption)
        {
            var fb = FirebaseManager.Instance;
            if (fb == null || jpg == null || _delivering) return;
            _delivering = true;
            string pet = PetName();
            var friend = _pickFriend;
            int idx = _pickFriendIdx;
            if (friend == null) { _delivering = false; return; }
            fb.SendPostcard(friend, OneWayFor(idx), jpg, caption, err =>
            {
                _delivering = false;
                if (this == null) return;
                if (err != null) { ShowToast(err); return; }
                ClosePick();
                ShowToast($"{pet} 出發了！送到後{friend.name}就會收到");
            });
        }

        // ─── 護照（明信片收藏）──────────────────────────────────────────

        void OpenPassportView()
        {
            if (_passportLayer != null) Destroy(_passportLayer.gameObject);
            _passportLayer = Stretch(_root, "Passport");
            _passportLayer.SetAsLastSibling();
            var bgImg = _passportLayer.gameObject.AddComponent<Image>();
            if (_pc != null && _pc.passportBg != null) bgImg.sprite = _pc.passportBg;
            bgImg.color = _pc != null && _pc.passportBg != null ? Color.white : PassportInk;
            float k = _root.rect.width / DesignW;
            float topInset = TopInsetCanvas(), bottomInset = _capture ? 0f : BottomInsetDesign() * k;
            var design = new GameObject("Design", typeof(RectTransform)).GetComponent<RectTransform>();
            design.SetParent(_passportLayer, false);
            design.anchorMin = design.anchorMax = new Vector2(0.5f, 1f);
            design.pivot = new Vector2(0.5f, 1f);
            design.localScale = Vector3.one * k;
            design.anchoredPosition = new Vector2(0f, -topInset);
            float h = (_root.rect.height - topInset - bottomInset) / Mathf.Max(0.01f, k);
            design.sizeDelta = new Vector2(DesignW, h);

            var fb = FirebaseManager.Instance;
            var cards = fb != null ? fb.VisiblePostcards() : new List<FirebaseManager.Postcard>();
            // 每個好友一排（地圖格子順序），之後是已經不是好友、但還留著明信片的營地
            var rows = new List<(string id, string name, int slot)>();
            if (fb != null) foreach (var f in fb.CampFriends) rows.Add((f.roomId, string.IsNullOrEmpty(f.name) ? "營地好友" : f.name, f.slot));
            foreach (var c in cards) if (!rows.Exists(r => r.id == c.from)) rows.Add((c.from, c.fromName, c.fromSlot));

            var back = Place(design, "Back", 12, 14, 44, 44);
            var bImg = SkinImage(back, _skin?.passportBack, new Color(1f, 1f, 1f, 0.15f), 22, false);
            if (_skin == null || _skin.passportBack == null) Label(back, 0, 0, 44, 44, "‹", 22, PassportGold);
            Button(back, bImg, () => _passportLayer.gameObject.SetActive(false));
            var title = Label(design, 66, 14, 300, 26, $"{CampName()}的護照", 22, PassportGold);
            title.alignment = TextAlignmentOptions.Left;
            int friends = fb != null ? fb.CampFriends.Count : 0;
            var sub = Label(design, 66, 40, 310, 16, $"收到 {cards.Count} 張明信片・{friends} 個好友", 11, new Color(PassportGold.r, PassportGold.g, PassportGold.b, 0.85f));
            sub.alignment = TextAlignmentOptions.Left;

            float pageH = h - 76f - 20f;
            var page = Place(design, "Page", 14, 76, DesignW - 28f, pageH);
            UIShapes.AddShadow(page, new Color(0f, 0f, 0f, 0.35f), 14, 24, 10);
            SkinImage(Stretch(page, "Bg"), _pc?.passportPage, Hex(0xF2E6CB), 18, true);
            var view = Place(page, "View", 0, 0, DesignW - 28f, pageH);
            view.gameObject.AddComponent<RectMask2D>();
            var hit = view.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            var content = Place(view, "Content", 0, 0, DesignW - 28f, 0);
            var sr = view.gameObject.AddComponent<ScrollRect>();
            sr.content = content; sr.viewport = view; sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped;

            const float cw = 92f, ch = 112f, gap = 12f;
            float pageW = DesignW - 28f, x0 = (pageW - (cw * 3f + gap * 2f)) / 2f;
            float y = 16f;
            if (rows.Count == 0)
            {
                var e = Label(content, 14, 30, pageW - 28f, 40, "還沒有好友：在地圖右上抽好友，交換明信片", 12, SubText);
                e.enableWordWrapping = true;
                y = 90f;
            }
            foreach (var row in rows)
            {
                var mine = cards.FindAll(c => c.from == row.id);
                // 頭像 30＋營地名 14 Black #2E4686＋「收到 N 張」11
                var av = Place(content, "Avatar", 16, y, 30, 30);
                UIShapes.AddRounded(av.gameObject, SceneGreen, 15).raycastTarget = false;
                if (_skin != null && _skin.pet != null) PetImg(av, 2, 4, 26, 23, PostcardArt.PetTint(Mathf.Max(0, row.slot)), false);
                var nl = Label(content, 54, y, 200, 30, row.name, 14, PassportInk);
                nl.alignment = TextAlignmentOptions.Left;
                nl.overflowMode = TextOverflowModes.Ellipsis;
                var cl = Label(content, pageW - 16f - 90f, y, 90, 30, $"收到 {mine.Count} 張", 11, SubText);
                cl.alignment = TextAlignmentOptions.Right;
                y += 30f + 10f;
                if (mine.Count == 0)
                {
                    EmptySlot(content, x0, y);
                    y += ch + 18f;
                    continue;
                }
                for (int i = 0; i < mine.Count; i++)
                {
                    MiniPolaroid(content, x0 + (i % 3) * (cw + gap), y + (i / 3) * (ch + gap), mine[i]);
                }
                y += ((mine.Count + 2) / 3) * (ch + gap) + 6f;
            }
            content.sizeDelta = new Vector2(pageW, y + 16f);
            _passportLayer.gameObject.SetActive(true);
        }

        void EmptySlot(RectTransform parent, float x, float y)
        {
            var rt = Place(parent, "Empty", x, y, 92, 112);
            if (_pc != null && _pc.emptySlot != null) { var img = rt.gameObject.AddComponent<Image>(); img.sprite = _pc.emptySlot; img.raycastTarget = false; }
            else UIShapes.AddRing(rt.gameObject, Hex(0xB9AE9C), 6).raycastTarget = false;
            Label(rt, 0, 28, 92, 26, "？", 20, NoteGray);
            Label(rt, 0, 62, 92, 16, "還沒交換過", 11, NoteGray);
        }

        /// <summary>小拍立得 92 × 112（照片區 (6,7) 80 × 80、下面一句標題、右上小郵戳＋日期），微微歪斜 −4～3°。</summary>
        void MiniPolaroid(RectTransform parent, float x, float y, FirebaseManager.Postcard p)
        {
            var rt = Place(parent, "Mini", x, y, 92, 112);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x + 46f, -(y + 56f));
            rt.localRotation = Quaternion.Euler(0f, 0f, -PostcardRules.MiniTilt(p.id));
            UIShapes.AddShadow(rt, new Color(0f, 0f, 0f, 0.2f), 2, 8, 3);
            var bgRt = Stretch(rt, "Bg");
            Image bg;
            if (_pc != null && _pc.miniPolaroid != null) { bg = bgRt.gameObject.AddComponent<Image>(); bg.sprite = _pc.miniPolaroid; }
            else bg = UIShapes.AddRounded(bgRt.gameObject, Color.white, 2);
            var photo = Place(rt, "Photo", 6, 7, 80, 80).gameObject.AddComponent<RawImage>();
            photo.color = Hex(0xE8DFCF);
            photo.uvRect = PostcardArt.PhotoUv();
            photo.raycastTarget = false;
            var fb = FirebaseManager.Instance;
            fb?.LoadPostcardImage(p.id, tex => { if (photo != null && tex != null) { photo.texture = tex; photo.color = Color.white; } });
            string cap = string.IsNullOrEmpty(p.caption) ? p.fromName : p.caption;
            var cl = Label(rt, 4, 90, 84, 16, cap, 10, Ink);
            cl.overflowMode = TextOverflowModes.Ellipsis;
            var pm = Place(rt, "Postmark", 92f - 26f, -8, 34, 34);
            if (_pc != null && _pc.postmarkS != null) { var pi = pm.gameObject.AddComponent<Image>(); pi.sprite = _pc.postmarkS; pi.raycastTarget = false; }
            else UIShapes.AddRing(pm.gameObject, PostcardArt.PostmarkRed, 17).raycastTarget = false;
            Label(pm, 0, 0, 34, 34, PostcardRules.PostmarkShort(p.visibleAt), 7, PostcardArt.PostmarkRed);
            Button(rt, bg, () => OpenViewer(p));
        }

        /// <summary>放大看一張明信片：整張拍立得、下載、封鎖・檢舉。</summary>
        void OpenViewer(FirebaseManager.Postcard p)
        {
            if (_viewerLayer != null) Destroy(_viewerLayer.gameObject);
            Action close = () => { if (_viewerLayer != null) Destroy(_viewerLayer.gameObject); _viewerLayer = null; };
            _viewerLayer = OverlayLayer("Viewer", 0.75f, out var design, close);
            string head = p.kind == FirebaseManager.PostcardKindScenery ? $"{p.fromName}的風景明信片" : $"{p.fromName}寄來的明信片";
            Label(design, 20, 60, DesignW - 40f, 22, head, 16, Color.white);
            Label(design, 20, 84, DesignW - 40f, 16, PostcardRules.PostmarkDate(p.visibleAt), 11, new Color(1f, 1f, 1f, 0.75f));
            float w = 300f, h = w * PolaroidRules.CardH / PolaroidRules.CardW;
            var card = Place(design, "Card", (DesignW - w) / 2f, 110, w, h);
            UIShapes.AddShadow(card, new Color(0f, 0f, 0f, 0.4f), 4, 30, 12);
            var raw = Stretch(card, "Img").gameObject.AddComponent<RawImage>();
            raw.color = Hex(0xE8DFCF);
            raw.raycastTarget = true; // 點卡片不會關
            FirebaseManager.Instance?.LoadPostcardImage(p.id, tex => { if (raw != null && tex != null) { raw.texture = tex; raw.color = Color.white; } });
            float y = 110f + h + 18f;
            const float W = DesignW - 60f;
            var dl = Place(design, "Download", 30, y, W, 50);
            var dlImg = SkinImage(dl, _pc?.btnNo, Color.white, 15, true);
            Label(dl, 0, 0, W, 50, "下載", 15, Ink);
            Button(dl, dlImg, () => FirebaseManager.Instance?.LoadPostcardJpg(p.id, bytes =>
                PhotoExport.SaveImage(bytes, (ok, msg) => { if (this != null && !string.IsNullOrEmpty(msg)) ShowToast(msg); })));
            y += 53f + 10f;
            if (p.kind != FirebaseManager.PostcardKindScenery) // 風景明信片是自動產生的，不是對方的內容
                BlockReportLinks(design, y, DesignW, p.from, p.fromName, p.id, "postcard", Color.white, () => { close(); if (_passportLayer != null && _passportLayer.gameObject.activeSelf) OpenPassportView(); });
        }
    }
}
