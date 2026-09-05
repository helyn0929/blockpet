using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// UI Toolkit presentation for the Market page: stage preview (room + character) on top,
/// category tabs and item grid below. Reuses the existing catalog/economy/inventory logic
/// (<see cref="ShopItemData"/>, <see cref="MarketCategory"/>, <see cref="MarketSampleData"/>,
/// <see cref="MarketInventoryStore"/>, <see cref="MarketWallet"/>, <see cref="EconomyManager"/>).
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class MarketUIController : MonoBehaviour
{
    [SerializeField] PageManager pageManager;
    [Tooltip("If empty at runtime, sample data is generated.")]
    [SerializeField] List<ShopItemData> catalogOverride;

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

    /// <summary>Collapsed glyph shown on an unselected category tab; the full label only shows once selected.</summary>
    static readonly Dictionary<MarketCategory, string> CategoryShortLabel = new()
    {
        { MarketCategory.Pets,        "🐾" },
        { MarketCategory.Accessories, "🎀" },
        { MarketCategory.Furnitures,  "🛋" },
        { MarketCategory.Money,       "💰" },
    };

    const int MaxAccessoryPreviewLayers = 4;

    UIDocument _doc;
    VisualElement _charPet, _charFurniture, _charAccessories;
    VisualElement _categoryList, _itemList;
    Label _coinsText, _gemsText, _selectedNameLabel, _buyEquipLabel;
    Button _backBtn, _tryOnBtn, _removeAllBtn, _buyEquipBtn;

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
    }

    void OnEnable()
    {
        VisualElement root = _doc.rootVisualElement;

        _charPet         = root.Q<VisualElement>("char-pet");
        _charFurniture   = root.Q<VisualElement>("char-furniture");
        _charAccessories = root.Q<VisualElement>("char-accessories");
        _categoryList    = root.Q<VisualElement>("category-list");
        _itemList        = root.Q<VisualElement>("item-list");
        _coinsText       = root.Q<Label>("coins-text");
        _gemsText        = root.Q<Label>("gems-text");
        _selectedNameLabel = root.Q<Label>("selected-name");
        _buyEquipLabel   = root.Q<Label>("buy-equip-label");
        _backBtn         = root.Q<Button>("btn-back");
        _tryOnBtn        = root.Q<Button>("btn-tryon");
        _removeAllBtn    = root.Q<Button>("btn-removeall");
        _buyEquipBtn     = root.Q<Button>("btn-buyequip");

        if (_backBtn != null) _backBtn.clicked += OnBackClicked;
        if (_tryOnBtn != null) _tryOnBtn.clicked += OnTryOnClicked;
        if (_removeAllBtn != null) _removeAllBtn.clicked += OnRemoveAllClicked;
        if (_buyEquipBtn != null) _buyEquipBtn.clicked += OnBuyEquipClicked;

        BuildCategoryTabs();
        RestoreEquippedLook();
        SelectCategory(MarketCategory.Pets);
        RefreshCurrency();
    }

    void OnDisable()
    {
        if (_backBtn != null) _backBtn.clicked -= OnBackClicked;
        if (_tryOnBtn != null) _tryOnBtn.clicked -= OnTryOnClicked;
        if (_removeAllBtn != null) _removeAllBtn.clicked -= OnRemoveAllClicked;
        if (_buyEquipBtn != null) _buyEquipBtn.clicked -= OnBuyEquipClicked;
        _categoryButtons.Clear();
    }

    // ─── Category tabs ─────────────────────────────────────────────

    void BuildCategoryTabs()
    {
        _categoryList.Clear();
        _categoryButtons.Clear();

        foreach (MarketCategory cat in CategoryOrder)
        {
            MarketCategory captured = cat;
            var btn = new Button(() => SelectCategory(captured)) { text = CategoryShortLabel[cat] };
            btn.AddToClassList("category-tab");
            btn.AddToClassList("category-tab--" + cat.ToString().ToLowerInvariant());
            _categoryList.Add(btn);
            _categoryButtons[cat] = btn;
        }
    }

    public void SelectCategory(MarketCategory cat)
    {
        _category = cat;
        foreach (var kvp in _categoryButtons)
        {
            bool selected = kvp.Key == cat;
            kvp.Value.EnableInClassList("category-tab--selected", selected);
            kvp.Value.text = selected ? CategoryLabel[kvp.Key] : CategoryShortLabel[kvp.Key];
        }

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
                header.AddToClassList("section-header");
                _itemList.Add(header);
            }

            _itemList.Add(BuildItemCard(item));
        }
    }

    VisualElement BuildItemCard(ShopItemData item)
    {
        var card = new Button(() => OnItemClicked(item));
        card.AddToClassList("item-card");
        card.EnableInClassList("item-card--locked", item.isLocked);
        card.EnableInClassList("item-card--equipped", item.isEquipped);
        card.SetEnabled(!item.isLocked);
        card.tooltip = item.itemName;

        var icon = new VisualElement();
        icon.AddToClassList("item-card-icon");
        if (item.icon != null)
            icon.style.backgroundImage = new StyleBackground(item.icon);
        card.Add(icon);

        var price = new Label(FormatPrice(item));
        price.AddToClassList("item-card-price");
        card.Add(price);

        if (item.isOwned)
        {
            var badge = new Label("✓");
            badge.AddToClassList("item-card-badge");
            card.Add(badge);
        }
        else if (item.isLocked)
        {
            var badge = new Label("×");
            badge.AddToClassList("item-card-badge");
            badge.AddToClassList("item-card-badge--locked");
            card.Add(badge);
        }

        return card;
    }

    static string FormatPrice(ShopItemData item)
    {
        if (item.category == MarketCategory.Money)
            return item.price > 0 ? $"{item.price} 🪙 → {item.grantGems} 💎" : $"Free → {item.grantGems} 💎";
        if (item.gemPrice > 0 && item.price > 0) return $"{item.price} + {item.gemPrice} gems";
        if (item.gemPrice > 0) return $"{item.gemPrice} gems";
        return item.price > 0 ? $"{item.price} coins" : "Free";
    }

    void OnItemClicked(ShopItemData item)
    {
        _selected = item;
        RebuildItemList();
        UpdateActionBar();
        if (item.category != MarketCategory.Money)
            ApplyPreview(item);
    }

    void UpdateActionBar()
    {
        if (_selectedNameLabel != null)
            _selectedNameLabel.text = _selected != null ? _selected.itemName : "";

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
        if (pet != null) PreviewPet(pet);
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

    void PreviewPet(ShopItemData item) => SetLayerSprite(_charPet, item?.PreviewOrIcon);
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
        layer.AddToClassList("char-layer");
        layer.style.backgroundImage = new StyleBackground(item.PreviewOrIcon);
        _charAccessories.Add(layer);
        _accessoryPreviewLayers.Add(layer);
    }

    void ClearPreview()
    {
        foreach (VisualElement layer in _accessoryPreviewLayers)
            layer.RemoveFromHierarchy();
        _accessoryPreviewLayers.Clear();

        SetLayerSprite(_charPet, null);
        SetLayerSprite(_charFurniture, null);
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

    void OnTryOnClicked()
    {
        if (_selected == null || _selected.category == MarketCategory.Money) return;
        ApplyPreview(_selected);
    }

    void OnRemoveAllClicked() => ClearPreview();

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
