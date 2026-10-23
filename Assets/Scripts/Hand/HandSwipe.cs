using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BlockPet.Hand
{
    /// <summary>
    /// 手牌左右滑：掛在手牌的父物件上，卡片與滑動區（子物件）上的拖曳都會傳到這裡。
    /// 回報水平位移（設計座標 px，向右為正）；直向為主的拖曳不處理。拖曳開始後按鈕不會再觸發點擊。
    /// </summary>
    public class HandSwipe : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        /// <summary>參考座標系（設計根），用來把螢幕位移換成設計 px。</summary>
        public RectTransform space;
        public Func<bool> canSwipe;
        public Action<float> onDrag;
        public Action<float> onEnd;

        bool _active;
        Vector2 _start;

        Vector2 ToLocal(PointerEventData e, Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(space, screen, e.pressEventCamera, out var p);
            return p;
        }

        public void OnBeginDrag(PointerEventData e)
        {
            var d = e.position - e.pressPosition;
            _active = (canSwipe == null || canSwipe()) && Mathf.Abs(d.x) >= Mathf.Abs(d.y);
            if (_active) _start = ToLocal(e, e.pressPosition);
        }

        public void OnDrag(PointerEventData e)
        {
            if (_active) onDrag?.Invoke(ToLocal(e, e.position).x - _start.x);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!_active) return;
            _active = false;
            onEnd?.Invoke(ToLocal(e, e.position).x - _start.x);
        }
    }
}
