using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a demo catalog for prototyping. Replace with ScriptableObject assets or server data in production.
/// </summary>
public static class MarketSampleData
{
    public static List<ShopItemData> CreateSampleCatalog()
    {
        var list = new List<ShopItemData>();

        // --- Pets (5) ---
        Add(list, "pet_cat", "Fluffy Cat", MarketCategory.Pets, 200, 0, false, false, false);
        Add(list, "pet_bunny", "Sleepy Bunny", MarketCategory.Pets, 250, 0, false, false, false);
        Add(list, "pet_dog", "Happy Pup", MarketCategory.Pets, 180, 0, true, true, false);
        Add(list, "pet_fox", "Tiny Fox", MarketCategory.Pets, 320, 50, false, false, false);
        Add(list, "pet_bear", "Cozy Bear", MarketCategory.Pets, 400, 0, false, false, true);

        // --- Accessories (10) ---
        Add(list, "acc_hat", "Straw Hat", MarketCategory.Accessories, 40, 0, false, false, false);
        Add(list, "acc_glasses", "Round Glasses", MarketCategory.Accessories, 35, 0, false, false, false);
        Add(list, "acc_scarf", "Knit Scarf", MarketCategory.Accessories, 45, 0, false, false, false);
        Add(list, "acc_bow", "Pink Bow", MarketCategory.Accessories, 25, 0, true, true, false);
        Add(list, "acc_crown", "Mini Crown", MarketCategory.Accessories, 120, 0, false, false, false);
        Add(list, "acc_ribbon", "Silk Ribbon", MarketCategory.Accessories, 30, 0, false, false, false);
        Add(list, "acc_mask", "Party Mask", MarketCategory.Accessories, 55, 0, false, false, false);
        Add(list, "acc_flower", "Sunflower", MarketCategory.Accessories, 20, 0, false, false, false);
        Add(list, "acc_wings", "Fairy Wings", MarketCategory.Accessories, 200, 40, false, false, false);
        Add(list, "acc_cape", "Hero Cape", MarketCategory.Accessories, 150, 0, false, false, false);

        // --- Furniture (6) ---
        Add(list, "fur_bed", "Cloud Bed", MarketCategory.Furnitures, 90, 0, false, false, false);
        Add(list, "fur_lamp", "Star Lamp", MarketCategory.Furnitures, 60, 0, false, false, false);
        Add(list, "fur_sofa", "Plush Sofa", MarketCategory.Furnitures, 110, 0, false, false, false);
        Add(list, "fur_table", "Tea Table", MarketCategory.Furnitures, 75, 0, false, false, false);
        Add(list, "fur_rug", "Round Rug", MarketCategory.Furnitures, 50, 0, false, false, false);
        Add(list, "fur_shelf", "Toy Shelf", MarketCategory.Furnitures, 85, 0, false, false, false);

        // --- Backgrounds (5) ---
        Add(list, "bg_forest", "Forest", MarketCategory.Backgrounds, 70, 0, false, false, false);
        Add(list, "bg_beach", "Beach", MarketCategory.Backgrounds, 70, 0, false, false, false);
        Add(list, "bg_bedroom", "Bedroom", MarketCategory.Backgrounds, 0, 0, true, true, false);
        Add(list, "bg_stars", "Starry Night", MarketCategory.Backgrounds, 100, 0, false, false, false);
        Add(list, "bg_cafe", "Café", MarketCategory.Backgrounds, 95, 0, false, false, false);

        // --- Spaces (4) ---
        Add(list, "sp_small", "Small Room", MarketCategory.Spaces, 0, 0, true, true, false);
        Add(list, "sp_garden", "Garden", MarketCategory.Spaces, 150, 0, false, false, false);
        Add(list, "sp_aqua", "Aquarium", MarketCategory.Spaces, 220, 30, false, false, false);
        Add(list, "sp_roof", "Rooftop", MarketCategory.Spaces, 180, 0, false, false, false);

        // --- Money packs (3) — price = soft currency cost to buy pack in demo; grant* = contents ---
        AddMoneyPack(list, "money_small", "Coin Pouch", 99, 500, 0);
        AddMoneyPack(list, "money_gem", "Gem Stack", 199, 0, 50);
        AddMoneyPack(list, "money_combo", "Starter Pack", 149, 300, 20);

        ApplyPersistenceFlags(list);
        return list;
    }

    static void Add(List<ShopItemData> list, string id, string name, MarketCategory cat, int coins, int gems,
        bool owned, bool equipped, bool locked)
    {
        list.Add(new ShopItemData
        {
            id = id,
            itemName = name,
            category = cat,
            price = coins,
            gemPrice = gems,
            isOwned = owned,
            isEquipped = equipped,
            isLocked = locked
        });
    }

    static void AddMoneyPack(List<ShopItemData> list, string id, string name, int priceCoins, int grantCoins, int grantGems)
    {
        list.Add(new ShopItemData
        {
            id = id,
            itemName = name,
            category = MarketCategory.Money,
            price = priceCoins,
            gemPrice = 0,
            grantCoins = grantCoins,
            grantGems = grantGems,
            isOwned = false,
            isEquipped = false,
            isLocked = false
        });
    }

    /// <summary>Merges PlayerPrefs ownership with catalog defaults and recomputes equipped flags.</summary>
    public static void ApplyPersistenceFlags(List<ShopItemData> list)
    {
        foreach (ShopItemData item in list)
        {
            if (item.category == MarketCategory.Money)
            {
                item.isOwned = false;
                item.isEquipped = false;
                continue;
            }

            item.isOwned = MarketInventoryStore.IsOwned(item.id) || item.isOwned;
            item.isEquipped = false;
            switch (item.category)
            {
                case MarketCategory.Pets:
                    item.isEquipped = MarketInventoryStore.GetEquippedPetId() == item.id;
                    break;
                case MarketCategory.Backgrounds:
                    item.isEquipped = MarketInventoryStore.GetEquippedBackgroundId() == item.id;
                    break;
                case MarketCategory.Spaces:
                    item.isEquipped = MarketInventoryStore.GetEquippedSpaceId() == item.id;
                    break;
                case MarketCategory.Accessories:
                    item.isEquipped = MarketInventoryStore.IsAccessoryEquipped(item.id);
                    break;
                case MarketCategory.Furnitures:
                    item.isEquipped = MarketInventoryStore.GetEquippedFurnitureId() == item.id;
                    break;
            }
        }
    }
}
