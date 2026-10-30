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
            _sr.sprite            = sprite;
            _sr.sortingLayerName  = "Pet";
            _sr.sortingOrder      = sortingOrder;

            // Collider used for tap-to-delete hit testing (RoomEditorController raycasts against it).
            var col = GetComponent<BoxCollider2D>();
            if (col == null) col = gameObject.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = sprite != null ? sprite.bounds.size : Vector2.one;
            // 圖的 pivot 不一定在正中央（家具多半在底部），碰撞框要對齊圖本身
            col.offset = sprite != null ? (Vector2)sprite.bounds.center : Vector2.zero;
        }

        /// <summary>世界座標裡圖的上緣中點（佈置模式的浮動按鈕放在這上面）。</summary>
        public Vector3 TopCenter
        {
            get
            {
                var b = _sr.bounds;
                return new Vector3(b.center.x, b.max.y, 0f);
            }
        }

        // ─── Selection ─────────────────────────────────────────────────

        bool _selected;
        float _pulse;

        /// <summary>佈置模式選中這件：一閃一閃並稍微放大，讓玩家知道正在移動哪一件。</summary>
        public void SetSelected(bool selected)
        {
            _selected = selected;
            _pulse = 0f;
            if (!selected) _sr.color = Color.white;
        }

        void Update()
        {
            if (!_selected) return;
            _pulse += Time.unscaledDeltaTime;
            float k = 0.5f + 0.5f * Mathf.Sin(_pulse * 6f);
            _sr.color = Color.Lerp(Color.white, new Color(1f, 0.92f, 0.6f, 1f), k);
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
            if (!editing) SetSelected(false);
        }

        public bool CanDelete { get; private set; }

        /// <summary>Call this from the delete button's OnClick.</summary>
        public void RequestDelete() => OnDeleteRequested?.Invoke(this);
    }
}
