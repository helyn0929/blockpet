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
        static readonly Color ReactHit = Hex(0xC2621A);
        static readonly Color CleanGreen = Hex(0x4F8F45);
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
        Color _reactColor;
        BlockPet.Feed.FeedResult? _pendingLine;
        float _pendingAt;

        Transform _wobbleTarget;
        Quaternion _wobbleBase;
        float _wobbleTime;
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

        void ShowReaction(BlockPet.Feed.FeedResult r)
        {
            // 命中：開心跳起；沒命中：中性的「嗯…？」（不用難過的表情）
            _reactTitle = r.dead ? "……" : (r.hit ? "好好吃！" : "嗯…？");
            _reactSub = r.dead ? "昏倒了，點想吃膠囊復活" : r.hit
                ? $"命中現在想吃・+{r.coins} 金幣"
                : (string.IsNullOrEmpty(r.want) ? $"+{r.coins} 金幣" : $"現在想吃{r.want}・+{r.coins} 金幣");
            _reactColor = r.dead ? Ink : (r.hit ? ReactHit : Muted);
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
            Color color = Ink;
            float size = 17f;
            switch (say)
            {
                case Say.Reaction: title = _reactTitle; sub = _reactSub; color = _reactColor; size = 19f; break;
                case Say.Clean: title = "乾淨了！"; sub = "大家的照片都看過囉"; color = CleanGreen; size = 19f; break;
                case Say.Full: title = "飽飽的～"; sub = "等一下再餵我"; break;
                case Say.Hungry:
                    string want = BlockPet.Feed.TodayWant.Current();
                    title = "好餓…";
                    sub = string.IsNullOrEmpty(want) ? "點我餵食" : $"點我餵食・想吃{want}";
                    break;
            }
            if (!petVisible) title = null;
            UpdateBubble(title, sub, color, size, r);

            // 餓了左右輕晃；有反應、出牌中就停
            if (hungry && say == Say.Hungry) Wobble();
            else StopWobble();
        }

        void UpdateBubble(string title, string sub, Color color, float size, Rect pet)
        {
            float dt = Time.unscaledDeltaTime;
            if (title == null)
            {
                if (!_bubble.gameObject.activeSelf) return;
                _bubbleGroup.alpha = Mathf.MoveTowards(_bubbleGroup.alpha, 0f, dt / 0.2f);
                if (_bubbleGroup.alpha <= 0f) { _bubble.gameObject.SetActive(false); _shownBubble = null; }
                return;
            }

            string key = title + "\n" + sub + "\n" + size;
            if (key != _shownBubble)
            {
                _shownBubble = key;
                _bubbleTitle.text = title;
                _bubbleTitle.fontSize = size;
                _bubbleTitle.color = color;
                _bubbleSub.text = sub ?? "";
                float titleH = Mathf.Round(size * 1.3f), subH = string.IsNullOrEmpty(sub) ? 0f : 15f;
                float textW = Mathf.Max(TextW(_bubbleTitle, title), string.IsNullOrEmpty(sub) ? 0f : TextW(_bubbleSub, sub));
                float bw = Mathf.Ceil(textW) + 24f, bh = 7f + titleH + subH + 7f;
                _bubble.sizeDelta = new Vector2(bw, bh);
                _bubbleTitle.rectTransform.sizeDelta = new Vector2(bw - 24f, titleH);
                _bubbleSub.rectTransform.anchoredPosition = new Vector2(12f, -(7f + titleH));
                _bubbleSub.rectTransform.sizeDelta = new Vector2(bw - 24f, subH);
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

        void Wobble()
        {
            var pet = AIController.Instance;
            if (pet == null) return;
            var r = pet.GetComponentInChildren<Renderer>();
            var target = r != null ? r.transform : pet.transform;
            if (_wobbleTarget != target)
            {
                StopWobble();
                _wobbleTarget = target;
                _wobbleBase = target.localRotation;
                _wobbleTime = 0f;
            }
            // 設計稿 hHungry：1.2 秒一次，−3° → +3° → −3°（CSS 角度；Unity 正角度＝逆時針）
            _wobbleTime += Time.unscaledDeltaTime;
            float a = 3f * Mathf.Cos(_wobbleTime * Mathf.PI * 2f / 1.2f);
            target.localRotation = _wobbleBase * Quaternion.Euler(0f, 0f, a);
        }

        void StopWobble()
        {
            if (_wobbleTarget == null) return;
            _wobbleTarget.localRotation = _wobbleBase;
            _wobbleTarget = null;
        }

        /// <summary>文字寬度（動態字型要先把字加進去才量得準）。</summary>
        static float TextW(TMP_Text t, string s)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            if (t.font != null) t.font.TryAddCharacters(s);
            return t.GetPreferredValues(s).x;
        }
    }
}
