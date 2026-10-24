using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BlockPet.UI.TopBar;

namespace BlockPet.Hand
{
    /// <summary>
    /// 首頁手牌，依設計檔「首頁 v8」：
    /// - 底部扇形手牌：最左是「拍照」牌，後面是相簿最新 5 張相機照片（出過的不再出現），越新越右邊。
    /// - 往左滑收起手牌（底部換成相簿／拍照／聊天，左側留「手牌 ›」小標籤），往右滑或點標籤展開。
    /// - 點照片 → 照片浮起成小卡（放回／餵給寵物），寵物走到下方等待。
    /// - 餵給寵物 → 照片縮小飛向寵物後消失、寵物吃掉；照片存進相簿（同相機餵食，也會自動分享到營地）。
    /// - 出牌（或相機拍照餵食）後進入共用冷卻（<see cref="PhotoCooldown"/>），手牌收起，
    ///   底部變成「相簿／拍照／聊天」；相簿打開遊戲裡的相簿頁，拍照在冷卻中顯示倒數不能按，
    ///   冷卻結束後手牌自動展開。
    /// 介面在執行時以 390×772 的設計座標建立（直接對照設計檔），再依螢幕寬度等比放大、貼齊畫面底部。
    /// 餵食效果（辨識、結算）在第 3 步透過 <see cref="OnFeedRequested"/> 接上。
    /// </summary>
    public class HandController : MonoBehaviour
    {
        const float DesignW = 390f;
        const float DesignH = 772f;
        const string PrefsPlayedIds = "BlockPet.Hand.PlayedIds";
        const int MaxPlayedIds = 100;
        const float CollapseShift = 320f;   // 收起時手牌往左移多少（移出畫面）
        const float SwipeThreshold = 50f;   // 滑多遠才算收起／展開
        const float FlightTime = 0.6f;      // 照片飛向寵物的時間
        const int AlbumPhotoMaxSize = 1280; // 存進相簿的照片最長邊

        static readonly Color Ink = Hex(0x3A2E22);
        static readonly Color Cream = Hex(0xFFFCF4);
        static readonly Color Coral = Hex(0xE86B4F);
        static readonly Color Yellow = Hex(0xF6C945);
        static readonly Color YellowShadow = Hex(0xD9A92C);
        static readonly Color Beige = Hex(0xF2E9D8);
        static readonly Color Glass = new Color(1f, 0.973f, 0.925f, 0.72f);
        static readonly Color CardShadow = new Color(0.12f, 0.12f, 0.08f, 0.35f);
        static readonly Color Muted = Hex(0x8A7A66);

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
        /// 按下「餵給寵物」時觸發，參數為照片與縮圖貼圖。第 3 步接上辨識與結算。
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
        }

        // 版面
        RectTransform _overlay;
        CanvasGroup _overlayGroup;
        RectTransform _design;
        RectTransform _cardsRoot;
        float _lastWidth = -1f;

        // 手牌
        readonly List<Card> _photoCards = new List<Card>();
        Card _cameraCard;
        TMP_Text _cameraCardLabel;
        TMP_Text _countLabel;
        Card _selected;

        // 浮起的照片
        RectTransform _popup;
        RawImage _popupPhoto;
        TMP_Text _popupCaption;
        TMP_Text _feedLabel;
        float _popupTime;

        // 出牌後的相簿／拍照／聊天
        RectTransform _dock;
        CanvasGroup _dockGroup;
        TMP_Text _dockHint;
        RawImage _albumThumb;
        CanvasGroup _dockCameraGroup;
        TMP_Text _dockCameraLabel;

        // 營地最新照片卡（手牌最右）
        RectTransform _latest;
        CanvasGroup _latestGroup;
        RawImage _latestPhoto;
        Image _latestGlowLo;
        Image _latestGlowHi;
        Image _latestPosterBg;
        TMP_Text _latestPoster;
        TMP_Text _latestName;
        TMP_Text _latestCount;
        RectTransform _latestBadge;
        Texture2D _latestTexture;
        string _latestFile;
        int _latestCountValue = -1;
        float _latestTime;
        float _nextLatestPoll;

        // 空狀態
        RectTransform _empty;
        TMP_Text _emptyText;
        GameObject _emptySettingsButton;

        // 左右滑收起／展開
        bool _collapsed;
        float _swipeOffset;
        RectTransform _handTab;
        TMP_Text _handTabLabel;
        Image _swipeZone;

        // 飛向寵物的照片
        class Flight
        {
            public RectTransform rt;
            public CanvasGroup group;
            public HandPhoto photo;
            public Texture2D texture;
            public Vector3 from;
            public float time;
        }
        readonly List<Flight> _flights = new List<Flight>();

        bool _dockMode;
        /// <summary>底部顯示相簿／拍照／聊天：出牌後的冷卻中，或手牌被滑動收起時。</summary>
        bool DockVisible => _dockMode || _collapsed;
        bool _wasCooling;
        bool _loading;
        bool _loadedOnce;
        bool _petSummoned;
        readonly List<string> _playedIds = new List<string>();

        // ─── Lifecycle ─────────────────────────────────────────────────

        void Awake()
        {
            if (cameraUI == null) cameraUI = FindObjectOfType<CameraUIManager>(true);
            if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
            if (feedController == null) feedController = FindObjectOfType<FeedController>(true);
            LoadPlayedIds();

            BuildOverlay();
            BuildCountLabel();
            BuildEmptyState();
            _cardsRoot = Stretch(_design, "Cards");
            BuildSwipe();
            BuildCameraCard();
            BuildLatest();
            BuildDock();
            BuildPopup();
            LayoutCards(snap: true);
        }

        void OnEnable()
        {
            if (_overlay != null) _overlay.gameObject.SetActive(true);
            if (cameraUI != null) cameraUI.hideHomeButtons = true;
            Refresh();
        }

        void OnDisable()
        {
            if (_selected != null) PutBack();
            if (_overlay != null) _overlay.gameObject.SetActive(false);
            if (cameraUI != null) cameraUI.hideHomeButtons = false;
        }

        void OnDestroy()
        {
            foreach (var c in _photoCards)
                if (c.texture != null) Destroy(c.texture);
            if (_latestTexture != null) Destroy(_latestTexture);
            foreach (var f in _flights) if (f.texture != null) Destroy(f.texture);
            _flights.Clear();
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
            bool home = (cameraUI == null || cameraUI.CurrentMode == UIMode.Home)
                        && (pageManager == null || pageManager.IsHomePageActive)
                        && !(decorMode != null && decorMode.hideHand && DecorModeSettings.IsDecorating);
            _overlayGroup.alpha = home ? 1f : 0f;
            _overlayGroup.blocksRaycasts = home;

            float w = _overlay.rect.width;
            if (w > 0f && !Mathf.Approximately(w, _lastWidth))
            {
                _lastWidth = w;
                _design.localScale = Vector3.one * (w / DesignW);
            }

            // 冷卻開始（出牌或相機拍照餵食）→ 收起手牌；冷卻結束 → 自動展開
            bool cooling = PhotoCooldown.IsActive;
            if (cooling && !_wasCooling) EnterDock();
            else if (!cooling && _dockMode) ExitDock();
            _wasCooling = cooling;

            UpdateDockState(cooling);
            if (_cameraCardLabel != null)
                _cameraCardLabel.text = cooling ? PhotoCooldown.RemainingText : "拍照";

            AnimateCards();
            AnimateFlights();
            AnimatePopup();
            AnimateDock();
            PollLatest();
            AnimateLatest();

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
                // 相簿是新到舊；手牌由左到右是舊到新，最新的在最右邊（緊鄰營地最新照片）
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

            RefreshAlbumThumb();
            LayoutCards();
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

        // ─── Layout & animation ────────────────────────────────────────

        void LayoutCards(bool snap = false)
        {
            // 設計檔：手牌中心 x=156（右邊留給營地最新照片），牌距最多 46、總寬 220，每張轉 6°
            var items = new List<Card> { _cameraCard };
            items.AddRange(_photoCards);
            int n = items.Count;
            float center = 156f;
            float sp = n > 1 ? Mathf.Min(46f, 220f / (n - 1)) : 0f;

            // 收起：整排往左移出畫面；拖曳中跟著手指
            float shift = (_collapsed ? -CollapseShift : 0f) + _swipeOffset;
            float visible = Mathf.Clamp01(1f + shift / CollapseShift);

            for (int i = 0; i < n; i++)
            {
                var c = items[i];
                float off = i - (n - 1) / 2f;
                float left = Mathf.Round(center + off * sp - 46f);
                float top = Mathf.Round(620f + Mathf.Abs(off) * 9f) + (_dockMode ? 200f : 0f);
                c.targetPos = new Vector2(left + 46f + shift, -(top + 124f));
                c.targetRot = -off * 6f;
                c.targetAlpha = _dockMode || c == _selected ? 0f : visible;
                bool interactable = !_dockMode && c != _selected && !_collapsed;
                c.group.blocksRaycasts = interactable;
                c.group.interactable = interactable;
                c.rt.SetSiblingIndex(i + 1); // 0 是滑動區，要在卡片後面

                if (snap)
                {
                    c.rt.anchoredPosition = c.targetPos;
                    c.rt.localRotation = Quaternion.Euler(0f, 0f, c.targetRot);
                    c.group.alpha = c.targetAlpha;
                }
            }

            if (_countLabel != null)
            {
                _countLabel.text = $"我的手牌 {_photoCards.Count} 張";
                _countLabel.gameObject.SetActive(!_dockMode && !_collapsed && _photoCards.Count > 0);
            }
            if (_swipeZone != null) _swipeZone.raycastTarget = !_dockMode; // 出牌後不擋住點營地
            if (_handTab != null)
            {
                _handTab.gameObject.SetActive(_collapsed && !_dockMode);
                _handTabLabel.text = $"手牌 {_photoCards.Count} ›";
            }
            UpdateEmptyState();
        }

        void AnimateCards()
        {
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 12f);
            AnimateCard(_cameraCard, k);
            foreach (var c in _photoCards) AnimateCard(c, k);
        }

        static void AnimateCard(Card c, float k)
        {
            c.rt.anchoredPosition = Vector2.Lerp(c.rt.anchoredPosition, c.targetPos, k);
            c.rt.localRotation = Quaternion.Slerp(c.rt.localRotation, Quaternion.Euler(0f, 0f, c.targetRot), k);
            c.group.alpha = Mathf.Lerp(c.group.alpha, c.targetAlpha, k);
        }

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

        void AnimateDock()
        {
            float target = DockVisible ? 1f : 0f;
            _dockGroup.alpha = Mathf.MoveTowards(_dockGroup.alpha, target, Time.unscaledDeltaTime / 0.35f);
            _dockGroup.blocksRaycasts = DockVisible;
            bool show = DockVisible || _dockGroup.alpha > 0.01f;
            if (_dock.gameObject.activeSelf != show) _dock.gameObject.SetActive(show);
        }

        void UpdateEmptyState()
        {
            if (_empty == null) return;
            bool show = !_dockMode && !_collapsed && _photoCards.Count == 0;
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

        // ─── Interaction ───────────────────────────────────────────────

        void OnCameraCardClicked()
        {
            if (_selected != null) PutBack();
            cameraUI?.OpenCameraFromHand();
        }

        void Select(Card card)
        {
            if (_dockMode || PhotoCooldown.IsActive) return;
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

            // 照片從浮起的位置縮小飛向寵物（寵物先留在原地等，吃完才放開）
            var photoRt = _popupPhoto.rectTransform;
            Vector3 from = _design.InverseTransformPoint(photoRt.TransformPoint(photoRt.rect.center));
            StartFlight(card.photo, card.texture, from);

            _selected = null;
            _popup.gameObject.SetActive(false);

            MarkPlayed(card.photo.id);
            _photoCards.Remove(card);
            card.texture = null;
            Destroy(card.rt.gameObject);
            RefreshAlbumThumb();

            PhotoCooldown.Start();
            _wasCooling = true;
            EnterDock();
        }

        // ─── 出牌：照片飛向寵物、吃掉、存進相簿 ─────────────────────────

        void StartFlight(HandPhoto photo, Texture2D tex, Vector3 from)
        {
            var rt = Place(_design, "FeedFlight", 0, 0, 222, 152);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localPosition = from;
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            Fill(Stretch(rt, "Bg"), Skin(s => s.popup), true, Cream, 12);
            MaskedPhoto(rt, 6, 6, 210, 140, Skin(s => s.maskR10), 10, tex);
            rt.SetAsLastSibling();
            _flights.Add(new Flight { rt = rt, group = group, photo = photo, texture = tex, from = from });
        }

        void AnimateFlights()
        {
            for (int i = _flights.Count - 1; i >= 0; i--)
            {
                var f = _flights[i];
                f.time += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(f.time / FlightTime);

                // 目標每幀重算（寵物可能還在走）；找不到寵物就往畫面中央下方縮小
                Vector3 to = PetPointInDesign() ?? new Vector3(0f, 300f, 0f);
                Vector3 control = (f.from + to) / 2f + new Vector3(0f, 90f, 0f); // 往上拋一點的弧線
                float u = t * t * (3f - 2f * t);
                Vector3 p = Vector3.Lerp(Vector3.Lerp(f.from, control, u), Vector3.Lerp(control, to, u), u);
                f.rt.localPosition = p;
                f.rt.localScale = Vector3.one * Mathf.Lerp(1f, 0.12f, t * t);
                f.rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, -25f, t));
                f.group.alpha = t < 0.8f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.8f) / 0.2f);

                if (t >= 1f)
                {
                    _flights.RemoveAt(i);
                    Destroy(f.rt.gameObject);
                    CompleteFeed(f.photo, f.texture);
                }
            }
        }

        /// <summary>寵物身體中心在設計座標（_design 本地座標）的位置。</summary>
        Vector3? PetPointInDesign()
        {
            var pet = AIController.Instance;
            var cam = Camera.main;
            if (pet == null || cam == null) return null;
            Vector3 world = pet.transform.position;
            var r = pet.GetComponentInChildren<Renderer>();
            if (r != null) world = r.bounds.center;
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0f) return null;
            var canvas = _design.GetComponentInParent<Canvas>().rootCanvas;
            var uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_design, screen, uiCam, out var local)) return null;
            return new Vector3(local.x, local.y, 0f);
        }

        void CompleteFeed(HandPhoto photo, Texture2D thumb)
        {
            // 寵物吃掉（同相機餵食的 Eat 動畫）
            if (feedController != null && feedController.petAnimator != null)
                feedController.petAnimator.SetTrigger("Eat");
            ReleasePet();

            // 存進相簿：用較大的原圖（縮圖只有 thumbnailMaxSize）；SavePhoto 也會自動分享到營地
            Texture2D full = null;
            try { full = NativeGallery.LoadImageAtPath(photo.path, AlbumPhotoMaxSize, false, false); }
            catch (Exception e) { Debug.LogWarning("[HandController] 讀取原圖失敗，改存縮圖: " + e.Message); }
            if (SaveManager.Instance != null) SaveManager.Instance.SavePhoto(full != null ? full : thumb);
            if (full != null) Destroy(full);

            if (OnFeedRequested != null)
                OnFeedRequested.Invoke(photo, thumb);
            else if (thumb != null)
                Destroy(thumb);
        }

        // ─── 左右滑收起／展開 ──────────────────────────────────────────

        void BuildSwipe()
        {
            // 滑動區：手牌那一帶（不含右邊的營地最新照片），放在卡片後面
            var zone = Place(_cardsRoot, "SwipeZone", 0, 560, 252, DesignH - 560f);
            _swipeZone = zone.gameObject.AddComponent<Image>();
            _swipeZone.color = new Color(1f, 1f, 1f, 0f);
            zone.SetAsFirstSibling();

            var swipe = _cardsRoot.gameObject.AddComponent<HandSwipe>();
            swipe.space = _design;
            swipe.canSwipe = () => !_dockMode && _selected == null;
            swipe.onDrag = dx =>
            {
                // 展開時只能往左拉、收起時只能往右拉（反方向留一點彈性）
                _swipeOffset = _collapsed ? Mathf.Clamp(dx, -20f, CollapseShift) : Mathf.Clamp(dx, -CollapseShift, 20f);
                LayoutCards();
            };
            swipe.onEnd = dx =>
            {
                if (!_collapsed && dx <= -SwipeThreshold) _collapsed = true;
                else if (_collapsed && dx >= SwipeThreshold) _collapsed = false;
                _swipeOffset = 0f;
                LayoutCards();
            };

            // 收起後左邊的小標籤（底部同時出現相簿／拍照／聊天）：點一下或往右滑展開
            // 放在相簿／拍照／聊天上方，不擋住相簿鈕
            _handTab = Place(_design, "HandTab", -16, 580, 96, 40);
            var tabImg = Fill(_handTab, Skin(s => s.glassPill), true, Glass, 20);
            _handTabLabel = Label(_handTab, 16, 0, 80, 40, "手牌 ›", 13, Ink, TextAlignmentOptions.Center);
            var tb = _handTab.gameObject.AddComponent<Button>();
            tb.targetGraphic = tabImg;
            tb.onClick.AddListener(() => { _collapsed = false; LayoutCards(); });
            var tabSwipe = _handTab.gameObject.AddComponent<HandSwipe>();
            tabSwipe.space = _design;
            tabSwipe.onEnd = dx => { if (dx >= SwipeThreshold) { _collapsed = false; LayoutCards(); } };
            _handTab.gameObject.SetActive(false);
        }

        void EnterDock()
        {
            if (_selected != null) PutBack();
            _dockMode = true;
            LayoutCards();
        }

        void ExitDock()
        {
            _dockMode = false;
            LayoutCards();
            Refresh();
        }

        void OnAlbumClicked() => cameraUI?.OpenAlbumFromHand();

        void OnDockCameraClicked()
        {
            if (PhotoCooldown.IsActive) return;
            cameraUI?.OpenCameraFromHand();
        }

        void OnChatClicked() => cameraUI?.OpenChatFromHand();

        /// <summary>最新照片卡：打開分割畫面（大照片＋留言＋縮小的營地）；沒有掛 PhotoSplitView 時改開相簿頁。</summary>
        void OnLatestClicked()
        {
            if (_selected != null) PutBack();
            if (_splitView == null) _splitView = GetComponent<PhotoSplitView>();
            if (_splitView == null) _splitView = FindObjectOfType<PhotoSplitView>(true);
            if (_splitView != null && _splitView.Open()) return;
            cameraUI?.OpenAlbumFromHand();
        }

        PhotoSplitView _splitView;

        // ─── 營地最新照片 ──────────────────────────────────────────────

        /// <summary>
        /// 讀相簿（SaveManager，含其他成員從營地同步下來的照片）最新一張與張數。
        /// 發布者名字目前照片資料沒有記錄，先顯示「營地」；第 7 步營地動態會補上。
        /// </summary>
        void PollLatest()
        {
            if (Time.unscaledTime < _nextLatestPoll) return;
            _nextLatestPoll = Time.unscaledTime + 1f;

            var photos = SaveManager.Instance?.data?.photos;
            int count = photos?.Count ?? 0;
            var last = count > 0 ? photos[count - 1] : null;
            string file = last?.fileName;

            if (file != _latestFile)
            {
                _latestFile = file;
                if (_latestTexture != null) Destroy(_latestTexture);
                _latestTexture = last != null ? SaveManager.Instance.LoadPhoto(last) : null;
                _latestPhoto.texture = _latestTexture;
                if (_latestTexture != null)
                    _latestPhoto.uvRect = AspectFillUv(_latestTexture.width, _latestTexture.height, 110f, 110f);
                _latestPoster.text = "營";
                _latestPosterBg.color = Hex(0x6FA45F);
            }
            if (count != _latestCountValue)
            {
                _latestCountValue = count;
                _latestCount.text = $"點開看・營地 {count} 張";
            }
            if (last != null)
                _latestName.text = $"營地 <size=10><color=#8A7A66>{Ago(last.timestamp)}</color></size>";

            bool show = last != null && _latestTexture != null;
            if (_latest.gameObject.activeSelf != show) _latest.gameObject.SetActive(show);
        }

        static string Ago(string timestamp)
        {
            if (!DateTime.TryParse(timestamp, out var t)) return "";
            var d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "剛剛";
            if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} 分前";
            if (d.TotalDays < 1) return $"{(int)d.TotalHours} 小時前";
            if (d.TotalDays < 2) return "昨天";
            return $"{(int)d.TotalDays} 天前";
        }

        void AnimateLatest()
        {
            if (_latest == null || !_latest.gameObject.activeSelf) return;
            // 手牌展開時才出現（設計稿 handOpen）
            float target = DockVisible ? 0f : 1f;
            _latestGroup.alpha = Mathf.MoveTowards(_latestGroup.alpha, target, Time.unscaledDeltaTime / 0.3f);
            _latestGroup.blocksRaycasts = !DockVisible;

            // 金框發光 c5Glow、「新」c5Pulse：1.8 秒一次
            _latestTime += Time.unscaledDeltaTime;
            float k = 0.5f - 0.5f * Mathf.Cos(_latestTime * Mathf.PI * 2f / 1.8f);
            if (_latestGlowLo != null) _latestGlowLo.color = new Color(1f, 1f, 1f, 1f - k);
            if (_latestGlowHi != null) _latestGlowHi.color = new Color(1f, 1f, 1f, k);
            if (_latestBadge != null) _latestBadge.localScale = Vector3.one * Mathf.Lerp(1f, 1.18f, k);
        }

        void UpdateDockState(bool cooling)
        {
            if (!DockVisible) return;
            _dockHint.text = cooling ? $"{PhotoCooldown.RemainingText} 後手牌回來・可以再出一張" : "";
            _dockCameraLabel.text = cooling ? PhotoCooldown.RemainingText : "拍照";
            _dockCameraGroup.alpha = cooling ? 0.5f : 1f;
        }

        void ReleasePet()
        {
            if (!_petSummoned) return;
            _petSummoned = false;
            AIController.Instance?.ReleaseBusy();
        }

        void RefreshAlbumThumb()
        {
            if (_albumThumb == null) return;
            var tex = _photoCards.Count > 0 ? _photoCards[_photoCards.Count - 1].texture : null; // 最新一張
            _albumThumb.texture = tex;
            _albumThumb.enabled = tex != null;
            if (tex != null) _albumThumb.uvRect = AspectFillUv(tex.width, tex.height, 1f, 1f);
        }

        // ─── UI construction（座標皆為設計檔 390×772 的 px，左上為原點）──
        // 有 HandSkin 時用設計稿切出來的 sprite；沒有時用 UIShapes 程式產生的圓角近似。

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

        void BuildCountLabel()
        {
            _countLabel = Label(_design, 12, 588, 200, 16, "", 11, Color.white, TextAlignmentOptions.Left, onScene: true);
        }

        void BuildEmptyState()
        {
            _empty = Place(_design, "Empty", 206, 628, 172, 96);
            Fill(_empty, Skin(s => s.glassPill), true, Glass, 16).raycastTarget = false;
            _emptyText = Label(_empty, 10, 10, 152, 44, "", 12, Ink, TextAlignmentOptions.Center);

            var btn = Place(_empty, "OpenSettings", 26, 58, 120, 30);
            var bg = Fill(btn, Skin(s => s.pillYellow), true, Yellow, 15);
            Label(btn, 0, 0, 120, 30, "開啟設定", 12, Ink, TextAlignmentOptions.Center);
            var b = btn.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            b.onClick.AddListener(PhotoLibrary.OpenSettings);
            _emptySettingsButton = btn.gameObject;
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

        void BuildCameraCard()
        {
            _cameraCard = NewCard("CameraCard");
            Image bgImg;
            var cameraSprite = Skin(s => s.cardCamera);
            if (cameraSprite != null)
            {
                // 設計稿：#E86B4F 底＋內距 5 的白色虛線框（同一張圖）
                bgImg = Fill(Stretch(_cameraCard.rt, "Bg"), cameraSprite, false, Coral, 10);
            }
            else
            {
                bgImg = UIShapes.AddRounded(Stretch(_cameraCard.rt, "Bg").gameObject, Coral, 10);
                UIShapes.AddRing(Place(_cameraCard.rt, "Dash", 5, 5, 82, 114).gameObject, new Color(1f, 1f, 1f, 0.85f), 7);
            }
            CameraIcon(_cameraCard.rt, 31, 36, 30);
            _cameraCardLabel = Label(_cameraCard.rt, 0, 70, 92, 18, "拍照", 12, Color.white, TextAlignmentOptions.Center);

            var b = _cameraCard.rt.gameObject.AddComponent<Button>();
            b.targetGraphic = bgImg;
            b.onClick.AddListener(OnCameraCardClicked);
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

            // 從手牌下方滑上來
            card.rt.anchoredPosition = new Vector2(156f, -DesignH - 40f);
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

        void BuildDock()
        {
            // 出牌後：提示文字＋相簿／拍照／聊天（設計稿 left 50, top 626, 寬 290）
            _dock = Place(_design, "Dock", 50, 626, 290, 131);
            _dockGroup = _dock.gameObject.AddComponent<CanvasGroup>();
            _dockGroup.alpha = 0f;
            _dockHint = Label(_dock, 0, 0, 290, 16, "", 11, Color.white, TextAlignmentOptions.Center, onScene: true);

            // 相簿：玻璃方塊＋疊起來的兩張小照片（上面那張放最新手牌縮圖）
            var album = Place(_dock, "Album", 0, 50, 64, 81);
            var albumSq = Place(album, "Square", 2, 0, 60, 60);
            var albumBg = DockTile(albumSq);
            var minis = Skin(s => s.albumMinis);
            RectTransform thumbParent;
            if (minis != null)
            {
                Fill(Place(albumSq, "Minis", -8, -8, 76, 76), minis, false, Cream, 5).raycastTarget = false;
                thumbParent = Place(albumSq, "Mini2", 16, 10, 30, 36);
            }
            else
            {
                var mini1 = Place(albumSq, "Mini1", 13, 11, 30, 36);
                UIShapes.AddRounded(mini1.gameObject, Cream, 5).raycastTarget = false;
                Rotate(mini1, 10f);
                thumbParent = Place(albumSq, "Mini2", 16, 10, 30, 36);
                UIShapes.AddRounded(thumbParent.gameObject, Cream, 5).raycastTarget = false;
            }
            Rotate(thumbParent, -6f);
            _albumThumb = MaskedPhoto(thumbParent, 3, 3, 24, 24, Skin(s => s.maskR3), 3, null);
            Label(album, 0, 65, 64, 16, "相簿", 12, Color.white, TextAlignmentOptions.Center, onScene: true);
            var albumBtn = album.gameObject.AddComponent<Button>();
            albumBtn.targetGraphic = albumBg;
            albumBtn.onClick.AddListener(OnAlbumClicked);

            // 拍照：84×84 紅色圓鈕（圖含相機圖示與陰影）
            var cam = Place(_dock, "Camera", 101, 26, 88, 105);
            _dockCameraGroup = cam.gameObject.AddComponent<CanvasGroup>();
            var circle = Place(cam, "Circle", 2, 0, 84, 84);
            Image camImg;
            var dockCam = Skin(s => s.dockCamera);
            if (dockCam != null)
            {
                camImg = Fill(Place(circle, "Bg", -24, -24, 132, 132), dockCam, false, Coral, 42);
            }
            else
            {
                UIShapes.AddShadow(circle, new Color(0.12f, 0.12f, 0.08f, 0.4f), 42, 14, 6);
                camImg = UIShapes.AddRounded(Stretch(circle, "Rim").gameObject, Cream, 42);
                UIShapes.AddRounded(Place(circle, "Fill", 5, 5, 74, 74).gameObject, Coral, 37).raycastTarget = false;
                CameraIcon(circle, 23, 23, 38);
            }
            _dockCameraLabel = Label(cam, 0, 89, 88, 16, "拍照", 12, Color.white, TextAlignmentOptions.Center, onScene: true);
            var camBtn = cam.gameObject.AddComponent<Button>();
            camBtn.targetGraphic = camImg;
            camBtn.onClick.AddListener(OnDockCameraClicked);

            // 聊天
            var chat = Place(_dock, "Chat", 226, 50, 64, 81);
            var chatSq = Place(chat, "Square", 2, 0, 60, 60);
            var chatBg = DockTile(chatSq);
            ChatIcon(chatSq, 16, 16);
            Label(chat, 0, 65, 64, 16, "聊天", 12, Color.white, TextAlignmentOptions.Center, onScene: true);
            var chatBtn = chat.gameObject.AddComponent<Button>();
            chatBtn.targetGraphic = chatBg;
            chatBtn.onClick.AddListener(OnChatClicked);

            _dock.gameObject.SetActive(false);
        }

        /// <summary>相簿／聊天的 60×60 玻璃方塊（圖含陰影外擴 16）。</summary>
        Image DockTile(RectTransform square)
        {
            var tile = Skin(s => s.dockTile);
            if (tile != null) return Fill(Place(square, "Bg", -16, -16, 92, 92), tile, false, Glass, 18);
            UIShapes.AddShadow(square, new Color(0.12f, 0.12f, 0.08f, 0.25f), 18, 10, 4);
            return UIShapes.AddRounded(Stretch(square, "Bg").gameObject, Glass, 18);
        }

        void BuildLatest()
        {
            // 營地最新照片卡（設計稿 left 258, top 560, 寬 124）：後面疊兩張、主卡轉 2°、金框發光、「新」標籤跳動
            _latest = Place(_design, "LatestPhoto", 258, 560, 124, 168);
            _latestGroup = _latest.gameObject.AddComponent<CanvasGroup>();

            BackCard(Place(_latest, "Back1", 10, 14, 116, 168), -7f);
            BackCard(Place(_latest, "Back2", 4, 10, 116, 168), 4f);

            var main = Place(_latest, "Card", 0, 0, 124, 168);
            Rotate(main, -2f);
            _latestGlowLo = GlowLayer(main, Skin(s => s.latestGlowLo));
            _latestGlowHi = GlowLayer(main, Skin(s => s.latestGlowHi));
            if (_latestGlowLo == null)
            {
                // 沒有素材：用金色外框近似
                UIShapes.AddRing(Place(main, "Glow", -3, -3, 130, 174).gameObject, Yellow, 15);
            }
            var bg = Fill(Stretch(main, "Bg"), Skin(s => s.latestCard), true, Cream, 12);

            _latestPhoto = MaskedPhoto(main, 7, 7, 110, 110, Skin(s => s.maskR6), 7, null);
            var dot = Place(main, "Poster", 7, 123, 20, 20);
            _latestPosterBg = Fill(dot, Skin(s => s.circle), false, Color.white, 10);
            _latestPosterBg.raycastTarget = false;
            _latestPoster = Label(dot, 0, 0, 20, 20, "", 10, Color.white, TextAlignmentOptions.Center);
            _latestName = Label(main, 31, 123, 88, 20, "", 11, Ink, TextAlignmentOptions.Left);
            var eye = Skin(s => s.iconEye);
            if (eye != null) Fill(Place(main, "Eye", 7, 148, 12, 10), eye, false, Muted, 0).raycastTarget = false;
            _latestCount = Label(main, 22, 145, 98, 16, "", 10, Muted, TextAlignmentOptions.Left);

            // 「新」：右上 right -6、top -9
            _latestBadge = Place(main, "New", 105, -9, 25, 19);
            _latestBadge.pivot = new Vector2(0.5f, 0.5f);
            _latestBadge.anchoredPosition = new Vector2(105f + 12.5f, 9f - 9.5f);
            Fill(Place(_latestBadge, "Bg", 0, 0, 25, 19), Skin(s => s.badgeNew), true, Coral, 8).raycastTarget = false;
            Label(_latestBadge, 0, 0, 25, 17, "新", 11, Color.white, TextAlignmentOptions.Center);

            var btn = main.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(OnLatestClicked);

            _latestGroup.alpha = 0f;
            _latest.gameObject.SetActive(false);
        }

        void BackCard(RectTransform rt, float rot)
        {
            Rotate(rt, rot);
            ShadowLayer(rt, Skin(s => s.latestBackShadow), 24, new Color(0f, 0f, 0f, 0.3f), 12, 10, 4);
            Fill(Stretch(rt, "Bg"), Skin(s => s.latestCard), true, Cream, 12).raycastTarget = false;
        }

        Image GlowLayer(RectTransform parent, Sprite sprite)
        {
            if (sprite == null) return null;
            var rt = Stretch(parent, "Glow");
            rt.offsetMin = new Vector2(-32f, -32f);
            rt.offsetMax = new Vector2(32f, 32f);
            var img = Fill(rt, sprite, true, Yellow, 12);
            img.raycastTarget = false;
            return img;
        }

        // ─── Small builders ────────────────────────────────────────────

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
