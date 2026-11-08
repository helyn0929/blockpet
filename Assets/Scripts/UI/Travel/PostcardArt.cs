using BlockPet.Hand;
using BlockPet.Polaroid;
using BlockPet.Travel;
using BlockPet.UI.Polaroid;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Travel
{
    /// <summary>
    /// 寄明信片共用的圖（設計文件 5.21）：
    /// - 風景明信片：對方 24 小時沒回時寵物帶回的那張——對方營地＋寵物的畫面（自動產生，不用對方的私人照片），做成白邊拍立得。
    /// - 明信片圖（整張拍立得 JPG）裡照片那一塊的 uv（小拍立得、帶回來的明信片只露照片）。
    /// - 好友營地的上色（地圖、面板、風景明信片同一套；設計稿用 hue-rotate，先用上色近似）。
    /// </summary>
    public static class PostcardArt
    {
        public static readonly Color[] FriendPetTint = { new Color(0.8f, 0.8f, 0.8f), new Color(1f, 0.82f, 0.72f), new Color(0.7f, 0.8f, 1f), new Color(0.85f, 1f, 0.78f), new Color(0.78f, 0.95f, 1f) };
        public static readonly Color[] FriendSceneTint = { new Color(0.95f, 0.95f, 0.9f), new Color(1f, 0.95f, 0.85f), new Color(0.85f, 0.95f, 1f), new Color(1f, 1f, 0.92f), new Color(0.9f, 1f, 0.95f) };
        public static readonly Color PostmarkRed = new Color(201f / 255f, 60f / 255f, 50f / 255f, 0.9f);

        public static Color SceneTint(int slot) => FriendSceneTint[Mathf.Abs(slot) % FriendSceneTint.Length];
        public static Color PetTint(int slot) => FriendPetTint[Mathf.Abs(slot) % FriendPetTint.Length];

        /// <summary>明信片 JPG（整張拍立得 354 × 431）裡照片那一塊（318 正方形）的 uv。</summary>
        public static Rect PhotoUv()
        {
            float w = PolaroidRules.CardW, h = PolaroidRules.CardH;
            return new Rect(PolaroidRules.PhotoX / w, 1f - (PolaroidRules.PhotoY + PolaroidRules.PhotoSize) / h,
                            PolaroidRules.PhotoSize / w, PolaroidRules.PhotoSize / h);
        }

        /// <summary>風景明信片：白邊拍立得（同拍立得的大小）、照片區是對方營地＋寵物、下面寫「○○的風景」。</summary>
        public static byte[] RenderScenery(FirebaseManager.SentPostcard sent)
        {
            var skin = TravelSkin.Load();
            int k = PolaroidScreen.PostcardScale;
            int w = Mathf.RoundToInt(PolaroidRules.CardW * k), h = Mathf.RoundToInt(PolaroidRules.CardH * k);
            int slot = sent != null ? Mathf.Max(0, sent.toSlot) : 0;
            string name = sent != null && !string.IsNullOrEmpty(sent.toName) ? sent.toName : "好友營地";
            return PolaroidExport.RenderUI(w, h, holder =>
            {
                var card = PUI.Stretch(holder, "Card");
                card.gameObject.AddComponent<Image>().color = Color.white;
                var photo = PUI.Place(card, "Photo", PolaroidRules.PhotoX * k, PolaroidRules.PhotoY * k, PolaroidRules.PhotoSize * k, PolaroidRules.PhotoSize * k);
                photo.gameObject.AddComponent<RectMask2D>();
                photo.gameObject.AddComponent<Image>().color = PUI.Hex(0x8FA868);
                if (skin != null && skin.campBackground != null)
                {
                    var raw = PUI.Stretch(photo, "Camp").gameObject.AddComponent<RawImage>();
                    raw.texture = skin.campBackground;
                    raw.color = SceneTint(slot);
                    // 營地底圖中間偏下那一塊（正方形）
                    float aspect = (float)skin.campBackground.width / Mathf.Max(1, skin.campBackground.height);
                    float uh = Mathf.Min(1f, aspect);
                    raw.uvRect = new Rect(0f, 0.28f, 1f, uh);
                }
                if (skin != null && skin.pet != null)
                {
                    float s = PolaroidRules.PhotoSize * k;
                    var pet = PUI.Place(photo, "Pet", s * 0.25f, s * 0.42f, s * 0.5f, s * 0.45f).gameObject.AddComponent<Image>();
                    pet.sprite = skin.pet;
                    pet.preserveAspect = true;
                    pet.color = PetTint(slot);
                }
                var font = skin != null ? skin.font : null;
                PUI.Label(font, card, 0, (PolaroidRules.PhotoY + PolaroidRules.PhotoSize + 22f) * k, PolaroidRules.CardW * k, 40f * k,
                          $"{name}的風景", 20f * k, PUI.Hex(0x3A2E22), TextAlignmentOptions.Center);
            }, 85);
        }

        /// <summary>郵戳（pc_postmark 圓框，上：營地名、下：日期；轉 −14°）。回傳整個郵戳（pivot 中心）。</summary>
        public static RectTransform Postmark(RectTransform parent, PostcardSkin pc, TMP_FontAsset font, float x, float y, float size, string camp, string date)
        {
            var rt = PUI.Place(parent, "Postmark", x, y, size, size);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x + size / 2f, -(y + size / 2f));
            rt.localRotation = Quaternion.Euler(0f, 0f, 14f); // CSS rotate(-14deg)：逆時針
            if (pc != null && pc.postmark != null) { var img = rt.gameObject.AddComponent<Image>(); img.sprite = pc.postmark; img.raycastTarget = false; }
            else UIShapes.AddRing(rt.gameObject, PostmarkRed, size / 2f).raycastTarget = false;
            float k = size / 92f;
            string shortName = camp != null && camp.Length > 6 ? camp.Substring(0, 6) : camp;
            PUI.Label(font, rt, 0, 22f * k, size, 18f * k, shortName ?? "", 11f * k, PostmarkRed, TextAlignmentOptions.Center);
            PUI.Label(font, rt, 0, 52f * k, size, 16f * k, date ?? "", 10f * k, PostmarkRed, TextAlignmentOptions.Center);
            return rt;
        }
    }
}
