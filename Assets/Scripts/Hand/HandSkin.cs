using TMPro;
using UnityEngine;

namespace BlockPet.Hand
{
    /// <summary>
    /// 手牌介面素材：設計稿首頁 v8 的 CSS 直接切出的 sprite（docs/hand/Tools/make_hand_sprites.py）＋字型。
    /// 由「BlockPet/Build Hand Skin」產生 Assets/Data/HandSkin.asset 並接到場景的 HandController。
    /// 沒有指定時 HandController 會改用程式產生的圓角（看起來接近但不完全一樣）。
    /// </summary>
    [CreateAssetMenu(fileName = "HandSkin", menuName = "BlockPet/Hand Skin")]
    public class HandSkin : ScriptableObject
    {
        [Header("Text")]
        public TMP_FontAsset font;
        [Tooltip("白字＋陰影（營地背景上的標籤）")]
        public Material labelShadowMaterial;

        [Header("手牌卡 92×124")]
        public Sprite card;
        [Tooltip("四邊外擴 24")] public Sprite cardShadow;
        public Sprite cardCamera;
        public Sprite iconCamera;
        public Sprite maskR6;
        public Sprite maskR10;
        public Sprite maskR3;

        [Header("舉起的照片")]
        public Sprite popup;
        [Tooltip("四邊外擴 48")] public Sprite popupShadow;
        public Sprite buttonBeige;
        [Tooltip("含下緣 3 立體，高 = 按鈕高 + 3")] public Sprite buttonYellow;

        [Header("出牌後：相簿／拍照／聊天")]
        [Tooltip("60×60 方塊，含陰影外擴 16")] public Sprite dockTile;
        [Tooltip("84×84 拍照鈕，含陰影外擴 24")] public Sprite dockCamera;
        public Sprite iconChat;
        [Tooltip("60×60 區域，外擴 8")] public Sprite albumMinis;

        [Header("營地最新照片卡")]
        public Sprite latestCard;
        [Tooltip("四邊外擴 24")] public Sprite latestBackShadow;
        [Tooltip("四邊外擴 32")] public Sprite latestGlowLo;
        [Tooltip("四邊外擴 32")] public Sprite latestGlowHi;
        [Tooltip("含下緣 2 立體")] public Sprite badgeNew;
        public Sprite iconEye;
        public Sprite circle;

        [Header("空狀態")]
        public Sprite glassPill;
        public Sprite pillYellow;
    }
}
