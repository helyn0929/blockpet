using System;
using System.IO;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace BlockPet.UI.Album
{
    /// <summary>
    /// 照片儲存到手機相簿、分享（照片檢視點拍立得的選單用）。
    /// 輸出的是原始照片，右下角加「BlockPet」浮水印（寬約照片 24%、離邊 3%）；營地裡所有照片都可以存和分享（2026-10-31 定案）。
    /// - 儲存：NativeGallery 存到相簿「BlockPet」，第一次會跟手機要「加入照片」權限。
    /// - 分享：iOS 打開系統分享面板（Assets/Plugins/iOS/BlockPetShare.mm）；Android 的圖片分享還沒做，先改成存到相簿。
    /// - Editor：沒有相簿和分享面板，改存到 persistentDataPath/Exports 方便檢查。
    /// 浮水印圖：Resources/UI/watermark_blockpet.bytes（docs/photoview/Tools/make_watermark.py 產生）。
    /// </summary>
    public static class PhotoExport
    {
        const string Album = "BlockPet";
        const float WatermarkWidth = 0.24f;
        const float WatermarkMargin = 0.03f;
        const int JpgQuality = 92;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void _BlockPet_ShareImage(string path);
#endif

        static Texture2D _watermark;

        /// <summary>存到手機相簿。done(成功, 給玩家看的訊息)。</summary>
        public static void Save(PhotoMeta meta, Action<bool, string> done)
        {
            var bytes = Render(meta);
            if (bytes == null) { done?.Invoke(false, "找不到這張照片"); return; }
            string name = FileName();
#if UNITY_EDITOR
            string path = WriteExport(bytes, name);
            Debug.Log("[PhotoExport] Editor 沒有相簿，已存到：" + path);
            done?.Invoke(true, "已存到相簿");
#else
            NativeGallery.SaveImageToGallery(bytes, Album, name, (ok, _) =>
                done?.Invoke(ok, ok ? "已存到相簿" : "沒有存成功，請到設定允許 BlockPet 加入照片"));
#endif
        }

        /// <summary>打開手機的分享面板。done(成功, 給玩家看的訊息；分享面板打開時是 null)。</summary>
        public static void Share(PhotoMeta meta, Action<bool, string> done)
        {
            var bytes = Render(meta);
            if (bytes == null) { done?.Invoke(false, "找不到這張照片"); return; }
#if UNITY_IOS && !UNITY_EDITOR
            string path = Path.Combine(Application.temporaryCachePath, FileName());
            File.WriteAllBytes(path, bytes);
            _BlockPet_ShareImage(path);
            done?.Invoke(true, null);
#elif UNITY_EDITOR
            string path = WriteExport(bytes, FileName());
            Debug.Log("[PhotoExport] Editor 沒有分享面板，已存到：" + path);
            done?.Invoke(true, "Editor 沒有分享面板，已存成檔案");
#else
            // Android 的圖片分享要另外設定 FileProvider，還沒做：先存到相簿
            NativeGallery.SaveImageToGallery(bytes, Album, FileName(), (ok, _) =>
                done?.Invoke(ok, ok ? "已存到相簿，可以從相簿分享" : "沒有存成功，請到設定允許 BlockPet 加入照片"));
#endif
        }

        /// <summary>讀原始照片、蓋上浮水印、轉成 JPG。照片不存在回傳 null。</summary>
        static byte[] Render(PhotoMeta meta)
        {
            var photo = SaveManager.Instance != null ? SaveManager.Instance.LoadPhoto(meta) : null;
            if (photo == null) return null;
            try
            {
                StampWatermark(photo);
                return photo.EncodeToJPG(JpgQuality);
            }
            finally { UnityEngine.Object.Destroy(photo); }
        }

        /// <summary>把浮水印疊到照片右下角（直接改這張貼圖；LoadPhoto 每次都是新的一張）。</summary>
        static void StampWatermark(Texture2D photo)
        {
            var wm = Watermark();
            if (wm == null) return;
            int pw = photo.width, ph = photo.height;
            int ww = Mathf.Max(1, Mathf.RoundToInt(pw * WatermarkWidth));
            int wh = Mathf.Max(1, Mathf.RoundToInt(ww * (float)wm.height / wm.width));
            int margin = Mathf.RoundToInt(Mathf.Min(pw, ph) * WatermarkMargin);
            int x0 = pw - ww - margin, y0 = margin; // 貼圖原點在左下
            if (x0 < 0 || wh + y0 > ph) return;

            var block = photo.GetPixels32(0);
            for (int y = 0; y < wh; y++)
            {
                float v = (y + 0.5f) / wh;
                for (int x = 0; x < ww; x++)
                {
                    Color s = wm.GetPixelBilinear((x + 0.5f) / ww, v);
                    if (s.a <= 0.003f) continue;
                    int i = (y0 + y) * pw + (x0 + x);
                    Color d = block[i];
                    block[i] = new Color(
                        s.r * s.a + d.r * (1f - s.a),
                        s.g * s.a + d.g * (1f - s.a),
                        s.b * s.a + d.b * (1f - s.a), 1f);
                }
            }
            photo.SetPixels32(block, 0);
            photo.Apply(false);
        }

        static Texture2D Watermark()
        {
            if (_watermark != null) return _watermark;
            var bytes = Resources.Load<TextAsset>("UI/watermark_blockpet");
            if (bytes == null) { Debug.LogWarning("[PhotoExport] 找不到浮水印 Resources/UI/watermark_blockpet"); return null; }
            _watermark = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            _watermark.LoadImage(bytes.bytes);
            return _watermark;
        }

        static string FileName() => $"BlockPet_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";

#if UNITY_EDITOR
        static string WriteExport(byte[] bytes, string name)
        {
            string dir = Path.Combine(Application.persistentDataPath, "Exports");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }
#endif
    }
}
