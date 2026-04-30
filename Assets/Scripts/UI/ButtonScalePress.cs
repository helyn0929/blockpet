using UnityEngine;
using UnityEngine.EventSystems;

public class ButtonScalePress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [SerializeField] float pressedScale = 0.95f;

    Vector3 _normalScale;

    void Awake() => _normalScale = transform.localScale;

    public void OnPointerDown(PointerEventData _) => transform.localScale = _normalScale * pressedScale;
    public void OnPointerUp(PointerEventData _)   => transform.localScale = _normalScale;
    public void OnPointerExit(PointerEventData _) => transform.localScale = _normalScale;
}
