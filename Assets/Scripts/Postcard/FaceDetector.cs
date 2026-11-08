using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace BlockPet.Postcard
{
    /// <summary>
    /// 照片有沒有人臉（設計文件 5.21：有人臉的照片不能寄）：iOS Vision（BlockPetFaceDetect.mm）、Android ML Kit（BlockPetFaceDetect.java，
    /// 依賴寫在 Assets/Editor/Postcard/PostcardDependencies.xml）。在手機上算、不上傳。
    /// 一張一張排隊做，結果記在手機上（PlayerPrefs，同一張照片不再算）。Editor 沒有偵測：當作沒有臉。
    /// </summary>
    public class FaceDetector : MonoBehaviour
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void _BlockPet_DetectFaces(string gameObjectName, string requestId, string path);
#endif
        const string Prefs = "BlockPet.Face.";
        static FaceDetector _instance;
        readonly Queue<(string file, string path)> _queue = new Queue<(string, string)>();
        readonly Dictionary<string, List<Action<bool>>> _waiting = new Dictionary<string, List<Action<bool>>>();
        string _busy;
        float _busySince;

        /// <summary>已經知道結果了嗎（has＝有沒有臉）。</summary>
        public static bool TryGetCached(string fileName, out bool has)
        {
            int v = PlayerPrefs.GetInt(Prefs + fileName, -1);
            has = v == 1;
            return v >= 0;
        }

        /// <summary>檢查一張照片（done 在主執行緒，true＝有人臉）。</summary>
        public static void Check(PhotoMeta meta, Action<bool> done)
        {
            if (meta == null || string.IsNullOrEmpty(meta.fileName)) { done?.Invoke(false); return; }
            if (TryGetCached(meta.fileName, out bool has)) { done?.Invoke(has); return; }
#if UNITY_EDITOR || !(UNITY_IOS || UNITY_ANDROID)
            done?.Invoke(false); // Editor 沒有人臉偵測
#else
            var me = Instance();
            if (me._waiting.TryGetValue(meta.fileName, out var list)) { list.Add(done); return; }
            me._waiting[meta.fileName] = new List<Action<bool>> { done };
            me._queue.Enqueue((meta.fileName, Path.Combine(Application.persistentDataPath, "photos", meta.fileName)));
            me.Next();
#endif
        }

        static FaceDetector Instance()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("FaceDetector (runtime)");
            DontDestroyOnLoad(go);
            return _instance = go.AddComponent<FaceDetector>();
        }

        void Update()
        {
            // 原生端沒回應（例如照片讀不到）：10 秒後當作沒有臉，換下一張
            if (_busy != null && Time.unscaledTime - _busySince > 10f) Finish(_busy, false, false);
        }

        void Next()
        {
            if (_busy != null || _queue.Count == 0) return;
            var (file, path) = _queue.Dequeue();
            if (!File.Exists(path)) { Finish(file, false, false); return; }
            _busy = file;
            _busySince = Time.unscaledTime;
            try
            {
#if UNITY_IOS && !UNITY_EDITOR
                _BlockPet_DetectFaces(gameObject.name, file, path);
#elif UNITY_ANDROID && !UNITY_EDITOR
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var cls = new AndroidJavaClass("com.blockpet.facedetect.BlockPetFaceDetect"))
                    cls.CallStatic("detect", activity, gameObject.name, file, path);
#endif
            }
            catch (Exception e) { Debug.LogWarning("[FaceDetector] 人臉偵測失敗：" + e.Message); Finish(file, false, false); }
        }

        /// <summary>原生端回傳：「檔名|1」有臉、「檔名|0」沒有、「檔名|-1」失敗。</summary>
        public void OnFaceResult(string msg)
        {
            int bar = msg != null ? msg.LastIndexOf('|') : -1;
            if (bar < 0) return;
            string file = msg.Substring(0, bar), v = msg.Substring(bar + 1);
            Finish(file, v == "1", v != "-1");
        }

        void Finish(string file, bool has, bool remember)
        {
            if (remember) { PlayerPrefs.SetInt(Prefs + file, has ? 1 : 0); PlayerPrefs.Save(); }
            if (_busy == file) _busy = null;
            if (_waiting.TryGetValue(file, out var list))
            {
                _waiting.Remove(file);
                foreach (var cb in list) cb?.Invoke(has);
            }
            Next();
        }
    }
}
