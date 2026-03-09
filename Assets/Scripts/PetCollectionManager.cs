using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase.Database;
using System;
using System.Collections.Generic;

/// <summary>
/// Shared pet collection progress via Firebase. Pets 0–4 need 10 photos; 5–9 need 5.
/// Listens to SharedPets/Pet_XX/CurrentCount for real-time bar; pushes increment on photo save (when logged in).
/// </summary>
public class PetCollectionManager : MonoBehaviour
{
    public static PetCollectionManager Instance;

    const string PrefsPetIndex = "PetCollection_CurrentPetIndex";
    const string SharedPetsPath = "SharedPets";
    const string CurrentCountKey = "CurrentCount";

    /// <summary>Pets 0–4 need 10 photos; pets 5–9 need 5 photos.</summary>
    public static int GetTargetAmountForPet(int petIndex)
    {
        return petIndex < 5 ? 10 : 5;
    }

    static string PetKey(int index) => "Pet_" + index.ToString("D2");

    [Header("UI")]
    [SerializeField] Slider progressSlider;
    [SerializeField] TextMeshProUGUI progressPercentText;
    [SerializeField] TextMeshProUGUI progressCountText;
    [SerializeField] TextMeshProUGUI petNameText;

    int currentPetIndex;
    int sharedProgressCount; // from Firebase for current pet
    EventHandler<ValueChangedEventArgs> currentPetListener;
    DatabaseReference currentPetRef;
    Queue<Action> mainThreadQueue = new Queue<Action>();

    int TargetAmount => GetTargetAmountForPet(currentPetIndex);

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadFromPlayerPrefs();
        SubscribeToCurrentPet();
        RefreshAllUI();
    }

    void Update()
    {
        lock (mainThreadQueue)
        {
            while (mainThreadQueue.Count > 0)
                mainThreadQueue.Dequeue().Invoke();
        }
    }

    void Start()
    {
        RefreshAllUI();
    }

    void OnEnable()
    {
        SaveManager.OnPhotoSaved += OnPhotoSaved;
    }

    void OnDisable()
    {
        SaveManager.OnPhotoSaved -= OnPhotoSaved;
        UnsubscribeFromCurrentPet();
    }

    void LoadFromPlayerPrefs()
    {
        currentPetIndex = PlayerPrefs.GetInt(PrefsPetIndex, 0);
    }

    void SaveToPlayerPrefs()
    {
        PlayerPrefs.SetInt(PrefsPetIndex, currentPetIndex);
        PlayerPrefs.Save();
    }

    DatabaseReference GetSharedPetCountRef(int petIndex)
    {
        var root = FirebaseManager.Instance?.GetDatabaseRoot();
        if (root == null) return null;
        return root.Child(SharedPetsPath).Child(PetKey(petIndex)).Child(CurrentCountKey);
    }

    void UnsubscribeFromCurrentPet()
    {
        if (currentPetRef != null && currentPetListener != null)
        {
            currentPetRef.ValueChanged -= currentPetListener;
            currentPetRef = null;
            currentPetListener = null;
        }
    }

    void SubscribeToCurrentPet()
    {
        UnsubscribeFromCurrentPet();
        var refCount = GetSharedPetCountRef(currentPetIndex);
        if (refCount == null) return;

        currentPetRef = refCount;
        currentPetListener = (sender, args) =>
        {
            if (args.DatabaseError != null)
            {
                Debug.LogWarning("[PetCollection] Firebase error: " + args.DatabaseError.Message);
                return;
            }
            long val = 0;
            if (args.Snapshot.Exists && args.Snapshot.Value != null)
            {
                if (args.Snapshot.Value is long l) val = l;
                else if (args.Snapshot.Value is int i) val = i;
            }
            int count = (int)Mathf.Clamp(val, 0, int.MaxValue);
            lock (mainThreadQueue)
            {
                mainThreadQueue.Enqueue(() =>
                {
                    sharedProgressCount = count;
                    CheckCompletionAndAdvancePet();
                    RefreshAllUI();
                });
            }
        };
        currentPetRef.ValueChanged += currentPetListener;
    }

    void CheckCompletionAndAdvancePet()
    {
        int target = TargetAmount;
        if (target <= 0 || sharedProgressCount < target) return;

        currentPetIndex = UnityEngine.Random.Range(0, 10);
        SaveToPlayerPrefs();
        UnsubscribeFromCurrentPet();
        SubscribeToCurrentPet();
    }

    void OnPhotoSaved()
    {
        if (FirebaseManager.Instance == null || !FirebaseManager.Instance.IsLoggedIn)
            return;

        var refCount = GetSharedPetCountRef(currentPetIndex);
        if (refCount == null) return;

        refCount.RunTransaction(mutableData =>
        {
            long current = 0;
            if (mutableData.Value != null)
            {
                if (mutableData.Value is long l) current = l;
                else if (mutableData.Value is int i) current = i;
            }
            mutableData.Value = current + 1;
            return TransactionResult.Success(mutableData);
        }).ContinueWith(t =>
        {
            if (t.IsFaulted)
                Debug.LogWarning("[PetCollection] Increment failed: " + t.Exception?.Message);
        });

        RefreshAllUI();
    }

    void RefreshAllUI()
    {
        int progress = sharedProgressCount;
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
