using TMPro;
using UnityEngine;

namespace BlockPet.UI.Travel
{
    /// <summary>
    /// 旅行（設計文件 5.21）用的素材。放在 Resources/UI/TravelSkin，執行時自己讀，不用在場景裡接線。
    /// 由 Editor 的 TravelSkinBuilder 自動從 Assets/Art/UI/Travel/Sprites 填好（9-slice 見 docs/travel/travel_assets.json）。
    /// </summary>
    public class TravelSkin : ScriptableObject
    {
        public const string ResourcePath = "UI/TravelSkin";

        [Header("字型與圖")]
        public TMP_FontAsset font;
        [Tooltip("大頭針裡的寵物（先用營地寵物 Zapup）")]
        public Sprite pet;
        [Tooltip("大頭針裡的營地場景")]
        public Texture campBackground;

        [Header("地圖")]
        public Sprite seaWave, landDisc;
        public Sprite pinFrame, pinMask, homeFrame, homeMask, labelBg, homeLabel;
        public Sprite back;
        public Sprite stampBadge, propBench, propSign, propFlowerbed, propLamp;

        [Header("頂部、底部")]
        public Sprite headerBg, inviteButton, redBadge, addButton, passportButton, statusCard;

        [Header("面板")]
        public Sprite paperPlane;

        [Header("旅行流程")]
        public Sprite backpack, tagPill, glassBar, backSmall, tagTraveling, bubbleLeft, bubbleRight;
        public Sprite awaySign, popupCard, avatarBg, infoCell, photoMask;

        [Header("護照")]
        public Sprite passportBack, passportPage, passportFold, stampRing, stampEmpty, timesBadge;

        static TravelSkin _loaded;

        public static TravelSkin Load()
        {
            if (_loaded == null) _loaded = Resources.Load<TravelSkin>(ResourcePath);
            return _loaded;
        }
    }
}
