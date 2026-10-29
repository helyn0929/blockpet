using System.Collections.Generic;
using UnityEngine;

namespace BlockPet.UI.Market
{
    public enum MarketTab { Furniture, Decor, Environment, Egg, Item }

    /// <summary>商店一件商品（設計稿「商店 A」MarketA.dc.html 的 DATA）。價格全部是空間金幣。</summary>
    public class MarketEntry
    {
        public string id;          // 也是 Rooms/{roomId}/shop/{id} 的 key；家具同時是 DecorationItem.itemId
        public MarketTab tab;
        public string name;
        public string desc;
        public int price;
        public string sprite;      // Assets/Art/UI/Market/Sprites 的檔名
        public Color tone;         // 卡片圖底色
        public bool petLikes;      // 寵物喜歡（左上愛心、試擺時「好喜歡！」）
        public string requires;    // 要先買過哪個商品才解鎖（例如投影幕要先有湖畔環境）
        public string lockText;
        // 裝飾：寵物從照片撿來的，不用買
        public string category;
        public int need;
    }

    /// <summary>
    /// 商店商品清單。設計稿原本環境（原「擴建」）與寵物蛋用鑽石，2026-10-23 定案「都改用錢」：
    /// 鑽石價格先以 1 鑽 = 20 金幣換算（投影幕 30 鑽 → 600、湖畔 60 → 1200、樹屋 90 → 1800、抽蛋 30 → 600），之後調價改這裡。
    /// </summary>
    public static class MarketCatalog
    {
        public const string Lake = "exp_lake";
        /// <summary>復活藥水（2026-10-28 定案：寵物死亡時用藥水復活，藥水在商城用金幣買）。id 與 PetHearts.PotionId 相同。</summary>
        public const string RevivePotion = "item_revive";

        public static readonly List<MarketEntry> All = new List<MarketEntry>
        {
            // 家具
            F("furn_lights", "拍立得燈串", "多掛 6 張大家餵過的照片", 450, 0xFFF0D0, true),
            F("furn_fire", "營火", "晚上會亮，寵物喜歡圍著轉", 200, 0xFDE6DE, true),
            F("furn_tent", "照片帳篷", "帳篷外面貼一張照片", 300, 0xFDE6DE),
            F("furn_hammock", "吊床", "寵物休息的地方", 380, 0xE3EAF9),
            F("furn_mat", "野餐墊", "美食照片會擺在墊子上", 150, 0xFCE7A6, true),
            F("furn_easel", "大照片畫架", "營地中央放一張大照片", 500, 0xF1E8D6),
            F("furn_lantern", "燈籠", "照亮營地的小角落", 120, 0xFFF0D0),
            F("furn_mailbox", "明信片信箱", "朋友寄來的明信片放這裡", 260, 0xE3EAF9),
            new MarketEntry { id = "furn_screen", tab = MarketTab.Furniture, name = "露天投影幕", desc = "晚上輪播營地照片", price = 600,
                              sprite = "furn_screen", tone = Hex(0xDCE4F0), requires = Lake, lockText = "先解鎖湖畔環境" },

            // 裝飾（寵物從餵的照片撿到，不用買；進度要等照片辨識接上）
            Dec("dec_cup", "咖啡杯", "美食", 5, "餵 5 張咖啡照片就撿得到", 0xFFE3C7),
            Dec("dec_ramen", "拉麵碗", "美食", 8, "餵 8 張拉麵照片就撿得到", 0xFFE3C7),
            Dec("dec_leaf", "葉子", "自然", 5, "餵 5 張樹和公園的照片就撿得到", 0xE3EED9),
            Dec("dec_flower", "小紅花", "自然", 5, "餵 5 張花的照片就撿得到", 0xE3EED9),
            Dec("dec_cone", "三角錐", "城市", 5, "餵 5 張街道的照片就撿得到", 0xE3EAF9),
            Dec("dec_party", "派對帽", "生活人物", 6, "餵 6 張和朋友的合照就撿得到", 0xF6E3F4),
            Dec("dec_band", "運動頭帶", "自然", 5, "餵 5 張戶外運動的照片就撿得到", 0xFBF0C8),
            Dec("dec_star", "金星星", "今日題目", 5, "今日題目連續 5 天集滿就撿得到（限定）", 0xFFF0D0),

            // 環境（2026-10-23「擴建」改名「環境」；id 沿用 exp_*，已買的不受影響）
            new MarketEntry { id = Lake, tab = MarketTab.Environment, name = "湖畔", desc = "營地往右多一塊，空位 +4", price = 1200, sprite = "exp_lake", tone = Hex(0xDDEFF6), petLikes = true },
            new MarketEntry { id = "exp_tree", tab = MarketTab.Environment, name = "樹屋", desc = "多一層，寵物可以爬上去", price = 1800, sprite = "exp_tree", tone = Hex(0xE3EED9) },
            new MarketEntry { id = "exp_flower", tab = MarketTab.Environment, name = "花田", desc = "營地往左多一塊，空位 +3", price = 1200, sprite = "exp_flower", tone = Hex(0xE3EED9) },

            // 寵物蛋（抽蛋機率與保底還沒定案：先直接買選中的那一種蛋）
            new MarketEntry { id = "egg_forest", tab = MarketTab.Egg, name = "森林蛋", desc = "比較容易孵出自然系的寵物", price = 600, sprite = "egg_forest", tone = Hex(0xE3EED9) },
            new MarketEntry { id = "egg_city", tab = MarketTab.Egg, name = "城市蛋", desc = "比較容易孵出城市系的寵物", price = 600, sprite = "egg_city", tone = Hex(0xE3EAF9) },
            new MarketEntry { id = "egg_star", tab = MarketTab.Egg, name = "星空蛋（限定）", desc = "限定外觀，有機會出稀有寵物", price = 600, sprite = "egg_star", tone = Hex(0xEFE3F6) },

            // 道具（可以買很多個，用掉就少一個）
            new MarketEntry { id = RevivePotion, tab = MarketTab.Item, name = "復活藥水", desc = "寵物昏倒時喝一瓶，愛心回到 5 顆滿", price = 100, sprite = "item_potion", tone = Hex(0xFDE3EA) },
        };

        /// <summary>10 顆蛋一起買的價格（9 折，同設計稿 270 / 300）。</summary>
        public const int EggTenPrice = 5400;

        /// <summary>營地家具空位：基本 8 格，湖畔環境 +4、花田環境 +3。</summary>
        public static int Slots(IReadOnlyDictionary<string, int> owned)
        {
            int n = 8;
            if (owned != null && owned.ContainsKey(Lake)) n += 4;
            if (owned != null && owned.ContainsKey("exp_flower")) n += 3;
            return n;
        }

        public static MarketEntry Get(string id) => All.Find(e => e.id == id);

        /// <summary>這個家具（DecorationItem.itemId）要先在商店買過才能擺。</summary>
        public static bool IsShopFurniture(string itemId) =>
            All.Exists(e => e.tab == MarketTab.Furniture && e.id == itemId);

        public static List<MarketEntry> InTab(MarketTab tab) => All.FindAll(e => e.tab == tab);

        public static Color CategoryColor(string category)
        {
            switch (category)
            {
                case "美食": return Hex(0xF29A3A);
                case "自然": return Hex(0x6FA45F);
                case "城市": return Hex(0x6F8FE0);
                case "生活人物": return Hex(0xD78AD0);
                case "寵物": return Hex(0xE8C33A);
                case "今日題目": return Hex(0xF6C945);
                default: return Color.gray;
            }
        }

        static MarketEntry F(string id, string name, string desc, int price, int tone, bool like = false) =>
            new MarketEntry { id = id, tab = MarketTab.Furniture, name = name, desc = desc, price = price, sprite = id, tone = Hex(tone), petLikes = like };

        static MarketEntry Dec(string id, string name, string cat, int need, string how, int tone) =>
            new MarketEntry { id = id, tab = MarketTab.Decor, name = name, desc = how, category = cat, need = need, sprite = id, tone = Hex(tone) };

        static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
