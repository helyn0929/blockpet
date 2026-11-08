using System;
using System.Collections;
using System.Collections.Generic;
using BlockPet.Hand;
using BlockPet.Polaroid;
using BlockPet.UI.Chat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Polaroid
{
    /// <summary>
    /// 拍立得分割畫面（設計文件 5.26；docs/polaroid/polaroid_spec.json）：取代 5.16 分割畫面與 5.14 ⑥ 照片檢視。
    /// 入口：首頁最新照片貼紙、相簿點照片、聊天室「到營地看這張」（都打開這裡，左右滑換張）。
    /// - 上：返回、「小美拍的・3 分鐘前」、右上辨識類別。
    /// - 中：拍立得卡片（畫框、照片＋濾鏡、照片左下最新 2 則留言、標題、大家的貼紙）。
    /// - 下：分頁「貼紙／畫框／濾鏡」＋分享、下載；工具區；短滑動條；最底下整條留言框（打字時移到鍵盤上方）。
    /// 貼紙、畫框、濾鏡、標題存在 Rooms/{room}/polaroid（FirebaseManager.Polaroid），營地成員都看得到；規則在 PolaroidRules。
    /// 執行時自己建一個最上層的 Screen Space Overlay（sortingOrder 37），390 × 772 設計框等比縮放、放在安全區裡。
    /// </summary>
    public partial class PolaroidScreen : MonoBehaviour
    {
        const float DesignW = 390f, DesignH = 772f;
        const float CardX = 18f, CardY = 54f;
        const float InputY = 706f, InputH = 46f;

        static readonly Color Bg = PUI.Hex(0xF1ECE3);
        static readonly Color Ink = PUI.Hex(0x3A2E22);
        static readonly Color Sub = PUI.Hex(0x6B5D4E);
        static readonly Color Note = PUI.Hex(0x8A7B68);
        static readonly Color Cream = PUI.Hex(0xFFFCF4);
        static readonly Color Coral = PUI.Hex(0xE86B4F);
        static readonly Color Yellow = PUI.Hex(0xF6C945);
        static readonly Color MeAvatar = PUI.Hex(0xD9A92C);

        static PolaroidScreen _instance;

        PolaroidSkin _skin;
        TMP_FontAsset _font;

        readonly List<PhotoMeta> _photos = new List<PhotoMeta>();
        int _index;
        bool _open;
        float _openT, _enterT = 1f;

        Canvas _canvas;
        CanvasGroup _group;
        RectTransform _root, _design, _cardHolder;
        CanvasGroup _cardGroup;
        PolaroidCard _card;
        Image _flash;
        float _flashT = -1f;

        // 上方
        RectTransform _ownerAvatar, _category;
        Image _ownerAvatarBg, _categoryBg, _categoryDot;
        TMP_Text _ownerInitial, _ownerLabel, _ownerTime, _categoryText;
        AvatarPhoto _ownerPhoto;

        // 卡片右下編輯鈕（切換寵物頁／裝飾頁；下載的圖不含）
        Image _editIcon, _editDot;

        // 照片
        Texture2D _square, _thumbBase, _filtered;
        readonly Dictionary<string, Texture2D> _filterThumbs = new Dictionary<string, Texture2D>();
        string _filteredKey;
        Coroutine _loader;

        // 留言
        bool _commentsDirty;
        int _shownComments = -1;
        RectTransform _risePill;
        CanvasGroup _riseGroup;
        float _riseT = 1f;

        // 輸入
        enum InputMode { Comment, Caption }
        InputMode _mode;
        RectTransform _inputBar;
        TMP_InputField _input;
        TMP_Text _placeholder;
        float _lastW = -1f, _lastH = -1f, _lastKb = -1f, _k = 1f;
        Rect _lastSafe;

        PolaroidStyle _style = new PolaroidStyle();
        readonly List<PolaroidSticker> _stickers = new List<PolaroidSticker>();

        PhotoMeta Current => _photos.Count > 0 ? _photos[Mathf.Clamp(_index, 0, _photos.Count - 1)] : null;
        static string Me => _captureMe ?? (FirebaseManager.Instance != null ? FirebaseManager.Instance.GetUserId() : null);
        static string _captureMe;                                   // Editor 截圖用
        static System.Func<PhotoMeta, List<ChatEntry>> _captureComments;
        static float _captureHearts = -1f;                          // Editor 截圖：寵物頁的愛心、進化、想吃
        static float[] _captureEnergy;
        static string _captureWant;
        bool IsOwner => Current != null && PolaroidRules.CanStyle(Current.takerUserId, Me);
        string OwnerName(PhotoMeta m) =>
            m == null ? "" : (IsMine(m) ? "我" : (string.IsNullOrEmpty(m.takerName) ? "營地" : m.takerName));
        static bool IsMine(PhotoMeta m) => m != null && !string.IsNullOrEmpty(Me) && m.takerUserId == Me;

        // ─── 入口 ──────────────────────────────────────────────────────

        /// <summary>相簿：照相簿的順序，從 photos[index] 開始。</summary>
        public static void Open(IList<PhotoMeta> photos, int index)
        {
            if (photos == null || photos.Count == 0) return;
            Instance().Show(photos, index);
        }

        /// <summary>首頁（最新照片貼紙）、推播：今天的新照片（新到舊），從 fileName 開始（null＝最新一張）。沒有照片回傳 false。</summary>
        public static bool OpenToday(string fileName = null)
        {
            var all = AllPhotosNewestFirst();
            if (all.Count == 0) return false;
            var target = string.IsNullOrEmpty(fileName) ? all[0] : all.Find(p => p.fileName == fileName);
            if (target == null) return false;
            var today = all.FindAll(p => DateTime.TryParse(p.timestamp, out var t) && t.Date == DateTime.Today);
            if (!today.Contains(target)) today.Insert(0, target);
            Instance().Show(today, today.IndexOf(target));
            return true;
        }

        /// <summary>聊天室「到營地看這張」：營地全部照片（新到舊），從這張開始。照片還沒同步到相簿時回傳 false。</summary>
        public static bool OpenPhoto(string fileName)
        {
            var all = AllPhotosNewestFirst();
            int i = all.FindIndex(p => p.fileName == fileName);
            if (i < 0) return false;
            Instance().Show(all, i);
            return true;
        }

        /// <summary>聊天室右上「營地照片」：營地全部照片（新到舊），從最新一張開始。沒有照片回傳 false。</summary>
        public static bool OpenLatest()
        {
            var all = AllPhotosNewestFirst();
            if (all.Count == 0) return false;
            Instance().Show(all, 0);
            return true;
        }

        static List<PhotoMeta> AllPhotosNewestFirst()
        {
            var list = new List<PhotoMeta>();
            var photos = SaveManager.Instance?.data?.photos;
            if (photos != null) foreach (var p in photos) if (p != null && !string.IsNullOrEmpty(p.fileName)) list.Add(p);
            list.Sort((a, b) => string.CompareOrdinal(b.timestamp ?? "", a.timestamp ?? ""));
            return list;
        }

        static PolaroidScreen Instance()
        {
            if (_instance == null)
            {
                var go = new GameObject("PolaroidScreen (runtime)", typeof(RectTransform));
                _instance = go.AddComponent<PolaroidScreen>();
                _instance.Build();
            }
            return _instance;
        }

        void Show(IList<PhotoMeta> photos, int index)
        {
            _photos.Clear();
            foreach (var p in photos) if (p != null && !string.IsNullOrEmpty(p.fileName)) _photos.Add(p);
            if (_photos.Count == 0) return;
            _index = Mathf.Clamp(index, 0, _photos.Count - 1);
            gameObject.SetActive(true);
            if (!_open)
            {
                FirebaseManager.OnChatEntryAdded += OnChat;
                FirebaseManager.OnChatEntryChanged += OnChat;
                FirebaseManager.OnChatEntryRemoved += OnChatRemoved;
                FirebaseManager.OnChatEntriesReset += OnChatReset;
                FirebaseManager.OnPolaroidChanged += OnPolaroidData;
            }
            _open = true;
            _tab = Tab.Stickers;
            SetPage(Page.Pet, true);
            ShowPhoto(false);
        }

        void Close()
        {
            if (!_open) return;
            _open = false;
            EndTyping();
            EndSendMode();
            FirebaseManager.OnChatEntryAdded -= OnChat;
            FirebaseManager.OnChatEntryChanged -= OnChat;
            FirebaseManager.OnChatEntryRemoved -= OnChatRemoved;
            FirebaseManager.OnChatEntriesReset -= OnChatReset;
            FirebaseManager.OnPolaroidChanged -= OnPolaroidData;
            FirebaseManager.Instance?.StopPolaroid();
            // 看過的照片不再算新（同原本分割畫面，與相簿共用同一個記錄）
            var fb = FirebaseManager.Instance;
            if (fb != null && !string.IsNullOrEmpty(fb.RoomId)) PlayerPrefs.SetString("BlockPet.AlbumSeen." + fb.RoomId, DateTime.Now.ToString("o"));
        }

        void OnChat(ChatEntry e) => _commentsDirty = true;
        void OnChatRemoved(string key) => _commentsDirty = true;
        void OnChatReset() => _commentsDirty = true;

        void Kill(UnityEngine.Object o) => PUI.Kill(o);

        void OnDestroy()
        {
            Close();
            ReleasePhoto();
            if (_instance == this) _instance = null;
        }

        // ─── 每幀 ──────────────────────────────────────────────────────

        void Update()
        {
            Relayout();
            float dt = Time.unscaledDeltaTime;
            _openT = Mathf.MoveTowards(_openT, _open ? 1f : 0f, dt / 0.2f);
            _group.alpha = _openT;
            _group.blocksRaycasts = _open;
            if (!_open && _openT <= 0f)
            {
                ReleasePhoto();
                gameObject.SetActive(false);
                return;
            }

            // 換張：0.3 秒由下 8 淡入
            if (_enterT < 1f)
            {
                _enterT = Mathf.MoveTowards(_enterT, 1f, dt / 0.3f);
                float e = 1f - (1f - _enterT) * (1f - _enterT);
                _cardHolder.anchoredPosition = new Vector2(CardX, -CardY - 8f * (1f - e));
                _cardGroup.alpha = e;
            }

            // 下載完成：卡片閃一下白（0.9 → 0，0.5 秒）
            if (_flashT >= 0f)
            {
                _flashT += dt;
                _flash.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.9f, 0f, _flashT / 0.5f));
                if (_flashT >= 0.5f) { _flashT = -1f; _flash.enabled = false; }
            }

            // 新留言從下面浮上來（0.35 秒）
            if (_riseT < 1f && _risePill != null)
            {
                _riseT = Mathf.MoveTowards(_riseT, 1f, dt / 0.35f);
                float e = 1f - (1f - _riseT) * (1f - _riseT);
                _riseGroup.alpha = e;
                _risePill.localPosition = new Vector3(_risePill.localPosition.x, _riseBaseY - 12f * (1f - e), 0f);
            }

            if (_commentsDirty) { _commentsDirty = false; BuildComments(); }
            UpdateStickers(dt);
            UpdatePanel(dt);
        }

        float _riseBaseY;

        // ─── 換張 ──────────────────────────────────────────────────────

        void Go(int delta)
        {
            int next = _index + delta;
            if (next < 0 || next >= _photos.Count) return; // 不循環
            _index = next;
            ShowPhoto(true);
        }

        void ShowPhoto(bool animate)
        {
            var m = Current;
            if (m == null) { Close(); return; }
            EndTyping();
            Deselect();
            _enterT = animate ? 0f : 1f;
            _cardGroup.alpha = animate ? 0f : 1f;
            _cardHolder.anchoredPosition = new Vector2(CardX, -CardY - (animate ? 8f : 0f));

            // 上方：誰拍的（兩行：「小美拍的」／時間）、類別小標籤
            string who = OwnerName(m);
            DateTime.TryParse(m.timestamp, out var t);
            _ownerLabel.text = who + "拍的";
            _ownerTime.text = t == default ? "" : Ago(t);
            _ownerAvatarBg.color = IsMine(m) ? MeAvatar : ChatScreenController.PersonColor(who, false);
            _ownerInitial.text = ChatScreenController.Initial(who, IsMine(m));
            _ownerPhoto?.SetUser(m.takerUserId);
            SetCategory(m.category);

            // 拍立得資料：先用 Firebase 已經有的（同一張），沒有就預設，等監聽回來再更新
            var fb = FirebaseManager.Instance;
            fb?.WatchPolaroid(m.fileName);
            bool have = fb != null && fb.PolaroidFileNow == m.fileName;
            _style = have ? Copy(fb.PolaroidStyleNow) : new PolaroidStyle();
            _stickers.Clear();
            if (have) _stickers.AddRange(fb.PolaroidStickersNow);

            _placeholder.text = PlaceholderFor(m);
            _input.text = "";
            _mode = InputMode.Comment;
            _input.characterLimit = 200;
            SyncCaptionMode();

            ReleasePhoto();
            _card.SetPhoto(null, false);
            ApplyStyle();
            RebuildStickers();
            _shownComments = -1;
            BuildComments();
            BuildTray(true);
            RefreshPetPage();

            if (_loader != null) StopCoroutine(_loader);
            _loader = StartCoroutine(LoadPhoto(m));
        }

        string PlaceholderFor(PhotoMeta m) => IsMine(m) ? "留言…" : $"留言給{OwnerName(m)}…";

        static PolaroidStyle Copy(PolaroidStyle s) => s == null ? new PolaroidStyle() :
            new PolaroidStyle { frame = s.frame, filter = s.filter, caption = s.caption, by = s.by, at = s.at };

        /// <summary>Firebase 送來這張照片的貼紙／畫框／濾鏡／標題。</summary>
        void OnPolaroidData(string file)
        {
            var m = Current;
            var fb = FirebaseManager.Instance;
            if (m == null || fb == null || file != m.fileName) return;
            string oldFilter = _style.filter;
            _style = Copy(fb.PolaroidStyleNow);
            ApplyStyle();
            if (_mode == InputMode.Caption && !_input.isFocused) _input.text = _style.caption ?? "";
            if ((oldFilter ?? "none") != (_style.filter ?? "none")) RefreshFiltered();
            _stickers.Clear();
            _stickers.AddRange(fb.PolaroidStickersNow);
            if (!_draggingSticker) RebuildStickers();
            BuildTray();
        }

        void ApplyStyle()
        {
            _card.ApplyStyle(_style, false, PhotoTime(Current)); // 2026-11-08 使用者：拿掉標題旁的編輯筆，寫標題改從右下編輯鈕進入
            // 編輯鈕：鉛筆顏色跟畫框字色、70%
            var ink = PolaroidRules.Frame(_style.frame).ink;
            if (_editIcon != null) _editIcon.color = new Color(ink.r, ink.g, ink.b, 0.7f);
        }

        static DateTime PhotoTime(PhotoMeta m) => m != null && DateTime.TryParse(m.timestamp, out var t) ? t : default;

        // ─── 照片 ──────────────────────────────────────────────────────

        IEnumerator LoadPhoto(PhotoMeta m)
        {
            yield return null; // 先讓卡片和工具區畫出來（讀大圖要一點時間）
            var sq = PolaroidPhoto.LoadSquare(m, PolaroidPhoto.PhotoPx);
            if (this == null) yield break;
            if (Current != m) { if (sq != null) Kill(sq); yield break; }
            _square = sq;
            if (_square != null) _thumbBase = PolaroidPhoto.Square(_square, PolaroidPhoto.ThumbPx);
            RefreshFiltered();
            yield return null;
            // 濾鏡縮圖（用這張照片套每一種濾鏡）
            if (_thumbBase != null)
                foreach (var f in PolaroidRules.Filters)
                {
                    if (Current != m) yield break;
                    _filterThumbs[f.key] = PolaroidPhoto.Filtered(_thumbBase, f.key);
                }
            if (_tab == Tab.Filters) BuildTray();
            _loader = null;
        }

        void RefreshFiltered()
        {
            if (_square == null) return;
            string key = _style.filter ?? PolaroidRules.DefaultFilter;
            if (key == _filteredKey && _filtered != null) return;
            if (_filtered != null) Kill(_filtered);
            _filtered = PolaroidPhoto.Filtered(_square, key);
            _filteredKey = key;
            _card.SetPhoto(_filtered, Current != null && Current.fisheye); // 先裁正方形、套顏色濾鏡，再套魚眼
        }

        void ReleasePhoto()
        {
            if (_loader != null) { StopCoroutine(_loader); _loader = null; }
            if (_square != null) Kill(_square);
            if (_thumbBase != null) Kill(_thumbBase);
            if (_filtered != null) Kill(_filtered);
            foreach (var t in _filterThumbs.Values) if (t != null) Kill(t);
            _filterThumbs.Clear();
            _square = _thumbBase = _filtered = null;
            _filteredKey = null;
        }

        // ─── 上方 ──────────────────────────────────────────────────────

        /// <summary>
        /// 辨識類別小膠囊：接在「誰拍的」後面；高 22、圓角 11、內距 0 9 0 7；色點 8（白圈 2）＋類別名 12 Black（顏色同 5.5 想吃膠囊）。
        /// </summary>
        void SetCategory(string category)
        {
            bool has = CategoryColors(category, out var bg, out var fg, out var dot);
            _category.gameObject.SetActive(has);
            if (!has) return;
            _categoryText.text = category;
            _categoryText.color = fg;
            _categoryBg.color = bg;
            _categoryDot.color = dot;
            float tw = Mathf.Ceil(_categoryText.GetPreferredValues(category).x);
            _categoryText.rectTransform.sizeDelta = new Vector2(tw + 2f, 22f);
            _category.sizeDelta = new Vector2(7f + 12f + 5f + tw + 9f, 22f);
            // 放在兩行字的右邊（取比較寬的那一行），和兩行字一起垂直置中
            float textW = Mathf.Max(_ownerLabel.GetPreferredValues(_ownerLabel.text).x, _ownerTime.GetPreferredValues(_ownerTime.text).x);
            float x = 48f + 24f + 7f + Mathf.Min(150f, Mathf.Ceil(textW)) + 8f;
            _category.anchoredPosition = new Vector2(x, -(10f + (36f - 22f) / 2f));
        }

        /// <summary>類別顏色（polaroid_spec.json category_tag.colors）。</summary>
        static bool CategoryColors(string category, out Color bg, out Color fg, out Color dot)
        {
            switch (category)
            {
                case "美食": bg = PUI.Hex(0xFFE3C7); fg = PUI.Hex(0xA4511B); dot = PUI.Hex(0xF29A3A); return true;
                case "自然": bg = PUI.Hex(0xDDEFD6); fg = PUI.Hex(0x3F6E33); dot = PUI.Hex(0x6FA45F); return true;
                case "城市": bg = PUI.Hex(0xDCE5FA); fg = PUI.Hex(0x3E5CA8); dot = PUI.Hex(0x6F8FE0); return true;
                case "生活人物": bg = PUI.Hex(0xF6E2F4); fg = PUI.Hex(0x8A3F84); dot = PUI.Hex(0xD78AD0); return true;
                case "寵物": bg = PUI.Hex(0xFBF0C4); fg = PUI.Hex(0x8A6A12); dot = PUI.Hex(0xE8C33A); return true;
            }
            bg = fg = dot = Ink;
            return false;
        }

        static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "剛剛";
            if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} 分鐘前";
            if (d.TotalDays < 1) return $"{(int)d.TotalHours} 小時前";
            if (t.Date == DateTime.Today.AddDays(-1)) return "昨天";
            return t.Year == DateTime.Today.Year ? $"{t.Month}/{t.Day}" : $"{t.Year}/{t.Month}/{t.Day}";
        }

        // ─── 留言（照片左下，最新 2 則）────────────────────────────────

        void BuildComments()
        {
            var m = Current;
            var layer = _card.commentLayer;
            for (int i = layer.childCount - 1; i >= 0; i--) Kill(layer.GetChild(i).gameObject);
            _risePill = null;
            if (m == null) return;
            var list = _captureComments != null ? _captureComments(m) : PhotoComments.Of(m);
            bool grew = _shownComments >= 0 && list.Count > _shownComments;
            _shownComments = list.Count;

            int start = Mathf.Max(0, list.Count - 2);
            float bottom = 10f; // 離照片下緣
            for (int i = list.Count - 1; i >= start; i--)
            {
                var e = list[i];
                bool mine = !string.IsNullOrEmpty(Me) && e.msg.senderId == Me;
                string name = mine ? "我" : ChatMessage.GetBestDisplayName(e.msg);
                if (string.IsNullOrEmpty(name)) name = "?";
                string text = name + "：" + (e.msg.message ?? "").Replace("\n", " ");

                var pill = PUI.Place(layer, "Comment", 10, 0, 100, 30);
                pill.anchorMin = pill.anchorMax = pill.pivot = new Vector2(0f, 0f);
                pill.anchoredPosition = new Vector2(10f, bottom);
                var bg = PUI.Fill(PUI.Stretch(pill, "Bg"), _skin?.pillComment, true, new Color(27f / 255f, 23f / 255f, 18f / 255f, 0.55f), 15);
                bg.raycastTarget = false;
                var av = PUI.Place(pill, "Avatar", 4, 4, 22, 22);
                UIShapes.AddRounded(av.gameObject, mine ? MeAvatar : ChatScreenController.PersonColor(name, false), 11).raycastTarget = false;
                PUI.Label(_font, av, 0, 0, 22, 22, ChatScreenController.Initial(name, mine), 10, Color.white, TextAlignmentOptions.Center);
                AvatarPhoto.Attach(av, e.msg.senderId);
                var label = PUI.Label(_font, pill, 32, 0, 260, 30, text, 13, Color.white, TextAlignmentOptions.Left);
                float tw = Mathf.Min(PolaroidRules.PhotoSize - 20f - 32f - 10f, Mathf.Ceil(label.GetPreferredValues(text).x));
                label.rectTransform.sizeDelta = new Vector2(tw, 30f);
                pill.sizeDelta = new Vector2(32f + tw + 10f, 30f);

                if (grew && i == list.Count - 1)
                {
                    _risePill = pill;
                    _riseGroup = pill.gameObject.AddComponent<CanvasGroup>();
                    _riseGroup.alpha = 0f;
                    _riseBaseY = pill.localPosition.y;
                    _riseT = 0f;
                }
                bottom += 30f + 6f;
            }
        }

        // ─── 輸入（留言／標題）──────────────────────────────────────────

        void Submit()
        {
            var m = Current;
            if (m == null) return;
            if (_mode == InputMode.Caption)
            {
                if (!IsOwner) { EndTyping(); return; }
                _style.caption = PolaroidRules.CleanCaption(_input.text);
                ApplyStyle();
                FirebaseManager.Instance?.SetPolaroidStyle(m.fileName, Copy(_style));
                EndTyping();
                return;
            }
            if (PhotoComments.Send(m, OwnerName(m), _input.text)) _input.text = "";
        }

        /// <summary>點自己照片的標題：留言框變成「寫一句標題…」並打開鍵盤。</summary>
        void BeginCaption()
        {
            if (!IsOwner) return; // 別人的照片點標題沒反應
            Deselect();
            EnterCaptionMode(true);
        }

        /// <summary>留言框變成寫標題（自己拍的照片才行）；focus＝要不要打開鍵盤。</summary>
        void EnterCaptionMode(bool focus)
        {
            if (_input == null || !IsOwner) return;
            _mode = InputMode.Caption;
            _placeholder.text = "寫一句標題…";
            _input.characterLimit = PolaroidRules.CaptionMax;
            if (!_input.isFocused) _input.text = _style.caption ?? "";
            if (focus) _input.ActivateInputField();
        }

        /// <summary>
        /// 裝飾頁＝寫標題模式（2026-11-08 使用者：按右下編輯鈕，留言框自動變成寫標題，不跳鍵盤）；寵物頁＝留言。
        /// 換頁、換張、Firebase 送來新標題時呼叫。
        /// </summary>
        void SyncCaptionMode()
        {
            if (_input == null) return;
            if (_page == Page.Deco && IsOwner) EnterCaptionMode(false);
            else if (_mode == InputMode.Caption) EndTyping();
        }

        bool _endingTyping;

        void EndTyping()
        {
            if (_input == null || _endingTyping) return;
            _endingTyping = true;
            if (_input.isFocused) _input.DeactivateInputField();
            if (_mode == InputMode.Caption)
            {
                _mode = InputMode.Comment;
                _input.characterLimit = 200;
                _input.text = "";
                _placeholder.text = PlaceholderFor(Current);
            }
            _endingTyping = false;
            if (_open && _page == Page.Deco && IsOwner) EnterCaptionMode(false); // 還在裝飾頁：回到寫標題模式
        }

        /// <summary>點拍立得空白處：收起鍵盤、取消選取貼紙。</summary>
        void OnCardTapped()
        {
            if (_input.isFocused) { EndTyping(); return; }
            Deselect();
        }

        // ─── 版面 ──────────────────────────────────────────────────────

        void Relayout()
        {
            float w = _root.rect.width, h = _root.rect.height;
            if (w <= 0f || h <= 0f) return;
            var safe = Screen.safeArea;
            float scale = _canvas.rootCanvas.scaleFactor;
            float px = scale > 0f ? 1f / scale : 1f;
            float kb = _input.isFocused && TouchScreenKeyboard.visible ? TouchScreenKeyboard.area.height * px : 0f;
            if (Mathf.Approximately(w, _lastW) && Mathf.Approximately(h, _lastH) && safe == _lastSafe && Mathf.Approximately(kb, _lastKb)) return;
            _lastW = w; _lastH = h; _lastSafe = safe; _lastKb = kb;

            float top = (Screen.height - safe.yMax) * px, bottom = safe.yMin * px; // 畫布單位
            // 設計框 390 × 772 放進安全區：寬、高都要放得下
            _k = Mathf.Min(w / DesignW, Mathf.Max(1f, h - top - bottom) / DesignH);
            _design.localScale = Vector3.one * _k;
            _design.anchoredPosition = new Vector2(0f, -top);

            // 打字時留言框移到鍵盤正上方；沒有鍵盤（Editor）就留在原位
            float y = InputY;
            if (kb > 0f) y = Mathf.Min(InputY, (h - top - kb) / _k - InputH - 8f);
            _inputBar.anchoredPosition = new Vector2(14f, -y);
        }

        // ─── 建立介面 ──────────────────────────────────────────────────

        void Build(bool capture = false)
        {
            _skin = PolaroidSkin.Load();
            if (_skin == null) Debug.LogWarning("[Polaroid] 找不到 Resources/UI/PolaroidSkin，先用程式畫的形狀（執行 BlockPet/Polaroid/Build Polaroid Skin）");
            _font = _skin != null ? _skin.font : null;

            if (!capture)
            {
                _canvas = gameObject.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 37; // 聊天室 33、相簿 34 之上
                var scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                gameObject.AddComponent<GraphicRaycaster>();
            }
            _group = gameObject.AddComponent<CanvasGroup>();
            _root = (RectTransform)transform;

            var bg = PUI.Stretch(_root, "Bg").gameObject.AddComponent<Image>();
            bg.color = Bg;
            PUI.MakeButton(bg.gameObject, bg, OnCardTapped); // 點空白處收起鍵盤

            _design = new GameObject("Design", typeof(RectTransform)).GetComponent<RectTransform>();
            _design.SetParent(_root, false);
            _design.anchorMin = _design.anchorMax = new Vector2(0.5f, 1f);
            _design.pivot = new Vector2(0.5f, 1f);
            _design.sizeDelta = new Vector2(DesignW, DesignH);

            BuildTop();
            BuildCard();
            BuildPanel();
            BuildInput();
            BuildToasts();
            gameObject.SetActive(false);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor 截圖用（BlockPet/Debug/拍立得截圖，不按 Play）：在 parent 裡建整個畫面，用給的照片、樣式、貼紙、留言。
        /// tab 0 貼紙／1 畫框／2 濾鏡；selectId＝要選取的自己的貼紙。
        /// </summary>
        public static GameObject BuildForCapture(RectTransform parent, PhotoMeta meta, Texture2D photo, PolaroidStyle style,
            List<PolaroidSticker> stickers, List<ChatEntry> comments, string me, int page, int tab, string selectId, float scale,
            float hearts, float[] energy, string want)
        {
            _captureMe = me;
            _captureHearts = hearts;
            _captureEnergy = energy;
            _captureWant = want;
            _captureComments = _ => comments ?? new List<ChatEntry>();
            var go = new GameObject("PolaroidCapture", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var s = go.AddComponent<PolaroidScreen>();
            s.Build(capture: true); // 截圖畫在截圖用的畫布上，不建自己的最上層畫布
            go.SetActive(true);
            s._design.localScale = Vector3.one * scale;
            s._design.anchoredPosition = Vector2.zero;
            s._photos.Add(meta);
            s._index = 0;
            s._tab = (Tab)tab;
            s.ShowPhoto(false);
            s.SetPage((Page)page, true);
            if (s._loader != null) { s.StopCoroutine(s._loader); s._loader = null; }
            s._style = Copy(style);
            s._stickers.AddRange(stickers);
            s.ApplyStyle();
            s.RebuildStickers();
            s._square = PolaroidPhoto.Square(photo, PolaroidPhoto.PhotoPx);
            s._thumbBase = PolaroidPhoto.Square(photo, PolaroidPhoto.ThumbPx);
            s.RefreshFiltered();
            foreach (var f in PolaroidRules.Filters) s._filterThumbs[f.key] = PolaroidPhoto.Filtered(s._thumbBase, f.key);
            s.BuildTray(true);
            if (!string.IsNullOrEmpty(selectId)) s.ShowSelection(selectId);
            s.AnimatePet(0.3f); // 截一個動作中的樣子
            _captureMe = null;
            _captureComments = null;
            _captureHearts = -1f;
            _captureEnergy = null;
            _captureWant = null;
            return go;
        }
#endif

        void BuildTop()
        {
            // 返回：(6, 6) 點擊範圍 44；‹ 線條 28，沒有底
            var back = PUI.Place(_design, "Back", 6, 6, 44, 44);
            var hit = PUI.Hit(back);
            var icon = PUI.Icon(back, _skin?.iconBack, 8, 8, 28, Color.white);
            if (icon.sprite == null) PUI.Label(_font, back, 0, 0, 44, 44, "‹", 30, Ink, TextAlignmentOptions.Center);
            Glow(icon);
            PUI.MakeButton(back.gameObject, hit, Close);

            // 誰拍的：(48, 10) 高 36、間距 7；頭像 24＋「小美拍的」13 Black／「3 分鐘前」11 Bold #8A7B68
            _ownerAvatar = PUI.Place(_design, "OwnerAvatar", 48, 16, 24, 24);
            _ownerAvatarBg = UIShapes.AddRounded(_ownerAvatar.gameObject, MeAvatar, 12);
            _ownerAvatarBg.raycastTarget = false;
            _ownerInitial = PUI.Label(_font, _ownerAvatar, 0, 0, 24, 24, "", 11, Color.white, TextAlignmentOptions.Center);
            _ownerPhoto = AvatarPhoto.Attach(_ownerAvatar, null);
            _ownerLabel = PUI.Label(_font, _design, 48f + 24f + 7f, 9, 150, 20, "", 13, Ink, TextAlignmentOptions.BottomLeft);
            _ownerLabel.overflowMode = TextOverflowModes.Overflow; // 一行；太長時 SetCategory 會限制寬度
            _ownerTime = PUI.Label(_font, _design, 48f + 24f + 7f, 29, 150, 18, "", 11, Note, TextAlignmentOptions.TopLeft);
            _ownerTime.overflowMode = TextOverflowModes.Overflow;

            // 辨識類別小膠囊（位置在 SetCategory 依名字寬度放）
            _category = PUI.Place(_design, "Category", 160, 17, 80, 22);
            _categoryBg = PUI.Fill(PUI.Stretch(_category, "Bg"), _skin?.tagCat, true, Color.white, 11);
            _categoryBg.raycastTarget = false;
            var dotRt = PUI.Place(_category, "Dot", 7, 5, 12, 12);
            var ring = UIShapes.AddRounded(dotRt.gameObject, Color.white, 6);
            ring.raycastTarget = false;
            _categoryDot = UIShapes.AddRounded(PUI.Place(dotRt, "Fill", 2, 2, 8, 8).gameObject, Color.white, 4);
            _categoryDot.raycastTarget = false;
            _categoryText = PUI.Label(_font, _category, 7f + 12f + 5f, 0, 60, 22, "", 12, Ink, TextAlignmentOptions.Left);

            // 右上：分享、下載（右 4、上 6；每顆點擊範圍 38 × 44；沒有底的線條圖示 23，白光）
            TopIcon("Download", DesignW - 4f - 38f, _skin?.iconDownload, "↓", () => Export(false));
            TopIcon("Share", DesignW - 4f - 38f - 38f, _skin?.iconShare, "↑", () => Export(true));
        }

        void TopIcon(string name, float x, Sprite sprite, string fallback, UnityEngine.Events.UnityAction onClick)
        {
            var b = PUI.Place(_design, name, x, 6, 38, 44);
            var hit = PUI.Hit(b);
            var icon = PUI.Icon(b, sprite, (38f - 23f) / 2f, (44f - 23f) / 2f, 23, Color.white);
            if (icon.sprite == null) PUI.Label(_font, b, 0, 0, 38, 44, fallback, 20, Ink, TextAlignmentOptions.Center);
            Glow(icon);
            PUI.MakeButton(b.gameObject, hit, onClick);
        }

        /// <summary>線條圖示的淡白光（同 5.25 Home 鍵）：圖示後面墊一層放大一點的白色半透明複本。</summary>
        static void Glow(Image icon)
        {
            if (icon == null || icon.sprite == null) return;
            var g = PUI.Stretch(icon.rectTransform, "Glow");
            g.offsetMin = new Vector2(-1.5f, -1.5f);
            g.offsetMax = new Vector2(1.5f, 1.5f);
            g.SetAsFirstSibling();
            var img = g.gameObject.AddComponent<Image>();
            img.sprite = PUI.White(icon.sprite);
            img.preserveAspect = true;
            img.color = new Color(1f, 252f / 255f, 244f / 255f, 0.75f);
            img.raycastTarget = false;
            // 子物件畫在父物件上面：把圖示本身的圖換到最上層
            var top = PUI.Stretch(icon.rectTransform, "Line");
            var line = top.gameObject.AddComponent<Image>();
            line.sprite = icon.sprite;
            line.preserveAspect = true;
            line.color = icon.color;
            line.raycastTarget = false;
            icon.color = new Color(0, 0, 0, 0);
        }

        void BuildCard()
        {
            _cardHolder = PUI.Place(_design, "CardHolder", CardX, CardY, PolaroidRules.CardW, PolaroidRules.CardH);
            _cardGroup = _cardHolder.gameObject.AddComponent<CanvasGroup>();
            _card = new PolaroidCard(_cardHolder, _skin, _font, true);

            // 整張卡片：左右滑換張（拖自己的貼紙時不換，見 StickerHandle）、點空白處取消選取／收鍵盤
            var hitImg = _card.root.gameObject.AddComponent<Image>();
            hitImg.color = new Color(0, 0, 0, 0);
            var swipe = _card.root.gameObject.AddComponent<HandSwipe>();
            swipe.space = _design;
            swipe.canSwipe = () => !_draggingSticker && !_input.isFocused;
            swipe.onEnd = dx => { if (dx < -40f) Go(1); else if (dx > 40f) Go(-1); };
            PUI.MakeButton(_card.root.gameObject, hitImg, OnCardTapped);

            // 標題：拍照的人點了改標題
            var capHit = PUI.Place(_card.root, "CaptionHit", 22, 352, 310, 70);
            var capImg = PUI.Hit(capHit);
            PUI.MakeButton(capHit.gameObject, capImg, BeginCaption);
            capHit.SetSiblingIndex(_card.stickerLayer.GetSiblingIndex()); // 在貼紙下面

            // 編輯鈕：卡片右下（右 8、下 8），點擊範圍 34；透明、只有鉛筆線條 20；裝飾頁時下方黃點 5
            var edit = PUI.Place(_card.root, "Edit", PolaroidRules.CardW - 8f - 34f, PolaroidRules.CardH - 8f - 34f, 34, 34);
            var editHit = PUI.Hit(edit);
            _editIcon = PUI.Icon(edit, PUI.White(_skin?.iconEdit), 7, 5, 20, Ink);
            if (_editIcon.sprite == null) { _editIcon.enabled = true; _editIcon.color = Ink; }
            _editDot = PUI.Icon(edit, _skin?.dotYellow, 14.5f, 27, 5, Color.white);
            if (_editDot.sprite == null) { PUI.Kill(_editDot); _editDot = UIShapes.AddRounded(PUI.Place(edit, "Dot", 14.5f, 27, 5, 5).gameObject, Yellow, 2.5f); _editDot.raycastTarget = false; }
            _editDot.enabled = false;
            PUI.MakeButton(edit.gameObject, editHit, () => SetPage(_page == Page.Pet ? Page.Deco : Page.Pet, false));

            // 下載完成時閃白
            _flash = PUI.Stretch(_card.root, "Flash").gameObject.AddComponent<Image>();
            _flash.raycastTarget = false;
            _flash.enabled = false;
        }

        void BuildInput()
        {
            // 左右 14、y 706、高 46：底 #FFFCF4、圓角 23、陰影；內距 0 5 0 14；送出 36 圓 #E86B4F＋白紙飛機
            float bw = DesignW - 28f;
            _inputBar = PUI.Place(_design, "InputBar", 14, InputY, bw, InputH);
            UIShapes.AddShadow(_inputBar, new Color(60f / 255f, 45f / 255f, 25f / 255f, 0.16f), 23, 10, 3);
            var barImg = PUI.Fill(PUI.Stretch(_inputBar, "Bg"), _skin?.inputBar, true, Cream, 23);

            float iw = bw - 14f - 5f - 36f - 8f;
            var field = PUI.Place(_inputBar, "Input", 14, 0, iw, InputH);
            field.gameObject.SetActive(false); // 先設定好 TMP_InputField 的欄位再啟用
            var fieldImg = PUI.Hit(field);
            var area = PUI.Place(field, "Text Area", 0, 0, iw, InputH);
            area.gameObject.AddComponent<RectMask2D>();
            _placeholder = PUI.Label(_font, area, 0, 0, iw, InputH, "留言…", 15, new Color(Note.r, Note.g, Note.b, 0.85f), TextAlignmentOptions.Left);
            var text = PUI.Label(_font, area, 0, 0, iw, InputH, "", 15, Ink, TextAlignmentOptions.Left);
            text.overflowMode = TextOverflowModes.Overflow;
            _input = field.gameObject.AddComponent<TMP_InputField>();
            _input.targetGraphic = fieldImg;
            _input.textViewport = area;
            _input.textComponent = text;
            _input.placeholder = _placeholder;
            _input.lineType = TMP_InputField.LineType.SingleLine;
            _input.characterLimit = 200;
            _input.shouldHideMobileInput = true; // 留言框自己會移到鍵盤上方
            _input.onSubmit.AddListener(_ => Submit());
            _input.onDeselect.AddListener(_ => { if (_mode == InputMode.Caption && !_input.isFocused) EndTyping(); });
            if (_font != null) _input.fontAsset = _font;
            field.gameObject.SetActive(true);

            var send = PUI.Place(_inputBar, "Send", bw - 5f - 36f, 5, 36, 36);
            var sendImg = PUI.Fill(send, _skin?.btnSend, false, Coral, 18);
            PUI.Icon(send, _skin?.iconSend, 10.5f, 10.5f, 15, Color.white);
            PUI.MakeButton(send.gameObject, sendImg, Submit);
            barImg.raycastTarget = true;
        }
    }
}
