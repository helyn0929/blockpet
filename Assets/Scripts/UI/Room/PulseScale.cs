using UnityEngine;

namespace BlockPet.UI.Room
{
    /// <summary>1 → scale → 1 的呼吸縮放（設計稿 ePulse：1.8s、1.1 倍）。</summary>
    public class PulseScale : MonoBehaviour
    {
        public float period = 1.8f;
        public float scale = 1.1f;

        void OnDisable() => transform.localScale = Vector3.one;

        void Update()
        {
            float t = Time.unscaledTime % period / period;
            float k = 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI * 2f);
            transform.localScale = Vector3.one * Mathf.Lerp(1f, scale, k);
        }
    }
}
