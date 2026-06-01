using UnityEditor;
using UnityEditor.Callbacks;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif
using System.IO;

public class iOSPostBuildSigning
{
#if UNITY_IOS
    [PostProcessBuild(999)]
    public static void OnPostProcessBuild(BuildTarget target, string buildPath)
    {
        if (target != BuildTarget.iOS) return;

        // ── Code signing ─────────────────────────────────────────────
        string pbxPath = PBXProject.GetPBXProjectPath(buildPath);
        PBXProject proj = new PBXProject();
        proj.ReadFromFile(pbxPath);

        string teamId = "9A62SU769D";
        string mainTarget = proj.GetUnityMainTargetGuid();
        string frameworkTarget = proj.GetUnityFrameworkTargetGuid();

        foreach (string t in new[] { mainTarget, frameworkTarget })
        {
            proj.SetTeamId(t, teamId);
            proj.SetBuildProperty(t, "CODE_SIGN_STYLE", "Automatic");
            proj.SetBuildProperty(t, "DEVELOPMENT_TEAM", teamId);
        }

        proj.WriteToFile(pbxPath);

        // ── Google Sign-In URL scheme (REVERSED_CLIENT_ID) ───────────
        string infoPlistPath = Path.Combine(buildPath, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(infoPlistPath);

        const string reversedClientId = "com.googleusercontent.apps.39782728703-9kf7f2feei6ot7qu2lh16b60c7tn8su9";

        var urlTypes = plist.root["CFBundleURLTypes"] as PlistElementArray
                       ?? plist.root.CreateArray("CFBundleURLTypes");

        // Avoid adding duplicates on rebuild.
        bool alreadyAdded = false;
        foreach (var item in urlTypes.values)
        {
            var dict = item as PlistElementDict;
            if (dict == null) continue;
            var schemes = dict["CFBundleURLSchemes"] as PlistElementArray;
            if (schemes == null) continue;
            foreach (var s in schemes.values)
                if (s.AsString() == reversedClientId) { alreadyAdded = true; break; }
            if (alreadyAdded) break;
        }

        if (!alreadyAdded)
        {
            var entry = urlTypes.AddDict();
            entry.SetString("CFBundleTypeRole", "Editor");
            var schemes = entry.CreateArray("CFBundleURLSchemes");
            schemes.AddString(reversedClientId);
        }

        plist.WriteToFile(infoPlistPath);

        // ── Sign In with Apple entitlement ────────────────────────────
        string entitlementsPath = Path.Combine(buildPath, "Unity-iPhone", "Unity-iPhone.entitlements");
        var entitlements = new PlistDocument();
        if (File.Exists(entitlementsPath))
            entitlements.ReadFromFile(entitlementsPath);

        var appleSignIn = entitlements.root["com.apple.developer.applesignin"] as PlistElementArray
                          ?? entitlements.root.CreateArray("com.apple.developer.applesignin");

        bool hasDefault = false;
        foreach (var v in appleSignIn.values)
            if (v.AsString() == "Default") { hasDefault = true; break; }
        if (!hasDefault)
            appleSignIn.AddString("Default");

        entitlements.WriteToFile(entitlementsPath);

        proj.ReadFromFile(pbxPath);
        proj.SetBuildProperty(mainTarget, "CODE_SIGN_ENTITLEMENTS", "Unity-iPhone/Unity-iPhone.entitlements");
        proj.WriteToFile(pbxPath);
    }
#endif
}
