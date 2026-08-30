using UnityEngine;

namespace BlockPet.Decoration
{
    public enum DecorationCategory { Furniture, Wall, Floor, Plant, Misc }

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
    }
}
