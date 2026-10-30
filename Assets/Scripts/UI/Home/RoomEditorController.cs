using UnityEngine;
using BlockPet.Core;
using BlockPet.Decoration;

namespace BlockPet.UI.Home
{
    public class RoomEditorController : MonoBehaviour
    {
        // ─── Inspector ─────────────────────────────────────────────────

        [Header("Edit Mode UI")]
        [SerializeField] DecorationPickerController picker;


        [Header("Placement preview")]
        [Tooltip("A SpriteRenderer placed at scene root used as a placement ghost")]
        [SerializeField] SpriteRenderer placementPreview;
        [SerializeField] Camera worldCamera;

        // ─── State ─────────────────────────────────────────────────────

        DecorationItem _pendingItem;
        DecorationItem _queuedItem; // 從商店「去擺放」：拿到編輯鎖後直接開始擺這件
        bool _placingItem;
        bool _placingInputGuard; // skip input for one frame after item is selected

        public bool IsEditing { get; private set; }

        /// <summary>True while any RoomEditorController is in edit mode. Lets interactive world objects
        /// ignore taps that are meant for edit-mode placement/deletion instead.</summary>
        public static bool AnyEditModeActive { get; private set; }

        // ─── Lifecycle ─────────────────────────────────────────────────

        void Start()
        {
            if (worldCamera == null) worldCamera = Camera.main;
            if (placementPreview != null) placementPreview.enabled = false;
        }

        void Update()
        {
            if (_placingInputGuard) { _placingInputGuard = false; return; }
            if (!IsEditing) return;

            HandlePointer();
            UpdateActions();
        }

        // ─── Edit mode ─────────────────────────────────────────────────

        public void EnterEditMode()
        {
            if (IsEditing) return;

            // 若 Firebase 還沒就緒，嘗試補呼叫 StartListening
            if (!DecorationManager.Instance.IsListening)
            {
                string roomId = FirebaseManager.Instance?.RoomId ?? "";
                if (string.IsNullOrEmpty(roomId))
                {
                    Debug.LogWarning("[RoomEditorController] 尚未加入房間，無法進入編輯模式。");
                    return;
                }
                DecorationManager.Instance.StartListening(roomId);
            }

            DecorationManager.Instance.AcquireEditLock(ok =>
            {
                if (!ok)
                {
                    Debug.Log("[RoomEditorController] Edit lock unavailable — another player is editing.");
                    return;
                }

                IsEditing = true;
                AnyEditModeActive = true;
                SetFrozen(true);
                picker?.Show();
                DecorationManager.Instance.SetAllDecorationsEditMode(true);
                if (_queuedItem != null)
                {
                    var queued = _queuedItem;
                    _queuedItem = null;
                    OnItemSelected(queued);
                }
            });
        }

        /// <summary>進入佈置模式並直接開始擺放這件家具（商店的「去擺放」）。</summary>
        public void EnterEditModeAndPlace(DecorationItem item)
        {
            if (IsEditing) { OnItemSelected(item); return; }
            _queuedItem = item;
            EnterEditMode();
        }

        /// <param name="save">Pass false to discard pending placement without saving.</param>
        public void ExitEditMode(bool save)
        {
            if (!IsEditing) return;

            CancelPlacement();
            Deselect();
            _dragging = false;
            DecorationManager.Instance.ReleaseEditLock();
            DecorationManager.Instance.SetAllDecorationsEditMode(false);
            IsEditing = false;
            AnyEditModeActive = false;
            SetFrozen(false);
            picker?.Hide();
        }

        // ─── Item selection ────────────────────────────────────────────

        /// <summary>Call from UI when the player picks a decoration to place.</summary>
        public void OnItemSelected(DecorationItem item)
        {
            if (!IsEditing || item == null) return;
            Deselect();
            _pendingItem       = item;
            _placingItem       = true;
            _placingInputGuard = true;

            if (placementPreview != null)
            {
                placementPreview.sprite                      = item.worldSprite != null ? item.worldSprite : item.icon;
                placementPreview.color                       = new Color(1f, 1f, 1f, 0.6f);
                placementPreview.sortingLayerName            = "Pet";
                placementPreview.sortingOrder                = 999;
                placementPreview.transform.localScale        = Vector3.one * item.worldScale;
                placementPreview.enabled                     = true;

                // 立刻移到螢幕中央，不用等手指移動才出現
                Vector3 center = worldCamera.ScreenToWorldPoint(
                    new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 10f));
                center.z = 0f;
                placementPreview.transform.position = center;
            }
        }

        /// <summary>Call from a confirm button if you prefer explicit two-step placement.</summary>
        public void OnPlacementConfirmed(Vector3 worldPos)
        {
            if (!_placingItem || _pendingItem == null) return;

            var item = _pendingItem;
            CancelPlacement();

            DecorationManager.Instance.PlaceDecoration(
                item, worldPos, 0f, item.defaultSortingOrder,
                (ok, decorId) =>
                {
                    if (!ok) Debug.Log("[RoomEditorController] Placement failed (insufficient coins or network error).");
                });
        }

        public void OnPlacementCancelled() => CancelPlacement();

        // ─── Button helpers (no-arg wrappers for UnityEvent) ──────────

        /// <summary>Wired to the Exit/Done button inside editModePanel.</summary>
        public void OnExitEditModeButton() => ExitEditMode(true);

        /// <summary>Wired to a Cancel button if you want to discard placement.</summary>
        public void OnCancelEditModeButton() => ExitEditMode(false);

        // ─── Input ─────────────────────────────────────────────────────
        // 手機上的操作：
        // ・新家具：從下方挑一件 → 出現在畫面中央 → 手指在畫面任何地方拖曳都會帶著它走（不會被手指擋住）→ 按「放這裡」
        // ・已擺好的家具：點一下選中（一閃一閃）→ 按住拖曳移動，放開就存 → 浮動按鈕「收回」或「好了」
        // 按在下方家具列或浮動按鈕上的手勢不算在場景裡。

        DecorationObject _selected;
        Transform _dragTarget;
        Vector3 _dragOffset;
        Vector2 _downScreen;
        bool _dragging, _dragMoved, _gestureIgnored;

        const float DragThresholdPx = 12f;

        void HandlePointer()
        {
            if (!ReadPointer(out bool down, out bool held, out bool up, out Vector2 screen)) return;

            if (down)
            {
                _gestureIgnored = picker != null && picker.IsOverUI(screen);
                if (_gestureIgnored) return;
                _downScreen = screen;
                _dragMoved = false;
                Vector3 world = ScreenToWorld(screen);

                if (_placingItem && placementPreview != null)
                {
                    // 新家具：拖哪裡都帶著預覽走（相對位移）
                    BeginDrag(placementPreview.transform, world);
                    return;
                }

                var hit = FindDecorAt(world);
                if (hit != null)
                {
                    Select(hit);
                    BeginDrag(hit.transform, world);
                }
                else
                {
                    Deselect();
                }
                return;
            }

            if (_gestureIgnored) { if (up) _gestureIgnored = false; return; }

            if (held && _dragging && _dragTarget != null)
            {
                if (!_dragMoved && (screen - _downScreen).magnitude >= DragThresholdPx) _dragMoved = true;
                if (_dragMoved) _dragTarget.position = ScreenToWorld(screen) + _dragOffset;
            }

            if (up)
            {
                // 拖完已擺好的家具：寫回新位置
                if (_dragging && _dragMoved && _selected != null && _dragTarget == _selected.transform)
                {
                    DecorationManager.Instance.MoveDecoration(_selected.DecorId, _selected.transform.position, ok =>
                    {
                        if (!ok) Debug.Log("[RoomEditorController] 移動家具沒有存到（網路錯誤）。");
                    });
                }
                _dragging = false;
                _dragTarget = null;
            }
        }

        void BeginDrag(Transform target, Vector3 world)
        {
            _dragTarget = target;
            _dragOffset = target.position - world;
            _dragOffset.z = 0f;
            _dragging = true;
        }

        /// <summary>只抓自己擺的家具（CanDelete）；疊在一起時抓排序最上面那件。</summary>
        DecorationObject FindDecorAt(Vector3 worldPos)
        {
            DecorationObject best = null;
            int bestOrder = int.MinValue;
            foreach (var hit in Physics2D.OverlapPointAll(worldPos))
            {
                var decor = hit.GetComponent<DecorationObject>();
                if (decor == null || !decor.CanDelete) continue;
                int order = decor.GetComponent<SpriteRenderer>().sortingOrder;
                if (best == null || order > bestOrder) { best = decor; bestOrder = order; }
            }
            return best;
        }

        void Select(DecorationObject decor)
        {
            if (_selected == decor) return;
            Deselect();
            _selected = decor;
            _selected.SetSelected(true);
        }

        void Deselect()
        {
            if (_selected != null) _selected.SetSelected(false);
            _selected = null;
        }

        /// <summary>浮動按鈕跟著正在擺的新家具或選中的家具。</summary>
        void UpdateActions()
        {
            if (picker == null) return;
            if (_placingItem && placementPreview != null && placementPreview.enabled)
            {
                var b = placementPreview.bounds;
                picker.ShowActions(new Vector3(b.center.x, b.max.y, 0f), worldCamera, true);
            }
            else if (_selected != null)
            {
                picker.ShowActions(_selected.TopCenter, worldCamera, false);
            }
            else
            {
                picker.HideActions();
            }
        }

        // ─── Floating action buttons（DecorationPickerController 呼叫）──

        /// <summary>「放這裡」：新家具擺在預覽目前的位置。</summary>
        public void OnActionConfirm()
        {
            if (!_placingItem || placementPreview == null) return;
            OnPlacementConfirmed(placementPreview.transform.position);
        }

        /// <summary>「取消」：不擺這件新家具。</summary>
        public void OnActionCancel() => CancelPlacement();

        /// <summary>「收回」：把選中的家具從營地拿掉。</summary>
        public void OnActionRemove()
        {
            if (_selected == null) return;
            var decor = _selected;
            Deselect();
            decor.RequestDelete();
        }

        /// <summary>「好了」：取消選取。</summary>
        public void OnActionDone() => Deselect();

        static bool ReadPointer(out bool down, out bool held, out bool up, out Vector2 pos)
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            down = Input.GetMouseButtonDown(0);
            held = Input.GetMouseButton(0);
            up = Input.GetMouseButtonUp(0);
            pos = Input.mousePosition;
            return down || held || up;
#else
            down = held = up = false;
            pos = default;
            if (Input.touchCount == 0) return false;
            var t = Input.GetTouch(0);
            pos = t.position;
            down = t.phase == TouchPhase.Began;
            held = t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary;
            up = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
            return true;
#endif
        }

        Vector3 ScreenToWorld(Vector3 screenPos)
        {
            var world = worldCamera.ScreenToWorldPoint(screenPos);
            world.z = 0f;
            return world;
        }

        // ─── Helpers ───────────────────────────────────────────────────

        void CancelPlacement()
        {
            _placingItem = false;
            _pendingItem = null;
            if (placementPreview != null)
            {
                placementPreview.enabled = false;
                if (_dragTarget == placementPreview.transform) { _dragging = false; _dragTarget = null; }
            }
        }

        void SetFrozen(bool frozen)
        {
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb is IEditModeFreezable f) f.SetEditModeFrozen(frozen);
        }
    }
}
