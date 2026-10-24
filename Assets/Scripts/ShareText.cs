using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// 用手機的分享面板分享一段文字（iOS：Assets/Plugins/iOS/BlockPetShare.mm；Android：系統分享選單）。
/// Editor 沒有分享面板，改成複製到剪貼簿並回傳 false，讓呼叫端提示「已複製」。
/// </summary>
public static class ShareText
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    static extern void _BlockPet_ShareText(string text);
#endif

    /// <summary>打開分享面板；回傳 false 表示沒有分享面板、已改成複製文字。</summary>
    public static bool Send(string text, string chooserTitle = "分享給朋友")
    {
        if (string.IsNullOrEmpty(text)) return false;
#if UNITY_IOS && !UNITY_EDITOR
        _BlockPet_ShareText(text);
        return true;
#elif UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var intent = new AndroidJavaObject("android.content.Intent"))
            using (var intentClass = new AndroidJavaClass("android.content.Intent"))
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                intent.Call<AndroidJavaObject>("setAction", "android.intent.action.SEND");
                intent.Call<AndroidJavaObject>("setType", "text/plain");
                intent.Call<AndroidJavaObject>("putExtra", "android.intent.extra.TEXT", text);
                var chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, chooserTitle);
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                activity.Call("startActivity", chooser);
            }
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[ShareText] Android share failed: " + e.Message);
            GUIUtility.systemCopyBuffer = text;
            return false;
        }
#else
        GUIUtility.systemCopyBuffer = text;
        return false;
#endif
    }
}
