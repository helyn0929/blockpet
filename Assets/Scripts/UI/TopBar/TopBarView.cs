using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BlockPet.UI.TopBar
{
    /// <summary>
    /// 首頁頂部狀態欄（設計稿首頁 v8）：寵物頭像＋Lv、名字、愛心、想吃膠囊、進化條、金幣按鈕。
    /// 版面由「BlockPet/Build TopBar」依 README 建好；這裡負責填數值與隨內容調整寬度
    /// （金幣按鈕寬隨數字、中欄寬 = 金幣按鈕左邊 − 間距 − 59、Lv 徽章寬隨字）。
    /// 目前用 <see cref="previewData"/> 假資料；之後接 PetHealthManager／EconomyManager 時呼叫 Set* 方法。
    /// </summary>
    public class TopBarView : MonoBehaviour
    {
        [Serializable]
        public class PreviewData
        {
            public string petName = "BowBow";
            public int level = 10;
            [Range(0f, 5f)] public float hearts = 4f;
            public string want = "美食";
            [Tooltip("依 TopBarConfig.segments 順序：美食、自然、城市、生活人物、運動戶外")]
            public float[] energies = { 30, 16, 10, 7, 5 };
            public int coins = 500;
            public bool dead;
        }

        [SerializeField] TopBarConfig config;

        [Header("Live data")]
        [Tooltip("開啟時：金幣讀 EconomyManager、愛心讀 PetHealthManager、名字／圖／進度讀 PetCollectionManager。想吃與 Lv 目前沒有資料來源，仍用下方假資料。")]
        [SerializeField] bool useLiveData = true;

        [Header("Fake data")]
        [SerializeField] bool usePreviewData = true;
        [SerializeField] PreviewData previewData = new PreviewData();

        [Header("References（Build TopBar 自動接好）")]
        [SerializeField] CanvasGroup rootGroup;
        [SerializeField] RectTransform bar;
        [SerializeField] Image petImage;
        [SerializeField] RectTransform lvBadge;
        [SerializeField] TMP_Text lvText;
        [SerializeField] TMP_Text nameText;
        [SerializeField] HeartsView hearts;
        [SerializeField] Image wantChip;
        [SerializeField] TMP_Text wantText;
        [SerializeField] RectTransform row1;
        [SerializeField] RectTransform row2;
        [SerializeField] EvoBarView evoBar;
        [SerializeField] RectTransform coinButton;
        [SerializeField] RectTransform coinNumberRect;
        [SerializeField] TMP_Text coinNumber;
        [Tooltip("佈置模式時依 DecorModeSettings 個別隱藏")]
        [SerializeField] CanvasGroup barGroup;
        [SerializeField] CanvasGroup homeButtonGroup;
        [SerializeField] CanvasGroup menuGroup;

        [Header("舊版狀態欄（新狀態欄顯示時藏起來，移除新狀態欄時恢復）")]
        [Tooltip("Build TopBar 會自動填入：舊頭像、舊名字／進度、舊金幣文字、購物車鈕。只藏顯示用的物件，不關掉金幣與寵物進度的程式。")]
        [SerializeField] GameObject[] legacyHud;

        [Header("Events")]
        public UnityEvent onCoinClicked = new UnityEvent();
        public UnityEvent onAvatarClicked = new UnityEvent();
        public UnityEvent onHomeClicked = new UnityEvent();
        public UnityEvent onReviveRequested = new UnityEvent();

        string _want = "美食";
        bool _dead;
        float _lastBarWidth = -1f;
        Rect _lastSafeArea;
        PageManager _pageManager;
        CameraUIManager _cameraUI;
        PetHealthManager _health;
        HomeTopBar _legacyTopBar;
        float _nextLivePoll;
        int _liveCoins = int.MinValue;
        float _liveHearts = -1f;
        float _liveProgress = -1f;
        int _livePetIndex = int.MinValue;
        string _livePetName;

        public HeartsView Hearts => hearts;
        public EvoBarView EvoBar => evoBar;

        void Awake()
        {
            _pageManager = FindObjectOfType<PageManager>(true);
            _cameraUI = FindObjectOfType<CameraUIManager>(true);
            _health = FindObjectOfType<PetHealthManager>(true);
            _legacyTopBar = FindObjectOfType<HomeTopBar>(true);
        }

        /// <summary>
        /// 從現有系統讀數值（每 0.25 秒，有變才更新）。
        /// 愛心：目前 PetHealthManager 是 24 小時秒數，換算成 0～5 顆（第 6 步改成半顆制）。
        /// 進化條：目前只有「收集進度」一個數字、還沒有分類能量（第 3 步辨識後才有），先整段用第一個色段顯示。
        /// </summary>
        void PollLiveData()
        {
            if (!useLiveData || Time.unscaledTime < _nextLivePoll) return;
            _nextLivePoll = Time.unscaledTime + 0.25f;

            var eco = EconomyManager.Instance;
            if (eco != null && eco.CurrentMoney != _liveCoins)
            {
                _liveCoins = eco.CurrentMoney;
                SetCoins(_liveCoins);
            }

            if (_health != null && _health.maxHealth > 0f && config != null)
            {
                float h = _health.CurrentHealth / _health.maxHealth * config.maxHearts;
                if (!Mathf.Approximately(h, _liveHearts))
                {
                    _liveHearts = h;
                    SetHearts(h);
                }
            }

            var pets = PetCollectionManager.Instance;
            if (pets != null)
            {
                string petName = pets.GetPetDisplayName();
                if (petName != _livePetName)
                {
                    _livePetName = petName;
                    SetPetName(petName);
                }
                if (pets.CurrentPetIndex != _livePetIndex)
                {
                    _livePetIndex = pets.CurrentPetIndex;
                    var sprite = _legacyTopBar != null ? _legacyTopBar.GetPetSprite(_livePetIndex) : null;
                    if (sprite != null) SetPetSprite(sprite);
                }
                float progress = pets.ProgressPercent;
                if (!Mathf.Approximately(progress, _liveProgress))
                {
                    _liveProgress = progress;
                    int n = config != null ? config.segments.Count : 5;
                    var energies = new float[n];
                    if (n > 0) energies[0] = progress;
                    SetEnergies(energies);
                }
            }
        }

        readonly System.Collections.Generic.List<GameObject> _hiddenLegacy = new System.Collections.Generic.List<GameObject>();

        void OnDisable() => SetLegacyHudVisible(true);

        /// <summary>
        /// 找出舊版首頁狀態欄裡和新狀態欄重複的顯示物件：舊頭像（HomeTopBar）、購物車（HomeShopButton）、
        /// 舊寵物進度與舊金幣文字（PetCollectionManager／EconomyManager 底下的子物件；這兩個物件本身有程式，不能關）。
        /// 依元件類型找，不靠路徑（這兩個 Manager 執行時會把自己移到 Canvas 底下）。
        /// </summary>
        void CollectLegacyHud()
        {
            _hiddenLegacy.Clear();
            if (legacyHud != null)
                foreach (var go in legacyHud) AddLegacy(go);

            var oldAvatar = FindObjectOfType<HomeTopBar>(true);
            if (oldAvatar != null) AddLegacy(oldAvatar.gameObject);
            var oldShop = FindObjectOfType<HomeShopButton>(true);
            if (oldShop != null) AddLegacy(oldShop.gameObject);
            var pets = FindObjectOfType<PetCollectionManager>(true);
            if (pets != null) foreach (Transform child in pets.transform) AddLegacy(child.gameObject);
            var eco = FindObjectOfType<EconomyManager>(true);
            if (eco != null) foreach (Transform child in eco.transform) AddLegacy(child.gameObject);
        }

        void AddLegacy(GameObject go)
        {
            if (go != null && go != gameObject && !go.transform.IsChildOf(transform) && !_hiddenLegacy.Contains(go))
                _hiddenLegacy.Add(go);
        }

        void SetLegacyHudVisible(bool visible)
        {
            foreach (var go in _hiddenLegacy)
                if (go != null) go.SetActive(visible);
        }

        void Start()
        {
            // 在 Start 找：所有 Awake（含 Manager 把自己搬到 Canvas 底下）都跑完了
            CollectLegacyHud();
            SetLegacyHudVisible(false);
            if (usePreviewData) ApplyPreview();
        }

        /// <summary>套用 Inspector 裡的假資料（Build TopBar 也會呼叫，讓 prefab 一打開就是設計稿的樣子）。</summary>
        public void ApplyPreview()
        {
            var d = previewData;
            SetPetName(d.petName);
            SetLevel(d.level);
            hearts?.SetHearts(d.hearts);
            SetWant(d.want);
            evoBar?.SetEnergies(d.energies, animate: Application.isPlaying);
            SetCoins(d.coins);
            SetDead(d.dead);
            Relayout();
        }

        // ─── Public API ────────────────────────────────────────────────

        public void SetPetName(string value)
        {
            if (nameText != null) nameText.text = value;
            Relayout();
        }

        public void SetLevel(int level)
        {
            if (lvText != null) lvText.text = "Lv" + level;
            Relayout();
        }

        public void SetPetSprite(Sprite sprite)
        {
            if (petImage != null) petImage.sprite = sprite;
        }

        public void SetHearts(float value) => hearts?.SetHearts(value);

        public void SetEnergies(float[] energies) => evoBar?.SetEnergies(energies);

        /// <summary>設定「今天想吃」的類別（美食、自然、城市、生活人物、運動戶外）。</summary>
        public void SetWant(string category)
        {
            _want = category;
            RefreshWantChip();
            Relayout();
        }

        public void SetCoins(int coins)
        {
            if (coinNumber != null) coinNumber.text = coins.ToString();
            Relayout();
        }

        /// <summary>補充狀態：死亡（頭像變灰、色段變灰、想吃膠囊改「點我復活」）。</summary>
        public void SetDead(bool dead)
        {
            _dead = dead;
            hearts?.SetDead(dead);
            evoBar?.SetDead(dead);
            if (petImage != null && config != null)
                petImage.color = dead ? Color.Lerp(Color.white, new Color(0.62f, 0.62f, 0.62f, 1f), config.deadAvatarGray) : Color.white;
            RefreshWantChip();
            Relayout();
        }

        /// <summary>金幣按鈕（Button.onClick 接這裡）。</summary>
        public void ClickCoin()
        {
            // 沒有另外接的話，照舊版購物車鈕打開商城
            if (onCoinClicked.GetPersistentEventCount() == 0) _pageManager?.ShowMarketPage();
            onCoinClicked.Invoke();
        }

        /// <summary>左上 Home 鍵（Button.onClick 接這裡）：回選擇寵物空間（取代舊版預設 Back 鈕）。</summary>
        public void ClickHome()
        {
            if (onHomeClicked.GetPersistentEventCount() == 0) _pageManager?.ShowRoomPage();
            onHomeClicked.Invoke();
        }

        /// <summary>頭像（Button.onClick 接這裡）：照舊版頭像打開寵物設定頁。</summary>
        public void ClickAvatar()
        {
            if (onAvatarClicked.GetPersistentEventCount() == 0) _pageManager?.ShowPetSettingPage();
            onAvatarClicked.Invoke();
        }

        /// <summary>想吃膠囊（Button.onClick 接這裡）：死亡時觸發復活。</summary>
        public void ClickWantChip()
        {
            if (!_dead) return;
            if (onReviveRequested.GetPersistentEventCount() == 0) Debug.Log("[TopBarView] 點我復活（尚未接上）");
            onReviveRequested.Invoke();
        }

        // ─── Layout ────────────────────────────────────────────────────

        void RefreshWantChip()
        {
            if (config == null) return;
            if (_dead)
            {
                if (wantChip != null) wantChip.color = config.deadChipBg;
                if (wantText != null) { wantText.text = config.deadChipText; wantText.color = config.deadChipFg; }
                return;
            }
            var c = config.WantColor(_want);
            if (wantChip != null) wantChip.color = c.bg;
            if (wantText != null) { wantText.text = config.wantPrefix + _want; wantText.color = c.fg; }
        }

        /// <summary>依文字寬度重算 Lv 徽章、金幣按鈕、中欄寬度。</summary>
        public void Relayout()
        {
            if (config == null || bar == null) return;

            if (lvBadge != null && lvText != null)
            {
                float w = lvText.preferredWidth + config.lvBadgeExtraWidth;
                lvBadge.sizeDelta = new Vector2(w, config.lvBadgeHeight);
            }

            float coinW = 0f;
            if (coinButton != null && coinNumber != null)
            {
                float numW = coinNumber.preferredWidth;
                if (coinNumberRect != null) coinNumberRect.sizeDelta = new Vector2(numW, config.coinNumber.z);
                coinW = config.coinNumber.x + numW + config.coinNumberToPlus + config.coinPlusSize.x + config.coinPlusRight;
                coinButton.sizeDelta = new Vector2(coinW, config.coinHeight);
            }

            // 中欄：從 rowX 到「金幣按鈕左緣 − 間距」
            float barW = bar.rect.width;
            float coinLeft = barW - config.coinRight - coinW;
            float mid = Mathf.Max(0f, coinLeft - config.panelGap - config.rowX);
            if (row1 != null) row1.sizeDelta = new Vector2(mid, config.row1Height);
            if (row2 != null) row2.sizeDelta = new Vector2(mid, config.row2Height);
            if (row1 != null) LayoutRebuilder.ForceRebuildLayoutImmediate(row1);
            if (row2 != null) LayoutRebuilder.ForceRebuildLayoutImmediate(row2);
            _lastBarWidth = barW;
        }

        void LateUpdate()
        {
            // 只在首頁、不在相機模式時顯示（相簿、聊天、商城、選房間時藏起來）
            bool home = (_cameraUI == null || _cameraUI.CurrentMode == UIMode.Home)
                        && (_pageManager == null || _pageManager.IsHomePageActive);
            if (rootGroup != null)
            {
                rootGroup.alpha = home ? 1f : 0f;
                rootGroup.blocksRaycasts = home;
            }

            // 佈置模式：依設定隱藏（預設全部不隱藏）
            var decor = config != null ? config.decorMode : null;
            bool decorating = decor != null && DecorModeSettings.IsDecorating;
            SetGroupVisible(barGroup, !(decorating && decor.hideTopBar));
            SetGroupVisible(homeButtonGroup, !(decorating && decor.hideHomeButton));
            SetGroupVisible(menuGroup, !(decorating && decor.hideMenuButton));

            PollLiveData();
            ApplySafeArea();
            if (bar != null && !Mathf.Approximately(bar.rect.width, _lastBarWidth)) Relayout();
        }

        static void SetGroupVisible(CanvasGroup g, bool visible)
        {
            if (g == null) return;
            g.alpha = visible ? 1f : 0f;
            g.blocksRaycasts = visible;
            g.interactable = visible;
        }

        /// <summary>瀏海／動態島：整個 HUD 往下移到安全區域內。</summary>
        void ApplySafeArea()
        {
            Rect safe = Screen.safeArea;
            if (safe == _lastSafeArea) return;
            _lastSafeArea = safe;
            var rt = (RectTransform)transform;
            var canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
            float topInset = (Screen.height - safe.yMax) / Mathf.Max(0.01f, scale);
            rt.offsetMax = new Vector2(rt.offsetMax.x, -topInset);
        }
    }
}
