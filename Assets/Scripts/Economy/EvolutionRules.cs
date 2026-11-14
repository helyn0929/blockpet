using System;
using System.Collections.Generic;

namespace BlockPet.Feed
{
    /// <summary>進化的結果：型（DexRules.Rainbow／Extreme／Evolved）、主色、副色（類別索引，順序同 PetPreferences.Categories）、五色張數。</summary>
    public struct EvolutionResult
    {
        public string type;
        public int main;
        public int sub;
        public int[] palette;
    }

    /// <summary>
    /// 進化規則（設計文件 5.17 ②④⑤，2026-11-14 補流程）：
    /// - 調色盤＝從這隻寵物開始算的前 100 張照片各類幾張（沒辨識的算美食，同 PetCollectionManager.EnergyFor）。
    /// - 型照 DexRules.TypeFor；主色＝最多的類別（一樣多取最後餵的那一類）；副色＝第二多的。
    /// - 100 張可進化；80～99 張顯示預告。
    /// </summary>
    public static class EvolutionRules
    {
        public const int Target = 100;
        public const int PreviewFrom = 80;

        /// <summary>進化型預告用的顏色字（設計只給「橘色」的例子，其他 2026-11-14 暫定）。</summary>
        static readonly string[] ColorWords = { "橘色", "綠色", "藍色", "粉紫色", "黃色" };

        static int IndexOf(string category)
        {
            int i = string.IsNullOrEmpty(category) ? -1 : Array.IndexOf(PetPreferences.Categories, category);
            return i >= 0 ? i : 0;
        }

        /// <summary>前 target 張（從 start 開始）各類幾張。</summary>
        public static int[] Palette(IList<string> photoCategories, int start, int target = Target)
        {
            var p = new int[PetPreferences.Categories.Length];
            if (photoCategories == null) return p;
            int end = Math.Min(photoCategories.Count, Math.Max(0, start) + target);
            for (int i = Math.Max(0, start); i < end; i++) p[IndexOf(photoCategories[i])]++;
            return p;
        }

        /// <summary>算進調色盤的最後一張是哪一類（沒有照片是 -1）。</summary>
        public static int LastFedCategory(IList<string> photoCategories, int start, int target = Target)
        {
            if (photoCategories == null) return -1;
            int end = Math.Min(photoCategories.Count, Math.Max(0, start) + target);
            return end > Math.Max(0, start) ? IndexOf(photoCategories[end - 1]) : -1;
        }

        public static EvolutionResult Decide(int[] palette, int lastFed)
        {
            var p = palette ?? new int[PetPreferences.Categories.Length];
            int main = -1;
            for (int i = 0; i < p.Length; i++)
                if (main < 0 || p[i] > p[main] || (p[i] == p[main] && i == lastFed)) main = i;
            int sub = -1;
            for (int i = 0; i < p.Length; i++)
                if (i != main && (sub < 0 || p[i] > p[sub])) sub = i;
            return new EvolutionResult { type = DexRules.TypeFor(p), main = Math.Max(0, main), sub = Math.Max(0, sub), palette = (int[])p.Clone() };
        }

        public static bool CanEvolve(int fed) => fed >= Target;
        public static bool ShowPreview(int fed) => fed >= PreviewFrom && fed < Target;

        public static string ColorWord(int category) => ColorWords[Math.Max(0, Math.Min(ColorWords.Length - 1, category))];

        /// <summary>80% 預告的一句話（5.17 ⑤），依現在的比例即時變。</summary>
        public static string PreviewLine(int[] palette, int lastFed)
        {
            var r = Decide(palette, lastFed);
            if (r.type == DexRules.Extreme) return $"全部都是{PetPreferences.Categories[r.main]}耶！好像要變成極致型…";
            if (r.type == DexRules.Rainbow) return "顏色好平均，說不定會變彩虹…";
            return $"好像會變成{ColorWord(r.main)}的…";
        }

        public static string ResultTitle(string type) =>
            type == DexRules.Extreme ? "變成極致型了！" : type == DexRules.Rainbow ? "變成彩虹型了！" : "變成進化型了！";
    }
}
