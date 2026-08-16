using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(SpriteRenderer))]
public class AIController : MonoBehaviour
{
    public enum State { Idle, Walking }

    [Header("Wander")]
    [SerializeField] float wanderRadius = 4f;
    [SerializeField] float sampleRadius = 1f;

    [Header("Heart Level (1–5)")]
    [Tooltip("Higher level → shorter idle wait and faster movement")]
    [SerializeField, Range(1, 5)] int heartLevel = 1;

    [Header("Y-Sort")]
    [SerializeField] int sortingBase = 0;
    [SerializeField] float sortingScale = 100f;

    NavMeshAgent _agent;
    SpriteRenderer _sr;
    State _state = State.Idle;

    // heartLevel → idle range in seconds (level 1 = 3–6 s, level 5 = 0.5–2 s)
    static readonly float[] IdleMin = { 3f, 2.5f, 2f, 1f, 0.5f };
    static readonly float[] IdleMax = { 6f, 5f,   4f, 3f, 2f   };

    // heartLevel → speed multiplier
    static readonly float[] SpeedMult = { 1f, 1.2f, 1.5f, 1.8f, 2.2f };

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _sr    = GetComponent<SpriteRenderer>();

        _agent.updateRotation = false;
        _agent.updateUpAxis   = false;
    }

    void OnEnable()
    {
        StartCoroutine(StateMachine());
    }

    void OnDisable()
    {
        StopAllCoroutines();
    }

    void Update()
    {
        ApplyFlip();
        ApplyYSort();
    }

    IEnumerator StateMachine()
    {
        while (true)
        {
            switch (_state)
            {
                case State.Idle:
                    yield return RunIdle();
                    break;
                case State.Walking:
                    yield return RunWalking();
                    break;
            }
        }
    }

    IEnumerator RunIdle()
    {
        _agent.ResetPath();
        int lvl = Mathf.Clamp(heartLevel, 1, 5) - 1;
        float wait = Random.Range(IdleMin[lvl], IdleMax[lvl]);
        yield return new WaitForSeconds(wait);
        _state = State.Walking;
    }

    IEnumerator RunWalking()
    {
        int lvl = Mathf.Clamp(heartLevel, 1, 5) - 1;
        _agent.speed = GetBaseSpeed() * SpeedMult[lvl];

        Vector3 target;
        if (!TryGetWanderPoint(out target))
        {
            // No valid point found — back to idle
            _state = State.Idle;
            yield break;
        }

        _agent.SetDestination(target);

        // Wait until agent arrives (or path becomes invalid)
        while (true)
        {
            if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance)
                break;
            if (_agent.pathStatus == NavMeshPathStatus.PathInvalid)
                break;
            yield return null;
        }

        _state = State.Idle;
    }

    bool TryGetWanderPoint(out Vector3 result)
    {
        // Try a few random directions before giving up
        for (int i = 0; i < 8; i++)
        {
            Vector2 offset  = Random.insideUnitCircle * wanderRadius;
            Vector3 candidate = transform.position + new Vector3(offset.x, offset.y, 0f);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }
        }
        result = transform.position;
        return false;
    }

    float GetBaseSpeed()
    {
        // Fall back to whatever speed is already set in the Inspector
        return _agent.speed > 0f ? _agent.speed : 2f;
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

    public State CurrentState => _state;
}
