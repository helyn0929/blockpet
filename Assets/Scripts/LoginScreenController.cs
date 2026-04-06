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

    // Bounce animation state
    const float BounceHeight = 10f;
    const long   BounceDownMs = 300;
    const long   BounceUpMs   = 300;

    void Awake()
    {
        _doc = GetComponent<UIDocument>();
    }

    void OnEnable()
    {
        _root = _doc.rootVisualElement;

        WireButton("btn-google", OnClickGoogle);
        WireButton("btn-apple",  OnClickApple);
        WireButton("btn-email",  OnClickGuest);

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

    void OnClickGuest()
    {
        if (loginUIHandler != null) loginUIHandler.OnClickGuest();
        else if (FirebaseManager.Instance != null) FirebaseManager.Instance.SignInAnonymously();
    }

    void OnLoginSuccess(bool success)
    {
        if (!success) return;
        // Hide the UI Toolkit document — LoginUIHandler owns the fade of the uGUI CanvasGroup.
        gameObject.SetActive(false);
    }

    // ── Pet bounce animation ─────────────────────────────────────────────────

    void StartPetBounce()
    {
        var pets = _root.Query<Label>(className: "pet-emoji").ToList();
        long staggerMs = 0L;
        foreach (var pet in pets)
        {
            long capturedDelay = staggerMs;
            pet.schedule.Execute(() => ScheduleBounceLoop(pet)).StartingIn(capturedDelay);
            staggerMs += 300L;
        }
    }

    void ScheduleBounceLoop(Label label)
    {
        // Up
        label.schedule.Execute(() =>
            label.style.translate = new StyleTranslate(new Translate(0, -BounceHeight, 0))
        ).ExecuteLater(0);

        // Down
        label.schedule.Execute(() =>
            label.style.translate = new StyleTranslate(new Translate(0, 0, 0))
        ).ExecuteLater(BounceDownMs);

        // Loop
        label.schedule.Execute(() => ScheduleBounceLoop(label))
            .ExecuteLater(BounceDownMs + BounceUpMs + 600L); // 600 ms rest between bounces
    }
}
