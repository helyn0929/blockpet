using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// 輕震動回饋（設計文件 5.8 ⑥ 搖桿）：iOS 用觸覺回饋（Assets/Plugins/iOS/BlockPetHaptics.mm），
/// Android 用很短的震動，Editor 不做事。
/// </summary>
public static class Haptics
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    static extern void _BlockPet_Haptic(int style);
#endif

    /// <summary>輕（換寵物）。</summary>
    public static void Light() => Play(0, 12);

    /// <summary>中（推到位、進去）。</summary>
    public static void Medium() => Play(1, 30);

    static void Play(int iosStyle, long androidMs)
    {
#if UNITY_IOS && !UNITY_EDITOR
        _BlockPet_Haptic(iosStyle);
#elif UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
                vibrator?.Call("vibrate", androidMs);
        }
        catch (System.Exception e) { Debug.LogWarning("[Haptics] " + e.Message); }
#endif
    }

    // Unity 看到程式裡有用 Handheld.Vibrate 才會在 Android 加上 VIBRATE 權限；這個方法不會被呼叫
    static void RequireVibratePermission() => Handheld.Vibrate();
}
