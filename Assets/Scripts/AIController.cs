using System.Collections;
using UnityEngine;

public class AIController : MonoBehaviour
{
    public enum State { Idle, Walking }

    [Header("Wander")]
    [SerializeField] float wanderRadius = 4f;

    [Header("Bounds")]
    [Tooltip("拖入 WalkableBounds 物件的 PolygonCollider2D")]
    [SerializeField] Collider2D walkableBounds;

    [Header("Heart Level (1–5)")]
    [Tooltip("Higher level → shorter idle wait and faster movement")]
    [SerializeField, Range(1, 5)] int heartLevel = 1;

    [Header("Y-Sort")]
    [SerializeField] int sortingBase = 0;
    [SerializeField] float sortingScale = 100f;

    SpriteRenderer _sr;
    State _state = State.Idle;
    Vector3 _startPos;
    Vector3 _target;
    float _speed;

    static readonly float[] IdleMin  = { 3f, 2.5f, 2f,  1f,  0.5f };
    static readonly float[] IdleMax  = { 6f, 5f,   4f,  3f,  2f   };
    static readonly float[] SpeedVal = { 1f, 1.2f, 1.5f,1.8f,2.2f };

    void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
    }

    void OnEnable()
    {
        _startPos = transform.position;
        _target   = transform.position;
        StartCoroutine(StateMachine());
    }

    void OnDisable() => StopAllCoroutines();

    void Update()
    {
        if (_state == State.Walking)
        {
            transform.position = Vector3.MoveTowards(transform.position, _target, _speed * Time.deltaTime);
            ApplyFlip();
        }
        ApplyYSort();
    }

    IEnumerator StateMachine()
    {
        while (true)
        {
            if (_state == State.Idle)
                yield return RunIdle();
            else
                yield return RunWalking();
        }
    }

    IEnumerator RunIdle()
    {
        int lvl = Mathf.Clamp(heartLevel, 1, 5) - 1;
        yield return new WaitForSeconds(Random.Range(IdleMin[lvl], IdleMax[lvl]));

        Vector2 candidate = (Vector2)_startPos + Random.insideUnitCircle * wanderRadius;
        if (walkableBounds != null)
        {
            // 最多試8次找一個在範圍內的點，找不到就用最近合法點
            bool found = false;
            for (int i = 0; i < 8; i++)
            {
                candidate = (Vector2)_startPos + Random.insideUnitCircle * wanderRadius;
                if (walkableBounds.OverlapPoint(candidate)) { found = true; break; }
            }
            if (!found) candidate = walkableBounds.ClosestPoint(candidate);
        }

        _target = new Vector3(candidate.x, candidate.y, transform.position.z);
        _speed  = SpeedVal[Mathf.Clamp(heartLevel, 1, 5) - 1];
        _state  = State.Walking;
    }

    IEnumerator RunWalking()
    {
        while (Vector3.Distance(transform.position, _target) > 0.05f)
            yield return null;
        _state = State.Idle;
    }

    void ApplyFlip()
    {
        float dx = _target.x - transform.position.x;
        if (dx > 0.05f)       _sr.flipX = false;
        else if (dx < -0.05f) _sr.flipX = true;
    }

    void ApplyYSort()
    {
        _sr.sortingOrder = sortingBase - Mathf.RoundToInt(transform.position.y * sortingScale);
    }

    public State CurrentState => _state;
}
