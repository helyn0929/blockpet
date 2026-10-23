using UnityEngine;

namespace BlockPet.UI.Chat
{
    /// <summary>讓另一個設計根跟著同樣的縮放與大小（聊天室的選單層與主畫面分開，但要一樣大）。</summary>
    public class ScaleWith : MonoBehaviour
    {
        public RectTransform source;

        void LateUpdate()
        {
            if (source == null) return;
            var rt = (RectTransform)transform;
            rt.localScale = source.localScale;
            rt.sizeDelta = source.sizeDelta;
        }
    }
}
