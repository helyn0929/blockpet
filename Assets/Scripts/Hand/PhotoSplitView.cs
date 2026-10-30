using System;
using System.Collections;
using System.Collections.Generic;
using BlockPet.Feed;
using BlockPet.UI.Chat;
using BlockPet.UI.Room;
using BlockPet.UI.TopBar;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.Hand
{
    /// <summary>
    /// 分割畫面（設計稿首頁 v8「2 分割畫面：大照片＋留言＋縮小的營地」Camp8Photo，頂部用 A 版）：
    /// 點手牌最右邊的營地最新照片打開（從最新一張開始）；聊天室點餵食貼文的照片也會打開（從那一張開始，蓋在聊天室上面，返回就回聊天室）。
    /// - 頂部玻璃列：返回、照片縮圖疊（目前這張黃框，最多 4 張＋「+N」）、「誰 的照片／多久前」、「新 N」（還沒看的新照片）、
    ///   最右邊聊天室入口（有未讀時右上角紅點數字）。
    /// - 中間大照片（346×346）：左右滑或兩側箭頭換張；下方疊最近 2 則留言與說明；底下留言框。
    /// - 下方縮小的營地：寵物（愛心、進化條）看著照片說話；右側中間半透明快門圈圈（設計稿 Camp8CamG 定案 G），
    ///   點了大照片變成相機畫面（右上角翻轉鏡頭），再按一次快門拍下直接餵寵物，新照片排在最前面，左下角跳出結果（Camp8CamShot）；
    ///   相機畫面時左上返回是關掉相機。拍照冷卻中圈圈變暗、中間顯示倒數。
    /// 留言就是聊天室裡回覆這張照片的訊息（照片的餵食貼文；舊照片沒有貼文時用 photo:{檔名} 當回覆對象），所以聊天室也看得到。
    /// 由「BlockPet/Build Split View」掛到手牌物件上並接好素材；沒有掛時，手牌的最新照片卡改開相簿。
    /// </summary>
    public class PhotoSplitView : MonoBehaviour
    {
        const float DesignW = 390f;
        const float PhotoSize = 346f;
        const int PhotoTexW = 1040;
        const int ThumbTexW = 120;

        static readonly Color Ink = Hex(0x3A2E22);
        static readonly Color Card = Hex(0xFFFCF4);
        static readonly Color Paper = Hex(0xFFF8EC);
        static readonly Color Beige = Hex(0xF2E9D8);
        static readonly Color Muted = Hex(0x6B5D4E);
        static readonly Color Yellow = Hex(0xF6C945);
        static readonly Color Coral = Hex(0xE86B4F);
        static readonly Color CoralEdge = Hex(0xB44A33);
        static readonly Color Dark = new Color(27f / 255f, 23f / 255f, 18f / 255f, 1f);

        [Header("Style")]
        [SerializeField] HandSkin skin;
        [Tooltip("營地圖（背景與下方縮小的營地）")]
        [SerializeField] Texture background;
        [SerializeField] Sprite[] petSprites;
        [Tooltip("愛心圖、進化條顏色沿用頂部狀態欄")]
        [SerializeField] TopBarConfig config;
        [SerializeField] int sortingOrder = 36;

        class Shot
        {
            public PhotoMeta meta;
            public DateTime time;
            public string who;
            public bool mine;
            public bool isNew;
        }

        readonly List<Shot> _shots = new List<Shot>();
        int _index;
        readonly HashSet<string> _viewed = new HashSet<string>();
        DateTime _seenBefore;
        bool _open;
        float _openT;
        bool _dirty;

        // 版面
        Canvas _canvas;
        RectTransform _root, _design, _camp;
        CanvasGroup _group;
        RenderTexture _blurred;
        float _lastW = -1f, _lastH = -1f, _k = 1f, _screenH = 772f, _topInset, _bottomInset;
        Rect _lastSafe;

        // 頂部
        RectTransform _stack;
        TMP_Text _title, _ago;
        RectTransform _newBadge;
        RectTransform _chatBadge;
        TMP_Text _chatBadgeText;
        int _shownUnread = -1;
        TMP_Text _newText;

        // 大照片
        RawImage _photo;
        RectTransform _newTag, _overlay;
        RectTransform _card, _cardFill, _frame, _commentRow, _prevBtn, _nextBtn;
        float _photoH = PhotoSize;   // 大照片的高度：螢幕比設計稿長時，多出來的高度給照片（營地維持 266）
        TMP_InputField _input;
        Image _sendBg;

        // 營地
        RectTransform _pet;
        Image _petImage;
        readonly Image[] _hearts = new Image[5];
        RectTransform _evoFill;
        Image _evoSeg;
        TMP_Text _energy, _petSay;
        RectTransform _sayBubble;

        // 營地右側的快門（拍照餵寵物）
        RectTransform _shutter;
        CanvasGroup _shutterGroup;
        TMP_Text _shutterText;
        RectTransform _camToast;
        TMP_Text _camToastTitle, _camToastSub;
        float _camToastUntil;
        Image _shutterCore;

        // 相機模式：大照片變成相機畫面
        bool _camMode;
        bool _camFront;
        WebCamTexture _cam;
        RawImage _camView;
        RectTransform _flipBtn;
        HandSwipe _swipe;

        // 圖
        readonly Dictionary<string, Texture> _big = new Dictionary<string, Texture>();
        readonly Dictionary<string, Texture> _thumbs = new Dictionary<string, Texture>();
        Coroutine _loader;

        public bool IsOpen => _open;

        // ─── 開關 ──────────────────────────────────────────────────────

        /// <summary>打開分割畫面，從最新一張照片開始。沒有照片回傳 false。</summary>
        public bool Open() => Open(null);

        /// <summary>打開分割畫面並顯示指定的照片（null＝最新一張）。沒有照片、或指定的照片還沒同步到相簿時回傳 false。</summary>
        public bool Open(string fileName)
        {
            LoadShots();
            int index = string.IsNullOrEmpty(fileName) ? 0 : _shots.FindIndex(s => s.meta.fileName == fileName);
            if (_shots.Count == 0 || index < 0) return false;
            if (_canvas == null) Build();
            _index = index;
            _viewed.Clear();
            _open = true;
            _openT = 0f;
            _canvas.gameObject.SetActive(true);
            MakeBackground();
            FirebaseManager.OnChatEntryAdded += OnChat;
            FirebaseManager.OnChatEntryChanged += OnChat;
            FirebaseManager.OnChatEntryRemoved += OnChatRemoved;
            SaveManager.OnSaveDataChanged += OnSaveChanged;
            _input.text = "";
            Show(_index);
            return true;
        }

        public void Close()
        {
            if (!_open) return;
            ExitCamera();
            _open = false;
            FirebaseManager.OnChatEntryAdded -= OnChat;
            FirebaseManager.OnChatEntryChanged -= OnChat;
            FirebaseManager.OnChatEntryRemoved -= OnChatRemoved;
            SaveManager.OnSaveDataChanged -= OnSaveChanged;
            // 看過的照片不再算新（與相簿共用同一個記錄）
            var fb = FirebaseManager.Instance;
            if (fb != null && !string.IsNullOrEmpty(fb.RoomId)) PlayerPrefs.SetString("BlockPet.AlbumSeen." + fb.RoomId, DateTime.Now.ToString("o"));
        }

        void OnDisable() => Close();

        /// <summary>右上聊天室：關掉分割畫面、打開聊天室（從聊天室進來的就直接回去）。</summary>
        void OpenChat()
        {
            Close();
            var pm = FindObjectOfType<PageManager>(true);
            pm?.ShowChatPage();
        }

        void OnDestroy()
        {
            StopCamera();
            foreach (var t in _big.Values) if (t != null) Destroy(t);
            foreach (var t in _thumbs.Values) if (t != null) Destroy(t);
            if (_blurred != null) { _blurred.Release(); Destroy(_blurred); }
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        void OnChat(ChatEntry e) => _dirty = true;
        void OnChatRemoved(string key) => _dirty = true;
        void OnSaveChanged()
        {
            // 有新照片同步下來：保持在同一張
            string cur = _index < _shots.Count ? _shots[_index].meta.fileName : null;
            LoadShots();
            int i = _shots.FindIndex(s => s.meta.fileName == cur);
            _index = Mathf.Max(0, i);
            _dirty = true;
        }

        void Update()
        {
            if (_canvas == null || !_canvas.gameObject.activeSelf) return;
            float dt = Time.unscaledDeltaTime;
            Relayout();

            _openT = Mathf.MoveTowards(_openT, _open ? 1f : 0f, dt / 0.25f);
            _group.alpha = _openT;
            _group.blocksRaycasts = _open;
            if (!_open && _openT <= 0f) { _canvas.gameObject.SetActive(false); return; }

            if (_dirty && !_camMode) { _dirty = false; Show(_index); }

            int unread = ChatScreenController.UnreadCount();
            if (unread != _shownUnread)
            {
                _shownUnread = unread;
                _chatBadge.gameObject.SetActive(unread > 0);
                _chatBadgeText.text = unread > 99 ? "99" : unread.ToString();
            }

            // 寵物：照片是自己拍的跳一跳，其他人的歪頭（設計稿 c5Jump／c5Tilt）
            float t = Time.unscaledTime;
            bool mine = _index < _shots.Count && _shots[_index].mine;
            if (mine)
            {
                float p = t % 1f;
                float y = p < 0.35f ? Mathf.Lerp(0f, 28f, p / 0.35f) : (p < 0.6f ? Mathf.Lerp(28f, 0f, (p - 0.35f) / 0.25f) : 0f);
                _pet.anchoredPosition = new Vector2(52f, -90f + y);
                _pet.localRotation = Quaternion.identity;
            }
            else
            {
                float p = t % 2f / 2f;
                float r = p < 0.3f ? Mathf.Lerp(0f, -8f, p / 0.3f) : (p < 0.6f ? Mathf.Lerp(-8f, 6f, (p - 0.3f) / 0.3f) : Mathf.Lerp(6f, 0f, (p - 0.6f) / 0.4f));
                _pet.anchoredPosition = new Vector2(52f, -90f);
                _pet.localRotation = Quaternion.Euler(0f, 0f, -r);
            }

            UpdateShutter();

            bool canSend = !string.IsNullOrWhiteSpace(_input.text);
            _sendBg.color = canSend ? Coral : new Color(Coral.r, Coral.g, Coral.b, 0.45f);
        }

        // ─── 資料 ──────────────────────────────────────────────────────

        void LoadShots()
        {
            _shots.Clear();
            var photos = SaveManager.Instance?.data?.photos;
            if (photos == null) return;
            var fb = FirebaseManager.Instance;
            string me = fb != null ? fb.GetUserId() : null;
            string raw = fb != null && !string.IsNullOrEmpty(fb.RoomId) ? PlayerPrefs.GetString("BlockPet.AlbumSeen." + fb.RoomId, "") : "";
            _seenBefore = DateTime.TryParse(raw, out var seen) ? seen : DateTime.Now;
            foreach (var p in photos)
            {
                if (p == null || string.IsNullOrEmpty(p.fileName)) continue;
                DateTime.TryParse(p.timestamp, out var t);
                bool mine = !string.IsNullOrEmpty(me) && p.takerUserId == me;
                _shots.Add(new Shot
                {
                    meta = p, time = t, mine = mine,
                    who = mine ? "我" : (string.IsNullOrEmpty(p.takerName) ? "營地" : p.takerName),
                    isNew = t > _seenBefore,
                });
            }
            _shots.Sort((a, b) => b.time.CompareTo(a.time)); // 新到舊
        }

        static string Ago(DateTime t)
        {
            if (t == default) return "";
            var d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "剛剛";
            if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} 分鐘前";
            if (d.TotalDays < 1) return $"{(int)d.TotalHours} 小時前";
            if (d.TotalDays < 2) return "昨天";
            return $"{(int)d.TotalDays} 天前";
        }

        static string PhotoRef(PhotoMeta m) => "photo:" + m.fileName;

        /// <summary>聊天室裡這張照片的餵食貼文。</summary>
        public static ChatEntry PostOf(PhotoMeta m)
        {
            var fb = FirebaseManager.Instance;
            if (fb == null) return null;
            foreach (var e in fb.ChatEntries) if (e.IsPost && e.msg.photoFileName == m.fileName) return e;
            return null;
        }

        /// <summary>這張照片的留言（回覆貼文、或回覆 photo:{檔名}），舊到新。</summary>
        public static List<ChatEntry> CommentsOf(PhotoMeta m)
        {
            var list = new List<ChatEntry>();
            var fb = FirebaseManager.Instance;
            if (fb == null) return list;
            var post = PostOf(m);
            string postId = post?.msg.messageId;
            string photoRef = PhotoRef(m);
            foreach (var e in fb.ChatEntries)
            {
                string r = e.msg.replyToMessageId;
                if (string.IsNullOrEmpty(r)) continue;
                if (r == photoRef || (!string.IsNullOrEmpty(postId) && r == postId)) list.Add(e);
            }
            return list;
        }

        Sprite PetSprite()
        {
            if (petSprites == null || petSprites.Length == 0) return null;
            int i = PetCollectionManager.Instance != null ? PetCollectionManager.Instance.CurrentPetIndex : 0;
            return petSprites[Mathf.Abs(i) % petSprites.Length];
        }

        // ─── 顯示一張 ──────────────────────────────────────────────────

        void Show(int index)
        {
            if (_shots.Count == 0) { Close(); return; }
            _index = (index % _shots.Count + _shots.Count) % _shots.Count;
            var s = _shots[_index];
            _viewed.Add(s.meta.fileName);

            // 頂部
            _title.text = s.who + " 的照片";
            _ago.text = Ago(s.time);
            int newLeft = 0;
            foreach (var x in _shots) if (x.isNew && !_viewed.Contains(x.meta.fileName)) newLeft++;
            _newBadge.gameObject.SetActive(newLeft > 0);
            _newText.text = "新 " + newLeft;
            BuildStack();

            // 大照片
            _photo.texture = _big.TryGetValue(s.meta.fileName, out var tex) ? tex : null;
            _photo.enabled = _photo.texture != null;
            FitPhoto();
            _newTag.gameObject.SetActive(s.isNew);
            BuildOverlay(s);
            ((TMP_Text)_input.placeholder).text = s.mine ? "補一句話…" : "留言給 " + s.who + "…";

            // 營地：寵物看照片說話
            SetSay(s.mine ? "咦，是我拍的耶！" : (s.who == "營地" ? "這是誰拍的呀？" : $"{s.who} 拍的！"));
            RefreshPetStatus();

            if (_loader != null) StopCoroutine(_loader);
            _loader = StartCoroutine(LoadTextures());
        }

        void OpenPhotoView()
        {
            if (_camMode || _shots.Count == 0) return;
            var metas = _shots.ConvertAll(x => x.meta);
            BlockPet.UI.Album.PhotoViewScreen.Open(metas, _index, skin != null ? skin.font : null, background);
        }

        void SetSay(string text)
        {
            _petSay.text = text;
            float sw = _petSay.GetPreferredValues(text).x + 22f;
            _sayBubble.sizeDelta = new Vector2(sw, 30f);
        }

        // ─── 快門：拍一張直接餵寵物 ─────────────────────────────────────

        /// <summary>快門：第一次按打開相機畫面，相機畫面時按就拍下並餵給寵物。</summary>
        void Shoot()
        {
            if (_camMode) { CaptureAndFeed(); return; }
            if (PhotoCooldown.IsActive) { SetSay("等一下再拍喔～"); return; }
            if (WebCamTexture.devices.Length == 0) { SetSay("找不到相機…"); return; }
            _camMode = true;
            StartCamera();
            _photo.enabled = false;
            _overlay.gameObject.SetActive(false);
            _newTag.gameObject.SetActive(false);
            _prevBtn.gameObject.SetActive(false);
            _nextBtn.gameObject.SetActive(false);
            _swipe.enabled = false;
            _camView.gameObject.SetActive(true);
            _flipBtn.gameObject.SetActive(true);
            SetSay("要拍什麼給我看？");
        }

        void StartCamera()
        {
            StopCamera();
            string device = null;
            foreach (var d in WebCamTexture.devices)
                if (d.isFrontFacing == _camFront) { device = d.name; break; }
            if (device == null && WebCamTexture.devices.Length > 0) device = WebCamTexture.devices[0].name;
            _cam = device != null ? new WebCamTexture(device, 1280, 1280) : new WebCamTexture();
            _camView.texture = _cam;
            _cam.Play();
        }

        void StopCamera()
        {
            if (_cam == null) return;
            _cam.Stop();
            Destroy(_cam);
            _cam = null;
            if (_camView != null) _camView.texture = null;
        }

        void FlipCamera()
        {
            if (!_camMode) return;
            _camFront = !_camFront;
            StartCamera();
        }

        /// <summary>關掉相機畫面，回到原本的照片。</summary>
        void ExitCamera()
        {
            if (!_camMode) return;
            _camMode = false;
            StopCamera();
            _camView.gameObject.SetActive(false);
            _flipBtn.gameObject.SetActive(false);
            _overlay.gameObject.SetActive(true);
            _prevBtn.gameObject.SetActive(true);
            _nextBtn.gameObject.SetActive(true);
            _swipe.enabled = true;
            if (_open) Show(_index);
        }

        /// <summary>相機畫面跟著鏡頭轉向（手機的感光元件多半是橫的），等比填滿照片框；前鏡頭左右鏡像。</summary>
        void UpdateCameraView()
        {
            if (!_camMode || _cam == null || _cam.width <= 16) return;
            int angle = _cam.videoRotationAngle;
            bool sideways = angle % 180 != 0;
            float fw = PhotoSize, fh = _photoH;
            var rt = _camView.rectTransform;
            rt.sizeDelta = sideways ? new Vector2(fh, fw) : new Vector2(fw, fh);
            rt.localRotation = Quaternion.AngleAxis(-angle, Vector3.forward);
            // 縮放先套用、再旋轉：畫面轉了 90° 時，螢幕上的左右鏡像要翻原圖的 y
            float sx = 1f, sy = _cam.videoVerticallyMirrored ? -1f : 1f;
            if (_camFront) { if (sideways) sy = -sy; else sx = -sx; }
            rt.localScale = new Vector3(sx, sy, 1f);
            _camView.uvRect = ChatScreenController.AspectFill(_cam.width, _cam.height, rt.sizeDelta.x, rt.sizeDelta.y);
        }

        /// <summary>拍下（轉正）→ 餵給寵物（跟相機拍照餵食同一條路：辨識、存照片、結算、冷卻）→ 顯示新照片與結果。</summary>
        void CaptureAndFeed()
        {
            if (_cam == null || _cam.width <= 16) return;
            var photo = CaptureUpright(_cam);

            FeedResult? result = null;
            Action<FeedResult> grab = r => result = r;
            FeedSettlement.OnSettled += grab;
            var feed = FindObjectOfType<FeedController>(true);
            if (feed != null) feed.FeedWithPhoto(photo);
            else if (SaveManager.Instance != null)
            {
                var vision = BlockPet.Vision.PhotoClassifier.Instance != null ? BlockPet.Vision.PhotoClassifier.Instance.Classify(photo) : default;
                SaveManager.Instance.SavePhoto(photo, vision.category, vision.confidence);
            }
            FeedSettlement.OnSettled -= grab;
            PhotoCooldown.Start();

            ExitCamera();
            LoadShots();
            Show(0);
            if (result.HasValue) ShowShotResult(result.Value);
        }

        /// <summary>把鏡頭的原始畫面轉成正的照片（先處理上下鏡像，再依 videoRotationAngle 順時針轉）。</summary>
        static Texture2D CaptureUpright(WebCamTexture cam)
        {
            int w = cam.width, h = cam.height;
            var src = cam.GetPixels32();
            if (cam.videoVerticallyMirrored)
            {
                for (int y = 0; y < h / 2; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int a = y * w + x, b = (h - 1 - y) * w + x;
                        (src[a], src[b]) = (src[b], src[a]);
                    }
            }
            int angle = ((cam.videoRotationAngle % 360) + 360) % 360;
            int dw = angle % 180 != 0 ? h : w, dh = angle % 180 != 0 ? w : h;
            var dst = angle == 0 ? src : new Color32[w * h];
            if (angle != 0)
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int nx, ny;
                        if (angle == 90) { nx = y; ny = w - 1 - x; }
                        else if (angle == 180) { nx = w - 1 - x; ny = h - 1 - y; }
                        else { nx = h - 1 - y; ny = x; }
                        dst[ny * dw + nx] = src[y * w + x];
                    }
            }
            var tex = new Texture2D(dw, dh, TextureFormat.RGBA32, false);
            tex.SetPixels32(dst);
            tex.Apply();
            return tex;
        }

        void ShowShotResult(FeedResult r)
        {
            string title, sub, say;
            Color color;
            if (r.dead) { title = "……"; sub = "昏倒了，先喝復活藥水"; say = "……"; color = Muted; }
            else if (r.hit) { title = "好好吃！"; sub = $"命中想吃・+{r.coins} 金幣"; say = "謝謝！好好吃～"; color = Hex(0xE0662E); }
            else { title = "嗯…？"; sub = $"不是想吃的・+{r.coins} 金幣"; say = "嗯…這是什麼？"; color = Muted; }
            _camToastTitle.text = title;
            _camToastTitle.color = color;
            _camToastSub.text = sub;
            float w = Mathf.Max(_camToastTitle.GetPreferredValues(title).x, _camToastSub.GetPreferredValues(sub).x) + 24f;
            _camToast.sizeDelta = new Vector2(w, 44f);
            _camToastUntil = Time.unscaledTime + 2.2f;
            SetSay(say);
        }

        void UpdateShutter()
        {
            UpdateCameraView();
            bool cooling = PhotoCooldown.IsActive && !_camMode;
            _shutterGroup.alpha = cooling ? 0.6f : 1f;
            _shutterCore.color = new Color(1f, 1f, 1f, _camMode ? 0.95f : 0.6f); // 相機畫面時圈圈變實心，表示按下就拍
            _shutterText.gameObject.SetActive(cooling);
            if (cooling) _shutterText.text = PhotoCooldown.RemainingText;

            // 結果泡泡：0.3 秒彈出（c5Pop），2.2 秒後收掉
            float left = _camToastUntil - Time.unscaledTime;
            bool show = left > 0f;
            if (_camToast.gameObject.activeSelf != show) _camToast.gameObject.SetActive(show);
            if (show)
            {
                float p = Mathf.Clamp01((2.2f - left) / 0.3f);
                _camToast.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, 1f - (1f - p) * (1f - p));
            }
        }

        void BuildStack()
        {
            foreach (Transform c in _stack) Destroy(c.gameObject);
            int n = Mathf.Min(4, _shots.Count);
            float x = 0f;
            // 目前這張在最前面，後面接著下幾張
            for (int k = 0; k < n; k++)
            {
                int qi = (_index + k) % _shots.Count;
                float size = k == 0 ? 38f : 32f;
                if (k > 0) x -= 12f;
                var b = Place(_stack, "Thumb", x, (40f - size) / 2f, size, size);
                b.SetSiblingIndex(0); // 前面的蓋在後面的上面
                var ring = UIShapes.AddRounded(b.gameObject, k == 0 ? Yellow : Card, size / 2f);
                float bw = k == 0 ? 2.5f : 2f;
                var frame = Place(b, "Frame", bw, bw, size - bw * 2f, size - bw * 2f);
                UIShapes.AddRounded(frame.gameObject, Dark, size / 2f - bw).raycastTarget = false;
                frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                var img = Place(frame, "Img", 0, 0, size - bw * 2f, size - bw * 2f).gameObject.AddComponent<RawImage>();
                img.raycastTarget = false;
                string file = _shots[qi].meta.fileName;
                if (_thumbs.TryGetValue(file, out var t) && t != null) { img.texture = t; img.uvRect = ChatScreenController.AspectFill(t.width, t.height, 1f, 1f); }
                else img.enabled = false;
                var btn = b.gameObject.AddComponent<Button>();
                btn.targetGraphic = ring;
                btn.transition = Selectable.Transition.None;
                int target = qi;
                btn.onClick.AddListener(() => Show(target));
                x += size;
            }
            if (_shots.Count > 4)
            {
                x -= 12f;
                var more = Place(_stack, "More", x, 5, 30, 30);
                more.SetSiblingIndex(0);
                UIShapes.AddRounded(more.gameObject, Card, 15).raycastTarget = false;
                UIShapes.AddRounded(Place(more, "Fill", 2, 2, 26, 26).gameObject, Ink, 13).raycastTarget = false;
                Label(more, 0, 0, 30, 30, "+" + (_shots.Count - 4), 10, Color.white, TextAlignmentOptions.Center);
                x += 30f;
            }
            _stack.sizeDelta = new Vector2(x, 40f);
            // 文字接在縮圖疊右邊
            float tx = 56f + x + 8f;
            _title.rectTransform.anchoredPosition = new Vector2(tx, _title.rectTransform.anchoredPosition.y);
            _ago.rectTransform.anchoredPosition = new Vector2(tx, _ago.rectTransform.anchoredPosition.y);
            float tw = DesignW - 20f - tx - 6f - 40f - (_newBadge.gameObject.activeSelf ? 8f + 48f + 8f : 8f);
            _title.rectTransform.sizeDelta = new Vector2(tw, 18f);
            _ago.rectTransform.sizeDelta = new Vector2(tw, 15f);
        }

        void BuildOverlay(Shot s)
        {
            foreach (Transform c in _overlay) Destroy(c.gameObject);
            float w = PhotoSize - 20f;
            var items = new List<RectTransform>();
            // 最近 2 則留言（深色玻璃）
            var comments = CommentsOf(s.meta);
            for (int i = Mathf.Max(0, comments.Count - 2); i < comments.Count; i++)
            {
                var e = comments[i];
                string who = ChatMessage.GetBestDisplayName(e.msg);
                bool mine = FirebaseManager.Instance != null && e.msg.senderId == FirebaseManager.Instance.GetUserId();
                string text = (mine ? "我" : who) + "：" + e.msg.message;
                var pill = Place(_overlay, "Comment", 0, 0, w, 30);
                var bg = UIShapes.AddRounded(pill.gameObject, new Color(Dark.r, Dark.g, Dark.b, 0.55f), 15);
                bg.raycastTarget = false;
                UIShapes.AddRounded(Place(pill, "Avatar", 4, 4, 22, 22).gameObject, ChatScreenController.PersonColor(who, mine), 11).raycastTarget = false;
                Label(pill, 4, 4, 22, 22, ChatScreenController.Initial(who, mine), 10, Color.white, TextAlignmentOptions.Center);
                var t = Label(pill, 32, 0, w - 42f, 30, text, 12, Color.white, TextAlignmentOptions.Left);
                t.enableWordWrapping = false;
                t.overflowMode = TextOverflowModes.Ellipsis;
                pill.sizeDelta = new Vector2(Mathf.Min(w, 32f + t.GetPreferredValues(text).x + 10f), 30f);
                items.Add(pill);
            }
            // 說明（米白膠囊）
            string caption = !string.IsNullOrEmpty(s.meta.caption) ? s.meta.caption : (s.mine ? "我餵的照片" : s.who + " 餵的照片");
            var cap = Place(_overlay, "Caption", 0, 0, w, 32);
            UIShapes.AddShadow(cap, new Color(0, 0, 0, 0.25f), 16, 8, 3);
            UIShapes.AddRounded(Place(cap, "Fill", 0, 0, 0, 0).gameObject, Card, 16).raycastTarget = false;
            StretchIn((RectTransform)cap.GetChild(1));
            var ct = Label(cap, 14, 0, w - 28f, 32, caption, 15, Ink, TextAlignmentOptions.Left);
            ct.enableWordWrapping = false;
            ct.overflowMode = TextOverflowModes.Ellipsis;
            cap.sizeDelta = new Vector2(Mathf.Min(w, ct.GetPreferredValues(caption).x + 28f), 32f);
            items.Add(cap);

            // 由下往上疊（間距 6）
            float y = 0f;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                items[i].anchoredPosition = new Vector2(0f, -(_overlay.sizeDelta.y - y - items[i].sizeDelta.y));
                y += items[i].sizeDelta.y + 6f;
            }
        }

        void RefreshPetStatus()
        {
            _petImage.sprite = PetSprite();
            _petImage.enabled = _petImage.sprite != null;
            var health = FindObjectOfType<PetHealthManager>(true);
            float hearts = health != null && health.maxHealth > 0f ? health.CurrentHealth / health.maxHealth * 5f : 5f;
            hearts = Mathf.Round(hearts * 2f) / 2f;
            for (int i = 0; i < _hearts.Length; i++)
            {
                float fill = Mathf.Clamp01(hearts - i);
                if (config != null && config.heartFull != null)
                    _hearts[i].sprite = fill >= 1f ? config.heartFull : (fill >= 0.5f ? config.heartHalf : config.heartEmpty);
                else _hearts[i].color = fill >= 0.5f ? Hex(0xF06B5B) : new Color(1, 1, 1, 0.55f);
            }
            float evo = PetCollectionManager.Instance != null ? PetCollectionManager.Instance.ProgressPercent : 0f;
            _evoFill.sizeDelta = new Vector2(64f * Mathf.Clamp01(evo / 100f), 6f);
            _evoSeg.color = config != null && config.segments.Count > 0 ? config.segments[0].color : Hex(0xF29A3A);
            _energy.text = Mathf.FloorToInt(evo) + "%";
        }

        /// <summary>先讀目前這張大圖，再讀縮圖疊要用的幾張（每幀一張）。</summary>
        IEnumerator LoadTextures()
        {
            var s = _shots[_index];
            if (!_big.ContainsKey(s.meta.fileName))
            {
                var full = SaveManager.Instance != null ? SaveManager.Instance.LoadPhoto(s.meta) : null;
                if (full != null)
                {
                    _big[s.meta.fileName] = ChatScreenController.Downscale(full, PhotoTexW);
                    _thumbs[s.meta.fileName] = ChatScreenController.Downscale(full, ThumbTexW);
                    Destroy(full);
                }
                if (this == null) yield break;
                var tex = _big.TryGetValue(s.meta.fileName, out var b) ? b : null;
                _photo.texture = tex;
                _photo.enabled = tex != null;
                if (tex != null) FitPhoto();
                yield return null;
            }
            bool changed = false;
            for (int k = 1; k < Mathf.Min(4, _shots.Count); k++)
            {
                var m = _shots[(_index + k) % _shots.Count].meta;
                if (_thumbs.ContainsKey(m.fileName)) continue;
                var full = SaveManager.Instance != null ? SaveManager.Instance.LoadPhoto(m) : null;
                if (full != null)
                {
                    _thumbs[m.fileName] = ChatScreenController.Downscale(full, ThumbTexW);
                    Destroy(full);
                    changed = true;
                }
                yield return null;
            }
            _ = changed;
            BuildStack(); // 縮圖讀好了，重畫縮圖疊
            _loader = null;
        }

        // ─── 留言 ──────────────────────────────────────────────────────

        void Send()
        {
            if (_shots.Count == 0) return;
            var s = _shots[_index];
            if (SendComment(s.meta, s.who, _input.text)) _input.text = "";
        }

        /// <summary>
        /// 留言給一張照片：就是聊天室裡回覆這張照片的訊息（照片的餵食貼文；沒有貼文時回覆 photo:{檔名}），
        /// 所以聊天室、分割畫面、相簿的照片檢視看到的是同一份留言。送出成功回傳 true。
        /// </summary>
        public static bool SendComment(PhotoMeta meta, string who, string text)
        {
            text = (text ?? "").Trim();
            var fb = FirebaseManager.Instance;
            if (text.Length == 0 || fb == null || meta == null) return false;
            var post = PostOf(meta);
            string caption = !string.IsNullOrEmpty(meta.caption) ? meta.caption : "餵了一張照片";
            var msg = new ChatMessage(fb.GetDisplayName(), text)
            {
                senderId = fb.GetUserId(),
                kind = "text",
                replyToMessageId = post != null && !string.IsNullOrEmpty(post.msg.messageId) ? post.msg.messageId : PhotoRef(meta),
                replyToDisplayName = who,
                replyToMessagePreview = "照片：" + caption,
            };
            fb.SendChatMessage(msg);
            return true;
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
            _design.anchoredPosition = new Vector2(0f, -_topInset * _k); // _topInset 是設計單位，位移要換回畫布單位
            // 營地維持設計稿的 266；螢幕比較長時多的高度給大照片，比較短時先縮營地（最少 200）、再縮照片
            float room = _screenH - _topInset - _bottomInset - 14f;   // 營地底下留 14
            float campH = Mathf.Clamp(room - 492f, 200f, 266f);
            float photoH = Mathf.Max(260f, PhotoSize + (room - 492f - campH));
            ApplyPhotoHeight(photoH);
            _camp.anchoredPosition = new Vector2(14f, -(_card.sizeDelta.y + 68f + 14f));
            _camp.sizeDelta = new Vector2(362f, campH);
        }

        void ApplyPhotoHeight(float h)
        {
            _photoH = h;
            _card.sizeDelta = new Vector2(362f, 8f + h + 8f + 40f + 8f);
            _cardFill.sizeDelta = _card.sizeDelta;
            _frame.sizeDelta = new Vector2(PhotoSize, h);
            _photo.rectTransform.sizeDelta = new Vector2(PhotoSize, h);
            _overlay.sizeDelta = new Vector2(PhotoSize - 20f, h - 20f);
            _commentRow.anchoredPosition = new Vector2(8f, -(8f + h + 8f));
            _prevBtn.anchoredPosition = new Vector2(_prevBtn.anchoredPosition.x, -(h / 2f - 22f));
            _nextBtn.anchoredPosition = new Vector2(_nextBtn.anchoredPosition.x, -(h / 2f - 22f));
            FitPhoto();
            if (_index >= 0 && _index < _shots.Count) BuildOverlay(_shots[_index]);
        }

        /// <summary>大照片等比填滿目前的照片框（框不是正方形時要依框的比例裁）。</summary>
        void FitPhoto()
        {
            var tex = _photo.texture;
            if (tex != null) _photo.uvRect = ChatScreenController.AspectFill(tex.width, tex.height, PhotoSize, _photoH);
        }

        // ─── 建立介面 ──────────────────────────────────────────────────

        void Build()
        {
            var go = new GameObject("PhotoSplitView (runtime)", typeof(RectTransform));
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = sortingOrder;
            go.AddComponent<GraphicRaycaster>();
            _group = go.AddComponent<CanvasGroup>();
            _root = (RectTransform)go.transform;

            // 背景：營地圖模糊＋深色
            Stretch(_root, "Base").gameObject.AddComponent<Image>().color = Hex(0x8FA868);
            var photo = Stretch(_root, "BackgroundPhoto").gameObject.AddComponent<RawImage>();
            photo.raycastTarget = false;
            photo.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            Stretch(_root, "Dim").gameObject.AddComponent<Image>().color = new Color(Dark.r, Dark.g, Dark.b, 0.5f);

            _design = new GameObject("Design", typeof(RectTransform)).GetComponent<RectTransform>();
            _design.SetParent(_root, false);
            _design.anchorMin = _design.anchorMax = new Vector2(0.5f, 1f);
            _design.pivot = new Vector2(0.5f, 1f);
            _design.sizeDelta = new Vector2(DesignW, 772f);

            BuildTopBar();
            BuildPhotoCard();
            BuildCamp();
            go.SetActive(false);
        }

        void BuildTopBar()
        {
            var bar = Place(_design, "TopBar", 10, 10, DesignW - 20f, 52);
            UIShapes.AddShadow(bar, new Color(30f / 255f, 40f / 255f, 20f / 255f, 0.18f), 26, 16, 6);
            UIShapes.AddRounded(Place(bar, "Border", 0, 0, DesignW - 20f, 52).gameObject, new Color(1f, 1f, 1f, 0.65f), 26).raycastTarget = false;
            UIShapes.AddRounded(Place(bar, "Fill", 1.5f, 1.5f, DesignW - 23f, 49).gameObject, new Color(1f, 0.973f, 0.925f, 0.62f), 24.5f).raycastTarget = false;

            var back = Place(bar, "Back", 6, 6, 40, 40);
            var bbg = UIShapes.AddRounded(back.gameObject, Paper, 20);
            var chev = AddGraphic<UIPolygon>(Place(back, "Chevron", 0, 0, 40, 40));
            chev.color = Ink;
            chev.raycastTarget = false;
            chev.SetPoints(new Vector2(23f, 13f), new Vector2(23f, 27f), new Vector2(16f, 20f));
            var bb = back.gameObject.AddComponent<Button>();
            bb.targetGraphic = bbg;
            bb.onClick.AddListener(() => { if (_camMode) ExitCamera(); else Close(); });

            _stack = Place(bar, "Stack", 48, 6, 120, 40);
            _title = Label(bar, 120, 9, 160, 18, "", 13, Ink, TextAlignmentOptions.Left);
            _title.enableWordWrapping = false;
            _title.overflowMode = TextOverflowModes.Ellipsis;
            _ago = Label(bar, 120, 27, 160, 15, "", 11, Muted, TextAlignmentOptions.Left);
            _ago.enableWordWrapping = false;

            // 聊天室入口（最右邊）：對話泡泡
            float barW = DesignW - 20f;
            var chat = Place(bar, "Chat", barW - 6f - 40f, 6, 40, 40);
            var cbg = UIShapes.AddRounded(chat.gameObject, Paper, 20);
            UIShapes.AddRing(Place(chat, "Bubble", 10, 11, 20, 15).gameObject, Ink, 5f);
            var ctail = AddGraphic<UIPolygon>(Place(chat, "Tail", 0, 0, 40, 40));
            ctail.color = Ink;
            ctail.raycastTarget = false;
            ctail.SetPoints(new Vector2(14f, 24f), new Vector2(19f, 24f), new Vector2(13f, 30f));
            var cb = chat.gameObject.AddComponent<Button>();
            cb.targetGraphic = cbg;
            cb.onClick.AddListener(OpenChat);
            _chatBadge = Place(chat, "Badge", 26, -4, 18, 18);
            UIShapes.AddRounded(_chatBadge.gameObject, Coral, 9).raycastTarget = false;
            _chatBadgeText = Label(_chatBadge, 0, 0, 18, 18, "", 10, Color.white, TextAlignmentOptions.Center);
            _chatBadge.gameObject.SetActive(false);

            _newBadge = Place(bar, "New", barW - 6f - 40f - 8f - 48f, 16, 48, 20);
            UIShapes.AddRounded(Place(_newBadge, "Edge", 0, 2, 48, 20).gameObject, CoralEdge, 8).raycastTarget = false;
            UIShapes.AddRounded(Place(_newBadge, "Fill", 0, 0, 48, 20).gameObject, Coral, 8).raycastTarget = false;
            _newText = Label(_newBadge, 0, 0, 48, 20, "", 11, Color.white, TextAlignmentOptions.Center);
        }

        void BuildPhotoCard()
        {
            var card = _card = Place(_design, "PhotoCard", 14, 68, 362, 8f + PhotoSize + 8f + 40f + 8f);
            UIShapes.AddShadow(card, new Color(0, 0, 0, 0.4f), 20, 30, 14);
            _cardFill = Place(card, "Fill", 0, 0, 362, card.sizeDelta.y);
            UIShapes.AddRounded(_cardFill.gameObject, Card, 20).raycastTarget = false;

            var frame = _frame = Place(card, "Photo", 8, 8, PhotoSize, PhotoSize);
            var frameBg = UIShapes.AddRounded(frame.gameObject, Dark, 13);
            frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            _photo = Place(frame, "Img", 0, 0, PhotoSize, PhotoSize).gameObject.AddComponent<RawImage>();
            _photo.raycastTarget = false;
            // 左右滑換張（HandSwipe 只看水平方向）
            var swipe = _swipe = frame.gameObject.AddComponent<HandSwipe>();
            swipe.space = _design;
            swipe.onEnd = dx => { if (dx < -40f) Show(_index + 1); else if (dx > 40f) Show(_index - 1); };
            frameBg.raycastTarget = true;
            // 點大照片（沒有拖曳）：打開拍立得照片檢視（5.14 ⑥），關掉後回到這裡
            var tap = frame.gameObject.AddComponent<Button>();
            tap.targetGraphic = frameBg;
            tap.transition = Selectable.Transition.None;
            tap.onClick.AddListener(OpenPhotoView);

            _newTag = Place(frame, "NewTag", PhotoSize - 10f - 32f, 10, 32, 20);
            UIShapes.AddRounded(Place(_newTag, "Edge", 0, 2, 32, 20).gameObject, CoralEdge, 8).raycastTarget = false;
            UIShapes.AddRounded(Place(_newTag, "Fill", 0, 0, 32, 20).gameObject, Coral, 8).raycastTarget = false;
            Label(_newTag, 0, 0, 32, 20, "新", 12, Color.white, TextAlignmentOptions.Center);

            _prevBtn = Arrow(frame, 6, true);
            _nextBtn = Arrow(frame, PhotoSize - 6f - 44f, false);

            _overlay = Place(frame, "Overlay", 10, 10, PhotoSize - 20f, PhotoSize - 20f);

            // 相機畫面（相機模式才顯示；置中，依鏡頭方向旋轉）
            _camView = Place(frame, "Camera", 0, 0, PhotoSize, PhotoSize).gameObject.AddComponent<RawImage>();
            _camView.rectTransform.anchorMin = _camView.rectTransform.anchorMax = _camView.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _camView.rectTransform.anchoredPosition = Vector2.zero;
            _camView.raycastTarget = false;
            _camView.color = Color.white;
            _camView.gameObject.SetActive(false);

            // 右上：翻轉鏡頭（深色半透明圓＋繞圈箭頭）
            _flipBtn = Place(frame, "Flip", 0, 0, 44, 44);
            _flipBtn.anchorMin = _flipBtn.anchorMax = _flipBtn.pivot = new Vector2(1f, 1f);
            _flipBtn.anchoredPosition = new Vector2(-10f, -10f);
            var fbg = UIShapes.AddRounded(_flipBtn.gameObject, new Color(Dark.r, Dark.g, Dark.b, 0.4f), 22);
            UIShapes.AddThinRing(Place(_flipBtn, "Circle", 11, 11, 22, 22).gameObject, Color.white, 11);
            // 兩個箭頭尖：右上往下、左下往上，看起來是繞一圈
            var a1 = AddGraphic<UIPolygon>(Place(_flipBtn, "ArrowA", 0, 0, 44, 44));
            a1.color = Color.white;
            a1.raycastTarget = false;
            a1.SetPoints(new Vector2(29f, 17f), new Vector2(37f, 17f), new Vector2(33f, 23f));
            var a2 = AddGraphic<UIPolygon>(Place(_flipBtn, "ArrowB", 0, 0, 44, 44));
            a2.color = Color.white;
            a2.raycastTarget = false;
            a2.SetPoints(new Vector2(7f, 27f), new Vector2(15f, 27f), new Vector2(11f, 21f));
            var fb = _flipBtn.gameObject.AddComponent<Button>();
            fb.targetGraphic = fbg;
            fb.onClick.AddListener(FlipCamera);
            _flipBtn.gameObject.SetActive(false);

            // 留言框
            var row = _commentRow = Place(card, "Comment", 8, 8 + PhotoSize + 8, PhotoSize, 40);
            UIShapes.AddRounded(row.gameObject, Beige, 13).raycastTarget = false;
            _input = BuildInput(row, 12, 2, PhotoSize - 12f - 4f - 32f - 6f, 36);
            var send = Place(row, "Send", PhotoSize - 4f - 32f, 4, 32, 32);
            _sendBg = UIShapes.AddRounded(send.gameObject, Coral, 16);
            var plane = AddGraphic<UIPolygon>(Place(send, "Plane", 0, 0, 32, 32));
            plane.color = Color.white;
            plane.raycastTarget = false;
            // 紙飛機（設計稿 M2 10 18 2l-4 16-4-6-8-2Z，縮到 14px）
            float u = 14f / 20f, o = 9f;
            plane.SetPoints(new Vector2(o + 2f * u, o + 10f * u), new Vector2(o + 18f * u, o + 2f * u), new Vector2(o + 14f * u, o + 18f * u), new Vector2(o + 10f * u, o + 12f * u));
            var sb = send.gameObject.AddComponent<Button>();
            sb.targetGraphic = _sendBg;
            sb.transition = Selectable.Transition.None;
            sb.onClick.AddListener(Send);
        }

        RectTransform Arrow(RectTransform frame, float x, bool left)
        {
            var b = Place(frame, left ? "Prev" : "Next", x, PhotoSize / 2f - 22f, 44, 44);
            var bg = UIShapes.AddRounded(b.gameObject, new Color(0, 0, 0, 0.22f), 22);
            var p = AddGraphic<UIPolygon>(Place(b, "Icon", 0, 0, 44, 44));
            p.color = Color.white;
            p.raycastTarget = false;
            if (left) p.SetPoints(new Vector2(25f, 15f), new Vector2(25f, 29f), new Vector2(18f, 22f));
            else p.SetPoints(new Vector2(19f, 15f), new Vector2(26f, 22f), new Vector2(19f, 29f));
            var btn = b.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => Show(_index + (left ? -1 : 1)));
            return b;
        }

        void BuildCamp()
        {
            _camp = Place(_design, "Camp", 14, 492, 362, 266);
            UIShapes.AddShadow(_camp, new Color(0, 0, 0, 0.4f), 22, 24, 10);
            UIShapes.AddRounded(Place(_camp, "Border", 0, 0, 0, 0).gameObject, Card, 22).raycastTarget = false;
            StretchIn((RectTransform)_camp.GetChild(1));
            var inner = Place(_camp, "Inner", 0, 0, 0, 0);
            StretchIn(inner, 3, 3, 3, 3);
            UIShapes.AddRounded(inner.gameObject, Hex(0x8FA868), 19).raycastTarget = false;
            inner.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var img = Place(inner, "Camp", 0, 0, 0, 0).gameObject.AddComponent<RawImage>();
            StretchIn(img.rectTransform);
            img.raycastTarget = false;
            img.texture = background;
            var fit = img.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            if (background != null) fit.aspectRatio = (float)background.width / Mathf.Max(1, background.height);

            // 左上：寵物頭像、愛心、進化條
            var pill = Place(inner, "Status", 5, 5, 200, 34);
            UIShapes.AddRounded(pill.gameObject, new Color(1f, 1f, 1f, 0.7f), 17).raycastTarget = false;
            StretchIn(UIShapes.AddRounded(Place(pill, "Fill", 0, 0, 0, 0).gameObject, new Color(1f, 0.973f, 0.925f, 0.7f), 16).rectTransform, 1, 1, 1, 1);
            var av = Place(pill, "Avatar", 4, 4, 26, 26);
            UIShapes.AddRounded(av.gameObject, Hex(0xFCE7A6), 13).raycastTarget = false;
            var avImg = Place(av, "Img", 1, 3, 24, 20).gameObject.AddComponent<Image>();
            avImg.preserveAspect = true;
            avImg.raycastTarget = false;
            avImg.sprite = PetSprite();
            avImg.enabled = avImg.sprite != null;
            for (int i = 0; i < 5; i++)
            {
                _hearts[i] = Place(pill, "Heart", 36f + i * 11f, 12.5f, 10, 9).gameObject.AddComponent<Image>();
                _hearts[i].preserveAspect = true;
                _hearts[i].raycastTarget = false;
            }
            var track = Place(pill, "Evo", 36f + 55f + 6f, 14, 64, 6);
            UIShapes.AddRounded(track.gameObject, new Color(1f, 1f, 1f, 0.6f), 3).raycastTarget = false;
            track.gameObject.AddComponent<RectMask2D>();
            _evoFill = Place(track, "Fill", 0, 0, 0, 6);
            _evoSeg = _evoFill.gameObject.AddComponent<Image>();
            _evoSeg.raycastTarget = false;
            _energy = Label(pill, 36f + 55f + 6f + 70f, 0, 40, 34, "", 11, Ink, TextAlignmentOptions.Left);
            _energy.enableWordWrapping = false;

            // 寵物（中間偏下）與說話泡泡
            var petHost = Place(inner, "PetHost", 107, 75, 104, 100);
            UIShapes.AddRounded(Place(petHost, "Shadow", 14, 80, 76, 12).gameObject, new Color(30f / 255f, 45f / 255f, 20f / 255f, 0.3f), 6).raycastTarget = false;
            _pet = Place(petHost, "Pet", 0, 0, 104, 90);
            _pet.pivot = new Vector2(0.5f, 0f); // 從腳底轉／跳
            _pet.anchoredPosition = new Vector2(52f, -90f);
            _petImage = Place(_pet, "Img", 0, 0, 0, 0).gameObject.AddComponent<Image>();
            StretchIn(_petImage.rectTransform);
            _petImage.preserveAspect = true;
            _petImage.raycastTarget = false;

            _sayBubble = Place(inner, "Say", 201, 45, 120, 30);
            UIShapes.AddShadow(_sayBubble, new Color(0, 0, 0, 0.18f), 14, 8, 3);
            UIShapes.AddRounded(Place(_sayBubble, "Fill", 0, 0, 0, 0).gameObject, Color.white, 14).raycastTarget = false;
            StretchIn((RectTransform)_sayBubble.GetChild(1));
            _petSay = Label(_sayBubble, 0, 0, 0, 0, "", 13, Ink, TextAlignmentOptions.Center);
            StretchIn(_petSay.rectTransform);
            _petSay.enableWordWrapping = false;

            // 右側中間：半透明快門圈圈（56，白色細外框＋中間 42 的白圓）
            _shutter = Place(inner, "Shutter", 0, 0, 56, 56);
            _shutter.anchorMin = _shutter.anchorMax = new Vector2(1f, 0.5f);
            _shutter.pivot = new Vector2(1f, 0.5f);
            _shutter.anchoredPosition = new Vector2(-9f, 0f); // 框的右邊 12（內框已縮 3）
            _shutterGroup = _shutter.gameObject.AddComponent<CanvasGroup>();
            UIShapes.AddShadow(_shutter, new Color(0, 0, 0, 0.2f), 28, 10, 3);
            var disc = UIShapes.AddRounded(Place(_shutter, "Disc", 0, 0, 56, 56).gameObject, new Color(Dark.r, Dark.g, Dark.b, 0.15f), 28);
            UIShapes.AddThinRing(Place(_shutter, "Ring", 0, 0, 56, 56).gameObject, new Color(1f, 1f, 1f, 0.8f), 28);
            _shutterCore = UIShapes.AddRounded(Place(_shutter, "Core", 7, 7, 42, 42).gameObject, new Color(1f, 1f, 1f, 0.6f), 21);
            _shutterCore.raycastTarget = false;
            _shutterText = Label(_shutter, 0, 0, 56, 56, "", 12, Ink, TextAlignmentOptions.Center);
            _shutterText.enableWordWrapping = false;
            var shoot = _shutter.gameObject.AddComponent<Button>();
            shoot.targetGraphic = disc;
            shoot.onClick.AddListener(Shoot);

            // 左下：拍完的結果
            _camToast = Place(inner, "ShotResult", 0, 0, 160, 44);
            _camToast.anchorMin = _camToast.anchorMax = _camToast.pivot = new Vector2(0f, 0f);
            _camToast.anchoredPosition = new Vector2(9f, 11f);
            UIShapes.AddShadow(_camToast, new Color(0, 0, 0, 0.22f), 14, 8, 3);
            StretchIn(UIShapes.AddRounded(Place(_camToast, "Fill", 0, 0, 0, 0).gameObject, Card, 14).rectTransform);
            _camToastTitle = Label(_camToast, 12, 5, 200, 20, "", 14, Hex(0xE0662E), TextAlignmentOptions.Left);
            _camToastSub = Label(_camToast, 12, 24, 200, 16, "", 11, Muted, TextAlignmentOptions.Left);
            _camToastTitle.enableWordWrapping = _camToastSub.enableWordWrapping = false;
            _camToast.gameObject.SetActive(false);
        }

        TMP_InputField BuildInput(RectTransform parent, float x, float y, float w, float h)
        {
            var rt = Place(parent, "Input", x, y, w, h);
            rt.gameObject.SetActive(false); // 先設定好 TMP_InputField 的欄位再啟用
            var hit = rt.gameObject.AddComponent<Image>();
            hit.color = new Color(1, 1, 1, 0);
            var area = Place(rt, "Text Area", 0, 0, w, h);
            area.gameObject.AddComponent<RectMask2D>();
            var ph = Label(area, 0, 0, w, h, "", 14, new Color(Muted.r, Muted.g, Muted.b, 0.6f), TextAlignmentOptions.Left);
            var text = Label(area, 0, 0, w, h, "", 14, Ink, TextAlignmentOptions.Left);
            ph.enableWordWrapping = text.enableWordWrapping = false;
            var input = rt.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = hit;
            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = ph;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 200;
            input.onSubmit.AddListener(_ => Send());
            if (skin != null && skin.font != null) input.fontAsset = skin.font;
            rt.gameObject.SetActive(true);
            return input;
        }

        void MakeBackground()
        {
            var photo = _root.Find("BackgroundPhoto").GetComponent<RawImage>();
            if (background == null) { photo.enabled = false; return; }
            if (_blurred == null || !_blurred.IsCreated())
            {
                int w = 44, h = Mathf.Max(1, Mathf.RoundToInt(44f * background.height / Mathf.Max(1, background.width)));
                var mid = RenderTexture.GetTemporary(176, Mathf.Max(1, h * 4), 0);
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
