using UnityEngine;

/// <summary>
/// Insets this RectTransform to match <see cref="Screen.safeArea"/> (notches, home indicator).
/// Add to the root that should stay inside safe bounds; keep a full-bleed background as a sibling outside this object if desired.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class UISafeAreaPadding : MonoBehaviour
{
    RectTransform _rt;
    Rect _last;

    void Awake()
    {
        _rt = transform as RectTransform;
    }

    void OnEnable()
    {
        Apply();
    }

    void Update()
    {
        if (Screen.safeArea != _last)
            Apply();
    }

    public void Apply()
    {
        if (_rt == null)
            _rt = transform as RectTransform;
        if (_rt == null)
            return;

        _last = Screen.safeArea;
        _rt.anchorMin = Vector2.zero;
        _rt.anchorMax = Vector2.one;
        _rt.offsetMin = new Vector2(_last.xMin, _last.yMin);
        _rt.offsetMax = new Vector2(_last.xMax - Screen.width, _last.yMax - Screen.height);
    }
}
