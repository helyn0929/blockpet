using System;
using UnityEngine;

namespace BlockPet.Decoration
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class DecorationObject : MonoBehaviour
    {
        // ─── Identity ──────────────────────────────────────────────────

        public string DecorId  { get; private set; }
        public string ItemId   { get; private set; }
        public string PlacedBy { get; private set; }

        SpriteRenderer _sr;

        // ─── Events ────────────────────────────────────────────────────

        /// <summary>Fired when the user taps the delete button in edit mode.</summary>
        public event Action<DecorationObject> OnDeleteRequested;

        // ─── Init ──────────────────────────────────────────────────────

        void Awake() => _sr = GetComponent<SpriteRenderer>();

        public void Init(string decorId, string itemId, string placedBy, Sprite sprite, int sortingOrder)
        {
            DecorId  = decorId;
            ItemId   = itemId;
            PlacedBy = placedBy;
            _sr.sprite       = sprite;
            _sr.sortingOrder = sortingOrder;
        }

        // ─── Edit mode ─────────────────────────────────────────────────

        /// <summary>
        /// Called by DecorationManager when entering/leaving edit mode.
        /// canDelete is true only for items placed by the local user.
        /// Hook this up to a delete-button overlay in your UI prefab.
        /// </summary>
        public void SetEditMode(bool editing, bool canDelete)
        {
            // Override in a subclass or connect a UI overlay via UnityEvent/event.
            // Base implementation is intentionally empty — the delete button
            // lives on the prefab and can query CanDelete from here.
            CanDelete = editing && canDelete;
        }

        public bool CanDelete { get; private set; }

        /// <summary>Call this from the delete button's OnClick.</summary>
        public void RequestDelete() => OnDeleteRequested?.Invoke(this);
    }
}
