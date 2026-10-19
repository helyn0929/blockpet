using System;
using System.Collections.Generic;
using UnityEngine;
using Firebase.Database;

namespace BlockPet.Decoration
{
    public class DecorationManager : MonoBehaviour
    {
        public static DecorationManager Instance;

        // ─── Inspector ─────────────────────────────────────────────────

        [Header("References")]
        [SerializeField] DecorationDatabase database;
        [Tooltip("Prefab with SpriteRenderer + DecorationObject")]
        [SerializeField] GameObject decorationObjectPrefab;
        [SerializeField] Transform decorationContainer;

        // ─── State ─────────────────────────────────────────────────────

        DatabaseReference _decorRef;
        DatabaseReference _editingByRef;
        string _localUserId;
        string _roomId = "";

        readonly Dictionary<string, DecorationObject> _spawned = new Dictionary<string, DecorationObject>();
        readonly Queue<Action> _mainQueue = new Queue<Action>();

        bool _isListening;
        bool _isEditLockHeld;
        public bool IsEditLockHeld => _isEditLockHeld;
        public bool IsListening => _isListening;

        /// <summary>Fired right after a brand-new decoration is instantiated and Init()'d (not on position updates).</summary>
        public event Action<DecorationObject, DecorationItem> OnDecorationSpawned;
        /// <summary>Fired right before a decoration's GameObject is destroyed locally (deleted by anyone in the room).</summary>
        public event Action<string> OnDecorationRemoved;

        // ─── Lifecycle ─────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()
        {
            // Firebase 初始化是非同步的，訂閱登入成功事件確保 roomId 拿到後再開始監聽
            FirebaseManager.OnLoginSuccess += OnFirebaseReady;
            // 切換房間時跟著換監聽，並清掉上一個房間的家具
            FirebaseManager.OnRoomChanged += OnRoomChanged;

            // 若已登入（重新進主頁）直接啟動
            string roomId = FirebaseManager.Instance?.RoomId ?? "";
            if (!string.IsNullOrEmpty(roomId))
                StartListening(roomId);
        }

        void OnFirebaseReady(bool success)
        {
            if (!success) return;
            string roomId = FirebaseManager.Instance?.RoomId ?? "";
            if (!string.IsNullOrEmpty(roomId) && !_isListening)
                StartListening(roomId);
        }

        void OnRoomChanged()
        {
            string roomId = FirebaseManager.Instance?.RoomId ?? "";
            if (string.IsNullOrEmpty(roomId))
            {
                ReleaseEditLock();
                StopListening();
                ClearSpawned();
                _roomId = "";
                return;
            }
            StartListening(roomId);
        }

        void Update()
        {
            lock (_mainQueue)
                while (_mainQueue.Count > 0) _mainQueue.Dequeue()?.Invoke();
        }

        void OnDestroy()
        {
            FirebaseManager.OnLoginSuccess -= OnFirebaseReady;
            FirebaseManager.OnRoomChanged -= OnRoomChanged;
            StopListening();
            if (Instance == this) Instance = null;
        }

        // ─── Listening ─────────────────────────────────────────────────

        public void StartListening(string roomId)
        {
            // 已在監聽同一個房間：不要重開（同路徑立刻 unlisten + listen 會觸發 Firebase desktop 斷言）
            if (_isListening && roomId == _roomId) return;
            if (_isListening) StopListening();

            if (roomId != _roomId)
            {
                ReleaseEditLock();   // 用舊房間的 editingBy ref 釋放
                ClearSpawned();
            }
            _roomId = roomId;

            _localUserId = FirebaseManager.Instance?.GetUserId() ?? "";

            var roomRef = FirebaseDatabase.DefaultInstance.GetReference("Rooms").Child(roomId);
            _decorRef      = roomRef.Child("decorations");
            _editingByRef  = roomRef.Child("editingBy");

            _decorRef.ChildAdded   += OnChildAdded;
            _decorRef.ChildChanged += OnChildChanged;
            _decorRef.ChildRemoved += OnChildRemoved;
            _isListening = true;
        }

        public void StopListening()
        {
            if (!_isListening || _decorRef == null) return;
            _decorRef.ChildAdded   -= OnChildAdded;
            _decorRef.ChildChanged -= OnChildChanged;
            _decorRef.ChildRemoved -= OnChildRemoved;
            _isListening = false;
        }

        // ─── Firebase → scene ──────────────────────────────────────────

        void OnChildAdded(object sender, ChildChangedEventArgs e)
        {
            if (e.DatabaseError != null) { Debug.LogWarning($"[DecorationManager] ChildAdded error: {e.DatabaseError}"); return; }
            var snap = e.Snapshot;
            lock (_mainQueue) _mainQueue.Enqueue(() => { if (IsCurrentRoom(snap)) SpawnOrUpdate(snap); });
        }

        void OnChildChanged(object sender, ChildChangedEventArgs e)
        {
            if (e.DatabaseError != null) { Debug.LogWarning($"[DecorationManager] ChildChanged error: {e.DatabaseError}"); return; }
            var snap = e.Snapshot;
            lock (_mainQueue) _mainQueue.Enqueue(() => { if (IsCurrentRoom(snap)) SpawnOrUpdate(snap); });
        }

        void OnChildRemoved(object sender, ChildChangedEventArgs e)
        {
            if (e.DatabaseError != null) { Debug.LogWarning($"[DecorationManager] ChildRemoved error: {e.DatabaseError}"); return; }
            var snap = e.Snapshot;
            lock (_mainQueue) _mainQueue.Enqueue(() => { if (IsCurrentRoom(snap)) RemoveLocal(snap.Key); });
        }

        /// <summary>丟掉切換房間前就排進佇列的舊房間事件（路徑為 Rooms/{roomId}/decorations/{decorId}）。</summary>
        bool IsCurrentRoom(DataSnapshot snap)
        {
            string room = snap?.Reference?.Parent?.Parent?.Key;
            return _isListening && room == _roomId;
        }

        void ClearSpawned()
        {
            foreach (var kvp in _spawned)
            {
                if (kvp.Value == null) continue;
                kvp.Value.OnDeleteRequested -= HandleDeleteRequested;
                OnDecorationRemoved?.Invoke(kvp.Key);
                Destroy(kvp.Value.gameObject);
            }
            _spawned.Clear();
        }

        void SpawnOrUpdate(DataSnapshot snap)
        {
            string decorId      = snap.Key;
            string itemId       = snap.Child("itemId").Value?.ToString() ?? "";
            float  x            = ParseFloat(snap.Child("x").Value);
            float  y            = ParseFloat(snap.Child("y").Value);
            float  rotation     = ParseFloat(snap.Child("rotation").Value);
            int    sortingOrder = ParseInt(snap.Child("sortingOrder").Value);
            string placedBy     = snap.Child("placedBy").Value?.ToString() ?? "";

            DecorationItem item = database.GetById(itemId);
            if (item == null) { Debug.LogWarning($"[DecorationManager] Unknown itemId: {itemId}"); return; }

            if (_spawned.TryGetValue(decorId, out var existing))
            {
                existing.transform.position = new Vector3(x, y, 0f);
                existing.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
                existing.GetComponent<SpriteRenderer>().sortingOrder = sortingOrder;
            }
            else
            {
                var go = Instantiate(decorationObjectPrefab,
                                     new Vector3(x, y, 0f),
                                     Quaternion.Euler(0f, 0f, rotation),
                                     decorationContainer);
                go.name = $"Decor_{decorId}";
                go.transform.localScale = Vector3.one * item.worldScale;
                var obj = go.GetComponent<DecorationObject>();
                obj.Init(decorId, itemId, placedBy, item.worldSprite, sortingOrder);
                obj.OnDeleteRequested += HandleDeleteRequested;
                _spawned[decorId] = obj;
                OnDecorationSpawned?.Invoke(obj, item);
            }
        }

        void RemoveLocal(string decorId)
        {
            if (!_spawned.TryGetValue(decorId, out var obj)) return;
            obj.OnDeleteRequested -= HandleDeleteRequested;
            OnDecorationRemoved?.Invoke(decorId);
            Destroy(obj.gameObject);
            _spawned.Remove(decorId);
        }

        void HandleDeleteRequested(DecorationObject obj)
        {
            RemoveDecoration(obj.DecorId, null);
        }

        // ─── Place ─────────────────────────────────────────────────────

        /// <summary>
        /// Deducts coins and writes to Firebase.
        /// onDone receives (success, decorId) — decorId is null on failure.
        /// </summary>
        public void PlaceDecoration(DecorationItem item, Vector3 worldPos, float rotation,
                                    int sortingOrder, Action<bool, string> onDone)
        {
            var newRef  = _decorRef.Push();
            string decorId = newRef.Key;

            var data = new Dictionary<string, object>
            {
                { "itemId",       item.itemId },
                { "x",            worldPos.x  },
                { "y",            worldPos.y  },
                { "rotation",     rotation    },
                { "sortingOrder", sortingOrder },
                { "placedBy",     _localUserId },
                { "createdAt",    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
            };

            newRef.SetValueAsync(data).ContinueWith(task =>
            {
                bool ok = !task.IsFaulted && !task.IsCanceled;
                lock (_mainQueue) _mainQueue.Enqueue(() =>
                {
                    onDone?.Invoke(ok, ok ? decorId : null);
                });
            });
        }

        // ─── Remove ────────────────────────────────────────────────────

        public void RemoveDecoration(string decorId, Action<bool> onDone)
        {
            _decorRef.Child(decorId).RemoveValueAsync().ContinueWith(task =>
            {
                bool ok = !task.IsFaulted && !task.IsCanceled;
                lock (_mainQueue) _mainQueue.Enqueue(() => onDone?.Invoke(ok));
            });
        }

        // ─── Edit lock ─────────────────────────────────────────────────

        public void AcquireEditLock(Action<bool> onResult)
        {
            if (_editingByRef == null)
            {
                Debug.LogWarning("[DecorationManager] AcquireEditLock called before StartListening.");
                onResult?.Invoke(false);
                return;
            }
            _editingByRef.GetValueAsync().ContinueWith(readTask =>
            {
                if (readTask.IsFaulted || readTask.IsCanceled)
                {
                    lock (_mainQueue) _mainQueue.Enqueue(() => onResult?.Invoke(false));
                    return;
                }

                string holder = readTask.Result.Value?.ToString() ?? "";
                bool free = string.IsNullOrEmpty(holder) || holder == _localUserId;

                if (!free)
                {
                    lock (_mainQueue) _mainQueue.Enqueue(() => onResult?.Invoke(false));
                    return;
                }

                _editingByRef.SetValueAsync(_localUserId).ContinueWith(writeTask =>
                {
                    bool ok = !writeTask.IsFaulted && !writeTask.IsCanceled;
                    lock (_mainQueue) _mainQueue.Enqueue(() =>
                    {
                        _isEditLockHeld = ok;
                        onResult?.Invoke(ok);
                    });
                });
            });
        }

        public void ReleaseEditLock()
        {
            if (!_isEditLockHeld) return;
            _editingByRef.SetValueAsync(null);
            _isEditLockHeld = false;
        }

        // ─── Edit mode propagation ─────────────────────────────────────

        public void SetAllDecorationsEditMode(bool editing)
        {
            foreach (var kvp in _spawned)
            {
                bool canDelete = kvp.Value.PlacedBy == _localUserId;
                kvp.Value.SetEditMode(editing, canDelete);
            }
        }

        // ─── Helpers ───────────────────────────────────────────────────

        static float ParseFloat(object v) =>
            v != null && float.TryParse(v.ToString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float r) ? r : 0f;

        static int ParseInt(object v) =>
            v != null && int.TryParse(v.ToString(), out int r) ? r : 0;
    }
}
