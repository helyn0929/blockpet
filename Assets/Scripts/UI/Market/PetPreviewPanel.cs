using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pet dressing-room preview: background, pet sprite, layered accessories, optional furniture, space overlay.
/// Preview is temporary until the player confirms equip via <see cref="MarketPageController"/>.
/// </summary>
public class PetPreviewPanel : MonoBehaviour
{
    [Header("Layers (assign in Inspector)")]
    [SerializeField] Image backgroundImage;
    [SerializeField] Image spaceTintImage;
    [SerializeField] Image petImage;
    [SerializeField] Transform accessoryLayerRoot;
    [SerializeField] Transform furnitureLayerRoot;
    [Tooltip("Optional: normalized height of this panel (0–1 of parent). Applied on Enable if parent has RectTransform.")]
    [SerializeField] [Range(0.2f, 0.7f)] float previewHeightPercentOfScreen = 0.4f;

    [Header("Accessory stacking")]
    [SerializeField] int maxAccessoryPreviewLayers = 4;

    readonly List<Image> _accessoryPool = new List<Image>();
    Image _furnitureImage;
    ShopItemData _previewPet;
    ShopItemData _previewBackground;
    ShopItemData _previewSpace;
    ShopItemData _previewFurniture;

    void OnEnable()
    {
        ApplyPreviewPanelHeight();
    }

    void ApplyPreviewPanelHeight()
    {
        RectTransform rt = transform as RectTransform;
        if (rt == null || rt.parent is not RectTransform parent)
            return;
        Canvas.ForceUpdateCanvases();
        float parentH = parent.rect.height;
        if (parentH < 1f) return;
        float target = Mathf.Clamp(parentH * previewHeightPercentOfScreen, 120f, parentH * 0.85f);
        LayoutElement le = GetComponent<LayoutElement>();
        if (le == null) le = gameObject.AddComponent<LayoutElement>();
        le.minHeight = target;
        le.preferredHeight = target;
        le.flexibleHeight = 0f;
    }

    public void PreviewPet(ShopItemData item)
    {
        _previewPet = item;
        if (petImage == null) return;
        Sprite s = item != null ? item.PreviewOrIcon : null;
        petImage.sprite = s;
        petImage.enabled = s != null;
        petImage.preserveAspect = true;
    }

    public void PreviewBackground(ShopItemData item)
    {
        _previewBackground = item;
        if (backgroundImage == null) return;
        Sprite s = item != null ? item.PreviewOrIcon : null;
        backgroundImage.sprite = s;
        backgroundImage.enabled = s != null;
        backgroundImage.preserveAspect = true;
    }

    public void PreviewSpace(ShopItemData item)
    {
        _previewSpace = item;
        if (spaceTintImage == null) return;
        if (item == null || item.PreviewOrIcon == null)
        {
            spaceTintImage.enabled = false;
            return;
        }
        spaceTintImage.sprite = item.PreviewOrIcon;
        spaceTintImage.enabled = true;
        spaceTintImage.preserveAspect = true;
        spaceTintImage.color = Color.white;
    }

    public void PreviewFurniture(ShopItemData item)
    {
        _previewFurniture = item;
        if (furnitureLayerRoot == null) return;
        if (_furnitureImage == null)
        {
            var go = new GameObject("FurniturePreview", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(furnitureLayerRoot, false);
            _furnitureImage = go.GetComponent<Image>();
            StretchFull((RectTransform)go.transform);
        }
        Sprite s = item != null ? item.PreviewOrIcon : null;
        _furnitureImage.sprite = s;
        _furnitureImage.enabled = s != null;
        _furnitureImage.preserveAspect = true;
    }

    /// <summary>Adds or replaces accessory preview layers (stacked). Call <see cref="ClearPreview"/> to reset.</summary>
    public void PreviewAccessory(ShopItemData item)
    {
        if (accessoryLayerRoot == null || item == null) return;

        if (_accessoryPool.Count >= maxAccessoryPreviewLayers)
        {
            Image oldest = _accessoryPool[0];
            _accessoryPool.RemoveAt(0);
            if (oldest != null) Destroy(oldest.gameObject);
        }

        var go = new GameObject("Accessory_" + item.id, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(accessoryLayerRoot, false);
        var img = go.GetComponent<Image>();
        img.sprite = item.PreviewOrIcon;
        img.enabled = img.sprite != null;
        img.preserveAspect = true;
        StretchFull(img.rectTransform);
        _accessoryPool.Add(img);
    }

    public void ClearPreview()
    {
        foreach (var img in _accessoryPool)
            if (img != null) Destroy(img.gameObject);
        _accessoryPool.Clear();

        if (_furnitureImage != null)
        {
            Destroy(_furnitureImage.gameObject);
            _furnitureImage = null;
        }

        if (petImage != null) { petImage.sprite = null; petImage.enabled = false; }
        if (backgroundImage != null) { backgroundImage.sprite = null; backgroundImage.enabled = false; }
        if (spaceTintImage != null) { spaceTintImage.sprite = null; spaceTintImage.enabled = false; }
    }

    /// <summary>Writes current preview into <see cref="MarketInventoryStore"/> as equipped (after successful purchase or re-equip).</summary>
    public void ConfirmEquip(ShopItemData item)
    {
        if (item == null) return;
        switch (item.category)
        {
            case MarketCategory.Pets:
                MarketInventoryStore.SetEquippedPet(item.id);
                break;
            case MarketCategory.Backgrounds:
                MarketInventoryStore.SetEquippedBackground(item.id);
                break;
            case MarketCategory.Spaces:
                MarketInventoryStore.SetEquippedSpace(item.id);
                break;
            case MarketCategory.Furnitures:
                MarketInventoryStore.SetEquippedFurniture(item.id);
                break;
            case MarketCategory.Accessories:
                MarketInventoryStore.AddEquippedAccessory(item.id);
                break;
        }
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
    }
}
