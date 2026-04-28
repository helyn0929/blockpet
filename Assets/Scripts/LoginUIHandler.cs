using UnityEngine;
using System.Collections;
using TMPro;

/// <summary>
/// Login screen: Google, Apple, and Guest (Anonymous) buttons.
/// On success, shows main game and fades out the login panel.
/// </summary>
public class LoginUIHandler : MonoBehaviour
{
    /// <summary>True after login flow has shown the economy / pet HUD. PageManager uses this so wallet stays hidden during login even on Home.</summary>
    public static bool GameplayHudReleased { get; private set; }

    [Header("UI References")]
    public CanvasGroup loginPanel;
    public GameObject mainGameUI;
    public GameObject mainRoomBackground;
    public TextMeshProUGUI statusText;
    public GameObject loadingIcon;

    [Header("Gameplay HUD (hidden until login succeeds)")]
    [Tooltip("Optional override. If empty, finds EconomyManager in the scene.")]
    [SerializeField] EconomyManager economyManager;
    [Tooltip("Optional override. If empty, finds PetCollectionManager in the scene.")]
    [SerializeField] PetCollectionManager petCollectionManager;

    [Header("Settings")]
    public float fadeDuration = 0.5f;

    void Start()
    {
        if (loginPanel != null)
            loginPanel.alpha = 1f;

        if (mainGameUI != null)
            mainGameUI.SetActive(false);

        if (mainRoomBackground != null)
            mainRoomBackground.SetActive(false);

        if (loadingIcon != null)
            loadingIcon.SetActive(false);

        GameplayHudReleased = false;
        ResolveHudManagers();
        SetGameplayHudVisible(false);
    }

    void ResolveHudManagers()
    {
        if (economyManager == null)
            economyManager = FindObjectOfType<EconomyManager>();
        if (petCollectionManager == null)
            petCollectionManager = FindObjectOfType<PetCollectionManager>();
    }

    void SetGameplayHudVisible(bool visible)
    {
        if (visible)
            GameplayHudReleased = true;

        if (economyManager != null)
            economyManager.gameObject.SetActive(visible);
        if (petCollectionManager != null)
            petCollectionManager.gameObject.SetActive(visible);
    }

    /// <summary>
    /// Called after the player has selected / joined a room. This is the point where gameplay UI is allowed to appear.
    /// </summary>
    public void EnterMainGame()
    {
        // Show main game + world background + economy / pet progress HUD
        if (mainGameUI != null)
            mainGameUI.SetActive(true);

        if (mainRoomBackground != null)
            mainRoomBackground.SetActive(true);

        ResolveHudManagers();
        SetGameplayHudVisible(true);
    }

    void OnEnable()
    {
        FirebaseManager.OnLoginSuccess += OnLoginSuccess;
    }

    void OnDisable()
    {
        FirebaseManager.OnLoginSuccess -= OnLoginSuccess;
    }

    void OnDestroy()
    {
        FirebaseManager.OnLoginSuccess -= OnLoginSuccess;
    }

    public void OnClickGoogle()
    {
        if (statusText != null) statusText.text = "Connecting...";
        if (loadingIcon != null) loadingIcon.SetActive(true);
        if (FirebaseManager.Instance != null)
            FirebaseManager.Instance.SignInWithGoogle();
        else
        {
            if (loadingIcon != null) loadingIcon.SetActive(false);
            Debug.LogWarning("[LoginUIHandler] FirebaseManager.Instance is null.");
        }
    }

    public void OnClickApple()
    {
        if (statusText != null) statusText.text = "Connecting...";
        if (loadingIcon != null) loadingIcon.SetActive(true);
        if (FirebaseManager.Instance != null)
            FirebaseManager.Instance.SignInWithApple();
        else
        {
            if (loadingIcon != null) loadingIcon.SetActive(false);
            Debug.LogWarning("[LoginUIHandler] FirebaseManager.Instance is null.");
        }
    }

    public void OnClickGuest()
    {
        if (statusText != null) statusText.text = "Connecting...";
        if (loadingIcon != null) loadingIcon.SetActive(true);
        if (FirebaseManager.Instance != null)
            FirebaseManager.Instance.SignInAnonymously();
        else
        {
            if (loadingIcon != null) loadingIcon.SetActive(false);
            Debug.LogWarning("[LoginUIHandler] FirebaseManager.Instance is null.");
        }
    }

    // Invoked on Main Thread by FirebaseManager (thread-safe for UI updates)
    void OnLoginSuccess(bool success)
    {
        if (loadingIcon != null) loadingIcon.SetActive(false);
        if (success)
        {
            if (statusText != null) statusText.text = "Login Successful!";

            if (AvatarManager.Instance != null)
                AvatarManager.Instance.LoadAvatarFromSave();

            StartCoroutine(PostLoginFlow());
        }
    }

    IEnumerator PostLoginFlow()
    {
        // First-time user: prompt them to pick an avatar before entering the game.
        if (AvatarManager.Instance != null && !AvatarManager.Instance.HasAvatar)
        {
            if (statusText != null) statusText.text = "Choose your avatar!";

            bool pickFinished = false;
            AvatarManager.Instance.PickAvatarFromGallery((picked) =>
            {
                pickFinished = true;
            });

            // Wait until the gallery picker completes (selected or cancelled).
            while (!pickFinished)
                yield return null;
        }

        yield return StartCoroutine(FadeOutAndStartGame());
    }

    IEnumerator FadeOutAndStartGame()
    {
        // Fade out login first. Do NOT show gameplay yet — player must choose a room.
        if (loginPanel != null && fadeDuration > 0f)
        {
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                loginPanel.alpha = Mathf.Clamp01(1f - elapsed / fadeDuration);
                yield return null;
            }
            loginPanel.alpha = 0f;
        }
        else if (loginPanel != null)
            loginPanel.alpha = 0f;

        // Hide login panel.
        // IMPORTANT: if this LoginUIHandler is attached to the same GameObject as `loginPanel`,
        // deactivating it will stop coroutines and prevent post-login routing from completing.
        if (loginPanel != null)
        {
            loginPanel.blocksRaycasts = false;
            loginPanel.interactable = false;
            if (loginPanel.gameObject != null && loginPanel.gameObject != gameObject)
                loginPanel.gameObject.SetActive(false);
        }

        // Show the page system container so RoomPage (a child page) can render,
        // but keep gameplay visuals/HUD hidden until a room is selected.
        if (mainGameUI != null)
            mainGameUI.SetActive(true);
        if (mainRoomBackground != null)
            mainRoomBackground.SetActive(false);
        ResolveHudManagers();
        SetGameplayHudVisible(false);

        // Finally: go to Room selection page (join/create) before entering gameplay.
        var pageManager = FindObjectOfType<PageManager>(true);
        if (pageManager != null)
        {
            // Defensive: if InitialPage or other scripts toggled pages earlier, clear first.
            pageManager.HideAllPages();
            pageManager.ShowRoomPage();
            Debug.Log("[LoginUIHandler] Post-login: requested ShowRoomPage()");
            // If this component lives under LoginPanel, the panel may be deactivated right after fade-out.
            // Run the "re-assert room page" coroutine on PageManager which stays active.
            pageManager.StartCoroutine(ForceRoomPageNextFrame(pageManager));
        }
    }

    IEnumerator ForceRoomPageNextFrame(PageManager pageManager)
    {
        // Some scripts can still toggle pages on the same frame when MainGame is activated.
        // Re-assert RoomPage after a short delay to make the first screen deterministic.
        yield return null;
        yield return null;
        if (pageManager != null && !GameplayHudReleased)
        {
            Debug.Log("[LoginUIHandler] Post-login: re-asserting ShowRoomPage() (HUD not released yet)");
            pageManager.HideAllPages();
            pageManager.ShowRoomPage();
        }
    }
}
