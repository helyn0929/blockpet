using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Populates the scroll content with ShopItemCard instances, inserting section headers between groups.
/// </summary>
public class ShopGridController : MonoBehaviour
{
    [SerializeField] Transform contentRoot;
    [SerializeField] ShopItemCard itemPrefab;
    [Tooltip("Optional prefab for section header labels (needs a TMP_Text child). If null, headers are skipped.")]
    [SerializeField] GameObject sectionHeaderPrefab;

    readonly List<GameObject> _spawned = new List<GameObject>();

    public void Rebuild(List<ShopItemData> items, UnityAction<ShopItemData> onItemClicked)
    {
        Clear();

        if (contentRoot == null || itemPrefab == null)
        {
            Debug.LogWarning("[ShopGridController] Assign contentRoot and itemPrefab.");
            return;
        }

        string lastSection = null;
        foreach (ShopItemData data in items)
        {
            if (data == null) continue;

            // Insert section header when section label changes.
            if (!string.IsNullOrEmpty(data.section) && data.section != lastSection)
            {
                lastSection = data.section;
                SpawnSectionHeader(data.section);
            }

            ShopItemCard card = Instantiate(itemPrefab, contentRoot);
            card.Bind(data, ComputeState(data), onItemClicked);
            _spawned.Add(card.gameObject);
        }

        Canvas.ForceUpdateCanvases();
        if (contentRoot is RectTransform rt)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
    }

    void SpawnSectionHeader(string label)
    {
        if (sectionHeaderPrefab == null) return;
        GameObject go = Instantiate(sectionHeaderPrefab, contentRoot);
        var tmp = go.GetComponentInChildren<TMP_Text>();
        if (tmp != null) tmp.text = label;
        _spawned.Add(go);
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
        foreach (var go in _spawned)
            if (go != null) Destroy(go);
        _spawned.Clear();
    }
}
