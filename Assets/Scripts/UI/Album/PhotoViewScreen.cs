using System;
using System.Collections;
using System.Collections.Generic;
using BlockPet.Hand;
using BlockPet.UI.Chat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Album
{
    /// <summary>
    /// 相簿照片檢視（設計文件 5.14 ⑥「拍立得＋Discord 式留言」，設計稿 PhotoAltA；規格 docs/photoview/photo_shutter_spec.json）。
    /// 相簿點照片、分割畫面點大照片都打開這裡，從點的那張開始；左上關閉回到原本的畫面。
    /// - 拍立得：左右滑超過 40 換張（不循環），點左右兩側 90 寬也能換；換張 0.3 秒由下淡入；單數張、雙數張輕微反向傾斜。
    /// - 位置點、Discord 式留言列表（可捲動）、底部輸入列。
    /// 留言就是聊天室裡回覆這張照片的訊息（與分割畫面共用 PhotoSplitView.CommentsOf／SendComment），所以三個地方看到的是同一份。
    /// 執行時自己建一個最上層的 Screen Space Overlay，390 設計寬度等比縮放；素材從 Resources 的 PhotoViewSkin 讀。
    /// </summary>
    public class PhotoViewScreen : MonoBehaviour
    {
        const float DesignW = 390f;
        const float PolaroidW = 330f;
        const float PolaroidH = 400f;    // 內距 12＋照片 306＋12＋說明 28＋6＋誰拍的 22＋14
        const float PhotoSize = 306f;
        const float CommentsTop = 492f;
        const float InputH = 70f;        // 內距 8＋輸入框 42＋20
        const float TextW = 358f - 36f - 10f;
        const int PhotoTexW = 920;
        const int MaxDots = 9;

        static readonly Color Bg = Hex(0xF7F0E2);
        static readonly Color Ink = Hex(0x3A2E22);
        static readonly Color Byline = Hex(0x8A6A4E);
        static readonly Color TimeGray = Hex(0xA8977F);
        static readonly Color Cream = Hex(0xFFFCF4);
        static readonly Color MeAvatar = Hex(0xD9A92C);
        static readonly Color MeName = Hex(0xB07F12);

        // 成員顏色（同聊天室 PersonColor 的色盤）→ 名字用的深色版
        static readonly Dictionary<int, Color> DarkOf = new Dictionary<int, Color>
        {
            { 0xE88AA8, Hex(0xC2577A) }, { 0x6F8FE0, Hex(0x4F6FC4) }, { 0x6FA45F, Hex(0x4F8F45) },
            { 0xD78AD0, Hex(0xA85BA0) }, { 0xF29A3A, Hex(0xC2621A) }, { 0x8FA868, Hex(0x5F7A3E) },
        };

        static PhotoViewScreen _instance;

        PhotoViewSkin _skin;
        TMP_FontAsset _font;
        Texture _background;

        readonly List<PhotoMeta> _photos = new List<PhotoMeta>();
        int _index;
        bool _open;
        float _openT, _enterT = 1f;
        bool _commentsDirty;
        int _shownComments = -1;

        Canvas _canvas;
        CanvasGroup _group;
        RectTransform _root, _design, _polaroid, _dots, _content, _inputBar;
        CanvasGroup _polaroidGroup;
        ScrollRect _scroll;
        RawImage _bgPhoto, _photo;
        RenderTexture _blurred;
        TMP_Text _caption, _byline, _bylineInitial, _empty;
        Image _bylineAvatar, _send;
        Button _sendBtn;
        TMP_InputField _input;
        float _lastW = -1f, _lastH = -1f, _k = 1f;
        Rect _lastSafe;

        readonly Dictionary<string, Texture> _textures = new Dictionary<string, Texture>();
        Coroutine _loader;

        // ─── 開關 ──────────────────────────────────────────────────────

        /// <summary>打開照片檢視，從 photos[index] 開始（photos 依畫面上的順序）。</summary>
        public static void Open(IList<PhotoMeta> photos, int index, TMP_FontAsset font, Texture background)
        {
            if (photos == null || photos.Count == 0) return;
            if (_instance == null)
            {
                var go = new GameObject("PhotoViewScreen (runtime)", typeof(RectTransform));
                _instance = go.AddComponent<PhotoViewScreen>();
                _instance._font = font;
                _instance.Build();
            }
            _instance.Show(photos, index, background);
        }

        void Show(IList<PhotoMeta> photos, int index, Texture background)
        {
            _photos.Clear();
            foreach (var p in photos) if (p != null && !string.IsNullOrEmpty(p.fileName)) _photos.Add(p);
            if (_photos.Count == 0) return;
            _index = Mathf.Clamp(index, 0, _photos.Count - 1);
            if (background != null && background != _background) { _background = background; MakeBackground(); }
            gameObject.SetActive(true);
            if (!_open)
            {
                FirebaseManager.OnChatEntryAdded += OnChat;
                FirebaseManager.OnChatEntryChanged += OnChat;
                FirebaseManager.OnChatEntryRemoved += OnChatRemoved;
                FirebaseManager.OnChatEntriesReset += OnChatReset;
            }
            _open = true;
            _input.text = "";
            ShowPhoto(false);
        }

        void Close()
        {
            if (!_open) return;
            _open = false;
            FirebaseManager.OnChatEntryAdded -= OnChat;
            FirebaseManager.OnChatEntryChanged -= OnChat;
            FirebaseManager.OnChatEntryRemoved -= OnChatRemoved;
            FirebaseManager.OnChatEntriesReset -= OnChatReset;
        }

        void OnChat(ChatEntry e) => _commentsDirty = true;
        void OnChatRemoved(string key) => _commentsDirty = true;
        void OnChatReset() => _commentsDirty = true;

        void OnDestroy()
        {
            Close();
            ClearTextures(null);
            if (_blurred != null) { _blurred.Release(); Destroy(_blurred); }
            if (_instance == this) _instance = null;
        }

        void Update()
        {
            Relayout();
            float dt = Time.unscaledDeltaTime;
            _openT = Mathf.MoveTowards(_openT, _open ? 1f : 0f, dt / 0.2f);
            _group.alpha = _openT;
            _group.blocksRaycasts = _open;
            if (!_open && _openT <= 0f)
            {
                ClearTextures(null);
                gameObject.SetActive(false);
                return;
            }

            // 換張：0.3 秒由下 8 淡入
            if (_enterT < 1f)
            {
                _enterT = Mathf.MoveTowards(_enterT, 1f, dt / 0.3f);
                float e = 1f - (1f - _enterT) * (1f - _enterT);
                _polaroid.anchoredPosition = new Vector2(30f + PolaroidW / 2f, -(60f + PolaroidH / 2f) - 8f * (1f - e));
                _polaroidGroup.alpha = e;
            }

            if (_commentsDirty) { _commentsDirty = false; BuildComments(); }

            bool hasText = !string.IsNullOrWhiteSpace(_input.text);
            if (_sendBtn.interactable != hasText || _send.sprite == null)
            {
                _sendBtn.interactable = hasText;
                var sp = hasText ? _skin?.sendOn : _skin?.sendOff;
                _send.sprite = sp;
                _send.color = sp != null ? Color.white : (hasText ? Hex(0xF6C945) : Hex(0xE2D6C0));
                // 有字的送出鈕含下緣立體 3（42 × 45）
                _send.rectTransform.sizeDelta = new Vector2(42f, hasText && sp != null ? 45f : 42f);
            }
        }

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
            var m = _photos[_index];
            bool mine = IsMinePhoto(m);
            string who = mine ? "我" : (string.IsNullOrEmpty(m.takerName) ? "營地" : m.takerName);

            // 單數張、雙數張反向傾斜（設計稿 odd 1.5deg 順時針、even −1.5deg）
            _polaroid.localRotation = Quaternion.Euler(0f, 0f, (_index % 2 == 0) ? -1.5f : 1.5f);
            _enterT = animate ? 0f : 1f;
            _polaroidGroup.alpha = animate ? 0f : 1f;
            _polaroid.anchoredPosition = new Vector2(30f + PolaroidW / 2f, -(60f + PolaroidH / 2f) - (animate ? 8f : 0f));

            _caption.text = !string.IsNullOrEmpty(m.caption) ? m.caption : (mine ? "我餵的照片" : who + " 餵的照片");
            DateTime.TryParse(m.timestamp, out var t);
            _byline.text = t == default ? who : who + "・" + FormatTime(t);
            _bylineAvatar.color = mine ? MeAvatar : ChatScreenController.PersonColor(who, false);
            _bylineInitial.text = ChatScreenController.Initial(who, mine);

            _photo.texture = _textures.TryGetValue(m.fileName, out var tex) ? tex : null;
            _photo.enabled = _photo.texture != null;
            FitPhoto();

            BuildDots();
            _shownComments = -1;
            BuildComments();

            if (_loader != null) StopCoroutine(_loader);
            _loader = StartCoroutine(LoadTextures());
        }

        void FitPhoto()
        {
            var tex = _photo.texture;
            if (tex != null) _photo.uvRect = ChatScreenController.AspectFill(tex.width, tex.height, PhotoSize, PhotoSize);
        }

        /// <summary>先讀目前這張，再預讀前後各一張；其他的釋放掉（照片很多時不佔記憶體）。</summary>
        IEnumerator LoadTextures()
        {
            var keep = new HashSet<string>();
            for (int d = 0; d <= 1; d++)
                foreach (int i in new[] { _index + d, _index - d })
                    if (i >= 0 && i < _photos.Count) keep.Add(_photos[i].fileName);
            ClearTextures(keep);

            foreach (int i in new[] { _index, _index + 1, _index - 1 })
            {
                if (i < 0 || i >= _photos.Count) continue;
                var m = _photos[i];
                if (_textures.ContainsKey(m.fileName)) continue;
                var full = SaveManager.Instance != null ? SaveManager.Instance.LoadPhoto(m) : null;
                if (full != null)
                {
                    _textures[m.fileName] = ChatScreenController.Downscale(full, PhotoTexW);
                    Destroy(full);
                }
                if (this == null) yield break;
                if (i == _index && _textures.TryGetValue(m.fileName, out var tex))
                {
                    _photo.texture = tex;
                    _photo.enabled = true;
                    FitPhoto();
                }
                yield return null;
            }
            _loader = null;
        }

        void ClearTextures(HashSet<string> keep)
        {
            var drop = new List<string>();
            foreach (var kv in _textures) if (keep == null || !keep.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (var k in drop)
            {
                if (_textures[k] != null) Destroy(_textures[k]);
                _textures.Remove(k);
            }
        }

        /// <summary>位置點：每張一個 6 × 6，目前這張 18 × 6；照片很多時只顯示目前這張附近 9 個。</summary>
        void BuildDots()
        {
            for (int i = _dots.childCount - 1; i >= 0; i--) Destroy(_dots.GetChild(i).gameObject);
            int n = _photos.Count;
            if (n <= 1) return;
            int count = Mathf.Min(n, MaxDots);
            int start = Mathf.Clamp(_index - MaxDots / 2, 0, n - count);
            float total = (count - 1) * (6f + 6f) + 18f;
            float x = (DesignW - total) / 2f;
            for (int i = start; i < start + count; i++)
            {
                bool on = i == _index;
                float w = on ? 18f : 6f;
                var img = Place(_dots, on ? "DotOn" : "Dot", x, 0, w, 6).gameObject.AddComponent<Image>();
                img.raycastTarget = false;
                var sp = on ? _skin?.dotOn : _skin?.dot;
                if (sp != null)
                {
                    img.sprite = sp;
                    img.type = on ? Image.Type.Sliced : Image.Type.Simple;
                }
                else img.color = on ? Ink : new Color(Ink.r, Ink.g, Ink.b, 0.25f);
                x += w + 6f;
            }
        }

        // ─── 留言 ──────────────────────────────────────────────────────

        void BuildComments()
        {
            var m = _photos[_index];
            var list = PhotoSplitView.CommentsOf(m);
            bool grew = _shownComments >= 0 && list.Count > _shownComments;
            bool first = _shownComments < 0;
            _shownComments = list.Count;

            for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
            _empty.gameObject.SetActive(list.Count == 0);

            string me = FirebaseManager.Instance != null ? FirebaseManager.Instance.GetUserId() : null;
            float y = 4f;
            foreach (var e in list)
            {
                bool mine = !string.IsNullOrEmpty(me) && e.msg.senderId == me;
                string name = mine ? "我" : ChatMessage.GetBestDisplayName(e.msg);
                if (string.IsNullOrEmpty(name)) name = "?";
                Color light = mine ? MeAvatar : ChatScreenController.PersonColor(name, false);
                Color dark = mine ? MeName : DarkVersion(light);

                // 大頭貼 36（成員顏色＋白色首字）
                var av = Place(_content, "Avatar", 0, y, 36, 36);
                var avImg = av.gameObject.AddComponent<Image>();
                avImg.sprite = _skin?.avatar;
                avImg.color = light;
                avImg.raycastTarget = false;
                if (avImg.sprite == null) { Destroy(avImg); UIShapes.AddRounded(av.gameObject, light, 18).raycastTarget = false; }
                Label(av, 0, 0, 36, 36, ChatScreenController.Initial(name, mine), 14, Color.white, TextAlignmentOptions.Center);

                // 第一行：名字（成員深色）＋間距 8＋日期時間
                var nameLabel = Label(_content, 46, y, TextW, 20, name, 14, dark, TextAlignmentOptions.Left);
                nameLabel.enableWordWrapping = false;
                float nw = Mathf.Min(nameLabel.GetPreferredValues(name).x, TextW - 90f);
                nameLabel.rectTransform.sizeDelta = new Vector2(nw, 20f);
                nameLabel.overflowMode = TextOverflowModes.Ellipsis;
                var time = Label(_content, 46 + nw + 8f, y + 2f, 120, 18, FormatTime(TimeOf(e.msg)), 11, TimeGray, TextAlignmentOptions.Left);
                time.enableWordWrapping = false;

                // 第二行：留言內容，可換行
                var body = Label(_content, 46, y + 22f, TextW, 20, e.msg.message ?? "", 14, Ink, TextAlignmentOptions.TopLeft);
                body.lineSpacing = 18f; // 行高約 1.45
                float bh = Mathf.Ceil(body.GetPreferredValues(body.text, TextW, 0f).y);
                body.rectTransform.sizeDelta = new Vector2(TextW, bh);

                y += Mathf.Max(36f, 22f + bh) + 14f;
            }
            _content.sizeDelta = new Vector2(_content.sizeDelta.x, y);

            // 剛打開從最上面看；有新留言（例如自己送出）捲到最下面
            if (grew) StartCoroutine(ScrollToBottom());
            else if (first) _scroll.verticalNormalizedPosition = 1f;
        }

        IEnumerator ScrollToBottom()
        {
            yield return null;
            if (_scroll != null) _scroll.verticalNormalizedPosition = 0f;
        }

        void Send()
        {
            var m = _photos[_index];
            bool mine = IsMinePhoto(m);
            string who = mine ? "我" : (string.IsNullOrEmpty(m.takerName) ? "營地" : m.takerName);
            if (PhotoSplitView.SendComment(m, who, _input.text)) _input.text = "";
        }

        static bool IsMinePhoto(PhotoMeta m)
        {
            string me = FirebaseManager.Instance != null ? FirebaseManager.Instance.GetUserId() : null;
            return !string.IsNullOrEmpty(me) && m.takerUserId == me;
        }

        static Color DarkVersion(Color c)
        {
            int rgb = (Mathf.RoundToInt(c.r * 255f) << 16) | (Mathf.RoundToInt(c.g * 255f) << 8) | Mathf.RoundToInt(c.b * 255f);
            return DarkOf.TryGetValue(rgb, out var d) ? d : new Color(c.r * 0.75f, c.g * 0.75f, c.b * 0.75f, 1f);
        }

        /// <summary>聊天訊息的時間（timestamp 是秒；舊資料可能是毫秒）。</summary>
        static DateTime TimeOf(ChatMessage msg)
        {
            long ts = msg.timestamp;
            if (ts <= 0) return DateTime.Now;
            var dto = ts > 100000000000L ? DateTimeOffset.FromUnixTimeMilliseconds(ts) : DateTimeOffset.FromUnixTimeSeconds(ts);
            return dto.LocalDateTime;
        }

        /// <summary>今天 10:15／昨天 18:00／10/20 18:30／跨年 2025/12/3 18:30（24 小時制）。</summary>
        public static string FormatTime(DateTime t)
        {
            var today = DateTime.Today;
            string hm = t.ToString("HH:mm");
            if (t.Date == today) return "今天 " + hm;
            if (t.Date == today.AddDays(-1)) return "昨天 " + hm;
            if (t.Year == today.Year) return $"{t.Month}/{t.Day} {hm}";
            return $"{t.Year}/{t.Month}/{t.Day} {hm}";
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
            float topInset = (Screen.height - safe.yMax) * px / _k;      // 設計單位
            float bottomInset = safe.yMin * px / _k;
            float screenH = h / _k;
            _design.localScale = Vector3.one * _k;
            _design.sizeDelta = new Vector2(DesignW, screenH - topInset);
            _design.anchoredPosition = new Vector2(0f, -topInset * _k); // 位移是畫布單位

            // 輸入列貼底（含底部安全區），留言列表從 y 492 到輸入列上方
            float barH = InputH + bottomInset;
            _inputBar.sizeDelta = new Vector2(DesignW, barH);
            var view = (RectTransform)_scroll.transform;
            view.sizeDelta = new Vector2(358f, Mathf.Max(60f, screenH - topInset - CommentsTop - barH));
        }

        void MakeBackground()
        {
            if (_background == null) { _bgPhoto.enabled = false; return; }
            // 營地底圖縮小再放大＝模糊（約 blur 14），透明度 16%
            int w = 40, h = Mathf.Max(1, Mathf.RoundToInt(40f * _background.height / Mathf.Max(1, _background.width)));
            var mid = RenderTexture.GetTemporary(160, Mathf.Max(1, h * 4), 0);
            mid.filterMode = FilterMode.Bilinear;
            Graphics.Blit(_background, mid);
            if (_blurred == null || _blurred.width != w || _blurred.height != h)
            {
                if (_blurred != null) { _blurred.Release(); Destroy(_blurred); }
                _blurred = new RenderTexture(w, h, 0) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            }
            Graphics.Blit(mid, _blurred);
            RenderTexture.ReleaseTemporary(mid);
            _bgPhoto.texture = _blurred;
            _bgPhoto.enabled = true;
            _bgPhoto.GetComponent<AspectRatioFitter>().aspectRatio = (float)_background.width / Mathf.Max(1, _background.height);
        }

        // ─── 建立介面 ──────────────────────────────────────────────────

        void Build()
        {
            _skin = PhotoViewSkin.Load();
            if (_skin == null) Debug.LogWarning("[PhotoViewScreen] 找不到 Resources/UI/PhotoViewSkin，先用程式畫的形狀（執行 BlockPet/Album/Build Photo View Skin）");

            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 37; // 相簿 34、分割畫面 36 之上
            gameObject.AddComponent<GraphicRaycaster>();
            _group = gameObject.AddComponent<CanvasGroup>();
            _root = (RectTransform)transform;

            // 背景：#F7F0E2＋營地底圖（模糊、16%）
            Stretch(_root, "Base").gameObject.AddComponent<Image>().color = Bg;
            _bgPhoto = Stretch(_root, "Camp").gameObject.AddComponent<RawImage>();
            _bgPhoto.raycastTarget = false;
            _bgPhoto.color = new Color(1f, 1f, 1f, 0.16f);
            _bgPhoto.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            _bgPhoto.enabled = false;

            _design = new GameObject("Design", typeof(RectTransform)).GetComponent<RectTransform>();
            _design.SetParent(_root, false);
            _design.anchorMin = _design.anchorMax = new Vector2(0.5f, 1f);
            _design.pivot = new Vector2(0.5f, 1f);
            _design.sizeDelta = new Vector2(DesignW, 772f);

            BuildPolaroid();

            // 左上關閉 (12, 12) 44 × 44
            var close = Place(_design, "Close", 12, 12, 44, 44);
            var closeImg = close.gameObject.AddComponent<Image>();
            closeImg.sprite = _skin?.close;
            if (closeImg.sprite == null)
            {
                Destroy(closeImg);
                closeImg = UIShapes.AddRounded(close.gameObject, Hex(0xF2E9D8), 22);
                Label(close, 0, 0, 44, 44, "×", 20, Ink, TextAlignmentOptions.Center);
            }
            var cb = close.gameObject.AddComponent<Button>();
            cb.targetGraphic = closeImg;
            cb.onClick.AddListener(Close);

            // 位置點 y 470
            _dots = Place(_design, "Dots", 0, 470, DesignW, 6);

            // 留言列表（可捲動）
            var view = Place(_design, "Comments", 16, CommentsTop, 358, 200);
            view.gameObject.AddComponent<RectMask2D>();
            var hit = view.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0); // 讓空白處也能拖曳捲動
            _content = Place(view, "Content", 0, 0, 358, 0);
            _scroll = view.gameObject.AddComponent<ScrollRect>();
            _scroll.content = _content;
            _scroll.viewport = view;
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 20f;
            _empty = Label(view, 0, 16, 358, 24, "還沒有留言，說點什麼吧", 13, Byline, TextAlignmentOptions.Center);

            BuildInputBar();
            gameObject.SetActive(false);
        }

        void BuildPolaroid()
        {
            // (30, 60) 寬 330；以中心旋轉
            _polaroid = Place(_design, "Polaroid", 0, 0, PolaroidW, PolaroidH);
            _polaroid.pivot = new Vector2(0.5f, 0.5f);
            _polaroid.anchoredPosition = new Vector2(30f + PolaroidW / 2f, -(60f + PolaroidH / 2f));
            _polaroidGroup = _polaroid.gameObject.AddComponent<CanvasGroup>();

            UIShapes.AddShadow(_polaroid, new Color(30f / 255f, 30f / 255f, 20f / 255f, 0.22f), 6, 24, 10);
            var frame = Stretch(_polaroid, "Frame");
            var frameImg = frame.gameObject.AddComponent<Image>();
            if (_skin != null && _skin.polaroidFrame != null) { frameImg.sprite = _skin.polaroidFrame; frameImg.type = Image.Type.Sliced; }
            else { Destroy(frameImg); frameImg = UIShapes.AddRounded(frame.gameObject, Cream, 6); }

            // 照片 306 × 306，圓角 3 遮罩
            var holder = Place(_polaroid, "Photo", 12, 12, PhotoSize, PhotoSize);
            var maskImg = holder.gameObject.AddComponent<Image>();
            if (_skin != null && _skin.photoMask != null) { maskImg.sprite = _skin.photoMask; maskImg.type = Image.Type.Sliced; }
            maskImg.color = Hex(0xF2E9D8);
            holder.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            _photo = Place(holder, "Img", 0, 0, PhotoSize, PhotoSize).gameObject.AddComponent<RawImage>();
            _photo.raycastTarget = false;

            // 說明（照片下 12）與誰拍的（說明下 6）
            _caption = Label(_polaroid, 12, 12f + PhotoSize + 12f, PhotoSize, 28, "", 21, Ink, TextAlignmentOptions.Left);
            _caption.enableWordWrapping = false;
            _caption.overflowMode = TextOverflowModes.Ellipsis;
            float by = 12f + PhotoSize + 12f + 28f + 6f;
            var av = Place(_polaroid, "Avatar", 12, by, 22, 22);
            _bylineAvatar = av.gameObject.AddComponent<Image>();
            _bylineAvatar.sprite = _skin?.avatar;
            _bylineAvatar.raycastTarget = false;
            _bylineInitial = Label(av, 0, 0, 22, 22, "", 10, Color.white, TextAlignmentOptions.Center);
            _byline = Label(_polaroid, 40, by, PhotoSize - 28f, 22, "", 12, Byline, TextAlignmentOptions.Left);
            _byline.enableWordWrapping = false;

            // 膠帶：上緣中間 (125, −12) 80 × 24，轉 −3°（逆時針）
            var tape = Place(_polaroid, "Tape", 125, -12, 80, 24);
            tape.pivot = new Vector2(0.5f, 0.5f);
            tape.anchoredPosition = new Vector2(125f + 40f, 12f - 12f);
            tape.localRotation = Quaternion.Euler(0f, 0f, 3f);
            var tapeImg = tape.gameObject.AddComponent<Image>();
            tapeImg.raycastTarget = false;
            if (_skin != null && _skin.tape != null) tapeImg.sprite = _skin.tape;
            else tapeImg.color = new Color(246f / 255f, 201f / 255f, 69f / 255f, 0.55f);

            // 左右滑超過 40 換張；點左右兩側 90 寬也能換
            var swipe = _polaroid.gameObject.AddComponent<HandSwipe>();
            swipe.space = _design;
            swipe.onEnd = dx => { if (dx < -40f) Go(1); else if (dx > 40f) Go(-1); };
            TapZone(0f, -1);
            TapZone(PolaroidW - 90f, 1);
        }

        void TapZone(float x, int delta)
        {
            var z = Place(_polaroid, delta < 0 ? "PrevZone" : "NextZone", x, 0, 90, PolaroidH);
            var img = z.gameObject.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0);
            var b = z.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => Go(delta));
        }

        void BuildInputBar()
        {
            // 貼底：內距 8 12 20 12、間距 8；輸入框 42 高、送出 42
            _inputBar = Place(_design, "InputBar", 0, 0, DesignW, InputH);
            _inputBar.anchorMin = _inputBar.anchorMax = new Vector2(0f, 0f);
            _inputBar.pivot = new Vector2(0f, 0f);
            _inputBar.anchoredPosition = Vector2.zero;
            var barImg = _inputBar.gameObject.AddComponent<Image>();
            if (_skin != null && _skin.inputBar != null) { barImg.sprite = _skin.inputBar; barImg.type = Image.Type.Sliced; }
            else barImg.color = new Color(1f, 248f / 255f, 236f / 255f, 0.96f);

            float iw = DesignW - 12f - 12f - 8f - 42f;
            var field = Place(_inputBar, "Input", 12, 8, iw, 42);
            field.gameObject.SetActive(false); // 先設定好 TMP_InputField 的欄位再啟用
            var fieldImg = field.gameObject.AddComponent<Image>();
            if (_skin != null && _skin.chatInput != null) { fieldImg.sprite = _skin.chatInput; fieldImg.type = Image.Type.Sliced; }
            else { Destroy(fieldImg); fieldImg = UIShapes.AddRounded(field.gameObject, Color.white, 21); }
            var area = Place(field, "Text Area", 16, 0, iw - 32f, 42);
            area.gameObject.AddComponent<RectMask2D>();
            var ph = Label(area, 0, 0, iw - 32f, 42, "留言…", 15, new Color(Byline.r, Byline.g, Byline.b, 0.6f), TextAlignmentOptions.Left);
            var text = Label(area, 0, 0, iw - 32f, 42, "", 15, Ink, TextAlignmentOptions.Left);
            ph.enableWordWrapping = text.enableWordWrapping = false;
            _input = field.gameObject.AddComponent<TMP_InputField>();
            _input.targetGraphic = fieldImg;
            _input.textViewport = area;
            _input.textComponent = text;
            _input.placeholder = ph;
            _input.lineType = TMP_InputField.LineType.SingleLine;
            _input.characterLimit = 200;
            _input.onSubmit.AddListener(_ => Send());
            if (_font != null) _input.fontAsset = _font;
            field.gameObject.SetActive(true);

            var send = Place(_inputBar, "Send", 12f + iw + 8f, 8, 42, 42);
            _send = send.gameObject.AddComponent<Image>();
            _send.preserveAspect = true;
            _sendBtn = send.gameObject.AddComponent<Button>();
            _sendBtn.targetGraphic = _send;
            _sendBtn.transition = Selectable.Transition.None;
            _sendBtn.onClick.AddListener(Send);
            _sendBtn.interactable = false;
            _send.sprite = null; // 第一次 Update 套用 on／off
        }

        // ─── UI helpers（與其他新版頁面相同的設計座標寫法）────────────

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
            t.enableWordWrapping = true;
            return t;
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
    }
}
