using System;
using System.Collections.Generic;
using BlockPet.Hand;
using BlockPet.UI.Room;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Chat
{
    /// <summary>
    /// 營地聊天室（設計稿「聊天室與相簿」Chat.dc.html、ChatMenu、ChatReply）：
    /// - 頂部：返回、寵物頭像、空間名稱、「N 位成員・一起養 寵物」、相簿。
    /// - 訊息由舊到新、最新在最下面；自己的黃色靠右，別人的米白靠左（頭像＋名字）；跨日加日期。
    /// - 餵食照片也會出現（「餵了 寵物 一張照片」，FirebaseManager 存照片時自動發）。
    /// - 訊息下方是 emoji（點一下按／取消）；點訊息：上方 emoji 列、下方「回覆／複製／收回（只有自己的）」。
    /// - 回覆：輸入框上方顯示在回誰，送出後訊息裡帶引用，點引用跳回原訊息並閃一下。
    /// 資料來自 <see cref="FirebaseManager.ChatEntries"/>（含 key 與 emoji），emoji 圖由 docs/chat/Tools/make_emoji_sprites.py 產生。
    /// 介面在執行時以 390 寬設計座標建立，依螢幕寬度等比縮放；鍵盤出現時輸入列跟著往上。
    /// 由「BlockPet/Build Chat And Album」掛到 ChatPage，並關掉舊的 UI Toolkit 聊天室。
    /// </summary>
    public class ChatScreenController : MonoBehaviour
    {
        const float DesignW = 390f;
        const float HeaderH = 64f;
        const float InputH = 70f;      // 8 + 42 + 20
        const float ReplyBarH = 54f;   // 46 + 8
        const float SidePad = 12f;
        const float BubbleMaxW = 252f;
        const float PostImgW = 216f, PostImgH = 160f;

        public static readonly string[] EmojiKeys = { "heart", "laugh", "yum", "wow", "like", "paw" };

        static readonly Color Ink = Hex(0x3A2E22);
        static readonly Color Paper = Hex(0xFFF8EC);
        static readonly Color Card = Hex(0xFFFCF4);
        static readonly Color Bg = Hex(0xF7F0E2);
        static readonly Color Beige = Hex(0xF2E9D8);
        static readonly Color Line = Hex(0xE2D6C0);
        static readonly Color Divider = Hex(0xEFE6D4);
        static readonly Color Muted = Hex(0x6B5D4E);
        static readonly Color Muted2 = Hex(0x8A6A4E);
        static readonly Color Yellow = Hex(0xF6C945);
        static readonly Color YellowEdge = Hex(0xD9A92C);
        static readonly Color YellowSoft = Hex(0xFFF1C9);
        static readonly Color Red = Hex(0xC2452B);
        static readonly Color[] People = { Hex(0xE88AA8), Hex(0x6F8FE0), Hex(0x6FA45F), Hex(0xD78AD0), Hex(0xF29A3A), Hex(0x8FA868) };

        [Header("Style")]
        [SerializeField] HandSkin skin;
        [SerializeField] Texture background;
        [SerializeField] Sprite[] petSprites;
        [Tooltip("emoji_heart／laugh／yum／wow／like／paw")]
        [SerializeField] List<Sprite> emojiSprites = new List<Sprite>();
        [SerializeField] int sortingOrder = 33;

        [Header("Links")]
        [SerializeField] PageManager pageManager;

        // 版面
        Canvas _canvas;
        RectTransform _root, _design, _viewport, _content, _bottom, _replyBar;
        ScrollRect _scroll;
        float _lastW = -1f, _lastH = -1f, _lastKb = -1f;
        Rect _lastSafe;
        float _k = 1f, _screenH = 772f, _topInset, _bottomInset;
        RenderTexture _blurred;

        // 頂部
        TMP_Text _roomName, _roomSub;
        Image _petAvatar;

        // 輸入
        TMP_InputField _input;
        Image _sendBg, _sendEdge;
        UIPolygon _sendArrow;
        TMP_Text _replyWho, _replyText;
        Image _replyAccent;
        RawImage _replyImg;
        string _replyKey;

        // 訊息
        bool _dirty = true;
        bool _stickToBottom = true;
        string _highlightKey;
        float _highlightTime;
        RectTransform _highlightRow;
        readonly Dictionary<string, RectTransform> _rows = new Dictionary<string, RectTransform>();
        readonly Dictionary<string, Texture> _postTex = new Dictionary<string, Texture>();

        // 點訊息後的選單
        RectTransform _menuLayer;
        CanvasGroup _menuGroup;
        RectTransform _menuBox;
        string _menuKey;
        float _menuT;

        RectTransform _toast;
        CanvasGroup _toastGroup;
        TMP_Text _toastText;
        float _toastUntil;

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
            FirebaseManager.OnChatEntryAdded += OnEntryAdded;
            FirebaseManager.OnChatEntryChanged += OnEntryChanged;
            FirebaseManager.OnChatEntryRemoved += OnEntryRemoved;
            FirebaseManager.OnChatEntriesReset += OnEntriesReset;
            SaveManager.OnSaveDataChanged += OnSaveChanged;
            FirebaseManager.Instance?.EnsureChatListening();
            CloseMenu(instant: true);
            SetReply(null);
            _stickToBottom = true;
            _dirty = true;
            RefreshHeader();
            MarkRead();
        }

        void OnDisable()
        {
            FirebaseManager.OnChatEntryAdded -= OnEntryAdded;
            FirebaseManager.OnChatEntryChanged -= OnEntryChanged;
            FirebaseManager.OnChatEntryRemoved -= OnEntryRemoved;
            FirebaseManager.OnChatEntriesReset -= OnEntriesReset;
            SaveManager.OnSaveDataChanged -= OnSaveChanged;
            MarkRead();
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            foreach (var t in _postTex.Values) if (t != null) Destroy(t);
            _postTex.Clear();
            if (_blurred != null) { _blurred.Release(); Destroy(_blurred); }
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        void OnEntryAdded(ChatEntry e) { _dirty = true; MarkRead(); }
        void OnEntryChanged(ChatEntry e) => _dirty = true;
        void OnEntryRemoved(string key) { _dirty = true; if (key == _replyKey) SetReply(null); if (key == _menuKey) CloseMenu(); }
        void OnEntriesReset() { _dirty = true; SetReply(null); CloseMenu(); }
        void OnSaveChanged() => _dirty = true; // 別人的餵食照片同步下來了

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            Relayout();
            if (_dirty)
            {
                _dirty = false;
                bool atBottom = _stickToBottom || _scroll.verticalNormalizedPosition <= 0.02f;
                RebuildMessages();
                if (atBottom) ScrollToBottom();
                _stickToBottom = false;
            }

            // 送出鈕：有字才亮
            bool canSend = !string.IsNullOrWhiteSpace(_input.text);
            _sendBg.color = canSend ? Yellow : Line;
            _sendEdge.enabled = canSend;
            _sendArrow.color = canSend ? Ink : Color.white;

            // 跳到被引用的訊息時閃一下（cHl 1.2s）
            if (_highlightRow != null)
            {
                _highlightTime += dt;
                float t = _highlightTime / 1.2f;
                float a = t < 0.3f ? t / 0.3f : Mathf.Clamp01(1f - (t - 0.3f) / 0.7f);
                _highlightRow.GetComponent<Image>().color = new Color(Yellow.r, Yellow.g, Yellow.b, 0.35f * a);
                if (t >= 1f) { _highlightRow.GetComponent<Image>().color = new Color(1, 1, 1, 0); _highlightRow = null; }
            }

            // 選單淡入
            if (_menuLayer.gameObject.activeSelf)
            {
                _menuT = Mathf.MoveTowards(_menuT, _menuKey != null ? 1f : 0f, dt / 0.2f);
                _menuGroup.alpha = _menuT;
                _menuBox.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, _menuT);
                if (_menuKey == null && _menuT <= 0f) _menuLayer.gameObject.SetActive(false);
            }

            _toastGroup.alpha = Mathf.MoveTowards(_toastGroup.alpha, Time.unscaledTime < _toastUntil ? 1f : 0f, dt / 0.2f);
        }

        // ─── 資料 ──────────────────────────────────────────────────────

        static IReadOnlyList<ChatEntry> Entries =>
            FirebaseManager.Instance != null ? FirebaseManager.Instance.ChatEntries : (IReadOnlyList<ChatEntry>)Array.Empty<ChatEntry>();

        static string MyId => FirebaseManager.Instance != null ? FirebaseManager.Instance.GetUserId() : null;

        static bool IsMine(ChatEntry e)
        {
            string me = MyId;
            if (!string.IsNullOrEmpty(me) && !string.IsNullOrEmpty(e.msg.senderId)) return e.msg.senderId == me;
            return FirebaseManager.Instance != null && ChatMessage.GetBestDisplayName(e.msg) == FirebaseManager.Instance.GetDisplayName();
        }

        static ChatEntry FindByKey(string key)
        {
            foreach (var e in Entries) if (e.key == key) return e;
            return null;
        }

        static ChatEntry FindByMessageId(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var e in Entries) if (e.msg.messageId == id) return e;
            return null;
        }

        public static Color PersonColor(string name, bool mine)
        {
            if (mine) return YellowEdge;
            return People[Mathf.Abs((name ?? "").GetHashCode()) % People.Length];
        }

        public static string Initial(string name, bool mine) =>
            mine ? "我" : (string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1));

        string Snippet(ChatEntry e) => e.IsPost ? "照片：" + (string.IsNullOrEmpty(e.msg.message) ? "餵了一張照片" : e.msg.message) : e.msg.message;

        static DateTime LocalTime(long ts)
        {
            // 舊訊息是秒；保險起見毫秒也能讀
            var t = ts > 100000000000L ? DateTimeOffset.FromUnixTimeMilliseconds(ts) : DateTimeOffset.FromUnixTimeSeconds(ts);
            return t.LocalDateTime;
        }

        public static string DayLabel(DateTime d)
        {
            string[] wk = { "日", "一", "二", "三", "四", "五", "六" };
            string md = $"{d.Month}/{d.Day}（{wk[(int)d.DayOfWeek]}）";
            if (d.Date == DateTime.Now.Date) return "今天 " + md;
            if (d.Date == DateTime.Now.Date.AddDays(-1)) return "昨天 " + md;
            return md;
        }

        void RefreshHeader()
        {
            var fb = FirebaseManager.Instance;
            string pet = PetName();
            _roomName.text = "營地";
            _roomSub.text = "一起養 " + pet;
            _petAvatar.sprite = PetSprite();
            _petAvatar.enabled = _petAvatar.sprite != null;
            if (fb == null || string.IsNullOrEmpty(fb.RoomId)) return;
            string room = fb.RoomId;
            fb.GetRoomMeta(room, meta => { if (this != null && meta != null && !string.IsNullOrEmpty(meta.name)) _roomName.text = meta.name; });
            fb.GetRoomMembers(room, list =>
            {
                if (this == null) return;
                int n = list != null ? list.Count : 0;
                _roomSub.text = n > 0 ? $"{n} 位成員・一起養 {pet}" : "一起養 " + pet;
            });
        }

        /// <summary>記下讀到哪裡（相簿右上聊天鈕的未讀數用）。</summary>
        void MarkRead()
        {
            var fb = FirebaseManager.Instance;
            if (fb == null || string.IsNullOrEmpty(fb.RoomId) || !isActiveAndEnabled) return;
            long last = 0;
            foreach (var e in Entries) last = Math.Max(last, e.msg.timestamp);
            PlayerPrefs.SetString(ReadPrefsKey(fb.RoomId), last.ToString());
        }

        public static string ReadPrefsKey(string roomId) => "BlockPet.ChatRead." + roomId;

        /// <summary>這個空間有幾則別人發、還沒看過的訊息。</summary>
        public static int UnreadCount()
        {
            var fb = FirebaseManager.Instance;
            if (fb == null || string.IsNullOrEmpty(fb.RoomId)) return 0;
            long.TryParse(PlayerPrefs.GetString(ReadPrefsKey(fb.RoomId), "0"), out long read);
            int n = 0;
            foreach (var e in fb.ChatEntries) if (e.msg.timestamp > read && !IsMine(e)) n++;
            return n;
        }

        Sprite Emoji(string key)
        {
            foreach (var s in emojiSprites) if (s != null && s.name == "emoji_" + key) return s;
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

        /// <summary>餵食照片：從本機相簿（含同步下來的）讀，縮成 432px 寬存著。還沒同步到就先空白。</summary>
        Texture PostTexture(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            if (_postTex.TryGetValue(fileName, out var t) && t != null) return t;
            var photos = SaveManager.Instance?.data?.photos;
            var meta = photos?.Find(p => p != null && p.fileName == fileName);
            if (meta == null) return null;
            var full = SaveManager.Instance.LoadPhoto(meta);
            if (full == null) return null;
            var small = Downscale(full, 432);
            Destroy(full);
            _postTex[fileName] = small;
            return small;
        }

        public static Texture Downscale(Texture2D src, int maxW)
        {
            int w = Mathf.Min(maxW, src.width);
            int h = Mathf.Max(1, Mathf.RoundToInt((float)src.height * w / src.width));
            var rt = new RenderTexture(w, h, 0) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Graphics.Blit(src, rt);
            return rt;
        }

        // ─── 訊息列表 ──────────────────────────────────────────────────

        void RebuildMessages()
        {
            foreach (Transform c in _content) Destroy(c.gameObject);
            _rows.Clear();
            _highlightRow = null;

            float y = 10f;
            DateTime? lastDay = null;
            foreach (var e in Entries)
            {
                var day = LocalTime(e.msg.timestamp).Date;
                if (lastDay == null || day != lastDay.Value)
                {
                    y += BuildDate(DayLabel(day), y) + 12f;
                    lastDay = day;
                }
                var row = BuildMessage(e, y);
                _rows[e.key] = row;
                y += row.sizeDelta.y + 12f;
            }
            if (Entries.Count == 0)
            {
                var empty = Label(_content, 0, 40, DesignW, 60, "還沒有人說話\n餵一張照片，或打聲招呼吧！", 13, Muted2, TextAlignmentOptions.Center);
                empty.lineSpacing = 8f;
                y = 120f;
            }
            _content.sizeDelta = new Vector2(DesignW, y);
        }

        float BuildDate(string text, float y)
        {
            var t = Label(_content, 0, y, 10, 20, text, 11, Muted, TextAlignmentOptions.Center);
            float w = t.GetPreferredValues(text).x + 20f;
            var chip = Place(_content, "Date", (DesignW - w) / 2f, y, w, 20);
            UIShapes.AddRounded(chip.gameObject, new Color(Ink.r, Ink.g, Ink.b, 0.08f), 10).raycastTarget = false;
            t.rectTransform.SetParent(chip, false);
            t.rectTransform.anchoredPosition = Vector2.zero;
            t.rectTransform.sizeDelta = new Vector2(w, 20f);
            return 20f;
        }

        RectTransform BuildMessage(ChatEntry e, float y)
        {
            bool mine = IsMine(e);
            string who = ChatMessage.GetBestDisplayName(e.msg);
            var row = Place(_content, "Msg", 0, y, DesignW, 40);
            // 整列底色只用來閃一下（跳到被引用的訊息）
            var hl = row.gameObject.AddComponent<Image>();
            hl.color = new Color(1, 1, 1, 0);
            hl.raycastTarget = false;

            float colX = mine ? 0f : SidePad + 30f + 8f; // 別人的訊息左邊留頭像
            float top = 0f;
            if (!mine)
            {
                var av = Place(row, "Avatar", SidePad, 16, 30, 30);
                UIShapes.AddRounded(av.gameObject, Card, 15).raycastTarget = false;
                UIShapes.AddRounded(Place(av, "Fill", 2, 2, 26, 26).gameObject, PersonColor(who, false), 13).raycastTarget = false;
                Label(av, 0, 0, 30, 30, Initial(who, false), 12, Color.white, TextAlignmentOptions.Center);
                var name = Label(row, colX + 4f, 0, 200, 15, who, 11, Muted, TextAlignmentOptions.Left);
                name.enableWordWrapping = false;
                top = 18f;
            }

            // 泡泡內容
            var bubble = Place(row, "Bubble", 0, top, 100, 40);
            float pad = e.IsPost ? 10f : 13f, padY = e.IsPost ? 10f : 9f;
            float inner = BubbleMaxW - pad * 2f;
            float bw = 0f, by = padY;
            var items = new List<(RectTransform rt, float h)>();

            var quote = FindByMessageId(e.msg.replyToMessageId);
            if (!string.IsNullOrEmpty(e.msg.replyToMessageId))
            {
                var q = BuildQuote(bubble, quote, e.msg, mine, inner);
                items.Add((q, q.sizeDelta.y));
                bw = Mathf.Max(bw, q.sizeDelta.x);
            }
            if (e.IsPost)
            {
                var head = Label(bubble, 0, 0, PostImgW, 15, "餵了 " + PetName() + " 一張照片", 11, Muted, TextAlignmentOptions.Left);
                head.enableWordWrapping = false;
                items.Add((head.rectTransform, 15f));
                var frame = Place(bubble, "Photo", 0, 0, PostImgW, PostImgH);
                UIShapes.AddRounded(frame.gameObject, Beige, 12).raycastTarget = false;
                frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                var tex = PostTexture(e.msg.photoFileName);
                if (tex != null)
                {
                    var img = Place(frame, "Img", 0, 0, PostImgW, PostImgH).gameObject.AddComponent<RawImage>();
                    img.texture = tex;
                    img.uvRect = AspectFill(tex.width, tex.height, PostImgW, PostImgH);
                    img.raycastTarget = false;
                }
                else Label(frame, 0, 0, PostImgW, PostImgH, "照片同步中…", 12, Muted2, TextAlignmentOptions.Center);
                items.Add((frame, PostImgH));
                bw = Mathf.Max(bw, PostImgW);
                if (!string.IsNullOrEmpty(e.msg.message))
                {
                    var cap = Label(bubble, 0, 0, PostImgW, 20, e.msg.message, 15, Ink, TextAlignmentOptions.TopLeft);
                    float ch = Mathf.Ceil(cap.GetPreferredValues(e.msg.message, PostImgW, 0f).y);
                    cap.rectTransform.sizeDelta = new Vector2(PostImgW, ch);
                    items.Add((cap.rectTransform, ch));
                }
            }
            else
            {
                string text = e.msg.message ?? "";
                var t = Label(bubble, 0, 0, inner, 20, text, 15, Ink, TextAlignmentOptions.TopLeft);
                t.lineSpacing = 6f;
                var pref = t.GetPreferredValues(text, inner, 0f);
                float tw = Mathf.Min(inner, Mathf.Ceil(pref.x) + 1f), th = Mathf.Ceil(pref.y);
                t.rectTransform.sizeDelta = new Vector2(tw, th);
                items.Add((t.rectTransform, th));
                bw = Mathf.Max(bw, tw);
            }
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) by += 6f;
                items[i].rt.anchoredPosition = new Vector2(pad, -by);
                by += items[i].h;
            }
            float bubbleW = bw + pad * 2f, bubbleH = by + padY;
            bubble.sizeDelta = new Vector2(bubbleW, bubbleH);
            float bx = mine ? DesignW - SidePad - bubbleW : colX;
            bubble.anchoredPosition = new Vector2(bx, -top);
            // 底色＋陰影要放在內容後面
            var shadow = UIShapes.AddShadow(bubble, new Color(30f / 255f, 30f / 255f, 20f / 255f, 0.08f), 16, 6, 2);
            shadow.transform.SetAsFirstSibling();
            var bg = UIShapes.AddRounded(Place(bubble, "Bg", 0, 0, 0, 0).gameObject, mine ? Yellow : Card, 16);
            StretchIn(bg.rectTransform);
            bg.transform.SetSiblingIndex(1);
            var b = bubble.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            b.transition = Selectable.Transition.None;
            string key = e.key;
            b.onClick.AddListener(() => OpenMenu(key));

            // 時間：泡泡旁邊、靠下
            var time = Label(row, 0, 0, 40, 14, LocalTime(e.msg.timestamp).ToString("H:mm"), 10, Muted2, mine ? TextAlignmentOptions.Right : TextAlignmentOptions.Left);
            time.enableWordWrapping = false;
            float tx = mine ? bx - 5f - 40f : bx + bubbleW + 5f;
            time.rectTransform.anchoredPosition = new Vector2(tx, -(top + bubbleH - 16f));

            float h = top + bubbleH;
            // emoji
            if (e.reactions.Count > 0)
            {
                h += 1f;
                float rx = 0f;
                var chips = Place(row, "Reacts", 0, h, 300, 24);
                foreach (var k in EmojiKeys)
                {
                    if (!e.reactions.TryGetValue(k, out var uids) || uids.Count == 0) continue;
                    bool me = !string.IsNullOrEmpty(MyId) && uids.Contains(MyId);
                    var chip = Place(chips, k, rx, 0, 44, 24);
                    UIShapes.AddRounded(chip.gameObject, me ? Yellow : Divider, 12).raycastTarget = false;
                    var cbg = UIShapes.AddRounded(Place(chip, "Fill", 0, 0, 0, 0).gameObject, me ? YellowSoft : Card, 10.5f);
                    StretchIn(cbg.rectTransform, 1.5f, 1.5f, 1.5f, 1.5f);
                    var em = Place(chip, "Emoji", 6, 4, 16, 16).gameObject.AddComponent<Image>();
                    em.sprite = Emoji(k);
                    em.raycastTarget = false;
                    var n = Label(chip, 25, 0, 20, 24, uids.Count.ToString(), 11, Ink, TextAlignmentOptions.Left);
                    n.enableWordWrapping = false;
                    float cw = 25f + n.GetPreferredValues(n.text).x + 8f;
                    chip.sizeDelta = new Vector2(cw, 24f);
                    var cb = chip.gameObject.AddComponent<Button>();
                    cb.targetGraphic = cbg;
                    cb.transition = Selectable.Transition.None;
                    string ek = k;
                    cb.onClick.AddListener(() => FirebaseManager.Instance?.ToggleChatReaction(key, ek));
                    rx += cw + 4f;
                }
                rx -= 4f;
                chips.sizeDelta = new Vector2(rx, 24f);
                chips.anchoredPosition = new Vector2(mine ? DesignW - SidePad - rx : colX, -h);
                h += 24f;
            }
            row.sizeDelta = new Vector2(DesignW, h);
            return row;
        }

        RectTransform BuildQuote(RectTransform parent, ChatEntry quoted, ChatMessage msg, bool mine, float maxW)
        {
            string who = quoted != null ? ChatMessage.GetBestDisplayName(quoted.msg) : (msg.replyToDisplayName ?? "");
            string text = quoted != null ? Snippet(quoted) : (msg.replyToMessagePreview ?? "（訊息已收回）");
            bool quotedMine = quoted != null && IsMine(quoted);
            Color c = PersonColor(who, quotedMine);

            var q = Place(parent, "Quote", 0, 0, maxW, 40);
            var bg = UIShapes.AddRounded(q.gameObject, mine ? new Color(1f, 1f, 1f, 0.55f) : Bg, 10);
            var bar = Place(q, "Bar", 0, 0, 3, 40).gameObject.AddComponent<Image>();
            bar.color = c;
            bar.raycastTarget = false;
            float x = 9f;
            var tex = quoted != null && quoted.IsPost ? PostTexture(quoted.msg.photoFileName) : null;
            if (tex != null)
            {
                var frame = Place(q, "Thumb", x, 5, 30, 30);
                UIShapes.AddRounded(frame.gameObject, Beige, 6).raycastTarget = false;
                frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                var img = Place(frame, "Img", 0, 0, 30, 30).gameObject.AddComponent<RawImage>();
                img.texture = tex;
                img.uvRect = AspectFill(tex.width, tex.height, 1f, 1f);
                img.raycastTarget = false;
                x += 37f;
            }
            var w = Label(q, x, 5, maxW - x - 8f, 14, who, 10, c, TextAlignmentOptions.Left);
            w.enableWordWrapping = false;
            var t = Label(q, x, 19, Mathf.Min(190f, maxW - x - 8f), 16, text, 11, Muted, TextAlignmentOptions.Left);
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            float width = Mathf.Min(maxW, x + Mathf.Max(w.GetPreferredValues(who).x, Mathf.Min(190f, t.GetPreferredValues(text).x)) + 8f);
            q.sizeDelta = new Vector2(width, 40f);
            bar.rectTransform.sizeDelta = new Vector2(3f, 40f);

            // 點引用：跳回原訊息並閃一下
            if (quoted != null)
            {
                var b = q.gameObject.AddComponent<Button>();
                b.targetGraphic = bg;
                b.transition = Selectable.Transition.None;
                string target = quoted.key;
                b.onClick.AddListener(() => JumpTo(target));
            }
            return q;
        }

        void JumpTo(string key)
        {
            if (!_rows.TryGetValue(key, out var row)) return;
            Canvas.ForceUpdateCanvases();
            float contentH = _content.rect.height, viewH = _viewport.rect.height;
            if (contentH > viewH)
            {
                float y = -row.anchoredPosition.y - viewH * 0.35f;
                _scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(y / (contentH - viewH));
            }
            _highlightRow = row;
            _highlightTime = 0f;
        }

        void ScrollToBottom()
        {
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 0f;
        }

        // ─── 點訊息：emoji 與選單 ──────────────────────────────────────

        void OpenMenu(string key)
        {
            var e = FindByKey(key);
            if (e == null) return;
            _menuKey = key;
            bool mine = IsMine(e);
            foreach (Transform c in _menuBox) Destroy(c.gameObject);

            float w = DesignW - 32f;
            float y = 0f;
            // emoji 列（已按過的有黃底）
            var bar = Place(_menuBox, "Emoji", 0, y, 6 * 44f + 12f, 52);
            UIShapes.AddShadow(bar, new Color(0, 0, 0, 0.25f), 26, 20, 8);
            UIShapes.AddRounded(Place(bar, "Fill", 0, 0, 6 * 44f + 12f, 52).gameObject, Card, 26).raycastTarget = false;
            for (int i = 0; i < EmojiKeys.Length; i++)
            {
                string ek = EmojiKeys[i];
                bool on = e.reactions.TryGetValue(ek, out var uids) && !string.IsNullOrEmpty(MyId) && uids.Contains(MyId);
                var btn = Place(bar, ek, 6f + i * 44f, 5, 42, 42);
                var bbg = UIShapes.AddRounded(btn.gameObject, on ? YellowSoft : new Color(1, 1, 1, 0), 21);
                var img = Place(btn, "E", 9, 9, 24, 24).gameObject.AddComponent<Image>();
                img.sprite = Emoji(ek);
                img.raycastTarget = false;
                var b = btn.gameObject.AddComponent<Button>();
                b.targetGraphic = bbg;
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => { FirebaseManager.Instance?.ToggleChatReaction(e.key, ek); CloseMenu(); });
            }
            float barX = mine ? w - bar.sizeDelta.x : 0f;
            bar.anchoredPosition = new Vector2(barX, -y);
            y += 52f + 8f;

            // 這則訊息的預覽
            string text = Snippet(e);
            var preview = Place(_menuBox, "Preview", 0, y, 280, 40);
            var pt = Label(preview, 13, 9, 254, 20, text, 15, Ink, TextAlignmentOptions.TopLeft);
            var pref = pt.GetPreferredValues(text, 254f, 0f);
            float pw = Mathf.Min(254f, Mathf.Ceil(pref.x) + 1f), ph = Mathf.Min(120f, Mathf.Ceil(pref.y));
            pt.rectTransform.sizeDelta = new Vector2(pw, ph);
            pt.overflowMode = TextOverflowModes.Ellipsis;
            preview.sizeDelta = new Vector2(pw + 26f, ph + 18f);
            var psh = UIShapes.AddShadow(preview, new Color(0, 0, 0, 0.2f), 16, 16, 6);
            psh.transform.SetAsFirstSibling();
            var pbg = UIShapes.AddRounded(Place(preview, "Bg", 0, 0, 0, 0).gameObject, mine ? Yellow : Card, 16);
            StretchIn(pbg.rectTransform);
            pbg.transform.SetSiblingIndex(1);
            preview.anchoredPosition = new Vector2(mine ? w - preview.sizeDelta.x : 0f, -y);
            y += preview.sizeDelta.y + 8f;

            // 回覆／複製／收回
            var items = new List<(string label, Color color, Action act)>
            {
                ("回覆", Ink, () => { SetReply(e.key); CloseMenu(); _input.ActivateInputField(); }),
                ("複製", Ink, () => { GUIUtility.systemCopyBuffer = e.msg.message ?? ""; CloseMenu(); Toast("已複製"); }),
            };
            if (mine) items.Add(("收回", Red, () =>
            {
                CloseMenu();
                FirebaseManager.Instance?.RecallChatMessage(e.key, ok => { if (!ok && this != null) Toast("收回失敗，請再試一次"); });
            }));
            var menu = Place(_menuBox, "Menu", 0, y, 200, items.Count * 48f);
            UIShapes.AddShadow(menu, new Color(0, 0, 0, 0.25f), 16, 20, 8);
            UIShapes.AddRounded(Place(menu, "Fill", 0, 0, 200, items.Count * 48f).gameObject, Card, 16).raycastTarget = false;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var r = Place(menu, it.label, 0, i * 48f, 200, 48);
                var hit = r.gameObject.AddComponent<Image>();
                hit.color = new Color(1, 1, 1, 0);
                Label(r, 16, 0, 168, 48, it.label, 15, it.color, TextAlignmentOptions.Left);
                if (i < items.Count - 1)
                {
                    var d = Place(r, "Line", 0, 47, 200, 1).gameObject.AddComponent<Image>();
                    d.color = Divider;
                    d.raycastTarget = false;
                }
                var b = r.gameObject.AddComponent<Button>();
                b.targetGraphic = hit;
                b.transition = Selectable.Transition.None;
                var act = it.act;
                b.onClick.AddListener(() => act());
            }
            menu.anchoredPosition = new Vector2(mine ? w - 200f : 0f, -y);
            y += menu.sizeDelta.y;

            // 放在畫面上 230 的位置（設計稿），太長就往上移
            _menuBox.sizeDelta = new Vector2(w, y);
            float topY = Mathf.Min(_topInset + 230f, _screenH - _bottomInset - y - 20f);
            _menuBox.anchoredPosition = new Vector2(16f + w / 2f, -Mathf.Max(_topInset + 70f, topY)); // pivot 在上緣中間（彈出時從中間放大）
            _menuLayer.gameObject.SetActive(true);
        }

        void CloseMenu(bool instant = false)
        {
            _menuKey = null;
            if (instant && _menuLayer != null) { _menuT = 0f; _menuLayer.gameObject.SetActive(false); }
        }

        // ─── 回覆與送出 ────────────────────────────────────────────────

        void SetReply(string key)
        {
            var e = key != null ? FindByKey(key) : null;
            _replyKey = e?.key;
            _replyBar.gameObject.SetActive(e != null);
            if (e != null)
            {
                string who = ChatMessage.GetBestDisplayName(e.msg);
                Color c = PersonColor(who, IsMine(e));
                _replyWho.text = "回覆 " + who;
                _replyWho.color = c;
                _replyAccent.color = c;
                _replyText.text = Snippet(e);
                var tex = e.IsPost ? PostTexture(e.msg.photoFileName) : null;
                _replyImg.transform.parent.gameObject.SetActive(tex != null);
                if (tex != null)
                {
                    _replyImg.texture = tex;
                    _replyImg.uvRect = AspectFill(tex.width, tex.height, 1f, 1f);
                }
                float x = tex != null ? 72f : 34f;
                _replyWho.rectTransform.anchoredPosition = new Vector2(x, _replyWho.rectTransform.anchoredPosition.y);
                _replyText.rectTransform.anchoredPosition = new Vector2(x, _replyText.rectTransform.anchoredPosition.y);
                ((TMP_Text)_input.placeholder).text = "回覆 " + who + "…";
            }
            else ((TMP_Text)_input.placeholder).text = "說點什麼…";
            _lastW = -1f; // 輸入區高度變了
        }

        void Send()
        {
            string text = (_input.text ?? "").Trim();
            var fb = FirebaseManager.Instance;
            if (text.Length == 0 || fb == null) return;
            var msg = new ChatMessage(fb.GetDisplayName(), text) { senderId = fb.GetUserId(), kind = "text" };
            var reply = _replyKey != null ? FindByKey(_replyKey) : null;
            if (reply != null)
            {
                if (string.IsNullOrEmpty(reply.msg.messageId)) reply.msg.messageId = reply.key;
                msg.replyToMessageId = reply.msg.messageId;
                msg.replyToDisplayName = ChatMessage.GetBestDisplayName(reply.msg);
                msg.replyToMessagePreview = Snippet(reply);
            }
            fb.SendChatMessage(msg);
            _input.text = "";
            SetReply(null);
            _stickToBottom = true;
        }

        void Back()
        {
            if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
            pageManager?.ShowHomePage();
        }

        void OpenAlbum()
        {
            if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
            pageManager?.ShowAlbumFromChat();
        }

        void OpenCamera()
        {
            // 「＋」：拍一張餵給寵物，照片會自動出現在聊天室
            Back();
            FindObjectOfType<CameraUIManager>(true)?.OpenCameraFromHand();
        }

        void Toast(string msg)
        {
            _toastText.text = msg;
            float w = Mathf.Min(DesignW - 40f, _toastText.GetPreferredValues(msg).x + 32f);
            _toast.sizeDelta = new Vector2(w, 40f);
            _toast.anchoredPosition = new Vector2((DesignW - w) / 2f, _toast.anchoredPosition.y);
            _toastUntil = Time.unscaledTime + 1.8f;
        }

        // ─── 版面 ──────────────────────────────────────────────────────

        void Relayout()
        {
            float w = _root.rect.width, h = _root.rect.height;
            var safe = Screen.safeArea;
            float scale = _canvas.rootCanvas.scaleFactor;
            float px = scale > 0f ? 1f / scale : 1f;
            float kb = _input.isFocused && TouchScreenKeyboard.visible ? TouchScreenKeyboard.area.height * px : 0f;
            if (w <= 0f || h <= 0f) return;
            if (Mathf.Approximately(w, _lastW) && Mathf.Approximately(h, _lastH) && safe == _lastSafe && Mathf.Approximately(kb, _lastKb)) return;
            bool kbChanged = !Mathf.Approximately(kb, _lastKb);
            _lastW = w; _lastH = h; _lastSafe = safe; _lastKb = kb;

            _k = w / DesignW;
            _topInset = (Screen.height - safe.yMax) * px / _k;
            _bottomInset = safe.yMin * px / _k;
            _screenH = h / _k;
            float kbDesign = kb / _k;

            _design.localScale = Vector3.one * _k;
            _design.sizeDelta = new Vector2(DesignW, _screenH);
            ((RectTransform)_design.Find("Header")).anchoredPosition = new Vector2(0f, 0f);
            ((RectTransform)_design.Find("Header")).sizeDelta = new Vector2(DesignW, _topInset + HeaderH);
            ((RectTransform)_design.Find("Header/Row")).anchoredPosition = new Vector2(0f, -_topInset);

            // 鍵盤出來時輸入列貼在鍵盤上方，否則留 Home 指示條的安全區
            float replyH = _replyBar.gameObject.activeSelf ? ReplyBarH : 0f;
            float bottomPad = kbDesign > 0f ? 8f : 20f + _bottomInset;
            float bottomH = 8f + replyH + 42f + bottomPad;
            _bottom.sizeDelta = new Vector2(DesignW, bottomH);
            _bottom.anchoredPosition = new Vector2(0f, -(_screenH - kbDesign - bottomH));
            _replyBar.anchoredPosition = new Vector2(12f, -8f);
            ((RectTransform)_bottom.Find("Row")).anchoredPosition = new Vector2(10f, -(8f + replyH));

            float vpTop = _topInset + HeaderH;
            _viewport.anchoredPosition = new Vector2(0f, -vpTop);
            _viewport.sizeDelta = new Vector2(DesignW, _screenH - vpTop - kbDesign - bottomH);

            _toast.anchoredPosition = new Vector2(_toast.anchoredPosition.x, -(_screenH - kbDesign - bottomH - 52f));
            if (kbChanged) _dirty = true; // 保持在最新訊息
        }

        // ─── 建立介面 ──────────────────────────────────────────────────

        void Build()
        {
            // 獨立的最上層 Canvas（不受頁面上層 CanvasGroup 影響），顯示／隱藏由 OnEnable／OnDisable 同步
            var go = new GameObject("ChatScreen (runtime)", typeof(RectTransform));
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

            BuildMessagesArea();
            BuildHeader();
            BuildInput();
            BuildMenu();
            BuildToast();
            go.SetActive(false); // 元件啟用時（OnEnable）才顯示
        }

        void BuildHeader()
        {
            var header = Place(_design, "Header", 0, 0, DesignW, HeaderH);
            header.gameObject.AddComponent<Image>().color = new Color(1f, 0.973f, 0.925f, 0.94f);
            var line = Place(header, "Line", 0, 0, DesignW, 1);
            line.anchorMin = line.anchorMax = new Vector2(0f, 0f);
            line.pivot = new Vector2(0f, 0f);
            line.anchoredPosition = Vector2.zero;
            line.gameObject.AddComponent<Image>().color = Divider;

            var row = Place(header, "Row", 0, 0, DesignW, HeaderH);
            var back = Place(row, "Back", 12, 10, 44, 44);
            var bbg = UIShapes.AddRounded(back.gameObject, Beige, 22);
            var chev = AddGraphic<UIPolygon>(Place(back, "Chevron", 0, 0, 44, 44));
            chev.color = Ink;
            chev.raycastTarget = false;
            chev.SetPoints(new Vector2(25f, 14.5f), new Vector2(25f, 29.5f), new Vector2(17.5f, 22f));
            var bb = back.gameObject.AddComponent<Button>();
            bb.targetGraphic = bbg;
            bb.onClick.AddListener(Back);

            var av = Place(row, "Pet", 66, 12, 40, 40);
            UIShapes.AddShadow(av, new Color(30f / 255f, 30f / 255f, 20f / 255f, 0.12f), 20, 6, 2);
            UIShapes.AddRounded(Place(av, "Border", 0, 0, 40, 40).gameObject, Card, 20).raycastTarget = false;
            UIShapes.AddRounded(Place(av, "Fill", 2, 2, 36, 36).gameObject, Hex(0xFCE7A6), 18).raycastTarget = false;
            _petAvatar = Place(av, "Img", 3, 5, 34, 29).gameObject.AddComponent<Image>();
            _petAvatar.preserveAspect = true;
            _petAvatar.raycastTarget = false;

            _roomName = Label(row, 116, 13, 200, 22, "", 16, Ink, TextAlignmentOptions.Left);
            _roomName.enableWordWrapping = false;
            _roomName.overflowMode = TextOverflowModes.Ellipsis;
            _roomSub = Label(row, 116, 35, 210, 16, "", 11, Muted2, TextAlignmentOptions.Left);
            _roomSub.enableWordWrapping = false;
            _roomSub.overflowMode = TextOverflowModes.Ellipsis;

            // 相簿鈕：兩張疊起來的照片
            var album = Place(row, "Album", DesignW - 12f - 44f, 10, 44, 44);
            var abg = UIShapes.AddRounded(album.gameObject, Beige, 14);
            UIShapes.AddRing(Place(album, "Back", 16, 11, 17, 17).gameObject, Ink, 3.5f);
            UIShapes.AddRounded(Place(album, "FrontFill", 10, 16, 18, 18).gameObject, Beige, 3.5f).raycastTarget = false;
            UIShapes.AddRing(Place(album, "Front", 10, 16, 18, 18).gameObject, Ink, 3.5f);
            var ab = album.gameObject.AddComponent<Button>();
            ab.targetGraphic = abg;
            ab.onClick.AddListener(OpenAlbum);
        }

        void BuildMessagesArea()
        {
            _viewport = Place(_design, "Viewport", 0, HeaderH, DesignW, 600);
            _viewport.gameObject.AddComponent<RectMask2D>();
            var vp = _viewport.gameObject.AddComponent<Image>();
            vp.color = new Color(1, 1, 1, 0);
            _content = Place(_viewport, "Content", 0, 0, DesignW, 600);
            _scroll = _viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = _viewport;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Elastic;
            _scroll.scrollSensitivity = 20f;
        }

        void BuildInput()
        {
            _bottom = Place(_design, "Bottom", 0, 700, DesignW, InputH);
            _bottom.gameObject.AddComponent<Image>().color = new Color(1f, 0.973f, 0.925f, 0.96f);
            var line = Place(_bottom, "Line", 0, 0, DesignW, 1).gameObject.AddComponent<Image>();
            line.color = Divider;
            line.raycastTarget = false;

            // 回覆列
            _replyBar = Place(_bottom, "Reply", 12, 8, DesignW - 24f, 46);
            UIShapes.AddRounded(_replyBar.gameObject, Card, 12).raycastTarget = false;
            _replyAccent = Place(_replyBar, "Accent", 0, 0, 3, 46).gameObject.AddComponent<Image>();
            _replyAccent.raycastTarget = false;
            var arrow = AddGraphic<UIPolygon>(Place(_replyBar, "Arrow", 10, 15, 16, 16));
            arrow.color = Muted2;
            arrow.raycastTarget = false;
            arrow.SetPoints(new Vector2(1f, 8f), new Vector2(8f, 2f), new Vector2(8f, 14f));
            var thumb = Place(_replyBar, "Thumb", 32, 7, 32, 32);
            UIShapes.AddRounded(thumb.gameObject, Beige, 6).raycastTarget = false;
            thumb.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            _replyImg = Place(thumb, "Img", 0, 0, 32, 32).gameObject.AddComponent<RawImage>();
            _replyImg.raycastTarget = false;
            _replyWho = Label(_replyBar, 34, 6, 260, 15, "", 11, Ink, TextAlignmentOptions.Left);
            _replyWho.enableWordWrapping = false;
            _replyText = Label(_replyBar, 34, 22, 260, 18, "", 12, Muted, TextAlignmentOptions.Left);
            _replyText.enableWordWrapping = false;
            _replyText.overflowMode = TextOverflowModes.Ellipsis;
            var cancel = Place(_replyBar, "Cancel", DesignW - 24f - 6f - 32f, 7, 32, 32);
            var cbg = UIShapes.AddRounded(cancel.gameObject, Beige, 16);
            foreach (float z in new[] { 45f, -45f })
            {
                var x = Place(cancel, "X", 0, 0, 10, 2.2f);
                x.pivot = new Vector2(0.5f, 0.5f);
                x.anchoredPosition = new Vector2(16f, -16f);
                x.localRotation = Quaternion.Euler(0, 0, z);
                var xi = x.gameObject.AddComponent<Image>();
                xi.color = Ink;
                xi.raycastTarget = false;
            }
            var cb = cancel.gameObject.AddComponent<Button>();
            cb.targetGraphic = cbg;
            cb.onClick.AddListener(() => SetReply(null));
            _replyBar.gameObject.SetActive(false);

            // 輸入列：＋、輸入框、送出
            var row = Place(_bottom, "Row", 10, 8, DesignW - 20f, 42);
            var plus = Place(row, "Plus", 0, 1, 40, 40);
            var pbg = UIShapes.AddRounded(plus.gameObject, Beige, 20);
            foreach (var (pw, ph) in new[] { (14f, 2.4f), (2.4f, 14f) })
            {
                var bar = Place(plus, "Bar", (40f - pw) / 2f, (40f - ph) / 2f, pw, ph).gameObject.AddComponent<Image>();
                bar.color = Ink;
                bar.raycastTarget = false;
            }
            var pb = plus.gameObject.AddComponent<Button>();
            pb.targetGraphic = pbg;
            pb.onClick.AddListener(OpenCamera);

            float inputW = DesignW - 20f - 40f - 8f - 42f - 8f;
            _input = BuildInputField(row, 48, 0, inputW, 42);

            var send = Place(row, "Send", DesignW - 20f - 42f, 0, 42, 42);
            _sendEdge = UIShapes.AddRounded(Place(send, "Edge", 0, 3, 42, 42).gameObject, YellowEdge, 21);
            _sendEdge.raycastTarget = false;
            _sendBg = UIShapes.AddRounded(Place(send, "Fill", 0, 0, 42, 42).gameObject, Yellow, 21);
            _sendArrow = AddGraphic<UIPolygon>(Place(send, "Arrow", 0, 0, 42, 42));
            _sendArrow.raycastTarget = false;
            _sendArrow.SetPoints(new Vector2(21f, 12f), new Vector2(29f, 21f), new Vector2(23.2f, 21f), new Vector2(23.2f, 30f),
                                 new Vector2(18.8f, 30f), new Vector2(18.8f, 21f), new Vector2(13f, 21f));
            var sb = send.gameObject.AddComponent<Button>();
            sb.targetGraphic = _sendBg;
            sb.transition = Selectable.Transition.None;
            sb.onClick.AddListener(Send);
        }

        TMP_InputField BuildInputField(RectTransform parent, float x, float y, float w, float h)
        {
            var rt = Place(parent, "Input", x, y, w, h);
            rt.gameObject.SetActive(false); // 先設定好 TMP_InputField 的欄位再啟用
            UIShapes.AddRounded(rt.gameObject, Line, h / 2f);
            var fill = UIShapes.AddRounded(Place(rt, "Fill", 1.5f, 1.5f, w - 3f, h - 3f).gameObject, Color.white, h / 2f - 1.5f);
            var area = Place(rt, "Text Area", 14, 0, w - 28f, h);
            area.gameObject.AddComponent<RectMask2D>();
            var ph = Label(area, 0, 0, w - 28f, h, "說點什麼…", 15, new Color(Muted.r, Muted.g, Muted.b, 0.55f), TextAlignmentOptions.Left);
            var text = Label(area, 0, 0, w - 28f, h, "", 15, Ink, TextAlignmentOptions.Left);
            ph.enableWordWrapping = text.enableWordWrapping = false;
            var input = rt.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = fill;
            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = ph;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 500;
            input.shouldHideMobileInput = true; // 輸入列自己會移到鍵盤上方
            input.onSubmit.AddListener(_ => Send());
            if (skin != null && skin.font != null) input.fontAsset = skin.font;
            rt.gameObject.SetActive(true);
            return input;
        }

        void BuildMenu()
        {
            _menuLayer = Stretch(_root, "MenuLayer");
            _menuGroup = _menuLayer.gameObject.AddComponent<CanvasGroup>();
            var dim = Stretch(_menuLayer, "Dim").gameObject.AddComponent<Image>();
            dim.color = new Color(20f / 255f, 24f / 255f, 14f / 255f, 0.42f);
            dim.gameObject.AddComponent<Button>().onClick.AddListener(() => CloseMenu());
            var scaled = new GameObject("Design", typeof(RectTransform)).GetComponent<RectTransform>();
            scaled.SetParent(_menuLayer, false);
            scaled.anchorMin = scaled.anchorMax = new Vector2(0.5f, 1f);
            scaled.pivot = new Vector2(0.5f, 1f);
            scaled.sizeDelta = new Vector2(DesignW, 772f);
            scaled.gameObject.AddComponent<ScaleWith>().source = _design;
            _menuBox = Place(scaled, "Box", 16, 230, DesignW - 32f, 300);
            _menuBox.pivot = new Vector2(0.5f, 1f);
            _menuLayer.gameObject.SetActive(false);
        }

        void BuildToast()
        {
            _toast = Place(_design, "Toast", 20, 600, DesignW - 40f, 40);
            _toastGroup = _toast.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.blocksRaycasts = false;
            _toastGroup.alpha = 0f;
            UIShapes.AddRounded(_toast.gameObject, new Color(Ink.r, Ink.g, Ink.b, 0.92f), 20).raycastTarget = false;
            _toastText = Label(_toast, 0, 0, 0, 0, "", 13, Paper, TextAlignmentOptions.Center);
            StretchIn(_toastText.rectTransform, 16, 0, 16, 0);
        }

        void MakeBackground()
        {
            var photo = _root.Find("BackgroundPhoto").GetComponent<RawImage>();
            if (background == null) { photo.enabled = false; return; }
            if (_blurred == null || !_blurred.IsCreated())
            {
                // 縮小成 40px 寬再放大，當作 blur(14px)
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
            t.richText = false; // 聊天內容不解析 rich text
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

        public static Rect AspectFill(float texW, float texH, float boxW, float boxH)
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

        static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
