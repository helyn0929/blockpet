using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Drives the UI Toolkit LoginScreen.uxml.
/// Email/password are directly on screen. No overlay needed for email auth.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class LoginScreenController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] LoginUIHandler loginUIHandler;

    UIDocument _doc;
    VisualElement _root;

    // Main screen
    TextField _emailField;
    TextField _passwordField;
    Label _mainError;
    Label _btnSubmitLabel;

    // Sign up overlay
    VisualElement _signupOverlay;
    TextField _signupEmailField;
    TextField _signupPasswordField;
    Label _signupError;

    // Nickname overlay
    VisualElement _nicknameOverlay;
    TextField _nicknameField;
    Label _nicknameError;

    void Awake()
    {
        _doc = GetComponent<UIDocument>();
    }

    void OnEnable()
    {
        _root = _doc.rootVisualElement;

        // Login screen
        WireButton("btn-submit",          OnClickLoginSubmit);
        WireButton("btn-google",          OnClickLoginGoogle);
        WireButton("btn-apple",           OnClickLoginApple);
        WireButton("btn-goto-signup",     OnClickGotoSignup);

        // Sign up screen
        WireButton("btn-signup-submit",   OnClickSignupSubmit);
        WireButton("btn-signup-google",   OnClickSignupGoogle);
        WireButton("btn-signup-apple",    OnClickSignupApple);
        WireButton("btn-back-to-login",   OnClickBackToLogin);

        // Nickname overlay
        WireButton("btn-nickname-submit", OnClickNicknameSubmit);
        WireButton("btn-nickname-skip",   OnClickNicknameSkip);

        _emailField        = _root.Q<TextField>("email-field");
        _passwordField     = _root.Q<TextField>("password-field");
        _mainError         = _root.Q<Label>("main-error");

        _signupOverlay     = _root.Q<VisualElement>("signup-overlay");
        _signupEmailField  = _root.Q<TextField>("signup-email-field");
        _signupPasswordField = _root.Q<TextField>("signup-password-field");
        _signupError       = _root.Q<Label>("signup-error");

        _nicknameOverlay   = _root.Q<VisualElement>("nickname-overlay");
        _nicknameField     = _root.Q<TextField>("nickname-field");
        _nicknameError     = _root.Q<Label>("nickname-error");

        if (_emailField != null)
        {
            _emailField.keyboardType = TouchScreenKeyboardType.EmailAddress;
            _emailField.isDelayed = false;
        }
        if (_signupEmailField != null)
        {
            _signupEmailField.keyboardType = TouchScreenKeyboardType.EmailAddress;
            _signupEmailField.isDelayed = false;
        }

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

    // ── Login screen ──────────────────────────────────────────────────────────

    void OnClickLoginSubmit()
    {
        string email = _emailField?.value?.Trim().ToLowerInvariant() ?? "";
        string pass  = _passwordField?.value ?? "";

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(pass))
        {
            ShowMainError("請輸入 Email 和密碼");
            return;
        }

        HideMainError();
        if (FirebaseManager.Instance != null)
            FirebaseManager.Instance.SignInWithEmail(email, pass);
        else
            ShowMainError("系統錯誤，請重試");
    }

    void OnClickLoginGoogle()
    {
        HideMainError();
        if (loginUIHandler != null) loginUIHandler.OnClickGoogle();
        else FirebaseManager.Instance?.SignInWithGoogle();
    }

    void OnClickLoginApple()
    {
        HideMainError();
        if (loginUIHandler != null) loginUIHandler.OnClickApple();
        else FirebaseManager.Instance?.SignInWithApple();
    }

    void OnClickGotoSignup()
    {
        HideMainError();
        if (_signupEmailField != null) _signupEmailField.value = "";
        if (_signupPasswordField != null) _signupPasswordField.value = "";
        if (_signupError != null) _signupError.text = "";
        _signupOverlay?.AddToClassList("signup-overlay--visible");
    }

    // ── Sign up screen ────────────────────────────────────────────────────────

    void OnClickSignupSubmit()
    {
        string email = _signupEmailField?.value?.Trim().ToLowerInvariant() ?? "";
        string pass  = _signupPasswordField?.value ?? "";

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(pass))
        {
            if (_signupError != null) ShowError(_signupError, "請輸入 Email 和密碼");
            return;
        }

        if (_signupError != null) _signupError.text = "";
        if (FirebaseManager.Instance != null)
            FirebaseManager.Instance.CreateUserWithEmail(email, pass);
        else if (_signupError != null)
            ShowError(_signupError, "系統錯誤，請重試");
    }

    void OnClickSignupGoogle()
    {
        if (loginUIHandler != null) loginUIHandler.OnClickGoogle();
        else FirebaseManager.Instance?.SignInWithGoogle();
    }

    void OnClickSignupApple()
    {
        if (loginUIHandler != null) loginUIHandler.OnClickApple();
        else FirebaseManager.Instance?.SignInWithApple();
    }

    void OnClickBackToLogin()
    {
        _signupOverlay?.RemoveFromClassList("signup-overlay--visible");
    }

    // ── Nickname overlay ──────────────────────────────────────────────────────

    void ShowNicknameOverlay()
    {
        _signupOverlay?.RemoveFromClassList("signup-overlay--visible");
        if (_nicknameField != null) _nicknameField.value = "";
        if (_nicknameError != null) _nicknameError.text  = "";
        _nicknameOverlay?.AddToClassList("overlay--visible");
    }

    void OnClickNicknameSubmit()
    {
        string name = _nicknameField?.value?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            if (_nicknameError != null) ShowError(_nicknameError, "請輸入暱稱");
            return;
        }
        if (_nicknameError != null) _nicknameError.text = "";
        FirebaseManager.Instance?.SetDisplayName(name, ok =>
        {
            if (ok) EnterGame();
            else if (_nicknameError != null) ShowError(_nicknameError, "儲存失敗，請重試");
        });
    }

    void OnClickNicknameSkip() => EnterGame();

    // ── Login result ──────────────────────────────────────────────────────────

    void OnLoginSuccess(bool success)
    {
        if (!success)
        {
            bool isSignup = _signupOverlay != null &&
                            _signupOverlay.ClassListContains("signup-overlay--visible");
            if (isSignup)
                ShowError(_signupError, "帳號建立失敗，Email 可能已被使用");
            else
                ShowMainError("Email 或密碼錯誤");
            return;
        }

        HideMainError();
        if (FirebaseManager.Instance != null)
            FirebaseManager.Instance.CheckHasNickname(has => { if (!has) ShowNicknameOverlay(); else EnterGame(); });
        else
            EnterGame();
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

    void ShowError(Label label, string msg)
    {
        if (label == null) return;
        label.text = msg;
        label.style.display = DisplayStyle.Flex;
    }

    void EnterGame()
    {
        if (loginUIHandler != null)
            loginUIHandler.StartGameFlow();
        else
            Debug.LogWarning("[LoginScreenController] loginUIHandler not assigned.");
        gameObject.SetActive(false);
    }
}
