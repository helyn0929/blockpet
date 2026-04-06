using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Central controller for the Market page: catalog, category filter, shop grid, preview panel, currency, and purchase/equip flow.
/// </summary>
public class MarketPageController : MonoBehaviour
{
    [Header("Navigation")]
    [SerializeField] PageManager pageManager;
    [SerializeField] Button backButton;

    [Header("Core UI")]
    [SerializeField] PetPreviewPanel previewPanel;
    [SerializeField] MarketCategoryBar categoryBar;
    [SerializeField] ShopGridController shopGrid;

    [Header("Actions")]
    [SerializeField] Button tryOnButton;
    [SerializeField] Button removeAllButton;
    [SerializeField] Button buyEquipButton;

    [Header("Currency display")]
    [SerializeField] TMP_Text coinsText;
    [SerializeField] TMP_Text gemsText;

    [Header("Catalog")]
    [Tooltip("If empty at runtime, sample data is generated.")]
    [SerializeField] List<ShopItemData> catalogOverride;

    List<ShopItemData> _catalog = new List<ShopItemData>();
    ShopItemData _selected;
    MarketCategory _category = MarketCategory.Pets;
    bool _categoryBarReady;

    void Awake()
    {
        if (catalogOverride != null && catalogOverride.Count > 0)
            _catalog = new List<ShopItemData>(catalogOverride);
        else
            _catalog = MarketSampleData.CreateSampleCatalog();
        MarketSampleData.ApplyPersistenceFlags(_catalog);
    }

    void Start()
    {
        _categoryBarReady = true;
        if (categoryBar != null)
            categoryBar.Initialize(this);
        else
            OnCategorySelected(MarketCategory.Pets);

        if (backButton != null)
            backButton.onClick.AddListener(OnBackClicked);
        if (tryOnButton != null)
            tryOnButton.onClick.AddListener(OnTryOnClicked);
        if (removeAllButton != null)
            removeAllButton.onClick.AddListener(OnRemoveAllClicked);
        if (buyEquipButton != null)
            buyEquipButton.onClick.AddListener(OnBuyEquipClicked);
    }

    void OnEnable()
    {
        RefreshCatalogFlags();
        if (_categoryBarReady)
            RebuildGridFiltered();
        RefreshCurrency();
    }

    void OnDestroy()
    {
        if (backButton != null)
            backButton.onClick.RemoveListener(OnBackClicked);
        if (tryOnButton != null)
            tryOnButton.onClick.RemoveListener(OnTryOnClicked);
        if (removeAllButton != null)
            removeAllButton.onClick.RemoveListener(OnRemoveAllClicked);
        if (buyEquipButton != null)
            buyEquipButton.onClick.RemoveListener(OnBuyEquipClicked);
    }

    public void OnCategorySelected(MarketCategory cat)
    {
        _category = cat;
        RefreshCatalogFlags();
        RebuildGridFiltered();
    }

    void RebuildGridFiltered()
    {
        if (shopGrid == null)
            return;
        List<ShopItemData> filtered = _catalog.FindAll(x => x.category == _category);
        shopGrid.Rebuild(filtered, OnShopItemClicked);
    }

    void OnShopItemClicked(ShopItemData item)
    {
        _selected = item;
        if (item == null || item.category == MarketCategory.Money)
            return;
        ApplyPreview(item);
    }

    void ApplyPreview(ShopItemData item)
    {
        if (previewPanel == null || item == null) return;
        switch (item.category)
        {
            case MarketCategory.Pets:
                previewPanel.PreviewPet(item);
                break;
            case MarketCategory.Accessories:
                previewPanel.PreviewAccessory(item);
                break;
            case MarketCategory.Furnitures:
                previewPanel.PreviewFurniture(item);
                break;
            case MarketCategory.Backgrounds:
                previewPanel.PreviewBackground(item);
                break;
            case MarketCategory.Spaces:
                previewPanel.PreviewSpace(item);
                break;
        }
    }

    void OnTryOnClicked()
    {
        if (_selected == null || _selected.category == MarketCategory.Money)
            return;
        ApplyPreview(_selected);
    }

    void OnRemoveAllClicked()
    {
        previewPanel?.ClearPreview();
    }

    void OnBuyEquipClicked()
    {
        if (_selected == null) return;
        if (_selected.isLocked) return;

        if (_selected.category == MarketCategory.Money)
        {
            TryPurchaseMoneyPack(_selected);
            return;
        }

        if (_selected.isOwned)
        {
            previewPanel?.ConfirmEquip(_selected);
            RefreshCatalogFlags();
            RebuildGridFiltered();
            return;
        }

        if (!TryPurchase(_selected))
            return;

        MarketInventoryStore.SetOwned(_selected.id);
        previewPanel?.ConfirmEquip(_selected);
        RefreshCatalogFlags();
        RebuildGridFiltered();
        RefreshCurrency();
    }

    bool TryPurchase(ShopItemData item)
    {
        if (EconomyManager.Instance == null)
        {
            Debug.LogWarning("[MarketPageController] EconomyManager missing.");
            return false;
        }

        if (item.price > 0 && EconomyManager.Instance.CurrentMoney < item.price)
            return false;
        if (item.gemPrice > 0 && MarketWallet.Gems < item.gemPrice)
            return false;

        if (item.price > 0 && !EconomyManager.Instance.TrySpend(item.price))
            return false;
        if (item.gemPrice > 0 && !MarketWallet.TrySpendGems(item.gemPrice))
        {
            if (item.price > 0)
                EconomyManager.Instance.AddCoins(item.price);
            return false;
        }

        return true;
    }

    void TryPurchaseMoneyPack(ShopItemData item)
    {
        if (EconomyManager.Instance == null) return;
        if (item.price > 0 && EconomyManager.Instance.CurrentMoney < item.price)
            return;
        if (!EconomyManager.Instance.TrySpend(item.price))
            return;
        EconomyManager.Instance.AddCoins(item.grantCoins);
        MarketWallet.AddGems(item.grantGems);
        RefreshCurrency();
    }

    void OnBackClicked()
    {
        if (pageManager == null)
            pageManager = FindObjectOfType<PageManager>();
        if (pageManager != null)
            pageManager.ShowHomePage();
    }

    /// <summary>Wire a Home screen "Market" button here (or call <see cref="PageManager.ShowMarketPage"/> on PageManager).</summary>
    public void ShowMarketFromHome()
    {
        if (pageManager == null)
            pageManager = FindObjectOfType<PageManager>();
        pageManager?.ShowMarketPage();
    }

    void RefreshCatalogFlags()
    {
        MarketSampleData.ApplyPersistenceFlags(_catalog);
    }

    void RefreshCurrency()
    {
        if (coinsText != null && EconomyManager.Instance != null)
            coinsText.text = $"{EconomyManager.Instance.CurrentMoney}";
        if (gemsText != null)
            gemsText.text = $"{MarketWallet.Gems}";
    }
}
