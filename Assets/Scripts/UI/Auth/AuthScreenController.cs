using System.Collections.Generic;
using BlockPet.Hand;
using BlockPet.UI.Room;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Auth
{
    /// <summary>
    /// 帳號登入／註冊（設計稿「帳號登入／註冊」Auth.dc.html、AuthSignup、AuthLogin）：
    /// - 同一頁用分頁切換「註冊／登入」；開場選 Apple、Google 或 Email，下方「先逛逛，不登入」＝訪客登入。
    /// - Email 註冊：暱稱、Email、密碼（至少 8 個字）、同意條款，都填好按鈕才會亮。
    /// - Email 登入：密碼錯誤時紅框抖動＋提示；「忘記密碼？」寄重設信。
    /// - Apple／Google／訪客登入後還沒有暱稱 → 顯示「取個暱稱」（可略過）。
    /// 完成後呼叫 <see cref="LoginUIHandler.StartGameFlow"/> 進入選擇空間，與舊的 LoginScreenController 相同。
    /// 介面在執行時以 390×772 設計座標建立：主視覺置中在上方空間，操作區貼齊畫面底部，依螢幕寬度等比縮放。
    /// 由「BlockPet/Build Auth Screen」掛到舊登入頁的物件上，並關掉舊的 UI Toolkit 登入頁。
    /// </summary>
    public class AuthScreenController : MonoBehaviour
    {
        const float DesignW = 390f;
        const float DesignH = 772f;
        const float PadX = 22f;
        const float PanelW = DesignW - PadX * 2f;
        const float PadBottom = 26f;
        const int MinPassword = 8;

        static readonly Color Ink = Hex(0x3A2E22);
        static readonly Color Paper = Hex(0xFFF8EC);
        static readonly Color Field = new Color(1f, 0.988f, 0.957f, 0.94f);
        static readonly Color Yellow = Hex(0xF6C945);
        static readonly Color YellowEdge = Hex(0xD9A92C);
        static readonly Color Grass = Hex(0x8FA868);
        static readonly Color Muted = Hex(0x6B5D4E);
        static readonly Color ErrorBg = Hex(0xFCE3DC);
        static readonly Color ErrorFg = Hex(0x9B3420);
        static readonly Color ErrorRed = Hex(0xC2452B);
        static readonly Color InfoBg = Hex(0xE3EED9);
        static readonly Color InfoFg = Hex(0x4A7A3F);
        static readonly Color InfoGreen = Hex(0x6FA45F);

        [Header("Style")]
        [Tooltip("字型與標籤陰影材質沿用手牌（BlockPet/Build Auth Screen 會自動接上）")]
        [SerializeField] HandSkin skin;
        [Tooltip("背景：營地圖，執行時縮小模糊")]
        [SerializeField] Texture background;
        [Tooltip("第一張是主角（BowBow），朋友頭像列輪流用全部")]
        [SerializeField] Sprite[] petSprites;
        [SerializeField] int sortingOrder = 40;

        [Header("Links")]
        [SerializeField] LoginUIHandler loginUIHandler;

        enum Mode { Methods, Email, Nickname }

        // 狀態
        bool _signup = true;
        Mode _mode = Mode.Methods;
        bool _busy;
        bool _done;
        bool _agree;
        bool _showPw;
        string _pendingNick;
        float _shakeT = -1f;

        // 版面
        Canvas _canvas;
        RectTransform _root;
        RectTransform _top;
        RectTransform _bottom;
        RawImage _bg;
        RenderTexture _blurred;
        Texture2D _gradient;
        float _lastW = -1f, _lastH = -1f;
        Rect _lastSafe;
        bool _layoutDirty = true;

        // 主視覺
        RectTransform _hero;
        TMP_Text _logo;
        TMP_Text _tagline;
        RectTransform _pet;
        RectTransform _petSprite;
        RectTransform _friends;
        RectTransform _friendsRow;
        float _friendsHalfW;
        RectTransform _friendsCaption;
        RectTransform _nudge;

        // 操作區
        RectTransform _panel;
        RectTransform _tabs;
        GameObject _tabSignupOn, _tabLoginOn;
        TMP_Text _tabSignupText, _tabLoginText;
        RectTransform _titleBlock;
        TMP_Text _title, _sub;
        RectTransform _methodsError;
        TMP_Text _methodsErrorText;
        RectTransform _methods;
        TMP_Text _emailCta;
        RectTransform _terms;
        readonly List<Selectable> _methodButtons = new List<Selectable>();

        RectTransform _form;
        RectTransform _backRow, _nickRow, _emailRow, _pwRow, _formError, _forgotRow, _agreeRow, _submitRow, _skipRow;
        TMP_Text _formErrorText;
        Image _formErrorBg, _formErrorIcon;
        TMP_Text _pwHint;
        TMP_InputField _nick, _email, _pw;
        Image _pwBorder;
        RectTransform _eyeSlash;
        Image _agreeBox;
        RectTransform _agreeCheck;
        Button _submit;
        Image _submitBg, _submitEdge;
        TMP_Text _submitText;

        // ─── Lifecycle ─────────────────────────────────────────────────

        void Awake()
        {
            if (loginUIHandler == null) loginUIHandler = FindObjectOfType<LoginUIHandler>(true);
            Build();
        }

        void OnEnable()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(true);
            MakeBackground();
            _done = false;
            _busy = false;
            _pendingNick = null;
            SetMode(Mode.Methods);
            FirebaseManager.OnLoginSuccess += OnLoginResult;
        }

        void OnDisable()
        {
            FirebaseManager.OnLoginSuccess -= OnLoginResult;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_blurred != null) { _blurred.Release(); Destroy(_blurred); }
            if (_gradient != null) Destroy(_gradient);
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        void Update()
        {
            Relayout();
            float t = Time.unscaledTime;

            // 寵物上下跳（aBob 2.4s、8px）
            float bob = 8f * (0.5f - 0.5f * Mathf.Cos(t / 2.4f * Mathf.PI * 2f));
            _petSprite.anchoredPosition = new Vector2(0f, bob);

            // 朋友頭像列往右流動（aFlow 16s：-50% → 0）
            if (_friends.gameObject.activeSelf && _friendsHalfW > 0f)
                _friendsRow.anchoredPosition = new Vector2(-_friendsHalfW + _friendsHalfW * (t / 16f % 1f), _friendsRow.anchoredPosition.y);
            if (_nudge != null)
                _nudge.anchoredPosition = new Vector2(_nudgeX + 5f * (0.5f - 0.5f * Mathf.Cos(t / 1.4f * Mathf.PI * 2f)), _nudgeY);

            // 密碼錯誤：左右抖兩下（aShake 0.3s × 2）
            if (_shakeT >= 0f)
            {
                _shakeT += Time.unscaledDeltaTime;
                float p = _shakeT / 0.3f;
                float x = p >= 2f ? 0f : 5f * Mathf.Sin(p * Mathf.PI * 2f);
                var inputRt = (RectTransform)_pw.transform;
                inputRt.anchoredPosition = new Vector2(x, inputRt.anchoredPosition.y);
                if (p >= 2f) _shakeT = -1f;
            }

            RefreshSubmit();
        }

        float _nudgeX, _nudgeY;

        // ─── 狀態切換 ──────────────────────────────────────────────────

        void SetTab(bool signup)
        {
            if (_busy) return;
            _signup = signup;
            HideErrors();
            SetMode(_mode == Mode.Nickname ? Mode.Methods : _mode);
        }

        void SetMode(Mode mode)
        {
            _mode = mode;
            bool methods = mode == Mode.Methods, email = mode == Mode.Email, nick = mode == Mode.Nickname;

            _tabs.gameObject.SetActive(!nick);
            _tabSignupOn.SetActive(_signup);
            _tabLoginOn.SetActive(!_signup);
            _tabSignupText.color = _signup ? Ink : Color.white;
            _tabLoginText.color = _signup ? Color.white : Ink;

            if (nick)
            {
                _title.text = "最後一步：取個暱稱";
                _sub.text = "空間裡的成員會看到這個名字";
            }
            else if (_signup)
            {
                _title.text = "建立帳號";
                _sub.text = email ? "用 Email 建立帳號" : "選一種方式，開始養第一隻寵物";
            }
            else
            {
                _title.text = "歡迎回來";
                _sub.text = email ? "用 Email 登入" : "你的寵物都在等你";
            }

            _methods.gameObject.SetActive(methods);
            _terms.gameObject.SetActive(methods);
            _emailCta.text = _signup ? "用 Email 註冊" : "用 Email 登入";
            if (!methods) _methodsError.gameObject.SetActive(false);

            _form.gameObject.SetActive(!methods);
            _backRow.gameObject.SetActive(email);
            _nickRow.gameObject.SetActive(nick || (email && _signup));
            _emailRow.gameObject.SetActive(email);
            _pwRow.gameObject.SetActive(email);
            _forgotRow.gameObject.SetActive(email && !_signup);
            _agreeRow.gameObject.SetActive(email && _signup);
            _skipRow.gameObject.SetActive(nick);
            if (!email && !nick) _formError.gameObject.SetActive(false);

            _pwHint.text = _signup ? "至少 " + MinPassword + " 個字" : "";
            ((TMP_Text)_pw.placeholder).text = _signup ? "至少 " + MinPassword + " 個字" : "輸入密碼";
            _submitText.text = nick ? "開始" : (_signup ? "建立帳號" : "登入");

            // 主視覺：表單時縮小、藏起標語與朋友列
            bool small = !methods;
            _logo.fontSize = small ? 30f : 42f;
            _tagline.gameObject.SetActive(!small);
            _friends.gameObject.SetActive(!small);
            _friendsCaption.gameObject.SetActive(!small);
            _pet.sizeDelta = small ? new Vector2(96f, 84f) : new Vector2(140f, 124f);

            _layoutDirty = true;
        }

        void ToEmail() { if (!_busy) { HideErrors(); SetMode(Mode.Email); } }
        void BackToMethods() { if (!_busy) { HideErrors(); SetMode(Mode.Methods); } }

        // ─── 登入動作 ──────────────────────────────────────────────────

        void OnApple()
        {
            if (_busy) return;
            var fb = FirebaseManager.Instance;
            if (fb == null) { ShowMethodsError("系統還沒準備好，請稍後再試"); return; }
            SetBusy(true);
            if (loginUIHandler != null) loginUIHandler.OnClickApple();
            else fb.SignInWithApple();
        }

        void OnGoogle()
        {
            if (_busy) return;
            if (!FirebaseManager.GoogleSignInAvailable)
            {
                ShowMethodsError("這個版本還不能用 Google 登入，請改用 Apple 或 Email");
                return;
            }
            var fb = FirebaseManager.Instance;
            if (fb == null) { ShowMethodsError("系統還沒準備好，請稍後再試"); return; }
            SetBusy(true);
            if (loginUIHandler != null) loginUIHandler.OnClickGoogle();
            else fb.SignInWithGoogle();
        }

        void OnGuest()
        {
            if (_busy) return;
            var fb = FirebaseManager.Instance;
            if (fb == null) { ShowMethodsError("系統還沒準備好，請稍後再試"); return; }
            SetBusy(true);
            fb.SignInAnonymously();
        }

        void OnSubmit()
        {
            if (_busy || !IsReady()) return;
            var fb = FirebaseManager.Instance;
            if (_mode == Mode.Nickname) { SaveNickname(_nick.text.Trim()); return; }
            if (fb == null) { ShowFormError("系統還沒準備好，請稍後再試"); return; }

            string email = _email.text.Trim().ToLowerInvariant();
            string pw = _pw.text;
            if (!email.Contains("@")) { ShowFormError("Email 格式不對，再檢查一次"); return; }
            HideErrors();
            SetBusy(true);
            if (_signup)
            {
                _pendingNick = _nick.text.Trim();
                fb.CreateUserWithEmail(email, pw);
            }
            else fb.SignInWithEmail(email, pw);
        }

        void OnForgot()
        {
            if (_busy) return;
            string email = _email.text.Trim().ToLowerInvariant();
            if (!email.Contains("@")) { ShowFormError("先在上面輸入 Email，再按「忘記密碼？」"); return; }
            var fb = FirebaseManager.Instance;
            if (fb == null) { ShowFormError("系統還沒準備好，請稍後再試"); return; }
            SetBusy(true);
            fb.SendPasswordReset(email, ok =>
            {
                if (this == null) return;
                SetBusy(false);
                if (ok) ShowFormInfo("重設密碼的信已寄到 " + email + "，收信後再回來登入");
                else ShowFormError("寄送失敗，請確認 Email 是否正確");
            });
        }

        void OnLoginResult(bool success)
        {
            if (_done) return;
            if (!success)
            {
                bool wasBusy = _busy;
                SetBusy(false);
                _pendingNick = null;
                if (!wasBusy) return; // 不是這個畫面發起的（例如自動登入失敗）
                if (_mode == Mode.Email)
                {
                    if (_signup) ShowFormError("建立帳號失敗：這個 Email 可能已經註冊過了，試試「登入」");
                    else
                    {
                        ShowFormError("Email 或密碼不對，再試一次。也可以點「忘記密碼？」重設。");
                        _shakeT = 0f;
                    }
                }
                else ShowMethodsError("登入沒有完成，請再試一次");
                return;
            }

            // 登入成功（含開啟 App 時自動登入）。Firebase 可能連續通知兩次，只處理一次。
            _done = true;
            SetBusy(true);
            var fb = FirebaseManager.Instance;
            if (!string.IsNullOrEmpty(_pendingNick) && fb != null)
            {
                string nick = _pendingNick;
                _pendingNick = null;
                fb.SetDisplayName(nick, ok =>
                {
                    if (this == null) return;
                    if (ok) EnterGame();
                    else AskNickname("暱稱儲存失敗，請再試一次");
                });
                return;
            }
            if (fb == null) { EnterGame(); return; }
            fb.CheckHasNickname(has =>
            {
                if (this == null) return;
                if (has) EnterGame();
                else AskNickname(null);
            });
        }

        void AskNickname(string error)
        {
            SetBusy(false);
            _nick.text = "";
            SetMode(Mode.Nickname);
            if (error != null) ShowFormError(error);
            else HideErrors();
        }

        void SaveNickname(string name)
        {
            var fb = FirebaseManager.Instance;
            if (fb == null) { EnterGame(); return; }
            SetBusy(true);
            fb.SetDisplayName(name, ok =>
            {
                if (this == null) return;
                SetBusy(false);
                if (ok) EnterGame();
                else ShowFormError("暱稱儲存失敗，請再試一次");
            });
        }

        void EnterGame()
        {
            _done = true;
            if (loginUIHandler != null) loginUIHandler.StartGameFlow();
            else Debug.LogWarning("[AuthScreen] loginUIHandler not assigned.");
            gameObject.SetActive(false);
        }

        void SetBusy(bool busy)
        {
            _busy = busy;
            foreach (var b in _methodButtons) if (b != null) b.interactable = !busy;
        }

        bool IsReady()
        {
            if (_mode == Mode.Nickname) return _nick.text.Trim().Length > 0;
            if (_mode != Mode.Email) return false;
            bool email = _email.text.Trim().Length > 0;
            if (_signup) return _nick.text.Trim().Length > 0 && email && _pw.text.Length >= MinPassword && _agree;
            return email && _pw.text.Length > 0;
        }

        void RefreshSubmit()
        {
            if (!_form.gameObject.activeSelf) return;
            bool ready = IsReady() && !_busy;
            _submit.interactable = ready;
            _submitBg.color = ready ? Yellow : new Color(1f, 0.973f, 0.925f, 0.45f);
            _submitEdge.enabled = ready;
            _submitText.color = ready ? Ink : Color.white;
            string label = _mode == Mode.Nickname ? "開始" : (_signup ? "建立帳號" : "登入");
            _submitText.text = _busy ? "處理中…" : label;
        }

        // ─── 提示 ──────────────────────────────────────────────────────

        void ShowMethodsError(string msg)
        {
            _methodsErrorText.text = msg;
            _methodsError.gameObject.SetActive(true);
            _methodsError.sizeDelta = new Vector2(PanelW, BoxHeight(_methodsErrorText));
            _layoutDirty = true;
        }

        void ShowFormError(string msg) => ShowFormBox(msg, ErrorBg, ErrorFg, ErrorRed, "!");
        void ShowFormInfo(string msg) => ShowFormBox(msg, InfoBg, InfoFg, InfoGreen, "i");

        void ShowFormBox(string msg, Color bg, Color fg, Color icon, string glyph)
        {
            _formErrorText.text = msg;
            _formErrorText.color = fg;
            _formErrorBg.color = bg;
            _formErrorIcon.color = icon;
            _formErrorIcon.GetComponentInChildren<TMP_Text>().text = glyph;
            _formError.gameObject.SetActive(true);
            _formError.sizeDelta = new Vector2(PanelW, BoxHeight(_formErrorText));
            bool err = bg == ErrorBg && _mode == Mode.Email && !_signup;
            _pwBorder.color = err ? ErrorRed : new Color(1f, 1f, 1f, 0.85f);
            _layoutDirty = true;
        }

        void HideErrors()
        {
            _methodsError.gameObject.SetActive(false);
            _formError.gameObject.SetActive(false);
            _pwBorder.color = new Color(1f, 1f, 1f, 0.85f);
            _layoutDirty = true;
        }

        static float BoxHeight(TMP_Text text)
        {
            float h = text.GetPreferredValues(text.text, PanelW - 48f, 0f).y;
            return Mathf.Max(38f, h + 20f);
        }

        // ─── 版面 ──────────────────────────────────────────────────────

        void Relayout()
        {
            float w = _root.rect.width, h = _root.rect.height;
            var safe = Screen.safeArea;
            if (w <= 0f || h <= 0f) return;
            if (!_layoutDirty && Mathf.Approximately(w, _lastW) && Mathf.Approximately(h, _lastH) && safe == _lastSafe) return;
            _layoutDirty = false;
            _lastW = w; _lastH = h; _lastSafe = safe;

            float k = w / DesignW;
            float scale = _canvas.rootCanvas.scaleFactor;
            float px = scale > 0f ? 1f / scale : 1f;
            float topInset = (Screen.height - safe.yMax) * px / k;
            float bottomInset = safe.yMin * px / k;
            float screenH = h / k; // 設計單位

            _top.localScale = _bottom.localScale = Vector3.one * k;

            // 表單列（gap 10）→ 操作區（gap 14），從底部往上排
            if (_form.gameObject.activeSelf)
                _form.sizeDelta = new Vector2(PanelW, Stack(_form, 10f));
            float panelH = Stack(_panel, 14f);
            float panelTop = DesignH - PadBottom - bottomInset - panelH;
            _panel.anchoredPosition = new Vector2(PadX, -panelTop);
            _panel.sizeDelta = new Vector2(PanelW, panelH);

            // 主視覺置中在「安全區頂端 ～ 操作區頂端」之間（上方多留 10）
            float heroH = Stack(_hero, 4f);
            float panelTopFromScreenTop = screenH - (DesignH - panelTop);
            float space = panelTopFromScreenTop - topInset - 10f;
            float heroTop = topInset + 10f + Mathf.Max(0f, (space - heroH) / 2f);
            _hero.anchoredPosition = new Vector2(0f, -heroTop);
            _hero.sizeDelta = new Vector2(DesignW, heroH);
        }

        readonly Dictionary<Transform, float> _margins = new Dictionary<Transform, float>();

        /// <summary>把啟用中的子物件由上往下排（各自的高度＋間距＋額外上邊距），回傳總高。子物件的 x 不變。</summary>
        float Stack(RectTransform parent, float gap)
        {
            float y = 0f;
            bool first = true;
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = (RectTransform)parent.GetChild(i);
                if (!c.gameObject.activeSelf) continue;
                if (!first) y += gap;
                first = false;
                if (_margins.TryGetValue(c, out var m)) y += m;
                c.anchoredPosition = new Vector2(c.anchoredPosition.x, -y);
                y += c.sizeDelta.y;
            }
            return y;
        }

        // ─── 建立介面 ──────────────────────────────────────────────────

        void Build()
        {
            // 獨立的最上層 Canvas，不放在這個物件底下：登入頁物件在 LoginPanel 裡，
            // LoginUIHandler 把 LoginPanel 的 CanvasGroup 設成透明、不能點（原本給 UI Toolkit 用），子 Canvas 會跟著看不見。
            // 顯示／隱藏由 OnEnable／OnDisable 同步。
            var go = new GameObject("AuthScreen (runtime)", typeof(RectTransform));
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = sortingOrder;
            go.AddComponent<GraphicRaycaster>();
            _root = (RectTransform)go.transform;
            StretchIn(_root);

            // 背景：營地圖模糊＋上淡下深的暗綠漸層
            Stretch(_root, "Background").gameObject.AddComponent<Image>().color = Grass;
            _bg = Stretch(_root, "BackgroundPhoto").gameObject.AddComponent<RawImage>();
            _bg.raycastTarget = false;
            _bg.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            _gradient = MakeGradient(new Color(40f / 255f, 52f / 255f, 30f / 255f, 0.12f), new Color(40f / 255f, 52f / 255f, 30f / 255f, 0.42f));
            var grad = Stretch(_root, "Shade").gameObject.AddComponent<RawImage>();
            grad.texture = _gradient;
            grad.raycastTarget = false;

            _top = DesignRoot("Top", top: true);
            _bottom = DesignRoot("Bottom", top: false);

            var glow = Place(_top, "Glow", 45, 70, 300, 300);
            UIShapes.AddShadow(glow, new Color(1f, 0.973f, 0.925f, 0.4f), 150, 70, 0);
            UIShapes.AddRounded(Place(glow, "Disc", 0, 0, 300, 300).gameObject, new Color(1f, 0.973f, 0.925f, 0.32f), 150).raycastTarget = false;

            BuildHero();
            BuildPanel();
            SetMode(Mode.Methods);
            go.SetActive(false); // 元件啟用時（OnEnable）才顯示
        }

        RectTransform DesignRoot(string name, bool top)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_root, false);
            float y = top ? 1f : 0f;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, y);
            rt.pivot = new Vector2(0.5f, y);
            rt.sizeDelta = new Vector2(DesignW, DesignH);
            return rt;
        }

        void BuildHero()
        {
            _hero = Place(_top, "Hero", 0, 0, DesignW, 300);

            _logo = Label(_hero, 0, 0, DesignW, 46, "BlockPet", 42, Color.white, TextAlignmentOptions.Center, onScene: true);
            _tagline = Label(_hero, 0, 0, DesignW, 18, "拍照餵寵物，和家人朋友一起養", 13, Color.white, TextAlignmentOptions.Center, onScene: true);

            _pet = Place(_hero, "Pet", 0, 0, 140, 124);
            _pet.anchorMin = _pet.anchorMax = new Vector2(0.5f, 1f);
            _pet.pivot = new Vector2(0.5f, 1f);
            _margins[_pet] = 6f;
            var shadow = Place(_pet, "Shadow", 0, 0, 0, 14);
            shadow.anchorMin = new Vector2(0.18f, 0f);
            shadow.anchorMax = new Vector2(0.82f, 0f);
            shadow.pivot = new Vector2(0.5f, 0f);
            shadow.anchoredPosition = Vector2.zero;
            shadow.sizeDelta = new Vector2(0f, 14f);
            UIShapes.AddRounded(shadow.gameObject, new Color(30f / 255f, 45f / 255f, 20f / 255f, 0.3f), 7).raycastTarget = false;
            _petSprite = Place(_pet, "Sprite", 0, 0, 0, 0);
            _petSprite.anchorMin = new Vector2(0f, 0.06f);
            _petSprite.anchorMax = Vector2.one;
            _petSprite.pivot = new Vector2(0.5f, 0f);
            _petSprite.offsetMin = _petSprite.offsetMax = Vector2.zero;
            var petImg = _petSprite.gameObject.AddComponent<Image>();
            petImg.sprite = petSprites != null && petSprites.Length > 0 ? petSprites[0] : null;
            petImg.enabled = petImg.sprite != null;
            petImg.preserveAspect = true;
            petImg.raycastTarget = false;

            BuildFriends();
        }

        void BuildFriends()
        {
            // 一串朋友的寵物頭像往右走：和每個朋友各養一隻
            _friends = Place(_hero, "Friends", 0, 0, DesignW, 84);
            _margins[_friends] = 10f;
            _friends.gameObject.AddComponent<RectMask2D>();
            _friendsRow = Place(_friends, "Row", 0, 0, 0, 84);

            var colors = new[] { 0xF29A3A, 0xD78AD0, 0x6FA45F, 0xE86B4F, 0xF6C945, 0x6F8FE0 };
            var dys = new[] { 0f, 12f, -6f, 10f, -2f, 12f };
            var sizes = new[] { 56f, 50f, 58f, 52f, 56f, 50f };
            float x = 12f;
            for (int i = 0; i < 24; i++)
            {
                int b = i % 6;
                float s = sizes[b];
                var c = Place(_friendsRow, "Friend", x, (84f - s) / 2f + dys[b], s, s);
                UIShapes.AddShadow(c, new Color(30f / 255f, 40f / 255f, 20f / 255f, 0.28f), s / 2f, 10f, 4f);
                UIShapes.AddRounded(Place(c, "Border", 0, 0, s, s).gameObject, Paper, s / 2f).raycastTarget = false;
                UIShapes.AddRounded(Place(c, "Fill", 2.5f, 2.5f, s - 5f, s - 5f).gameObject, Hex(colors[b]), s / 2f - 2.5f).raycastTarget = false;
                var img = Place(c, "Pet", s * 0.14f, s * 0.16f, s * 0.72f, s * 0.7f).gameObject.AddComponent<Image>();
                img.sprite = petSprites != null && petSprites.Length > 0 ? petSprites[i % petSprites.Length] : null;
                img.enabled = img.sprite != null;
                img.preserveAspect = true;
                img.raycastTarget = false;
                x += s + 12f;
                if (i == 11) _friendsHalfW = x - 12f;
            }
            _friendsRow.sizeDelta = new Vector2(x, 84f);

            _friendsCaption = Place(_hero, "Caption", 0, 0, DesignW, 18);
            var cap = Label(_friendsCaption, 0, 0, DesignW - 28f, 18, "和每個朋友，各養一隻", 12, Color.white, TextAlignmentOptions.Center, onScene: true);
            float tw = cap.GetPreferredValues(cap.text).x;
            // 右邊的小箭頭會輕輕往右推
            _nudgeX = (DesignW - 28f) / 2f + tw / 2f + 6f;
            _nudgeY = -3f;
            _nudge = Place(_friendsCaption, "Arrow", _nudgeX, 3, 22, 12);
            var bar = Place(_nudge, "Bar", 2, 4.9f, 17, 2.2f).gameObject.AddComponent<Image>();
            bar.raycastTarget = false;
            var head = AddGraphic<UIPolygon>(Place(_nudge, "Head", 0, 0, 22, 12));
            head.raycastTarget = false;
            head.SetPoints(new Vector2(13.5f, 0.5f), new Vector2(20.5f, 6f), new Vector2(13.5f, 11.5f));
        }

        void BuildPanel()
        {
            _panel = Place(_bottom, "Panel", PadX, 0, PanelW, 400);

            // 分頁：註冊／登入
            _tabs = Place(_panel, "Tabs", 0, 0, PanelW, 52);
            UIShapes.AddRounded(_tabs.gameObject, new Color(1f, 0.973f, 0.925f, 0.3f), 18).raycastTarget = false;
            float tabW = (PanelW - 8f) / 2f;
            BuildTab(4f, tabW, "註冊", true, out _tabSignupOn, out _tabSignupText);
            BuildTab(4f + tabW, tabW, "登入", false, out _tabLoginOn, out _tabLoginText);

            _titleBlock = Place(_panel, "Title", 0, 0, PanelW, 49);
            _title = Label(_titleBlock, 0, 0, PanelW, 30, "", 24, Color.white, TextAlignmentOptions.Left, onScene: true);
            _sub = Label(_titleBlock, 0, 32, PanelW, 17, "", 12, Color.white, TextAlignmentOptions.Left, onScene: true);

            _methodsError = BuildMessageBox(_panel, out _methodsErrorText, out _, out _);
            _methodsError.gameObject.SetActive(false);

            BuildMethods();
            BuildForm();

            _terms = Place(_panel, "Terms", 0, 0, PanelW, 18);
            Label(_terms, 0, 0, PanelW, 18, "繼續即表示你同意 <u>服務條款</u> 與 <u>隱私權政策</u>", 11, Color.white, TextAlignmentOptions.Center, onScene: true);
        }

        void BuildTab(float x, float w, string text, bool signup, out GameObject on, out TMP_Text label)
        {
            var rt = Place(_tabs, signup ? "Signup" : "Login", x, 4, w, 44);
            var hit = rt.gameObject.AddComponent<Image>();
            hit.color = new Color(1, 1, 1, 0);
            // 選中的分頁才有米白底色＋陰影
            var onRt = Place(rt, "On", 0, 0, w, 44);
            UIShapes.AddShadow(onRt, new Color(30f / 255f, 30f / 255f, 20f / 255f, 0.15f), 12, 6, 2);
            UIShapes.AddRounded(Place(onRt, "Fill", 0, 0, w, 44).gameObject, Paper, 12).raycastTarget = false;
            on = onRt.gameObject;
            label = Label(rt, 0, 0, w, 44, text, 15, Ink, TextAlignmentOptions.Center);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = hit;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => SetTab(signup));
        }

        void BuildMethods()
        {
            _methods = Place(_panel, "Methods", 0, 0, PanelW, 260);
            _methodButtons.Add(PillButton(_methods, 0, "用 Apple 帳號繼續", Hex(0x1C1C1C), Color.white, false, OnApple));
            _methodButtons.Add(PillButton(_methods, 62, "用 Google 帳號繼續", Field, Ink, false, OnGoogle));
            var email = PillButton(_methods, 124, "用 Email 註冊", Yellow, Ink, true, ToEmail);
            _emailCta = email.GetComponentInChildren<TMP_Text>();
            _methodButtons.Add(email);

            var div = Place(_methods, "Divider", 0, 186, PanelW, 20);
            float half = (PanelW - 30f) / 2f;
            var l = Place(div, "L", 0, 9.5f, half, 1).gameObject.AddComponent<Image>();
            var r = Place(div, "R", PanelW - half, 9.5f, half, 1).gameObject.AddComponent<Image>();
            l.color = r.color = new Color(1, 1, 1, 0.5f);
            l.raycastTarget = r.raycastTarget = false;
            Label(div, half, 0, 30, 20, "或", 11, Color.white, TextAlignmentOptions.Center, onScene: true);

            var guest = Place(_methods, "Guest", PanelW / 2f - 80f, 216, 160, 44);
            var hit = guest.gameObject.AddComponent<Image>();
            hit.color = new Color(1, 1, 1, 0);
            Label(guest, 0, 0, 160, 44, "<u>先逛逛，不登入</u>", 13, Color.white, TextAlignmentOptions.Center, onScene: true);
            var gb = guest.gameObject.AddComponent<Button>();
            gb.targetGraphic = hit;
            gb.transition = Selectable.Transition.None;
            gb.onClick.AddListener(OnGuest);
            _methodButtons.Add(gb);
        }

        Button PillButton(RectTransform parent, float y, string text, Color bg, Color fg, bool edge, UnityEngine.Events.UnityAction onClick)
        {
            var rt = Place(parent, "Button", 0, y, PanelW, 52);
            if (edge) UIShapes.AddRounded(Place(rt, "Edge", 0, 3, PanelW, 52).gameObject, YellowEdge, 16).raycastTarget = false;
            else UIShapes.AddShadow(rt, new Color(30f / 255f, 40f / 255f, 20f / 255f, 0.22f), 16, 12, 4);
            var fill = UIShapes.AddRounded(Place(rt, "Fill", 0, 0, PanelW, 52).gameObject, bg, 16);
            Label(rt, 0, 0, PanelW, 52, text, 15, fg, TextAlignmentOptions.Center);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = fill;
            rt.gameObject.AddComponent<ButtonScalePress>();
            b.onClick.AddListener(onClick);
            return b;
        }

        void BuildForm()
        {
            _form = Place(_panel, "Form", 0, 0, PanelW, 300);

            // 其他登入方式
            _backRow = Place(_form, "Back", 0, 0, 140, 32);
            var backHit = _backRow.gameObject.AddComponent<Image>();
            backHit.color = new Color(1, 1, 1, 0);
            var chev = AddGraphic<UIPolygon>(Place(_backRow, "Chevron", 0, 9, 14, 14));
            chev.raycastTarget = false;
            chev.SetPoints(new Vector2(10f, 3f), new Vector2(10f, 11f), new Vector2(4.5f, 7f));
            Label(_backRow, 16, 0, 124, 32, "其他登入方式", 12, Color.white, TextAlignmentOptions.Left, onScene: true);
            var bb = _backRow.gameObject.AddComponent<Button>();
            bb.targetGraphic = backHit;
            bb.transition = Selectable.Transition.None;
            bb.onClick.AddListener(BackToMethods);

            _nickRow = InputRow(_form, "暱稱（成員會看到）", "例如 小美", TMP_InputField.ContentType.Standard, out _nick, out _, out _);
            _nick.characterLimit = 16;
            _emailRow = InputRow(_form, "Email", "name@example.com", TMP_InputField.ContentType.EmailAddress, out _email, out _, out _);
            _pwRow = InputRow(_form, "密碼", "", TMP_InputField.ContentType.Password, out _pw, out _pwHint, out _pwBorder);
            BuildEye();

            _formError = BuildMessageBox(_form, out _formErrorText, out _formErrorBg, out _formErrorIcon);
            _formError.gameObject.SetActive(false);

            _forgotRow = Place(_form, "Forgot", 0, 0, PanelW, 32);
            var fHit = Place(_forgotRow, "Hit", PanelW - 90f, 0, 90, 32);
            var fImg = fHit.gameObject.AddComponent<Image>();
            fImg.color = new Color(1, 1, 1, 0);
            Label(fHit, 0, 0, 90, 32, "<u>忘記密碼？</u>", 12, Color.white, TextAlignmentOptions.Right, onScene: true);
            var fb = fHit.gameObject.AddComponent<Button>();
            fb.targetGraphic = fImg;
            fb.transition = Selectable.Transition.None;
            fb.onClick.AddListener(OnForgot);

            BuildAgree();

            _submitRow = Place(_form, "Submit", 0, 0, PanelW, 55);
            _submitEdge = UIShapes.AddRounded(Place(_submitRow, "Edge", 0, 5, PanelW, 52).gameObject, YellowEdge, 16);
            _submitEdge.raycastTarget = false;
            var border = UIShapes.AddRounded(Place(_submitRow, "Border", 0, 2, PanelW, 52).gameObject, new Color(1f, 1f, 1f, 0.6f), 16);
            border.raycastTarget = false;
            _submitBg = UIShapes.AddRounded(Place(_submitRow, "Fill", 1.5f, 3.5f, PanelW - 3f, 49).gameObject, Yellow, 14.5f);
            _submitText = Label(_submitRow, 0, 2, PanelW, 52, "", 16, Ink, TextAlignmentOptions.Center);
            _submit = _submitRow.gameObject.AddComponent<Button>();
            _submit.targetGraphic = _submitBg;
            _submit.transition = Selectable.Transition.None;
            _submit.onClick.AddListener(OnSubmit);

            _skipRow = Place(_form, "Skip", PanelW / 2f - 60f, 0, 120, 32);
            var sImg = _skipRow.gameObject.AddComponent<Image>();
            sImg.color = new Color(1, 1, 1, 0);
            Label(_skipRow, 0, 0, 120, 32, "<u>之後再說</u>", 12, Color.white, TextAlignmentOptions.Center, onScene: true);
            var sb = _skipRow.gameObject.AddComponent<Button>();
            sb.targetGraphic = sImg;
            sb.transition = Selectable.Transition.None;
            sb.onClick.AddListener(() => { if (!_busy) EnterGame(); });
        }

        RectTransform InputRow(RectTransform parent, string label, string placeholder, TMP_InputField.ContentType type,
                               out TMP_InputField input, out TMP_Text hint, out Image border)
        {
            var row = Place(parent, "Row", 0, 0, PanelW, 68);
            Label(row, 0, 0, PanelW, 17, label, 12, Color.white, TextAlignmentOptions.Left, onScene: true);
            hint = Label(row, 0, 0, PanelW, 17, "", 11, Color.white, TextAlignmentOptions.Right, onScene: true);

            bool pw = type == TMP_InputField.ContentType.Password;
            var rt = Place(row, "Input", 0, 22, PanelW, 46);
            rt.gameObject.SetActive(false); // 先設定好 TMP_InputField 的欄位再啟用
            border = UIShapes.AddRounded(rt.gameObject, new Color(1f, 1f, 1f, 0.85f), 13);
            var fill = UIShapes.AddRounded(Place(rt, "Fill", 1.5f, 1.5f, PanelW - 3f, 43).gameObject, Field, 11.5f);

            float right = pw ? 56f : 12f;
            var area = Place(rt, "Text Area", 12, 0, PanelW - 12f - right, 46);
            area.gameObject.AddComponent<RectMask2D>();
            var ph = Label(area, 0, 0, PanelW - 12f - right, 46, placeholder, 15, new Color(Muted.r, Muted.g, Muted.b, 0.55f), TextAlignmentOptions.Left);
            var text = Label(area, 0, 0, PanelW - 12f - right, 46, "", 15, Ink, TextAlignmentOptions.Left);
            ph.enableWordWrapping = text.enableWordWrapping = false;

            input = rt.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = fill;
            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = ph;
            input.contentType = type;
            input.lineType = TMP_InputField.LineType.SingleLine;
            if (skin != null && skin.font != null) input.fontAsset = skin.font;
            rt.gameObject.SetActive(true);
            return row;
        }

        void BuildEye()
        {
            // 顯示／隱藏密碼：眼睛（外框＋瞳孔），顯示時加一條斜線
            var inputRt = (RectTransform)_pw.transform;
            var btn = Place(inputRt, "Eye", PanelW - 46f, 1, 44, 44);
            var hit = btn.gameObject.AddComponent<Image>();
            hit.color = new Color(1, 1, 1, 0);
            UIShapes.AddRing(Place(btn, "Outline", 11, 16, 22, 13).gameObject, Muted, 6.5f);
            UIShapes.AddRounded(Place(btn, "Pupil", 19, 19, 6, 6).gameObject, Muted, 3).raycastTarget = false;
            _eyeSlash = Place(btn, "Slash", 22 - 12, 22 - 1.1f, 24, 2.2f);
            _eyeSlash.pivot = new Vector2(0.5f, 0.5f);
            _eyeSlash.anchoredPosition = new Vector2(22f, -22f);
            _eyeSlash.localRotation = Quaternion.Euler(0, 0, -40f);
            var slash = _eyeSlash.gameObject.AddComponent<Image>();
            slash.color = Muted;
            slash.raycastTarget = false;
            _eyeSlash.gameObject.SetActive(false);
            var b = btn.gameObject.AddComponent<Button>();
            b.targetGraphic = hit;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() =>
            {
                _showPw = !_showPw;
                _pw.contentType = _showPw ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
                _pw.ForceLabelUpdate();
                _eyeSlash.gameObject.SetActive(_showPw);
            });
        }

        void BuildAgree()
        {
            _agreeRow = Place(_form, "Agree", 0, 0, PanelW, 22);
            var hit = _agreeRow.gameObject.AddComponent<Image>();
            hit.color = new Color(1, 1, 1, 0);
            var box = Place(_agreeRow, "Box", 0, 1, 20, 20);
            UIShapes.AddRounded(box.gameObject, new Color(1f, 1f, 1f, 0.9f), 5).raycastTarget = false;
            _agreeBox = UIShapes.AddRounded(Place(box, "On", 0, 0, 20, 20).gameObject, YellowEdge, 5);
            _agreeBox.raycastTarget = false;
            _agreeCheck = Place(box, "Check", 0, 0, 20, 20);
            // 勾勾：短邊 (4,10)→(8,14)、長邊 (8,14)→(16,6)，各以中心點旋轉
            foreach (var (cx, cy, w, z) in new[] { (6f, 12f, 6.5f, -45f), (12f, 10f, 12f, 45f) })
            {
                var bar = Place(_agreeCheck, "Bar", 0, 0, w, 2.4f);
                bar.pivot = new Vector2(0.5f, 0.5f);
                bar.anchoredPosition = new Vector2(cx, -cy);
                bar.localRotation = Quaternion.Euler(0, 0, z);
                var img = bar.gameObject.AddComponent<Image>();
                img.color = Color.white;
                img.raycastTarget = false;
            }
            Label(_agreeRow, 28, 0, PanelW - 28f, 22, "我已閱讀並同意 <u>服務條款</u> 與 <u>隱私權政策</u>", 12, Color.white, TextAlignmentOptions.Left, onScene: true);
            var b = _agreeRow.gameObject.AddComponent<Button>();
            b.targetGraphic = hit;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => { _agree = !_agree; RefreshAgree(); });
            RefreshAgree();
        }

        void RefreshAgree()
        {
            _agreeBox.enabled = _agree;
            _agreeCheck.gameObject.SetActive(_agree);
        }

        RectTransform BuildMessageBox(RectTransform parent, out TMP_Text text, out Image bg, out Image icon)
        {
            var rt = Place(parent, "Message", 0, 0, PanelW, 38);
            bg = UIShapes.AddRounded(rt.gameObject, ErrorBg, 13);
            bg.raycastTarget = false;
            icon = UIShapes.AddRounded(Place(rt, "Icon", 12, 11, 16, 16).gameObject, ErrorRed, 8);
            icon.raycastTarget = false;
            Label((RectTransform)icon.transform, 0, 0, 16, 16, "!", 11, Color.white, TextAlignmentOptions.Center);
            text = Label(rt, 36, 10, PanelW - 48f, 20, "", 12, ErrorFg, TextAlignmentOptions.TopLeft);
            var trt = text.rectTransform;
            trt.anchorMin = new Vector2(0f, 0f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = new Vector2(36f, 10f);
            trt.offsetMax = new Vector2(-12f, -10f);
            return rt;
        }

        void MakeBackground()
        {
            if (background == null || _bg == null) return;
            if (_blurred == null || !_blurred.IsCreated())
            {
                // 縮小成 48px 寬再放大，當作 blur(8px)
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

        static Texture2D MakeGradient(Color top, Color bottom)
        {
            const int n = 32;
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

        // ─── UI helpers（與 HandController、RoomRingController 相同的設計座標寫法）─

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
            // Noto Sans TC Black 本身就是粗體；只有退回其他字型時才加粗
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

        static void StretchIn(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
