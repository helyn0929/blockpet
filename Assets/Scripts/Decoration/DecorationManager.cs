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

        readonly Dictionary<string, DecorationObject> _spawned = new Dictionary<string, DecorationObject>();
        readonly Queue<Action> _mainQueue = new Queue<Action>();

        bool _isListening;
        bool _isEditLockHeld;
        public bool IsEditLockHeld => _isEditLockHeld;

        // ─── Lifecycle ─────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Update()
        {
            lock (_mainQueue)
                while (_mainQueue.Count > 0) _mainQueue.Dequeue()?.Invoke();
        }

        void OnDestroy()
        {
            StopListening();
            if (Instance == this) Instance = null;
        }

        // ─── Listening ─────────────────────────────────────────────────

        public void StartListening(string roomId)
        {
            if (_isListening) StopListening();

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
            lock (_mainQueue) _mainQueue.Enqueue(() => SpawnOrUpdate(snap));
        }

        void OnChildChanged(object sender, ChildChangedEventArgs e)
        {
            if (e.DatabaseError != null) { Debug.LogWarning($"[DecorationManager] ChildChanged error: {e.DatabaseError}"); return; }
            var snap = e.Snapshot;
            lock (_mainQueue) _mainQueue.Enqueue(() => SpawnOrUpdate(snap));
        }

        void OnChildRemoved(object sender, ChildChangedEventArgs e)
        {
            if (e.DatabaseError != null) { Debug.LogWarning($"[DecorationManager] ChildRemoved error: {e.DatabaseError}"); return; }
            string key = e.Snapshot.Key;
            lock (_mainQueue) _mainQueue.Enqueue(() => RemoveLocal(key));
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
                var obj = go.GetComponent<DecorationObject>();
                obj.Init(decorId, itemId, placedBy, item.worldSprite, sortingOrder);
                obj.OnDeleteRequested += HandleDeleteRequested;
                _spawned[decorId] = obj;
            }
        }

        void RemoveLocal(string decorId)
        {
            if (!_spawned.TryGetValue(decorId, out var obj)) return;
            obj.OnDeleteRequested -= HandleDeleteRequested;
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
            if (!EconomyManager.Instance.TrySpend(item.price))
            {
                onDone?.Invoke(false, null);
                return;
            }

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
                    if (!ok) EconomyManager.Instance?.AddCoins(item.price); // refund
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
