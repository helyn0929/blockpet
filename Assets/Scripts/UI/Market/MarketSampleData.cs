using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Demo catalog. Replace with ScriptableObject assets or server data in production.
/// Items within each category are grouped by section label for display in the grid.
/// </summary>
public static class MarketSampleData
{
    public static List<ShopItemData> CreateSampleCatalog()
    {
        var list = new List<ShopItemData>();

        // ── Pets ──────────────────────────────────────────────
        Add(list, "pet_dog",     "Happy Pup",     MarketCategory.Pets,        "Everyday Collection", 50,   0,  true,  true,  false);
        Add(list, "pet_cat",     "Kitty",         MarketCategory.Pets,        "Everyday Collection", 50,   0,  false, false, false);
        Add(list, "pet_bunny",   "Bunny",         MarketCategory.Pets,        "Everyday Collection", 50,   0,  false, false, false);
        Add(list, "pet_pig",     "Little Pig",    MarketCategory.Pets,        "Everyday Collection", 50,   0,  false, false, false);
        Add(list, "pet_fox",     "Tiny Fox",      MarketCategory.Pets,        "Everyday Collection", 80,   0,  false, false, false);
        Add(list, "pet_raccoon", "Raccoon",       MarketCategory.Pets,        "Limited Collection",  1000, 0,  false, false, false);
        Add(list, "pet_lobster", "Lobster",       MarketCategory.Pets,        "Limited Collection",  1800, 0,  false, false, false);
        Add(list, "pet_mammoth", "Mammoth",       MarketCategory.Pets,        "Limited Collection",  3000, 0,  false, false, true);

        // ── Accessories ───────────────────────────────────────
        Add(list, "acc_hat",     "Straw Hat",     MarketCategory.Accessories, "Everyday Collection", 40,   0,  false, false, false);
        Add(list, "acc_glasses", "Round Glasses", MarketCategory.Accessories, "Everyday Collection", 35,   0,  false, false, false);
        Add(list, "acc_bow",     "Pink Bow",      MarketCategory.Accessories, "Everyday Collection", 25,   0,  true,  true,  false);
        Add(list, "acc_scarf",   "Knit Scarf",    MarketCategory.Accessories, "Everyday Collection", 45,   0,  false, false, false);
        Add(list, "acc_flower",  "Sunflower",     MarketCategory.Accessories, "Everyday Collection", 20,   0,  false, false, false);
        Add(list, "acc_ribbon",  "Silk Ribbon",   MarketCategory.Accessories, "Everyday Collection", 30,   0,  false, false, false);
        Add(list, "acc_crown",   "Mini Crown",    MarketCategory.Accessories, "Limited Collection",  120,  0,  false, false, false);
        Add(list, "acc_wings",   "Fairy Wings",   MarketCategory.Accessories, "Limited Collection",  200,  40, false, false, false);
        Add(list, "acc_cape",    "Hero Cape",     MarketCategory.Accessories, "Limited Collection",  150,  0,  false, false, false);

        // ── Furnitures ────────────────────────────────────────
        Add(list, "fur_bed",     "Cloud Bed",     MarketCategory.Furnitures,  "Everyday Collection", 90,   0,  false, false, false);
        Add(list, "fur_lamp",    "Star Lamp",     MarketCategory.Furnitures,  "Everyday Collection", 60,   0,  false, false, false);
        Add(list, "fur_rug",     "Round Rug",     MarketCategory.Furnitures,  "Everyday Collection", 50,   0,  false, false, false);
        Add(list, "fur_table",   "Tea Table",     MarketCategory.Furnitures,  "Everyday Collection", 75,   0,  false, false, false);
        Add(list, "fur_sofa",    "Plush Sofa",    MarketCategory.Furnitures,  "Limited Collection",  110,  0,  false, false, false);
        Add(list, "fur_shelf",   "Toy Shelf",     MarketCategory.Furnitures,  "Limited Collection",  85,   0,  false, false, false);

        // ── Backgrounds ───────────────────────────────────────
        Add(list, "bg_bedroom",  "Bedroom",       MarketCategory.Backgrounds, "Everyday Collection", 0,    0,  true,  true,  false);
        Add(list, "bg_forest",   "Forest",        MarketCategory.Backgrounds, "Everyday Collection", 70,   0,  false, false, false);
        Add(list, "bg_beach",    "Beach",         MarketCategory.Backgrounds, "Everyday Collection", 70,   0,  false, false, false);
        Add(list, "bg_cafe",     "Café",          MarketCategory.Backgrounds, "Limited Collection",  95,   0,  false, false, false);
        Add(list, "bg_stars",    "Starry Night",  MarketCategory.Backgrounds, "Limited Collection",  100,  0,  false, false, false);

        // ── Spaces ────────────────────────────────────────────
        Add(list, "sp_small",    "Small Room",    MarketCategory.Spaces,      "Everyday Collection", 0,    0,  true,  true,  false);
        Add(list, "sp_garden",   "Garden",        MarketCategory.Spaces,      "Everyday Collection", 150,  0,  false, false, false);
        Add(list, "sp_aqua",     "Aquarium",      MarketCategory.Spaces,      "Limited Collection",  220,  30, false, false, false);
        Add(list, "sp_roof",     "Rooftop",       MarketCategory.Spaces,      "Limited Collection",  180,  0,  false, false, false);

        ApplyPersistenceFlags(list);
        return list;
    }

    static void Add(List<ShopItemData> list, string id, string name, MarketCategory cat, string section,
        int coins, int gems, bool owned, bool equipped, bool locked)
    {
        list.Add(new ShopItemData
        {
            id       = id,
            itemName = name,
            category = cat,
            section  = section,
            price    = coins,
            gemPrice = gems,
            isOwned  = owned,
            isEquipped = equipped,
            isLocked = locked
        });
    }

    /// <summary>Merges PlayerPrefs ownership with catalog defaults and recomputes equipped flags.</summary>
    public static void ApplyPersistenceFlags(List<ShopItemData> list)
    {
        foreach (ShopItemData item in list)
        {
            if (item.category == MarketCategory.Money) { item.isOwned = false; item.isEquipped = false; continue; }

            item.isOwned = MarketInventoryStore.IsOwned(item.id) || item.isOwned;
            item.isEquipped = false;
            switch (item.category)
            {
                case MarketCategory.Pets:        item.isEquipped = MarketInventoryStore.GetEquippedPetId()        == item.id; break;
                case MarketCategory.Backgrounds: item.isEquipped = MarketInventoryStore.GetEquippedBackgroundId() == item.id; break;
                case MarketCategory.Spaces:      item.isEquipped = MarketInventoryStore.GetEquippedSpaceId()      == item.id; break;
                case MarketCategory.Accessories: item.isEquipped = MarketInventoryStore.IsAccessoryEquipped(item.id); break;
                case MarketCategory.Furnitures:  item.isEquipped = MarketInventoryStore.GetEquippedFurnitureId()  == item.id; break;
            }
        }
    }
}
