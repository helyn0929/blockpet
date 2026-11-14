using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.Hand
{
    /// <summary>
    /// 首頁 v9 的寵物（設計文件 5.25 ①②）：
    /// - 餓了（出牌冷卻結束）：寵物左右輕晃（1.2 秒一次、±3°），泡泡「好餓…」「點我餵食・想吃○○」；點寵物 → 發牌。
    /// - 不餓：點寵物 → 泡泡「飽飽的～」「等一下再餵我」1.6 秒。
    /// - 餵完的反應（命中／沒命中）、貼紙全部撕完的「乾淨了！」也用同一個泡泡。
    /// - 「可以進化時先進化」要等進化系統（開發順序第 10 步）做好再接，現在點寵物直接出牌。
    /// 寵物會走動，點擊區和泡泡每幀跟著寵物在畫面上的位置。
    /// </summary>
    public partial class HandController
    {
        static readonly Color CleanGreen = Hex(0x4F8F45);
        const float DotSize = 10f, DotGap = 5f; // 第二行的類別色點 10（5.25 ③，2026-11-07）

        /// <summary>
        /// 餵完泡泡的類別色點與字色（設計文件 5.25 ③、設計稿 Home9 catStyle）：點＝類別色，字＝那一類的深色。
        /// 自然、城市的深色設計稿沒有，沿用想吃膠囊的字色（2026-11-06 使用者決定）。
        /// </summary>
        static bool CategoryColors(string category, out Color dot, out Color ink)
        {
            switch (category)
            {
                case "美食": dot = Hex(0xF29A3A); ink = Hex(0xA4511B); return true;
                case "自然": dot = Hex(0x6FA45F); ink = Hex(0x4A7A3F); return true;
                case "城市": dot = Hex(0x6F8FE0); ink = Hex(0x3F5BA8); return true;
                case "生活人物": dot = Hex(0xD78AD0); ink = Hex(0x8A3F84); return true;
                case "寵物": dot = Hex(0xE8C33A); ink = Hex(0x8A6A12); return true;
            }
            dot = ink = Ink;
            return false;
        }
        const float FullHold = 1.6f;
        const float ReactionHold = 2.4f;
        const float CleanHold = 2.2f;
        // 設計稿寵物 (128, 430) 134×124；找不到寵物時泡泡放這裡
        static readonly Rect DefaultPetRect = new Rect(128f - DesignW / 2f, DesignH - 430f - 124f, 134f, 124f);

        enum Say { None, Hungry, Full, Reaction, Clean }

        RectTransform _petHit;
        RectTransform _bubble;
        CanvasGroup _bubbleGroup;
        TMP_Text _bubbleTitle;
        TMP_Text _bubbleSub;
        Say _say;
        float _sayUntil;
        string _shownBubble;
        float _bubblePop = -1f;
        string _reactTitle, _reactSub;
        Color _reactColor, _reactSubColor;
        Color? _reactDot;   // 餵完泡泡的類別色點（辨識失敗、昏倒沒有）
        bool _reactTilt;    // 沒命中：寵物歪頭
        RectTransform _bubbleDot;
        Image _bubbleDotOuter, _bubbleDotInner;
        BlockPet.Feed.FeedResult? _pendingLine;
        float _pendingAt;

        Transform _wobbleTarget;
        Quaternion _wobbleBase;
        float _wobbleTime;
        bool _wobbleIsTilt;
        PetHealthManager _health;

        void BuildPetHit()
        {
            // 透明的點擊區，每幀蓋在寵物身上（在貼紙下面，不會擋到貼紙）
            _petHit = Place(_design, "PetHit", 128, 430, 134, 124);
            _petHit.anchorMin = _petHit.anchorMax = new Vector2(0.5f, 0f);
            _petHit.pivot = Vector2.zero;
            var img = _petHit.gameObject.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);
            var b = _petHit.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(OnPetTapped);
        }

        void BuildBubble()
        {
            // 白底、圓角 16 16 16 4（左下角尖）、內距 7 12、陰影 0 4 10 rgba(0,0,0,0.15)；不擋點擊
            _bubble = Place(_design, "PetBubble", 0, 0, 120, 50);
            _bubble.anchorMin = _bubble.anchorMax = new Vector2(0.5f, 0f);
            _bubble.pivot = Vector2.zero; // 左下角對著寵物頭的右上
            _bubbleGroup = _bubble.gameObject.AddComponent<CanvasGroup>();
            _bubbleGroup.blocksRaycasts = false;
            _bubbleGroup.interactable = false;
            UIShapes.AddShadow(_bubble, new Color(0f, 0f, 0f, 0.15f), 16, 10, 4);
            var sprite = H9(s => s.petBubble);
            if (sprite != null) Fill(Stretch(_bubble, "Bg"), sprite, true, Color.white, 16).raycastTarget = false;
            else UIShapes.AddRounded(Stretch(_bubble, "Bg").gameObject, Color.white, 16).raycastTarget = false;
            // 類別色點 12 圓，外面一圈白 2＋同色 1（CSS box-shadow 往外畫，不佔排版位置）
            _bubbleDot = Place(_bubble, "Dot", 12f - 3f, 0, DotSize + 6f, DotSize + 6f);
            _bubbleDotOuter = UIShapes.AddRounded(_bubbleDot.gameObject, Color.white, (DotSize + 6f) / 2f);
            _bubbleDotOuter.raycastTarget = false;
            UIShapes.AddRounded(Place(_bubbleDot, "White", 1, 1, DotSize + 4f, DotSize + 4f).gameObject, Color.white, (DotSize + 4f) / 2f).raycastTarget = false;
            _bubbleDotInner = UIShapes.AddRounded(Place(_bubbleDot, "Fill", 3, 3, DotSize, DotSize).gameObject, Color.white, DotSize / 2f);
            _bubbleDotInner.raycastTarget = false;
            _bubbleDot.gameObject.SetActive(false);
            _bubbleTitle = Label(_bubble, 12, 7, 100, 22, "", 17, Ink, TextAlignmentOptions.Left);
            _bubbleTitle.enableWordWrapping = false;
            _bubbleSub = Label(_bubble, 12, 29, 100, 15, "", 11, Muted, TextAlignmentOptions.Left);
            _bubbleSub.enableWordWrapping = false;
            _bubble.gameObject.SetActive(false);
        }

        // ─── 點寵物 ────────────────────────────────────────────────────

        void OnPetTapped()
        {
            if (_dealt || _selected != null) return;
            if (!BlockPet.Feed.FeedGate.Check()) return; // 昏倒：打開復活視窗
            if (PhotoCooldown.IsActive)
            {
                SayFor(Say.Full, FullHold);
                return;
            }
            // 進化優先（5.25 ①）：進化系統還沒做，之後在這裡先進化
            if (_say == Say.Full) _say = Say.None;
            Deal();
        }

        void SayFor(Say say, float seconds)
        {
            _say = say;
            _sayUntil = Time.unscaledTime + seconds;
        }

        /// <summary>貼紙全部撕完：寵物泡泡「乾淨了！」，開心跳一下（不給金幣）。</summary>
        void SayClean()
        {
            SayFor(Say.Clean, CleanHold);
            if (AIController.Instance != null) BlockPet.Feed.PetHop.Play(AIController.Instance.gameObject, 1);
        }

        void OnFeedSettled(BlockPet.Feed.FeedResult r)
        {
            _pendingLine = r;
            _pendingAt = Time.unscaledTime;
        }

        /// <summary>餵完寵物隨機說一句（設計文件 5.25 ③，2026-11-07）。</summary>
        static readonly string[] HitLines = { "好好吃！", "超好吃～", "還要還要！" };
        static readonly string[] MissLines = { "嗯…？", "這是什麼…", "好像不是我想吃的" };

        void ShowReaction(BlockPet.Feed.FeedResult r)
        {
            // 設計文件 5.25 ③（2026-11-07 改；取代 11-06 寵物唸出「是美食！好好吃！」）：泡泡兩行——
            // 第一行寵物隨機說一句（Huninn 19 #3A2E22）：命中跳起來、沒命中歪頭；
            // 第二行小字 12：類別色點 10＋類別名，字用那一類的深色。辨識失敗只有第一行「這是什麼呀？」。不寫細項、愛心、金幣。
            _reactDot = null;
            _reactTilt = false;
            _reactSub = null;
            _reactSubColor = Muted;
            if (r.dead)
            {
                _reactTitle = "……";
                _reactSub = "昏倒了，點想吃膠囊復活";
                _reactColor = Ink;
            }
            else if (CategoryColors(r.category, out var dot, out var ink))
            {
                var lines = r.hit ? HitLines : MissLines;
                _reactTitle = lines[UnityEngine.Random.Range(0, lines.Length)];
                _reactColor = Ink;
                _reactSub = r.category;
                _reactSubColor = ink;
                _reactDot = dot;
                _reactTilt = !r.hit;
            }
            else
            {
                _reactTitle = "這是什麼呀？";
                _reactColor = Ink;
                _reactTilt = !r.hit;
            }
            SayFor(Say.Reaction, ReactionHold);
            _shownBubble = null; // 同樣的字也重新彈一次
            if (r.hit && !r.dead && AIController.Instance != null) BlockPet.Feed.PetHop.Play(AIController.Instance.gameObject);
        }

        // ─── 每幀 ──────────────────────────────────────────────────────

        void UpdatePet(bool cooling)
        {
            // 相機拍照餵食時首頁被藏起來，回到首頁才冒反應（太久就不冒了）
            if (_pendingLine.HasValue && _home)
            {
                if (Time.unscaledTime - _pendingAt < 6f) ShowReaction(_pendingLine.Value);
                _pendingLine = null;
            }
            if (_say != Say.None && _say != Say.Hungry && Time.unscaledTime >= _sayUntil) _say = Say.None;

            var rect = PetRectInDesign();
            bool petVisible = rect.HasValue;
            var r = rect ?? DefaultPetRect;

            // 點擊區：寵物圖的範圍，至少 90×90
            float w = Mathf.Max(90f, r.width), h = Mathf.Max(90f, r.height);
            _petHit.anchoredPosition = new Vector2(r.center.x - w / 2f, r.yMin);
            _petHit.sizeDelta = new Vector2(w, h);
            bool canTap = petVisible && !BlockPet.UI.TopBar.DecorModeSettings.IsDecorating; // 佈置時不要擋到拖家具
            if (_petHit.gameObject.activeSelf != canTap) _petHit.gameObject.SetActive(canTap);

            if (_health == null) _health = FindObjectOfType<PetHealthManager>();
            bool alive = _health == null || BlockPet.Feed.EconomyRules.CanFeed(_health.Hearts);
            bool hungry = petVisible && alive && !cooling && !_dealt;

            // 泡泡要說什麼：反應 > 乾淨了 > 飽飽的 > 好餓
            Say say = _say != Say.None ? _say : (hungry ? Say.Hungry : Say.None);
            string title = null, sub = null;
            Color color = Ink, subColor = Muted;
            Color? dot = null;
            float size = 17f, subSize = 11f;
            switch (say)
            {
                case Say.Reaction:
                    title = _reactTitle; sub = _reactSub; color = _reactColor; subColor = _reactSubColor; dot = _reactDot; size = 19f;
                    if (dot.HasValue) subSize = 12f; // 第二行辨識結果 12 Black
                    break;
                case Say.Clean: title = "乾淨了！"; sub = "大家的照片都看過囉"; color = CleanGreen; size = 19f; break;
                case Say.Full: title = "飽飽的～"; sub = "等一下再餵我"; break;
                case Say.Hungry:
                    string want = BlockPet.Feed.TodayWant.Current();
                    title = "好餓…";
                    sub = string.IsNullOrEmpty(want) ? "點我餵食" : $"點我餵食・想吃{want}";
                    break;
            }
            if (!petVisible) title = null;
            UpdateBubble(title, sub, color, subColor, subSize, dot, size, r);

            // 餓了左右輕晃；沒命中的反應歪頭；其他時候停
            if (say == Say.Reaction && _reactTilt && petVisible) Wobble(tilt: true);
            else if (hungry && say == Say.Hungry) Wobble(tilt: false);
            else StopWobble();
        }

        /// <summary>泡泡：標題一行＋說明一行；dot＝說明那行前面的類別色點（餵完的辨識結果）。</summary>
        void UpdateBubble(string title, string sub, Color color, Color subColor, float subSize, Color? dot, float size, Rect pet)
        {
            float dt = Time.unscaledDeltaTime;
            if (title == null)
            {
                if (!_bubble.gameObject.activeSelf) return;
                _bubbleGroup.alpha = Mathf.MoveTowards(_bubbleGroup.alpha, 0f, dt / 0.2f);
                if (_bubbleGroup.alpha <= 0f) { _bubble.gameObject.SetActive(false); _shownBubble = null; }
                return;
            }

            string key = title + "\n" + sub + "\n" + size + "\n" + (dot.HasValue ? ColorUtility.ToHtmlStringRGB(dot.Value) : "") + ColorUtility.ToHtmlStringRGB(subColor);
            if (key != _shownBubble)
            {
                _shownBubble = key;
                _bubbleTitle.text = title;
                _bubbleTitle.fontSize = size;
                _bubbleTitle.color = color;
                _bubbleSub.text = sub ?? "";
                _bubbleSub.fontSize = subSize;
                _bubbleSub.color = subColor;
                float titleH = Mathf.Round(size * 1.3f), subH = string.IsNullOrEmpty(sub) ? 0f : Mathf.Round(subSize * 1.35f);
                // 色點在第二行字的前面（10＋間距 5，外圈白 2＋同色 1），和那行字垂直置中
                float dotW = dot.HasValue && subH > 0f ? DotSize + DotGap : 0f;
                _bubbleDot.gameObject.SetActive(dotW > 0f);
                if (dotW > 0f)
                {
                    _bubbleDotOuter.color = dot.Value;
                    _bubbleDotInner.color = dot.Value;
                    _bubbleDot.anchoredPosition = new Vector2(12f - 3f, -(7f + titleH + (subH - DotSize) / 2f - 3f));
                }
                _bubbleTitle.rectTransform.anchoredPosition = new Vector2(12f, -7f);
                float textW = Mathf.Max(TextW(_bubbleTitle, title), string.IsNullOrEmpty(sub) ? 0f : dotW + TextW(_bubbleSub, sub));
                float bw = Mathf.Ceil(textW) + 24f, bh = 7f + titleH + subH + 7f;
                _bubble.sizeDelta = new Vector2(bw, bh);
                _bubbleTitle.rectTransform.sizeDelta = new Vector2(bw - 24f, titleH);
                _bubbleSub.rectTransform.anchoredPosition = new Vector2(12f + dotW, -(7f + titleH));
                _bubbleSub.rectTransform.sizeDelta = new Vector2(bw - 24f - dotW, subH);
                _bubbleSub.gameObject.SetActive(subH > 0f);
                _bubblePop = 0f; // 出現、換話都彈一下（0.3 秒 c5Pop）
                _bubble.gameObject.SetActive(true);
            }
            _bubbleGroup.alpha = Mathf.MoveTowards(_bubbleGroup.alpha, 1f, dt / 0.15f);

            // 彈出：0.3 秒，稍微超過再回來
            if (_bubblePop >= 0f)
            {
                _bubblePop += dt;
                float t = Mathf.Clamp01(_bubblePop / 0.3f);
                float s = t < 0.7f ? Mathf.Lerp(0.6f, 1.06f, Mathf.Sin(t / 0.7f * Mathf.PI * 0.5f)) : Mathf.Lerp(1.06f, 1f, (t - 0.7f) / 0.3f);
                _bubble.localScale = Vector3.one * s;
                if (t >= 1f) _bubblePop = -1f;
            }

            // 位置：設計稿寵物 (128,430) 134×124 → 泡泡左下 (226, 392+高)，依寵物在畫面上的大小等比；不超出畫面右邊
            Vector2 size2 = _bubble.sizeDelta;
            float x = pet.xMin + pet.width * (98f / 134f);
            float y = pet.yMax - pet.height * (12f / 124f);
            x = Mathf.Min(x, DesignW / 2f - 6f - size2.x);
            _bubble.anchoredPosition = new Vector2(Mathf.Round(x), Mathf.Round(y));
        }

        /// <summary>餓了輕晃（tilt=false）或沒命中歪頭（tilt=true）；同一個寵物圖、同一個原始角度。</summary>
        void Wobble(bool tilt)
        {
            var pet = AIController.Instance;
            if (pet == null) return;
            var r = pet.GetComponentInChildren<Renderer>();
            var target = r != null ? r.transform : pet.transform;
            if (_wobbleTarget != target || _wobbleIsTilt != tilt)
            {
                StopWobble();
                _wobbleTarget = target;
                _wobbleBase = target.localRotation;
                _wobbleTime = 0f;
                _wobbleIsTilt = tilt;
            }
            _wobbleTime += Time.unscaledDeltaTime;
            float a = tilt ? TiltAngle(_wobbleTime)
                // 設計稿 hHungry：1.2 秒一次，−3° → +3° → −3°（CSS 角度；Unity 正角度＝逆時針）
                : 3f * Mathf.Cos(_wobbleTime * Mathf.PI * 2f / 1.2f);
            target.localRotation = _wobbleBase * Quaternion.Euler(0f, 0f, a);
        }

        /// <summary>設計稿 c5Tilt 1.6 秒循環：0° → 30% −8° → 60% +6° → 0°（CSS 順時針為正，換成 Unity 角度要變號）。</summary>
        static float TiltAngle(float time)
        {
            float p = Mathf.Repeat(time / 1.6f, 1f);
            float css;
            if (p < 0.3f) css = Mathf.Lerp(0f, -8f, Mathf.SmoothStep(0f, 1f, p / 0.3f));
            else if (p < 0.6f) css = Mathf.Lerp(-8f, 6f, Mathf.SmoothStep(0f, 1f, (p - 0.3f) / 0.3f));
            else css = Mathf.Lerp(6f, 0f, Mathf.SmoothStep(0f, 1f, (p - 0.6f) / 0.4f));
            return -css;
        }

        void StopWobble()
        {
            if (_wobbleTarget == null) return;
            _wobbleTarget.localRotation = _wobbleBase;
            _wobbleTarget = null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor 截圖用（BlockPet/Debug/寵物泡泡截圖）：在指定的 390 × 772 設計框裡只建泡泡，照餵完結果顯示（不按 Play）。
        /// </summary>
        public void CaptureReaction(RectTransform design, HandSkin handSkin, BlockPet.Feed.FeedResult r)
        {
            skin = handSkin;
            _h9 = BlockPet.UI.Home.Home9Skin.Load();
            _design = design;
            BuildBubble();
            ShowReaction(r);
            UpdateBubble(_reactTitle, _reactSub, _reactColor, _reactSubColor, _reactDot.HasValue ? 12f : 11f, _reactDot, 19f, DefaultPetRect);
            _bubblePop = -1f;
            _bubble.localScale = Vector3.one;
            _bubbleGroup.alpha = 1f;
        }

        public static Rect CapturePetRect => DefaultPetRect;
#endif

        /// <summary>文字寬度（動態字型要先把字加進去才量得準）。</summary>
        static float TextW(TMP_Text t, string s)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            if (t.font != null) t.font.TryAddCharacters(s);
            return t.GetPreferredValues(s).x;
        }
    }
}
