using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BlockPet.Hand
{
    /// <summary>
    /// 營地最新照片卡的拖曳：掛在卡片根物件上，回報手指從按下點開始的位移（設計座標 px，x 向右、y 向下）。
    /// 放開時另外回報速度（設計 px／秒），用來判斷「往左甩」。
    /// 拖曳開始後卡片上的按鈕不會再觸發點擊（Unity 拖曳規則），所以點一下仍然是打開分割畫面。
    /// </summary>
    public class LatestCardDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        /// <summary>參考座標系（設計根），用來把螢幕位移換成設計 px。</summary>
        public RectTransform space;
        public Func<bool> canDrag;
        public Action onBegin;
        public Action<Vector2> onDrag;
        /// <summary>放開（位移、速度）。</summary>
        public Action<Vector2, Vector2> onEnd;

        bool _active;
        Vector2 _start;
        Vector2 _last;
        float _lastTime;
        Vector2 _velocity;

        void Track(Vector2 d)
        {
            float now = Time.unscaledTime;
            float dt = now - _lastTime;
            if (dt > 0.0001f)
            {
                var v = (d - _last) / dt;
                _velocity = Vector2.Lerp(_velocity, v, 0.6f); // 平滑一下，最後幾格的方向最重要
            }
            _last = d;
            _lastTime = now;
        }

        Vector2 Delta(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(space, e.position, e.pressEventCamera, out var p);
            var d = p - _start;
            return new Vector2(d.x, -d.y);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            _active = canDrag == null || canDrag();
            if (!_active) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(space, e.pressPosition, e.pressEventCamera, out _start);
            onBegin?.Invoke();
            _last = Delta(e);
            _lastTime = Time.unscaledTime;
            _velocity = Vector2.zero;
            onDrag?.Invoke(_last);
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_active) return;
            var d = Delta(e);
            Track(d);
            onDrag?.Invoke(d);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!_active) return;
            _active = false;
            var d = Delta(e);
            // 停住一陣子才放開：不算甩
            if (Time.unscaledTime - _lastTime > 0.12f) _velocity = Vector2.zero;
            onEnd?.Invoke(d, _velocity);
        }
    }
}
