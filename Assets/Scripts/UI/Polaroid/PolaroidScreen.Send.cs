using System;
using System.Collections;
using System.Collections.Generic;
using BlockPet.Hand;
using BlockPet.Polaroid;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Polaroid
{
    /// <summary>
    /// 拍立得畫面的「寄明信片」模式（設計文件 5.21 ④-3）：只看這一張（不能左右換），留言框換成黃色「寄出」；
    /// 照樣可以貼貼紙、換畫框、套濾鏡、寫標題（存回營地的拍立得）。按「寄出」時把當下那一版畫成 JPG 交給寄明信片。
    /// 明信片地圖在 sortingOrder 38，所以這個模式把拍立得畫面提到 39。
    /// </summary>
    public partial class PolaroidScreen
    {
        /// <summary>寄出的明信片解析度：@2x（708 × 862），存進資料庫比較小。</summary>
        public const int PostcardScale = 2;
        const int PostcardJpgQuality = 85;
        const int SendSortingOrder = 39;

        Action<byte[], string> _onSend;
        RectTransform _sendBar;
        TMP_Text _sendLabel;
        bool _sendBusy;

        /// <summary>打開這張照片的拍立得來做明信片；按「寄出」→ onSend(JPG, 標題)，然後關掉。</summary>
        public static void OpenForSend(PhotoMeta photo, string buttonText, Action<byte[], string> onSend)
        {
            if (photo == null) return;
            var s = Instance();
            s._onSend = onSend;
            s.Show(new List<PhotoMeta> { photo }, 0);
            s.BeginSendMode(string.IsNullOrEmpty(buttonText) ? "寄出" : buttonText);
        }

        void BeginSendMode(string text)
        {
            if (_canvas != null) _canvas.sortingOrder = SendSortingOrder;
            _inputBar.gameObject.SetActive(false);
            if (_sendBar == null)
            {
                float bw = DesignW - 28f;
                _sendBar = PUI.Place(_design, "SendPostcard", 14, InputY - 6f, bw, 53);
                UIShapes.AddRounded(PUI.Place(_sendBar, "Edge", 0, 3, bw, 50).gameObject, PUI.Hex(0xD9A92C), 15).raycastTarget = false;
                var fill = UIShapes.AddRounded(PUI.Place(_sendBar, "Fill", 0, 0, bw, 50).gameObject, Yellow, 15);
                _sendLabel = PUI.Label(_font, _sendBar, 0, 0, bw, 50, "", 16, Ink, TextAlignmentOptions.Center);
                PUI.MakeButton(_sendBar.gameObject, fill, () => { if (!_sendBusy) StartCoroutine(SendRoutine()); });
            }
            _sendLabel.text = text;
            _sendBar.gameObject.SetActive(true);
            _sendBusy = false;
        }

        /// <summary>Close 時呼叫：回到一般的拍立得畫面。</summary>
        void EndSendMode()
        {
            _onSend = null;
            _sendBusy = false;
            if (_canvas != null) _canvas.sortingOrder = 37;
            if (_sendBar != null) _sendBar.gameObject.SetActive(false);
            if (_inputBar != null) _inputBar.gameObject.SetActive(true);
        }

        IEnumerator SendRoutine()
        {
            if (Current == null) yield break;
            if (_filtered == null) { ShowToast("照片還在讀取，等一下再試", false); yield break; }
            _sendBusy = true;
            Deselect();
            _sendLabel.text = "做成明信片…";
            yield return null;
            byte[] jpg = null;
            try { jpg = RenderNow(); }
            catch (Exception e) { Debug.LogWarning("[Polaroid] 畫明信片失敗：" + e.Message); }
            var cb = _onSend;
            string caption = _style != null ? _style.caption : "";
            if (jpg == null) { _sendBusy = false; ShowToast("做明信片失敗，請再試一次", false); yield break; }
            Close();
            cb?.Invoke(jpg, caption);
        }

        byte[] RenderNow() => PolaroidExport.Render(_filtered, _style, _stickers, _skin, _font, PhotoTime(Current),
                                                     Current.fisheye, PostcardScale);

        /// <summary>
        /// 不打開畫面，直接把一張照片（含營地裡的貼紙、畫框、濾鏡、標題）畫成明信片 JPG（已經裝飾過的拍立得「直接用」）。讀不到照片回傳 null。
        /// </summary>
        public static byte[] RenderPostcard(PhotoMeta photo, PolaroidStyle style, IList<PolaroidSticker> stickers)
        {
            if (photo == null) return null;
            var sq = PolaroidPhoto.LoadSquare(photo, PolaroidPhoto.PhotoPx);
            if (sq == null) return null;
            var filtered = PolaroidPhoto.Filtered(sq, style?.filter ?? PolaroidRules.DefaultFilter);
            try
            {
                var skin = PolaroidSkin.Load();
                DateTime.TryParse(photo.timestamp, out var t);
                return PolaroidExport.Render(filtered, style ?? new PolaroidStyle(), stickers ?? new List<PolaroidSticker>(),
                                             skin, skin != null ? skin.font : null, t, photo.fisheye, PostcardScale);
            }
            finally
            {
                PUI.Kill(sq);
                if (filtered != sq) PUI.Kill(filtered);
            }
        }
    }
}
