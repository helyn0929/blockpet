using System;
using System.IO;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace BlockPet.UI.Album
{
    /// <summary>
    /// 圖片存到手機相簿、打開分享選單（拍立得分割畫面 5.26 ⑥：存的是整張拍立得，由 PolaroidExport 畫好再交給這裡）。
    /// - 儲存：NativeGallery 存到相簿「BlockPet」，第一次會跟手機要「加入照片」權限。
    /// - 分享：iOS 打開系統分享面板（Assets/Plugins/iOS/BlockPetShare.mm）；Android 的圖片分享還沒做，先改成存到相簿。
    /// - Editor：沒有相簿和分享面板，改存到 persistentDataPath/Exports 方便檢查。
    /// （5.14 ⑥ 舊照片檢視的「原始照片＋浮水印」2026-11-07 隨舊頁面拿掉。）
    /// </summary>
    public static class PhotoExport
    {
        const string Album = "BlockPet";

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void _BlockPet_ShareImage(string path);
#endif

        /// <summary>存到手機相簿。done(成功, 給玩家看的訊息)。</summary>
        public static void SaveImage(byte[] jpg, Action<bool, string> done)
        {
            if (jpg == null) { done?.Invoke(false, "找不到這張照片"); return; }
            string name = FileName();
#if UNITY_EDITOR
            string path = WriteExport(jpg, name);
            Debug.Log("[PhotoExport] Editor 沒有相簿，已存到：" + path);
            done?.Invoke(true, "已存到手機相簿");
#else
            NativeGallery.SaveImageToGallery(jpg, Album, name, (ok, _) =>
                done?.Invoke(ok, ok ? "已存到手機相簿" : "沒有存成功，請到設定允許 BlockPet 加入照片"));
#endif
        }

        /// <summary>打開手機的分享選單。done(成功, 給玩家看的訊息；分享選單打開時是 null)。</summary>
        public static void ShareImage(byte[] jpg, Action<bool, string> done)
        {
            if (jpg == null) { done?.Invoke(false, "找不到這張照片"); return; }
#if UNITY_IOS && !UNITY_EDITOR
            string path = Path.Combine(Application.temporaryCachePath, FileName());
            File.WriteAllBytes(path, jpg);
            _BlockPet_ShareImage(path);
            done?.Invoke(true, null);
#elif UNITY_EDITOR
            string path = WriteExport(jpg, FileName());
            Debug.Log("[PhotoExport] Editor 沒有分享選單，已存到：" + path);
            done?.Invoke(true, "Editor 沒有分享選單，已存成檔案");
#else
            // Android 的圖片分享要另外設定 FileProvider，還沒做：先存到相簿
            NativeGallery.SaveImageToGallery(jpg, Album, FileName(), (ok, _) =>
                done?.Invoke(ok, ok ? "已存到手機相簿，可以從相簿分享" : "沒有存成功，請到設定允許 BlockPet 加入照片"));
#endif
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
