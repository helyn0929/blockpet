using UnityEngine;
using UnityEngine.EventSystems;

namespace BlockPet.UI.Room
{
    /// <summary>把轉盤上的按下／拖曳／放開轉成設計座標（左上為原點、y 向下）交給 <see cref="RoomRingController"/>。</summary>
    public class RoomRingInput : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IPointerUpHandler
    {
        internal RoomRingController owner;

        Vector2 ToDesign(PointerEventData e)
        {
            var rt = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var local);
            var r = rt.rect;
            return new Vector2(local.x - r.xMin, r.yMax - local.y);
        }

        public void OnPointerDown(PointerEventData e) => owner?.OnRingDown(ToDesign(e));
        // 有實作 IBeginDragHandler，拖曳才會留在轉盤上，不會被外層捲動元件搶走
        public void OnBeginDrag(PointerEventData e) { }
        public void OnDrag(PointerEventData e) => owner?.OnRingDrag(ToDesign(e));
        public void OnPointerUp(PointerEventData e) => owner?.OnRingUp(ToDesign(e), !e.dragging);
    }
}
