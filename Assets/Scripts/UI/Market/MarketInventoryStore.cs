using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ownership and equipped state for market items.
/// Owned items are local (per-device). Equipped state is synced to Firebase so all
/// room members see the same decoration/pet.
/// </summary>
public static class MarketInventoryStore
{
    const string OwnedKey                = "Market_OwnedIds";
    const string EquippedPetKey          = "Market_EquippedPetId";
    const string EquippedBgKey           = "Market_EquippedBgId";
    const string EquippedSpaceKey        = "Market_EquippedSpaceId";
    const string EquippedAccessoriesKey  = "Market_EquippedAccessoryIds";
    const string EquippedFurnitureKey    = "Market_EquippedFurnitureIds";

    /// <summary>Fired whenever equipped state changes (local or from Firebase).</summary>
    public static event Action OnEquipStateChanged;

    static HashSet<string> _ownedCache;

    static HashSet<string> OwnedSet
    {
        get
        {
            if (_ownedCache == null)
            {
                _ownedCache = new HashSet<string>(StringComparer.Ordinal);
                string raw = PlayerPrefs.GetString(OwnedKey, "");
                foreach (string id in raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    _ownedCache.Add(id.Trim());
            }
            return _ownedCache;
        }
    }

    static void SaveOwned()
    {
        PlayerPrefs.SetString(OwnedKey, string.Join(",", OwnedSet));
        PlayerPrefs.Save();
    }

    public static bool IsOwned(string id) => !string.IsNullOrEmpty(id) && OwnedSet.Contains(id);

    public static bool IsAccessoryEquipped(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        foreach (var a in GetEquippedAccessoryIds())
            if (a == id) return true;
        return false;
    }

    public static void SetOwned(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        OwnedSet.Add(id);
        SaveOwned();
    }

    // ─── Equip getters ──────────────────────────────────────────────

    public static string GetEquippedPetId()         => PlayerPrefs.GetString(EquippedPetKey, "");
    public static string GetEquippedBackgroundId()  => PlayerPrefs.GetString(EquippedBgKey, "");
    public static string GetEquippedSpaceId()       => PlayerPrefs.GetString(EquippedSpaceKey, "");
    public static string GetEquippedFurnitureId()   => PlayerPrefs.GetString(EquippedFurnitureKey, "");

    public static string[] GetEquippedAccessoryIds()
    {
        string s = PlayerPrefs.GetString(EquippedAccessoriesKey, "");
        if (string.IsNullOrEmpty(s)) return Array.Empty<string>();
        return s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
    }

    // ─── Equip setters (local + Firebase publish) ─────────────────

    public static void SetEquippedPet(string id)
    {
        PlayerPrefs.SetString(EquippedPetKey, id ?? "");
        PlayerPrefs.Save();
        FirebaseManager.Instance?.PublishRoomEquipment();
        OnEquipStateChanged?.Invoke();
    }

    public static void SetEquippedBackground(string id)
    {
        PlayerPrefs.SetString(EquippedBgKey, id ?? "");
        PlayerPrefs.Save();
        FirebaseManager.Instance?.PublishRoomEquipment();
        OnEquipStateChanged?.Invoke();
    }

    public static void SetEquippedSpace(string id)
    {
        PlayerPrefs.SetString(EquippedSpaceKey, id ?? "");
        PlayerPrefs.Save();
        FirebaseManager.Instance?.PublishRoomEquipment();
        OnEquipStateChanged?.Invoke();
    }

    public static void SetEquippedFurniture(string id)
    {
        PlayerPrefs.SetString(EquippedFurnitureKey, id ?? "");
        PlayerPrefs.Save();
        FirebaseManager.Instance?.PublishRoomEquipment();
        OnEquipStateChanged?.Invoke();
    }

    public static void SetEquippedAccessories(IEnumerable<string> ids)
    {
        PlayerPrefs.SetString(EquippedAccessoriesKey, string.Join(",", ids));
        PlayerPrefs.Save();
        FirebaseManager.Instance?.PublishRoomEquipment();
        OnEquipStateChanged?.Invoke();
    }

    public static void AddEquippedAccessory(string id)
    {
        var list = new List<string>(GetEquippedAccessoryIds());
        if (!list.Contains(id)) list.Add(id);
        SetEquippedAccessories(list);
    }

    public static void ClearEquippedAccessories()
    {
        PlayerPrefs.SetString(EquippedAccessoriesKey, "");
        PlayerPrefs.Save();
        FirebaseManager.Instance?.PublishRoomEquipment();
        OnEquipStateChanged?.Invoke();
    }

    // ─── Apply from Firebase (no re-publish to avoid loop) ────────

    public static void ApplyFromFirebase(string petId, string bgId, string spaceId,
                                         string accessoryIds, string furnitureId)
    {
        PlayerPrefs.SetString(EquippedPetKey,         petId        ?? "");
        PlayerPrefs.SetString(EquippedBgKey,          bgId         ?? "");
        PlayerPrefs.SetString(EquippedSpaceKey,       spaceId      ?? "");
        PlayerPrefs.SetString(EquippedAccessoriesKey, accessoryIds ?? "");
        PlayerPrefs.SetString(EquippedFurnitureKey,   furnitureId  ?? "");
        PlayerPrefs.Save();
        OnEquipStateChanged?.Invoke();
    }

    public static void ClearCache() => _ownedCache = null;
}
