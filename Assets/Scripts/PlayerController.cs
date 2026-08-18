using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] float moveSpeed = 3f;

    [Header("Y-Sort")]
    [SerializeField] int sortingBase = 0;
    [SerializeField] float sortingScale = 100f;

    SpriteRenderer _sr;
    Camera _cam;
    Vector3 _target;
    bool _moving;

    void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        _target = transform.position;
    }

    void Start()
    {
        _cam = Camera.main;
    }

    void Update()
    {
        HandleInput();
        MoveToTarget();
        ApplyFlip();
        ApplyYSort();
    }

    void HandleInput()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        if (Input.GetMouseButtonDown(0))
            SetTarget(Input.mousePosition);
#else
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            SetTarget(Input.GetTouch(0).position);
#endif
    }

    void SetTarget(Vector3 screenPos)
    {
        Vector3 world = _cam.ScreenToWorldPoint(screenPos);
        world.z = transform.position.z;
        _target = world;
        _moving = true;
    }

    void MoveToTarget()
    {
        if (!_moving) return;
        transform.position = Vector3.MoveTowards(transform.position, _target, moveSpeed * Time.deltaTime);
        if (Vector3.Distance(transform.position, _target) < 0.05f)
            _moving = false;
    }

    void ApplyFlip()
    {
        if (!_moving) return;
        float dx = _target.x - transform.position.x;
        if (dx > 0.05f)       _sr.flipX = false;
        else if (dx < -0.05f) _sr.flipX = true;
    }

    void ApplyYSort()
    {
        _sr.sortingOrder = sortingBase - Mathf.RoundToInt(transform.position.y * sortingScale);
    }
}
