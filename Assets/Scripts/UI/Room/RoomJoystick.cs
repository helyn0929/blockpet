using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BlockPet.UI.Room
{
    /// <summary>
    /// 選空間頁的搖桿底座（設計文件 5.8 ⑥）：把手指相對底座中心的位移（設計座標，y 向下）回報給 RoomRingController。
    /// 底座是 112 × 112、左上為原點；手指移出底座也照樣追蹤（拖曳中）。
    /// </summary>
    public class RoomJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public const float Size = 112f;

        /// <summary>按下（位移）。</summary>
        public Action<Vector2> onDown;
        /// <summary>拖曳中（位移）。</summary>
        public Action<Vector2> onMove;
        /// <summary>放開。</summary>
        public Action onUp;

        int _pointer = int.MinValue;

        Vector2 Offset(PointerEventData e)
        {
            var rt = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var local);
            // pivot 在左上：local.x 0～112、local.y 0～−112
            return new Vector2(local.x - Size / 2f, -(local.y + Size / 2f));
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (_pointer != int.MinValue) return;
            _pointer = e.pointerId;
            onDown?.Invoke(Offset(e));
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != _pointer) return;
            onMove?.Invoke(Offset(e));
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != _pointer) return;
            _pointer = int.MinValue;
            onUp?.Invoke();
        }

        void OnDisable()
        {
            if (_pointer == int.MinValue) return;
            _pointer = int.MinValue;
            onUp?.Invoke();
        }
    }
}
