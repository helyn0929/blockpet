using System;
using System.Collections.Generic;

namespace BlockPet.Feed
{
    /// <summary>喜歡的一件家具：家具 id、互動方式（look 看／sleep 睡／sit 坐…，動畫待美術）、比重（越大越常去）。</summary>
    [Serializable]
    public class LikeEntry
    {
        public string item;
        public string action;
        public float weight = 1f;
    }

    /// <summary>一組喜歡的家具：key＝主色類別（美食…寵物）、rainbow（彩虹型）、juvenile（進化前，從這裡隨機挑）。</summary>
    [Serializable]
    public class LikeSet
    {
        public string key;
        public LikeEntry[] likes;
    }

    [Serializable]
    public class BreedLikes
    {
        public string breed;
        public LikeSet[] sets;
    }

    /// <summary>規則表（Resources/Pets/furniture_likes.json）＋走過去的頻率（設計 5.18 ⑤ 暫定值）。</summary>
    [Serializable]
    public class FurnitureLikeTable
    {
        public float visitChance = 0.3f;
        public float staySeconds = 6f;
        public float cooldownSeconds = 60f;
        public int maxLikes = 5;
        public int juvenileCount = 2;
        public BreedLikes[] breeds;
    }

    /// <summary>
    /// 家具愛好（設計文件 5.18 ⑤，2026-11-14）：進化後依主色（彩虹型用 rainbow）決定喜歡的家具，最多 5 件；
    /// 進化前隨機喜歡 2 件，出生時決定（同一個 seed 一樣）。寵物閒晃結束時有機率走去一件「有擺出來」的喜歡家具，
    /// 同一件冷卻一段時間不重複。掉心速度不變。
    /// </summary>
    public static class FurnitureLikeRules
    {
        public const string DefaultBreed = "bowbow";
        public const string RainbowKey = "rainbow";
        public const string JuvenileKey = "juvenile";

        static LikeEntry[] SetOf(FurnitureLikeTable t, string breed, string key)
        {
            if (t?.breeds == null) return null;
            BreedLikes b = Array.Find(t.breeds, x => x != null && x.breed == breed)
                           ?? Array.Find(t.breeds, x => x != null && x.breed == DefaultBreed);
            var set = b?.sets != null ? Array.Find(b.sets, s => s != null && s.key == key) : null;
            return set?.likes;
        }

        /// <summary>現在喜歡哪些家具：evoType 空＝還沒進化（從 juvenile 用 seed 隨機挑 juvenileCount 件）。</summary>
        public static List<LikeEntry> LikesFor(FurnitureLikeTable t, string breed, string evoType, string mainCategory, int seed)
        {
            var result = new List<LikeEntry>();
            if (t == null) return result;
            if (string.IsNullOrEmpty(evoType))
            {
                var pool = SetOf(t, breed, JuvenileKey);
                if (pool == null) return result;
                var list = new List<LikeEntry>(pool);
                var rnd = new Random(seed);
                int n = Math.Min(Math.Max(0, t.juvenileCount), list.Count);
                for (int i = 0; i < n; i++)
                {
                    int j = rnd.Next(i, list.Count);
                    (list[i], list[j]) = (list[j], list[i]);
                    result.Add(list[i]);
                }
                return result;
            }
            var likes = SetOf(t, breed, evoType == DexRules.Rainbow ? RainbowKey : mainCategory);
            if (likes != null)
                for (int i = 0; i < likes.Length && result.Count < Math.Max(0, t.maxLikes); i++)
                    if (likes[i] != null) result.Add(likes[i]);
            return result;
        }

        /// <summary>
        /// 這次閒晃要不要走去一件喜歡的家具：先擲機率（visitChance），再從「有擺出來、不在冷卻」的裡面依比重挑。不去回傳 null。
        /// </summary>
        public static LikeEntry PickVisit(IList<LikeEntry> likes, Func<string, bool> isPlaced, IDictionary<string, float> lastVisit,
                                          float now, FurnitureLikeTable t, Random rnd)
        {
            if (likes == null || likes.Count == 0 || t == null) return null;
            rnd ??= new Random();
            if (rnd.NextDouble() >= t.visitChance) return null;
            var ok = new List<LikeEntry>();
            float total = 0f;
            foreach (var e in likes)
            {
                if (e == null || string.IsNullOrEmpty(e.item)) continue;
                if (isPlaced != null && !isPlaced(e.item)) continue;
                if (lastVisit != null && lastVisit.TryGetValue(e.item, out float at) && now - at < t.cooldownSeconds) continue;
                ok.Add(e);
                total += Math.Max(0.0001f, e.weight);
            }
            if (ok.Count == 0) return null;
            double r = rnd.NextDouble() * total;
            foreach (var e in ok)
            {
                r -= Math.Max(0.0001f, e.weight);
                if (r <= 0) return e;
            }
            return ok[ok.Count - 1];
        }

        public static bool Likes(IList<LikeEntry> likes, string itemId)
        {
            if (likes == null || string.IsNullOrEmpty(itemId)) return false;
            foreach (var e in likes) if (e != null && e.item == itemId) return true;
            return false;
        }
    }
}
