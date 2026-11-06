using System;
using System.Collections.Generic;

namespace BlockPet.Feed
{
    /// <summary>圖鑑的一格＝進化的一種型：彩虹型、極致型、進化型（設計文件 5.17 ②、5.23 ①；2026-11-05 從 7 格改成 3 格）。</summary>
    public class DexForm
    {
        public string key;
        public string name;
        /// <summary>點還沒收到的格子時說明怎麼養（設計文件 5.23 ⑤）。</summary>
        public string how;
    }

    /// <summary>圖鑑的一個系列（一個品種一本）。</summary>
    public class DexSeries
    {
        public string id;
        /// <summary>系列名，例「BowBow 系列」。</summary>
        public string name;
        /// <summary>短名（分頁、詳細標題用），例「BowBow」。</summary>
        public string shortName;
        /// <summary>蛋的名字，例「BowBow 蛋」「貓咪蛋」。</summary>
        public string eggName;
        /// <summary>品種圖示方塊底色、蛋上鋸齒線顏色（#RRGGBB）。</summary>
        public string iconBg, eggLine;
        /// <summary>季節限定：顯示「季節限定」標籤；還沒有時說明開放期間。</summary>
        public bool seasonal;
        public string seasonNote;
    }

    /// <summary>圖鑑裡的一筆紀錄＝營地的一代寵物（進化那一刻記下）。</summary>
    public class DexEntry
    {
        /// <summary>營地的第幾代。</summary>
        public int gen;
        /// <summary>進化時間（UTC 毫秒）。</summary>
        public long evolvedAt;
        /// <summary>養了幾天才進化。</summary>
        public int days;
        /// <summary>進化時的調色盤：五大類（美食、自然、城市、生活人物、寵物）各佔幾 %。</summary>
        public int[] palette = new int[5];
    }

    /// <summary>
    /// 圖鑑規則（設計文件 5.23；docs/dex/dex_spec.json）：營地共用一本、分系列、每系列 3 格（彩虹型、極致型、進化型，沒有稀有）、進化那一刻記。
    /// 系列目前照設計稿先放 BowBow、貓咪、兔子、萬聖節（2026-11-05 定）；之後有正式品種時改這裡的清單。
    /// 獎勵數字在 <see cref="EconomyRules"/>。
    /// </summary>
    public static class DexRules
    {
        public const int CellsPerSeries = 3;

        /// <summary>
        /// 遊戲還沒有品種系統：現在的寵物都算 BowBow（2026-11-05 定），所以每個營地一開始就有 BowBow 這個系列（7 格剪影）。
        /// 之後有品種系統時改成看營地養過哪些品種。
        /// </summary>
        public const string DefaultSeries = "bowbow";

        /// <summary>
        /// 每個系列都放 7 格剪影（2026-11-05 使用者決定，和設計文件 5.23 ②「還沒有的品種只放一格去商店抽蛋」不同）。
        /// 改回設計的做法時設成 false：營地沒有的品種只放「去商店抽蛋」。
        /// </summary>
        public const bool ShowCellsForAllSeries = true;

        public static readonly DexForm[] Forms =
        {
            new DexForm { key = Rainbow, name = "彩虹型", how = "五種照片都要餵，而且不要偏一種：最多的顏色不到 30%。" },
            new DexForm { key = Extreme, name = "極致型", how = "專心餵同一種照片，讓一種顏色超過一半。哪一種都可以，偏的顏色不同，樣子也不一樣。" },
            new DexForm { key = Evolved, name = "進化型", how = "有兩、三種照片餵得比較多，就會變成這樣，最常見。" },
        };

        public const string Rainbow = "rainbow", Extreme = "extreme", Evolved = "evolved";

        /// <summary>
        /// 進化當下判斷是哪一型（設計文件 5.17 ②，依序看）：
        /// 1 極致型：主色超過 50%；2 彩虹型：五色都有，而且主色不到 30%；3 其他是進化型。
        /// palette 是五大類各幾張（或幾 %），沒有資料回傳進化型。
        /// </summary>
        public static string TypeFor(int[] palette)
        {
            if (palette == null || palette.Length == 0) return Evolved;
            int sum = 0, top = 0;
            bool allFive = palette.Length >= 5;
            for (int i = 0; i < palette.Length; i++)
            {
                int v = Math.Max(0, palette[i]);
                sum += v;
                if (v > top) top = v;
                if (i < 5 && v <= 0) allFive = false;
            }
            if (sum <= 0) return Evolved;
            if (top * 2 > sum) return Extreme;              // 超過 50%
            if (allFive && top * 10 < sum * 3) return Rainbow; // 不到 30%
            return Evolved;
        }

        public static readonly DexSeries[] Series =
        {
            new DexSeries { id = "bowbow",    name = "BowBow 系列", shortName = "BowBow", eggName = "BowBow 蛋", iconBg = "#FCE7A6", eggLine = "#F29A3A" },
            new DexSeries { id = "cat",       name = "貓咪系列",    shortName = "貓咪",   eggName = "貓咪蛋",    iconBg = "#F6E2F4", eggLine = "#D78AD0" },
            new DexSeries { id = "rabbit",    name = "兔子系列",    shortName = "兔子",   eggName = "兔子蛋",    iconBg = "#E3EED9", eggLine = "#6FA45F" },
            new DexSeries { id = "halloween", name = "萬聖節系列",  shortName = "萬聖節", eggName = "萬聖節蛋",  iconBg = "#FFE3C7", eggLine = "#E86B4F", seasonal = true, seasonNote = "10/15～11/15 才抽得到萬聖節蛋" },
        };

        public static DexForm Form(string key) => Array.Find(Forms, f => f.key == key);
        public static DexSeries FindSeries(string id) => Array.Find(Series, s => s.id == id);
        public static int FormIndex(string key) => Array.FindIndex(Forms, f => f.key == key);

        /// <summary>
        /// 收滿一個系列的獎勵：免費抽一次寵物蛋（設計文件 5.23 ①，2026-11-05 定案）。
        /// 先記在營地的 Rooms/{room}/shop/free_egg_draw（張數），抽蛋（5.24）做好時從這裡扣。
        /// </summary>
        public const string FreeEggDrawId = "free_egg_draw";

        /// <summary>
        /// 系列說明（設計文件 5.23 ②）：營地有的品種＝「收滿 3 格送免費抽一次寵物蛋」，收滿後改「收滿了！已經送過…」；
        /// 還沒有的品種＝「還沒有兔子：抽寵物蛋有機會抽到」；季節限定寫開放期間。
        /// </summary>
        public static string Note(DexSeries s, bool owned, bool complete)
        {
            if (complete) return "收滿了！已經送過免費抽一次寵物蛋";
            if (owned) return $"收滿 {CellsPerSeries} 格送免費抽一次寵物蛋";
            if (s.seasonal && !string.IsNullOrEmpty(s.seasonNote)) return s.seasonNote;
            return $"還沒有{s.shortName}：抽寵物蛋有機會抽到";
        }

        /// <summary>還沒有的品種那一格：「去商店抽寵物蛋」；季節系列寫「去商店抽萬聖節蛋」（抽蛋是抽品種，沒有單賣某品種的蛋）。</summary>
        public static string ShopCta(DexSeries s) => s.seasonal ? "去商店抽" + s.eggName : "去商店抽寵物蛋";
    }

    /// <summary>營地的圖鑑內容：系列 → 外型 → 每一代的紀錄（按代數排）。</summary>
    public class DexBook
    {
        readonly Dictionary<string, Dictionary<string, List<DexEntry>>> _data = new Dictionary<string, Dictionary<string, List<DexEntry>>>();

        public void Add(string seriesId, string formKey, DexEntry e)
        {
            if (string.IsNullOrEmpty(seriesId) || string.IsNullOrEmpty(formKey) || e == null) return;
            if (!_data.TryGetValue(seriesId, out var forms)) _data[seriesId] = forms = new Dictionary<string, List<DexEntry>>();
            if (!forms.TryGetValue(formKey, out var list)) forms[formKey] = list = new List<DexEntry>();
            list.Add(e);
            list.Sort((a, b) => a.gen != b.gen ? a.gen.CompareTo(b.gen) : a.evolvedAt.CompareTo(b.evolvedAt));
        }

        /// <summary>某一格收過的紀錄（沒有回傳空清單）。</summary>
        public IReadOnlyList<DexEntry> Entries(string seriesId, string formKey) =>
            _data.TryGetValue(seriesId, out var forms) && forms.TryGetValue(formKey, out var list) ? list : (IReadOnlyList<DexEntry>)Array.Empty<DexEntry>();

        public int Count(string seriesId, string formKey) => Entries(seriesId, formKey).Count;

        /// <summary>某系列收到幾格（同一格收好幾次只算一格；只算這 7 種外型）。</summary>
        public int Got(string seriesId)
        {
            int n = 0;
            foreach (var f in DexRules.Forms) if (Count(seriesId, f.key) > 0) n++;
            return n;
        }

        public bool HasAny(string seriesId) => Got(seriesId) > 0;

        /// <summary>營地有這個品種：放 7 格（沒收到的是剪影）；沒有的品種只放「去商店抽蛋」（設計文件 5.23 ②）。</summary>
        public bool Owns(string seriesId) => HasAny(seriesId) || seriesId == DexRules.DefaultSeries;
        public bool IsComplete(string seriesId) => Got(seriesId) >= DexRules.CellsPerSeries;

        /// <summary>全部系列一共收到幾格。</summary>
        public int TotalGot()
        {
            int n = 0;
            foreach (var s in DexRules.Series) n += Got(s.id);
            return n;
        }

        /// <summary>全部系列一共幾格。</summary>
        public static int TotalMax => DexRules.Series.Length * DexRules.CellsPerSeries;

        /// <summary>全部紀錄筆數（＝營地進化過幾代）。</summary>
        public int TotalEntries()
        {
            int n = 0;
            foreach (var forms in _data.Values) foreach (var list in forms.Values) n += list.Count;
            return n;
        }

        /// <summary>收到這一格會不會是「新的一格」（還沒收過）。</summary>
        public bool IsNewCell(string seriesId, string formKey) => Count(seriesId, formKey) == 0;

        /// <summary>主頁的系列順序：營地有的品種在前面，其餘照目錄順序。</summary>
        public List<DexSeries> OrderedSeries()
        {
            var list = new List<DexSeries>(DexRules.Series);
            var order = new Dictionary<string, int>();
            for (int i = 0; i < list.Count; i++) order[list[i].id] = i;
            list.Sort((a, b) =>
            {
                bool ha = Owns(a.id), hb = Owns(b.id);
                if (ha != hb) return ha ? -1 : 1;
                return order[a.id].CompareTo(order[b.id]);
            });
            return list;
        }
    }
}
