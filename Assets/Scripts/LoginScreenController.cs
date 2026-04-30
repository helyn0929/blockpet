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

    // Email overlay
    VisualElement _emailOverlay;
    TextField _emailField;
    TextField _passwordField;
    Label _emailError;

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

        _emailOverlay    = _root.Q<VisualElement>("email-overlay");
        _emailField      = _root.Q<TextField>("email-field");
        _passwordField   = _root.Q<TextField>("password-field");
        _emailError      = _root.Q<Label>("email-error");

        _nicknameOverlay = _root.Q<VisualElement>("nickname-overlay");
        _nicknameField   = _root.Q<TextField>("nickname-field");
        _nicknameError   = _root.Q<Label>("nickname-error");

        if (_emailField != null)
        {
            _emailField.keyboardType = TouchScreenKeyboardType.EmailAddress;
            _emailField.isDelayed = false;
        }

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
        if (loginUIHandler != null) loginUIHandler.OnClickGoogle();
        else if (FirebaseManager.Instance != null) FirebaseManager.Instance.SignInWithGoogle();
    }

    void OnClickApple()
    {
        if (loginUIHandler != null) loginUIHandler.OnClickApple();
        else if (FirebaseManager.Instance != null) FirebaseManager.Instance.SignInWithApple();
    }

    // ── Email overlay ────────────────────────────────────────────────────────

    void OnClickEmailOpen()
    {
        if (_emailOverlay == null) return;
        _emailOverlay.AddToClassList("email-overlay--visible");
        if (_emailError != null) _emailError.text = "";
        if (_emailField != null) _emailField.value = "";
        if (_passwordField != null) _passwordField.value = "";
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

        if (FirebaseManager.Instance != null)
            FirebaseManager.Instance.SignInWithEmail(email, pass);
        else if (_emailError != null)
            _emailError.text = "系統錯誤，請重試";
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
            if (_emailError != null) _emailError.text = "Email 或密碼錯誤";
            return;
        }

        // If the user has no display name yet, prompt them to set one first.
        bool needsNickname = FirebaseManager.Instance != null && !FirebaseManager.Instance.HasDisplayName;
        if (needsNickname)
            ShowNicknameOverlay();
        else
            EnterGame();
    }

    void EnterGame()
    {
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
