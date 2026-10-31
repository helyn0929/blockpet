using UnityEditor;
using UnityEditor.Callbacks;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
using UnityEditor.iOS.Xcode.Extensions; // AddAppExtension
#endif
using System.IO;
using UnityEngine;

/// <summary>
/// iOS 手機桌面小工具（設計文件 5.22，靜態版）：每次 Build iOS 時把 ios-widget/BlockPetWidget/ 複製進 Xcode 專案，
/// 加成 App Extension target「BlockPetWidget」並嵌入主 App。排在 iOSPostBuildSigning（999）之後。
/// Append 模式重複 Build 時，target 已經存在就只更新檔案和設定，不會再加一次。
/// </summary>
public class iOSWidgetPostBuild
{
#if UNITY_IOS
    const string TargetName = "BlockPetWidget";
    const string BundleId = "com.linnTech.blockpet.widget";
    const string TeamId = "9A62SU769D";            // 同 iOSPostBuildSigning
    const string MinIOS = "15.0";                  // 同專案最低 iOS；containerBackground 在 Swift 裡依版本判斷
    const string SwiftVersion = "5.0";
    /// <summary>App 和小工具共用資料的 App Group（同 WidgetSync.AppGroupId、BlockPetWidget.swift 的 appGroupId）</summary>
    const string AppGroup = "group.com.linnTech.blockpet";

    [PostProcessBuild(1000)]
    public static void OnPostProcessBuild(BuildTarget target, string buildPath)
    {
        if (target != BuildTarget.iOS) return;

        string src = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ios-widget", TargetName));
        if (!Directory.Exists(src))
        {
            Debug.LogWarning("[iOSWidget] 找不到小工具原始檔：" + src + "，略過。");
            return;
        }

        // ── 複製小工具檔案（每次覆蓋）──────────────────────────────
        string dst = Path.Combine(buildPath, TargetName);
        if (Directory.Exists(dst)) Directory.Delete(dst, true);
        CopyDirectory(src, dst);

        // 小工具的版本號要跟主 App 一樣（不一樣 Xcode 會警告、上架會被擋）
        SyncVersion(Path.Combine(buildPath, "Info.plist"), Path.Combine(dst, "Info.plist"));

        // ── Xcode target ──────────────────────────────────────────
        string pbxPath = PBXProject.GetPBXProjectPath(buildPath);
        var proj = new PBXProject();
        proj.ReadFromFile(pbxPath);

        string mainTarget = proj.GetUnityMainTargetGuid();
        string ext = FindTargetGuid(File.ReadAllText(pbxPath), TargetName);
        if (string.IsNullOrEmpty(ext))
        {
            ext = proj.AddAppExtension(mainTarget, TargetName, BundleId, TargetName + "/Info.plist");

            proj.AddSourcesBuildPhase(ext);
            proj.AddResourcesBuildPhase(ext);
            proj.AddFrameworksBuildPhase(ext);

            string swift = proj.AddFile(TargetName + "/BlockPetWidget.swift", TargetName + "/BlockPetWidget.swift");
            proj.AddFileToBuild(ext, swift);
            string assets = proj.AddFile(TargetName + "/Assets.xcassets", TargetName + "/Assets.xcassets");
            proj.AddFileToBuild(ext, assets);
            proj.AddFile(TargetName + "/Info.plist", TargetName + "/Info.plist"); // 只放進專案，不加進 build
            proj.AddFile(TargetName + "/BlockPetWidget.entitlements", TargetName + "/BlockPetWidget.entitlements");

            proj.AddFrameworkToProject(ext, "WidgetKit.framework", false);
            proj.AddFrameworkToProject(ext, "SwiftUI.framework", false);
            Debug.Log("[iOSWidget] 已加入 App Extension target：" + TargetName);
        }
        else
        {
            Debug.Log("[iOSWidget] " + TargetName + " target 已存在（Append），只更新檔案和設定。");
        }

        // ── Build settings（每次都設，重複設不會出事）───────────────
        try { proj.SetTeamId(ext, TeamId); }
        catch (System.ArgumentException) { } // Append 模式已有 TargetAttributes 時會丟 duplicate key，DEVELOPMENT_TEAM 已足夠
        proj.SetBuildProperty(ext, "CODE_SIGN_STYLE", "Automatic");
        proj.SetBuildProperty(ext, "DEVELOPMENT_TEAM", TeamId);
        proj.SetBuildProperty(ext, "PRODUCT_BUNDLE_IDENTIFIER", BundleId);
        proj.SetBuildProperty(ext, "INFOPLIST_FILE", TargetName + "/Info.plist");
        proj.SetBuildProperty(ext, "GENERATE_INFOPLIST_FILE", "NO");
        proj.SetBuildProperty(ext, "SWIFT_VERSION", SwiftVersion);
        proj.SetBuildProperty(ext, "IPHONEOS_DEPLOYMENT_TARGET", MinIOS);
        proj.SetBuildProperty(ext, "TARGETED_DEVICE_FAMILY", "1,2");
        proj.SetBuildProperty(ext, "SKIP_INSTALL", "YES");
        proj.SetBuildProperty(ext, "ENABLE_BITCODE", "NO");
        proj.SetBuildProperty(ext, "LD_RUNPATH_SEARCH_PATHS", "$(inherited) @executable_path/Frameworks @executable_path/../../Frameworks");
        proj.SetBuildProperty(ext, "ASSETCATALOG_COMPILER_GENERATE_SWIFT_ASSET_SYMBOL_EXTENSIONS", "NO");
        proj.SetBuildProperty(ext, "ENABLE_USER_SCRIPT_SANDBOXING", "NO");
        proj.SetBuildProperty(ext, "ALWAYS_SEARCH_USER_PATHS", "NO");
        proj.SetBuildProperty(ext, "CODE_SIGN_ENTITLEMENTS", TargetName + "/BlockPetWidget.entitlements");

        // Unity 的 Swift 外掛（Assets/Plugins/iOS/BlockPetWidgetReload.swift）編在 UnityFramework
        string framework = proj.GetUnityFrameworkTargetGuid();
        if (string.IsNullOrEmpty(proj.GetBuildPropertyForAnyConfig(framework, "SWIFT_VERSION")))
            proj.SetBuildProperty(framework, "SWIFT_VERSION", SwiftVersion);

        proj.WriteToFile(pbxPath);

        // 主 App 也要有同一個 App Group（iOSPostBuildSigning 已經建好 entitlements 檔，這裡只加這一項）
        AddAppGroup(Path.Combine(buildPath, "Unity-iPhone", "Unity-iPhone.entitlements"));
    }

    static void AddAppGroup(string entitlementsPath)
    {
        var doc = new PlistDocument();
        if (File.Exists(entitlementsPath)) doc.ReadFromFile(entitlementsPath);
        var groups = doc.root["com.apple.security.application-groups"] as PlistElementArray
                     ?? doc.root.CreateArray("com.apple.security.application-groups");
        foreach (var v in groups.values)
            if (v.AsString() == AppGroup) return;
        groups.AddString(AppGroup);
        doc.WriteToFile(entitlementsPath);
    }

    /// <summary>
    /// 找已經存在的 target（Append 模式）。這版 Unity 的 PBXProject 沒有公開的「依名稱找 target」，
    /// 所以直接在 project.pbxproj 裡找「GUID /* 名稱 */ = { isa = PBXNativeTarget;」。找不到回傳 null。
    /// </summary>
    static string FindTargetGuid(string pbxText, string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(pbxText,
            @"([0-9A-F]{24}) /\* " + System.Text.RegularExpressions.Regex.Escape(name) + @" \*/ = \{\s*isa = PBXNativeTarget;");
        return m.Success ? m.Groups[1].Value : null;
    }

    static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            string name = Path.GetFileName(file);
            if (name == ".DS_Store" || name.EndsWith(".meta")) continue;
            File.Copy(file, Path.Combine(to, name), true);
        }
        foreach (var dir in Directory.GetDirectories(from))
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    static void SyncVersion(string appPlistPath, string extPlistPath)
    {
        if (!File.Exists(appPlistPath) || !File.Exists(extPlistPath)) return;
        var app = new PlistDocument();
        app.ReadFromFile(appPlistPath);
        var ext = new PlistDocument();
        ext.ReadFromFile(extPlistPath);
        foreach (var key in new[] { "CFBundleShortVersionString", "CFBundleVersion" })
        {
            var v = app.root[key];
            if (v != null) ext.root.SetString(key, v.AsString());
        }
        ext.WriteToFile(extPlistPath);
    }
#endif
}
