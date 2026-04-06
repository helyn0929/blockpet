using UnityEngine;

/// <summary>
/// Persists gem balance for the market (separate from EconomyManager coins).
/// </summary>
public static class MarketWallet
{
    const string GemsKey = "Market_Gems";

    public static int Gems => PlayerPrefs.GetInt(GemsKey, 120);

    static void SetGems(int value)
    {
        PlayerPrefs.SetInt(GemsKey, Mathf.Max(0, value));
        PlayerPrefs.Save();
    }

    public static bool TrySpendGems(int amount)
    {
        if (amount < 0 || Gems < amount)
            return false;
        SetGems(Gems - amount);
        return true;
    }

    public static void AddGems(int amount)
    {
        if (amount <= 0) return;
        SetGems(Gems + amount);
    }
}
