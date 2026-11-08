using System.Collections;
using UnityEngine;
using BlockPet.Core;

public class AIController : MonoBehaviour, IEditModeFreezable
{
    public enum State { Idle, Walking }

    /// <summary>There is exactly one shared room pet — convenience accessor for systems that need
    /// to summon it (e.g. sending it to walk to a point).</summary>
    public static AIController Instance { get; private set; }

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
    bool _frozen;

    /// <summary>True while the pet is running a directed action (e.g. walking over to react to
    /// something) and should not be re-summoned until it finishes and calls ReleaseBusy().</summary>
    public bool IsBusy { get; private set; }

    static readonly float[] IdleMin  = { 3f, 2.5f, 2f,  1f,  0.5f };
    static readonly float[] IdleMax  = { 6f, 5f,   4f,  3f,  2f   };
    /// <summary>基本移動速度（愛心乘數 ×1.0 時，每秒幾格；2026-11-05 定：沿用原本 3 顆心的 1.5）。實際速度＝基本 × 愛心乘數（EconomyRules）。</summary>
    const float BaseSpeed = 1.5f;
    float LevelSpeed => BaseSpeed * BlockPet.Feed.EconomyRules.LevelMultiplier(heartLevel);
    bool _still;

    /// <summary>昏倒時停住不動（規格 3.2 暫用值），醒來再繼續走。</summary>
    public void SetStill(bool still) => _still = still;

    void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        Instance = this;
    }

    void OnEnable()
    {
        _startPos = transform.position;
        _target   = transform.position;
        StartCoroutine(StateMachine());
    }

    void OnDisable() => StopAllCoroutines();

    /// <summary>愛心星等 ★1～★5（PetHealthManager 依房間愛心即時設定）：越高待機越短、走越快，下一段待機／移動開始時生效。</summary>
    public void SetHeartLevel(int level) => heartLevel = Mathf.Clamp(level, 1, 5);

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void SetEditModeFrozen(bool frozen)
    {
        _frozen = frozen;
        if (frozen)
        {
            StopAllCoroutines();
            _state = State.Idle;
            // A directed action (e.g. GoTo) was interrupted — let the caller re-issue it later.
            IsBusy = false;
        }
        else if (gameObject.activeInHierarchy)
        {
            StartCoroutine(StateMachine());
        }
    }

    // ================================================================
    //  Directed action — interrupts wandering to walk to a specific
    //  point and hand control back via a callback.
    // ================================================================

    /// <summary>
    /// Interrupts wandering and walks the pet to <paramref name="worldPos"/>. Calls
    /// <paramref name="onArrived"/> once it gets there. Does nothing if already busy —
    /// callers should keep retrying (e.g. from an Update loop) until it returns true.
    /// The caller MUST call <see cref="ReleaseBusy"/> once it's done with the pet
    /// (e.g. after an eat animation finishes) so wandering can resume.
    /// </summary>
    public bool GoTo(Vector3 worldPos, System.Action onArrived)
    {
        if (IsBusy || _frozen) return false;
        IsBusy = true;
        StopAllCoroutines();
        StartCoroutine(GoToRoutine(worldPos, onArrived));
        return true;
    }

    /// <summary>Call once a directed action (started via GoTo) is fully done, so the pet resumes wandering.</summary>
    public void ReleaseBusy()
    {
        IsBusy = false;
        if (gameObject.activeInHierarchy) StartCoroutine(StateMachine());
    }

    IEnumerator GoToRoutine(Vector3 worldPos, System.Action onArrived)
    {
        _target = new Vector3(worldPos.x, worldPos.y, transform.position.z);
        _speed  = LevelSpeed;
        _state  = State.Walking;

        while (Vector3.Distance(transform.position, _target) > 0.05f)
            yield return null;

        _state = State.Idle;
        onArrived?.Invoke();
        // Note: IsBusy stays true — caller releases it via ReleaseBusy() once fully done.
    }

    void Update()
    {
        if (!_frozen && !_still && _state == State.Walking)
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
        while (_still) yield return null; // 昏倒中：不找新的目的地

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
        _speed  = LevelSpeed;
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
        _sr.sortingOrder = SortOrderForY(transform.position.y);
    }

    /// <summary>
    /// 前後順序（設計文件 5.28 ③）：腳越低越前面。家具用底座最下角的高度、寵物用腳的位置，同一套公式，
    /// 所以和 i + j + 寬 + 深 越大越前面的規則一樣。
    /// </summary>
    public static int SortOrderForY(float worldY)
    {
        var a = Instance;
        int b = a != null ? a.sortingBase : 0;
        float k = a != null ? a.sortingScale : 100f;
        return b - Mathf.RoundToInt(worldY * k);
    }

    public State CurrentState => _state;
}
