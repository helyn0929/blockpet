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
        [Tooltip("世界裡的縮放比例，調到跟角色比例相符（佈置格子 5.28 之後改由底座決定大小，這個只給舊的自由擺放用）")]
        public float worldScale = 1f;

        [Header("佈置格子（設計文件 5.28 ③）")]
        [Tooltip("底座沿 i 方向（往右下）佔幾格＝寬")]
        public int footprintW = 1;
        [Tooltip("底座沿 j 方向（往左下）佔幾格＝深")]
        public int footprintD = 1;
        [Tooltip("掛在高處的東西（拍立得燈串）：不佔地上格子，掛點還沒做，佈置時先不出現")]
        public bool hangPoint;
    }
}
