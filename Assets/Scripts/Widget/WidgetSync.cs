using System;
using System.Globalization;
using System.IO;
using System.Text;
using BlockPet.Feed;
using BlockPet.UI.Chat;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace BlockPet.Widget
{
    /// <summary>
    /// 把目前營地的資料交給 iOS 手機桌面小工具（設計文件 5.22）：寫進 App Group 共用資料夾的 widget/，
    /// 小工具（ios-widget/BlockPetWidget）讀這裡的 state.json 和 latest.jpg。
    /// - state.json：寵物名字、愛心（＋下一次掉心的時間，小工具自己往下算）、進化條各色、最新照片的誰拍的／什麼時候拍的。
    /// - latest.jpg：營地最新照片，置中裁成正方形 600px。
    /// 有新照片、換空間、餵食結算、App 退到背景時寫一次，內容有變才通知 iOS 重畫小工具；開著 App 時每 5 分鐘檢查一次。
    /// 只在 iOS 實機有作用（Editor、Android 沒有小工具）。執行時自動建立。
    /// </summary>
    public class WidgetSync : MonoBehaviour
    {
        public const string AppGroupId = "group.com.linnTech.blockpet";
        const int PhotoSize = 600;
        const float PeriodicSeconds = 300f;
        const float Debounce = 2f;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern string _BlockPet_AppGroupPath(string groupId);
        [DllImport("__Internal")] static extern void _BlockPet_ReloadWidgets();
#endif

        static WidgetSync _instance;
        string _dir;
        string _lastJson, _lastPhoto;
        float _dueAt = -1f, _nextPeriodic;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (_instance != null) return;
            var go = new GameObject("WidgetSync (auto)");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<WidgetSync>();
#endif
        }

        void Awake()
        {
            SaveManager.OnSaveDataChanged += Schedule;
            SaveManager.OnRoomSwitched += Schedule;
            FirebaseManager.OnRoomChanged += Schedule;
            FeedSettlement.OnSettled += OnSettled;
        }

        void OnDestroy()
        {
            SaveManager.OnSaveDataChanged -= Schedule;
            SaveManager.OnRoomSwitched -= Schedule;
            FirebaseManager.OnRoomChanged -= Schedule;
            FeedSettlement.OnSettled -= OnSettled;
            if (_instance == this) _instance = null;
        }

        void OnSettled(FeedResult r) => Schedule();

        /// <summary>資料變了：等一下再寫（連續變動只寫一次）。</summary>
        void Schedule()
        {
            if (_dueAt < 0f) _dueAt = Time.realtimeSinceStartup + Debounce;
        }

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            if ((_dueAt >= 0f && now >= _dueAt) || now >= _nextPeriodic)
            {
                _dueAt = -1f;
                _nextPeriodic = now + PeriodicSeconds;
                Write();
            }
        }

        // 退到背景時一定寫一次（使用者接著就會看到桌面）
        void OnApplicationPause(bool paused) { if (paused) Write(); }

        void Write()
        {
            try
            {
                string dir = Dir();
                if (dir == null) return;
                var fb = FirebaseManager.Instance;
                if (fb == null || string.IsNullOrEmpty(fb.RoomId)) return;

                // 最新照片：檔名變了才重新產生
                var latest = Latest();
                bool photoChanged = false;
                string photoKey = latest != null ? fb.RoomId + "/" + latest.fileName : null;
                if (photoKey != _lastPhoto)
                {
                    string path = Path.Combine(dir, "latest.jpg");
                    if (latest != null && WritePhoto(latest, path)) photoChanged = true;
                    else if (latest == null && File.Exists(path)) { File.Delete(path); photoChanged = true; }
                    _lastPhoto = photoKey;
                }

                string json = StateJson(fb, latest);
                if (json != _lastJson || photoChanged)
                {
                    WriteAtomic(Path.Combine(dir, "state.json"), Encoding.UTF8.GetBytes(json));
                    _lastJson = json;
                    Reload();
                }
            }
            catch (Exception e) { Debug.LogWarning("[WidgetSync] " + e.Message); }
        }

        string Dir()
        {
            if (_dir != null) return _dir;
#if UNITY_IOS && !UNITY_EDITOR
            string root = _BlockPet_AppGroupPath(AppGroupId);
            if (string.IsNullOrEmpty(root)) { Debug.LogWarning("[WidgetSync] 拿不到 App Group 資料夾（Xcode 的 App Groups 有沒有勾 " + AppGroupId + "？）"); return null; }
            _dir = Path.Combine(root, "widget");
            Directory.CreateDirectory(_dir);
#endif
            return _dir;
        }

        static void Reload()
        {
#if UNITY_IOS && !UNITY_EDITOR
            _BlockPet_ReloadWidgets();
#endif
        }

        static PhotoMeta Latest()
        {
            var photos = SaveManager.Instance?.data?.photos;
            if (photos == null) return null;
            PhotoMeta best = null;
            DateTime bestT = DateTime.MinValue;
            foreach (var p in photos)
            {
                if (p == null || string.IsNullOrEmpty(p.fileName)) continue;
                DateTime.TryParse(p.timestamp, out var t);
                if (best == null || t > bestT) { best = p; bestT = t; }
            }
            return best;
        }

        static bool WritePhoto(PhotoMeta meta, string path)
        {
            var full = SaveManager.Instance != null ? SaveManager.Instance.LoadPhoto(meta) : null;
            if (full == null) return false;
            try
            {
                // 置中裁成正方形、縮成 600px
                int side = Mathf.Min(full.width, full.height);
                var scale = new Vector2((float)side / full.width, (float)side / full.height);
                var offset = new Vector2((1f - scale.x) / 2f, (1f - scale.y) / 2f);
                int size = Mathf.Min(PhotoSize, side);
                var rt = RenderTexture.GetTemporary(size, size);
                Graphics.Blit(full, rt, scale, offset);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var sq = new Texture2D(size, size, TextureFormat.RGB24, false);
                sq.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                sq.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                WriteAtomic(path, sq.EncodeToJPG(85));
                Destroy(sq);
                return true;
            }
            finally { Destroy(full); }
        }

        static string StateJson(FirebaseManager fb, PhotoMeta latest)
        {
            var health = FindObjectOfType<PetHealthManager>();
            float hearts = health != null ? health.Hearts : fb.GetRoomHeartsNow();
            if (hearts < 0f) hearts = PetHearts.Max;
            var next = fb.GetRoomHeartsNextDrop();
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long nextDropAt = next > TimeSpan.Zero ? nowMs + (long)next.TotalMilliseconds : 0;

            var pets = PetCollectionManager.Instance;
            float[] evo = pets != null ? pets.EnergyByCategory(PetPreferences.Categories) : new float[PetPreferences.Categories.Length];

            var sb = new StringBuilder();
            sb.Append('{');
            Prop(sb, "version", 1); sb.Append(',');
            Prop(sb, "roomId", fb.RoomId); sb.Append(',');
            Prop(sb, "petName", pets != null ? pets.GetPetDisplayName() : ""); sb.Append(',');
            Prop(sb, "hearts", hearts); sb.Append(',');
            Prop(sb, "nextDropAt", nextDropAt); sb.Append(',');
            Prop(sb, "dropHours", (float)PetHearts.DecayHours); sb.Append(',');
            sb.Append("\"evolution\":[");
            for (int i = 0; i < evo.Length; i++) { if (i > 0) sb.Append(','); sb.Append(evo[i].ToString("0.##", CultureInfo.InvariantCulture)); }
            sb.Append(']');
            if (latest != null)
            {
                string me = fb.GetUserId();
                bool mine = !string.IsNullOrEmpty(me) && latest.takerUserId == me;
                string who = mine ? "我" : (string.IsNullOrEmpty(latest.takerName) ? "營地" : latest.takerName);
                DateTime.TryParse(latest.timestamp, out var t);
                long takenAt = t == default ? 0 : new DateTimeOffset(t).ToUnixTimeSeconds();
                sb.Append(",\"photo\":{");
                Prop(sb, "file", "latest.jpg"); sb.Append(',');
                Prop(sb, "who", who); sb.Append(',');
                Prop(sb, "initial", ChatScreenController.Initial(who, mine)); sb.Append(',');
                Prop(sb, "color", "#" + ColorUtility.ToHtmlStringRGB(ChatScreenController.PersonColor(who, mine))); sb.Append(',');
                Prop(sb, "takenAt", takenAt);
                sb.Append('}');
            }
            sb.Append('}');
            return sb.ToString();
        }

        static void Prop(StringBuilder sb, string key, string value)
        {
            sb.Append('"').Append(key).Append("\":\"");
            foreach (char c in value ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 0x20) sb.Append(' ');
                else sb.Append(c);
            }
            sb.Append('"');
        }

        static void Prop(StringBuilder sb, string key, long value) => sb.Append('"').Append(key).Append("\":").Append(value);
        static void Prop(StringBuilder sb, string key, float value) => sb.Append('"').Append(key).Append("\":").Append(value.ToString("0.##", CultureInfo.InvariantCulture));

        static void WriteAtomic(string path, byte[] bytes)
        {
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }
}
