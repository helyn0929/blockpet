using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Sentis;
using UnityEngine;

namespace BlockPet.Vision
{
    /// <summary>照片辨識結果：五大類之一＋信心（0～1）。</summary>
    public struct PhotoCategoryResult
    {
        public string category;   // 美食／自然／城市／生活人物／寵物；辨識失敗是 null
        public float confidence;
        public float[] scores;    // 依 PhotoClassifier.Categories 順序
        public bool Ok => !string.IsNullOrEmpty(category);
    }

    /// <summary>
    /// 照片辨識（第 3 步）：TinyCLIP（Microsoft，MIT 授權）的影像模型在手機上跑（Unity Sentis），不連網。
    /// 照片 → 512 維向量；和每一類每句描述的文字向量（photo_categories.json，事先在電腦上算好）比相似度，
    /// 每類取最像的一句當分數 → softmax → 最高的那類。
    /// 前處理照 TinyCLIP：短邊縮到 224、置中裁正方形、0～1、減 mean 除 std。
    /// 在 CPU 上直接讀照片原始像素做裁切縮放（每格 4×4 取平均），避免 Linear 色彩空間在 GPU 取樣時把顏色變暗。
    /// 模型與向量由 docs/vision/Tools/build_photo_classifier.py 產生；換模型、改句子都只改那邊，這裡不用動。
    /// 舊照片補辨識：進房間、相簿有變動時，在背景一幀一張把還沒有類別、或是用舊版類別辨識的照片重新辨識，
    /// 結果寫回相簿、營地照片記錄與聊天室貼文（2026-10-29 運動戶外併入自然、新增寵物，版本 2）。
    /// </summary>
    public class PhotoClassifier : MonoBehaviour
    {
        [SerializeField] ModelAsset model;
        [Tooltip("Assets/Resources/Vision/photo_categories.json")]
        [SerializeField] TextAsset categories;
        [Tooltip("有 compute shader 的裝置用 GPU，否則用 CPU")]
        [SerializeField] bool preferGpu = true;

        public static PhotoClassifier Instance { get; private set; }

        /// <summary>模型與類別向量放在 Resources/Vision：場景裡沒有 PhotoClassifier 時，遊戲開始就自動建一個（不用手動執行 Build）。</summary>
        const string ResModel = "Vision/photo_classifier_vision";
        const string ResCategories = "Vision/photo_categories";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null || FindObjectOfType<PhotoClassifier>() != null) return;
            var go = new GameObject("PhotoClassifier (auto)");
            DontDestroyOnLoad(go);
            go.AddComponent<PhotoClassifier>();
        }

        [Serializable]
        class CategoryJson { public string name; public string[] prompts; public float[] embeddings; }

        [Serializable]
        class FileJson
        {
            public string model;
            public string version;   // 類別與句子的版本；改了就會重新辨識舊照片
            public int inputSize = 224;
            public float[] mean;
            public float[] std;
            public float logitScale = 50f;
            public int dim = 512;
            public CategoryJson[] categories;
        }

        FileJson _cfg;
        Worker _worker;
        bool _loaded;

        /// <summary>目前類別資料的版本（舊資料沒有版本時是 "1"）。</summary>
        public string Version
        {
            get
            {
                EnsureLoaded();
                return _cfg == null ? null : (string.IsNullOrEmpty(_cfg.version) ? "1" : _cfg.version);
            }
        }

        public IReadOnlyList<string> Categories
        {
            get
            {
                EnsureLoaded();
                var list = new List<string>();
                if (_cfg?.categories != null) foreach (var c in _cfg.categories) list.Add(c.name);
                return list;
            }
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            SaveManager.OnRoomSwitched += KickBackfill;
            SaveManager.OnSaveDataChanged += KickBackfill;
            KickBackfill();
        }

        void OnDestroy()
        {
            SaveManager.OnRoomSwitched -= KickBackfill;
            SaveManager.OnSaveDataChanged -= KickBackfill;
            ReleaseWorker();
            if (Instance == this) Instance = null;
        }

        // ─── 舊照片補辨識 ──────────────────────────────────────────────

        Coroutine _backfill;
        readonly HashSet<string> _backfillFailed = new HashSet<string>();

        void KickBackfill()
        {
            if (_backfill == null && isActiveAndEnabled && Instance == this) _backfill = StartCoroutine(Backfill());
        }

        IEnumerator Backfill()
        {
            yield return null; // 等進房間的其他處理跑完
            string version = Version;
            if (version == null) { _backfill = null; yield break; } // 模型載入失敗
            int done = 0;
            while (true)
            {
                var save = SaveManager.Instance;
                var photos = save != null ? save.data?.photos : null;
                if (photos == null) break;
                var next = photos.Find(p => p != null && !string.IsNullOrEmpty(p.fileName)
                                            && (string.IsNullOrEmpty(p.category) || p.categoryVersion != version)
                                            && !_backfillFailed.Contains(p.fileName));
                if (next == null) break;

                var tex = save.LoadPhoto(next);
                var r = tex != null ? Classify(tex) : default;
                if (tex != null) Destroy(tex);
                if (!r.Ok) { _backfillFailed.Add(next.fileName); yield return null; continue; }

                next.category = r.category;
                next.categoryConfidence = r.confidence;
                next.categoryVersion = version;
                FirebaseManager.Instance?.UpdatePhotoCategory(next);
                done++;
                if (done % 10 == 0) save.Save();
                yield return null; // 一幀一張，不卡畫面
            }
            if (done > 0)
            {
                SaveManager.Instance?.Save();
                Debug.Log($"[PhotoClassifier] 舊照片補辨識完成：{done} 張");
                SaveManager.OnSaveDataChanged?.Invoke(); // 相簿篩選、進化條更新（_backfill 還沒清掉，不會重跑）
            }
            _backfill = null;
        }

        /// <summary>釋放 Sentis worker（Editor 測試用完要呼叫）。</summary>
        public void ReleaseWorker()
        {
            _worker?.Dispose();
            _worker = null;
            _loaded = false;
        }

        bool EnsureLoaded()
        {
            if (_loaded) return _worker != null && _cfg != null;
            _loaded = true;
            if (model == null) model = Resources.Load<ModelAsset>(ResModel);
            if (categories == null) categories = Resources.Load<TextAsset>(ResCategories);
            if (model == null || categories == null)
            {
                Debug.LogWarning("[PhotoClassifier] 找不到模型或類別向量（Assets/Resources/Vision）");
                return false;
            }
            try
            {
                _cfg = JsonUtility.FromJson<FileJson>(categories.text);
                var m = ModelLoader.Load(model);
                var backend = preferGpu && SystemInfo.supportsComputeShaders ? BackendType.GPUCompute : BackendType.CPU;
                _worker = new Worker(m, backend);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[PhotoClassifier] 載入失敗：" + e.Message);
                _worker = null;
                return false;
            }
        }

        /// <summary>辨識一張照片（貼圖要可讀：NativeGallery 讀的、SaveManager.LoadPhoto、相機拍的都可以）。約數十毫秒，會卡一下主執行緒。</summary>
        public PhotoCategoryResult Classify(Texture2D photo)
        {
            var result = new PhotoCategoryResult();
            if (photo == null || !EnsureLoaded()) return result;
            try
            {
                int n = _cfg.inputSize;
                var pixels = Preprocess(photo, n, _cfg.mean, _cfg.std);
                using var input = new Tensor<float>(new TensorShape(1, 3, n, n), pixels);
                _worker.Schedule(input);
                var output = _worker.PeekOutput() as Tensor<float>;
                var emb = output.DownloadToArray(); // 同步讀回（Awaitable 版本要 Unity 2023.2）
                Normalize(emb);

                int k = _cfg.categories.Length;
                var logits = new float[k];
                for (int c = 0; c < k; c++)
                {
                    // 每類取最像的一句
                    var e = _cfg.categories[c].embeddings;
                    int sentences = e.Length / _cfg.dim;
                    float best = float.MinValue;
                    for (int s = 0; s < sentences; s++)
                    {
                        float dot = 0f;
                        int o = s * _cfg.dim;
                        for (int i = 0; i < _cfg.dim; i++) dot += e[o + i] * emb[i];
                        if (dot > best) best = dot;
                    }
                    logits[c] = _cfg.logitScale * best;
                }
                var prob = Softmax(logits);
                int top = 0;
                for (int c = 1; c < k; c++) if (prob[c] > prob[top]) top = c;
                result.category = _cfg.categories[top].name;
                result.confidence = prob[top];
                result.scores = prob;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PhotoClassifier] 辨識失敗：" + e.Message);
            }
            return result;
        }

        /// <summary>短邊縮到 n、置中裁正方形（每格 4×4 平均），轉成 NCHW 並 normalize。直接讀原始像素，不經過 GPU 色彩轉換。</summary>
        static float[] Preprocess(Texture2D tex, int n, float[] mean, float[] std)
        {
            var src = tex.GetPixels32();
            int w = tex.width, h = tex.height;
            float side = Mathf.Min(w, h);
            float x0 = (w - side) / 2f, y0 = (h - side) / 2f;
            float step = side / n;
            const int S = 4;
            var data = new float[3 * n * n];
            int plane = n * n;
            for (int ty = 0; ty < n; ty++)
            {
                for (int tx = 0; tx < n; tx++)
                {
                    float r = 0, g = 0, b = 0;
                    for (int sy = 0; sy < S; sy++)
                    {
                        // 貼圖第 0 列在最下面；模型要由上往下
                        float fy = y0 + side - (ty + (sy + 0.5f) / S) * step;
                        int py = Mathf.Clamp((int)fy, 0, h - 1);
                        for (int sx = 0; sx < S; sx++)
                        {
                            int px = Mathf.Clamp((int)(x0 + (tx + (sx + 0.5f) / S) * step), 0, w - 1);
                            var c = src[py * w + px];
                            r += c.r; g += c.g; b += c.b;
                        }
                    }
                    const float inv = 1f / (255f * S * S);
                    int i = ty * n + tx;
                    data[i] = (r * inv - mean[0]) / std[0];
                    data[plane + i] = (g * inv - mean[1]) / std[1];
                    data[2 * plane + i] = (b * inv - mean[2]) / std[2];
                }
            }
            return data;
        }

        static void Normalize(float[] v)
        {
            double sum = 0;
            foreach (var x in v) sum += x * x;
            float len = (float)Math.Sqrt(sum);
            if (len <= 0f) return;
            for (int i = 0; i < v.Length; i++) v[i] /= len;
        }

        static float[] Softmax(float[] logits)
        {
            float max = float.MinValue;
            foreach (var x in logits) max = Mathf.Max(max, x);
            var p = new float[logits.Length];
            float sum = 0f;
            for (int i = 0; i < p.Length; i++) { p[i] = Mathf.Exp(logits[i] - max); sum += p[i]; }
            for (int i = 0; i < p.Length; i++) p[i] /= sum;
            return p;
        }
    }
}
