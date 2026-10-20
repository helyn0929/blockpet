using System;
using System.Collections.Generic;
using BlockPet.Hand;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Settings
{
    /// <summary>
    /// 設定（設計稿「設定」Settings.dc.html、SettingsMember、SettingsName、SettingsDelete）：
    /// - 從下方滑上來的大面板：頭像（可換）、名稱、帳號種類（訪客／Email／Apple／Google）。
    /// - 訪客：上方黃色提醒「綁定帳號，寵物才不會不見」，可用 Apple／Google／Email 綁定（保留同一個帳號，資料不會不見）。
    /// - 帳號：修改名稱（下方滑出輸入框，12 字）、修改密碼（只有 Email 帳號）、登出。
    /// - 通知：肚子餓提醒、朋友餵了新照片、收到空間邀請（開關先存在這支手機，推播上線後再接）。
    /// - 隱私與其他：相簿權限（開系統設定）、隱私權政策、服務條款；刪除帳號（再確認一次才刪）；版本號。
    /// 介面在執行時以 390 寬的設計座標建立，依螢幕寬度等比縮放，內容可上下捲動。
    /// 由「BlockPet/Build Settings Screen」掛到 SettingPage，並關掉舊的 UI Toolkit 設定頁。
    /// </summary>
    public class SettingsScreenController : MonoBehaviour
    {
        const float DesignW = 390f;
        const float SheetTop = 34f;
        const float HeaderH = 58f;
        const float PadX = 16f;
        const float ContentW = DesignW - PadX * 2f;
        const int NameMax = 12;
        const int MinPassword = 8;

        const string PrivacyPolicyUrl = "https://blockpet.app/privacy";
        const string TermsUrl = "https://blockpet.app/terms";
        const string PrefsNotify = "BlockPet.Notify.";

        static readonly Color Ink = Hex(0x3A2E22);
        static readonly Color Paper = Hex(0xFFF8EC);
        static readonly Color Card = Hex(0xFFFCF4);
        static readonly Color Yellow = Hex(0xF6C945);
        static readonly Color YellowEdge = Hex(0xD9A92C);
        static readonly Color Beige = Hex(0xF2E9D8);
        static readonly Color Line = Hex(0xE2D6C0);
        static readonly Color Divider = Hex(0xEFE6D4);
        static readonly Color Muted = Hex(0x6B5D4E);
        static readonly Color Muted2 = Hex(0x8A6A4E);
        static readonly Color Chevron = Hex(0xB9AC96);
        static readonly Color Blue = Hex(0x6F8FE0);
        static readonly Color Orange = Hex(0xF29A3A);
        static readonly Color Green = Hex(0x6FA45F);
        static readonly Color Coral = Hex(0xE86B4F);
        static readonly Color Red = Hex(0xC2452B);
        static readonly Color Grass = Hex(0x8FA868);

        [Header("Style")]
        [Tooltip("字型沿用手牌（BlockPet/Build Settings Screen 會自動接上）")]
        [SerializeField] HandSkin skin;
        [Tooltip("背景：營地圖，執行時縮小模糊")]
        [SerializeField] Texture background;
        [Tooltip("沒有自訂頭像時用目前寵物的圖")]
        [SerializeField] Sprite[] petSprites;
        [SerializeField] int sortingOrder = 35;

        [Header("Links")]
        [SerializeField] PageManager pageManager;
        [SerializeField] LoginUIHandler loginUIHandler;

        class NotifyToggle
        {
            public string key;
            public Image track;
            public RectTransform knob;
            public bool on;
        }

        class Sheet
        {
            public RectTransform layer;
            public CanvasGroup group;
            public RectTransform panel;
            public RectTransform content;
            public TMP_Text status;
            public float contentH; // 面板內容高度（不含底部留白）
            public float t;
            public bool open;
        }

        // 版面
        Canvas _canvas;
        RectTransform _root;
        RectTransform _design;
        RawImage _bg;
        RenderTexture _blurred;
        float _lastW = -1f, _lastH = -1f;
        Rect _lastSafe;
        bool _dirty = true;
        float _k = 1f;

        RectTransform _sheet;
        RectTransform _viewport;
        RectTransform _content;
        ScrollRect _scroll;
        float _openT;

        // 內容
        RawImage _avatarPhoto;
        Image _avatarPet;
        TMP_Text _name;
        TMP_Text _typeChip;
        Image _typeChipBg;
        TMP_Text _typeSub;
        RectTransform _guestCard;
        TMP_Text _nameValue;
        RectTransform _passwordRow;
        RectTransform _passwordDivider;
        TMP_Text _logoutSub;
        RectTransform _accountCard;
        TMP_Text _photoValue;
        TMP_Text _version;
        readonly List<NotifyToggle> _toggles = new List<NotifyToggle>();
        readonly List<Selectable> _guestButtons = new List<Selectable>();

        // 下方面板與確認框
        Sheet _nameSheet, _passwordSheet, _bindSheet;
        TMP_InputField _nameInput, _newPw, _confirmPw, _bindEmail, _bindPw;
        TMP_Text _nameCount;
        RectTransform _deleteLayer;
        CanvasGroup _deleteGroup;
        RectTransform _deleteCard;
        TMP_Text _deleteStatus;
        TMP_Text _deleteButtonText;
        float _deleteT;
        bool _deleteOpen;

        RectTransform _toast;
        CanvasGroup _toastGroup;
        TMP_Text _toastText;
        float _toastUntil;

        bool _busy;

        // ─── Lifecycle ─────────────────────────────────────────────────

        void Awake()
        {
            if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
            if (loginUIHandler == null) loginUIHandler = FindObjectOfType<LoginUIHandler>(true);
            Build();
        }

        void OnEnable()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(true);
            MakeBackground();
            _busy = false;
            _openT = 0f;
            foreach (var s in new[] { _nameSheet, _passwordSheet, _bindSheet }) CloseSheet(s, instant: true);
            CloseDelete(instant: true);
            _toastGroup.alpha = 0f;
            _scroll.verticalNormalizedPosition = 1f;
            AvatarManager.OnAvatarChanged += RefreshAvatar;
            Refresh();
        }

        void OnDisable()
        {
            AvatarManager.OnAvatarChanged -= RefreshAvatar;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_blurred != null) { _blurred.Release(); Destroy(_blurred); }
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        void OnApplicationFocus(bool focus)
        {
            // 從系統設定（相簿權限）回來時更新
            if (focus && isActiveAndEnabled) RefreshPhotoPermission();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            Relayout();

            // 面板從下方滑上來（0.3s）
            _openT = Mathf.MoveTowards(_openT, 1f, dt / 0.3f);
            float e = 1f - Mathf.Pow(1f - _openT, 3f);
            _sheet.anchoredPosition = new Vector2(0f, -(SheetTopY() + (1f - e) * 200f));
            _sheet.GetComponent<CanvasGroup>().alpha = Mathf.Clamp01(_openT * 2f);

            foreach (var t in _toggles)
            {
                var p = t.knob.anchoredPosition;
                t.knob.anchoredPosition = new Vector2(Mathf.Lerp(p.x, t.on ? 23f : 3f, 1f - Mathf.Exp(-dt * 18f)), p.y);
                t.track.color = Color.Lerp(t.track.color, t.on ? Green : Hex(0xD9CFBD), 1f - Mathf.Exp(-dt * 18f));
            }

            foreach (var s in new[] { _nameSheet, _passwordSheet, _bindSheet }) AnimateSheet(s, dt);
            AnimateDelete(dt);

            float ta = Time.unscaledTime < _toastUntil ? 1f : 0f;
            _toastGroup.alpha = Mathf.MoveTowards(_toastGroup.alpha, ta, dt / 0.2f);
        }

        // ─── 資料 ──────────────────────────────────────────────────────

        void Refresh()
        {
            var fb = FirebaseManager.Instance;
            string name = fb != null ? fb.GetDisplayName() : "";
            bool guest = fb != null && fb.IsGuest;
            string provider = fb != null ? fb.GetSignInProvider() : null;

            _name.text = string.IsNullOrEmpty(name) ? "還沒取名字" : name;
            _nameValue.text = _name.text;

            string chip, sub;
            Color chipBg = Hex(0xE3EED9), chipFg = Hex(0x4A7A3F);
            switch (provider)
            {
                case "anonymous": chip = "訪客"; sub = "還沒綁定帳號"; chipBg = Beige; chipFg = Muted; break;
                case "apple.com": chip = "Apple 帳號"; sub = fb.GetEmail() ?? ""; break;
                case "google.com": chip = "Google 帳號"; sub = fb.GetEmail() ?? ""; break;
                case null: chip = "未登入"; sub = ""; chipBg = Beige; chipFg = Muted; break;
                default: chip = "Email 帳號"; sub = fb.GetEmail() ?? ""; break;
            }
            _typeChip.text = chip;
            _typeChip.color = chipFg;
            _typeChipBg.color = chipBg;
            float cw = _typeChip.GetPreferredValues(chip).x + 16f;
            ((RectTransform)_typeChipBg.transform).sizeDelta = new Vector2(cw, 19f);
            _typeSub.text = sub;
            _typeSub.rectTransform.anchoredPosition = new Vector2(cw + 6f, _typeSub.rectTransform.anchoredPosition.y);

            _guestCard.gameObject.SetActive(guest);
            bool email = provider == "password";
            _passwordRow.gameObject.SetActive(email);
            _passwordDivider.gameObject.SetActive(email);
            _logoutSub.gameObject.SetActive(guest);
            LayoutCard(_accountCard);

            RefreshAvatar();
            RefreshPhotoPermission();
            _version.text = "版本 " + Application.version;
            _dirty = true;
        }

        void RefreshAvatar()
        {
            var tex = AvatarManager.Instance != null ? AvatarManager.Instance.CurrentAvatar : null;
            _avatarPhoto.texture = tex;
            _avatarPhoto.gameObject.SetActive(tex != null);
            if (tex != null)
                _avatarPhoto.uvRect = AspectFill(tex.width, tex.height, 1f, 1f);
            int pet = PetCollectionManager.Instance != null ? PetCollectionManager.Instance.CurrentPetIndex : 0;
            _avatarPet.sprite = petSprites != null && petSprites.Length > 0 ? petSprites[Mathf.Abs(pet) % petSprites.Length] : null;
            _avatarPet.gameObject.SetActive(tex == null && _avatarPet.sprite != null);
        }

        void RefreshPhotoPermission()
        {
            if (_photoValue == null) return;
            _photoValue.text = PhotoLibrary.HasPermission() ? "已允許" : "未允許";
        }

        // ─── 操作 ──────────────────────────────────────────────────────

        void Close()
        {
            if (_busy) return;
            if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
            pageManager?.ShowRoomPage();
        }

        void OnChangeAvatar()
        {
            if (AvatarManager.Instance == null) return;
            AvatarManager.Instance.PickAvatarFromGallery(ok => { if (ok) RefreshAvatar(); });
        }

        void OpenName()
        {
            var fb = FirebaseManager.Instance;
            string name = fb != null && fb.HasDisplayName ? fb.GetDisplayName() : "";
            _nameInput.text = name.Length > NameMax ? name.Substring(0, NameMax) : name;
            SetStatus(_nameSheet, "", false);
            OpenSheet(_nameSheet);
        }

        void SaveName()
        {
            if (_busy) return;
            string name = (_nameInput.text ?? "").Trim();
            if (name.Length == 0) { SetStatus(_nameSheet, "請輸入名稱", true); return; }
            var fb = FirebaseManager.Instance;
            if (fb == null) return;
            _busy = true;
            fb.SetDisplayName(name, ok =>
            {
                _busy = false;
                if (this == null) return;
                if (!ok) { SetStatus(_nameSheet, "儲存失敗，請再試一次", true); return; }
                CloseSheet(_nameSheet);
                Refresh();
                Toast("名稱已更新");
            });
        }

        void OpenPassword()
        {
            _newPw.text = "";
            _confirmPw.text = "";
            SetStatus(_passwordSheet, "", false);
            OpenSheet(_passwordSheet);
        }

        void SavePassword()
        {
            if (_busy) return;
            string pw = _newPw.text ?? "";
            if (pw.Length < MinPassword) { SetStatus(_passwordSheet, "密碼至少 " + MinPassword + " 個字", true); return; }
            if (pw != _confirmPw.text) { SetStatus(_passwordSheet, "兩次輸入的密碼不一樣", true); return; }
            var fb = FirebaseManager.Instance;
            if (fb == null) return;
            _busy = true;
            fb.UpdatePassword(pw, (ok, err) =>
            {
                _busy = false;
                if (this == null) return;
                if (!ok)
                {
                    SetStatus(_passwordSheet, NeedsRecentLogin(err) ? "為了安全，請登出再登入一次後再改密碼" : "更新失敗，請再試一次", true);
                    return;
                }
                CloseSheet(_passwordSheet);
                Toast("密碼已更新");
            });
        }

        void OnLogout()
        {
            if (_busy) return;
            gameObject.SetActive(false);
            if (loginUIHandler != null) loginUIHandler.Logout();
            else
            {
                AvatarManager.Instance?.ClearAvatar();
                FirebaseManager.Instance?.SignOut();
            }
        }

        void BindApple()
        {
            var fb = FirebaseManager.Instance;
            if (fb == null || _busy) return;
            SetGuestBusy(true);
            fb.LinkWithApple(OnBound);
        }

        void BindGoogle()
        {
            var fb = FirebaseManager.Instance;
            if (fb == null || _busy) return;
            if (!FirebaseManager.GoogleSignInAvailable) { Toast("這個版本還不能用 Google 綁定，請改用 Apple 或 Email"); return; }
            SetGuestBusy(true);
            fb.LinkWithGoogle(OnBound);
        }

        void OpenBindEmail()
        {
            _bindEmail.text = "";
            _bindPw.text = "";
            SetStatus(_bindSheet, "綁定後用這組 Email 和密碼登入，寵物和空間都會保留", false);
            OpenSheet(_bindSheet);
        }

        void BindEmail()
        {
            if (_busy) return;
            string email = (_bindEmail.text ?? "").Trim().ToLowerInvariant();
            string pw = _bindPw.text ?? "";
            if (!email.Contains("@")) { SetStatus(_bindSheet, "Email 格式不對，再檢查一次", true); return; }
            if (pw.Length < MinPassword) { SetStatus(_bindSheet, "密碼至少 " + MinPassword + " 個字", true); return; }
            var fb = FirebaseManager.Instance;
            if (fb == null) return;
            SetGuestBusy(true);
            fb.LinkWithEmail(email, pw, (ok, err) =>
            {
                if (this == null) return;
                if (!ok)
                {
                    SetGuestBusy(false);
                    SetStatus(_bindSheet, "綁定失敗：這個 Email 可能已經註冊過了", true);
                    return;
                }
                CloseSheet(_bindSheet);
                OnBound(true, null);
            });
        }

        void OnBound(bool ok, string err)
        {
            if (this == null) return;
            SetGuestBusy(false);
            if (!ok)
            {
                Debug.LogWarning("[Settings] Link failed: " + err);
                Toast("綁定沒有完成，這個帳號可能已經用過了");
                return;
            }
            Refresh();
            Toast("綁定完成！換手機也找得回寵物");
        }

        void SetGuestBusy(bool busy)
        {
            _busy = busy;
            foreach (var b in _guestButtons) if (b != null) b.interactable = !busy;
        }

        // ─── 刪除帳號 ──────────────────────────────────────────────────

        void OpenDelete()
        {
            _deleteStatus.text = "";
            _deleteButtonText.text = "永久刪除帳號";
            _deleteOpen = true;
            _deleteLayer.gameObject.SetActive(true);
        }

        void CloseDelete(bool instant = false)
        {
            if (_busy && !instant) return;
            _deleteOpen = false;
            if (instant) { _deleteT = 0f; _deleteLayer.gameObject.SetActive(false); }
        }

        void ConfirmDelete()
        {
            var fb = FirebaseManager.Instance;
            if (fb == null || _busy) return;
            // 正式帳號太久沒登入時 Firebase 會拒絕刪除；先擋下來，免得空間刪了帳號卻沒刪
            if (!fb.IsGuest && !fb.SignedInWithin(TimeSpan.FromMinutes(5)))
            {
                _deleteStatus.text = "為了安全，請先登出再登入一次，再回來刪除帳號";
                return;
            }
            _busy = true;
            _deleteButtonText.text = "刪除中…";
            _deleteStatus.text = "";
            fb.GetMyRoomSummaries(rooms =>
            {
                var ids = new List<string>();
                foreach (var r in rooms) if (r != null && !string.IsNullOrEmpty(r.roomId)) ids.Add(r.roomId);
                CleanupRooms(ids, 0, () =>
                {
                    fb.DeleteUserData(_ =>
                    {
                        fb.DeleteAccount((ok, err) =>
                        {
                            _busy = false;
                            if (this == null) return;
                            if (!ok)
                            {
                                Debug.LogWarning("[Settings] DeleteAccount failed: " + err);
                                _deleteButtonText.text = "永久刪除帳號";
                                _deleteStatus.text = NeedsRecentLogin(err) ? "為了安全，請先登出再登入一次，再回來刪除帳號" : "刪除失敗，請再試一次";
                                return;
                            }
                            OnLogout();
                        });
                    });
                });
            });
        }

        /// <summary>只有自己一個人的空間整個刪掉；和別人一起的空間只退出，讓其他成員繼續養。</summary>
        void CleanupRooms(List<string> ids, int i, Action done)
        {
            var fb = FirebaseManager.Instance;
            if (fb == null || i >= ids.Count) { done(); return; }
            string id = ids[i];
            fb.GetRoomMembers(id, members =>
            {
                Action<bool, string> next = (ok, err) =>
                {
                    if (!ok) Debug.LogWarning("[Settings] cleanup room " + id + " failed: " + err);
                    CleanupRooms(ids, i + 1, done);
                };
                if (members == null || members.Count <= 1) fb.DeleteRoom(id, next);
                else fb.LeaveRoom(id, next);
            });
        }

        static bool NeedsRecentLogin(string err) =>
            !string.IsNullOrEmpty(err) && (err.IndexOf("recent", StringComparison.OrdinalIgnoreCase) >= 0
                                           || err.IndexOf("RequiresRecentLogin", StringComparison.OrdinalIgnoreCase) >= 0);

        // ─── 提示與動畫 ────────────────────────────────────────────────

        void Toast(string msg)
        {
            _toastText.text = msg;
            float w = Mathf.Min(DesignW - 40f, _toastText.GetPreferredValues(msg).x + 32f);
            _toast.sizeDelta = new Vector2(w, 40f);
            _toast.anchoredPosition = new Vector2((DesignW - w) / 2f, _toast.anchoredPosition.y);
            _toastUntil = Time.unscaledTime + 2.6f;
        }

        void SetStatus(Sheet s, string msg, bool error)
        {
            s.status.text = msg;
            s.status.color = error ? Red : Muted;
        }

        void OpenSheet(Sheet s)
        {
            s.open = true;
            s.layer.gameObject.SetActive(true);
        }

        void CloseSheet(Sheet s, bool instant = false)
        {
            if (s == null) return;
            if (_busy && !instant) return;
            s.open = false;
            if (instant) { s.t = 0f; s.layer.gameObject.SetActive(false); }
        }

        void AnimateSheet(Sheet s, float dt)
        {
            if (s == null || !s.layer.gameObject.activeSelf) return;
            s.t = Mathf.MoveTowards(s.t, s.open ? 1f : 0f, dt / 0.3f);
            float e = 1f - Mathf.Pow(1f - s.t, 3f);
            s.group.alpha = e;
            s.group.blocksRaycasts = s.open;
            s.panel.anchoredPosition = new Vector2(0f, -(1f - e) * s.panel.rect.height * s.panel.localScale.y);
            if (!s.open && s.t <= 0f) s.layer.gameObject.SetActive(false);
        }

        void AnimateDelete(float dt)
        {
            if (!_deleteLayer.gameObject.activeSelf) return;
            _deleteT = Mathf.MoveTowards(_deleteT, _deleteOpen ? 1f : 0f, dt / 0.25f);
            _deleteGroup.alpha = _deleteT;
            _deleteGroup.blocksRaycasts = _deleteOpen;
            _deleteCard.localScale = Vector3.one * _k * Mathf.Lerp(0.9f, 1f, _deleteT);
            if (!_deleteOpen && _deleteT <= 0f) _deleteLayer.gameObject.SetActive(false);
        }

        // ─── 版面 ──────────────────────────────────────────────────────

        float _topInset, _bottomInset, _screenH;

        float SheetTopY() => _topInset + SheetTop;

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
            _topInset = (Screen.height - safe.yMax) * px / _k;
            _bottomInset = safe.yMin * px / _k;
            _screenH = h / _k;

            _design.localScale = Vector3.one * _k;
            _design.sizeDelta = new Vector2(DesignW, _screenH);

            float sheetH = _screenH - SheetTopY();
            _sheet.sizeDelta = new Vector2(DesignW, sheetH);
            _viewport.sizeDelta = new Vector2(DesignW, sheetH - HeaderH);

            // 內容：上 4、下 28＋安全區，間距 18
            float y = Stack(_content, 18f, 4f);
            _content.sizeDelta = new Vector2(DesignW, y + 28f + _bottomInset);

            foreach (var s in new[] { _nameSheet, _passwordSheet, _bindSheet })
            {
                s.panel.localScale = Vector3.one * _k;
                s.panel.sizeDelta = new Vector2(DesignW, s.contentH + 26f + _bottomInset);
            }
            _deleteCard.localScale = Vector3.one * _k;
            _toast.anchoredPosition = new Vector2(_toast.anchoredPosition.x, -(_screenH - 40f - 24f - _bottomInset));
        }

        /// <summary>把啟用中的子物件由上往下排，回傳最後的 y。子物件的 x 不變。</summary>
        static float Stack(RectTransform parent, float gap, float top)
        {
            float y = top;
            bool first = true;
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = (RectTransform)parent.GetChild(i);
                if (!c.gameObject.activeSelf) continue;
                if (!first) y += gap;
                first = false;
                c.anchoredPosition = new Vector2(c.anchoredPosition.x, -y);
                y += c.sizeDelta.y;
            }
            return y;
        }

        // ─── 建立介面 ──────────────────────────────────────────────────

        void Build()
        {
            // 獨立的最上層 Canvas（不受頁面上層 CanvasGroup 影響），顯示／隱藏由 OnEnable／OnDisable 同步
            var go = new GameObject("SettingsScreen (runtime)", typeof(RectTransform));
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = sortingOrder;
            go.AddComponent<GraphicRaycaster>();
            _root = (RectTransform)go.transform;

            Stretch(_root, "Background").gameObject.AddComponent<Image>().color = Grass;
            _bg = Stretch(_root, "BackgroundPhoto").gameObject.AddComponent<RawImage>();
            _bg.raycastTarget = false;
            _bg.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            var dim = Stretch(_root, "Dim").gameObject.AddComponent<Image>();
            dim.color = new Color(20f / 255f, 24f / 255f, 14f / 255f, 0.4f);

            _design = new GameObject("Design", typeof(RectTransform)).GetComponent<RectTransform>();
            _design.SetParent(_root, false);
            _design.anchorMin = _design.anchorMax = new Vector2(0.5f, 1f);
            _design.pivot = new Vector2(0.5f, 1f);
            _design.sizeDelta = new Vector2(DesignW, 772f);

            BuildSheet();
            BuildContent();
            _nameSheet = BuildNameSheet();
            _passwordSheet = BuildPasswordSheet();
            _bindSheet = BuildBindSheet();
            BuildDelete();
            BuildToast();
            go.SetActive(false); // 元件啟用時（OnEnable）才顯示
        }

        void BuildSheet()
        {
            _sheet = Place(_design, "Sheet", 0, SheetTop, DesignW, 700);
            _sheet.gameObject.AddComponent<CanvasGroup>();
            UIShapes.AddShadow(_sheet, new Color(0, 0, 0, 0.25f), 26, 30, -10);
            var fill = UIShapes.AddRounded(Place(_sheet, "Fill", 0, 0, 0, 0).gameObject, Paper, 26);
            StretchIn(fill.rectTransform, 0, 0, 0, -30);

            // 標題列：把手、「設定」、關閉
            UIShapes.AddRounded(Place(_sheet, "Handle", 175, 8, 40, 5).gameObject, Line, 2.5f).raycastTarget = false;
            Label(_sheet, 0, 14, DesignW, 30, "設定", 22, Ink, TextAlignmentOptions.Center);
            CloseButton(_sheet, DesignW - 12f - 44f, 8f, Close);

            _viewport = Place(_sheet, "Viewport", 0, HeaderH, DesignW, 600);
            _viewport.gameObject.AddComponent<RectMask2D>();
            var vpImg = _viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(1, 1, 1, 0);
            _content = Place(_viewport, "Content", 0, 0, DesignW, 1000);
            _scroll = _viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = _viewport;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Elastic;
            _scroll.scrollSensitivity = 20f;
        }

        void BuildContent()
        {
            BuildProfile();
            BuildGuestCard();

            // 帳號
            Section("帳號", out _accountCard);
            Row(_accountCard, Blue, "名", "修改名稱", null, "", OpenName, out _, out _nameValue);
            _passwordDivider = RowDivider(_accountCard);
            _passwordRow = Row(_accountCard, Blue, "密", "修改密碼", null, null, OpenPassword, out _, out _);
            RowDivider(_accountCard);
            Row(_accountCard, Blue, "出", "登出", "訪客登出後，資料會消失", null, OnLogout, out _logoutSub, out _);
            LayoutCard(_accountCard);

            // 通知
            Section("通知", out var notify);
            ToggleRow(notify, "寵物肚子餓的提醒", "餓", "hungry");
            RowDivider(notify);
            ToggleRow(notify, "朋友餵了新照片", "照", "photo");
            RowDivider(notify);
            ToggleRow(notify, "收到空間邀請", "邀", "invite");
            LayoutCard(notify);

            // 隱私與其他
            Section("隱私與其他", out var other);
            Row(other, Green, "相", "相簿權限", null, "", PhotoLibrary.OpenSettings, out _, out _photoValue);
            RowDivider(other);
            Row(other, Green, "私", "隱私權政策", null, null, () => Application.OpenURL(PrivacyPolicyUrl), out _, out _);
            RowDivider(other);
            Row(other, Green, "約", "服務條款", null, null, () => Application.OpenURL(TermsUrl), out _, out _);
            LayoutCard(other);

            // 刪除帳號
            var delCard = CardBox(_content, "DeleteCard");
            var del = Row(delCard, Coral, "刪", "刪除帳號", "永久刪除你的資料，無法復原", null, OpenDelete, out _, out _);
            del.Find("Title").GetComponent<TMP_Text>().color = Red;
            LayoutCard(delCard);

            var ver = Place(_content, "Version", 0, 0, DesignW, 16);
            _version = Label(ver, 0, 0, DesignW, 16, "", 11, Muted2, TextAlignmentOptions.Center);
        }

        void BuildProfile()
        {
            var row = Place(_content, "Profile", PadX + 4f, 0, ContentW - 8f, 68);

            // 頭像：自訂照片，沒有就用寵物圖；右下鉛筆換頭像
            var av = Place(row, "Avatar", 0, 0, 68, 68);
            UIShapes.AddShadow(av, new Color(30f / 255f, 30f / 255f, 20f / 255f, 0.15f), 34, 10, 4);
            UIShapes.AddRounded(Place(av, "Border", 0, 0, 68, 68).gameObject, Card, 34).raycastTarget = false;
            var disc = Place(av, "Disc", 3, 3, 62, 62);
            UIShapes.AddRounded(disc.gameObject, Hex(0xFCE7A6), 31).raycastTarget = false;
            disc.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            _avatarPhoto = Place(disc, "Photo", 0, 0, 62, 62).gameObject.AddComponent<RawImage>();
            _avatarPhoto.raycastTarget = false;
            _avatarPet = Place(disc, "Pet", 62 * 0.08f, 62 * 0.18f, 62 * 0.84f, 62 * 0.76f).gameObject.AddComponent<Image>();
            _avatarPet.preserveAspect = true;
            _avatarPet.raycastTarget = false;

            var pen = Place(av, "Edit", 68 - 28 + 4, 68 - 28 + 2, 28, 28);
            var penBorder = UIShapes.AddRounded(pen.gameObject, Paper, 14);
            UIShapes.AddRounded(Place(pen, "Fill", 2, 2, 24, 24).gameObject, Ink, 12).raycastTarget = false;
            Label(pen, 0, 0, 28, 28, "換", 11, Color.white, TextAlignmentOptions.Center);
            var pb = pen.gameObject.AddComponent<Button>();
            pb.targetGraphic = penBorder;
            pb.onClick.AddListener(OnChangeAvatar);

            float x = 68 + 14;
            _name = Label(row, x, 6, ContentW - 8f - x, 30, "", 24, Ink, TextAlignmentOptions.Left);
            _name.enableWordWrapping = false;
            _name.overflowMode = TextOverflowModes.Ellipsis;
            var chip = Place(row, "Chip", x, 42, 60, 19);
            _typeChipBg = UIShapes.AddRounded(chip.gameObject, Beige, 8);
            _typeChipBg.raycastTarget = false;
            _typeChip = Label(chip, 0, 0, 0, 0, "", 11, Muted, TextAlignmentOptions.Center);
            StretchIn(_typeChip.rectTransform);
            // 帳號種類右邊的說明（Email 或「還沒綁定帳號」），x 依 chip 寬度在 Refresh 調整
            _typeSub = Label(chip, 66, 0, ContentW - 8f - x - 66, 19, "", 12, Muted, TextAlignmentOptions.Left);
            _typeSub.enableWordWrapping = false;
            _typeSub.overflowMode = TextOverflowModes.Ellipsis;
        }

        void BuildGuestCard()
        {
            _guestCard = Place(_content, "Guest", PadX, 0, ContentW, 150);
            UIShapes.AddRounded(_guestCard.gameObject, Yellow, 18).raycastTarget = false;
            UIShapes.AddRounded(Place(_guestCard, "Fill", 1.5f, 1.5f, ContentW - 3f, 147).gameObject, Hex(0xFFF1C9), 16.5f).raycastTarget = false;
            var fillRt = (RectTransform)_guestCard.GetChild(0);
            StretchIn(fillRt, 1.5f, 1.5f, 1.5f, 1.5f);

            Label(_guestCard, 14, 14, ContentW - 28f, 20, "綁定帳號，寵物才不會不見", 15, Ink, TextAlignmentOptions.Left);
            var desc = Label(_guestCard, 14, 37, ContentW - 28f, 40, "現在的資料只存在這支手機。換手機或刪掉 App，寵物和照片就找不回來。", 12, Muted, TextAlignmentOptions.TopLeft);
            desc.lineSpacing = 8f;
            float dh = Mathf.Ceil(desc.GetPreferredValues(desc.text, ContentW - 28f, 0f).y);
            desc.rectTransform.sizeDelta = new Vector2(ContentW - 28f, dh);
            float by = 37f + dh + 10f;
            float bw = (ContentW - 28f - 16f) / 3f;
            _guestButtons.Add(SmallButton(_guestCard, 14, by, bw, "Apple", Hex(0x1C1C1C), Color.white, false, BindApple));
            _guestButtons.Add(SmallButton(_guestCard, 14 + bw + 8, by, bw, "Google", Color.white, Ink, false, BindGoogle));
            _guestButtons.Add(SmallButton(_guestCard, 14 + (bw + 8) * 2, by, bw, "Email", Yellow, Ink, true, OpenBindEmail));
            _guestCard.sizeDelta = new Vector2(ContentW, by + 44f + 14f);
        }

        Button SmallButton(RectTransform parent, float x, float y, float w, string text, Color bg, Color fg, bool edge, UnityEngine.Events.UnityAction onClick)
        {
            var rt = Place(parent, text, x, y, w, 44);
            if (edge) UIShapes.AddRounded(Place(rt, "Edge", 0, 3, w, 44).gameObject, YellowEdge, 13).raycastTarget = false;
            else if (bg == Color.white) UIShapes.AddRounded(Place(rt, "Border", 0, 0, w, 44).gameObject, Line, 13).raycastTarget = false;
            var fill = bg == Color.white
                ? UIShapes.AddRounded(Place(rt, "Fill", 1.5f, 1.5f, w - 3f, 41).gameObject, bg, 11.5f)
                : UIShapes.AddRounded(Place(rt, "Fill", 0, 0, w, 44).gameObject, bg, 13);
            Label(rt, 0, 0, w, 44, text, 13, fg, TextAlignmentOptions.Center);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = fill;
            b.onClick.AddListener(onClick);
            rt.gameObject.AddComponent<ButtonScalePress>();
            return b;
        }

        RectTransform Section(string title, out RectTransform card)
        {
            var sec = Place(_content, title, PadX, 0, ContentW, 100);
            Label(sec, 6, 0, ContentW - 6f, 17, title, 12, Muted, TextAlignmentOptions.Left);
            card = CardBox(sec, "Card");
            card.anchoredPosition = new Vector2(0f, -24f);
            return sec;
        }

        RectTransform CardBox(RectTransform parent, string name)
        {
            bool inSection = parent != _content;
            var card = Place(parent, name, inSection ? 0f : PadX, 0, ContentW, 56);
            UIShapes.AddShadow(card, new Color(30f / 255f, 30f / 255f, 20f / 255f, 0.08f), 18, 10, 3);
            var fill = UIShapes.AddRounded(Place(card, "Fill", 0, 0, 0, 0).gameObject, Card, 18);
            fill.raycastTarget = false;
            StretchIn(fill.rectTransform);
            return card;
        }

        /// <summary>把卡片內的列排好、更新卡片（與所在 section）高度。前兩個子物件是陰影與底色。</summary>
        void LayoutCard(RectTransform card)
        {
            float y = 0f;
            for (int i = 2; i < card.childCount; i++)
            {
                var c = (RectTransform)card.GetChild(i);
                if (!c.gameObject.activeSelf) continue;
                c.anchoredPosition = new Vector2(c.anchoredPosition.x, -y);
                y += c.sizeDelta.y;
            }
            card.sizeDelta = new Vector2(ContentW, y);
            var parent = (RectTransform)card.parent;
            if (parent != _content) parent.sizeDelta = new Vector2(ContentW, 24f + y);
            _dirty = true;
        }

        RectTransform RowDivider(RectTransform card)
        {
            var d = Place(card, "Divider", 62, 0, ContentW - 62f, 1);
            var img = d.gameObject.AddComponent<Image>();
            img.color = Divider;
            img.raycastTarget = false;
            return d;
        }

        RectTransform Row(RectTransform card, Color iconColor, string glyph, string title, string sub, string value,
                          Action onClick, out TMP_Text subText, out TMP_Text valueText)
        {
            var row = Place(card, title, 0, 0, ContentW, 56);
            var hit = row.gameObject.AddComponent<Image>();
            hit.color = new Color(1, 1, 1, 0);
            Icon(row, iconColor, glyph);

            float right = 14f + 16f + 14f; // 右邊箭頭
            float valueW = 0f;
            valueText = null;
            if (value != null)
            {
                valueText = Label(row, 0, 0, 120, 56, value, 13, Muted, TextAlignmentOptions.Right);
                valueText.rectTransform.anchoredPosition = new Vector2(ContentW - right - 120f, 0f);
                valueText.enableWordWrapping = false;
                valueText.overflowMode = TextOverflowModes.Ellipsis;
                valueW = 128f;
            }
            float tw = ContentW - 62f - right - valueW;
            var t = Label(row, 62, sub != null ? 9 : 0, tw, sub != null ? 22 : 56, title, 15, Ink, TextAlignmentOptions.Left);
            t.gameObject.name = "Title";
            t.enableWordWrapping = false;
            subText = null;
            if (sub != null)
            {
                subText = Label(row, 62, 31, tw, 16, sub, 11, Muted2, TextAlignmentOptions.Left);
                subText.enableWordWrapping = false;
            }
            Label(row, ContentW - 14f - 16f, 0, 16, 56, ">", 14, Chevron, TextAlignmentOptions.Center);

            var b = row.gameObject.AddComponent<Button>();
            b.targetGraphic = hit;
            var colors = b.colors;
            colors.normalColor = new Color(1, 1, 1, 0);
            colors.highlightedColor = new Color(1, 1, 1, 0);
            colors.pressedColor = new Color(0.9f, 0.86f, 0.78f, 0.6f);
            colors.selectedColor = new Color(1, 1, 1, 0);
            b.colors = colors;
            hit.color = Color.white;
            b.onClick.AddListener(() => onClick?.Invoke());
            return row;
        }

        void Icon(RectTransform row, Color color, string glyph)
        {
            var tile = Place(row, "Icon", 14, 11, 34, 34);
            UIShapes.AddRounded(tile.gameObject, color, 11).raycastTarget = false;
            Label(tile, 0, 0, 34, 34, glyph, 15, Color.white, TextAlignmentOptions.Center);
        }

        void ToggleRow(RectTransform card, string title, string glyph, string key)
        {
            var row = Place(card, title, 0, 0, ContentW, 56);
            Icon(row, Orange, glyph);
            Label(row, 62, 0, ContentW - 62f - 80f, 56, title, 15, Ink, TextAlignmentOptions.Left).enableWordWrapping = false;

            var sw = Place(row, "Switch", ContentW - 14f - 52f, 12, 52, 32);
            var track = UIShapes.AddRounded(sw.gameObject, Green, 16);
            var knob = Place(sw, "Knob", 23, 3, 26, 26);
            UIShapes.AddShadow(knob, new Color(0, 0, 0, 0.2f), 13, 4, 2);
            UIShapes.AddRounded(Place(knob, "Fill", 0, 0, 26, 26).gameObject, Color.white, 13).raycastTarget = false;

            var t = new NotifyToggle { key = key, track = track, knob = knob, on = PlayerPrefs.GetInt(PrefsNotify + key, 1) == 1 };
            knob.anchoredPosition = new Vector2(t.on ? 23f : 3f, -3f);
            track.color = t.on ? Green : Hex(0xD9CFBD);
            _toggles.Add(t);
            var b = sw.gameObject.AddComponent<Button>();
            b.targetGraphic = track;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() =>
            {
                t.on = !t.on;
                PlayerPrefs.SetInt(PrefsNotify + key, t.on ? 1 : 0);
                PlayerPrefs.Save();
            });
        }

        // ─── 下方面板 ──────────────────────────────────────────────────

        Sheet NewSheet(string title, float contentH)
        {
            var s = new Sheet();
            s.layer = Stretch(_root, title + "Layer");
            s.group = s.layer.gameObject.AddComponent<CanvasGroup>();
            var dim = Stretch(s.layer, "Dim").gameObject.AddComponent<Image>();
            dim.color = new Color(20f / 255f, 24f / 255f, 14f / 255f, 0.45f);
            var sheetRef = s;
            dim.gameObject.AddComponent<Button>().onClick.AddListener(() => CloseSheet(sheetRef));

            s.panel = new GameObject(title, typeof(RectTransform)).GetComponent<RectTransform>();
            s.panel.SetParent(s.layer, false);
            s.panel.anchorMin = s.panel.anchorMax = new Vector2(0.5f, 0f);
            s.panel.pivot = new Vector2(0.5f, 0f);
            s.panel.sizeDelta = new Vector2(DesignW, contentH + 26f);
            s.contentH = contentH;

            var bg = Place(s.panel, "Panel", 0, 0, 0, 0);
            StretchIn(bg, 0, 0, 0, -30);
            UIShapes.AddShadow(bg, new Color(0, 0, 0, 0.25f), 26, 30, -10);
            StretchIn(UIShapes.AddRounded(Place(bg, "Fill", 0, 0, 0, 0).gameObject, Paper, 26).rectTransform);
            UIShapes.AddRounded(Place(s.panel, "Handle", 175, 12, 40, 5).gameObject, Line, 2.5f).raycastTarget = false;
            Label(s.panel, 18, 31, DesignW - 36f, 28, title, 21, Ink, TextAlignmentOptions.Left);
            s.content = Place(s.panel, "Content", 18, 73, DesignW - 36f, contentH - 73f);
            s.layer.gameObject.SetActive(false);
            return s;
        }

        Sheet BuildNameSheet()
        {
            // 把手 12+5、間距 14、標題 28、間距 14、欄位 71、狀態 16、間距 14、按鈕 50
            var s = NewSheet("修改名稱", 73f + 71f + 6f + 16f + 14f + 50f);
            var c = s.content;
            float w = DesignW - 36f;
            Label(c, 0, 0, w, 17, "名稱（成員會看到）", 12, Ink, TextAlignmentOptions.Left);
            _nameCount = Label(c, 0, 0, w, 17, "0 / " + NameMax, 12, Muted, TextAlignmentOptions.Right);
            _nameInput = Input(c, 0, 23, w, "", TMP_InputField.ContentType.Standard);
            _nameInput.characterLimit = NameMax;
            _nameInput.onValueChanged.AddListener(v => _nameCount.text = (v ?? "").Length + " / " + NameMax);
            s.status = Label(c, 0, 77, w, 16, "", 12, Muted, TextAlignmentOptions.Left);
            SheetButtons(s, c, 107, "儲存", SaveName);
            return s;
        }

        Sheet BuildPasswordSheet()
        {
            var s = NewSheet("修改密碼", 73f + 71f + 10f + 71f + 6f + 16f + 14f + 50f);
            var c = s.content;
            float w = DesignW - 36f;
            Label(c, 0, 0, w, 17, "新密碼（至少 " + MinPassword + " 個字）", 12, Ink, TextAlignmentOptions.Left);
            _newPw = Input(c, 0, 23, w, "", TMP_InputField.ContentType.Password);
            Label(c, 0, 81, w, 17, "再輸入一次", 12, Ink, TextAlignmentOptions.Left);
            _confirmPw = Input(c, 0, 104, w, "", TMP_InputField.ContentType.Password);
            s.status = Label(c, 0, 158, w, 16, "", 12, Muted, TextAlignmentOptions.Left);
            SheetButtons(s, c, 188, "更新密碼", SavePassword);
            return s;
        }

        Sheet BuildBindSheet()
        {
            var s = NewSheet("用 Email 綁定", 73f + 71f + 10f + 71f + 6f + 34f + 14f + 50f);
            var c = s.content;
            float w = DesignW - 36f;
            Label(c, 0, 0, w, 17, "Email", 12, Ink, TextAlignmentOptions.Left);
            _bindEmail = Input(c, 0, 23, w, "name@example.com", TMP_InputField.ContentType.EmailAddress);
            Label(c, 0, 81, w, 17, "密碼（至少 " + MinPassword + " 個字）", 12, Ink, TextAlignmentOptions.Left);
            _bindPw = Input(c, 0, 104, w, "", TMP_InputField.ContentType.Password);
            s.status = Label(c, 0, 158, w, 34, "", 12, Muted, TextAlignmentOptions.TopLeft);
            SheetButtons(s, c, 206, "綁定", BindEmail);
            return s;
        }

        void SheetButtons(Sheet s, RectTransform c, float y, string primary, Action onPrimary)
        {
            float w = DesignW - 36f;
            float cancelW = (w - 8f) / 2.5f;
            var cancel = Place(c, "Cancel", 0, y, cancelW, 50);
            var cbg = UIShapes.AddRounded(cancel.gameObject, Beige, 15);
            Label(cancel, 0, 0, cancelW, 50, "取消", 15, Ink, TextAlignmentOptions.Center);
            var cb = cancel.gameObject.AddComponent<Button>();
            cb.targetGraphic = cbg;
            cb.onClick.AddListener(() => CloseSheet(s));

            float pw = w - cancelW - 8f;
            var ok = Place(c, "Save", cancelW + 8f, y, pw, 50);
            UIShapes.AddRounded(Place(ok, "Edge", 0, 3, pw, 50).gameObject, YellowEdge, 15).raycastTarget = false;
            var obg = UIShapes.AddRounded(Place(ok, "Fill", 0, 0, pw, 50).gameObject, Yellow, 15);
            Label(ok, 0, 0, pw, 50, primary, 15, Ink, TextAlignmentOptions.Center);
            var ob = ok.gameObject.AddComponent<Button>();
            ob.targetGraphic = obg;
            ob.onClick.AddListener(() => onPrimary());
            ok.gameObject.AddComponent<ButtonScalePress>();
        }

        TMP_InputField Input(RectTransform parent, float x, float y, float w, string placeholder, TMP_InputField.ContentType type)
        {
            var rt = Place(parent, "Input", x, y, w, 48);
            rt.gameObject.SetActive(false); // 先設定好 TMP_InputField 的欄位再啟用
            UIShapes.AddRounded(rt.gameObject, Line, 13);
            var fill = UIShapes.AddRounded(Place(rt, "Fill", 2, 2, w - 4f, 44).gameObject, Color.white, 11);
            var area = Place(rt, "Text Area", 12, 0, w - 24f, 48);
            area.gameObject.AddComponent<RectMask2D>();
            var ph = Label(area, 0, 0, w - 24f, 48, placeholder, 16, new Color(Muted.r, Muted.g, Muted.b, 0.5f), TextAlignmentOptions.Left);
            var text = Label(area, 0, 0, w - 24f, 48, "", 16, Ink, TextAlignmentOptions.Left);
            ph.enableWordWrapping = text.enableWordWrapping = false;
            var input = rt.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = fill;
            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = ph;
            input.contentType = type;
            input.lineType = TMP_InputField.LineType.SingleLine;
            if (skin != null && skin.font != null) input.fontAsset = skin.font;
            rt.gameObject.SetActive(true);
            return input;
        }

        // ─── 刪除確認 ──────────────────────────────────────────────────

        void BuildDelete()
        {
            _deleteLayer = Stretch(_root, "DeleteLayer");
            _deleteGroup = _deleteLayer.gameObject.AddComponent<CanvasGroup>();
            var dim = Stretch(_deleteLayer, "Dim").gameObject.AddComponent<Image>();
            dim.color = new Color(20f / 255f, 24f / 255f, 14f / 255f, 0.55f);

            const float w = DesignW - 48f, inner = w - 36f;
            _deleteCard = new GameObject("Dialog", typeof(RectTransform)).GetComponent<RectTransform>();
            _deleteCard.SetParent(_deleteLayer, false);
            _deleteCard.anchorMin = _deleteCard.anchorMax = new Vector2(0.5f, 0.5f);
            _deleteCard.pivot = new Vector2(0.5f, 0.5f);

            // 陰影與底色先建（撐滿卡片），卡片高度最後依內容決定
            UIShapes.AddShadow(_deleteCard, new Color(0, 0, 0, 0.35f), 24, 40, 16);
            var bgRt = Place(_deleteCard, "Bg", 0, 0, 0, 0);
            StretchIn(bgRt);
            UIShapes.AddRounded(bgRt.gameObject, Paper, 24);
            var items = new List<RectTransform>();

            var icon = Place(_deleteCard, "Icon", (w - 56f) / 2f, 0, 56, 56);
            UIShapes.AddRounded(icon.gameObject, Hex(0xFCE3DC), 28).raycastTarget = false;
            Label(icon, 0, 0, 56, 56, "!", 26, Red, TextAlignmentOptions.Center);
            items.Add(icon);

            var title = Place(_deleteCard, "Title", 18, 0, inner, 28);
            Label(title, 0, 0, inner, 28, "確定要刪除帳號？", 21, Ink, TextAlignmentOptions.Center);
            items.Add(title);

            var box = Place(_deleteCard, "Box", 18, 0, inner, 100);
            UIShapes.AddRounded(box.gameObject, Card, 14).raycastTarget = false;
            var txt = Label(box, 12, 10, inner - 24f, 80,
                "・你的帳號、名稱和自己餵過的照片會<color=#C2452B>永久刪除</color>，無法復原。\n・只有你一個人的空間，寵物也會一起刪除。\n・和朋友一起的空間會留下來，其他成員可以繼續養。",
                13, Muted, TextAlignmentOptions.TopLeft);
            txt.lineSpacing = 10f;
            txt.paragraphSpacing = 6f;
            float th = Mathf.Ceil(txt.GetPreferredValues(txt.text, inner - 24f, 0f).y);
            txt.rectTransform.sizeDelta = new Vector2(inner - 24f, th);
            box.sizeDelta = new Vector2(inner, th + 20f);
            items.Add(box);

            var status = Place(_deleteCard, "Status", 18, 0, inner, 34);
            _deleteStatus = Label(status, 0, 0, inner, 34, "", 12, Red, TextAlignmentOptions.Center);
            items.Add(status);

            var keep = Place(_deleteCard, "Keep", 18, 0, inner, 53);
            UIShapes.AddRounded(Place(keep, "Edge", 0, 3, inner, 50).gameObject, YellowEdge, 15).raycastTarget = false;
            var kbg = UIShapes.AddRounded(Place(keep, "Fill", 0, 0, inner, 50).gameObject, Yellow, 15);
            Label(keep, 0, 0, inner, 50, "先不要", 15, Ink, TextAlignmentOptions.Center);
            var kb = keep.gameObject.AddComponent<Button>();
            kb.targetGraphic = kbg;
            kb.onClick.AddListener(() => CloseDelete());
            items.Add(keep);

            var go = Place(_deleteCard, "Delete", 18, 0, inner, 48);
            UIShapes.AddRounded(go.gameObject, Red, 15);
            var gbg = UIShapes.AddRounded(Place(go, "Fill", 2, 2, inner - 4f, 44).gameObject, Paper, 13);
            _deleteButtonText = Label(go, 0, 0, inner, 48, "永久刪除帳號", 15, Red, TextAlignmentOptions.Center);
            var gb = go.gameObject.AddComponent<Button>();
            gb.targetGraphic = gbg;
            gb.onClick.AddListener(ConfirmDelete);
            items.Add(go);

            // 由上往下排：上 22、間距 12、下 18
            float y = 22f;
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) y += i == items.Count - 1 ? 8f : 12f;
                items[i].anchoredPosition = new Vector2(items[i].anchoredPosition.x, -y);
                y += items[i].sizeDelta.y;
            }
            _deleteCard.sizeDelta = new Vector2(w, y + 18f);
            _deleteLayer.gameObject.SetActive(false);
        }

        void BuildToast()
        {
            _toast = Place(_design, "Toast", 20, 700, DesignW - 40f, 40);
            _toastGroup = _toast.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.blocksRaycasts = false;
            _toastGroup.alpha = 0f;
            UIShapes.AddRounded(_toast.gameObject, new Color(Ink.r, Ink.g, Ink.b, 0.92f), 20).raycastTarget = false;
            _toastText = Label(_toast, 0, 0, 0, 0, "", 13, Paper, TextAlignmentOptions.Center);
            StretchIn(_toastText.rectTransform, 16, 0, 16, 0);
        }

        void CloseButton(RectTransform parent, float x, float y, Action onClick)
        {
            var close = Place(parent, "Close", x, y, 44, 44);
            var bg = UIShapes.AddRounded(close.gameObject, Beige, 22);
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
            var b = close.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            b.onClick.AddListener(() => onClick());
        }

        void MakeBackground()
        {
            if (background == null || _bg == null) return;
            if (_blurred == null || !_blurred.IsCreated())
            {
                // 縮小成 48px 寬再放大，當作 blur(10px)
                int w = 48, h = Mathf.Max(1, Mathf.RoundToInt(48f * background.height / Mathf.Max(1, background.width)));
                var mid = RenderTexture.GetTemporary(192, Mathf.Max(1, h * 4), 0);
                mid.filterMode = FilterMode.Bilinear;
                Graphics.Blit(background, mid);
                if (_blurred == null)
                    _blurred = new RenderTexture(w, h, 0) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                Graphics.Blit(mid, _blurred);
                RenderTexture.ReleaseTemporary(mid);
            }
            _bg.texture = _blurred;
            _bg.GetComponent<AspectRatioFitter>().aspectRatio = (float)background.width / Mathf.Max(1, background.height);
        }

        // ─── UI helpers（與 HandController、RoomRingController 相同的設計座標寫法）─

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
            // Noto Sans TC Black 本身就是粗體；只有退回其他字型時才加粗
            t.fontStyle = f != null ? FontStyles.Normal : FontStyles.Bold;
            t.alignment = align;
            t.raycastTarget = false;
            t.richText = true;
            t.enableWordWrapping = true;
            return t;
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

        static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
