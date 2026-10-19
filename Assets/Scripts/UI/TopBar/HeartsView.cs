using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.TopBar
{
    /// <summary>
    /// 愛心列：0～5 顆、以半顆為單位（滿／半／空三張圖）。
    /// 愛心 ≤ lowHeartsThreshold 時整列 1→1.18→1 跳動（設計稿 c5Pulse）；死亡時全部顯示空心。
    /// </summary>
    public class HeartsView : MonoBehaviour
    {
        [SerializeField] TopBarConfig config;
        [SerializeField] Image[] hearts;

        float _value;
        bool _dead;
        float _pulseTime;

        public float Value => _value;

        /// <summary>設定愛心數（會四捨五入到 0.5）。</summary>
        public void SetHearts(float value)
        {
            _value = Mathf.Clamp(Mathf.Round(value * 2f) / 2f, 0f, config != null ? config.maxHearts : 5);
            Refresh();
        }

        public void SetDead(bool dead)
        {
            _dead = dead;
            Refresh();
        }

        void Refresh()
        {
            if (config == null || hearts == null) return;
            for (int i = 0; i < hearts.Length; i++)
            {
                if (hearts[i] == null) continue;
                float fill = _dead ? 0f : Mathf.Clamp01(_value - i);
                hearts[i].sprite = fill >= 1f ? config.heartFull : (fill >= 0.5f ? config.heartHalf : config.heartEmpty);
            }
        }

        bool IsLow => !_dead && config != null && _value <= config.lowHeartsThreshold;

        void Update()
        {
            if (config == null) return;
            if (!IsLow)
            {
                if (transform.localScale != Vector3.one) transform.localScale = Vector3.one;
                _pulseTime = 0f;
                return;
            }
            // 0%,100% → 1；50% → 1.18，ease-in-out
            _pulseTime += Time.unscaledDeltaTime;
            float t = (_pulseTime % config.lowHeartsPulsePeriod) / config.lowHeartsPulsePeriod;
            float k = 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI * 2f);
            transform.localScale = Vector3.one * Mathf.Lerp(1f, config.lowHeartsPulseScale, k);
        }
    }
}
