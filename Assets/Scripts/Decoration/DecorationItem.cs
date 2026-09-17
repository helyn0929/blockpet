using UnityEngine;

namespace BlockPet.Decoration
{
    public enum DecorationCategory { Furniture, Wall, Floor, Plant, Misc, Interactive }

    [CreateAssetMenu(fileName = "New Decoration Item", menuName = "BlockPet/Decoration Item")]
    public class DecorationItem : ScriptableObject
    {
        public string itemId;
        public string displayName;
        public Sprite icon;
        public Sprite worldSprite;
        public int price;
        public DecorationCategory category;
        public Vector2 pivotOffset;
        public int defaultSortingOrder;
        [Tooltip("世界裡的縮放比例，調到跟角色比例相符")]
        public float worldScale = 1f;

        [Header("Interactive-only (e.g. fermentation jar)")]
        [Tooltip("Sprite shown while a photo is digesting inside. Leave empty to just tint worldSprite instead.")]
        public Sprite digestingSprite;
        [Tooltip("Sprite shown once digestion is done and the pet is on its way to eat. Leave empty to reuse digestingSprite/worldSprite.")]
        public Sprite readySprite;
    }
}
