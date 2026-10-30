using BlockPet.Weather;
using UnityEditor;
using UnityEngine;

namespace BlockPet.EditorTools.Weather
{
    /// <summary>
    /// 天氣測試（設計文件 5.20）：Play 時強制切換天氣／白天晚上，或用台北座標查一次真的天氣。
    /// Editor 沒有定位，平常一直是晴天。
    /// </summary>
    public static class WeatherDebugMenu
    {
        const string Root = "BlockPet/Debug/天氣/";

        [MenuItem(Root + "用真的天氣（取消覆寫）", false, 0)]
        static void UseReal() { WeatherService.Override = null; WeatherService.NightOverride = null; Log(); }

        [MenuItem(Root + "晴天", false, 20)] static void Clear() => Set(WeatherKind.Clear);
        [MenuItem(Root + "多雲", false, 21)] static void Cloudy() => Set(WeatherKind.Cloudy);
        [MenuItem(Root + "下雨", false, 22)] static void Rain() => Set(WeatherKind.Rain);
        [MenuItem(Root + "雷雨", false, 23)] static void Thunder() => Set(WeatherKind.Thunder);
        [MenuItem(Root + "下雪", false, 24)] static void Snow() => Set(WeatherKind.Snow);
        [MenuItem(Root + "起霧", false, 25)] static void Fog() => Set(WeatherKind.Fog);

        [MenuItem(Root + "白天", false, 40)] static void Day() { WeatherService.NightOverride = false; Log(); }
        [MenuItem(Root + "晚上", false, 41)] static void Night() { WeatherService.NightOverride = true; Log(); }

        [MenuItem(Root + "用台北座標查一次真的天氣", false, 60)]
        static void FetchTaipei()
        {
            if (!Application.isPlaying || WeatherService.Instance == null) { Debug.LogWarning("[Weather] 先按 Play"); return; }
            WeatherService.Override = null;
            WeatherService.Instance.DebugFetchAt(25.03f, 121.56f);
            Debug.Log("[Weather] 已送出查詢，幾秒後用「顯示目前天氣」看結果");
        }

        [MenuItem(Root + "顯示目前天氣", false, 61)]
        static void Log() => Debug.Log($"[Weather] 目前：{WeatherService.Current}・{(WeatherService.IsNight ? "晚上" : "白天")}　狀態：{WeatherService.Status}");

        static void Set(WeatherKind kind)
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Weather] 先按 Play"); return; }
            WeatherService.Override = kind;
            Log();
        }
    }
}
