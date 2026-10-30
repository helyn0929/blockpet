using UnityEngine;

namespace BlockPet.UI.Album
{
    /// <summary>
    /// 照片檢視（設計文件 5.14 ⑥）用的素材。放在 Resources/UI/PhotoViewSkin，執行時自己讀，不用在場景裡接線。
    /// 由 Editor 的 PhotoViewSkinBuilder 自動從 Assets/Art/UI/PhotoView/Sprites 填好（素材重新匯入時也會更新）。
    /// </summary>
    public class PhotoViewSkin : ScriptableObject
    {
        public const string ResourcePath = "UI/PhotoViewSkin";

        public Sprite close;
        public Sprite polaroidFrame;
        public Sprite photoMask;
        public Sprite tape;
        public Sprite dot;
        public Sprite dotOn;
        public Sprite avatar;
        public Sprite inputBar;
        public Sprite chatInput;
        public Sprite sendOn;
        public Sprite sendOff;

        static PhotoViewSkin _loaded;

        public static PhotoViewSkin Load()
        {
            if (_loaded == null) _loaded = Resources.Load<PhotoViewSkin>(ResourcePath);
            return _loaded;
        }
    }
}
