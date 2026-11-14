using UnityEditor;
using UnityEngine;

namespace BlockPet.EditorTools.Pets
{
    /// <summary>
    /// 進化測試（Play 時才能用；設計文件 5.17）：用假的五色張數模擬 80% 預告和滿 100 張，看三種型長怎樣。
    /// 模擬只在這台電腦、不存檔；按「進化」時會真的寫進營地（Rooms/{營地}/evolution），用「清掉進化結果」重來。
    /// 順序：美食、自然、城市、生活人物、寵物。
    /// </summary>
    static class EvolutionDebugMenu
    {
        const string Root = "BlockPet/Debug/進化/";

        static void Set(int[] palette, int lastFed = -1)
        {
            PetCollectionManager.DebugPalette = palette;
            PetCollectionManager.DebugLastFed = lastFed;
            Debug.Log(palette == null ? "[Evolution] 清掉模擬，回到真的照片" : "[Evolution] 模擬進化條：" + string.Join(",", palette));
        }

        [MenuItem(Root + "模擬 80%（偏美食）", false, 0)]
        static void Preview80Food() => Set(new[] { 60, 10, 5, 5, 0 });

        [MenuItem(Root + "模擬 80%（很平均）", false, 1)]
        static void Preview80Even() => Set(new[] { 16, 16, 16, 16, 16 });

        [MenuItem(Root + "模擬滿 100 張・極致型", false, 20)]
        static void FullExtreme() => Set(new[] { 70, 10, 10, 5, 5 });

        [MenuItem(Root + "模擬滿 100 張・彩虹型", false, 21)]
        static void FullRainbow() => Set(new[] { 20, 20, 20, 20, 20 });

        [MenuItem(Root + "模擬滿 100 張・進化型", false, 22)]
        static void FullEvolved() => Set(new[] { 45, 35, 10, 10, 0 });

        [MenuItem(Root + "清掉模擬", false, 40)]
        static void Clear() => Set(null);

        [MenuItem(Root + "清掉進化結果（重來）", false, 41)]
        static void ClearEvolution()
        {
            var fb = FirebaseManager.Instance;
            fb.DebugClearEvolution();
            PlayerPrefs.DeleteKey("BlockPet.Evo.Seen." + fb.RoomId);
            PlayerPrefs.Save();
            Debug.Log("[Evolution] 已清掉進化結果：寵物回到還沒進化");
        }

        [MenuItem(Root + "模擬 80%（偏美食）", true)]
        [MenuItem(Root + "模擬 80%（很平均）", true)]
        [MenuItem(Root + "模擬滿 100 張・極致型", true)]
        [MenuItem(Root + "模擬滿 100 張・彩虹型", true)]
        [MenuItem(Root + "模擬滿 100 張・進化型", true)]
        [MenuItem(Root + "清掉模擬", true)]
        [MenuItem(Root + "清掉進化結果（重來）", true)]
        static bool CanRun() => Application.isPlaying && FirebaseManager.Instance != null && !string.IsNullOrEmpty(FirebaseManager.Instance.RoomId);
    }
}
