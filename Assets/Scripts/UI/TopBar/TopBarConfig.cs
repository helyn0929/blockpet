using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace BlockPet.UI.TopBar
{
    /// <summary>
    /// 首頁頂部狀態欄的所有數值（版面、顏色、字級、動畫時間、素材）。
    /// 由 Editor 選單「BlockPet/Build TopBar」從 Assets/Data/blockpet_topbar_config.json 產生／更新；
    /// JSON 沒有的數值（README 的文字規格）以這裡的預設值為準。座標單位皆為設計稿 @1x px。
    /// </summary>
    [CreateAssetMenu(fileName = "TopBarConfig", menuName = "BlockPet/TopBar Config")]
    public class TopBarConfig : ScriptableObject
    {
        [Serializable]
        public class CategoryColor
        {
            public string category;
            public Color color;
        }

        [Serializable]
        public class ChipColor
        {
            public string category;
            public Color bg;
            public Color fg;
        }

        [Header("Canvas")]
        public Vector2 referenceResolution = new Vector2(390, 772);
        [Range(0f, 1f)] public float matchWidthOrHeight = 0.5f;

        [Header("Text")]
        public TMP_FontAsset font;
        [Tooltip("選單標籤用：白字＋陰影（TMP Underlay）")]
        public Material labelShadowMaterial;
        public Color textColor = Hex("#3A2E22");
        public float nameSize = 13;
        public float lvSize = 9;
        public float wantSize = 11;
        public float evoTextSize = 11;
        public float coinSize = 13;
        public float menuLabelSize = 11;

        [Header("Panel（相對畫面）")]
        public Vector2 panelScreenPos = new Vector2(10, 10);
        public Vector2 panelSize = new Vector2(370, 58);
        [Tooltip("元件間距（頭像、中欄、金幣按鈕之間）")]
        public float panelGap = 8;
        [Tooltip("陰影比底板四邊各大多少")]
        public float panelShadowOutset = 24;

        [Header("Avatar（相對狀態欄左上）")]
        public Rect avatar = new Rect(7, 7, 44, 44);
        [Tooltip("寵物圖，相對頭像左上")]
        public Rect avatarImage = new Rect(2, 5, 40, 34);
        public float lvBadgeHeight = 15;
        [Tooltip("Lv 徽章寬 = 字寬 + 這個值")]
        public float lvBadgeExtraWidth = 13;
        [Tooltip("Lv 徽章下緣比頭像低多少")]
        public float lvBadgeBelowAvatar = 5;
        public Color lvTextColor = Hex("#FFF8EC");

        [Header("Rows（相對狀態欄左上）")]
        public float rowX = 59;
        public float row1Y = 9.5f;
        public float row1Height = 17;
        public float row2Y = 32.5f;
        public float row2Height = 16;
        public float rowGap = 6;

        [Header("Hearts")]
        public Vector2 heartSize = new Vector2(15, 14);
        public float heartGap = 1;
        public int maxHearts = 5;
        [Tooltip("愛心 ≤ 這個值時跳動")]
        public float lowHeartsThreshold = 1;
        public float lowHeartsPulseScale = 1.18f;
        public float lowHeartsPulsePeriod = 1.8f;

        [Header("Want chip")]
        public float wantHeight = 17;
        public float wantPaddingX = 8;
        public string wantPrefix = "想吃 ";
        public List<ChipColor> wantColors = new List<ChipColor>();

        [Header("Evo bar")]
        public float evoHeight = 8;
        public float evoSegmentGap = 1;
        public float evoTransitionSec = 0.6f;
        [Tooltip("色段固定順序與顏色")]
        public List<CategoryColor> segments = new List<CategoryColor>();
        [Tooltip("{energy} 會換成總能量")]
        public string evoTextFormat = "進化 {energy}%";
        public string readyText = "可進化！";
        public Color readyTextColor = Hex("#C2621A");
        [Tooltip("滿條金框比條四邊各大多少")]
        public float readyGlowOutset = 14;

        [Header("Coin button（相對按鈕左上）")]
        [Tooltip("按鈕右緣到狀態欄右緣")]
        public float coinRight = 9;
        public float coinY = 14;
        public float coinHeight = 30;
        public Rect coinDot = new Rect(7, 8, 14, 14);
        [Tooltip("數字左緣（相對按鈕）、上緣、高")]
        public Vector3 coinNumber = new Vector3(25, 5.5f, 19);
        public float coinNumberToPlus = 4;
        public Vector2 coinPlusSize = new Vector2(22, 24);
        public float coinPlusRight = 4;
        public float coinPlusTop = 4;

        [Header("Dead（補充）")]
        public Color deadSegmentColor = Hex("#C9BFAF");
        public string deadChipText = "點我復活";
        public Color deadChipBg = Hex("#E86B4F");
        public Color deadChipFg = Color.white;
        [Range(0f, 1f)] public float deadAvatarGray = 0.7f;

        [Header("Menu toggle（相對畫面右上）")]
        public float menuRightMargin = 10;
        public float menuY = 80;
        public Vector2 menuSize = new Vector2(46, 32);
        [Tooltip("選單鈕素材含陰影，四邊外擴")]
        public float menuSpriteOutset = 12;
        public float chevronSize = 16;
        public float chevronRotateSec = 0.25f;
        public float menuTile = 46;
        public float menuGap = 8;
        public float menuIconSize = 22;
        public float menuItemWidth = 52;
        public float menuLabelGap = 2;
        public float menuPopSec = 0.22f;
        public List<string> menuOrder = new List<string> { "佈置", "旅行", "圖鑑", "設定" };

        [Header("Home 鍵（相對畫面左上；設計文件 5.7）")]
        public float homeLeftMargin = 10;
        public float homeY = 80;
        public Vector2 homeSize = new Vector2(46, 32);
        public float homeIconSize = 20;

        [Header("今日題目（第 9 步使用；設計文件 5.7：避開 Home 鍵）")]
        [Tooltip("今日題目直排格子的起始 y（相對畫面上緣）")]
        public float todayThemeStartY = 124;

        [Header("佈置模式")]
        [Tooltip("佈置模式要隱藏哪些介面（尚未定案，預設全部不隱藏）")]
        public DecorModeSettings decorMode;

        [Header("Sprites（Assets/Art/UI/TopBar/Sprites）")]
        public Sprite panel;
        public Sprite panelShadow;
        public Sprite avatarBg;
        public Sprite avatarMask;
        public Sprite lvBadge;
        public Sprite heartFull;
        public Sprite heartHalf;
        public Sprite heartEmpty;
        public Sprite pill17;
        public Sprite evoTrack;
        public Sprite evoMask;
        public Sprite evoSegment;
        public Sprite evoReadyGlow;
        public Sprite coinPill;
        public Sprite coinDotSprite;
        public Sprite coinPlus;
        public Sprite menuClosed;
        public Sprite menuOpen;
        public Sprite chevron;
        public Sprite menuTileSprite;
        public Sprite iconTravel;
        public Sprite iconDex;
        public Sprite iconSettings;
        [Tooltip("Icons/icon_bed@3x.png（選單「佈置」）")]
        public Sprite iconDecor;
        [Tooltip("Icons/icon_home@3x.png（Home 鍵）")]
        public Sprite iconHome;

        public Color SegmentColor(string category)
        {
            foreach (var s in segments) if (s.category == category) return s.color;
            return Color.gray;
        }

        public ChipColor WantColor(string category)
        {
            foreach (var c in wantColors) if (c.category == category) return c;
            return wantColors.Count > 0 ? wantColors[0] : new ChipColor { category = category, bg = Color.white, fg = textColor };
        }

        public Sprite MenuIcon(string label)
        {
            switch (label)
            {
                case "佈置": return iconDecor;
                case "旅行": return iconTravel;
                case "圖鑑": return iconDex;
                case "設定": return iconSettings;
                default: return null;
            }
        }

        public static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        }
    }
}
