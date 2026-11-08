using System.Collections.Generic;
using BlockPet.Polaroid;
using NUnit.Framework;
using UnityEngine;

namespace BlockPet.Tests
{
    /// <summary>拍立得分割畫面的規則（設計文件 5.26 ③④⑤；docs/polaroid/polaroid_spec.json）。</summary>
    public class PolaroidRulesTests
    {
        static PolaroidSticker S(string by, string kind = "star") => new PolaroidSticker { id = by + kind + Random.value, by = by, kind = kind };

        [Test]
        public void 每人每張最多3個貼紙_只算自己的()
        {
            var list = new List<PolaroidSticker> { S("me"), S("me"), S("amy"), S("amy"), S("amy") };
            Assert.AreEqual(1, PolaroidRules.StickersLeft(list, "me"));
            Assert.AreEqual(0, PolaroidRules.StickersLeft(list, "amy"));
            Assert.AreEqual(3, PolaroidRules.StickersLeft(list, "bob"));
            list.Add(S("me"));
            Assert.AreEqual(0, PolaroidRules.StickersLeft(list, "me"));
            Assert.IsFalse(PolaroidRules.CanAddSticker(list, "me"));
            Assert.IsTrue(PolaroidRules.CanAddSticker(list, "bob"));
            Assert.IsFalse(PolaroidRules.CanAddSticker(list, ""), "沒登入不能貼");
        }

        [Test]
        public void 畫框濾鏡標題只有拍照的人能改()
        {
            Assert.IsTrue(PolaroidRules.CanStyle("me", "me"));
            Assert.IsFalse(PolaroidRules.CanStyle("amy", "me"));
            Assert.IsFalse(PolaroidRules.CanStyle(null, "me"), "舊照片沒有拍照的人：誰都不能改");
            Assert.IsFalse(PolaroidRules.CanStyle("me", null));
        }

        [Test]
        public void 自己的貼紙才能拖和撕()
        {
            var mine = S("me");
            var other = S("amy");
            Assert.IsTrue(PolaroidRules.CanEditSticker(mine, "me"));
            Assert.IsFalse(PolaroidRules.CanEditSticker(other, "me"));
            Assert.IsFalse(PolaroidRules.CanEditSticker(mine, null));
        }

        [Test]
        public void 貼上位置依序換_左右各轉8度()
        {
            var a = PolaroidRules.SpawnSpot(0);
            var b = PolaroidRules.SpawnSpot(1);
            var c = PolaroidRules.SpawnSpot(2);
            Assert.AreEqual(new Vector2(196, 30), a.pos);
            Assert.AreEqual(new Vector2(40, 120), b.pos);
            Assert.AreEqual(new Vector2(150, 170), c.pos);
            Assert.AreEqual(8f, a.rot);
            Assert.AreEqual(-8f, b.rot);
            Assert.AreEqual(8f, c.rot);
            Assert.AreEqual(a.pos, PolaroidRules.SpawnSpot(3).pos, "超過三個從頭輪");
        }

        [Test]
        public void 貼紙大小_寵物92_手畫66()
        {
            Assert.AreEqual(92f, PolaroidRules.StickerSize("pet:happy"));
            Assert.AreEqual(66f, PolaroidRules.StickerSize("star"));
            Assert.IsTrue(PolaroidRules.IsPetSticker("pet:rainbow"));
            Assert.IsFalse(PolaroidRules.IsPetSticker("paw"));
            Assert.AreEqual(12, PolaroidRules.Tray.Length);
        }

        [Test]
        public void 標題最多16字_去掉前後空白和換行()
        {
            Assert.AreEqual("今天的拉麵", PolaroidRules.CleanCaption("  今天的拉麵 \n"));
            Assert.AreEqual(16, PolaroidRules.CleanCaption(new string('好', 30)).Length);
            Assert.AreEqual("一二", PolaroidRules.CleanCaption("一\n二"));
            Assert.AreEqual("", PolaroidRules.CleanCaption(null));
        }

        [Test]
        public void 畫框10種_濾鏡3種_預設不用濾鏡()
        {
            Assert.AreEqual(10, PolaroidRules.Frames.Length);
            Assert.AreEqual(3, PolaroidRules.Filters.Length);
            CollectionAssert.AreEqual(new[] { "leica", "fuji", "ccd" }, System.Array.ConvertAll(PolaroidRules.Filters, f => f.key));
            Assert.AreEqual("white", PolaroidRules.Frame("??").key);
            Assert.AreEqual("黑", PolaroidRules.Frame("black").name);
            Assert.AreEqual("gingham", PolaroidRules.Frame("gingham").tile);
            Assert.AreEqual("", PolaroidRules.DefaultFilter);
            Assert.IsFalse(PolaroidRules.HasFilter(null));
            Assert.IsFalse(PolaroidRules.HasFilter("insta"), "第一版的濾鏡已經拿掉，當作沒有濾鏡");
            Assert.IsTrue(PolaroidRules.HasFilter("ccd"));
        }

        [Test]
        public void 再點一次選到的濾鏡就取消()
        {
            Assert.AreEqual("leica", PolaroidRules.ToggleFilter("", "leica"));
            Assert.AreEqual("fuji", PolaroidRules.ToggleFilter("leica", "fuji"));
            Assert.AreEqual("", PolaroidRules.ToggleFilter("fuji", "fuji"));
        }

        [Test]
        public void Y2K日期戳_年月日兩位數()
        {
            Assert.AreEqual("'26 11 08", PolaroidRules.DateStamp(new System.DateTime(2026, 11, 8, 21, 5, 0)));
            Assert.IsTrue(PolaroidRules.Filter("ccd").dateStamp);
            Assert.IsFalse(PolaroidRules.Filter("leica").dateStamp);
            Assert.IsTrue(PolaroidRules.Filter("leica").vignette > 0f, "萊卡有暗角");
        }

        [Test]
        public void 照片鍵_去掉Firebase不能用的字()
        {
            string k = PolaroidRules.PhotoKey("IMG_0012.jpg");
            Assert.IsFalse(k.Contains("."));
            Assert.AreEqual(k, PolaroidRules.PhotoKey("IMG_0012.jpg"));
            Assert.AreNotEqual(k, PolaroidRules.PhotoKey("IMG_0012_jpg"), "不同檔名不能撞在一起");
            foreach (char c in PolaroidRules.PhotoKey("a#b$c[d]e/f.png")) Assert.IsFalse("#$[]/.".Contains(c.ToString()));
        }

        [Test]
        public void 濾鏡_沒有濾鏡不變_萊卡四角變暗_Y2K變亮()
        {
            var gray = new Color32(128, 128, 128, 255);
            var px = new Color32[9 * 9];
            for (int i = 0; i < px.Length; i++) px[i] = gray;

            var none = (Color32[])px.Clone();
            PolaroidFilters.Apply(none, PolaroidRules.Filter(""), 9, 9);
            Assert.AreEqual(gray, none[0]);

            var leica = (Color32[])px.Clone();
            PolaroidFilters.Apply(leica, PolaroidRules.Filter("leica"), 9, 9);
            Assert.Less(leica[0].r, leica[4 * 9 + 4].r, "暗角：角落比中間暗");

            var ccd = (Color32[])px.Clone();
            PolaroidFilters.Apply(ccd, PolaroidRules.Filter("ccd"), 9, 9);
            Assert.Greater(ccd[40].r + ccd[40].g + ccd[40].b, 128 * 3, "Y2K 比較亮");
            Assert.Greater(ccd[40].b, ccd[40].r, "Y2K 偏藍");
            Assert.AreEqual(255, ccd[40].a);
        }
    }
}
