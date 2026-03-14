using UnityEngine;
using TMPro;
using System.Collections;

public class EconomyManager : MonoBehaviour
{
    public static EconomyManager Instance;

    const string PlayerPrefsKey = "Economy_Money";

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

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadMoney();
        displayedMoney = currentMoney;
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
        if (Instance == this)
            Instance = null;
    }

    void LoadMoney()
    {
        currentMoney = PlayerPrefs.GetInt(PlayerPrefsKey, 0);
    }

    void SaveMoney()
    {
        PlayerPrefs.SetInt(PlayerPrefsKey, currentMoney);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Called when a photo is saved (e.g. via SaveManager.OnPhotoSaved).
    /// Adds money and updates the money UI with a smooth count-up.
    /// </summary>
    public void AddMoneyFromPhoto()
    {
        int previous = currentMoney;
        currentMoney += moneyPerPhoto;
        SaveMoney();

        if (moneyText != null)
        {
            if (countUpRoutine != null)
                StopCoroutine(countUpRoutine);
            countUpRoutine = StartCoroutine(CountUpMoneyRoutine(previous, currentMoney));
        }
        else
        {
            displayedMoney = currentMoney;
            RefreshMoneyUI();
        }
    }

    IEnumerator CountUpMoneyRoutine(int from, int to)
    {
        float elapsed = 0f;
        displayedMoney = from;
        RefreshMoneyUI();

        while (elapsed < countUpDuration)
        {
            if (moneyText == null)
                yield break;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / countUpDuration);
            t = 1f - (1f - t) * (1f - t); // ease-out quadratic
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

    /// <summary>Updates the wallet display (format: "$ 100 coin"). Call from UI or other scripts if needed.</summary>
    public void UpdateWalletUI()
    {
        RefreshMoneyUI();
    }

    /// <summary>
    /// Optional: set the money display at runtime (e.g. if UI is created later).
    /// </summary>
    public void SetMoneyText(TextMeshProUGUI text)
    {
        moneyText = text;
        displayedMoney = currentMoney;
        RefreshMoneyUI();
    }
}
