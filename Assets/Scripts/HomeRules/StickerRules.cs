using System;
using System.Collections.Generic;

namespace BlockPet.Home
{
    /// <summary>首頁貼紙要用到的照片資料（id＝照片檔名、author＝拍照人 uid、time＝拍照時間 ticks）。</summary>
    public struct StickerPhoto
    {
        public string id;
        public string author;
        public long time;

        public StickerPhoto(string id, string author, long time)
        {
            this.id = id;
            this.author = author;
            this.time = time;
        }
    }

    /// <summary>貼紙的位置（設計座標 390×772 的左上角，y 向下）、邊長與旋轉（CSS 角度，正＝順時針）。</summary>
    public struct StickerSpot
    {
        public float x, y, size, rot;
    }

    /// <summary>
    /// 首頁照片貼紙的規則（設計文件 5.25 ④；docs/home9/home9_spec.json）：
    /// - 其他成員發的、我還沒看過的照片；自己發的不算；只有撕掉才算看過（2026-11-06 使用者決定）。
    /// - 營地最新一張另外黏成「最新貼紙」，不算在一般貼紙裡。
    /// - 第一次用 v9 之前的照片都當作看過（2026-11-06 使用者決定），用 baseline 時間判斷。
    /// - 最多 8 張，多的顯示 +N；新照片進來就貼最新的、擠掉最舊的（2026-11-06 使用者決定，像以前公布欄的最新照片）。
    /// - 位置依照片 id 固定，隨機貼滿畫面、可以蓋到寵物，只避開狀態欄（y &lt; 120）和底部按鈕（y &gt; 640）；
    ///   大小 56～96 隨機（2026-11-06 使用者決定：有大有小、佔版面沒關係，逼大家清）；隨機歪 −12°～10°。
    /// </summary>
    public static class StickerRules
    {
        public const int MaxStickers = 8;
        public const float Size = 66f;          // 貼紙設計稿的尺寸（素材、版面以這個為準再縮放）
        public const float MinSize = 56f, MaxSize = 96f;
        public const float DesignW = 390f;
        public const float TopLimit = 120f;     // 狀態欄以下
        public const float BottomLimit = 640f;  // 底部三顆按鈕以上
        public const float SideMargin = 6f;
        public const float MinRot = -12f, MaxRot = 10f;

        /// <summary>
        /// 要貼的一般貼紙：最新的 <see cref="MaxStickers"/> 張，依舊到新排（新的疊在上面）；more＝沒貼出來的較舊張數。
        /// photos 順序不限；me 是自己的 uid；latestId 是現在當最新貼紙的那張（不重複貼）；
        /// baseline 之前（含）的照片當作看過。
        /// </summary>
        public static List<StickerPhoto> Visible(IEnumerable<StickerPhoto> photos, string me, ICollection<string> seen,
                                                 long baseline, string latestId, out int more)
        {
            var all = new List<StickerPhoto>();
            if (photos != null)
            {
                var ids = new HashSet<string>();
                foreach (var p in photos)
                {
                    if (string.IsNullOrEmpty(p.id) || !ids.Add(p.id)) continue;
                    if (!string.IsNullOrEmpty(me) && p.author == me) continue;
                    if (p.id == latestId) continue;
                    if (p.time <= baseline) continue;
                    if (seen != null && seen.Contains(p.id)) continue;
                    all.Add(p);
                }
            }
            // 舊到新；同時間依 id 排，結果固定
            all.Sort((a, b) => a.time != b.time ? a.time.CompareTo(b.time) : string.CompareOrdinal(a.id, b.id));
            more = Math.Max(0, all.Count - MaxStickers);
            if (more > 0) all.RemoveRange(0, more);
            return all;
        }

        /// <summary>這張照片的貼紙位置：同一個 id 永遠一樣。</summary>
        public static StickerSpot Spot(string id)
        {
            uint s = Hash(id ?? "");
            if (s == 0) s = 0x9E3779B9u;
            var spot = new StickerSpot();
            spot.size = (float)Math.Round(MinSize + Next(ref s) * (MaxSize - MinSize));
            float minX = SideMargin, maxX = DesignW - SideMargin - spot.size;
            float minY = TopLimit, maxY = BottomLimit - spot.size;
            spot.x = (float)Math.Round(minX + Next(ref s) * (maxX - minX));
            spot.y = (float)Math.Round(minY + Next(ref s) * (maxY - minY));
            spot.rot = (float)Math.Round(MinRot + Next(ref s) * (MaxRot - MinRot));
            return spot;
        }

        /// <summary>FNV-1a 32 位元。</summary>
        public static uint Hash(string s)
        {
            uint h = 2166136261u;
            foreach (char c in s)
            {
                h ^= c;
                h *= 16777619u;
            }
            return h;
        }

        /// <summary>xorshift32 → 0～1。</summary>
        static double Next(ref uint s)
        {
            s ^= s << 13;
            s ^= s >> 17;
            s ^= s << 5;
            return (s & 0xFFFFFF) / (double)0x1000000;
        }
    }
}
