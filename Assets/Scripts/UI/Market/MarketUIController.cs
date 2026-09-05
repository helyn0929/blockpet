using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using BlockPet.Decoration;

/// <summary>
/// UI Toolkit presentation for the Market page: header with currency, a stage showing the
/// current room + character, category tabs, an item grid and a bottom buy/equip bar.
/// Reuses the existing catalog/economy/inventory logic (<see cref="ShopItemData"/>,
/// <see cref="MarketCategory"/>, <see cref="MarketSampleData"/>, <see cref="MarketInventoryStore"/>,
/// <see cref="MarketWallet"/>, <see cref="EconomyManager"/>) unchanged.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class MarketUIController : MonoBehaviour
{
    [SerializeField] PageManager pageManager;

    [Header("Pet sprites (index matches PetCollectionManager.CurrentPetIndex)")]
    [Tooltip("Same mapping as HomeTopBar / RoomUIController. Shown on the stage when the equipped shop pet has no sprite.")]
    [SerializeField] Sprite[] petSprites;

    [Header("Catalog")]
    [Tooltip("If empty at runtime, sample data is generated.")]
    [SerializeField] List<ShopItemData> catalogOverride;
    [Tooltip("Room decorations (bear, flower, …) are listed under Furniture. If empty, the loaded DecorationDatabase is used.")]
    [SerializeField] DecorationDatabase decorationDatabase;

    const string DecorationSection = "Room Decorations";

    static readonly MarketCategory[] CategoryOrder =
    {
        MarketCategory.Pets, MarketCategory.Accessories, MarketCategory.Furnitures, MarketCategory.Money
    };

    static readonly Dictionary<MarketCategory, string> CategoryLabel = new()
    {
        { MarketCategory.Pets,        "🐾 Pets" },
        { MarketCategory.Accessories, "🎀 Accessories" },
        { MarketCategory.Furnitures,  "🛋 Furniture" },
        { MarketCategory.Money,       "💰 Shop" },
    };

    const int MaxAccessoryPreviewLayers = 4;

    UIDocument _doc;
    VisualElement _charPet, _charFurniture, _charAccessories;
    VisualElement _categoryList, _itemList;
    Label _coinsText, _gemsText, _selectedNameLabel, _selectedPriceLabel, _buyEquipLabel;
    Button _backBtn, _resetBtn, _buyEquipBtn;

    List<ShopItemData> _catalog = new List<ShopItemData>();
    ShopItemData _selected;
    MarketCategory _category = MarketCategory.Pets;
    readonly Dictionary<MarketCategory, Button> _categoryButtons = new Dictionary<MarketCategory, Button>();
    readonly List<VisualElement> _accessoryPreviewLayers = new List<VisualElement>();

    // ─── Lifecycle ─────────────────────────────────────────────────

    void Awake()
    {
        _doc = GetComponent<UIDocument>();
        if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);

        _catalog = (catalogOverride != null && catalogOverride.Count > 0)
            ? new List<ShopItemData>(catalogOverride)
            : MarketSampleData.CreateSampleCatalog();

        MergeDecorationItems();
    }

    /// <summary>
    /// Lists every <see cref="DecorationItem"/> from the room editor under the Furniture tab, ahead of the
    /// sample furniture, so the same bear/flower assets can be previewed on the stage and bought here.
    /// </summary>
    void MergeDecorationItems()
    {
        if (decorationDatabase == null)
        {
            var loaded = Resources.FindObjectsOfTypeAll<DecorationDatabase>();
            if (loaded != null && loaded.Length > 0) decorationDatabase = loaded[0];
        }
        if (decorationDatabase == null) return;

        int insertAt = _catalog.FindIndex(x => x != null && x.category == MarketCategory.Furnitures);
        if (insertAt < 0) insertAt = _catalog.Count;

        foreach (DecorationItem deco in decorationDatabase.GetAll())
        {
            if (deco == null || string.IsNullOrEmpty(deco.itemId)) continue;
            if (_catalog.Exists(x => x != null && x.id == deco.itemId)) continue;

            _catalog.Insert(insertAt++, new ShopItemData
            {
                id            = deco.itemId,
                itemName      = string.IsNullOrEmpty(deco.displayName) ? deco.itemId : deco.displayName,
                category      = MarketCategory.Furnitures,
                section       = DecorationSection,
                price         = deco.price,
                icon          = deco.icon,
                previewSprite = deco.worldSprite,
            });
        }

        MarketSampleData.ApplyPersistenceFlags(_catalog);
    }

    void OnEnable()
    {
        VisualElement root = _doc.rootVisualElement;

        _charPet            = root.Q<VisualElement>("char-pet");
        _charFurniture      = root.Q<VisualElement>("char-furniture");
        _charAccessories    = root.Q<VisualElement>("char-accessories");
        _categoryList       = root.Q<VisualElement>("category-list");
        _itemList           = root.Q<VisualElement>("item-list");
        _coinsText          = root.Q<Label>("coins-text");
        _gemsText           = root.Q<Label>("gems-text");
        _selectedNameLabel  = root.Q<Label>("selected-name");
        _selectedPriceLabel = root.Q<Label>("selected-price");
        _buyEquipLabel      = root.Q<Label>("buy-equip-label");
        _backBtn            = root.Q<Button>("btn-back");
        _resetBtn           = root.Q<Button>("btn-reset");
        _buyEquipBtn        = root.Q<Button>("btn-buyequip");

        if (_backBtn != null)     _backBtn.clicked     += OnBackClicked;
        if (_resetBtn != null)    _resetBtn.clicked    += OnResetClicked;
        if (_buyEquipBtn != null) _buyEquipBtn.clicked += OnBuyEquipClicked;

        _selected = null;
        BuildCategoryTabs();
        RestoreEquippedLook();
        SelectCategory(MarketCategory.Pets);
        UpdateActionBar();
        RefreshCurrency();
    }

    void OnDisable()
    {
        if (_backBtn != null)     _backBtn.clicked     -= OnBackClicked;
        if (_resetBtn != null)    _resetBtn.clicked    -= OnResetClicked;
        if (_buyEquipBtn != null) _buyEquipBtn.clicked -= OnBuyEquipClicked;
        _categoryButtons.Clear();
        _accessoryPreviewLayers.Clear();
    }

    // ─── Category tabs ─────────────────────────────────────────────

    void BuildCategoryTabs()
    {
        _categoryList.Clear();
        _categoryButtons.Clear();

        foreach (MarketCategory cat in CategoryOrder)
        {
            MarketCategory captured = cat;
            var btn = new Button(() => SelectCategory(captured)) { text = CategoryLabel[cat] };
            btn.AddToClassList("mk-tab");
            _categoryList.Add(btn);
            _categoryButtons[cat] = btn;
        }
    }

    public void SelectCategory(MarketCategory cat)
    {
        _category = cat;
        foreach (var kvp in _categoryButtons)
            kvp.Value.EnableInClassList("mk-tab--selected", kvp.Key == cat);

        RefreshCatalogFlags();
        RebuildItemList();

        // Keep the equipped pet visible so accessories/furniture preview on top of it.
        if (cat == MarketCategory.Accessories || cat == MarketCategory.Furnitures)
            RestoreEquippedLook();
    }

    // ─── Item grid ─────────────────────────────────────────────────

    void RebuildItemList()
    {
        _itemList.Clear();
        string lastSection = null;

        foreach (ShopItemData item in _catalog)
        {
            if (item == null || item.category != _category) continue;

            if (!string.IsNullOrEmpty(item.section) && item.section != lastSection)
            {
                lastSection = item.section;
                var header = new Label(item.section);
                header.AddToClassList("mk-section-header");
                _itemList.Add(header);
            }

            _itemList.Add(BuildItemCard(item));
        }
    }

    VisualElement BuildItemCard(ShopItemData item)
    {
        var card = new Button(() => OnItemClicked(item));
        card.AddToClassList("mk-card");
        card.EnableInClassList("mk-card--locked", item.isLocked);
        card.EnableInClassList("mk-card--equipped", item.isEquipped);
        card.SetEnabled(!item.isLocked);

        // Icon tile — falls back to the item's initial when no sprite is assigned.
        var tile = new VisualElement();
        tile.AddToClassList("mk-card-tile");
        tile.AddToClassList("mk-card-tile--" + item.category.ToString().ToLowerInvariant());
        if (item.icon != null)
        {
            tile.style.backgroundImage = new StyleBackground(item.icon);
        }
        else
        {
            var initial = new Label(string.IsNullOrEmpty(item.itemName) ? "?" : item.itemName.Substring(0, 1));
            initial.AddToClassList("mk-card-initial");
            tile.Add(initial);
        }
        card.Add(tile);

        var name = new Label(item.itemName);
        name.AddToClassList("mk-card-name");
        card.Add(name);

        var price = new Label(FormatPrice(item));
        price.AddToClassList("mk-card-price");
        price.EnableInClassList("mk-card-price--free", item.price == 0 && item.gemPrice == 0 && item.category != MarketCategory.Money);
        card.Add(price);

        string badgeText = item.isLocked ? "Locked" : item.isEquipped ? "Equipped" : item.isOwned ? "Owned" : null;
        if (badgeText != null)
        {
            var badge = new Label(badgeText);
            badge.AddToClassList("mk-card-badge");
            badge.EnableInClassList("mk-card-badge--equipped", item.isEquipped);
            badge.EnableInClassList("mk-card-badge--locked", item.isLocked);
            card.Add(badge);
        }

        return card;
    }

    static string FormatPrice(ShopItemData item)
    {
        if (item.category == MarketCategory.Money)
            return item.price > 0 ? $"{item.price} 🪙 → {item.grantGems} 💎" : $"Free → {item.grantGems} 💎";
        if (item.gemPrice > 0 && item.price > 0) return $"{item.price} 🪙 + {item.gemPrice} 💎";
        if (item.gemPrice > 0) return $"{item.gemPrice} 💎";
        return item.price > 0 ? $"{item.price} 🪙" : "Free";
    }

    void OnItemClicked(ShopItemData item)
    {
        _selected = item;
        UpdateActionBar();
        if (item.category != MarketCategory.Money)
            ApplyPreview(item);
    }

    void UpdateActionBar()
    {
        if (_selectedNameLabel != null)
            _selectedNameLabel.text = _selected != null ? _selected.itemName : "Select an item";

        if (_selectedPriceLabel != null)
            _selectedPriceLabel.text = _selected != null ? FormatPrice(_selected) : "";

        if (_buyEquipLabel != null)
        {
            if (_selected == null) _buyEquipLabel.text = "Buy";
            else if (_selected.category == MarketCategory.Money) _buyEquipLabel.text = "Purchase";
            else if (_selected.isEquipped) _buyEquipLabel.text = "Equipped";
            else if (_selected.isOwned) _buyEquipLabel.text = "Equip";
            else _buyEquipLabel.text = "Buy";
        }

        _buyEquipBtn?.SetEnabled(_selected != null && !_selected.isLocked && !_selected.isEquipped);
    }

    // ─── Stage preview (room + character) ───────────────────────────

    void RestoreEquippedLook()
    {
        ShopItemData pet = FindCatalogItem(MarketInventoryStore.GetEquippedPetId());
        Sprite sprite = pet?.PreviewOrIcon;

        if (sprite == null && petSprites != null && petSprites.Length > 0)
        {
            int petIndex = PetCollectionManager.Instance != null ? PetCollectionManager.Instance.CurrentPetIndex : 0;
            sprite = petSprites[petIndex % petSprites.Length];
        }

        SetLayerSprite(_charPet, sprite);
    }

    ShopItemData FindCatalogItem(string id) =>
        string.IsNullOrEmpty(id) ? null : _catalog.Find(x => x.id == id);

    void ApplyPreview(ShopItemData item)
    {
        switch (item.category)
        {
            case MarketCategory.Pets:        PreviewPet(item); break;
            case MarketCategory.Accessories: PreviewAccessory(item); break;
            case MarketCategory.Furnitures:  PreviewFurniture(item); break;
        }
    }

    void PreviewPet(ShopItemData item)
    {
        // Only swap the stage character when the shop item actually has art; otherwise keep the current pet.
        if (item?.PreviewOrIcon != null) SetLayerSprite(_charPet, item.PreviewOrIcon);
    }

    void PreviewFurniture(ShopItemData item) => SetLayerSprite(_charFurniture, item?.PreviewOrIcon);

    void PreviewAccessory(ShopItemData item)
    {
        if (_charAccessories == null || item?.PreviewOrIcon == null) return;

        if (_accessoryPreviewLayers.Count >= MaxAccessoryPreviewLayers)
        {
            VisualElement oldest = _accessoryPreviewLayers[0];
            _accessoryPreviewLayers.RemoveAt(0);
            oldest.RemoveFromHierarchy();
        }

        var layer = new VisualElement();
        layer.AddToClassList("mk-char-layer");
        layer.style.backgroundImage = new StyleBackground(item.PreviewOrIcon);
        _charAccessories.Add(layer);
        _accessoryPreviewLayers.Add(layer);
    }

    void ClearPreview()
    {
        foreach (VisualElement layer in _accessoryPreviewLayers)
            layer.RemoveFromHierarchy();
        _accessoryPreviewLayers.Clear();

        SetLayerSprite(_charFurniture, null);
        RestoreEquippedLook();
    }

    static void SetLayerSprite(VisualElement layer, Sprite sprite)
    {
        if (layer == null) return;
        layer.style.backgroundImage = sprite != null ? new StyleBackground(sprite) : StyleKeyword.None;
    }

    // ─── Buttons ───────────────────────────────────────────────────

    void OnBackClicked()
    {
        if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
        pageManager?.ShowHomePage();
    }

    void OnResetClicked()
    {
        _selected = null;
        ClearPreview();
        UpdateActionBar();
    }

    void OnBuyEquipClicked()
    {
        if (_selected == null || _selected.isLocked) return;

        if (_selected.category == MarketCategory.Money)
        {
            TryPurchaseMoneyPack(_selected);
            return;
        }

        if (_selected.isOwned)
        {
            ConfirmEquip(_selected);
            RefreshAfterChange();
            return;
        }

        if (!TryPurchase(_selected)) return;

        MarketInventoryStore.SetOwned(_selected.id);
        ConfirmEquip(_selected);
        RefreshAfterChange();
    }

    void RefreshAfterChange()
    {
        RefreshCatalogFlags();
        RebuildItemList();
        UpdateActionBar();
        RefreshCurrency();
    }

    void ConfirmEquip(ShopItemData item)
    {
        switch (item.category)
        {
            case MarketCategory.Pets:        MarketInventoryStore.SetEquippedPet(item.id); break;
            case MarketCategory.Furnitures:  MarketInventoryStore.SetEquippedFurniture(item.id); break;
            case MarketCategory.Accessories: MarketInventoryStore.AddEquippedAccessory(item.id); break;
        }
    }

    bool TryPurchase(ShopItemData item)
    {
        if (EconomyManager.Instance == null)
        {
            Debug.LogWarning("[MarketUIController] EconomyManager missing.");
            return false;
        }

        if (item.price > 0 && EconomyManager.Instance.CurrentMoney < item.price) return false;
        if (item.gemPrice > 0 && MarketWallet.Gems < item.gemPrice) return false;

        if (item.price > 0 && !EconomyManager.Instance.TrySpend(item.price)) return false;
        if (item.gemPrice > 0 && !MarketWallet.TrySpendGems(item.gemPrice))
        {
            if (item.price > 0) EconomyManager.Instance.AddCoins(item.price);
            return false;
        }

        return true;
    }

    void TryPurchaseMoneyPack(ShopItemData item)
    {
        if (EconomyManager.Instance == null) return;
        if (item.price > 0 && EconomyManager.Instance.CurrentMoney < item.price) return;
        if (!EconomyManager.Instance.TrySpend(item.price)) return;

        EconomyManager.Instance.AddCoins(item.grantCoins);
        MarketWallet.AddGems(item.grantGems);
        RefreshCurrency();
    }

    void RefreshCatalogFlags() => MarketSampleData.ApplyPersistenceFlags(_catalog);

    void RefreshCurrency()
    {
        if (_coinsText != null && EconomyManager.Instance != null)
            _coinsText.text = $"{EconomyManager.Instance.CurrentMoney}";
        if (_gemsText != null)
            _gemsText.text = $"{MarketWallet.Gems}";
    }
}
