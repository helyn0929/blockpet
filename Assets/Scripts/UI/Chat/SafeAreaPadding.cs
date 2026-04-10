using UnityEngine;

/// <summary>
/// Applies iOS/Android safe-area padding to a RectTransform by adjusting offsets.
/// Common usage: add to header bar (top) and input bar (bottom) so they don't collide with notches/home indicators.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SafeAreaPadding : MonoBehaviour
{
    public enum Edge { Top, Bottom, Left, Right }

    [SerializeField] Edge edge = Edge.Bottom;
    [SerializeField] float extraPadding = 8f;

    RectTransform _rt;
    Rect _lastSafe;
    Vector2 _baseOffsetMin;
    Vector2 _baseOffsetMax;

    void Awake()
    {
        _rt = GetComponent<RectTransform>();
        _baseOffsetMin = _rt.offsetMin;
        _baseOffsetMax = _rt.offsetMax;
        _lastSafe = new Rect(-1, -1, -1, -1);
        Apply();
    }

    void OnEnable() => Apply();

    void Update()
    {
        if (_lastSafe != Screen.safeArea)
            Apply();
    }

    void Apply()
    {
        if (_rt == null) return;
        Rect safe = Screen.safeArea;
        _lastSafe = safe;

        float leftInset = safe.xMin;
        float rightInset = Screen.width - safe.xMax;
        float bottomInset = safe.yMin;
        float topInset = Screen.height - safe.yMax;

        Canvas c = GetComponentInParent<Canvas>();
        float scale = c != null ? c.scaleFactor : 1f;
        if (scale < 0.01f) scale = 1f;

        float l = leftInset / scale;
        float r = rightInset / scale;
        float b = bottomInset / scale;
        float t = topInset / scale;

        Vector2 min = _baseOffsetMin;
        Vector2 max = _baseOffsetMax;

        switch (edge)
        {
            case Edge.Top:
                max.y = -Mathf.Max(0f, t + extraPadding);
                break;
            case Edge.Bottom:
                min.y = Mathf.Max(0f, b + extraPadding);
                break;
            case Edge.Left:
                min.x = Mathf.Max(0f, l + extraPadding);
                break;
            case Edge.Right:
                max.x = -Mathf.Max(0f, r + extraPadding);
                break;
        }

        _rt.offsetMin = min;
        _rt.offsetMax = max;
    }
}

