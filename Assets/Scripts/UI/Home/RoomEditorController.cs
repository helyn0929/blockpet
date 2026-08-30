using System.Collections.Generic;
using UnityEngine;
using BlockPet.Core;
using BlockPet.Decoration;

namespace BlockPet.UI.Home
{
    public class RoomEditorController : MonoBehaviour
    {
        // ─── Inspector ─────────────────────────────────────────────────

        [Header("Edit Mode UI")]
        [Tooltip("Panel shown while editing (item picker, confirm/cancel buttons, etc.)")]
        [SerializeField] GameObject editModePanel;

        [Header("Freeze targets")]
        [Tooltip("Drag in PlayerController and AIController GameObjects here")]
        [SerializeField] List<MonoBehaviour> freezeTargets;

        [Header("Placement preview")]
        [Tooltip("A SpriteRenderer placed at scene root used as a placement ghost")]
        [SerializeField] SpriteRenderer placementPreview;
        [SerializeField] Camera worldCamera;

        // ─── State ─────────────────────────────────────────────────────

        DecorationItem _pendingItem;
        bool _placingItem;

        public bool IsEditing { get; private set; }

        // ─── Lifecycle ─────────────────────────────────────────────────

        void Start()
        {
            if (worldCamera == null) worldCamera = Camera.main;
            if (editModePanel != null) editModePanel.SetActive(false);
            if (placementPreview != null) placementPreview.enabled = false;
        }

        void Update()
        {
            if (!IsEditing || !_placingItem) return;
            UpdatePreviewPosition();
            HandlePlacementInput();
        }

        // ─── Edit mode ─────────────────────────────────────────────────

        public void EnterEditMode()
        {
            if (IsEditing) return;

            DecorationManager.Instance.AcquireEditLock(ok =>
            {
                if (!ok)
                {
                    Debug.Log("[RoomEditorController] Edit lock unavailable — another player is editing.");
                    return;
                }

                IsEditing = true;
                SetFrozen(true);
                if (editModePanel != null) editModePanel.SetActive(true);
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
            if (editModePanel != null) editModePanel.SetActive(false);
        }

        // ─── Item selection ────────────────────────────────────────────

        /// <summary>Call from UI when the player picks a decoration to place.</summary>
        public void OnItemSelected(DecorationItem item)
        {
            if (!IsEditing || item == null) return;
            _pendingItem = item;
            _placingItem = true;

            if (placementPreview != null)
            {
                placementPreview.sprite  = item.worldSprite;
                placementPreview.enabled = true;
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
            foreach (var mb in freezeTargets)
                if (mb is IEditModeFreezable f) f.SetEditModeFrozen(frozen);
        }
    }
}
