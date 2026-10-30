using System.Collections.Generic;
using UnityEngine;

namespace BlockPet.Weather
{
    /// <summary>
    /// 營地天氣畫面效果（設計文件 5.20 ②，第 1 步：只有畫面，寵物動作之後做）：
    /// - 下雨：斜斜的雨滴、落地小水花、地上水窪，畫面偏藍變暗。
    /// - 雷雨：雨更大，每 8～20 秒隨機閃電（全畫面白光閃一下）。
    /// - 下雪：雪花慢慢飄、左右晃；地面幾分鐘內慢慢變白（只是畫面，不存檔）。
    /// - 多雲：畫面稍暗、雲影慢慢飄過地面。起霧：畫面霧白。
    /// - 晚上：整體調暗偏藍（之後美術給夜晚版底圖再換）。
    /// 地面上的東西（水窪、積雪、雲影）在 Background 圖層，壓在寵物、家具下面；
    /// 落下的雨雪、色調、閃電在 PetVFX 圖層，在寵物上面、UI 下面。
    /// 雨滴雪花用自己管理的 SpriteRenderer（圖是程式產生的，美術出圖後換掉 sprite 即可），不用 ParticleSystem，
    /// 避免手機版材質／shader 被拿掉。天氣切換時 2 秒淡入淡出。執行時自動建立，跟著主攝影機的畫面範圍。
    /// </summary>
    public class WeatherEffects : MonoBehaviour
    {
        const string GroundLayer = "Background";
        const string FxLayer = "PetVFX";
        const int RainMax = 200;
        const int SnowMax = 90;
        const int SplashMax = 24;
        const int PuddleCount = 5;
        const int CloudCount = 3;
        const float FadeSeconds = 2f;
        /// <summary>地面範圍：畫面下方 55%（天空的部分不放水窪、水花、積雪）。</summary>
        const float GroundPortion = 0.55f;
        const float SnowCoverSeconds = 180f;

        static WeatherEffects _instance;

        Camera _cam;
        Transform _root;
        Sprite _dropSprite, _flakeSprite, _dotSprite, _puddleSprite, _cloudSprite, _groundSprite, _pixelSprite;

        // 0～1 的強度，往目標慢慢靠近
        float _rain, _rainTarget, _rainHeavy;
        float _snow, _snowTarget;
        float _cloud, _cloudTarget;
        float _snowCover;

        SpriteRenderer _tint, _night, _flash, _fog, _snowGround;
        Color _tintColor = Clear0;
        float _nightA;
        float _nextLightning = -1f, _flashA;

        class Drop { public SpriteRenderer sr; public Vector2 pos; public float speed, endY, phase, size; }
        readonly List<Drop> _drops = new List<Drop>();
        readonly List<Drop> _flakes = new List<Drop>();
        readonly List<(SpriteRenderer sr, float t)> _splashes = new List<(SpriteRenderer, float)>();
        int _nextSplash;
        readonly List<SpriteRenderer> _puddles = new List<SpriteRenderer>();
        readonly List<(SpriteRenderer sr, float speed)> _clouds = new List<(SpriteRenderer, float)>();

        static readonly Color Clear0 = new Color(0f, 0f, 0f, 0f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (_instance != null) return;
            var go = new GameObject("WeatherEffects (auto)");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<WeatherEffects>();
        }

        void Awake()
        {
            _root = transform;
            MakeSprites();
            Build();
            WeatherService.OnChanged += ApplyTargets;
            ApplyTargets();
            // 一開場不要淡入，直接是目前的天氣
            _rain = _rainTarget; _snow = _snowTarget; _cloud = _cloudTarget;
        }

        void OnDestroy()
        {
            WeatherService.OnChanged -= ApplyTargets;
            if (_instance == this) _instance = null;
        }

        void ApplyTargets()
        {
            var k = WeatherService.Current;
            _rainTarget = k == WeatherKind.Rain || k == WeatherKind.Thunder ? 1f : 0f;
            _rainHeavy = k == WeatherKind.Thunder ? 1f : 0f;
            _snowTarget = k == WeatherKind.Snow ? 1f : 0f;
            _cloudTarget = k == WeatherKind.Cloudy ? 1f : (k == WeatherKind.Rain || k == WeatherKind.Thunder ? 0.6f : 0f);
            _nextLightning = k == WeatherKind.Thunder ? Time.time + Random.Range(3f, 8f) : -1f;
        }

        // ─── 每幀 ──────────────────────────────────────────────────────

        void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null || !_cam.orthographic) { SetVisible(false); return; }
            SetVisible(true);

            float dt = Time.deltaTime;
            float h = _cam.orthographicSize * 2f;
            float w = h * _cam.aspect;
            Vector2 c = _cam.transform.position;
            var view = new Rect(c.x - w / 2f, c.y - h / 2f, w, h);
            float groundTop = view.yMin + view.height * GroundPortion;

            _rain = Mathf.MoveTowards(_rain, _rainTarget, dt / FadeSeconds);
            _snow = Mathf.MoveTowards(_snow, _snowTarget, dt / FadeSeconds);
            _cloud = Mathf.MoveTowards(_cloud, _cloudTarget, dt / FadeSeconds);
            // 積雪：下雪時幾分鐘慢慢變白，停了就慢慢化掉
            _snowCover = Mathf.MoveTowards(_snowCover, _snowTarget, dt / (_snowTarget > 0f ? SnowCoverSeconds : 30f));

            FitFullScreen(_tint, view);
            FitFullScreen(_night, view);
            FitFullScreen(_flash, view);
            FitFullScreen(_fog, view);

            UpdateTint(dt);
            UpdateRain(dt, view, groundTop);
            UpdateSnow(dt, view);
            UpdateSplashes(dt);
            UpdateGround(view, groundTop);
            UpdateClouds(dt, view, groundTop);
            UpdateLightning(dt);
        }

        void UpdateTint(float dt)
        {
            var k = WeatherService.Current;
            Color target = k switch
            {
                WeatherKind.Cloudy => new Color(0.25f, 0.27f, 0.30f, 0.16f),
                WeatherKind.Rain => new Color(0.14f, 0.19f, 0.32f, 0.26f),
                WeatherKind.Thunder => new Color(0.10f, 0.12f, 0.22f, 0.36f),
                WeatherKind.Snow => new Color(0.62f, 0.72f, 0.88f, 0.14f),
                _ => new Color(0.14f, 0.19f, 0.32f, 0f),
            };
            _tintColor = Color.Lerp(_tintColor, target, 1f - Mathf.Exp(-dt * 2f)); // 約 2 秒換過去
            _tint.color = _tintColor;

            _nightA = Mathf.MoveTowards(_nightA, WeatherService.IsNight ? 0.42f : 0f, dt / FadeSeconds * 0.42f);
            _night.color = new Color(0.04f, 0.07f, 0.24f, _nightA);

            float fogTarget = k == WeatherKind.Fog ? 0.42f : 0f;
            var f = _fog.color;
            f.a = Mathf.MoveTowards(f.a, fogTarget, dt / FadeSeconds * 0.42f);
            _fog.color = f;
        }

        void UpdateRain(float dt, Rect view, float groundTop)
        {
            int want = Mathf.RoundToInt(RainMax * _rain * Mathf.Lerp(0.65f, 1f, _rainHeavy));
            float speed = Mathf.Lerp(12f, 16f, _rainHeavy);
            const float slant = -0.12f; // 往左下斜
            for (int i = 0; i < _drops.Count; i++)
            {
                var d = _drops[i];
                bool on = i < want;
                if (d.sr.enabled != on)
                {
                    d.sr.enabled = on;
                    if (on) RespawnDrop(d, view, groundTop, true);
                }
                if (!on) continue;
                d.pos += new Vector2(slant, -1f) * (d.speed * speed * dt);
                if (d.pos.y <= d.endY)
                {
                    if (d.endY < groundTop) Splash(new Vector2(d.pos.x, d.endY));
                    RespawnDrop(d, view, groundTop, false);
                }
                d.sr.transform.position = new Vector3(d.pos.x, d.pos.y, 0f);
                d.sr.color = new Color(0.86f, 0.92f, 1f, 0.5f * Mathf.Clamp01(_rain * 1.5f));
            }
        }

        void RespawnDrop(Drop d, Rect view, float groundTop, bool anywhere)
        {
            float pad = view.width * 0.2f;
            d.pos = new Vector2(Random.Range(view.xMin - pad * 0.2f, view.xMax + pad), anywhere ? Random.Range(view.yMin, view.yMax) : view.yMax + Random.Range(0.2f, 2f));
            // 落點：大部分落在地面範圍（有遠近感），少部分直接掉出畫面
            d.endY = Random.value < 0.85f ? Random.Range(view.yMin, groundTop) : view.yMin - 0.5f;
            d.speed = Random.Range(0.85f, 1.15f);
            float len = Random.Range(0.32f, 0.5f);
            d.sr.transform.localScale = new Vector3(1f, len / 0.5f, 1f);
        }

        void Splash(Vector2 at)
        {
            if (_splashes.Count == 0) return;
            var s = _splashes[_nextSplash];
            _splashes[_nextSplash] = (s.sr, 0f);
            _nextSplash = (_nextSplash + 1) % _splashes.Count;
            s.sr.transform.position = new Vector3(at.x, at.y, 0f);
            s.sr.enabled = true;
        }

        void UpdateSplashes(float dt)
        {
            for (int i = 0; i < _splashes.Count; i++)
            {
                var (sr, t) = _splashes[i];
                if (!sr.enabled) continue;
                t += dt / 0.3f;
                _splashes[i] = (sr, t);
                if (t >= 1f) { sr.enabled = false; continue; }
                sr.transform.localScale = new Vector3(Mathf.Lerp(0.3f, 1f, t), Mathf.Lerp(0.3f, 0.6f, t), 1f);
                sr.color = new Color(0.9f, 0.95f, 1f, 0.6f * (1f - t));
            }
        }

        void UpdateSnow(float dt, Rect view)
        {
            int want = Mathf.RoundToInt(SnowMax * _snow);
            for (int i = 0; i < _flakes.Count; i++)
            {
                var f = _flakes[i];
                bool on = i < want;
                if (f.sr.enabled != on)
                {
                    f.sr.enabled = on;
                    if (on) RespawnFlake(f, view, true);
                }
                if (!on) continue;
                f.phase += dt;
                f.pos.y -= f.speed * dt;
                float sway = Mathf.Sin(f.phase * 1.3f + f.size * 20f) * 0.25f;
                if (f.pos.y < view.yMin - 0.3f) RespawnFlake(f, view, false);
                f.sr.transform.position = new Vector3(f.pos.x + sway, f.pos.y, 0f);
                f.sr.color = new Color(1f, 1f, 1f, 0.9f * Mathf.Clamp01(_snow * 1.5f));
            }
        }

        void RespawnFlake(Drop f, Rect view, bool anywhere)
        {
            f.pos = new Vector2(Random.Range(view.xMin - 0.3f, view.xMax + 0.3f), anywhere ? Random.Range(view.yMin, view.yMax) : view.yMax + Random.Range(0.1f, 1f));
            f.speed = Random.Range(0.6f, 1.3f);
            f.size = Random.Range(0.05f, 0.12f);
            f.phase = Random.Range(0f, 6f);
            f.sr.transform.localScale = Vector3.one * (f.size / 0.1f);
        }

        void UpdateGround(Rect view, float groundTop)
        {
            // 水窪：下雨時慢慢出現（跟著雨的強度）
            for (int i = 0; i < _puddles.Count; i++)
            {
                var p = _puddles[i];
                // 依畫面比例固定位置（在地面範圍裡）
                float u = (i * 0.37f + 0.13f) % 1f;
                float v = (i * 0.61f + 0.21f) % 1f;
                p.transform.position = new Vector3(view.xMin + view.width * (0.12f + 0.76f * u), view.yMin + (groundTop - view.yMin) * (0.1f + 0.7f * v), 0f);
                p.color = new Color(0.55f, 0.65f, 0.8f, 0.32f * _rain);
                p.enabled = _rain > 0.01f;
            }

            // 積雪：蓋住地面範圍，上緣淡出
            _snowGround.enabled = _snowCover > 0.01f;
            _snowGround.transform.position = new Vector3(view.center.x, view.yMin, 0f);
            _snowGround.transform.localScale = new Vector3(view.width, groundTop - view.yMin + 0.6f, 1f);
            _snowGround.color = new Color(0.97f, 0.98f, 1f, 0.55f * _snowCover);
        }

        void UpdateClouds(float dt, Rect view, float groundTop)
        {
            for (int i = 0; i < _clouds.Count; i++)
            {
                var (sr, speed) = _clouds[i];
                sr.enabled = _cloud > 0.01f;
                if (!sr.enabled) continue;
                var p = sr.transform.position;
                p.x += speed * dt;
                float span = view.width + 6f;
                if (p.x > view.xMax + 3f) p.x -= span;
                if (p.x < view.xMin - 3f || Mathf.Abs(p.y) > 1000f)
                {
                    p.x = view.xMin - 3f + span * (i / (float)Mathf.Max(1, _clouds.Count));
                }
                p.y = view.yMin + (groundTop - view.yMin) * (0.2f + 0.3f * i);
                sr.transform.position = p;
                sr.color = new Color(0.1f, 0.12f, 0.15f, 0.16f * _cloud);
            }
        }

        void UpdateLightning(float dt)
        {
            if (_nextLightning > 0f && Time.time >= _nextLightning)
            {
                _flashA = 0.75f;
                _nextLightning = Time.time + Random.Range(8f, 20f);
            }
            _flashA = Mathf.MoveTowards(_flashA, 0f, dt / 0.35f);
            _flash.color = new Color(1f, 1f, 0.97f, _flashA);
            _flash.enabled = _flashA > 0f;
        }

        bool _visible = true;

        /// <summary>沒有營地攝影機（例如還在登入畫面）時整組藏起來。</summary>
        void SetVisible(bool on)
        {
            if (_visible == on) return;
            _visible = on;
            foreach (Transform t in _root) t.gameObject.SetActive(on);
        }

        static void FitFullScreen(SpriteRenderer sr, Rect view)
        {
            sr.transform.position = new Vector3(view.center.x, view.center.y, 0f);
            sr.transform.localScale = new Vector3(view.width + 0.2f, view.height + 0.2f, 1f);
        }

        // ─── 建立 ──────────────────────────────────────────────────────

        void Build()
        {
            _night = Renderer("Night", _pixelSprite, FxLayer, 1);
            _tint = Renderer("Tint", _pixelSprite, FxLayer, 2);
            _fog = Renderer("Fog", _pixelSprite, FxLayer, 3);
            _fog.color = new Color(0.93f, 0.95f, 0.97f, 0f);
            _flash = Renderer("Lightning", _pixelSprite, FxLayer, 30);
            _flash.enabled = false;

            for (int i = 0; i < RainMax; i++)
            {
                var sr = Renderer("Rain", _dropSprite, FxLayer, 10);
                sr.transform.rotation = Quaternion.Euler(0f, 0f, -7f); // 跟雨的方向一致
                sr.enabled = false;
                _drops.Add(new Drop { sr = sr });
            }
            for (int i = 0; i < SnowMax; i++)
            {
                var sr = Renderer("Snow", _flakeSprite, FxLayer, 11);
                sr.enabled = false;
                _flakes.Add(new Drop { sr = sr });
            }
            for (int i = 0; i < SplashMax; i++)
            {
                var sr = Renderer("Splash", _dotSprite, GroundLayer, 12);
                sr.enabled = false;
                _splashes.Add((sr, 1f));
            }
            for (int i = 0; i < PuddleCount; i++)
            {
                var sr = Renderer("Puddle", _puddleSprite, GroundLayer, 5);
                sr.transform.localScale = new Vector3(Random.Range(0.9f, 1.4f), Random.Range(0.8f, 1.1f), 1f);
                sr.enabled = false;
                _puddles.Add(sr);
            }
            _snowGround = Renderer("SnowGround", _groundSprite, GroundLayer, 10);
            _snowGround.enabled = false;
            for (int i = 0; i < CloudCount; i++)
            {
                var sr = Renderer("CloudShadow", _cloudSprite, GroundLayer, 8);
                sr.transform.localScale = new Vector3(Random.Range(2.5f, 3.5f), Random.Range(1f, 1.4f), 1f);
                sr.transform.position = new Vector3(-9999f, 0f, 0f);
                sr.enabled = false;
                _clouds.Add((sr, Random.Range(0.15f, 0.3f)));
            }
        }

        SpriteRenderer Renderer(string name, Sprite sprite, string layer, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerName = layer;
            sr.sortingOrder = order;
            return sr;
        }

        // ─── 程式產生的圖（美術出圖後換掉）─────────────────────────────

        void MakeSprites()
        {
            // 世界大小（單位）＝貼圖像素 ÷ ppu
            _pixelSprite = Make(4, 4, 4f, (x, y) => 1f);                                                   // 1 × 1 白方塊（色調、閃電、霧）
            _dropSprite = Make(4, 50, 100f, (x, y) => (1f - Mathf.Abs(x - 1.5f) / 2f) * Mathf.Clamp01(y / 50f * 1.4f)); // 0.04 × 0.5 雨滴
            _flakeSprite = Make(10, 10, 100f, (x, y) => Soft(Dist(x, y, 4.5f, 4.5f) / 5f));                 // 0.1 雪花
            _dotSprite = Make(24, 10, 100f, (x, y) => Ring(Dist(x, y * 2.4f, 11.5f, 11.5f) / 12f));          // 0.24 × 0.1 水花圈
            _puddleSprite = Make(64, 24, 50f, (x, y) => Soft(Dist(x, y * 2.67f, 31.5f, 31.5f) / 32f, 0.6f)); // 1.28 × 0.48 水窪
            _cloudSprite = Make(64, 32, 64f, (x, y) => Soft(Dist(x, y * 2f, 31.5f, 31.5f) / 32f, 0.2f));    // 1 × 0.5 雲影
            // 積雪：1 × 1，pivot 在底邊；底部不透明、上緣 30% 淡出
            _groundSprite = Make(64, 64, 64f, (x, y) => Mathf.Clamp01((1f - y / 64f) / 0.3f), new Vector2(0.5f, 0f));
        }

        static float Dist(float x, float y, float cx, float cy) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
        static float Soft(float r, float edge = 0.4f) => Mathf.Clamp01((1f - r) / edge);
        static float Ring(float r) => Mathf.Clamp01(1f - Mathf.Abs(r - 0.75f) / 0.25f);

        /// <summary>程式產生白色 sprite（顏色由 SpriteRenderer.color 決定）。</summary>
        static Sprite Make(int w, int h, float ppu, System.Func<int, int, float> alpha, Vector2? pivot = null)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(x, y)) * 255f));
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var s = Sprite.Create(tex, new Rect(0, 0, w, h), pivot ?? new Vector2(0.5f, 0.5f), ppu);
            s.hideFlags = HideFlags.DontSave;
            return s;
        }
    }
}
