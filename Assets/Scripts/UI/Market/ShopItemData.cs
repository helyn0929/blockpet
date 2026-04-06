using UnityEngine;

/// <summary>
/// Runtime catalog entry for a shop item. Icons/preview sprites are assigned in the Inspector on catalog assets or via sample data.
/// </summary>
[System.Serializable]
public class ShopItemData
{
    public string id;
    public string itemName;
    public MarketCategory category;
    /// <summary>Price in soft currency (coins). Ignored for pure gem / IAP rows when using gem-only fields.</summary>
    public int price;
    /// <summary>Optional gem cost. If both price and gemPrice &gt; 0, purchase requires BOTH unless you change purchase rules in MarketPageController.</summary>
    public int gemPrice;
    public Sprite icon;
    /// <summary>Optional larger sprite for pet / background preview; falls back to icon.</summary>
    public Sprite previewSprite;
    public bool isOwned;
    public bool isEquipped;
    /// <summary>Locked until level / quest (optional).</summary>
    public bool isLocked;
    /// <summary>For Money packs: coins granted on purchase.</summary>
    public int grantCoins;
    /// <summary>For Money packs: gems granted on purchase.</summary>
    public int grantGems;

    public Sprite PreviewOrIcon => previewSprite != null ? previewSprite : icon;
}
