using System;

/// <summary>
/// 相機拍照餵食與出照片牌共用的冷卻。
/// 開始時間存在 SaveData.lastCaptureTime（本機時間，每個房間各自一份）。
/// </summary>
public static class PhotoCooldown
{
    public const double DurationSeconds = 10 * 60; // 2026-10-29 改成 10 分鐘（原本 30 秒）

    public static double RemainingSeconds
    {
        get
        {
            var data = SaveManager.Instance?.data;
            if (data == null || string.IsNullOrEmpty(data.lastCaptureTime)) return 0;
            if (!DateTime.TryParse(data.lastCaptureTime, out DateTime last)) return 0;
            return Math.Max(0, DurationSeconds - (DateTime.Now - last).TotalSeconds);
        }
    }

    public static bool IsActive => RemainingSeconds > 0;

    /// <summary>剩餘時間，格式 "00:25"。</summary>
    public static string RemainingText
    {
        get
        {
            TimeSpan t = TimeSpan.FromSeconds(RemainingSeconds);
            return string.Format("{0:D2}:{1:D2}", t.Minutes, t.Seconds);
        }
    }

    public static void Start()
    {
        var sm = SaveManager.Instance;
        if (sm == null || sm.data == null) return;
        sm.data.lastCaptureTime = DateTime.Now.ToString();
        sm.Save();
    }
}
