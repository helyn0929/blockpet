using UnityEngine;

namespace BlockPet.Feed
{
    /// <summary>
    /// 寵物開心跳一下（命中今天想吃）。寵物的 Animator 目前沒有跳的動畫，用程式往上彈。
    /// 寵物本身會走路（AIController 每幀改 position），所以每幀先拿掉上一幀加的高度、AIController 移動完再加回去，
    /// 不會讓寵物越跳越偏。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class PetHop : MonoBehaviour
    {
        const float Height = 0.45f;   // 世界單位
        const float Duration = 0.36f; // 一下的時間

        float _applied;
        float _time = -1f;
        int _hops;

        public static void Play(GameObject pet, int hops = 2)
        {
            if (pet == null) return;
            var hop = pet.GetComponent<PetHop>();
            if (hop == null) hop = pet.AddComponent<PetHop>();
            hop._hops = hops;
            hop._time = 0f;
        }

        void Update()
        {
            if (_applied == 0f) return;
            transform.position -= new Vector3(0f, _applied, 0f);
            _applied = 0f;
        }

        void LateUpdate()
        {
            if (_time < 0f) return;
            _time += Time.deltaTime;
            float total = Duration * _hops;
            if (_time >= total) { _time = -1f; return; }
            float u = (_time % Duration) / Duration;
            _applied = Height * 4f * u * (1f - u) * (1f - 0.35f * Mathf.Floor(_time / Duration)); // 第二下矮一點
            transform.position += new Vector3(0f, _applied, 0f);
        }

        void OnDisable()
        {
            if (_applied != 0f) transform.position -= new Vector3(0f, _applied, 0f);
            _applied = 0f;
            _time = -1f;
        }
    }
}
