using UnityEngine;
using TMPro;

/// <summary>
/// Shifts a bottom UI strip up when the mobile software keyboard is visible so the input stays above it.
/// Attach to the same GameObject as <see cref="TMP_InputField"/> or assign the field explicitly.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ChatKeyboardAvoidance : MonoBehaviour
{
    [SerializeField] TMP_InputField inputField;
    [Tooltip("RectTransform to move (usually the input bar root). Defaults to this object.")]
    [SerializeField] RectTransform targetToMove;
    [SerializeField] float extraPadding = 8f;

    RectTransform _rt;
    Vector2 _defaultAnchoredPosition;

    void Awake()
    {
        _rt = targetToMove != null ? targetToMove : transform as RectTransform;
        if (inputField == null)
            inputField = GetComponentInParent<TMP_InputField>();
        if (_rt != null)
            _defaultAnchoredPosition = _rt.anchoredPosition;
    }

    void LateUpdate()
    {
        if (_rt == null)
            return;

#if UNITY_IOS || UNITY_ANDROID
        if (inputField != null && inputField.isFocused && TouchScreenKeyboard.visible)
        {
            Rect area = TouchScreenKeyboard.area;
            float px = area.height > 1f ? area.height : 0f;
            Canvas c = _rt.GetComponentInParent<Canvas>();
            float scale = c != null ? c.scaleFactor : 1f;
            if (scale < 0.01f) scale = 1f;
            float lift = px / scale + extraPadding;
            _rt.anchoredPosition = _defaultAnchoredPosition + new Vector2(0f, lift);
        }
        else
            _rt.anchoredPosition = _defaultAnchoredPosition;
#else
        _rt.anchoredPosition = _defaultAnchoredPosition;
#endif
    }
}
