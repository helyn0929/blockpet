using TMPro;
using UnityEngine;

namespace BlockPet.UI.Home
{
    /// <summary>
    /// 首頁 v9（設計文件 5.25）用的素材：底部三顆圓鈕、線條圖示、照片貼紙、寵物泡泡。
    /// 放在 Resources/UI/Home9Skin，執行時自己讀，不用在場景裡接線。
    /// 由 Editor 的 Home9SkinBuilder 自動從 Assets/Art/UI/Home9/Sprites 填好；9-slice 邊界見 docs/home9/home9_assets.json。
    /// 沒有素材時各畫面改用程式畫的圓角。
    /// </summary>
    public class Home9Skin : ScriptableObject
    {
        public const string ResourcePath = "UI/Home9Skin";

        public TMP_FontAsset font;

        [Header("狀態欄下的 Home、選單（沒有底）")]
        public Sprite iconHome, iconMenuChevron;

        [Header("底部相簿／相機／聊天")]
        public Sprite buttonRound, buttonCamera, iconAlbum, iconChat, badgeRed;

        [Header("照片貼紙")]
        public Sprite stickerFrame, stickerPhotoMask, stickerLatestFrame, stickerLatestMask, avatarRing, tagLatest, tagMore;

        [Header("寵物泡泡")]
        public Sprite petBubble;

        static Home9Skin _loaded;

        public static Home9Skin Load()
        {
            if (_loaded == null) _loaded = Resources.Load<Home9Skin>(ResourcePath);
            return _loaded;
        }
    }
}
