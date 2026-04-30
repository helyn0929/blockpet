using UnityEngine;
using TMPro;
using System.Collections;

/// <summary>
/// Manages the shared room coin wallet.
/// Balance is stored in Firebase (Rooms/{roomId}/economy/coins) and kept in
/// sync across all room members via FirebaseManager's listener.
/// </summary>
public class EconomyManager : MonoBehaviour
{
    public static EconomyManager Instance;

    [Header("Currency")]
    [SerializeField] int moneyPerPhoto = 50;

    [Header("UI")]
    [SerializeField] TextMeshProUGUI moneyText;

    [Header("Smooth text")]
    [SerializeField] float countUpDuration = 0.4f;

    int currentMoney;
    int displayedMoney;
    Coroutine countUpRoutine;

    public int CurrentMoney => currentMoney;

    /// <summary>Spend coins from the shared wallet. Returns false if insufficient balance.</summary>
    public bool TrySpend(int amount)
    {
        if (amount <= 0) return true;
        if (currentMoney < amount) return false;
        currentMoney -= amount;
        FirebaseManager.Instance?.WriteRoomCoins(currentMoney);
        if (countUpRoutine != null) StopCoroutine(countUpRoutine);
        displayedMoney = currentMoney;
        RefreshMoneyUI();
        return true;
    }

    /// <summary>Add coins to the shared wallet (e.g. refund).</summary>
    public void AddCoins(int amount)
    {
        if (amount <= 0) return;
        FirebaseManager.Instance?.AddRoomCoins(amount);
        // UI updates via Firebase listener (SetRoomBalance).
    }

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        Canvas hostCanvas = GetComponentInParent<Canvas>();
        if (hostCanvas != null)
            transform.SetParent(hostCanvas.transform, false);
        else
        {
            Canvas any = FindObjectOfType<Canvas>();
            if (any != null) transform.SetParent(any.transform, false);
            else if (transform.parent != null) transform.SetParent(null);
        }
        DontDestroyOnLoad(gameObject);

        displayedMoney = 0;
        RefreshMoneyUI();
    }

    void OnEnable()
    {
        SaveManager.OnPhotoSaved += AddMoneyFromPhoto;
    }

    void OnDisable()
    {
        SaveManager.OnPhotoSaved -= AddMoneyFromPhoto;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Called by FirebaseManager listener when the shared balance changes.</summary>
    public void SetRoomBalance(int coins)
    {
        int previous = displayedMoney;
        currentMoney = coins;

        if (moneyText != null && Mathf.Abs(coins - previous) > 0)
        {
            if (countUpRoutine != null) StopCoroutine(countUpRoutine);
            countUpRoutine = StartCoroutine(CountUpMoneyRoutine(previous, coins));
        }
        else
        {
            displayedMoney = currentMoney;
            RefreshMoneyUI();
        }
    }

    void AddMoneyFromPhoto()
    {
        FirebaseManager.Instance?.AddRoomCoins(moneyPerPhoto);
        // UI updates via Firebase listener (HandleRoomCoinsChanged → SetRoomBalance).
    }

    IEnumerator CountUpMoneyRoutine(int from, int to)
    {
        float elapsed = 0f;
        displayedMoney = from;
        RefreshMoneyUI();

        while (elapsed < countUpDuration)
        {
            if (moneyText == null) yield break;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / countUpDuration);
            t = 1f - (1f - t) * (1f - t);
            displayedMoney = (int)Mathf.Lerp(from, to, t);
            RefreshMoneyUI();
            yield return null;
        }

        if (moneyText != null)
        {
            displayedMoney = to;
            RefreshMoneyUI();
        }
        countUpRoutine = null;
    }

    void RefreshMoneyUI()
    {
        if (moneyText != null)
            moneyText.text = $"$ {displayedMoney} coin";
    }

    public void UpdateWalletUI() => RefreshMoneyUI();

    public void SetMoneyText(TextMeshProUGUI text)
    {
        moneyText = text;
        displayedMoney = currentMoney;
        RefreshMoneyUI();
    }
}
