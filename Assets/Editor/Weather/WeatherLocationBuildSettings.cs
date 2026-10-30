using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif
using UnityEngine;

namespace BlockPet.EditorTools.Weather
{
    /// <summary>
    /// 天氣用的定位設定（設計文件 5.20）：每次 Build 前自動套用，不用手動改 Player Settings。
    /// - iOS：權限視窗的說明文字；Info.plist 加 NSLocationDefaultAccuracyReduced，權限視窗預設只給「大概位置」。
    /// - Android：Low Accuracy Location（只要 ACCESS_COARSE_LOCATION）。
    /// </summary>
    public class WeatherLocationBuildSettings : IPreprocessBuildWithReport
    {
        /// <summary>iOS 要定位權限時跳出來的說明文字。</summary>
        public const string UsageDescription = "BlockPet 用你的大概位置查當地天氣，讓營地跟著下雨下雪。位置只留在手機上，不會上傳或給其他成員看。";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => Apply();

        [MenuItem("BlockPet/Weather/Apply Location Settings")]
        public static void Apply()
        {
            if (PlayerSettings.iOS.locationUsageDescription != UsageDescription)
                PlayerSettings.iOS.locationUsageDescription = UsageDescription;

            // Android「Low Accuracy Location」沒有公開 API，直接改 PlayerSettings 的序列化欄位
            var ps = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
            if (ps != null)
            {
                var so = new SerializedObject(ps);
                var p = so.FindProperty("androidUseLowAccuracyLocation");
                if (p != null && !p.boolValue)
                {
                    p.boolValue = true;
                    so.ApplyModifiedProperties();
                }
            }
            AssetDatabase.SaveAssets();
        }

#if UNITY_IOS
        // 在 iOSPostBuildSigning（999）、小工具（1000）之後
        [PostProcessBuild(1001)]
        public static void OnPostProcessBuild(BuildTarget target, string buildPath)
        {
            if (target != BuildTarget.iOS) return;
            string plistPath = Path.Combine(buildPath, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetString("NSLocationWhenInUseUsageDescription", UsageDescription);
            plist.root.SetBoolean("NSLocationDefaultAccuracyReduced", true);
            plist.WriteToFile(plistPath);
        }
#endif
    }
}
