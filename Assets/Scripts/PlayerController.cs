using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(SpriteRenderer))]
public class PlayerController : MonoBehaviour
{
    [Header("Navigation")]
    [SerializeField] float sampleRadius = 1f;

    [Header("Y-Sort")]
    [SerializeField] int sortingBase = 0;
    [SerializeField] float sortingScale = 100f;

    NavMeshAgent _agent;
    SpriteRenderer _sr;
    Camera _cam;

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _sr    = GetComponent<SpriteRenderer>();

        _agent.updateRotation = false;
        _agent.updateUpAxis   = false;
    }

    void Start()
    {
        _cam = Camera.main;
    }

    void Update()
    {
        HandleInput();
        ApplyFlip();
        ApplyYSort();
    }

    void HandleInput()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        if (Input.GetMouseButtonDown(0))
            TryMoveTo(Input.mousePosition);
#else
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            TryMoveTo(Input.GetTouch(0).position);
#endif
    }

    void TryMoveTo(Vector3 screenPos)
    {
        // Convert screen → world (keep agent's Z so the 2D plane stays correct)
        Vector3 worldPos = _cam.ScreenToWorldPoint(screenPos);
        worldPos.z = transform.position.z;

        if (NavMesh.SamplePosition(worldPos, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
            _agent.SetDestination(hit.position);
    }

    void ApplyFlip()
    {
        float vx = _agent.velocity.x;
        if (vx > 0.05f)       _sr.flipX = false;
        else if (vx < -0.05f) _sr.flipX = true;
    }

    void ApplyYSort()
    {
        _sr.sortingOrder = sortingBase - Mathf.RoundToInt(transform.position.y * sortingScale);
    }
}
