using System.Runtime.InteropServices;
using UnityEngine;

namespace BlockPet.Postcard
{
    /// <summary>
    /// 國家代碼（抽好友卡片上的國家）：用手機的地區設定（2026-11-08 使用者決定；不用定位權限，只上傳國家代碼，例 TW）。
    /// iOS：NSLocale countryCode（BlockPetFaceDetect.mm）；Android：Locale.getDefault().getCountry()；Editor：作業系統的地區。
    /// </summary>
    public static class DeviceRegion
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern string _BlockPet_CountryCode();
#endif
        static string _cached;

        public static string CountryCode()
        {
            if (_cached != null) return _cached;
            string code = "";
            try
            {
#if UNITY_IOS && !UNITY_EDITOR
                code = _BlockPet_CountryCode();
#elif UNITY_ANDROID && !UNITY_EDITOR
                using (var locale = new AndroidJavaClass("java.util.Locale"))
                using (var def = locale.CallStatic<AndroidJavaObject>("getDefault"))
                    code = def.Call<string>("getCountry");
#else
                code = System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName;
#endif
            }
            catch { code = ""; }
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length != 2) code = ""; // 不是兩個字母（例如 Editor 的 IV）就不顯示
            return _cached = code;
        }
    }
}
