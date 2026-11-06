using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BlockPet.UI.TopBar;
using BlockPet.UI.Home;

namespace BlockPet.Hand
{
    /// <summary>
    /// 首頁手牌與底部按鈕，依設計文件 5.25「首頁 v9」（5.6 的扇形排法、舉起照片、餵食反應照舊）：
    /// - 底部固定相簿／相機／聊天三顆圓鈕（不放字；聊天有未讀紅點）。
    /// - 寵物餓了（出牌冷卻結束）點寵物 → 底部三顆往下滑走，手牌從畫面底部一張一張發上來（只有照片牌，最多 5 張）。
    ///   點手牌上方空白處收回。寵物、泡泡在 HandController.Pet.cs。
    /// - 點照片 → 照片浮起成小卡（放回／餵給寵物）；餵給寵物 → 牌往上飛給寵物、寵物吃掉，照片存進相簿（自動分享到營地），
    ///   進入共用冷卻（<see cref="PhotoCooldown"/>），手牌收回、底部三顆回來。
    /// - 照片貼紙、最新照片貼紙在 HandController.Stickers.cs。
    /// 介面在執行時以 390×772 的設計座標建立（直接對照設計檔），再依螢幕寬度等比放大、貼齊畫面底部。
    /// 餵食結算（辨識、命中、金幣、愛心）在 <see cref="BlockPet.Feed.FeedSettlement"/>；結算完寵物泡泡顯示反應，命中時開心跳兩下。
    /// </summary>
    public partial class HandController : MonoBehaviour
    {
        const float DesignW = 390f;
        const float DesignH = 772f;
        const string PrefsPlayedIds = "BlockPet.Hand.PlayedIds";
        const int MaxPlayedIds = 100;
        const float FlightTime = 0.6f;      // 牌飛向寵物的時間
        const float DealStagger = 0.07f;    // 發牌每張間隔
        const float DealTime = 0.55f;       // 每張滑上來的時間
        const float BarTime = 0.35f;        // 底部三顆滑走／回來
        const float BarDrop = 160f;
        const int AlbumPhotoMaxSize = 1280; // 存進相簿的照片最長邊

        static readonly Color Ink = Hex(0x3A2E22);
        static readonly Color Cream = Hex(0xFFFCF4);
        static readonly Color Coral = Hex(0xE86B4F);
        static readonly Color Yellow = Hex(0xF6C945);
        static readonly Color YellowShadow = Hex(0xD9A92C);
        static readonly Color Beige = Hex(0xF2E9D8);
        static readonly Color Glass = new Color(1f, 0.973f, 0.925f, 0.72f);
        static readonly Color CardShadow = new Color(0.12f, 0.12f, 0.08f, 0.35f);
        static readonly Color Muted = Hex(0x6B5D4E);

        [Header("Style")]
        [Tooltip("設計稿切出來的素材與字型（BlockPet/Build Hand Skin 會自動接上）")]
        [SerializeField] HandSkin skin;
        [Tooltip("沒有 Hand Skin 時用的字型")]
        [SerializeField] TMP_FontAsset font;

        [Header("Data")]
        [SerializeField] int handSize = 5;
        [SerializeField] int recentDays = 7;
        [Tooltip("手牌縮圖最長邊（像素）")]
        [SerializeField] int thumbnailMaxSize = 512;

        [Header("Links")]
        [SerializeField] CameraUIManager cameraUI;
        [SerializeField] PageManager pageManager;
        [Tooltip("照片舉起時寵物走過去等待的位置（世界座標，可留空）")]
        [SerializeField] Transform petWaitPoint;
        [Tooltip("佈置模式要不要隱藏手牌（Build TopBar 會自動接上 Assets/Data/DecorModeSettings）")]
        [SerializeField] DecorModeSettings decorMode;
        [Tooltip("寵物吃東西的動畫（Eat trigger）沿用 FeedController 的 petAnimator；可留空")]
        [SerializeField] FeedController feedController;

        /// <summary>
        /// 按下「餵給寵物」時觸發，參數為照片與縮圖貼圖。
        /// 有訂閱者時貼圖交給訂閱者管理（用完要 Destroy）；沒有訂閱者時由手牌直接釋放。
        /// </summary>
        public event Action<HandPhoto, Texture2D> OnFeedRequested;

        public PhotoAccess Access { get; private set; } = PhotoAccess.Granted;
        public int PhotoCount => _photoCards.Count;

        class Card
        {
            public RectTransform rt;
            public CanvasGroup group;
            public HandPhoto photo;
            public Texture2D texture;
            public Vector2 targetPos;
            public float targetRot;
            public float targetAlpha = 1f;
            /// <summary>發牌動畫的時間（-1＝沒在發牌）。</summary>
            public float dealTime = -1f;
            public float dealDelay;
        }

        // 版面
        RectTransform _overlay;
        CanvasGroup _overlayGroup;
        RectTransform _design;
        RectTransform _cardsRoot;
        float _lastWidth = -1f;
        Home9Skin _h9;

        // 手牌（點寵物才發出來）
        readonly List<Card> _photoCards = new List<Card>();
        Card _selected;
        bool _dealt;
        RectTransform _backdrop;
        CanvasGroup _backdropGroup;
        TMP_Text _hint;

        // 浮起的照片
        RectTransform _popup;
        RawImage _popupPhoto;
        TMP_Text _popupCaption;
        TMP_Text _feedLabel;
        float _popupTime;

        // 底部固定的相簿／相機／聊天
        RectTransform _bar;
        CanvasGroup _barGroup;
        float _barOut;               // 0＝在原位、1＝滑走
        CanvasGroup _barCameraGroup;
        TMP_Text _cameraCountdown;
        RectTransform _chatBadge;
        TMP_Text _chatBadgeText;
        int _shownUnread = -1;

        // 空狀態（出牌時沒有照片）
        RectTransform _empty;
        TMP_Text _emptyText;
        GameObject _emptySettingsButton;

        // 飛向寵物的牌
        class Flight
        {
            public RectTransform rt;
            public CanvasGroup group;
            public HandPhoto photo;
            public Texture2D texture;
            public Vector2 from;
            public float time;
        }
        readonly List<Flight> _flights = new List<Flight>();

        bool _wasCooling;
        bool _loading;
        bool _loadedOnce;
        bool _petSummoned;
        bool _home;
        readonly List<string> _playedIds = new List<string>();

        // ─── Lifecycle ─────────────────────────────────────────────────

        void Awake()
        {
            if (cameraUI == null) cameraUI = FindObjectOfType<CameraUIManager>(true);
            if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
            if (feedController == null) feedController = FindObjectOfType<FeedController>(true);
            _h9 = Home9Skin.Load();
            LoadPlayedIds();

            // 由下往上的畫面順序：寵物點擊區 → 貼紙 → 最新貼紙 → 泡泡 → 底部三顆 → 出牌的暗底、提示、手牌 → 舉起的照片 → 飛的牌
            BuildOverlay();
            BuildPetHit();
            BuildStickers();
            BuildBubble();
            BuildBar();
            BuildBackdrop();
            _cardsRoot = Stretch(_design, "Cards");
            BuildEmptyState();
            BuildPopup();
            LayoutCards(snap: true);
        }

        void OnEnable()
        {
            if (_overlay != null) _overlay.gameObject.SetActive(true);
            if (cameraUI != null) cameraUI.hideHomeButtons = true;
            BlockPet.Feed.FeedSettlement.OnSettled += OnFeedSettled;
            Refresh();
        }

        void OnDisable()
        {
            if (_selected != null) PutBack();
            Collect();
            StopWobble();
            if (_overlay != null) _overlay.gameObject.SetActive(false);
            if (cameraUI != null) cameraUI.hideHomeButtons = false;
            BlockPet.Feed.FeedSettlement.OnSettled -= OnFeedSettled;
        }

        void OnDestroy()
        {
            foreach (var c in _photoCards)
                if (c.texture != null) Destroy(c.texture);
            foreach (var f in _flights) if (f.texture != null) Destroy(f.texture);
            _flights.Clear();
            DestroyStickerTextures();
            if (_overlay != null) Destroy(_overlay.gameObject);
        }

        void OnApplicationFocus(bool focus)
        {
            // 從設定頁（開權限）或相機 App 回來時重新讀取
            if (focus && isActiveAndEnabled && _selected == null) Refresh();
        }

        void Update()
        {
            // 只在首頁、且不在相機模式時顯示（相簿頁、聊天頁、選房間時都藏起來）
            _home = (cameraUI == null || cameraUI.CurrentMode == UIMode.Home)
                    && (pageManager == null || pageManager.IsHomePageActive)
                    && !(decorMode != null && decorMode.hideHand && DecorModeSettings.IsDecorating);
            _overlayGroup.alpha = _home ? 1f : 0f;
            _overlayGroup.blocksRaycasts = _home;

            float w = _overlay.rect.width;
            if (w > 0f && !Mathf.Approximately(w, _lastWidth))
            {
                _lastWidth = w;
                _design.localScale = Vector3.one * (w / DesignW);
            }

            // 冷卻開始（出牌、相機或快門拍照餵食）→ 手牌收回；冷卻結束 → 重新讀相簿，寵物又餓了
            bool cooling = PhotoCooldown.IsActive;
            if (cooling && !_wasCooling) Collect();
            else if (!cooling && _wasCooling) Refresh();
            _wasCooling = cooling;

            UpdateBar(cooling);
            AnimateCards();
            AnimateFlights();
            AnimatePopup();
            UpdatePet(cooling);
            UpdateStickers();
        }

        // ─── Data ──────────────────────────────────────────────────────

        /// <summary>重新讀取相簿最新照片（排除出過的）；照片沒變就不重建卡片。</summary>
        public void Refresh()
        {
            if (_loading) return;
            _loading = true;
            UpdateEmptyState();

            int fetchCount = Mathf.Min(30, handSize + _playedIds.Count);
            PhotoLibrary.Instance.FetchRecent(fetchCount, recentDays, (access, photos) =>
            {
                _loading = false;
                _loadedOnce = true;
                if (this == null) return;
                Access = access;

                var hand = new List<HandPhoto>();
                foreach (var p in photos)
                {
                    if (_playedIds.Contains(p.id)) continue;
                    hand.Add(p);
                    if (hand.Count >= handSize) break;
                }
                // 相簿是新到舊；手牌由左到右是舊到新，最新的在最右邊
                hand.Reverse();
                if (!SamePhotos(hand)) RebuildPhotoCards(hand);
                UpdateEmptyState();
            });
        }

        bool SamePhotos(List<HandPhoto> photos)
        {
            if (photos.Count != _photoCards.Count) return false;
            for (int i = 0; i < photos.Count; i++)
                if (photos[i].id != _photoCards[i].photo.id) return false;
            return true;
        }

        void RebuildPhotoCards(List<HandPhoto> photos)
        {
            if (_selected != null) PutBack();

            foreach (var c in _photoCards)
            {
                if (c.texture != null) Destroy(c.texture);
                Destroy(c.rt.gameObject);
            }
            _photoCards.Clear();

            foreach (var p in photos)
            {
                Texture2D tex = null;
                try { tex = NativeGallery.LoadImageAtPath(p.path, thumbnailMaxSize, false, false); }
                catch (Exception e) { Debug.LogWarning("[HandController] 讀取照片失敗: " + e.Message); }
                if (tex == null) continue;
                _photoCards.Add(BuildPhotoCard(p, tex));
            }

            LayoutCards(snap: !_dealt);
        }

        void LoadPlayedIds()
        {
            _playedIds.Clear();
            string raw = PlayerPrefs.GetString(PrefsPlayedIds, "");
            if (string.IsNullOrEmpty(raw)) return;
            foreach (var id in raw.Split('\n'))
                if (!string.IsNullOrEmpty(id)) _playedIds.Add(id);
        }

        void MarkPlayed(string id)
        {
            if (string.IsNullOrEmpty(id) || _playedIds.Contains(id)) return;
            _playedIds.Add(id);
            while (_playedIds.Count > MaxPlayedIds) _playedIds.RemoveAt(0);
            PlayerPrefs.SetString(PrefsPlayedIds, string.Join("\n", _playedIds));
            PlayerPrefs.Save();
        }

        static string DateCaption(HandPhoto p)
        {
            if (p.takenAtMs <= 0) return "";
            var t = DateTimeOffset.FromUnixTimeMilliseconds(p.takenAtMs).LocalDateTime;
            return $"{t.Month}/{t.Day}";
        }

        static string PetName()
        {
            string n = PetCollectionManager.Instance != null ? PetCollectionManager.Instance.GetPetDisplayName() : null;
            return string.IsNullOrEmpty(n) ? "寵物" : n;
        }

        // ─── 發牌／收回 ────────────────────────────────────────────────

        // 發牌的起點：畫面底部中間外面（設計稿 hdU：牌左上 (149, 760)）
        static readonly Vector2 DealFrom = new Vector2(149f + 46f, -(760f + 124f));

        /// <summary>點餓了的寵物：底部三顆滑走，手牌一張一張發上來。</summary>
        void Deal()
        {
            if (_dealt) return;
            _dealt = true;
            _hint.text = $"選一張餵 {PetName()}・點空白處收回";
            for (int i = 0; i < _photoCards.Count; i++)
            {
                var c = _photoCards[i];
                c.dealTime = 0f;
                c.dealDelay = i * DealStagger;
                c.rt.anchoredPosition = DealFrom;
                c.rt.localRotation = Quaternion.identity;
                c.rt.localScale = Vector3.one * 0.7f;
                c.group.alpha = 0f;
            }
            LayoutCards();
            Haptics.Light();
        }

        /// <summary>收回手牌（點空白處、餵完、冷卻開始）。</summary>
        void Collect()
        {
            if (!_dealt) return;
            if (_selected != null) PutBack();
            _dealt = false;
            foreach (var c in _photoCards) { c.dealTime = -1f; c.rt.localScale = Vector3.one; }
            LayoutCards();
        }

        // ─── Layout & animation ────────────────────────────────────────

        void LayoutCards(bool snap = false)
        {
            // 設計稿：手牌中心 x=156，牌距最多 46、總寬 220，每張轉 6°（5 張：left 18/64/110/156/202、top 638/629/620/629/638）
            int n = _photoCards.Count;
            float center = 156f;
            float sp = n > 1 ? Mathf.Min(46f, 220f / (n - 1)) : 0f;

            for (int i = 0; i < n; i++)
            {
                var c = _photoCards[i];
                float off = i - (n - 1) / 2f;
                float left = Mathf.Round(center + off * sp - 46f);
                float top = Mathf.Round(620f + Mathf.Abs(off) * 9f);
                // 收回時滑回畫面底部外面
                c.targetPos = _dealt ? new Vector2(left + 46f, -(top + 124f)) : DealFrom;
                c.targetRot = _dealt ? -off * 6f : 0f;
                c.targetAlpha = _dealt && c != _selected ? 1f : 0f;
                bool interactable = _dealt && c != _selected;
                c.group.blocksRaycasts = interactable;
                c.group.interactable = interactable;
                c.rt.SetSiblingIndex(i);

                if (snap)
                {
                    c.rt.anchoredPosition = c.targetPos;
                    c.rt.localRotation = Quaternion.Euler(0f, 0f, c.targetRot);
                    c.group.alpha = c.targetAlpha;
                }
            }
            UpdateEmptyState();
        }

        void AnimateCards()
        {
            float dt = Time.unscaledDeltaTime;
            float k = 1f - Mathf.Exp(-dt * 12f);
            foreach (var c in _photoCards)
            {
                if (c.dealTime >= 0f) AnimateDeal(c, dt);
                else
                {
                    c.rt.anchoredPosition = Vector2.Lerp(c.rt.anchoredPosition, c.targetPos, k);
                    c.rt.localRotation = Quaternion.Slerp(c.rt.localRotation, Quaternion.Euler(0f, 0f, c.targetRot), k);
                    c.group.alpha = Mathf.Lerp(c.group.alpha, c.targetAlpha, _dealt ? k : Mathf.Min(1f, k * 2f));
                }
            }

            // 出牌時的暗底和提示：0.3 秒淡入；舉起照片時提示先藏起來
            float bd = _dealt ? 1f : 0f;
            _backdropGroup.alpha = Mathf.MoveTowards(_backdropGroup.alpha, bd, dt / 0.3f);
            _backdropGroup.blocksRaycasts = _dealt;
            bool showBackdrop = _dealt || _backdropGroup.alpha > 0.01f;
            if (_backdrop.gameObject.activeSelf != showBackdrop) _backdrop.gameObject.SetActive(showBackdrop);
            bool hint = _dealt && _selected == null && _photoCards.Count > 0;
            if (_hint.gameObject.activeSelf != hint) _hint.gameObject.SetActive(hint);
        }

        /// <summary>
        /// 發牌（設計稿 hdU 關鍵影格，0.55 秒 cubic-bezier(.2,.8,.3,1)、每段各自套用）：
        /// 0% 底部中間、不轉、0.7 倍、透明 → 12% 不透明 → 78% 比定位高 10、轉 1.3 倍、1.04 倍 → 100% 定位。
        /// </summary>
        void AnimateDeal(Card c, float dt)
        {
            c.dealTime += dt;
            float t = (c.dealTime - c.dealDelay) / DealTime;
            if (t <= 0f) return;
            if (t >= 1f || c == _selected)
            {
                c.dealTime = -1f;
                c.rt.anchoredPosition = c.targetPos;
                c.rt.localRotation = Quaternion.Euler(0f, 0f, c.targetRot);
                c.rt.localScale = Vector3.one;
                c.group.alpha = c.targetAlpha;
                return;
            }

            Vector2 peak = c.targetPos + new Vector2(0f, 10f);
            Vector2 pos;
            float rot, scale;
            if (t < 0.78f)
            {
                float u = DealEase(t / 0.78f);
                pos = Vector2.LerpUnclamped(DealFrom, peak, u);
                rot = Mathf.LerpUnclamped(0f, c.targetRot * 1.3f, u);
                scale = Mathf.LerpUnclamped(0.7f, 1.04f, u);
            }
            else
            {
                float u = DealEase((t - 0.78f) / 0.22f);
                pos = Vector2.LerpUnclamped(peak, c.targetPos, u);
                rot = Mathf.LerpUnclamped(c.targetRot * 1.3f, c.targetRot, u);
                scale = Mathf.LerpUnclamped(1.04f, 1f, u);
            }
            c.rt.anchoredPosition = pos;
            c.rt.localRotation = Quaternion.Euler(0f, 0f, rot);
            c.rt.localScale = Vector3.one * scale;
            c.group.alpha = t < 0.12f ? DealEase(t / 0.12f) : 1f;
        }

        static float DealEase(float x) => CubicBezier(0.2f, 0.8f, 0.3f, 1f, x);

        /// <summary>CSS cubic-bezier(x1,y1,x2,y2)：給時間比例 x 回傳進度。</summary>
        static float CubicBezier(float x1, float y1, float x2, float y2, float x)
        {
            x = Mathf.Clamp01(x);
            float t = x;
            for (int i = 0; i < 8; i++)
            {
                float cx = Bez(x1, x2, t) - x;
                if (Mathf.Abs(cx) < 1e-4f) break;
                float d = BezD(x1, x2, t);
                if (Mathf.Abs(d) < 1e-6f) break;
                t = Mathf.Clamp01(t - cx / d);
            }
            return Bez(y1, y2, t);
        }

        static float Bez(float p1, float p2, float t) => 3f * (1 - t) * (1 - t) * t * p1 + 3f * (1 - t) * t * t * p2 + t * t * t;
        static float BezD(float p1, float p2, float t) => 3f * (1 - t) * (1 - t) * p1 + 6f * (1 - t) * t * (p2 - p1) + 3f * t * t * (1 - p2);

        void AnimatePopup()
        {
            if (!_popup.gameObject.activeSelf) return;
            _popupTime += Time.unscaledDeltaTime;
            // 出現：0.6 → 1 彈出；之後 3 秒一次輕輕上下浮動（設計檔 c5Float）
            float pop = Mathf.Clamp01(_popupTime / 0.25f);
            float scale = Mathf.Lerp(0.6f, 1f, 1f - (1f - pop) * (1f - pop));
            float phase = 0.5f - 0.5f * Mathf.Cos(_popupTime * Mathf.PI * 2f / 3f);
            _popup.localScale = Vector3.one * scale;
            _popup.anchoredPosition = PopupCenter + new Vector2(0f, 7f * phase);
            _popup.localRotation = Quaternion.Euler(0f, 0f, 2f - phase);
        }

        void UpdateEmptyState()
        {
            if (_empty == null) return;
            bool show = _dealt && _photoCards.Count == 0;
            _empty.gameObject.SetActive(show);
            if (!show) return;

            if (!_loadedOnce)
            {
                _emptyText.text = "讀取相簿中…";
                _emptySettingsButton.SetActive(false);
            }
            else if (Access == PhotoAccess.Denied)
            {
                _emptyText.text = "需要相簿權限\n才能用照片出牌";
                _emptySettingsButton.SetActive(true);
            }
            else
            {
                _emptyText.text = $"最近 {recentDays} 天沒有新照片\n用相機拍一張吧！";
                _emptySettingsButton.SetActive(false);
            }
        }

        // ─── 底部三顆 ──────────────────────────────────────────────────

        void UpdateBar(bool cooling)
        {
            // 出牌時往下 160、淡出（0.35 秒 ease）
            _barOut = Mathf.MoveTowards(_barOut, _dealt ? 1f : 0f, Time.unscaledDeltaTime / BarTime);
            float e = _barOut * _barOut * (3f - 2f * _barOut);
            _bar.anchoredPosition = new Vector2(50f, -664f - BarDrop * e);
            _barGroup.alpha = 1f - e;
            _barGroup.blocksRaycasts = !_dealt && _barOut < 0.5f;

            // 冷卻中相機變淡、不能按（共用出牌冷卻）；按鈕下方只放倒數時間（2026-11-06 使用者決定）
            _barCameraGroup.alpha = cooling ? 0.5f : 1f;
            if (_cameraCountdown.gameObject.activeSelf != cooling) _cameraCountdown.gameObject.SetActive(cooling);
            if (cooling) _cameraCountdown.text = PhotoCooldown.RemainingText;

            int unread = BlockPet.UI.Chat.ChatScreenController.UnreadCount();
            if (unread != _shownUnread)
            {
                _shownUnread = unread;
                _chatBadge.gameObject.SetActive(unread > 0);
                _chatBadgeText.text = unread > 99 ? "99" : unread.ToString();
            }
        }

        // ─── Interaction ───────────────────────────────────────────────

        void Select(Card card)
        {
            if (!_dealt || PhotoCooldown.IsActive) return;
            if (!BlockPet.Feed.FeedGate.Check()) return; // 昏倒中不能出牌，先復活
            bool wasOpen = _selected != null;
            _selected = card;

            _popupPhoto.texture = card.texture;
            _popupPhoto.uvRect = AspectFillUv(card.texture.width, card.texture.height, 210f, 140f);
            _popupCaption.text = DateCaption(card.photo);
            _feedLabel.text = "餵給 " + PetName();
            _popup.gameObject.SetActive(true);
            if (!wasOpen) _popupTime = 0f;

            if (!_petSummoned && petWaitPoint != null && AIController.Instance != null)
                _petSummoned = AIController.Instance.GoTo(petWaitPoint.position, null);

            LayoutCards();
        }

        /// <summary>把浮起的照片放回手牌。</summary>
        public void PutBack()
        {
            _selected = null;
            _popup.gameObject.SetActive(false);
            ReleasePet();
            LayoutCards();
        }

        void OnFeedClicked()
        {
            var card = _selected;
            if (card == null) return;
            if (!BlockPet.Feed.FeedGate.Check()) { PutBack(); return; }

            _selected = null;
            _popup.gameObject.SetActive(false);

            // 牌從手牌中間往上飛給寵物（寵物先留在原地等，吃完才放開）
            StartFlight(card.photo, card.texture);

            MarkPlayed(card.photo.id);
            _photoCards.Remove(card);
            card.texture = null;
            Destroy(card.rt.gameObject);

            PhotoCooldown.Start();
            _wasCooling = true;
            Collect();
        }

        // ─── 出牌：牌飛向寵物、吃掉、存進相簿 ───────────────────────────

        // 設計稿 hFlyUp：牌左上 (149, 610)，0.6 秒 ease-in 上移 250、轉 12°、縮到 0.3、淡出
        static readonly Vector2 FlightFrom = new Vector2(149f + 46f, -(610f + 62f));

        void StartFlight(HandPhoto photo, Texture2D tex)
        {
            var rt = Place(_design, "FeedFlight", 0, 0, 92, 124);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = FlightFrom;
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            ShadowLayer(rt, Skin(s => s.cardShadow), 24, CardShadow, 10, 12, 6);
            Fill(Stretch(rt, "Bg"), Skin(s => s.card), true, Cream, 10).raycastTarget = false;
            MaskedPhoto(rt, 5, 5, 82, 82, Skin(s => s.maskR6), 6, tex);
            rt.SetAsLastSibling();
            _flights.Add(new Flight { rt = rt, group = group, photo = photo, texture = tex, from = FlightFrom });
        }

        void AnimateFlights()
        {
            for (int i = _flights.Count - 1; i >= 0; i--)
            {
                var f = _flights[i];
                f.time += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(f.time / FlightTime);
                float u = CubicBezier(0.42f, 0f, 1f, 1f, t); // ease-in
                f.rt.anchoredPosition = f.from + new Vector2(0f, 250f * u);
                f.rt.localRotation = Quaternion.Euler(0f, 0f, -12f * u); // CSS 順時針 12°
                f.rt.localScale = Vector3.one * Mathf.Lerp(1f, 0.3f, u);
                f.group.alpha = 1f - u;

                if (t >= 1f)
                {
                    _flights.RemoveAt(i);
                    Destroy(f.rt.gameObject);
                    CompleteFeed(f.photo, f.texture);
                }
            }
        }

        /// <summary>寵物身體中心（top：頭頂）在設計座標（_design 本地座標）的位置。</summary>
        Vector3? PetPointInDesign(bool top = false)
        {
            var b = PetRectInDesign();
            if (!b.HasValue) return null;
            var r = b.Value;
            return top ? new Vector3(r.center.x, r.yMax, 0f) : new Vector3(r.center.x, r.center.y, 0f);
        }

        /// <summary>寵物圖在設計座標（_design 本地座標，y 向上）的範圍。</summary>
        Rect? PetRectInDesign()
        {
            var pet = AIController.Instance;
            var cam = Camera.main;
            if (pet == null || cam == null || !pet.gameObject.activeInHierarchy) return null;
            var r = pet.GetComponentInChildren<Renderer>();
            if (r != null && !r.enabled) return null; // 寵物出去旅行時圖被藏起來
            Bounds bounds = r != null ? r.bounds : new Bounds(pet.transform.position, Vector3.one);
            var canvas = _design.GetComponentInParent<Canvas>().rootCanvas;
            var uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector3 s0 = cam.WorldToScreenPoint(bounds.min);
            Vector3 s1 = cam.WorldToScreenPoint(bounds.max);
            if (s0.z < 0f || s1.z < 0f) return null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_design, s0, uiCam, out var a)) return null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_design, s1, uiCam, out var b)) return null;
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        void CompleteFeed(HandPhoto photo, Texture2D thumb)
        {
            // 寵物吃掉（同相機餵食的 Eat 動畫）
            if (feedController != null && feedController.petAnimator != null)
                feedController.petAnimator.SetTrigger("Eat");
            ReleasePet();

            // 照片辨識：縮圖就夠用，比原圖快
            var vision = BlockPet.Vision.PhotoClassifier.Instance != null ? BlockPet.Vision.PhotoClassifier.Instance.Classify(thumb) : default;
            if (vision.Ok) Debug.Log($"[HandController] 照片辨識：{vision.category}（{vision.confidence:P0}）");

            // 存進相簿：用較大的原圖（縮圖只有 thumbnailMaxSize）；SavePhoto 也會自動分享到營地
            Texture2D full = null;
            try { full = NativeGallery.LoadImageAtPath(photo.path, AlbumPhotoMaxSize, false, false); }
            catch (Exception e) { Debug.LogWarning("[HandController] 讀取原圖失敗，改存縮圖: " + e.Message); }
            if (SaveManager.Instance != null) SaveManager.Instance.SavePhoto(full != null ? full : thumb, vision.category, vision.confidence);
            if (full != null) Destroy(full);

            if (OnFeedRequested != null)
                OnFeedRequested.Invoke(photo, thumb);
            else if (thumb != null)
                Destroy(thumb);
        }

        void OnAlbumClicked() => cameraUI?.OpenAlbumFromHand();

        void OnCameraClicked()
        {
            if (PhotoCooldown.IsActive) return;
            if (!BlockPet.Feed.FeedGate.Check()) return;
            cameraUI?.OpenCameraFromHand();
        }

        void OnChatClicked() => cameraUI?.OpenChatFromHand();

        void ReleasePet()
        {
            if (!_petSummoned) return;
            _petSummoned = false;
            AIController.Instance?.ReleaseBusy();
        }

        // ─── UI construction（座標皆為設計檔 390×772 的 px，左上為原點）──
        // 有素材時用設計稿切出來的 sprite；沒有時用 UIShapes 程式產生的圓角近似。

        void BuildOverlay()
        {
            var canvas = GetComponentInParent<Canvas>();
            Transform parent = canvas != null ? canvas.rootCanvas.transform : transform;

            _overlay = Stretch(parent, "HandOverlay (runtime)");
            _overlay.SetAsLastSibling();
            _overlayGroup = _overlay.gameObject.AddComponent<CanvasGroup>();

            // 設計座標根：390×772，貼齊畫面底部置中，依螢幕寬度等比縮放
            var go = new GameObject("Design", typeof(RectTransform));
            _design = (RectTransform)go.transform;
            _design.SetParent(_overlay, false);
            _design.anchorMin = _design.anchorMax = new Vector2(0.5f, 0f);
            _design.pivot = new Vector2(0.5f, 0f);
            _design.sizeDelta = new Vector2(DesignW, DesignH);
            _design.anchoredPosition = Vector2.zero;
        }

        void BuildBackdrop()
        {
            // 手牌後面 y 560 以下：透明 → rgba(20,20,10,0.35) 的漸層；點它＝收回。往下多延伸，蓋到比設計稿高的螢幕底部
            _backdrop = Place(_design, "DealBackdrop", -200, 560, DesignW + 400, DesignH - 560f + 200f);
            _backdropGroup = _backdrop.gameObject.AddComponent<CanvasGroup>();
            _backdropGroup.alpha = 0f;
            var img = _backdrop.gameObject.AddComponent<RawImage>();
            img.texture = GradientTexture();
            float h = DesignH - 560f;
            img.uvRect = new Rect(0f, -200f / h, 1f, (h + 200f) / h); // 漸層只在 560～772，下面延伸部分維持最深
            var b = _backdrop.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(Collect);
            _backdrop.gameObject.SetActive(false);

            _hint = Label(_design, 0, 584, DesignW, 18, "", 12, Color.white, TextAlignmentOptions.Center, onScene: true);
            _hint.enableWordWrapping = false;
            _hint.gameObject.SetActive(false);
        }

        static Texture2D _gradient;

        static Texture2D GradientTexture()
        {
            if (_gradient != null) return _gradient;
            const int n = 64;
            _gradient = new Texture2D(1, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int i = 0; i < n; i++)
            {
                // y=0 是底部（最深）
                float a = 0.35f * (1f - i / (n - 1f));
                _gradient.SetPixel(0, i, new Color(20f / 255f, 20f / 255f, 10f / 255f, a));
            }
            _gradient.Apply();
            return _gradient;
        }

        void BuildEmptyState()
        {
            _empty = Place(_design, "Empty", 109, 628, 172, 96);
            Fill(_empty, Skin(s => s.glassPill), true, Glass, 16).raycastTarget = false;
            _emptyText = Label(_empty, 10, 10, 152, 44, "", 12, Ink, TextAlignmentOptions.Center);

            var btn = Place(_empty, "OpenSettings", 26, 58, 120, 30);
            var bg = Fill(btn, Skin(s => s.pillYellow), true, Yellow, 15);
            Label(btn, 0, 0, 120, 30, "開啟設定", 12, Ink, TextAlignmentOptions.Center);
            var b = btn.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            b.onClick.AddListener(PhotoLibrary.OpenSettings);
            _emptySettingsButton = btn.gameObject;
            _empty.gameObject.SetActive(false);
        }

        Card NewCard(string name)
        {
            // 卡片：92×124，旋轉中心在底邊中央；陰影 0 6 14 rgba(30,30,20,.35)
            var rt = Place(_cardsRoot, name, 0, 0, 92, 124);
            rt.pivot = new Vector2(0.5f, 0f);
            var card = new Card { rt = rt, group = rt.gameObject.AddComponent<CanvasGroup>() };
            ShadowLayer(rt, Skin(s => s.cardShadow), 24, CardShadow, 10, 12, 6);
            return card;
        }

        Card BuildPhotoCard(HandPhoto photo, Texture2D tex)
        {
            var card = NewCard("PhotoCard");
            card.photo = photo;
            card.texture = tex;

            var bgImg = Fill(Stretch(card.rt, "Bg"), Skin(s => s.card), true, Cream, 10);
            MaskedPhoto(card.rt, 5, 5, 82, 82, Skin(s => s.maskR6), 6, tex);
            Label(card.rt, 6, 92, 80, 16, DateCaption(photo), 10, Ink, TextAlignmentOptions.Left);

            var b = card.rt.gameObject.AddComponent<Button>();
            b.targetGraphic = bgImg;
            b.onClick.AddListener(() => Select(card));

            card.rt.anchoredPosition = DealFrom;
            card.group.alpha = 0f;
            return card;
        }

        static readonly Vector2 PopupCenter = new Vector2(80f + 115f, -(216f + 120.5f));

        void BuildPopup()
        {
            // 浮起的照片：230 寬、內距 10，照片 210×140 圓角 10，下方說明與兩顆按鈕（放回 : 餵給 = 1 : 1.5、間距 8）
            _popup = Place(_design, "RaisedPhoto", 80, 216, 230, 241);
            _popup.pivot = new Vector2(0.5f, 0.5f);
            _popup.anchoredPosition = PopupCenter;
            ShadowLayer(_popup, Skin(s => s.popupShadow), 48, new Color(0f, 0f, 0f, 0.35f), 16, 22, 16);
            Fill(Stretch(_popup, "Bg"), Skin(s => s.popup), true, Cream, 16);

            _popupPhoto = MaskedPhoto(_popup, 10, 10, 210, 140, Skin(s => s.maskR10), 10, null);
            _popupCaption = Label(_popup, 12, 156, 206, 22, "", 15, Ink, TextAlignmentOptions.Left);

            var back = Place(_popup, "PutBack", 10, 187, 80.8f, 44);
            var backImg = Fill(back, Skin(s => s.buttonBeige), true, Beige, 13);
            Label(back, 0, 0, 80.8f, 44, "放回", 14, Ink, TextAlignmentOptions.Center);
            var backBtn = back.gameObject.AddComponent<Button>();
            backBtn.targetGraphic = backImg;
            backBtn.onClick.AddListener(PutBack);

            var feed = Place(_popup, "Feed", 98.8f, 187, 121.2f, 44);
            Image feedImg;
            var yellow = Skin(s => s.buttonYellow);
            if (yellow != null)
            {
                // 圖含下緣 3 的立體邊：圖比按鈕高 3
                feedImg = Fill(Place(feed, "Bg", 0, 0, 121.2f, 47), yellow, true, Yellow, 13);
            }
            else
            {
                UIShapes.AddRounded(Place(feed, "Edge", 0, 3, 121.2f, 44).gameObject, YellowShadow, 13).raycastTarget = false;
                feedImg = UIShapes.AddRounded(Stretch(feed, "Bg").gameObject, Yellow, 13);
            }
            _feedLabel = Label(feed, 0, 0, 121.2f, 44, "餵給寵物", 14, Ink, TextAlignmentOptions.Center);
            var feedBtn = feed.gameObject.AddComponent<Button>();
            feedBtn.targetGraphic = feedImg;
            feedBtn.onClick.AddListener(OnFeedClicked);

            _popup.gameObject.SetActive(false);
        }

        void BuildBar()
        {
            // 設計文件 5.25 ②：區塊 (50, 664) 寬 290，三顆垂直置中、左右撐開，不放字
            _bar = Place(_design, "BottomBar", 50, 664, 290, 72);
            _barGroup = _bar.gameObject.AddComponent<CanvasGroup>();

            // 相簿 50 圓：毛玻璃底＋一疊拍立得圖示 28；陰影 0 3 8 rgba(30,40,20,0.22)
            var album = Place(_bar, "Album", 0, 11, 50, 50);
            var albumBg = RoundButton(album);
            LineIcon(album, H9(s => s.iconAlbum), "AlbumIcon");
            var albumBtn = album.gameObject.AddComponent<Button>();
            albumBtn.targetGraphic = albumBg;
            albumBtn.onClick.AddListener(OnAlbumClicked);
            album.gameObject.AddComponent<ButtonScalePress>();

            // 相機 72 圓（圖含白邊與相機圖示）；陰影 0 6 14 rgba(30,30,20,0.4)
            var cam = Place(_bar, "Camera", 109, 0, 72, 72);
            _barCameraGroup = cam.gameObject.AddComponent<CanvasGroup>();
            UIShapes.AddShadow(cam, new Color(0.12f, 0.12f, 0.08f, 0.4f), 36, 14, 6);
            Image camImg;
            var camSprite = H9(s => s.buttonCamera);
            if (camSprite != null) camImg = Fill(Stretch(cam, "Bg"), camSprite, false, Coral, 36);
            else
            {
                camImg = UIShapes.AddRounded(Stretch(cam, "Rim").gameObject, Cream, 36);
                UIShapes.AddRounded(Place(cam, "Fill", 4, 4, 64, 64).gameObject, Coral, 32).raycastTarget = false;
                CameraIcon(cam, 20, 20, 32);
            }
            _cameraCountdown = Label(_bar, 99, 74, 92, 16, "", 12, Color.white, TextAlignmentOptions.Center, onScene: true);
            _cameraCountdown.enableWordWrapping = false;
            _cameraCountdown.gameObject.SetActive(false);
            var camBtn = cam.gameObject.AddComponent<Button>();
            camBtn.targetGraphic = camImg;
            camBtn.onClick.AddListener(OnCameraClicked);
            cam.gameObject.AddComponent<ButtonScalePress>();

            // 聊天 50 圓：兩個對話泡泡圖示 28；未讀 18 紅圓貼在右上 (−4, −4)
            var chat = Place(_bar, "Chat", 240, 11, 50, 50);
            var chatBg = RoundButton(chat);
            if (H9(s => s.iconChat) != null) LineIcon(chat, H9(s => s.iconChat), "ChatIcon");
            else ChatIcon(chat, 11, 11);
            var chatBtn = chat.gameObject.AddComponent<Button>();
            chatBtn.targetGraphic = chatBg;
            chatBtn.onClick.AddListener(OnChatClicked);
            chat.gameObject.AddComponent<ButtonScalePress>();

            _chatBadge = Place(chat, "Badge", 36, -4, 18, 18);
            var badgeBg = H9(s => s.badgeRed);
            if (badgeBg != null) Fill(Stretch(_chatBadge, "Bg"), badgeBg, false, Coral, 9).raycastTarget = false;
            else
            {
                UIShapes.AddRounded(Stretch(_chatBadge, "Rim").gameObject, Color.white, 9).raycastTarget = false;
                UIShapes.AddRounded(Place(_chatBadge, "Fill", 2, 2, 14, 14).gameObject, Coral, 7).raycastTarget = false;
            }
            _chatBadgeText = Label(_chatBadge, -4, 0, 26, 18, "", 10, Color.white, TextAlignmentOptions.Center);
            _chatBadgeText.enableWordWrapping = false;
            _chatBadge.gameObject.SetActive(false);
        }

        /// <summary>相簿／聊天的 50 圓毛玻璃底（素材沒含陰影，另外加 0 3 8）。</summary>
        Image RoundButton(RectTransform circle)
        {
            UIShapes.AddShadow(circle, new Color(0.12f, 0.16f, 0.08f, 0.22f), 25, 8, 3);
            var sprite = H9(s => s.buttonRound);
            if (sprite != null) return Fill(Stretch(circle, "Bg"), sprite, false, Glass, 25);
            var img = UIShapes.AddRounded(Stretch(circle, "Bg").gameObject, Glass, 25);
            UIShapes.AddThinRing(Stretch(circle, "Edge").gameObject, new Color(1f, 1f, 1f, 0.85f), 25).raycastTarget = false;
            return img;
        }

        /// <summary>圓鈕中間的 28 線條圖示。</summary>
        static void LineIcon(RectTransform circle, Sprite icon, string name)
        {
            if (icon == null) return;
            var rt = Place(circle, name, 11, 11, 28, 28);
            Fill(rt, icon, false, Ink, 0).raycastTarget = false;
        }

        // ─── Small builders ────────────────────────────────────────────

        Sprite H9(Func<Home9Skin, Sprite> pick) => _h9 != null ? pick(_h9) : null;

        Sprite Skin(Func<HandSkin, Sprite> pick) => skin != null ? pick(skin) : null;

        /// <summary>有素材用素材（9-slice 或原尺寸），沒有就用程式圓角。</summary>
        static Image Fill(RectTransform rt, Sprite sprite, bool sliced, Color fallbackColor, float fallbackRadius)
        {
            if (sprite == null) return UIShapes.AddRounded(rt.gameObject, fallbackColor, fallbackRadius);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            return img;
        }

        /// <summary>陰影層：素材是「形狀＋四邊外擴 pad」，所以撐滿後四邊再外擴 pad。要在底圖之前建立（畫在下層）。</summary>
        static void ShadowLayer(RectTransform parent, Sprite sprite, float pad, Color fbColor, float fbRadius, float fbBlur, float fbOffset)
        {
            if (sprite == null)
            {
                UIShapes.AddShadow(parent, fbColor, fbRadius, fbBlur, fbOffset);
                return;
            }
            var rt = Stretch(parent, "Shadow");
            rt.offsetMin = new Vector2(-pad, -pad);
            rt.offsetMax = new Vector2(pad, pad);
            var img = Fill(rt, sprite, true, fbColor, fbRadius);
            img.raycastTarget = false;
        }

        static void Rotate(RectTransform rt, float degreesUnity)
        {
            // 以中心旋轉（CSS transform-origin 預設中心）；Unity 正角度 = CSS 逆時針
            Vector2 center = rt.anchoredPosition + new Vector2(rt.sizeDelta.x / 2f, -rt.sizeDelta.y / 2f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = center;
            rt.localRotation = Quaternion.Euler(0f, 0f, degreesUnity);
        }

        /// <summary>設計檔的相機圖示（40×40 viewBox），size 為顯示大小。</summary>
        void CameraIcon(RectTransform parent, float left, float top, float size)
        {
            var icon = Skin(s => s.iconCamera);
            if (icon != null)
            {
                Fill(Place(parent, "CameraIcon", left, top, size, size), icon, false, Color.white, 0).raycastTarget = false;
                return;
            }
            var root = Place(parent, "CameraIcon", left, top, size, size);
            float k = size / 40f;
            Shape(root, 4, 11, 32, 23, 6, Color.white, k);       // 機身
            Shape(root, 15, 6, 10, 7, 2, Color.white, k);        // 上方凸起
            Shape(root, 13.5f, 15.5f, 13, 13, 6.5f, Coral, k);   // 鏡頭
            Shape(root, 16.4f, 18.4f, 7.2f, 7.2f, 3.6f, Color.white, k);
        }

        /// <summary>設計檔的聊天圖示（30×30 viewBox，顯示 28×28）。</summary>
        void ChatIcon(RectTransform parent, float left, float top)
        {
            var icon = Skin(s => s.iconChat);
            if (icon != null)
            {
                Fill(Place(parent, "ChatIcon", left, top, 28, 28), icon, false, Ink, 0).raycastTarget = false;
                return;
            }
            var root = Place(parent, "ChatIcon", left, top, 28, 28);
            float k = 28f / 30f;
            Shape(root, 3, 6, 24, 16, 2, Ink, k);
            var tail = Shape(root, 7, 18, 7, 7, 1, Ink, k);
            tail.pivot = new Vector2(0.5f, 0.5f);
            tail.anchoredPosition = new Vector2(10.5f * k, -21.5f * k);
            tail.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var dot = Hex(0xFFF8EC);
            Shape(root, 8.4f, 12.4f, 3.2f, 3.2f, 1.6f, dot, k);
            Shape(root, 13.4f, 12.4f, 3.2f, 3.2f, 1.6f, dot, k);
            Shape(root, 18.4f, 12.4f, 3.2f, 3.2f, 1.6f, dot, k);
        }

        static RectTransform Shape(RectTransform parent, float x, float y, float w, float h, float r, Color c, float k)
        {
            var rt = Place(parent, "Shape", x * k, y * k, w * k, h * k);
            UIShapes.AddRounded(rt.gameObject, c, r * k).raycastTarget = false;
            return rt;
        }

        static RawImage MaskedPhoto(RectTransform parent, float left, float top, float w, float h, Sprite mask, float radius, Texture2D tex)
        {
            var frame = Place(parent, "PhotoFrame", left, top, w, h);
            Fill(frame, mask, true, Color.white, radius).raycastTarget = false;
            frame.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var img = Stretch(frame, "Photo").gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            img.texture = tex;
            if (tex != null) img.uvRect = AspectFillUv(tex.width, tex.height, w, h);
            return img;
        }

        /// <summary>文字。onScene：直接疊在營地背景上的白字，加陰影（設計稿 text-shadow）。</summary>
        TMP_Text Label(RectTransform parent, float left, float top, float w, float h, string value, float size,
                       Color color, TextAlignmentOptions align, bool onScene = false)
        {
            var rt = Place(parent, "Text", left, top, w, h);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            var f = skin != null && skin.font != null ? skin.font : font;
            if (f != null) t.font = f;
            t.text = value;
            t.fontSize = size;
            t.color = color;
            // Noto Sans TC Black 本身就是粗體；只有退回其他字型時才加粗
            t.fontStyle = skin != null && skin.font != null ? FontStyles.Normal : FontStyles.Bold;
            t.alignment = align;
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            if (onScene)
            {
                if (skin != null && skin.labelShadowMaterial != null)
                    t.fontSharedMaterial = skin.labelShadowMaterial;
                else
                {
                    t.outlineWidth = 0.18f;
                    t.outlineColor = new Color32(0, 0, 0, 150);
                }
            }
            return t;
        }

        /// <summary>在 parent 裡以左上角為原點擺一個 w×h 的方塊（設計檔座標）。</summary>
        static RectTransform Place(Transform parent, string name, float left, float top, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(left, -top);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        static RectTransform Stretch(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>讓照片以「填滿裁切」方式顯示在框裡。</summary>
        static Rect AspectFillUv(float texW, float texH, float boxW, float boxH)
        {
            float texAspect = texW / texH;
            float boxAspect = boxW / boxH;
            if (texAspect > boxAspect)
            {
                float w = boxAspect / texAspect;
                return new Rect((1f - w) / 2f, 0f, w, 1f);
            }
            float h = texAspect / boxAspect;
            return new Rect(0f, (1f - h) / 2f, 1f, h);
        }

        static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
