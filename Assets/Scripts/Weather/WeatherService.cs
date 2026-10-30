using System;
using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace BlockPet.Weather
{
    /// <summary>天氣種類（設計文件 5.20 ②）。</summary>
    public enum WeatherKind { Clear, Cloudy, Rain, Thunder, Snow, Fog }

    /// <summary>
    /// 營地天氣（設計文件 5.20）：每個人看到自己所在地的真實天氣，只改外觀和寵物動作，不影響任何數值、不寫入遊戲資料。
    /// - 第一次登入後向手機要「大概位置」權限（iOS 預設只給大概位置；Android 只要 COARSE）。
    /// - 位置只在手機上用來查天氣：四捨五入到小數 1 位（約 10 公里，城市等級）才送去查，不上傳 Firebase、不存檔。
    /// - 天氣來源 Open-Meteo（免費、不需要金鑰；注意：免費版限非商業使用，上架前要改付費方案或換來源，只要改 Fetch 這段）。
    /// - 約每 30 分鐘更新一次；回到前景時超過 30 分鐘也會重查。
    /// - 拿不到位置、沒給權限、查詢失敗 → 一直是晴天。
    /// - 白天／晚上跟著手機時間（IsNight）。
    /// 執行時自動建立，不用在場景裡放。畫面效果（雨、雪、夜晚）之後由營地訂閱 OnChanged 來做。
    /// </summary>
    public class WeatherService : MonoBehaviour
    {
        const float RefreshSeconds = 30f * 60f;
        const float LocationTimeout = 20f;
        const string Api = "https://api.open-meteo.com/v1/forecast";

        public static WeatherService Instance { get; private set; }

        /// <summary>目前天氣（有 Debug 覆寫時是覆寫的值）。</summary>
        public static WeatherKind Current => Override ?? (Instance != null ? Instance._kind : WeatherKind.Clear);

        /// <summary>晚上：手機時間 18:00～06:00（有 Debug 覆寫時是覆寫的值）。</summary>
        public static bool IsNight => NightOverride ?? (DateTime.Now.Hour >= 18 || DateTime.Now.Hour < 6);

        /// <summary>天氣或白天／晚上變了（主執行緒）。</summary>
        public static event Action OnChanged;

        /// <summary>Debug 用：強制指定天氣（null＝用真的）。</summary>
        public static WeatherKind? Override { get => _override; set { _override = value; OnChanged?.Invoke(); } }
        static WeatherKind? _override;
        public static bool? NightOverride { get => _nightOverride; set { _nightOverride = value; OnChanged?.Invoke(); } }
        static bool? _nightOverride;

        /// <summary>最近一次查天氣的狀態（給 Debug 看）。</summary>
        public static string Status => Instance != null ? Instance._status : "未啟動";

        WeatherKind _kind = WeatherKind.Clear;
        bool _night;
        float _lastFetch = -RefreshSeconds;
        bool _hasCoords;
        float _lat, _lon;          // 已經四捨五入到城市等級
        bool _locating, _fetching, _started;
        string _status = "等待登入";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("WeatherService (auto)");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<WeatherService>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _night = IsNight;
            FirebaseManager.OnLoginSuccess += OnLogin;
        }

        void OnDestroy()
        {
            FirebaseManager.OnLoginSuccess -= OnLogin;
            if (Instance == this) Instance = null;
        }

        // 登入後才要權限，不要一打開 App 就跳
        void OnLogin(bool ok)
        {
            if (!ok || _started) return;
            _started = true;
            StartCoroutine(Locate());
        }

        void Update()
        {
            // 白天／晚上換了也通知
            bool night = IsNight;
            if (night != _night) { _night = night; OnChanged?.Invoke(); }

            if (_hasCoords && !_fetching && Time.realtimeSinceStartup - _lastFetch >= RefreshSeconds)
                StartCoroutine(Fetch());
        }

        void OnApplicationPause(bool paused)
        {
            // 回到前景：Update 會依時間判斷要不要重查（realtimeSinceStartup 在背景也會走）
            if (!paused && _started && !_hasCoords && !_locating) StartCoroutine(Locate());
        }

        // ─── 位置 ──────────────────────────────────────────────────────

        IEnumerator Locate()
        {
            _locating = true;
            try
            {
#if UNITY_EDITOR
                _status = "Editor 沒有定位，維持晴天（可用 BlockPet/Debug/天氣 測試）";
                yield break;
#else
#if UNITY_ANDROID
                if (!Permission.HasUserAuthorizedPermission(Permission.CoarseLocation))
                {
                    Permission.RequestUserPermission(Permission.CoarseLocation);
                    float wait = 0f;
                    // 等使用者回應權限視窗（App 會暫停，回來再檢查）
                    while (!Permission.HasUserAuthorizedPermission(Permission.CoarseLocation) && wait < 30f)
                    {
                        wait += Time.unscaledDeltaTime;
                        yield return null;
                    }
                }
#endif
                if (!Input.location.isEnabledByUser)
                {
                    _status = "沒有開定位，維持晴天";
                    SetKind(WeatherKind.Clear);
                    yield break;
                }

                // 大概位置就夠了（3 公里精度；iOS 第一次會跳出權限視窗）
                Input.location.Start(3000f, 3000f);
                float t = 0f;
                while (Input.location.status == LocationServiceStatus.Initializing && t < LocationTimeout)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (Input.location.status != LocationServiceStatus.Running)
                {
                    _status = "拿不到位置（" + Input.location.status + "），維持晴天";
                    Input.location.Stop();
                    SetKind(WeatherKind.Clear);
                    yield break;
                }

                var d = Input.location.lastData;
                Input.location.Stop(); // 只要一次，省電
                _lat = Mathf.Round(d.latitude * 10f) / 10f;
                _lon = Mathf.Round(d.longitude * 10f) / 10f;
                _hasCoords = true;
                _lastFetch = -RefreshSeconds; // 馬上查
                _status = "已取得大概位置";
#endif
            }
            finally { _locating = false; }
        }

        // ─── 查天氣（Open-Meteo）─────────────────────────────────────

        IEnumerator Fetch()
        {
            _fetching = true;
            _lastFetch = Time.realtimeSinceStartup;
            string url = string.Format(CultureInfo.InvariantCulture,
                "{0}?latitude={1:0.0}&longitude={2:0.0}&current=weather_code&timezone=auto", Api, _lat, _lon);
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();
                _fetching = false;
                if (req.result != UnityWebRequest.Result.Success)
                {
                    // 查不到就維持上一次的天氣（第一次就是晴天），30 分鐘後再試
                    _status = "查天氣失敗：" + req.error;
                    yield break;
                }
                int code = ParseWeatherCode(req.downloadHandler.text);
                if (code < 0) { _status = "天氣資料看不懂"; yield break; }
                SetKind(FromWmo(code));
                _status = $"天氣碼 {code} → {Current}（{DateTime.Now:HH:mm} 更新）";
            }
        }

        void SetKind(WeatherKind kind)
        {
            if (kind == _kind) return;
            _kind = kind;
            if (Override == null) OnChanged?.Invoke();
        }

        /// <summary>從 {"current":{...,"weather_code":61}} 取出天氣碼；找不到回傳 -1。</summary>
        static int ParseWeatherCode(string json)
        {
            if (string.IsNullOrEmpty(json)) return -1;
            int cur = json.IndexOf("\"current\"", StringComparison.Ordinal);
            if (cur < 0) return -1;
            int k = json.IndexOf("\"weather_code\"", cur, StringComparison.Ordinal);
            if (k < 0) return -1;
            int colon = json.IndexOf(':', k);
            if (colon < 0) return -1;
            int i = colon + 1, start;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            start = i;
            while (i < json.Length && char.IsDigit(json[i])) i++;
            return int.TryParse(json.Substring(start, i - start), out int code) ? code : -1;
        }

        /// <summary>WMO 天氣碼（Open-Meteo 用的）→ 營地天氣。</summary>
        public static WeatherKind FromWmo(int code)
        {
            if (code <= 1) return WeatherKind.Clear;                       // 晴、大致晴
            if (code <= 3) return WeatherKind.Cloudy;                      // 局部多雲、陰
            if (code == 45 || code == 48) return WeatherKind.Fog;           // 霧
            if (code >= 95) return WeatherKind.Thunder;                     // 雷雨（95、96、99）
            if ((code >= 71 && code <= 77) || code == 85 || code == 86) return WeatherKind.Snow;
            if ((code >= 51 && code <= 67) || (code >= 80 && code <= 82)) return WeatherKind.Rain; // 毛毛雨、雨、凍雨、陣雨
            return WeatherKind.Clear;
        }

#if UNITY_EDITOR
        /// <summary>Editor 測試：用指定的座標查一次（不經過定位）。</summary>
        public void DebugFetchAt(float lat, float lon)
        {
            _lat = Mathf.Round(lat * 10f) / 10f;
            _lon = Mathf.Round(lon * 10f) / 10f;
            _hasCoords = true;
            if (!_fetching) StartCoroutine(Fetch());
        }
#endif
    }
}
