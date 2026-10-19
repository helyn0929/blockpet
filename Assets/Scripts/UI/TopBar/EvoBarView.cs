using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.TopBar
{
    /// <summary>
    /// 進化條：條總長代表 100 能量，每段寬 = 該類能量 %（固定順序、段與段間隔 1），
    /// 寬度變化用 0.6 秒 CSS ease 動畫。旁邊文字「進化 N%」。
    /// 補充：滿 100 → 金框＋「可進化！」，點條觸發 <see cref="OnEvolveRequested"/>；死亡時色段變灰。
    /// </summary>
    public class EvoBarView : MonoBehaviour
    {
        [SerializeField] TopBarConfig config;
        [SerializeField] RectTransform fill;
        [SerializeField] Image[] segments;
        [SerializeField] GameObject readyGlow;
        [SerializeField] TMP_Text evoText;

        /// <summary>滿條時點進化條。</summary>
        public event Action OnEvolveRequested;

        float[] _from = new float[0];
        float[] _to = new float[0];
        float[] _now = new float[0];
        float _tweenTime = -1f;
        float _lastWidth = -1f;
        bool _dead;

        public float TotalEnergy { get; private set; }
        public bool IsReady => !_dead && TotalEnergy >= 100f;

        /// <summary>依 config.segments 的順序傳入各類能量（0～100）。</summary>
        public void SetEnergies(float[] energies, bool animate = true)
        {
            int n = segments != null ? segments.Length : 0;
            EnsureArrays(n);
            float total = 0f;
            for (int i = 0; i < n; i++)
            {
                float v = energies != null && i < energies.Length ? Mathf.Max(0f, energies[i]) : 0f;
                _from[i] = _now[i];
                _to[i] = v;
                total += v;
            }
            TotalEnergy = Mathf.Min(100f, total);
            _tweenTime = animate && config != null && config.evoTransitionSec > 0f ? 0f : -1f;
            if (_tweenTime < 0f) Array.Copy(_to, _now, n);

            RefreshColors();
            RefreshText();
            ApplyWidths();
        }

        public void SetDead(bool dead)
        {
            _dead = dead;
            RefreshColors();
            RefreshText();
        }

        /// <summary>給 EvoBar 上的 Button 用：滿條時才觸發。</summary>
        public void Tap()
        {
            if (IsReady) OnEvolveRequested?.Invoke();
        }

        void EnsureArrays(int n)
        {
            if (_now.Length == n) return;
            _from = new float[n];
            _to = new float[n];
            _now = new float[n];
        }

        void RefreshColors()
        {
            if (config == null || segments == null) return;
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i] == null) continue;
                string cat = i < config.segments.Count ? config.segments[i].category : "";
                segments[i].color = _dead ? config.deadSegmentColor : config.SegmentColor(cat);
            }
            if (readyGlow != null) readyGlow.SetActive(IsReady);
        }

        void RefreshText()
        {
            if (evoText == null || config == null) return;
            if (IsReady)
            {
                evoText.text = config.readyText;
                evoText.color = config.readyTextColor;
            }
            else
            {
                evoText.text = config.evoTextFormat.Replace("{energy}", Mathf.RoundToInt(TotalEnergy).ToString());
                evoText.color = config.textColor;
            }
        }

        void Update()
        {
            float w = fill != null ? fill.rect.width : 0f;
            bool widthChanged = !Mathf.Approximately(w, _lastWidth);
            if (_tweenTime >= 0f)
            {
                _tweenTime += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(_tweenTime / config.evoTransitionSec);
                float e = CssEase(t);
                for (int i = 0; i < _now.Length; i++) _now[i] = Mathf.Lerp(_from[i], _to[i], e);
                if (t >= 1f) _tweenTime = -1f;
                ApplyWidths();
            }
            else if (widthChanged)
            {
                ApplyWidths();
            }
        }

        void ApplyWidths()
        {
            if (fill == null || segments == null || config == null) return;
            float w = fill.rect.width;
            _lastWidth = w;
            float x = 0f;
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i] == null) continue;
                float segW = (i < _now.Length ? _now[i] : 0f) / 100f * w;
                var rt = segments[i].rectTransform;
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(x, 0f);
                rt.sizeDelta = new Vector2(segW, 0f);
                // CSS flex gap：每段之間都留 1（包含寬度 0 的段）
                x += segW + config.evoSegmentGap;
            }
        }

        /// <summary>CSS transition-timing-function: ease = cubic-bezier(0.25, 0.1, 0.25, 1)。</summary>
        static float CssEase(float x)
        {
            const float x1 = 0.25f, y1 = 0.1f, x2 = 0.25f, y2 = 1f;
            float t = x;
            for (int i = 0; i < 8; i++)
            {
                float bx = Bezier(t, x1, x2) - x;
                float d = BezierDerivative(t, x1, x2);
                if (Mathf.Abs(bx) < 1e-5f || Mathf.Abs(d) < 1e-6f) break;
                t = Mathf.Clamp01(t - bx / d);
            }
            return Bezier(t, y1, y2);
        }

        static float Bezier(float t, float p1, float p2)
        {
            float u = 1f - t;
            return 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t;
        }

        static float BezierDerivative(float t, float p1, float p2)
        {
            float u = 1f - t;
            return 3f * u * u * p1 + 6f * u * t * (p2 - p1) + 3f * t * t * (1f - p2);
        }
    }
}
