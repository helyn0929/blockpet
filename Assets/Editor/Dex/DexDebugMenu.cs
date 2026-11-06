using BlockPet.Feed;
using BlockPet.UI.Dex;
using UnityEditor;
using UnityEngine;

namespace BlockPet.EditorTools.Dex
{
    /// <summary>
    /// 圖鑑測試用（Play 時才能用）：進化系統還沒做，先用這裡在目前的空間記一筆，看主頁、詳細、「圖鑑新增！」。
    /// 會真的寫進 Firebase 的 Rooms/{空間}/dex，新的一格也會真的加營地金幣（暫用值 +100）。
    /// </summary>
    static class DexDebugMenu
    {
        const string Root = "BlockPet/Debug/圖鑑/";

        [MenuItem(Root + "打開圖鑑")]
        static void OpenDex() => DexScreen.Open();

        [MenuItem(Root + "記一筆：BowBow 極致型")]
        static void BowBowExtreme() => Record("bowbow", DexRules.Extreme);

        [MenuItem(Root + "記一筆：BowBow 彩虹型")]
        static void BowBowRainbow() => Record("bowbow", DexRules.Rainbow);

        [MenuItem(Root + "記一筆：BowBow 進化型")]
        static void BowBowEvolved() => Record("bowbow", DexRules.Evolved);

        [MenuItem(Root + "記一筆：隨機系列、隨機型")]
        static void AnyRandom() => Record(DexRules.Series[Random.Range(0, DexRules.Series.Length)].id, DexRules.Forms[Random.Range(0, DexRules.Forms.Length)].key);

        [MenuItem(Root + "打開圖鑑", true)]
        [MenuItem(Root + "記一筆：BowBow 極致型", true)]
        [MenuItem(Root + "記一筆：BowBow 彩虹型", true)]
        [MenuItem(Root + "記一筆：BowBow 進化型", true)]
        [MenuItem(Root + "記一筆：隨機系列、隨機型", true)]
        static bool CanRun() => Application.isPlaying && FirebaseManager.Instance != null && !string.IsNullOrEmpty(FirebaseManager.Instance.RoomId);

        static int[] PaletteFor(string formKey)
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                var pal = new int[5];
                int main = Random.Range(0, 5);
                if (formKey == DexRules.Extreme) pal[main] = Random.Range(52, 75);
                else if (formKey == DexRules.Rainbow) pal[main] = Random.Range(22, 29);
                else pal[main] = Random.Range(32, 48);
                int left = 100 - pal[main];
                for (int i = 0; i < 5; i++)
                {
                    if (i == main) continue;
                    int remaining = 0;
                    for (int j = i + 1; j < 5; j++) if (j != main) remaining++;
                    int v = remaining == 0 ? left : Random.Range(formKey == DexRules.Rainbow ? 10 : 0, Mathf.Min(left, pal[main]) + 1);
                    pal[i] = v;
                    left -= v;
                }
                if (left == 0 && DexRules.TypeFor(pal) == formKey) return pal;
            }
            return formKey == DexRules.Extreme ? new[] { 60, 10, 10, 10, 10 }
                 : formKey == DexRules.Rainbow ? new[] { 22, 20, 20, 20, 18 }
                 : new[] { 40, 35, 10, 10, 5 };
        }

        static void Record(string seriesId, string formKey)
        {
            // 調色盤（加起來 100）：照那一型的條件隨機產生（設計文件 5.17 ②）
            var pal = PaletteFor(formKey);
            var entry = new DexEntry { days = Random.Range(25, 45), palette = pal };
            FirebaseManager.Instance.RecordDex(seriesId, formKey, entry, r =>
            {
                Debug.Log($"[Dex] 記一筆 {seriesId}/{formKey}：ok={r.ok} 新格={r.isNewCell} 系列 {r.got}/{DexRules.CellsPerSeries} 收滿={r.seriesComplete} 第 {r.gen} 代");
                if (r.isNewCell) DexScreen.ShowNew(seriesId, formKey, r);
            });
        }
    }
}
