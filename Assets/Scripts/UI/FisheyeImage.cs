using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI
{
    /// <summary>
    /// 在 RawImage 上套魚眼（設計文件 5.27 ③；shader Resources/Shaders/UIFisheye）：相機預覽即時套用；
    /// 照片存的是原圖＋「fisheye」標記（PhotoMeta.fisheye），手牌以外的照片顯示處（照片貼紙、相簿、拍立得）用這個再套一次。
    /// 用法：FisheyeImage.Set(rawImage, on)。uvRect（縮放、裁切）或大小變了會自動更新參數。
    /// 2026-11-08 使用者改成全片幅魚眼（像 Sony FE 8-14mm）：畫面填滿、沒有圓形暗角，只是中間稍微放大、邊緣壓縮。
    /// 11-08 再改（臉不能變形）：中間一個圓只等比放大、不拉歪，彎曲集中在外圈；三個數值在這裡調（比較圖 B）。
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class FisheyeImage : MonoBehaviour
    {
        /// <summary>中間不變形的圓：短邊一半的 60%。</summary>
        public const float SafeRadius = 0.6f;
        /// <summary>中間等比放大的倍率。</summary>
        public const float CenterZoom = 1.4f;
        /// <summary>外圈彎曲的集中程度（越大越集中在邊緣、邊緣越彎）。</summary>
        public const float EdgePower = 3f;
        public const string PrefsCameraFisheye = "BlockPet.Camera.Fisheye";

        static Shader _shader;
        RawImage _img;
        Material _mat;
        Rect _uv;
        float _aspect = -1f;

        static Shader Shader
        {
            get
            {
                if (_shader == null) _shader = Resources.Load<Shader>("Shaders/UIFisheye");
                return _shader;
            }
        }

        /// <summary>開／關某張 RawImage 的魚眼。</summary>
        public static void Set(RawImage img, bool on)
        {
            if (img == null) return;
            var f = img.GetComponent<FisheyeImage>();
            if (!on)
            {
                if (f != null) f.Off();
                return;
            }
            if (Shader == null) { Debug.LogWarning("[Fisheye] 找不到 Resources/Shaders/UIFisheye"); return; }
            if (f == null) f = img.gameObject.AddComponent<FisheyeImage>();
            f.enabled = true;
            f._img = img;
            f.Refresh(true);
        }

        void LateUpdate() => Refresh(false);

        void Refresh(bool force)
        {
            if (_img == null) _img = GetComponent<RawImage>();
            var r = _img.rectTransform.rect;
            float aspect = r.height > 0.01f ? r.width / r.height : 1f;
            if (!force && _mat != null && _uv == _img.uvRect && Mathf.Abs(aspect - _aspect) < 0.001f) return;
            _uv = _img.uvRect;
            _aspect = aspect;
            // Mask 底下的圖會用 material 的複本（stencil），改參數不會跟著變：參數變了就換一個新的 material
            var old = _mat;
            _mat = new Material(Shader) { hideFlags = HideFlags.DontSave };
            _mat.SetFloat("_SafeRadius", SafeRadius);
            _mat.SetFloat("_CenterZoom", CenterZoom);
            _mat.SetFloat("_EdgePower", EdgePower);
            _mat.SetVector("_UVRect", new Vector4(_uv.x, _uv.y, _uv.width, _uv.height));
            _mat.SetFloat("_Aspect", aspect);
            _img.material = _mat;
            Kill(old);
        }

        void Off()
        {
            if (_img == null) _img = GetComponent<RawImage>();
            if (_img != null) _img.material = null;
            Kill(_mat);
            _mat = null;
            enabled = false;
        }

        void OnDestroy() => Kill(_mat);

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
