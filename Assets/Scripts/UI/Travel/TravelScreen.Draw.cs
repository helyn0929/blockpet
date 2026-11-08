using System;
using System.Collections.Generic;
using BlockPet.Hand;
using BlockPet.Travel;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Travel
{
    /// <summary>
    /// 抽好友（設計文件 5.21 ③；postcard_spec.json draw_card；元件 Postcard 的 draw／waiting／incoming）：
    /// - 地圖右上黃色「抽好友」＋小膠囊「今天免費」／金幣價格；有營地想加我們時紅點。
    /// - 抽到 → 卡片翻開（0.7 秒）：營地場景＋寵物、左上國家、營地名、寵物・品種・成員數、最常拍的類別、「你們都愛拍○○！」；「略過」／「加好友」。
    /// - 按「加好友」→「等對方確認…」；對方也按了就成為好友（App 內通知，推播之後再做）。
    /// - 對方那邊：紅點 → 「有營地想跟你們當好友！」同樣的卡片。
    /// - 好友已滿：「好友已滿 N 個，解鎖更多位置？」。卡片下方都有「封鎖・檢舉」。
    /// </summary>
    public partial class TravelScreen
    {
        enum DrawView { Draw, Waiting, Incoming }

        static readonly Color ChipInk = Hex(0x6B4A12);
        static readonly Color PassportInk = Hex(0x2E4686);

        RectTransform _drawLayer, _drawCard, _spinner, _confirmLayer;
        float _drawFlipT = 1f;
        bool _drawing;
        DrawView _drawView;
        CampProfile _drawProfile;
        FirebaseManager.MatchRequest _drawRequest;
        readonly HashSet<string> _waitingFor = new HashSet<string>();
        HashSet<string> _knownFriends;

        // ─── 頂部按鈕 ──────────────────────────────────────────────────

        void BuildDrawButton(int invites)
        {
            var fb = FirebaseManager.Instance;
            int cost = !_capture && fb != null ? fb.DrawCostNow : 0;
            string chip = cost > 0 ? $"{cost} 金幣" : "今天免費";
            const string text = "抽好友";
            var tl = Label(_header, 0, 0, 10, 34, text, 13, Ink);
            var cl = Label(_header, 0, 0, 10, 16, chip, 10, ChipInk);
            float tw = Mathf.Ceil(TextWidth(tl, text)), cw = Mathf.Ceil(TextWidth(cl, chip)) + 12f;
            float bw = 12f + 16f + 5f + tw + 7f + cw + 7f;
            var btn = Place(_header, "Draw", DesignW - 14f - bw, 19, bw, 37);
            UIShapes.AddRounded(Place(btn, "Edge", 0, 3, bw, 34).gameObject, YellowEdge, 17).raycastTarget = false;
            var fill = SkinImage(Place(btn, "Fill", 0, 0, bw, 34), _pc?.btnDraw, Yellow, 17, true);
            if (_pc != null && _pc.iconDraw != null)
            {
                var ic = Place(btn, "Icon", 12, 9, 16, 16).gameObject.AddComponent<Image>();
                ic.sprite = _pc.iconDraw;
                ic.raycastTarget = false;
            }
            tl.rectTransform.SetParent(btn, false);
            tl.rectTransform.anchoredPosition = new Vector2(33f, 0f);
            tl.rectTransform.sizeDelta = new Vector2(tw + 2f, 34f);
            tl.alignment = TextAlignmentOptions.Left;
            var chipRt = Place(btn, "Chip", 33f + tw + 7f, 9, cw, 16);
            SkinImage(chipRt, _pc?.chipFree, new Color(1f, 1f, 1f, 0.6f), 8, true);
            cl.rectTransform.SetParent(chipRt, false);
            cl.rectTransform.anchoredPosition = Vector2.zero;
            cl.rectTransform.sizeDelta = new Vector2(cw, 16f);
            Button(btn, fill, OpenDraw);
            if (invites > 0)
            {
                var dot = Place(btn, "Dot", bw - 12f, -5, 18, 18);
                SkinImage(dot, _pc?.dotRed, Coral, 9, false);
                Label(dot, 0, 0, 18, 18, invites > 9 ? "9+" : invites.ToString(), 9, Color.white);
            }
        }

        // ─── 抽好友 ────────────────────────────────────────────────────

        void OpenDraw()
        {
            var fb = FirebaseManager.Instance;
            if (_capture || fb == null || _drawing) return;
            if (fb.MatchIn.Count > 0) { ShowDrawCard(fb.MatchIn[0].ToProfile(), DrawView.Incoming, fb.MatchIn[0]); return; }
            if (PostcardRules.IsFull(fb.CampFriends.Count, fb.FriendUnlocks, FirebaseManager.PostcardConfig)) { AskUnlock(); return; }
            int cost = fb.DrawCostNow;
            if (cost > 0)
                ShowConfirm("今天的免費次數用完了", $"再抽一次要 {cost} 金幣（從營地金幣扣）。", $"用 {cost} 金幣抽", DoDraw);
            else DoDraw();
        }

        void DoDraw()
        {
            var fb = FirebaseManager.Instance;
            if (fb == null || _drawing) return;
            _drawing = true;
            ShowToast("抽好友中…");
            fb.DrawFriend((p, err) =>
            {
                _drawing = false;
                if (this == null) return;
                if (err == "full") { AskUnlock(); return; }
                if (err != null) { ShowToast(err); return; }
                if (_toast != null) _toast.gameObject.SetActive(false);
                ShowDrawCard(p, DrawView.Draw, null);
            });
        }

        void AskUnlock()
        {
            var s = FirebaseManager.PostcardConfig;
            var fb = FirebaseManager.Instance;
            int cap = fb != null ? fb.FriendCapacity : s.friendLimit;
            ShowConfirm($"好友已滿 {cap} 個，解鎖更多位置？", $"用 {s.unlockPrice} 金幣多 {s.unlockSlots} 個好友位置（從營地金幣扣）。",
                $"{s.unlockPrice} 金幣解鎖", () => fb?.UnlockFriendSlot(err => { if (this != null) ShowToast(err ?? "解鎖了！可以再抽好友"); }));
        }

        void CloseDraw()
        {
            if (_drawLayer != null) Destroy(_drawLayer.gameObject);
            _drawLayer = _drawCard = _spinner = null;
        }

        /// <summary>配對營地卡（310 × 456，在 (40, 128)）：view 決定標題與下方按鈕。</summary>
        void ShowDrawCard(CampProfile p, DrawView view, FirebaseManager.MatchRequest req)
        {
            if (p == null) return;
            bool flip = _drawLayer == null || _drawProfile == null || _drawProfile.roomId != p.roomId;
            CloseDraw();
            _drawProfile = p;
            _drawRequest = req;
            _drawView = view;
            _drawLayer = OverlayLayer("Draw", 0.55f, out var design, view == DrawView.Waiting ? (Action)CloseDraw : null);

            string title = view == DrawView.Incoming ? "有營地想跟你們當好友！" : view == DrawView.Waiting ? "等對方確認…" : "抽到一個營地！";
            Label(design, 0, 84, DesignW, 30, title, 20, Color.white);

            _drawCard = Place(design, "Card", 40, 128, 310, 456);
            _drawCard.pivot = new Vector2(0.5f, 0.5f);
            _drawCard.anchoredPosition = new Vector2(40f + 155f, -(128f + 228f));
            UIShapes.AddShadow(_drawCard, new Color(20f / 255f, 30f / 255f, 20f / 255f, 0.35f), 26, 40, 16);
            SkinImage(Stretch(_drawCard, "Bg"), _pc?.card, Paper, 26, true).raycastTarget = true;
            CardBody(_drawCard, p);

            const float W = 310f - 36f;
            float by = 456f - 18f - 53f;
            if (view == DrawView.Waiting)
            {
                var wait = Place(_drawCard, "Wait", 18, by, W, 50);
                SkinImage(wait, _pc?.btnWait, Beige, 15, true);
                const string wt = "等對方確認…";
                var wl = Label(wait, 0, 0, W, 50, wt, 15, SubText);
                float ww = Mathf.Ceil(TextWidth(wl, wt));
                _spinner = Place(wait, "Spinner", (W - ww) / 2f - 24f, 17, 16, 16);
                _spinner.pivot = new Vector2(0.5f, 0.5f);
                _spinner.anchoredPosition = new Vector2((W - ww) / 2f - 24f + 8f, -25f);
                SkinImage(_spinner, _pc?.spinner, Line, 8, false);
                wl.rectTransform.anchoredPosition = new Vector2(10f, 0f);
                var note = Label(design, 30, 128 + 456 + 14, DesignW - 60f, 18, $"對方 {FirebaseManager.PostcardConfig.confirmExpireHours} 小時內確認就會成為好友・點空白處回地圖", 11, new Color(1f, 1f, 1f, 0.85f));
                note.enableWordWrapping = true;
            }
            else
            {
                float noW = 104f, yesW = W - noW - 10f;
                var no = Place(_drawCard, "Skip", 18, by, noW, 50);
                var noImg = SkinImage(no, _pc?.btnNo, Color.white, 15, true);
                Label(no, 0, 0, noW, 50, "略過", 15, Ink);
                Button(no, noImg, OnDrawSkip);
                var yes = Place(_drawCard, "Add", 18f + noW + 10f, by, yesW, 53);
                UIShapes.AddRounded(Place(yes, "Edge", 0, 3, yesW, 50).gameObject, YellowEdge, 15).raycastTarget = false;
                var yesImg = SkinImage(Place(yes, "Fill", 0, 0, yesW, 50), _pc?.btnYes, Yellow, 15, true);
                Label(yes, 0, 0, yesW, 50, "加好友", 15, Ink);
                Button(yes, yesImg, OnDrawAdd);
                BlockReportLinks(design, 128 + 456 + 12, DesignW, p.roomId, p.name, null, view == DrawView.Incoming ? "match_in" : "draw", Color.white, () => { CloseDraw(); ShowNextIncoming(); });
            }
            _drawFlipT = flip && view != DrawView.Waiting ? 0f : 1f;
            _drawCard.localScale = new Vector3(_drawFlipT >= 1f ? 1f : 0.01f, 1f, 1f);
        }

        /// <summary>卡片內容：場景＋寵物（內距 10）、國家、名字、寵物・品種・成員數、最常拍、一樣的話黃標。</summary>
        void CardBody(RectTransform card, CampProfile p)
        {
            int tint = (int)(TravelMapGen.Hash(p.roomId ?? "") % 5u);
            var scene = Place(card, "Scene", 10, 10, 290, 220);
            var mask = UIShapes.AddRounded(scene.gameObject, SceneGreen, 18);
            mask.raycastTarget = false;
            scene.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var camp = _skin != null ? _skin.campBackground : null;
            if (camp != null)
            {
                var raw = Stretch(scene, "Camp").gameObject.AddComponent<RawImage>();
                raw.texture = camp;
                raw.color = FriendSceneTint[tint];
                raw.raycastTarget = false;
                raw.uvRect = new Rect(0f, 0.25f, 1f, 220f / 290f * camp.width / Mathf.Max(1f, camp.height));
            }
            if (_skin != null && _skin.pet != null)
            {
                var pet = Place(scene, "Pet", 85, 70, 120, 104).gameObject.AddComponent<Image>();
                pet.sprite = _skin.pet;
                pet.preserveAspect = true;
                pet.color = FriendPetTint[tint];
                pet.raycastTarget = false;
            }
            string country = PostcardRules.CountryName(p.country);
            if (country.Length > 0)
            {
                var cl = Label(scene, 0, 0, 10, 22, country, 12, Ink);
                float cw = Mathf.Ceil(TextWidth(cl, country)) + 20f;
                var tag = Place(scene, "Country", 10, 10, cw, 22);
                SkinImage(tag, _pc?.tagCountry, new Color(Paper.r, Paper.g, Paper.b, 0.92f), 11, true);
                cl.rectTransform.SetParent(tag, false);
                cl.rectTransform.anchoredPosition = Vector2.zero;
                cl.rectTransform.sizeDelta = new Vector2(cw, 22f);
            }

            float y = 244f;
            var name = Label(card, 18, y, 274, 28, string.IsNullOrEmpty(p.name) ? "營地" : p.name, 20, Ink);
            name.overflowMode = TextOverflowModes.Ellipsis;
            y += 30f;
            string pet2 = string.IsNullOrEmpty(p.pet) ? "" : p.pet + "・";
            string breed = string.IsNullOrEmpty(p.breed) ? "" : p.breed + "・";
            Label(card, 18, y, 274, 18, $"{pet2}{breed}{Mathf.Max(1, p.members)} 位成員", 12, SubText);
            y += 24f;
            Label(card, 18, y, 274, 18, string.IsNullOrEmpty(p.top) ? "還沒有照片" : $"最常拍：{p.top}", 13, Brown);
            y += 26f;
            var fb = FirebaseManager.Instance;
            string mine = FirebaseManager.MyTopCategory();
            if (PostcardRules.SameTop(mine, p.top))
            {
                string st = $"你們都愛拍{p.top}！";
                var sl = Label(card, 0, 0, 10, 22, st, 12, Ink);
                float sw = Mathf.Ceil(TextWidth(sl, st)) + 22f;
                var tag = Place(card, "Same", (310f - sw) / 2f, y, sw, 22);
                SkinImage(tag, _pc?.tagSame, Yellow, 11, true);
                sl.rectTransform.SetParent(tag, false);
                sl.rectTransform.anchoredPosition = Vector2.zero;
                sl.rectTransform.sizeDelta = new Vector2(sw, 22f);
            }
        }

        void OnDrawSkip()
        {
            var fb = FirebaseManager.Instance;
            if (_drawView == DrawView.Incoming && _drawRequest != null) fb?.SkipMatch(_drawRequest); // 略過不通知對方
            CloseDraw();
        }

        void OnDrawAdd()
        {
            var fb = FirebaseManager.Instance;
            var p = _drawProfile;
            if (fb == null || p == null) return;
            if (PostcardRules.IsFull(fb.CampFriends.Count, fb.FriendUnlocks, FirebaseManager.PostcardConfig)) { CloseDraw(); AskUnlock(); return; }
            var req = _drawView == DrawView.Incoming ? _drawRequest : null;
            bool mutual = req != null || HasMatchIn(fb, p.roomId);
            Action<string> done = err =>
            {
                if (this == null) return;
                if (err == "full") { CloseDraw(); AskUnlock(); return; }
                if (err != null) { ShowToast(err); return; }
                if (mutual)
                {
                    CloseDraw();
                    ShowToast($"你和{p.name}成為好友了！");
                    ShowNextIncoming();
                }
                else
                {
                    _waitingFor.Add(p.roomId);
                    ShowDrawCard(p, DrawView.Waiting, null);
                }
            };
            if (req != null) fb.AcceptMatch(req, done); else fb.SendMatch(p, done);
        }

        static bool HasMatchIn(FirebaseManager fb, string roomId)
        {
            foreach (var m in fb.MatchIn) if (m.roomId == roomId) return true;
            return false;
        }

        void ShowNextIncoming()
        {
            var fb = FirebaseManager.Instance;
            if (fb != null && fb.MatchIn.Count > 0) ShowDrawCard(fb.MatchIn[0].ToProfile(), DrawView.Incoming, fb.MatchIn[0]);
        }

        /// <summary>抽好友的資料變了：等確認的那一個成為好友了就通知（App 內，推播之後做）。</summary>
        void OnPostcardDataDraw() => CheckNewFriends();

        void CheckNewFriends()
        {
            var fb = FirebaseManager.Instance;
            if (fb == null || !fb.CampFriendsLoaded) return;
            var now = new HashSet<string>();
            foreach (var f in fb.CampFriends) now.Add(f.roomId);
            if (_knownFriends != null)
                foreach (var f in fb.CampFriends)
                    if (!_knownFriends.Contains(f.roomId) && !f.roomId.StartsWith("DEBUG_"))
                    {
                        ShowToast($"你和{f.name}成為好友了！地圖長出一塊新地");
                        if (_drawLayer != null && _drawProfile != null && _drawProfile.roomId == f.roomId) CloseDraw();
                    }
            _knownFriends = now;
        }

        void AnimateDraw()
        {
            if (_spinner != null) _spinner.localRotation = Quaternion.Euler(0f, 0f, -Time.unscaledTime / 0.9f * 360f);
            if (_drawCard != null && _drawFlipT < 1f)
            {
                // 翻開：rotateY 90° → 0°（用寬度的 cos 近似）
                _drawFlipT = Mathf.MoveTowards(_drawFlipT, 1f, Time.unscaledDeltaTime / 0.7f);
                float e = 1f - Mathf.Pow(1f - _drawFlipT, 3f);
                _drawCard.localScale = new Vector3(Mathf.Max(0.01f, Mathf.Cos((1f - e) * Mathf.PI / 2f)), 1f, 1f);
            }
        }

        // ─── 共用：整頁的暗色圖層、確認框、封鎖・檢舉 ────────────────

        float TopInsetCanvas()
        {
            if (_capture) return 0f;
            float sc = _canvas.rootCanvas.scaleFactor;
            return (Screen.height - Screen.safeArea.yMax) * (sc > 0f ? 1f / sc : 1f);
        }

        /// <summary>蓋在地圖上的一層（暗色 dim），裡面的 design 是 390 × 772 設計座標。onTapOutside＝點空白處（null＝不能點掉）。</summary>
        RectTransform OverlayLayer(string name, float dim, out RectTransform design, Action onTapOutside)
        {
            var layer = Stretch(_root, name);
            layer.SetAsLastSibling();
            var img = layer.gameObject.AddComponent<Image>();
            img.color = new Color(Dim.r, Dim.g, Dim.b, dim);
            if (onTapOutside != null)
            {
                var b = layer.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => onTapOutside());
            }
            design = new GameObject("Design", typeof(RectTransform)).GetComponent<RectTransform>();
            design.SetParent(layer, false);
            design.anchorMin = design.anchorMax = new Vector2(0.5f, 1f);
            design.pivot = new Vector2(0.5f, 1f);
            design.sizeDelta = new Vector2(DesignW, DesignH);
            design.localScale = Vector3.one * (_root.rect.width / DesignW);
            design.anchoredPosition = new Vector2(0f, -TopInsetCanvas());
            if (_toast != null) _toast.SetAsLastSibling();
            return layer;
        }

        /// <summary>確認框：標題、說明、「取消」／黃色確認鈕。點空白處＝取消。</summary>
        void ShowConfirm(string title, string body, string okText, Action ok, string cancelText = "先不要")
        {
            if (_confirmLayer != null) Destroy(_confirmLayer.gameObject);
            Action close = () => { if (_confirmLayer != null) Destroy(_confirmLayer.gameObject); _confirmLayer = null; };
            _confirmLayer = OverlayLayer("Confirm", 0.5f, out var design, close);
            const float W = 310f, Inner = W - 36f;
            var card = Place(design, "Card", 40, 250, W, 200);
            UIShapes.AddShadow(card, new Color(0f, 0f, 0f, 0.3f), 22, 30, 10);
            UIShapes.AddRounded(Stretch(card, "Bg").gameObject, Paper, 22).raycastTarget = true;
            float y = 22f;
            var t = Label(card, 18, y, Inner, 24, title, 17, Ink);
            t.enableWordWrapping = true;
            float th = Mathf.Ceil(TextHeight(t, title, Inner));
            t.rectTransform.sizeDelta = new Vector2(Inner, th);
            y += th + 8f;
            var b = Label(card, 18, y, Inner, 18, body, 12, SubText);
            float bh = Mathf.Ceil(TextHeight(b, body, Inner));
            b.rectTransform.sizeDelta = new Vector2(Inner, bh);
            y += bh + 18f;
            float noW = 104f, yesW = Inner - noW - 10f;
            var no = Place(card, "Cancel", 18, y, noW, 48);
            var noImg = SkinImage(no, _pc?.btnNo, Color.white, 15, true);
            Label(no, 0, 0, noW, 48, cancelText, 14, Ink);
            Button(no, noImg, () => close());
            var yes = Place(card, "OK", 18f + noW + 10f, y, yesW, 51);
            UIShapes.AddRounded(Place(yes, "Edge", 0, 3, yesW, 48).gameObject, YellowEdge, 15).raycastTarget = false;
            var yesImg = SkinImage(Place(yes, "Fill", 0, 0, yesW, 48), _pc?.btnYes, Yellow, 15, true);
            Label(yes, 0, 0, yesW, 48, okText, 14, Ink);
            Button(yes, yesImg, () => { close(); ok?.Invoke(); });
            y += 51f + 18f;
            card.sizeDelta = new Vector2(W, y);
            card.anchoredPosition = new Vector2(40f, -(DesignH - y) / 2f + 40f);
        }

        /// <summary>
        /// 「封鎖・檢舉」兩個小字按鈕（配對卡、好友營地面板、收到的明信片，5.21 ⑦），置中在 (0, y) 寬 w。
        /// 封鎖：刪掉好友、地圖那塊地消失、以後不會再配到；檢舉：把內容送去審核。
        /// </summary>
        void BlockReportLinks(RectTransform parent, float y, float w, string roomId, string name, string postcardId, string where, Color color, Action afterBlock = null)
        {
            if (string.IsNullOrEmpty(roomId)) return;
            string label = string.IsNullOrEmpty(name) ? "這個營地" : name;
            var blk = Place(parent, "Block", w / 2f - 70f, y, 60, 24);
            var bImg = blk.gameObject.AddComponent<Image>();
            bImg.color = new Color(0, 0, 0, 0);
            Label(blk, 0, 0, 60, 24, "封鎖", 12, color);
            Button(blk, bImg, () => ShowConfirm($"封鎖{label}？", "封鎖後會刪掉好友，地圖上那塊地會消失，以後也不會再配到這個營地。", "封鎖", () =>
                FirebaseManager.Instance?.BlockCamp(roomId, ok =>
                {
                    if (this == null) return;
                    ShowToast(ok ? "已封鎖" : "封鎖失敗，請再試一次");
                    if (!ok) return;
                    if (_sheetKind == SheetKind.Send) CloseSheet();
                    afterBlock?.Invoke();
                })));
            Label(parent, w / 2f - 10f, y, 20, 24, "・", 12, color);
            var rep = Place(parent, "Report", w / 2f + 10f, y, 60, 24);
            var rImg = rep.gameObject.AddComponent<Image>();
            rImg.color = new Color(0, 0, 0, 0);
            Label(rep, 0, 0, 60, 24, "檢舉", 12, color);
            Button(rep, rImg, () => ShowConfirm($"檢舉{label}？", postcardId != null ? "會把這張明信片送去審核。" : "會把這個營地送去審核。", "檢舉", () =>
                FirebaseManager.Instance?.ReportCamp(roomId, name, postcardId, where, ok => { if (this != null) ShowToast(ok ? "已送出檢舉，謝謝你" : "送出失敗，請再試一次"); })));
        }
    }
}
