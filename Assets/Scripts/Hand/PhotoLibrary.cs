using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace BlockPet.Hand
{
    /// <summary>手牌用的一張相簿照片（已複製到 app 暫存資料夾的本地檔案）。</summary>
    [Serializable]
    public class HandPhoto
    {
        public string id;
        public string path;
        public long takenAtMs;
    }

    public enum PhotoAccess { Granted, Denied }

    /// <summary>
    /// 讀取手機相簿最新的相機照片（過濾截圖），給手牌使用。
    /// - iOS：Assets/Plugins/iOS/BlockPetPhotoLibrary.mm（Photos framework）
    /// - Android：Assets/Plugins/Android/BlockPetPhotoLibrary.java（MediaStore，只取 DCIM）
    /// - Editor：讀取專案根目錄的 EditorHandPhotos/ 資料夾，方便不用手機測試
    /// 權限詢問交給 NativeGallery。原生端在背景執行緒取檔，完成後用 UnitySendMessage 回到這個物件。
    /// </summary>
    public class PhotoLibrary : MonoBehaviour
    {
        const string GameObjectName = "BlockPetPhotoLibrary";

        static PhotoLibrary _instance;
        public static PhotoLibrary Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject(GameObjectName);
                    _instance = go.AddComponent<PhotoLibrary>();
                }
                return _instance;
            }
        }

        /// <summary>Editor 測試用照片資料夾（專案根目錄，不在 Assets 底下，不會被匯入）。</summary>
        public static string EditorFolder => Path.Combine(Path.GetDirectoryName(Application.dataPath), "EditorHandPhotos");

        readonly List<Action<PhotoAccess, List<HandPhoto>>> _pending = new List<Action<PhotoAccess, List<HandPhoto>>>();
        bool _fetching;

        [Serializable]
        class NativeResult { public List<HandPhoto> items; }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void _BlockPet_FetchRecentPhotos(string gameObjectName, int limit, double sinceUnixSeconds, int maxEdge);
#endif

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            // UnitySendMessage 用名稱找物件，名稱必須固定
            gameObject.name = GameObjectName;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        public static bool HasPermission()
        {
#if UNITY_EDITOR
            return true;
#else
            return NativeGallery.CheckPermission(NativeGallery.PermissionType.Read, NativeGallery.MediaType.Image) == NativeGallery.Permission.Granted;
#endif
        }

        public static void OpenSettings() => NativeGallery.OpenSettings();

        /// <summary>
        /// 取最新 <paramref name="count"/> 張、<paramref name="recentDays"/> 天內的相機照片（新到舊）。
        /// 沒權限時會先詢問。done 在主執行緒呼叫；同時有多個請求時合併成一次。
        /// </summary>
        public void FetchRecent(int count, int recentDays, Action<PhotoAccess, List<HandPhoto>> done)
        {
            _pending.Add(done);
            if (_fetching) return;
            _fetching = true;

#if UNITY_EDITOR
            Finish(PhotoAccess.Granted, ReadEditorFolder(count));
#else
            NativeGallery.RequestPermissionAsync(permission =>
            {
                if (permission != NativeGallery.Permission.Granted)
                {
                    Finish(PhotoAccess.Denied, new List<HandPhoto>());
                    return;
                }
                StartNativeFetch(count, recentDays);
            }, NativeGallery.PermissionType.Read, NativeGallery.MediaType.Image);
#endif
        }

        void StartNativeFetch(int count, int recentDays)
        {
            long since = DateTimeOffset.UtcNow.AddDays(-recentDays).ToUnixTimeSeconds();
#if UNITY_IOS && !UNITY_EDITOR
            _BlockPet_FetchRecentPhotos(GameObjectName, count, since, 1024);
#elif UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var lib = new AndroidJavaClass("com.blockpet.photolib.BlockPetPhotoLibrary"))
                {
                    lib.CallStatic("fetchRecentPhotos", activity, GameObjectName, count, since);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[PhotoLibrary] Android fetch failed: " + e.Message);
                Finish(PhotoAccess.Granted, new List<HandPhoto>());
            }
#else
            Finish(PhotoAccess.Granted, new List<HandPhoto>());
#endif
        }

        /// <summary>原生端 UnitySendMessage 回呼。</summary>
        void OnNativePhotosFetched(string json)
        {
            List<HandPhoto> items = null;
            try { items = JsonUtility.FromJson<NativeResult>(json)?.items; }
            catch (Exception e) { Debug.LogError("[PhotoLibrary] Parse failed: " + e.Message); }
            Finish(PhotoAccess.Granted, items ?? new List<HandPhoto>());
        }

        void Finish(PhotoAccess access, List<HandPhoto> photos)
        {
            _fetching = false;
            var callbacks = new List<Action<PhotoAccess, List<HandPhoto>>>(_pending);
            _pending.Clear();
            foreach (var cb in callbacks)
            {
                try { cb?.Invoke(access, photos); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

#if UNITY_EDITOR
        // Editor 不套用天數限制（測試照片多半是舊檔），只依檔案修改時間排序並過濾截圖檔名。
        static List<HandPhoto> ReadEditorFolder(int count)
        {
            var result = new List<HandPhoto>();
            string dir = EditorFolder;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                Debug.Log($"[PhotoLibrary] Editor 測試：把 jpg/png 放進 {dir} 就會出現在手牌。");
                return result;
            }

            var files = new List<FileInfo>();
            foreach (var f in new DirectoryInfo(dir).GetFiles())
            {
                string ext = f.Extension.ToLowerInvariant();
                if (ext != ".jpg" && ext != ".jpeg" && ext != ".png") continue;
                if (IsScreenshotName(f.Name)) continue;
                files.Add(f);
            }
            files.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));

            for (int i = 0; i < files.Count && result.Count < count; i++)
            {
                result.Add(new HandPhoto
                {
                    id = files[i].Name,
                    path = files[i].FullName,
                    takenAtMs = new DateTimeOffset(files[i].LastWriteTimeUtc).ToUnixTimeMilliseconds(),
                });
            }
            return result;
        }

        static bool IsScreenshotName(string name)
        {
            string n = name.ToLowerInvariant();
            return n.Contains("screenshot") || n.Contains("screen shot") || n.Contains("截圖") || n.Contains("螢幕快照") || n.Contains("截屏");
        }
#endif
    }
}
