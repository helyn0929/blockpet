using UnityEngine;

namespace BlockPet.UI.Home
{
    /// <summary>
    /// 進化後的樣子（設計文件 5.17 ④-2 第 7 點【暫定，待美術】）：三種身體還沒有美術圖，先用現在的寵物圖＋主色上色；
    /// 彩虹型用五色慢慢輪替（4 秒一輪）。只改顏色的 RGB，透明度留給別的系統（佈置時半透明等）。
    /// 掛在寵物（AIController）上，由 EvolutionFlow 設定。
    /// </summary>
    public class EvolvedLook : MonoBehaviour
    {
        /// <summary>五大類的色點（同 5.5 進化條）：美食、自然、城市、生活人物、寵物。</summary>
        public static readonly Color[] CategoryColors =
        {
            new Color(0xF2 / 255f, 0x9A / 255f, 0x3A / 255f),
            new Color(0x6F / 255f, 0xA4 / 255f, 0x5F / 255f),
            new Color(0x6F / 255f, 0x8F / 255f, 0xE0 / 255f),
            new Color(0xD7 / 255f, 0x8A / 255f, 0xD0 / 255f),
            new Color(0xE8 / 255f, 0xC3 / 255f, 0x3A / 255f),
        };

        const float TintStrength = 0.45f;   // 和白色混多少（全上色太重）
        const float RainbowCycle = 4f;

        public FirebaseManager.EvolutionRecord Record { get; set; }

        SpriteRenderer[] _renderers;
        bool _tinted;

        /// <summary>這個結果在 time 時的上色（白色＝不上色）。</summary>
        public static Color TintFor(FirebaseManager.EvolutionRecord r, float time)
        {
            if (r == null) return Color.white;
            if (r.type == BlockPet.Feed.DexRules.Rainbow)
            {
                float f = Mathf.Repeat(time / RainbowCycle, 1f) * CategoryColors.Length;
                int a = (int)f % CategoryColors.Length, b = (a + 1) % CategoryColors.Length;
                return Color.Lerp(Color.white, Color.Lerp(CategoryColors[a], CategoryColors[b], f - Mathf.Floor(f)), TintStrength);
            }
            return Color.Lerp(Color.white, CategoryColors[Mathf.Clamp(r.main, 0, CategoryColors.Length - 1)], TintStrength);
        }

        void LateUpdate()
        {
            if (Record == null && !_tinted) return;
            if (_renderers == null || _renderers.Length == 0) _renderers = GetComponentsInChildren<SpriteRenderer>(true);
            var tint = TintFor(Record, Time.unscaledTime);
            foreach (var sr in _renderers)
            {
                if (sr == null) continue;
                var c = sr.color;
                sr.color = new Color(tint.r, tint.g, tint.b, c.a);
            }
            _tinted = Record != null;
        }
    }
}
