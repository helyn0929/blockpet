using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 寵物進化進度（2026-10-29 定案）：集滿 <see cref="EvolveTarget"/> 張照片就「可進化」，
/// 從空間的第一張照片開始算（空間裡每個人餵的都算），集滿後停在滿格，等進化系統（開發順序第 10 步）做好再處理。
/// 以前的佔位寫法「集滿就隨機換一種寵物、進度歸零」已拿掉；房間裡舊的 startingPhotoCount 會被歸零（之前的照片要算）。
/// startingPhotoCount 留著給之後進化／換寵物時用。
/// </summary>
public class PetCollectionManager : MonoBehaviour
{
    public static PetCollectionManager Instance;

    static string PrefsPetIndex => $"PetCollection_PetIndex_{SaveManager.Instance?.CurrentRoomId ?? ""}";
    static string PrefsStartingCount => $"PetCollection_StartingCount_{SaveManager.Instance?.CurrentRoomId ?? ""}";

    /// <summary>集滿幾張照片可以進化（每種寵物都一樣）。</summary>
    public const int EvolveTarget = 100;

    static int GetTargetAmountForPet(int petIndex) => EvolveTarget;

    [Header("UI")]
    [SerializeField] Slider progressSlider;
    [SerializeField] TextMeshProUGUI progressPercentText;
    [SerializeField] TextMeshProUGUI progressCountText;
    [SerializeField] TextMeshProUGUI petNameText;

    int currentPetIndex;
    int startingPhotoCount;

    public int CurrentPetIndex => currentPetIndex;
    public int StartingPhotoCount => startingPhotoCount;

    /// <summary>目前寵物的收集進度（0～100），給新版頂部狀態欄的進化條用。</summary>
    public float ProgressPercent => TargetAmount > 0 ? Mathf.Clamp(100f * CurrentProgress / TargetAmount, 0f, 100f) : 0f;

    /// <summary>
    /// 進化條各色段（0～100，加起來＝ProgressPercent）：目前這隻寵物開始後餵的照片，依辨識類別分段。
    /// categories 是色段順序；辨識前的舊照片沒有類別，算在第一段（和以前整段一色一樣）。
    /// </summary>
    public float[] EnergyByCategory(System.Collections.Generic.IList<string> categories) =>
        EnergyFor(SaveManager.Instance != null ? SaveManager.Instance.data?.photos : null, startingPhotoCount, currentPetIndex, categories);

    /// <summary>任一空間的進化條色段（選擇空間轉盤用）：照片清單、這隻寵物從第幾張開始算、寵物種類。</summary>
    public static float[] EnergyFor(System.Collections.Generic.IList<PhotoMeta> photos, int startingPhotoCount, int petIndex,
                                    System.Collections.Generic.IList<string> categories)
    {
        var result = new float[categories.Count];
        int target = GetTargetAmountForPet(petIndex);
        if (photos == null || target <= 0 || result.Length == 0) return result;
        float each = 100f / target;
        float sum = 0f;
        for (int i = Mathf.Max(0, startingPhotoCount); i < photos.Count && sum < 100f; i++)
        {
            int c = photos[i] != null && !string.IsNullOrEmpty(photos[i].category) ? categories.IndexOf(photos[i].category) : -1;
            float add = Mathf.Min(each, 100f - sum);
            result[c >= 0 ? c : 0] += add;
            sum += add;
        }
        return result;
    }

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
        // No DontDestroyOnLoad: the game is a single scene (Feed.unity) and this object must stay
        // under the Canvas to render, so the call only produced a "root GameObjects" warning.

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
        EnsureValidState();
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

    /// <summary>
    /// 進化系統做好之前，進度一律從空間的第一張照片算：舊的佔位邏輯留下的 startingPhotoCount 歸零，並寫回房間讓大家一致。
    /// </summary>
    void EnsureValidState()
    {
        if (startingPhotoCount == 0) return;
        startingPhotoCount = 0;
        SaveToPlayerPrefs();
        PublishToRoomIfShared();
    }

    void OnPhotoSaved() => RefreshAllUI();

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

    /// <summary>目前寵物的名字：空間資訊裡取的名字優先（全空間共用）；還沒取名才用預設的種類名。</summary>
    public string GetPetDisplayName()
    {
        string named = FirebaseManager.Instance != null ? FirebaseManager.Instance.CurrentPetName : null;
        return !string.IsNullOrEmpty(named) ? named : GetPetDisplayName(currentPetIndex);
    }

    static string GetPetDisplayName(int petIndex)
    {
        return petIndex >= 5 ? "Cat" : "Dog";
    }
}
