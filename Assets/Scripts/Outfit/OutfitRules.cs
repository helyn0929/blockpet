using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockPet.Outfit
{
    /// <summary>一件穿搭（設定檔 Resources/Shop/outfits.json 的一筆）。價格是營地金幣。</summary>
    [Serializable]
    public class OutfitItem
    {
        public string id;          // 也是 Rooms/{room}/shop/{id} 的 key
        public string name;
        public string slot;        // head 頭／face 臉／neck 脖子／side 側邊（蝴蝶結）
        public int price;
        public string desc;
        public string tone = "#F4ECDD";   // 商品卡圖片框底色
        public int[] months;       // 只在這些月份開賣（空＝一直賣），例：聖誕帽 [12]
        public string lockText;    // 還沒開賣的原因（例「12 月限定」）
    }

    [Serializable]
    class OutfitFile { public OutfitItem[] items; }

    /// <summary>商店卡片、購買列的狀態。</summary>
    public enum OutfitState { Buyable, NotOnSale, Owned, Wearing }

    /// <summary>
    /// 穿搭規則（設計文件 5.11 ⑧、第 7 節營利邊界，2026-11-07）：
    /// - 只是打扮：隨時穿上、脫下，不影響進化、愛心、金幣、速度等任何數值（這裡沒有任何數值）；身體長相不賣。
    /// - 每件有部位；同一個部位一次只能穿一件，不同部位可以同時穿。
    /// - 待決 36 的暫定：穿著狀態是營地共用（和家具一樣）；不同品種都能穿。
    /// 與 11-06 刪除的「從照片撿裝飾」無關，是另一套。
    /// </summary>
    public static class OutfitRules
    {
        public const string Head = "head", Face = "face", Neck = "neck", Side = "side";
        public static readonly string[] Slots = { Head, Face, Neck, Side };

        public static bool IsValidSlot(string slot) => Array.IndexOf(Slots, slot) >= 0;

        public static string SlotName(string slot)
        {
            switch (slot)
            {
                case Head: return "頭";
                case Face: return "臉";
                case Neck: return "脖子";
                case Side: return "側邊";
            }
            return "";
        }

        /// <summary>讀設定檔；部位不對、沒有 id 的品項不收（身體長相不能賣）。</summary>
        public static List<OutfitItem> Parse(string json)
        {
            var list = new List<OutfitItem>();
            if (string.IsNullOrEmpty(json)) return list;
            OutfitFile file;
            try { file = JsonUtility.FromJson<OutfitFile>(json); }
            catch (Exception e) { Debug.LogWarning("[Outfit] 穿搭設定檔格式錯誤：" + e.Message); return list; }
            if (file?.items == null) return list;
            foreach (var i in file.items)
                if (i != null && !string.IsNullOrEmpty(i.id) && IsValidSlot(i.slot) && i.price >= 0) list.Add(i);
            return list;
        }

        /// <summary>這個月有沒有在賣（months 空＝一直賣）。</summary>
        public static bool IsOnSale(OutfitItem item, DateTime now)
        {
            if (item == null) return false;
            if (item.months == null || item.months.Length == 0) return true;
            return Array.IndexOf(item.months, now.Month) >= 0;
        }

        public static bool IsWearing(IReadOnlyDictionary<string, string> worn, OutfitItem item) =>
            item != null && worn != null && worn.TryGetValue(item.slot, out var id) && id == item.id;

        /// <summary>穿上：同部位原本的那件換掉。回傳新的（不改傳進來的）。</summary>
        public static Dictionary<string, string> Wear(IReadOnlyDictionary<string, string> worn, OutfitItem item)
        {
            var next = Copy(worn);
            if (item != null && IsValidSlot(item.slot)) next[item.slot] = item.id;
            return next;
        }

        /// <summary>脫下：只有正在穿的是這件才脫。回傳新的。</summary>
        public static Dictionary<string, string> TakeOff(IReadOnlyDictionary<string, string> worn, OutfitItem item)
        {
            var next = Copy(worn);
            if (IsWearing(worn, item)) next.Remove(item.slot);
            return next;
        }

        /// <summary>商店狀態：穿著 ＞ 已擁有 ＞ 還沒開賣 ＞ 可以買（買過的就算現在不賣也能穿）。</summary>
        public static OutfitState StateOf(OutfitItem item, bool owned, IReadOnlyDictionary<string, string> worn, DateTime now)
        {
            if (owned && IsWearing(worn, item)) return OutfitState.Wearing;
            if (owned) return OutfitState.Owned;
            return IsOnSale(item, now) ? OutfitState.Buyable : OutfitState.NotOnSale;
        }

        static Dictionary<string, string> Copy(IReadOnlyDictionary<string, string> worn)
        {
            var d = new Dictionary<string, string>();
            if (worn != null) foreach (var kv in worn) d[kv.Key] = kv.Value;
            return d;
        }
    }
}
