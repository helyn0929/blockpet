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

    // Jump animation state
    const float JumpHeight = 15f;   // px upward
    const long  JumpUpMs   = 200;   // matches USS transition-duration
    const long  JumpDownMs = 200;
    const long  JumpRestMs = 80;    // pause at bottom before next pet jumps
    const long  StaggerMs  = 280;   // delay between each pet's jump

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

        // Jump up (USS transition smooths this)
        pet.schedule.Execute(() =>
            pet.style.translate = new StyleTranslate(new Translate(0, -JumpHeight, 0))
        ).ExecuteLater(0);

        // Land back down
        pet.schedule.Execute(() =>
            pet.style.translate = new StyleTranslate(new Translate(0, 0, 0))
        ).ExecuteLater(JumpUpMs);

        // Next pet jumps after this one starts landing, then loop back
        int next = (index + 1) % pets.Count;
        long loopDelay = (next == 0) ? JumpUpMs + JumpDownMs + JumpRestMs : StaggerMs;

        pet.schedule.Execute(() =>
            ScheduleJumpSequence(pets, next)
        ).ExecuteLater(loopDelay);
    }
}
