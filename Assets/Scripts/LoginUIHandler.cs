using UnityEngine;
using System.Collections;
using TMPro;

/// <summary>
/// Login screen: Google, Apple, and Guest (Anonymous) buttons.
/// On success, shows main game and fades out the login panel.
/// </summary>
public class LoginUIHandler : MonoBehaviour
{
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
        if (economyManager != null)
            economyManager.gameObject.SetActive(visible);
        if (petCollectionManager != null)
            petCollectionManager.gameObject.SetActive(visible);
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
        // First: show main game + world background + economy / pet progress HUD
        if (mainGameUI != null)
            mainGameUI.SetActive(true);

        if (mainRoomBackground != null)
            mainRoomBackground.SetActive(true);

        SetGameplayHudVisible(true);

        // Second: lerp loginPanel CanvasGroup alpha from 1 to 0
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

        // Third: hide login panel
        if (loginPanel != null && loginPanel.gameObject != null)
            loginPanel.gameObject.SetActive(false);
    }
}
