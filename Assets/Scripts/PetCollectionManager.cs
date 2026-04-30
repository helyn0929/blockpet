using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Randomized pet collection: 10 pet types. Pets 1–5 need 10 new photos; Pets 6–10 need 5 new photos.
/// Progress is relative to startingPhotoCount. On completion, a new random pet is drawn and UI resets.
/// </summary>
public class PetCollectionManager : MonoBehaviour
{
    public static PetCollectionManager Instance;

    static string PrefsPetIndex => $"PetCollection_PetIndex_{SaveManager.Instance?.CurrentRoomId ?? "global"}";
    static string PrefsStartingCount => $"PetCollection_StartingCount_{SaveManager.Instance?.CurrentRoomId ?? "global"}";

    /// <summary>Pets 0–4 need 10 photos; pets 5–9 need 5 photos.</summary>
    static int GetTargetAmountForPet(int petIndex)
    {
        return petIndex < 5 ? 10 : 5;
    }

    [Header("UI")]
    [SerializeField] Slider progressSlider;
    [SerializeField] TextMeshProUGUI progressPercentText;
    [SerializeField] TextMeshProUGUI progressCountText;
    [SerializeField] TextMeshProUGUI petNameText;

    int currentPetIndex;
    int startingPhotoCount;

    public int CurrentPetIndex => currentPetIndex;
    public int StartingPhotoCount => startingPhotoCount;

    int CurrentPhotoCount => SaveManager.Instance != null && SaveManager.Instance.data?.photos != null
        ? SaveManager.Instance.data.photos.Count
        : 0;

    int CurrentProgress => Mathf.Max(0, CurrentPhotoCount - startingPhotoCount);
    int TargetAmount => GetTargetAmountForPet(currentPetIndex);

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        // Keep under a Canvas so progress slider / TMP still render. DontDestroyOnLoad + SetParent(null) breaks Screen Space UI.
        Canvas hostCanvas = GetComponentInParent<Canvas>();
        if (hostCanvas != null)
            transform.SetParent(hostCanvas.transform, false);
        else
        {
            Canvas any = FindObjectOfType<Canvas>();
            if (any != null)
                transform.SetParent(any.transform, false);
            else if (transform.parent != null)
                transform.SetParent(null);
        }
        DontDestroyOnLoad(gameObject);

        // Load persisted state first so album progress continues after restart
        LoadFromPlayerPrefs();
        EnsureValidState();
        RefreshAllUI();
    }

    void Start()
    {
        // Re-validate after SaveManager has loaded so CurrentPhotoCount is correct; save if state changed
        EnsureValidState();
        RefreshAllUI();
    }

    void OnEnable()
    {
        SaveManager.OnPhotoSaved += OnPhotoSaved;
        SaveManager.OnBeforeRoomSwitch += SaveToPlayerPrefs;
        SaveManager.OnRoomSwitched += OnRoomChanged;
    }

    void OnDisable()
    {
        SaveManager.OnPhotoSaved -= OnPhotoSaved;
        SaveManager.OnBeforeRoomSwitch -= SaveToPlayerPrefs;
        SaveManager.OnRoomSwitched -= OnRoomChanged;
    }

    void OnRoomChanged()
    {
        LoadFromPlayerPrefs();
        EnsureValidState();
        RefreshAllUI();
    }

    /// <summary>Called by FirebaseManager when Firebase pet state arrives for this room.</summary>
    public void ApplyRoomPetState(int petIndex, int startingCount)
    {
        currentPetIndex = petIndex;
        startingPhotoCount = startingCount;
        SaveToPlayerPrefs();
        RefreshAllUI();
    }

    void LoadFromPlayerPrefs()
    {
        currentPetIndex = PlayerPrefs.GetInt(PrefsPetIndex, 0);
        startingPhotoCount = PlayerPrefs.GetInt(PrefsStartingCount, 0);
    }

    /// <summary>Call whenever currentPetIndex or startingPhotoCount change so progress persists after restart.</summary>
    void SaveToPlayerPrefs()
    {
        PlayerPrefs.SetInt(PrefsPetIndex, currentPetIndex);
        PlayerPrefs.SetInt(PrefsStartingCount, startingPhotoCount);
        PlayerPrefs.Save();
    }

    /// <summary>Ensure we're not past target (e.g. after load) and complete pets until we're on a valid one.</summary>
    void EnsureValidState()
    {
        int total = CurrentPhotoCount;
        int progress = CurrentProgress;
        int target = TargetAmount;

        while (progress >= target && target > 0)
        {
            // Complete current pet and draw next
            currentPetIndex = Random.Range(0, 10);
            startingPhotoCount = total;
            SaveToPlayerPrefs();
            PublishToRoomIfShared();
            progress = CurrentProgress;
            target = TargetAmount;
        }
    }

    void OnPhotoSaved()
    {
        int total = CurrentPhotoCount;
        int progress = CurrentProgress;
        int target = TargetAmount;

        if (progress >= target && target > 0)
        {
            // Pet completed: random new pet, offset from current count
            currentPetIndex = Random.Range(0, 10);
            startingPhotoCount = total;
            SaveToPlayerPrefs();
            PublishToRoomIfShared();
        }

        RefreshAllUI();
    }

    void PublishToRoomIfShared()
    {
        if (FirebaseManager.Instance == null) return;
        if (!FirebaseManager.Instance.HasRoomPetState) return;
        FirebaseManager.Instance.PublishRoomPetProgress(currentPetIndex, startingPhotoCount);
    }

    void RefreshAllUI()
    {
        int progress = CurrentProgress;
        int target = TargetAmount;

        // Slider (0–1)
        if (progressSlider != null)
        {
            progressSlider.minValue = 0f;
            progressSlider.maxValue = 1f;
            progressSlider.value = target > 0 ? Mathf.Clamp01((float)progress / target) : 0f;
        }

        // Percentage
        if (progressPercentText != null)
        {
            float pct = target > 0 ? (100f * progress / target) : 0f;
            progressPercentText.text = $"{Mathf.FloorToInt(pct)}%";
        }

        // Progress count e.g. "3 / 10"
        if (progressCountText != null)
            progressCountText.text = $"{progress} / {target}";

        // Dynamic name by current pet index (Dog for 0–4, Cat for 5–9); updates immediately when pet changes
        if (petNameText != null)
            petNameText.text = GetPetDisplayName(currentPetIndex);
    }

    /// <summary>Display name by current pet index: 0–4 = Dog, 5–9 = Cat.</summary>
    static string GetPetDisplayName(int petIndex)
    {
        return petIndex >= 5 ? "Cat" : "Dog";
    }
}
