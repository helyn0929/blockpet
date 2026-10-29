using System;
using System.Collections;
using System.Collections.Generic;
using BlockPet.Hand;
using BlockPet.UI.Chat;
using BlockPet.UI.Room;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Album
{
    /// <summary>
    /// 營地相簿（設計稿「聊天室與相簿」Album.dc.html、AlbumFilter）：
    /// - 照日期分組（今天／昨天／M/D（週）＋張數），兩欄照片卡：誰拍的、時間、說明、emoji 與留言數。
    /// - 上次打開之後的新照片有黃框與「新」。
    /// - 右上「篩選」：類別（照片辨識的結果）、誰拍的；「看 N 張」才套用，有篩選時按鈕變深色並顯示項目數。
    /// - 右上聊天鈕顯示未讀數；點照片打開大圖（AlbumUIManager.OpenPhotoDetail）。
    /// emoji 與留言數取自聊天室裡同一張照片的那則餵食訊息（FirebaseManager.ChatEntries）。
    /// 介面在執行時以 390 寬設計座標建立，依螢幕寬度等比縮放；照片縮圖逐張載入。
    /// 由「BlockPet/Build Chat And Album」掛到 AlbumPage，並關掉舊的相簿面板。
    /// </summary>
    public class AlbumScreenController : MonoBehaviour
    {
        const float DesignW = 390f;
        const float PadX = 12f;
        const float Gap = 10f;
        const float CardW = (DesignW - PadX * 2f - Gap) / 2f;
        const float PhotoH = 170f;
        const float CardH = 5f + PhotoH + 6f + 17f + 6f + 16f + 8f;
        const int ThumbW = 384;

        static readonly Color Ink = Hex(0x3A2E22);
        static readonly Color Paper = Hex(0xFFF8EC);
        static readonly Color Card = Hex(0xFFFCF4);
        static readonly Color Bg = Hex(0xF7F0E2);
        static readonly Color Beige = Hex(0xF2E9D8);
        static readonly Color Line = Hex(0xE2D6C0);
        static readonly Color Muted = Hex(0x6B5D4E);
        static readonly Color Muted2 = Hex(0x8A6A4E);
        static readonly Color Yellow = Hex(0xF6C945);
        static readonly Color YellowEdge = Hex(0xD9A92C);
        static readonly Color Coral = Hex(0xE86B4F);
        static readonly Color CoralEdge = Hex(0xB44A33);
        static readonly Color Glass = new Color(1f, 0.973f, 0.925f, 0.9f);
        static readonly string[] Categories = { "全部", "美食", "自然", "城市", "生活人物", "寵物" };

        [Header("Style")]
        [SerializeField] HandSkin skin;
        [SerializeField] Texture background;
        [Tooltip("emoji_heart／laugh／yum／wow／like／paw")]
        [SerializeField] List<Sprite> emojiSprites = new List<Sprite>();
        [SerializeField] int sortingOrder = 34;

        [Header("Links")]
        [SerializeField] PageManager pageManager;

        class Shot
        {
            public PhotoMeta meta;
            public DateTime time;
            public string who;     // 顯示名字；舊照片沒有就是「營地」
            public string whoId;   // 篩選用
            public bool mine;
            public bool isNew;
        }

        // 篩選（套用中／面板裡還沒套用的）
        string _who, _draftWho;
        string _cat, _draftCat;   // null＝全部
        readonly List<(string cat, CanvasGroup group, Image ring, Image fill, TMP_Text text)> _catChips = new List<(string, CanvasGroup, Image, Image, TMP_Text)>();
        readonly List<Shot> _shots = new List<Shot>();
        DateTime _seenBefore;

        // 版面
        Canvas _canvas;
        RectTransform _root, _design, _viewport, _content;
        ScrollRect _scroll;
        float _lastW = -1f, _lastH = -1f;
        Rect _lastSafe;
        float _k = 1f, _screenH = 772f, _topInset, _bottomInset;
        bool _dirty = true;
        RenderTexture _blurred;

        RectTransform _filterBtn;
        Image _filterBg;
        TMP_Text _filterText;
        readonly List<Image> _filterIcon = new List<Image>();
        RectTransform _filterBadge;
        TMP_Text _filterBadgeText;
        RectTransform _chatBadge;
        TMP_Text _chatBadgeText;
        TMP_Text _emptyText;

        // 篩選面板
        RectTransform _sheetLayer, _sheet, _whoRow;
        CanvasGroup _sheetGroup;
        TMP_Text _applyText;
        bool _sheetOpen;
        float _sheetT;

        // 縮圖
        readonly Dictionary<string, Texture> _thumbs = new Dictionary<string, Texture>();
        readonly Dictionary<string, RawImage> _pending = new Dictionary<string, RawImage>();
        Coroutine _loader;
        readonly List<(RectTransform rt, float t)> _glows = new List<(RectTransform, float)>();

        // ─── Lifecycle ─────────────────────────────────────────────────

        void Awake()
        {
            if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
            Build();
        }

        void OnEnable()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(true);
            MakeBackground();
            _seenBefore = SeenTime();
            SaveManager.OnSaveDataChanged += MarkDirty;
            FirebaseManager.OnChatEntryAdded += OnChatChanged;
            FirebaseManager.OnChatEntryChanged += OnChatChanged;
            FirebaseManager.OnChatEntryRemoved += OnChatRemoved;
            CloseSheet(instant: true);
            _dirty = true;
            _scroll.verticalNormalizedPosition = 1f;
        }

        void OnDisable()
        {
            SaveManager.OnSaveDataChanged -= MarkDirty;
            FirebaseManager.OnChatEntryAdded -= OnChatChanged;
            FirebaseManager.OnChatEntryChanged -= OnChatChanged;
            FirebaseManager.OnChatEntryRemoved -= OnChatRemoved;
            // 這次看過的照片下次就不算新
            var fb = FirebaseManager.Instance;
            if (fb != null && !string.IsNullOrEmpty(fb.RoomId)) PlayerPrefs.SetString(SeenKey(fb.RoomId), DateTime.Now.ToString("o"));
            if (_loader != null) { StopCoroutine(_loader); _loader = null; }
            _pending.Clear();
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            foreach (var t in _thumbs.Values) if (t != null) Destroy(t);
            _thumbs.Clear();
            if (_blurred != null) { _blurred.Release(); Destroy(_blurred); }
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        void MarkDirty() => _dirty = true;
        void OnChatChanged(ChatEntry e) => _dirty = true;
        void OnChatRemoved(string key) => _dirty = true;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            Relayout();
            if (_dirty)
            {
                _dirty = false;
                LoadShots();
                RebuildGrid();
                RefreshTopBar();
            }

            // 新照片的黃框發光（aGlow 1.8s）
            float k = 0.5f - 0.5f * Mathf.Cos(Time.unscaledTime / 1.8f * Mathf.PI * 2f);
            foreach (var g in _glows) if (g.rt != null) g.rt.GetComponent<Image>().color = new Color(Yellow.r, Yellow.g, Yellow.b, Mathf.Lerp(0.5f, 0.95f, k));

            AnimateSheet(dt);
        }

        // ─── 資料 ──────────────────────────────────────────────────────

        static string SeenKey(string roomId) => "BlockPet.AlbumSeen." + roomId;

        DateTime SeenTime()
        {
            var fb = FirebaseManager.Instance;
            string raw = fb != null && !string.IsNullOrEmpty(fb.RoomId) ? PlayerPrefs.GetString(SeenKey(fb.RoomId), "") : "";
            return DateTime.TryParse(raw, out var t) ? t : DateTime.Now;
        }

        void LoadShots()
        {
            _shots.Clear();
            var photos = SaveManager.Instance?.data?.photos;
            if (photos == null) return;
            var fb = FirebaseManager.Instance;
            string me = fb != null ? fb.GetUserId() : null;
            foreach (var p in photos)
            {
                if (p == null || string.IsNullOrEmpty(p.fileName)) continue;
                DateTime.TryParse(p.timestamp, out var t);
                bool mine = !string.IsNullOrEmpty(me) && p.takerUserId == me;
                string who = mine ? "我" : (string.IsNullOrEmpty(p.takerName) ? "營地" : p.takerName);
                _shots.Add(new Shot
                {
                    meta = p, time = t, who = who, mine = mine,
                    whoId = !string.IsNullOrEmpty(p.takerUserId) ? p.takerUserId : "",
                    isNew = t > _seenBefore,
                });
            }
            _shots.Sort((a, b) => b.time.CompareTo(a.time)); // 新到舊
        }

        List<Shot> Filtered(string who, string cat)
        {
            if (who == null && cat == null) return _shots;
            return _shots.FindAll(s => (who == null || s.whoId == who) && (cat == null || s.meta.category == cat));
        }

        /// <summary>聊天室裡這張照片的那則餵食訊息（emoji 與回覆）。</summary>
        static ChatEntry PostOf(string fileName)
        {
            var fb = FirebaseManager.Instance;
            if (fb == null || string.IsNullOrEmpty(fileName)) return null;
            foreach (var e in fb.ChatEntries) if (e.IsPost && e.msg.photoFileName == fileName) return e;
            return null;
        }

        static int ReplyCount(ChatEntry post)
        {
            if (post == null || string.IsNullOrEmpty(post.msg.messageId)) return 0;
            int n = 0;
            foreach (var e in FirebaseManager.Instance.ChatEntries) if (e.msg.replyToMessageId == post.msg.messageId) n++;
            return n;
        }

        Sprite Emoji(string key)
        {
            foreach (var s in emojiSprites) if (s != null && s.name == "emoji_" + key) return s;
            return null;
        }

        // ─── 照片格 ────────────────────────────────────────────────────

        void RebuildGrid()
        {
            foreach (Transform c in _content) Destroy(c.gameObject);
            _glows.Clear();
            _pending.Clear();

            var list = Filtered(_who, _cat);
            float y = 56f + _topInset;
            DateTime? day = null;
            int i = 0;
            while (i < list.Count)
            {
                day = list[i].time.Date;
                int start = i;
                while (i < list.Count && list[i].time.Date == day.Value) i++;
                int n = i - start;

                // 日期列
                var head = Place(_content, "Day", PadX + 4f, y + 12f, DesignW - PadX * 2f - 8f, 20);
                Label(head, 0, 0, 260, 20, ChatScreenController.DayLabel(day.Value), 14, Ink, TextAlignmentOptions.Left);
                Label(head, 0, 0, DesignW - PadX * 2f - 8f, 20, n + " 張", 11, Muted2, TextAlignmentOptions.Right);
                y += 12f + 20f + 8f;

                for (int j = 0; j < n; j++)
                {
                    int col = j % 2, rowN = j / 2;
                    BuildCard(list[start + j], PadX + col * (CardW + Gap), y + rowN * (CardH + Gap));
                }
                y += Mathf.CeilToInt(n / 2f) * (CardH + Gap) - Gap;
            }
            y += 24f + _bottomInset;
            _emptyText.gameObject.SetActive(list.Count == 0);
            _emptyText.text = _shots.Count == 0 ? "還沒有照片\n去拍一張餵給寵物吧" : "這個篩選還沒有照片\n去拍一張餵給寵物吧";
            _content.sizeDelta = new Vector2(DesignW, Mathf.Max(y, 200f));

            if (_loader != null) StopCoroutine(_loader);
            _loader = StartCoroutine(LoadThumbs());
        }

        void BuildCard(Shot s, float x, float y)
        {
            var card = Place(_content, "Photo", x, y, CardW, CardH);
            if (s.isNew)
            {
                var glow = UIShapes.AddRounded(Place(card, "Glow", 0, 0, 0, 0).gameObject, Yellow, 19);
                StretchIn(glow.rectTransform, -3, -3, -3, -3);
                glow.raycastTarget = false;
                _glows.Add((glow.rectTransform, 0f));
            }
            else
            {
                UIShapes.AddShadow(card, new Color(30f / 255f, 30f / 255f, 20f / 255f, 0.1f), 16, 10, 3);
            }
            var bg = UIShapes.AddRounded(Place(card, "Fill", 0, 0, 0, 0).gameObject, Card, 16);
            StretchIn(bg.rectTransform);

            // 照片：圓角 12、下方漸層、誰拍的與時間
            float pw = CardW - 10f;
            var frame = Place(card, "Frame", 5, 5, pw, PhotoH);
            UIShapes.AddRounded(frame.gameObject, Beige, 12).raycastTarget = false;
            frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var img = Place(frame, "Img", 0, 0, pw, PhotoH).gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            if (_thumbs.TryGetValue(s.meta.fileName, out var tex) && tex != null) SetThumb(img, tex);
            else { img.enabled = false; _pending[s.meta.fileName] = img; }
            var shade = Place(frame, "Shade", 0, PhotoH - 46f, pw, 46).gameObject.AddComponent<RawImage>();
            shade.texture = ShadeTex();
            shade.raycastTarget = false;
            var av = Place(frame, "Avatar", 6, PhotoH - 28f, 22, 22);
            UIShapes.AddRounded(av.gameObject, Color.white, 11).raycastTarget = false;
            UIShapes.AddRounded(Place(av, "Fill", 1.5f, 1.5f, 19, 19).gameObject, ChatScreenController.PersonColor(s.who, s.mine), 9.5f).raycastTarget = false;
            Label(av, 0, 0, 22, 22, ChatScreenController.Initial(s.who, s.mine), 10, Color.white, TextAlignmentOptions.Center);
            var who = Label(frame, 33, PhotoH - 28f, pw - 80f, 22, s.who, 11, Color.white, TextAlignmentOptions.Left);
            who.enableWordWrapping = false;
            who.overflowMode = TextOverflowModes.Ellipsis;
            Label(frame, pw - 50f, PhotoH - 26f, 43, 18, s.time == default ? "" : s.time.ToString("HH:mm"), 10, Color.white, TextAlignmentOptions.Right);

            // 說明
            string caption = string.IsNullOrEmpty(s.meta.caption) ? (s.mine ? "我餵的照片" : s.who + " 餵的照片") : s.meta.caption;
            var cap = Label(card, 8, 5 + PhotoH + 6f, CardW - 16f, 17, caption, 12, Ink, TextAlignmentOptions.Left);
            cap.enableWordWrapping = false;
            cap.overflowMode = TextOverflowModes.Ellipsis;

            // emoji 摘要與留言數（聊天室裡同一張照片的那則）
            var post = PostOf(s.meta.fileName);
            float ry = 5 + PhotoH + 6f + 17f + 6f;
            float rx = 8f;
            int shown = 0;
            if (post != null)
            {
                foreach (var k in ChatScreenController.EmojiKeys)
                {
                    if (!post.reactions.TryGetValue(k, out var uids) || uids.Count == 0 || shown >= 3) continue;
                    var em = Place(card, k, rx, ry, 14, 14).gameObject.AddComponent<Image>();
                    em.sprite = Emoji(k);
                    em.raycastTarget = false;
                    var n = Label(card, rx + 15f, ry - 1f, 20, 16, uids.Count.ToString(), 11, Muted, TextAlignmentOptions.Left);
                    n.enableWordWrapping = false;
                    rx += 15f + n.GetPreferredValues(n.text).x + 7f;
                    shown++;
                }
            }
            if (shown == 0) Label(card, 8, ry - 1f, 120, 16, "還沒有人按", 11, Muted, TextAlignmentOptions.Left);
            int cm = ReplyCount(post);
            var bubble = Place(card, "Comments", CardW - 8f - 40f, ry, 40, 16);
            var cmText = Label(bubble, 0, -1, 40, 16, cm.ToString(), 11, Muted, TextAlignmentOptions.Right);
            float cw = cmText.GetPreferredValues(cmText.text).x;
            ChatIcon(bubble, 40f - cw - 4f - 12f, 2f, 12f, Muted2);

            if (s.isNew)
            {
                var badge = Place(card, "New", CardW - 26f, -7, 32, 18);
                UIShapes.AddRounded(Place(badge, "Edge", 0, 2, 32, 18).gameObject, CoralEdge, 8).raycastTarget = false;
                UIShapes.AddRounded(Place(badge, "Fill", 0, 0, 32, 18).gameObject, Coral, 8).raycastTarget = false;
                Label(badge, 0, 0, 32, 18, "新", 11, Color.white, TextAlignmentOptions.Center);
            }

            var b = card.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            b.transition = Selectable.Transition.None;
            var meta = s.meta;
            b.onClick.AddListener(() => AlbumUIManager.Resolve()?.OpenPhotoDetail(meta));
        }

        static void SetThumb(RawImage img, Texture tex)
        {
            img.texture = tex;
            var r = img.rectTransform.rect;
            img.uvRect = ChatScreenController.AspectFill(tex.width, tex.height, r.width, r.height);
            img.enabled = true;
        }

        /// <summary>逐張讀照片並縮成縮圖（每幀一張，避免卡頓）。</summary>
        IEnumerator LoadThumbs()
        {
            var keys = new List<string>(_pending.Keys);
            foreach (var file in keys)
            {
                if (!_pending.TryGetValue(file, out var img)) continue;
                if (!_thumbs.TryGetValue(file, out var tex) || tex == null)
                {
                    var meta = SaveManager.Instance?.data?.photos?.Find(p => p != null && p.fileName == file);
                    var full = meta != null ? SaveManager.Instance.LoadPhoto(meta) : null;
                    if (full != null)
                    {
                        tex = ChatScreenController.Downscale(full, ThumbW);
                        Destroy(full);
                        _thumbs[file] = tex;
                    }
                    yield return null;
                }
                if (tex != null && img != null) SetThumb(img, tex);
                _pending.Remove(file);
            }
            _loader = null;
        }

        static Texture2D _shadeTex;
        static Texture2D ShadeTex()
        {
            if (_shadeTex != null) return _shadeTex;
            const int n = 16;
            _shadeTex = new Texture2D(1, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            for (int i = 0; i < n; i++) _shadeTex.SetPixel(0, i, new Color(0, 0, 0, Mathf.Lerp(0.5f, 0f, i / (n - 1f))));
            _shadeTex.Apply(false, true);
            return _shadeTex;
        }

        // ─── 頂部 ──────────────────────────────────────────────────────

        void RefreshTopBar()
        {
            int count = (_who != null ? 1 : 0) + (_cat != null ? 1 : 0);
            bool filtered = count > 0;
            _filterBg.color = filtered ? Ink : Glass;
            _filterText.color = filtered ? Color.white : Ink;
            foreach (var bar in _filterIcon) bar.color = filtered ? Color.white : Ink;
            _filterBadge.gameObject.SetActive(filtered);
            _filterBadgeText.text = count.ToString();

            int unread = ChatScreenController.UnreadCount();
            _chatBadge.gameObject.SetActive(unread > 0);
            _chatBadgeText.text = unread > 99 ? "99+" : unread.ToString();
        }

        void Back()
        {
            if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
            pageManager?.CloseAlbumPage();
        }

        void OpenChat()
        {
            if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
            pageManager?.ShowChatPage();
        }

        // ─── 篩選面板 ──────────────────────────────────────────────────

        void OpenSheet()
        {
            _draftWho = _who;
            _draftCat = _cat;
            BuildWhoChips();
            RefreshCatChips();
            UpdateApply();
            _sheetOpen = true;
            _sheetLayer.gameObject.SetActive(true);
        }

        void CloseSheet(bool instant = false)
        {
            _sheetOpen = false;
            if (instant && _sheetLayer != null) { _sheetT = 0f; _sheetLayer.gameObject.SetActive(false); }
        }

        void AnimateSheet(float dt)
        {
            if (!_sheetLayer.gameObject.activeSelf) return;
            _sheetT = Mathf.MoveTowards(_sheetT, _sheetOpen ? 1f : 0f, dt / 0.3f);
            float e = 1f - Mathf.Pow(1f - _sheetT, 3f);
            _sheetGroup.alpha = e;
            _sheetGroup.blocksRaycasts = _sheetOpen;
            _sheet.anchoredPosition = new Vector2(0f, -(1f - e) * _sheet.rect.height * _sheet.localScale.y);
            if (!_sheetOpen && _sheetT <= 0f) _sheetLayer.gameObject.SetActive(false);
        }

        void UpdateApply() => _applyText.text = $"看 {Filtered(_draftWho, _draftCat).Count} 張";

        void RefreshCatChips()
        {
            foreach (var (cat, group, ring, fill, text) in _catChips)
            {
                bool on = cat == _draftCat;
                group.alpha = on ? 1f : 0.75f;
                ring.color = on ? Ink : Line;
                fill.color = on ? Ink : Card;
                text.color = on ? Color.white : Ink;
            }
        }

        void BuildWhoChips()
        {
            foreach (Transform c in _whoRow) Destroy(c.gameObject);
            // 全部＋相簿裡出現過的人（有記錄 uid 的照片）
            var people = new List<(string id, string name, bool mine)> { (null, "全部", false) };
            foreach (var s in _shots)
                if (!string.IsNullOrEmpty(s.whoId) && !people.Exists(p => p.id == s.whoId)) people.Add((s.whoId, s.who, s.mine));
            float x = 0f;
            foreach (var (id, name, mine) in people)
            {
                bool on = _draftWho == id;
                var chip = Place(_whoRow, name, x, 0, 50, 68);
                var group = chip.gameObject.AddComponent<CanvasGroup>();
                group.alpha = on || (_draftWho == null && id == null) ? 1f : 0.55f;
                var ring = UIShapes.AddRounded(Place(chip, "Ring", 2, 0, 46, 46).gameObject, on ? Ink : Card, 23);
                Color c = id == null ? Hex(0xB9AC96) : ChatScreenController.PersonColor(name, mine);
                UIShapes.AddRounded(Place(chip, "Fill", 5, 3, 40, 40).gameObject, c, 20).raycastTarget = false;
                Label(chip, 2, 0, 46, 46, id == null ? "全" : ChatScreenController.Initial(name, mine), 14, Color.white, TextAlignmentOptions.Center);
                var n = Label(chip, -5, 50, 60, 16, name, 11, Ink, TextAlignmentOptions.Center);
                n.enableWordWrapping = false;
                n.overflowMode = TextOverflowModes.Ellipsis;
                var b = chip.gameObject.AddComponent<Button>();
                b.targetGraphic = ring;
                b.transition = Selectable.Transition.None;
                string pick = id;
                b.onClick.AddListener(() => { _draftWho = pick; BuildWhoChips(); UpdateApply(); });
                x += 50f + 14f;
            }
        }

        // ─── 版面 ──────────────────────────────────────────────────────

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
            _topInset = (Screen.height - safe.yMax) * px / _k;
            _bottomInset = safe.yMin * px / _k;
            _screenH = h / _k;

            _design.localScale = Vector3.one * _k;
            _design.sizeDelta = new Vector2(DesignW, _screenH);
            _viewport.sizeDelta = new Vector2(DesignW, _screenH);
            ((RectTransform)_design.Find("TopBar")).anchoredPosition = new Vector2(0f, -_topInset);
            _sheet.localScale = Vector3.one * _k;
            _sheet.sizeDelta = new Vector2(DesignW, SheetH + _bottomInset);
            _dirty = true;
        }

        // ─── 建立介面 ──────────────────────────────────────────────────

        const float SheetH = 12f + 5f + 14f + 44f + 14f + 22f + 92f + 14f + 22f + 68f + 18f + 50f + 26f;

        void Build()
        {
            // 獨立的最上層 Canvas（不受頁面上層 CanvasGroup 影響），顯示／隱藏由 OnEnable／OnDisable 同步
            var go = new GameObject("AlbumScreen (runtime)", typeof(RectTransform));
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = sortingOrder;
            go.AddComponent<GraphicRaycaster>();
            _root = (RectTransform)go.transform;

            Stretch(_root, "Background").gameObject.AddComponent<Image>().color = Bg;
            var photo = Stretch(_root, "BackgroundPhoto").gameObject.AddComponent<RawImage>();
            photo.color = new Color(1f, 1f, 1f, 0.16f);
            photo.raycastTarget = false;
            photo.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;

            _design = new GameObject("Design", typeof(RectTransform)).GetComponent<RectTransform>();
            _design.SetParent(_root, false);
            _design.anchorMin = _design.anchorMax = new Vector2(0.5f, 1f);
            _design.pivot = new Vector2(0.5f, 1f);
            _design.sizeDelta = new Vector2(DesignW, 772f);

            _viewport = Place(_design, "Viewport", 0, 0, DesignW, 772);
            _viewport.gameObject.AddComponent<RectMask2D>();
            var vp = _viewport.gameObject.AddComponent<Image>();
            vp.color = new Color(1, 1, 1, 0);
            _content = Place(_viewport, "Content", 0, 0, DesignW, 1000);
            _scroll = _viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = _viewport;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Elastic;
            _scroll.scrollSensitivity = 20f;
            _emptyText = Label(_design, 0, 180, DesignW, 50, "", 13, Muted2, TextAlignmentOptions.Center);
            _emptyText.lineSpacing = 10f;

            BuildTopBar();
            BuildSheet();
            go.SetActive(false); // 元件啟用時（OnEnable）才顯示
        }

        void BuildTopBar()
        {
            var bar = Place(_design, "TopBar", 0, 0, DesignW, 64);

            var back = Place(bar, "Back", 12, 10, 44, 44);
            UIShapes.AddShadow(back, new Color(30f / 255f, 30f / 255f, 20f / 255f, 0.18f), 22, 10, 3);
            var bbg = UIShapes.AddRounded(Place(back, "Fill", 0, 0, 44, 44).gameObject, Glass, 22);
            var chev = AddGraphic<UIPolygon>(Place(back, "Chevron", 0, 0, 44, 44));
            chev.color = Ink;
            chev.raycastTarget = false;
            chev.SetPoints(new Vector2(25f, 14.5f), new Vector2(25f, 29.5f), new Vector2(17.5f, 22f));
            var bb = back.gameObject.AddComponent<Button>();
            bb.targetGraphic = bbg;
            bb.onClick.AddListener(Back);

            // 聊天（未讀數）
            var chat = Place(bar, "Chat", DesignW - 12f - 44f, 10, 44, 44);
            UIShapes.AddShadow(chat, new Color(30f / 255f, 30f / 255f, 20f / 255f, 0.18f), 14, 10, 3);
            var cbg = UIShapes.AddRounded(Place(chat, "Fill", 0, 0, 44, 44).gameObject, Glass, 14);
            ChatIcon(chat, 11f, 12f, 22f, Ink);
            _chatBadge = Badge(chat, out _chatBadgeText);
            var cb = chat.gameObject.AddComponent<Button>();
            cb.targetGraphic = cbg;
            cb.onClick.AddListener(OpenChat);

            // 篩選
            _filterBtn = Place(bar, "Filter", DesignW - 12f - 44f - 10f - 84f, 10, 84, 44);
            UIShapes.AddShadow(_filterBtn, new Color(30f / 255f, 30f / 255f, 20f / 255f, 0.18f), 14, 10, 3);
            _filterBg = UIShapes.AddRounded(Place(_filterBtn, "Fill", 0, 0, 84, 44).gameObject, Glass, 14);
            float[] lens = { 16f, 10f, 4f };
            for (int i = 0; i < 3; i++)
            {
                var l = Place(_filterBtn, "Bar", 12f + (16f - lens[i]) / 2f + 1f, 15f + i * 5f, lens[i], 2.4f).gameObject.AddComponent<Image>();
                l.raycastTarget = false;
                _filterIcon.Add(l);
            }
            _filterText = Label(_filterBtn, 36, 0, 40, 44, "篩選", 13, Ink, TextAlignmentOptions.Left);
            _filterBadge = Badge(_filterBtn, out _filterBadgeText);
            var fb = _filterBtn.gameObject.AddComponent<Button>();
            fb.targetGraphic = _filterBg;
            fb.onClick.AddListener(OpenSheet);
        }

        RectTransform Badge(RectTransform parent, out TMP_Text text)
        {
            var w = parent.sizeDelta.x;
            var b = Place(parent, "Badge", w - 16f, -4, 20, 20);
            UIShapes.AddRounded(b.gameObject, Paper, 10).raycastTarget = false;
            UIShapes.AddRounded(Place(b, "Fill", 2, 2, 16, 16).gameObject, Coral, 8).raycastTarget = false;
            text = Label(b, 0, 0, 20, 20, "", 10, Color.white, TextAlignmentOptions.Center);
            text.enableWordWrapping = false;
            b.gameObject.SetActive(false);
            return b;
        }

        /// <summary>對話泡泡圖示（圓角方塊＋左下小尾巴）。</summary>
        void ChatIcon(RectTransform parent, float x, float y, float size, Color color)
        {
            float u = size / 24f;
            UIShapes.AddRounded(Place(parent, "Bubble", x + 3f * u, y + 5f * u, 18f * u, 12f * u).gameObject, color, 1.5f * u).raycastTarget = false;
            var tail = AddGraphic<UIPolygon>(Place(parent, "Tail", x, y, size, size));
            tail.color = color;
            tail.raycastTarget = false;
            tail.SetPoints(new Vector2(6f * u, 16.5f * u), new Vector2(11f * u, 16.5f * u), new Vector2(6f * u, 20.5f * u));
        }

        void BuildSheet()
        {
            _sheetLayer = Stretch(_root, "FilterLayer");
            _sheetGroup = _sheetLayer.gameObject.AddComponent<CanvasGroup>();
            var dim = Stretch(_sheetLayer, "Dim").gameObject.AddComponent<Image>();
            dim.color = new Color(20f / 255f, 24f / 255f, 14f / 255f, 0.45f);
            dim.gameObject.AddComponent<Button>().onClick.AddListener(() => CloseSheet());

            _sheet = new GameObject("Sheet", typeof(RectTransform)).GetComponent<RectTransform>();
            _sheet.SetParent(_sheetLayer, false);
            _sheet.anchorMin = _sheet.anchorMax = new Vector2(0.5f, 0f);
            _sheet.pivot = new Vector2(0.5f, 0f);
            _sheet.sizeDelta = new Vector2(DesignW, SheetH);
            var panel = Place(_sheet, "Panel", 0, 0, 0, 0);
            StretchIn(panel, 0, 0, 0, -30);
            UIShapes.AddShadow(panel, new Color(0, 0, 0, 0.25f), 26, 30, -10);
            StretchIn(UIShapes.AddRounded(Place(panel, "Fill", 0, 0, 0, 0).gameObject, Paper, 26).rectTransform);
            UIShapes.AddRounded(Place(_sheet, "Handle", 175, 12, 40, 5).gameObject, Line, 2.5f).raycastTarget = false;

            float w = DesignW - 36f;
            Label(_sheet, 18, 39, 200, 28, "篩選", 21, Ink, TextAlignmentOptions.Left);
            var close = Place(_sheet, "Close", DesignW - 18f - 44f, 31, 44, 44);
            var xbg = UIShapes.AddRounded(close.gameObject, Beige, 22);
            foreach (float z in new[] { 45f, -45f })
            {
                var bar = Place(close, "X", 0, 0, 13, 2.4f);
                bar.pivot = new Vector2(0.5f, 0.5f);
                bar.anchoredPosition = new Vector2(22f, -22f);
                bar.localRotation = Quaternion.Euler(0, 0, z);
                var img = bar.gameObject.AddComponent<Image>();
                img.color = Ink;
                img.raycastTarget = false;
            }
            var xb = close.gameObject.AddComponent<Button>();
            xb.targetGraphic = xbg;
            xb.onClick.AddListener(() => CloseSheet());

            // 類別：照片辨識的結果（PhotoMeta.category）；辨識前的舊照片只在「全部」裡
            float y = 89f;
            Label(_sheet, 18, y, w, 16, "類別", 12, Muted, TextAlignmentOptions.Left);
            y += 22f;
            _catChips.Clear();
            float cx = 18f, cy = y;
            foreach (var cat in Categories)
            {
                bool all = cat == "全部";
                var t = Label(_sheet, 0, 0, 100, 38, cat, 13, all ? Color.white : Ink, TextAlignmentOptions.Center);
                float cw = t.GetPreferredValues(cat).x + (all ? 28f : 41f);
                if (cx + cw > 18f + w) { cx = 18f; cy += 46f; }
                var chip = Place(_sheet, cat, cx, cy, cw, 38);
                var group = chip.gameObject.AddComponent<CanvasGroup>();
                var ring = UIShapes.AddRounded(chip.gameObject, Line, 19);
                var fill = UIShapes.AddRounded(Place(chip, "Fill", 0, 0, 0, 0).gameObject, Card, 17.5f);
                fill.raycastTarget = false;
                StretchIn(fill.rectTransform, 1.5f, 1.5f, 1.5f, 1.5f);
                if (!all)
                    UIShapes.AddRounded(Place(chip, "Dot", 12, 14.5f, 9, 9).gameObject, CategoryColor(cat), 4.5f).raycastTarget = false;
                t.rectTransform.SetParent(chip, false);
                t.rectTransform.anchoredPosition = new Vector2(all ? 0f : 13f, 0f);
                t.rectTransform.sizeDelta = new Vector2(cw - (all ? 0f : 13f), 38f);
                string pick = all ? null : cat;
                _catChips.Add((pick, group, ring, fill, t));
                var b = chip.gameObject.AddComponent<Button>();
                b.targetGraphic = ring;
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => { _draftCat = pick; RefreshCatChips(); UpdateApply(); });
                cx += cw + 8f;
            }
            y = cy + 38f + 14f;

            Label(_sheet, 18, y, w, 16, "誰拍的", 12, Muted, TextAlignmentOptions.Left);
            y += 22f;
            _whoRow = Place(_sheet, "Who", 18, y, w, 68);
            y += 68f + 18f;

            float cancelW = (w - 8f) / 2.5f;
            var clear = Place(_sheet, "Clear", 18, y, cancelW, 50);
            var clbg = UIShapes.AddRounded(clear.gameObject, Beige, 15);
            Label(clear, 0, 0, cancelW, 50, "清除", 15, Ink, TextAlignmentOptions.Center);
            var clb = clear.gameObject.AddComponent<Button>();
            clb.targetGraphic = clbg;
            clb.onClick.AddListener(() => { _draftWho = null; _draftCat = null; BuildWhoChips(); RefreshCatChips(); UpdateApply(); });

            float aw = w - cancelW - 8f;
            var apply = Place(_sheet, "Apply", 18 + cancelW + 8f, y, aw, 50);
            UIShapes.AddRounded(Place(apply, "Edge", 0, 3, aw, 50).gameObject, YellowEdge, 15).raycastTarget = false;
            var abg = UIShapes.AddRounded(Place(apply, "Fill", 0, 0, aw, 50).gameObject, Yellow, 15);
            _applyText = Label(apply, 0, 0, aw, 50, "", 15, Ink, TextAlignmentOptions.Center);
            var ab = apply.gameObject.AddComponent<Button>();
            ab.targetGraphic = abg;
            ab.onClick.AddListener(() =>
            {
                _who = _draftWho;
                _cat = _draftCat;
                CloseSheet();
                _dirty = true;
                _scroll.verticalNormalizedPosition = 1f;
            });

            _sheetLayer.gameObject.SetActive(false);
        }

        /// <summary>五大類的代表色（篩選圓點、聊天室照片的類別標籤）。</summary>
        public static Color CategoryColor(string category)
        {
            switch (category)
            {
                case "美食": return Hex(0xF29A3A);
                case "自然": return Hex(0x6FA45F);
                case "城市": return Hex(0x6F8FE0);
                case "生活人物": return Hex(0xD78AD0);
                default: return Hex(0xE8C33A);
            }
        }

        void MakeBackground()
        {
            var photo = _root.Find("BackgroundPhoto").GetComponent<RawImage>();
            if (background == null) { photo.enabled = false; return; }
            if (_blurred == null || !_blurred.IsCreated())
            {
                int w = 40, h = Mathf.Max(1, Mathf.RoundToInt(40f * background.height / Mathf.Max(1, background.width)));
                var mid = RenderTexture.GetTemporary(160, Mathf.Max(1, h * 4), 0);
                mid.filterMode = FilterMode.Bilinear;
                Graphics.Blit(background, mid);
                if (_blurred == null)
                    _blurred = new RenderTexture(w, h, 0) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                Graphics.Blit(mid, _blurred);
                RenderTexture.ReleaseTemporary(mid);
            }
            photo.texture = _blurred;
            photo.GetComponent<AspectRatioFitter>().aspectRatio = (float)background.width / Mathf.Max(1, background.height);
        }

        // ─── UI helpers（與其他新版頁面相同的設計座標寫法）────────────

        TMP_Text Label(RectTransform parent, float left, float top, float w, float h, string value, float size,
                       Color color, TextAlignmentOptions align)
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
            t.richText = false;
            t.enableWordWrapping = true;
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

        static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
