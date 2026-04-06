using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Populates the scroll content with <see cref="ShopItemCard"/> instances for the current category filter.
/// </summary>
public class ShopGridController : MonoBehaviour
{
    [SerializeField] Transform contentRoot;
    [SerializeField] ShopItemCard itemPrefab;

    readonly List<ShopItemCard> _pool = new List<ShopItemCard>();

    public void Rebuild(List<ShopItemData> items, UnityAction<ShopItemData> onItemClicked)
    {
        Clear();

        if (contentRoot == null || itemPrefab == null)
        {
            Debug.LogWarning("[ShopGridController] Assign content root and ShopItemCard prefab.");
            return;
        }

        foreach (ShopItemData data in items)
        {
            if (data == null) continue;
            ShopItemCard card = Instantiate(itemPrefab, contentRoot);
            card.Bind(data, ComputeState(data), onItemClicked);
            _pool.Add(card);
        }

        Canvas.ForceUpdateCanvases();
        if (contentRoot is RectTransform rt)
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
    }

    static ShopItemCard.CardState ComputeState(ShopItemData d)
    {
        if (d.isLocked) return ShopItemCard.CardState.Locked;
        if (d.isEquipped) return ShopItemCard.CardState.Equipped;
        if (d.isOwned) return ShopItemCard.CardState.Owned;
        return ShopItemCard.CardState.Buy;
    }

    public void Clear()
    {
        foreach (var c in _pool)
            if (c != null) Destroy(c.gameObject);
        _pool.Clear();
    }
}
