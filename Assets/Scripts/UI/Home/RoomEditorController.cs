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
        bool _placingItem;
        bool _placingInputGuard; // skip input for one frame after item is selected

        public bool IsEditing { get; private set; }

        // ─── Lifecycle ─────────────────────────────────────────────────

        void Start()
        {
            if (worldCamera == null) worldCamera = Camera.main;
            if (placementPreview != null) placementPreview.enabled = false;
        }

        void Update()
        {
            if (_placingInputGuard) { _placingInputGuard = false; return; }
            if (!IsEditing || !_placingItem) return;
            UpdatePreviewPosition();
            HandlePlacementInput();
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
                SetFrozen(true);
                picker?.Show();
                DecorationManager.Instance.SetAllDecorationsEditMode(true);
            });
        }

        /// <param name="save">Pass false to discard pending placement without saving.</param>
        public void ExitEditMode(bool save)
        {
            if (!IsEditing) return;

            CancelPlacement();
            DecorationManager.Instance.ReleaseEditLock();
            DecorationManager.Instance.SetAllDecorationsEditMode(false);
            IsEditing = false;
            SetFrozen(false);
            picker?.Hide();
        }

        // ─── Item selection ────────────────────────────────────────────

        /// <summary>Call from UI when the player picks a decoration to place.</summary>
        public void OnItemSelected(DecorationItem item)
        {
            if (!IsEditing || item == null) return;
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

        void HandlePlacementInput()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            if (Input.GetMouseButtonDown(0))
            {
                var pos = ScreenToWorld(Input.mousePosition);
                OnPlacementConfirmed(pos);
            }
#else
            if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            {
                var pos = ScreenToWorld(Input.GetTouch(0).position);
                OnPlacementConfirmed(pos);
            }
#endif
        }

        void UpdatePreviewPosition()
        {
            if (placementPreview == null || !placementPreview.enabled) return;

#if UNITY_EDITOR || UNITY_STANDALONE
            placementPreview.transform.position = ScreenToWorld(Input.mousePosition);
#else
            if (Input.touchCount > 0)
                placementPreview.transform.position = ScreenToWorld(Input.GetTouch(0).position);
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
            if (placementPreview != null) placementPreview.enabled = false;
        }

        void SetFrozen(bool frozen)
        {
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb is IEditModeFreezable f) f.SetEditModeFrozen(frozen);
        }
    }
}
