using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Drives the UI Toolkit LoginScreen.uxml.
/// Delegates all Firebase/auth logic to the existing LoginUIHandler.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class LoginScreenController : MonoBehaviour
{
    [Header("Dependencies")]
    [Tooltip("The LoginUIHandler that owns Firebase calls and GameplayHudReleased flag.")]
    [SerializeField] LoginUIHandler loginUIHandler;

    UIDocument _doc;
    VisualElement _root;

    // Main screen mode (sign-in vs sign-up)
    bool _isSignUpMode;
    Label _welcomeTitle;
    Label _welcomeSubtitle;
    Label _signupHint;
    Label _modeToggleLabel;
    Label _mainError;
    Label _btnGoogleLabel;
    Label _btnAppleLabel;
    Label _btnEmailLabel;

    // Email overlay
    VisualElement _emailOverlay;
    TextField _emailField;
    TextField _passwordField;
    Label _emailError;
    Button _btnTabLogin;
    Button _btnTabRegister;
    Label _btnSubmitLabel;
    bool _isRegisterMode;

    // Nickname overlay
    VisualElement _nicknameOverlay;
    TextField _nicknameField;
    Label _nicknameError;

    // Jump animation state
    const float JumpHeight = 15f;
    const long  JumpUpMs   = 200;
    const long  JumpDownMs = 200;
    const long  JumpRestMs = 80;
    const long  StaggerMs  = 280;

    void Awake()
    {
        _doc = GetComponent<UIDocument>();
    }

    void OnEnable()
    {
        _root = _doc.rootVisualElement;

        WireButton("btn-google",          OnClickGoogle);
        WireButton("btn-apple",           OnClickApple);
        WireButton("btn-email",           OnClickEmailOpen);
        WireButton("btn-submit",          OnClickEmailSubmit);
        WireButton("btn-back",            OnClickEmailClose);
        WireButton("btn-nickname-submit", OnClickNicknameSubmit);
        WireButton("btn-nickname-skip",   OnClickNicknameSkip);
        WireButton("btn-tab-login",       OnClickTabLogin);
        WireButton("btn-tab-register",    OnClickTabRegister);
        WireButton("btn-mode-toggle",     OnClickModeToggle);

        _welcomeTitle    = _root.Q<Label>("welcome-title");
        _welcomeSubtitle = _root.Q<Label>("welcome-subtitle");
        _signupHint      = _root.Q<Label>("signup-hint");
        _modeToggleLabel = _root.Q<Label>("btn-mode-toggle-label");
        _mainError       = _root.Q<Label>("main-error");
        _btnGoogleLabel  = _root.Q<Label>("btn-google-label");
        _btnAppleLabel   = _root.Q<Label>("btn-apple-label");
        _btnEmailLabel   = _root.Q<Label>("btn-email-label");

        _emailOverlay    = _root.Q<VisualElement>("email-overlay");
        _emailField      = _root.Q<TextField>("email-field");
        _passwordField   = _root.Q<TextField>("password-field");
        _emailError      = _root.Q<Label>("email-error");
        _btnTabLogin     = _root.Q<Button>("btn-tab-login");
        _btnTabRegister  = _root.Q<Button>("btn-tab-register");
        _btnSubmitLabel  = _root.Q<Label>("btn-submit-label");

        _nicknameOverlay = _root.Q<VisualElement>("nickname-overlay");
        _nicknameField   = _root.Q<TextField>("nickname-field");
        _nicknameError   = _root.Q<Label>("nickname-error");

        if (_emailField != null)
        {
            _emailField.keyboardType = TouchScreenKeyboardType.EmailAddress;
            _emailField.isDelayed = false;
        }

        _isSignUpMode = false;
        StartPetBounce();
        FirebaseManager.OnLoginSuccess += OnLoginSuccess;
    }

    void OnDisable()
    {
        FirebaseManager.OnLoginSuccess -= OnLoginSuccess;
    }

    void WireButton(string name, System.Action callback)
    {
        var btn = _root.Q<Button>(name);
        if (btn != null)
            btn.clicked += callback;
        else
            Debug.LogWarning($"[LoginScreenController] Button '{name}' not found in UXML.");
    }

    void OnClickGoogle()
    {
        HideMainError();
        if (loginUIHandler != null) loginUIHandler.OnClickGoogle();
        else if (FirebaseManager.Instance != null) FirebaseManager.Instance.SignInWithGoogle();
    }

    void OnClickApple()
    {
        HideMainError();
        if (loginUIHandler != null) loginUIHandler.OnClickApple();
        else if (FirebaseManager.Instance != null) FirebaseManager.Instance.SignInWithApple();
    }

    // ── Email overlay ────────────────────────────────────────────────────────

    void OnClickModeToggle()
    {
        _isSignUpMode = !_isSignUpMode;
        if (_isSignUpMode)
        {
            if (_welcomeTitle != null)    _welcomeTitle.text    = "建立帳號";
            if (_welcomeSubtitle != null) _welcomeSubtitle.text = "加入 Blockpet，開始你的寵物冒險！";
            if (_signupHint != null)      _signupHint.text      = "已有帳號？";
            if (_modeToggleLabel != null) _modeToggleLabel.text = "登入";
            if (_btnGoogleLabel != null)  _btnGoogleLabel.text  = "使用 Google 建立帳號";
            if (_btnAppleLabel != null)   _btnAppleLabel.text   = "使用 Apple 建立帳號";
            if (_btnEmailLabel != null)   _btnEmailLabel.text   = "使用 Email 建立帳號";
        }
        else
        {
            if (_welcomeTitle != null)    _welcomeTitle.text    = "Welcome!";
            if (_welcomeSubtitle != null) _welcomeSubtitle.text = "Sign in to start your pet adventure";
            if (_signupHint != null)      _signupHint.text      = "沒有帳號？";
            if (_modeToggleLabel != null) _modeToggleLabel.text = "立即註冊";
            if (_btnGoogleLabel != null)  _btnGoogleLabel.text  = "使用 Google 登入";
            if (_btnAppleLabel != null)   _btnAppleLabel.text   = "使用 Apple 登入";
            if (_btnEmailLabel != null)   _btnEmailLabel.text   = "使用 Email 登入";
        }
    }

    void OnClickTabLogin()
    {
        _isRegisterMode = false;
        _btnTabLogin?.AddToClassList("auth-tab--active");
        _btnTabRegister?.RemoveFromClassList("auth-tab--active");
        if (_btnSubmitLabel != null) _btnSubmitLabel.text = "登入";
        if (_emailError != null) _emailError.text = "";
    }

    void OnClickTabRegister()
    {
        _isRegisterMode = true;
        _btnTabRegister?.AddToClassList("auth-tab--active");
        _btnTabLogin?.RemoveFromClassList("auth-tab--active");
        if (_btnSubmitLabel != null) _btnSubmitLabel.text = "建立帳號";
        if (_emailError != null) _emailError.text = "";
    }

    void OnClickEmailOpen()
    {
        if (_emailOverlay == null) return;
        _emailOverlay.AddToClassList("email-overlay--visible");
        if (_emailError != null) _emailError.text = "";
        if (_emailField != null) _emailField.value = "";
        if (_passwordField != null) _passwordField.value = "";
        // Mirror the main-screen mode into the email overlay.
        _isRegisterMode = _isSignUpMode;
        if (_isRegisterMode)
        {
            _btnTabRegister?.AddToClassList("auth-tab--active");
            _btnTabLogin?.RemoveFromClassList("auth-tab--active");
            if (_btnSubmitLabel != null) _btnSubmitLabel.text = "建立帳號";
        }
        else
        {
            _btnTabLogin?.AddToClassList("auth-tab--active");
            _btnTabRegister?.RemoveFromClassList("auth-tab--active");
            if (_btnSubmitLabel != null) _btnSubmitLabel.text = "登入";
        }
    }

    void OnClickEmailClose()
    {
        _emailOverlay?.RemoveFromClassList("email-overlay--visible");
    }

    void OnClickEmailSubmit()
    {
        string email = _emailField?.value?.Trim().ToLowerInvariant() ?? "";
        string pass  = _passwordField?.value ?? "";

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(pass))
        {
            if (_emailError != null) _emailError.text = "請輸入 Email 和密碼";
            return;
        }

        if (_emailError != null) _emailError.text = "";

        if (FirebaseManager.Instance == null)
        {
            if (_emailError != null) _emailError.text = "系統錯誤，請重試";
            return;
        }

        if (_isRegisterMode)
            FirebaseManager.Instance.CreateUserWithEmail(email, pass);
        else
            FirebaseManager.Instance.SignInWithEmail(email, pass);
    }

    // ── Nickname overlay ─────────────────────────────────────────────────────

    void ShowNicknameOverlay()
    {
        _emailOverlay?.RemoveFromClassList("email-overlay--visible");
        if (_nicknameField != null) _nicknameField.value = "";
        if (_nicknameError != null) _nicknameError.text = "";
        _nicknameOverlay?.AddToClassList("email-overlay--visible");
    }

    void OnClickNicknameSubmit()
    {
        string name = _nicknameField?.value?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            if (_nicknameError != null) _nicknameError.text = "請輸入暱稱";
            return;
        }

        if (_nicknameError != null) _nicknameError.text = "";

        FirebaseManager.Instance?.SetDisplayName(name, ok =>
        {
            if (ok) EnterGame();
            else if (_nicknameError != null) _nicknameError.text = "儲存失敗，請重試";
        });
    }

    void OnClickNicknameSkip()
    {
        EnterGame();
    }

    // ── Login success ────────────────────────────────────────────────────────

    void OnLoginSuccess(bool success)
    {
        if (!success)
        {
            bool emailOverlayOpen = _emailOverlay != null &&
                                    _emailOverlay.ClassListContains("email-overlay--visible");
            if (emailOverlayOpen)
            {
                if (_emailError != null)
                    _emailError.text = _isRegisterMode ? "帳號建立失敗，Email 可能已被使用" : "Email 或密碼錯誤";
            }
            else
            {
                ShowMainError("登入失敗，請再試一次");
            }
            return;
        }

        HideMainError();
        // Check RTDB for nickname — covers all auth methods including Google (which auto-populates
        // Firebase displayName from the Google profile, so HasDisplayName alone can't detect new users).
        if (FirebaseManager.Instance != null)
        {
            FirebaseManager.Instance.CheckHasNickname(hasNickname =>
            {
                if (!hasNickname)
                    ShowNicknameOverlay();
                else
                    EnterGame();
            });
        }
        else
        {
            EnterGame();
        }
    }

    void ShowMainError(string msg)
    {
        if (_mainError == null) return;
        _mainError.text = msg;
        _mainError.style.display = DisplayStyle.Flex;
    }

    void HideMainError()
    {
        if (_mainError == null) return;
        _mainError.text = "";
        _mainError.style.display = DisplayStyle.None;
    }

    void EnterGame()
    {
        // Trigger game navigation first, then hide login UI.
        // LoginUIHandler.StartGameFlow() runs FadeOutAndStartGame → RoomPage.
        // Doing this before SetActive(false) ensures the coroutine starts on an active object.
        if (loginUIHandler != null)
            loginUIHandler.StartGameFlow();
        else
            Debug.LogWarning("[LoginScreenController] loginUIHandler is not assigned — game flow will not start.");
        gameObject.SetActive(false);
    }

    // ── Pet jump animation ───────────────────────────────────────────────────

    void StartPetBounce()
    {
        var pets = _root.Query<VisualElement>(className: "pet-emoji").ToList();
        if (pets.Count == 0) return;
        ScheduleJumpSequence(pets, 0);
    }

    void ScheduleJumpSequence(System.Collections.Generic.List<VisualElement> pets, int index)
    {
        var pet = pets[index];

        pet.schedule.Execute(() =>
            pet.style.translate = new StyleTranslate(new Translate(0, -JumpHeight, 0))
        ).ExecuteLater(0);

        pet.schedule.Execute(() =>
            pet.style.translate = new StyleTranslate(new Translate(0, 0, 0))
        ).ExecuteLater(JumpUpMs);

        int next = (index + 1) % pets.Count;
        long loopDelay = (next == 0) ? JumpUpMs + JumpDownMs + JumpRestMs : StaggerMs;

        pet.schedule.Execute(() =>
            ScheduleJumpSequence(pets, next)
        ).ExecuteLater(loopDelay);
    }
}
