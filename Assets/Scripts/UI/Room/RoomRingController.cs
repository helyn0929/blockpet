using System;
using System.Collections.Generic;
using BlockPet.Hand;
using BlockPet.UI.TopBar;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlockPet.UI.Room
{
    /// <summary>
    /// 選擇寵物空間（設計稿「選擇寵物空間」E 角落轉盤，RoomsRing*.dc.html）：
    /// - 右下四分之一環：每格一個空間（最近照片當底圖），點格子或拖曳轉動，選中的格子轉到中間的金框。
    /// - 左上：選中空間的名稱＋幾天大、愛心、進化條與寵物；肚子餓（愛心 ≤ TopBarConfig.lowHeartsThreshold）顯示紅色提醒。
    /// - 寵物右邊：這個空間最新幾則聊天，像彈幕一樣由右往左飄過。
    /// - 右下大圓鈕：「進去」進入空間；轉到「＋ 新增」或空位時變「新增」，打開新增空間面板（建立新空間／邀請碼加入）。
    /// - 輸入邀請碼後先在轉盤上預覽朋友的空間（藍色「邀請」），按「加入」才真的加入。
    /// 介面在執行時以 390×772 設計座標建立：上半（標題、資訊、寵物）貼齊安全區頂端，轉盤與按鈕貼齊畫面底部，依螢幕寬度等比縮放。
    /// 由「BlockPet/Build Room Ring」掛到 RoomPage，並關掉舊的 UI Toolkit 選房頁（RoomUIController）。
    /// </summary>
    public class RoomRingController : MonoBehaviour
    {
        const float DesignW = 390f;
        const float DesignH = 772f;
        static readonly Vector2 Center = new Vector2(390f, 772f);
        const float R1 = 172f, R2 = 352f, Step = 22.5f, Mid = 225f, SegGap = 0.8f, WindowHalf = 45f;
        const float BigHalf = 17f, BigR1 = R1 - 18f, BigR2 = R2 + 30f;
        const int SlotCount = 11;
        const int MinItems = 4;

        static readonly Color Ink = Hex(0x3A2E22);
        static readonly Color Cream = Hex(0xFFFCF4);
        static readonly Color Paper = Hex(0xFFF8EC);
        static readonly Color Yellow = Hex(0xF6C945);
        static readonly Color YellowEdge = Hex(0xD9A92C);
        static readonly Color Orange = Hex(0xF2B33D);
        static readonly Color Coral = Hex(0xE86B4F);
        static readonly Color CoralEdge = Hex(0xB44A33);
        static readonly Color Blue = Hex(0x6F8FE0);
        static readonly Color BlueEdge = Hex(0x4F6FC4);
        static readonly Color Grass = Hex(0x8FA868);
        static readonly Color Beige = Hex(0xF2E9D8);
        static readonly Color Line = Hex(0xE2D6C0);
        static readonly Color Muted = Hex(0x6B5D4E);
        static readonly Color EggTile = Hex(0xFCE7A6);
        static readonly Color HungryText = Hex(0xFFB4A2);
        static readonly Color ReadyText = Hex(0xFFD45C);
        // 沒選中的格子蓋一層 rgba(30,40,20,.28)；用乘色近似
        static readonly Color Dim = new Color(0.75f, 0.76f, 0.74f, 1f);

        [Header("Style")]
        [Tooltip("字型與標籤陰影材質沿用手牌（BlockPet/Build Room Ring 會自動接上）")]
        [SerializeField] HandSkin skin;
        [Tooltip("愛心圖、進化條顏色沿用頂部狀態欄")]
        [SerializeField] TopBarConfig config;
        [Tooltip("背景：營地圖，執行時縮小模糊")]
        [SerializeField] Texture background;
        [SerializeField] Sprite[] petSprites;
        [SerializeField] int sortingOrder = 30;

        [Header("Data")]
        [Tooltip("新建立空間的預設名字（設計稿 E-4：新蛋）")]
        [SerializeField] string newRoomName = "新蛋";
        [SerializeField] int inviteCodeLength = 6;

        [Header("Links")]
        [SerializeField] PageManager pageManager;

        enum Kind { Room, Add, Empty, Join }

        class Item
        {
            public Kind kind;
            public string roomId;
            public string name;
            public int petIndex;
            public float hearts = -1f;   // < 0：不知道
            public float evo = -1f;      // < 0：不顯示進化條（目前只有正在使用的空間有進度）
            public string createdAt;     // 空間建立時間（算「幾天大」）
            public Texture2D photo;
            public bool Hungry => kind == Kind.Room && hearts >= 0f && hearts <= LowHearts;
            public static float LowHearts = 1f;
        }

        class Slot
        {
            public RingSectorGraphic sector;
            public RectTransform label;
            public CanvasGroup labelGroup;
            public Image tagBg;
            public TMP_Text tag;
            public RectTransform badge;
            public Image badgeBg;
            public Image badgeEdge;
            public TMP_Text badgeText;
            public int k = int.MinValue;
            public float rot = float.NaN;
            public bool selected;
        }

        // 版面
        Canvas _canvas;
        RectTransform _root;
        RectTransform _top;
        RectTransform _bottom;
        RawImage _bg;
        RenderTexture _blurred;
        float _lastWidth = -1f;
        Rect _lastSafe;

        // 轉盤
        readonly Slot[] _slots = new Slot[SlotCount];
        RingSectorGraphic _hit;
        RingSectorGraphic _big;
        RectTransform _bigLabel;
        TMP_Text _bigLabelText;

        // 左側資訊
        RectTransform _info;
        CanvasGroup _infoGroup;
        TMP_Text _place;
        RectTransform _infoBadge;
        Image _infoBadgeBg, _infoBadgeEdge;
        TMP_Text _infoBadgeText;
        TMP_Text _name;
        RectTransform _ageChip;
        TMP_Text _ageText;
        RectTransform _heartsRow;
        readonly Image[] _hearts = new Image[5];
        RectTransform _evoRow;
        RectTransform _evoFill;
        readonly List<Image> _evoSegs = new List<Image>();
        TMP_Text _evoText;
        TMP_Text _newText;
        float _infoTime;

        // 寵物
        RectTransform _pet;
        Image _petImage;

        // 留言彈幕
        class DanPill
        {
            public RectTransform rt;
            public float width, lane, duration, delay;
        }
        RectTransform _dan;
        readonly List<DanPill> _danPills = new List<DanPill>();
        readonly Dictionary<string, List<ChatMessage>> _chatCache = new Dictionary<string, List<ChatMessage>>();
        string _danRoomId;
        const float DanW = 190f, DanH = 188f;
        static readonly float[] DanLanes = { 0f, 40f, 80f, 120f, 160f };
        static readonly Color[] DanColors = { Hex(0xE88AA8), Hex(0x6F8FE0), Hex(0x6FA45F), Hex(0xD78AD0), Hex(0xF29A3A) };

        TMP_Text _hint;
        string _toast;
        float _toastUntil;

        // 大圓鈕
        TMP_Text _ctaText;

        // 新增空間面板
        RectTransform _sheetLayer;
        CanvasGroup _sheetGroup;
        RectTransform _sheet;
        TMP_InputField _codeInput;
        TMP_Text _joinHint;
        bool _sheetOpen;
        float _sheetT;

        // 狀態
        readonly List<Item> _items = new List<Item>();
        readonly Dictionary<string, Texture2D> _photos = new Dictionary<string, Texture2D>();
        Item _joinPreview;
        int _sel;
        int _shownSel = int.MinValue;
        float _rot;
        bool _dragging;
        bool _dragMoved;
        float _dragLastAngle;
        string _pendingSelectId;
        bool _busy;
        Firebase.Database.DatabaseReference _userRoomsRef;

        // ─── Lifecycle ─────────────────────────────────────────────────

        void Awake()
        {
            if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
            if (config != null) Item.LowHearts = config.lowHeartsThreshold;
            Build();
        }

        void OnEnable()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(true);
            MakeBlurredBackground();
            FirebaseManager.OnRoomChanged += Reload;
            FirebaseManager.OnLoginSuccess += OnLogin;
            StartRoomsListener();
            CloseSheet(instant: true);
            Reload();
        }

        void OnDisable()
        {
            FirebaseManager.OnRoomChanged -= Reload;
            FirebaseManager.OnLoginSuccess -= OnLogin;
            StopRoomsListener();
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            StopRoomsListener();
            foreach (var t in _photos.Values) if (t != null) Destroy(t);
            _photos.Clear();
            if (_blurred != null) { _blurred.Release(); Destroy(_blurred); }
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        void OnLogin(bool ok)
        {
            if (!ok) return;
            StartRoomsListener();
            Reload();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            Relayout();

            if (!_dragging)
            {
                float target = -_sel * Step;
                _rot = Mathf.Abs(_rot - target) < 0.01f ? target : Mathf.Lerp(_rot, target, 1f - Mathf.Exp(-dt * 11f));
            }
            LayoutRing();

            if (_sel != _shownSel) ShowSelected();

            // 資訊淡入（eIn 0.3s：往上 8px）
            _infoTime += dt;
            float k = Mathf.Clamp01(_infoTime / 0.3f);
            k = 1f - (1f - k) * (1f - k);
            _infoGroup.alpha = k;
            _info.anchoredPosition = new Vector2(18f, -22f - 8f * (1f - k));

            AnimatePet();
            AnimateDan();
            AnimateSheet(dt);

            if (_toast != null && Time.unscaledTime > _toastUntil) { _toast = null; UpdateHint(); }
        }

        // ─── 資料 ──────────────────────────────────────────────────────

        void StartRoomsListener()
        {
            if (_userRoomsRef != null) return;
            var fb = FirebaseManager.Instance;
            if (fb == null) return;
            _userRoomsRef = fb.GetUserRoomsRef();
            if (_userRoomsRef != null) _userRoomsRef.ValueChanged += OnRoomsChanged;
        }

        void StopRoomsListener()
        {
            if (_userRoomsRef != null) _userRoomsRef.ValueChanged -= OnRoomsChanged;
            _userRoomsRef = null;
        }

        void OnRoomsChanged(object sender, Firebase.Database.ValueChangedEventArgs args)
        {
            if (args?.DatabaseError != null) return;
            Reload();
        }

        void Reload()
        {
            _chatCache.Clear();
            _danRoomId = null;
            var fb = FirebaseManager.Instance;
            if (fb == null) { ApplyRooms(new List<FirebaseManager.RoomSummary>()); return; }
            fb.GetMyRoomSummaries(list =>
            {
                if (this == null) return;
                list ??= new List<FirebaseManager.RoomSummary>();
                string current = fb.RoomId;
                if (!string.IsNullOrEmpty(current) && !list.Exists(r => r.roomId == current))
                    list.Insert(0, new FirebaseManager.RoomSummary { roomId = current, name = current });
                ApplyRooms(list);
            });
        }

        void ApplyRooms(List<FirebaseManager.RoomSummary> rooms)
        {
            var fb = FirebaseManager.Instance;
            string current = fb != null ? fb.RoomId : "";
            // 重新整理後停在原本選的格子（新增／空位用種類找回）；第一次載入選目前的空間
            string keepId = current;
            Kind keepKind = Kind.Room;
            if (_pendingSelectId != null) keepId = _pendingSelectId;
            else if (_items.Count > 0)
            {
                var was = ItemAt(_sel);
                keepId = was.roomId;
                keepKind = was.kind;
            }
            _pendingSelectId = null;

            var health = FindObjectOfType<PetHealthManager>(true);
            float maxHealth = health != null && health.maxHealth > 0f ? health.maxHealth : 86400f;
            int maxHearts = config != null ? config.maxHearts : 5;

            var used = new HashSet<string>();
            _items.Clear();
            foreach (var r in rooms)
            {
                if (r == null || string.IsNullOrEmpty(r.roomId)) continue;
                bool isCurrent = r.roomId == current;
                float hp = isCurrent && health != null ? health.CurrentHealth : HealthNow(r, maxHealth);
                var item = new Item
                {
                    kind = Kind.Room,
                    roomId = r.roomId,
                    // 寵物名字（空間資訊可改）；沒有就沿用空間名稱
                    name = !string.IsNullOrEmpty(r.petName) ? r.petName : (string.IsNullOrEmpty(r.name) ? r.roomId : r.name),
                    petIndex = r.petIndex,
                    hearts = hp / maxHealth * maxHearts,
                    evo = isCurrent && PetCollectionManager.Instance != null ? PetCollectionManager.Instance.ProgressPercent : -1f,
                    photo = PhotoFor(r.roomId),
                    createdAt = r.createdAt,
                };
                used.Add(r.roomId);
                _items.Add(item);
            }
            // 預覽中的邀請空間：已經是自己的空間就不用再預覽
            if (_joinPreview != null && !used.Contains(_joinPreview.roomId)) _items.Add(_joinPreview);
            else _joinPreview = null;
            _items.Add(new Item { kind = Kind.Add, name = "＋ 新增" });
            while (_items.Count < MinItems) _items.Add(new Item { kind = Kind.Empty, name = "" });

            // 釋放已經不在轉盤上的照片
            var drop = new List<string>();
            foreach (var kv in _photos) if (!used.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (var id in drop) { if (_photos[id] != null) Destroy(_photos[id]); _photos.Remove(id); }

            int idx = keepId != null ? _items.FindIndex(i => i.roomId == keepId) : -1;
            if (idx < 0) idx = _items.FindIndex(i => i.kind == keepKind && i.kind != Kind.Room);
            if (idx < 0) idx = 0;
            SelectIndex(idx, snap: _shownSel == int.MinValue);
            _shownSel = int.MinValue; // 資料變了，重畫資訊與轉盤
            foreach (var s in _slots) s.k = int.MinValue;
        }

        static float HealthNow(FirebaseManager.RoomSummary r, float maxHealth)
        {
            // 與 FirebaseManager.GetRoomHealthNow 相同算法：每秒扣 1
            float hp = r.currentHealth;
            if (!string.IsNullOrEmpty(r.lastUpdateTime) && DateTime.TryParse(r.lastUpdateTime, out var last))
                hp -= (float)(DateTime.Now - last).TotalSeconds;
            return Mathf.Clamp(hp, 0f, maxHealth);
        }

        Texture2D PhotoFor(string roomId)
        {
            if (_photos.TryGetValue(roomId, out var tex)) return tex;
            tex = SaveManager.Instance != null ? SaveManager.Instance.GetLatestPhotoTextureForRoom(roomId) : null;
            _photos[roomId] = tex;
            return tex;
        }

        Item ItemAt(int k) => _items.Count == 0 ? new Item { kind = Kind.Add } : _items[Mod(k, _items.Count)];

        static int Mod(int k, int n) => ((k % n) + n) % n;

        /// <summary>選第 index 個項目；轉最近的那一圈，不會整圈繞回去。</summary>
        void SelectIndex(int index, bool snap)
        {
            int n = Mathf.Max(1, _items.Count);
            int k = _sel + Mod(index - _sel, n);
            if (k - _sel > n / 2) k -= n;
            _sel = k;
            if (snap) _rot = -_sel * Step;
        }

        // ─── 轉盤 ──────────────────────────────────────────────────────

        void LayoutRing()
        {
            int centerK = Mathf.RoundToInt(-_rot / Step);
            float clipMin = Mid - WindowHalf, clipMax = Mid + WindowHalf;
            for (int j = 0; j < SlotCount; j++)
            {
                var s = _slots[j];
                int k = centerK + j - SlotCount / 2;
                var item = ItemAt(k);
                bool selected = k == _sel;
                bool changed = k != s.k || selected != s.selected;
                if (changed)
                {
                    s.k = k;
                    s.selected = selected;
                    s.sector.SetTexture(item.photo);
                    s.sector.color = SectorColor(item, selected);
                    SetSlotLabel(s, item, selected);
                }
                if (changed || !Mathf.Approximately(s.rot, _rot))
                {
                    s.rot = _rot;
                    float a = Mid + k * Step;
                    s.sector.clipMin = clipMin;
                    s.sector.clipMax = clipMax;
                    s.sector.Set(a - Step / 2f + SegGap, a + Step / 2f - SegGap, _rot);

                    float at = a + _rot;
                    var p = Pt((R1 + R2) / 2f, at);
                    s.label.anchoredPosition = new Vector2(p.x, -p.y);
                    float edge = Mathf.Clamp01((WindowHalf - Mathf.Abs(at - Mid) - 2f) / 6f);
                    s.labelGroup.alpha = selected || item.kind == Kind.Empty ? 0f : edge;
                }
            }
            _hit.clipMin = clipMin;
            _hit.clipMax = clipMax;
        }

        static Color SectorColor(Item item, bool selected)
        {
            Color c;
            if (item.photo != null && (item.kind == Kind.Room || item.kind == Kind.Join))
                c = item.Hungry ? new Color(0.82f, 0.8f, 0.78f, 1f) : Color.white;
            else if (item.kind == Kind.Add) c = new Color(1f, 0.973f, 0.925f, 0.35f);
            else if (item.kind == Kind.Empty) c = new Color(1f, 0.973f, 0.925f, 0.1f);
            else c = Grass;
            if (!selected) c = new Color(c.r * Dim.r, c.g * Dim.g, c.b * Dim.b, c.a);
            return c;
        }

        void SetSlotLabel(Slot s, Item item, bool selected)
        {
            bool add = item.kind == Kind.Add;
            s.tag.text = item.name ?? "";
            s.tag.color = add ? Ink : Paper;
            s.tagBg.color = add ? new Color(1f, 0.973f, 0.925f, 0.92f) : Ink;
            string badge = BadgeOf(item, out var bg, out var fg, out var edge);
            bool hasBadge = !string.IsNullOrEmpty(badge) && !selected;
            // 名字＋徽章整組以格子中心置中（gap 3）
            float top = hasBadge ? -19.5f : -10f;
            float w = s.tag.GetPreferredValues(s.tag.text).x + 18f;
            SetSize(s.tagBg.rectTransform, w, 20f, -w / 2f, top);

            s.badge.gameObject.SetActive(hasBadge);
            if (hasBadge)
            {
                s.badgeText.text = badge;
                s.badgeText.color = fg;
                s.badgeBg.color = bg;
                s.badgeEdge.color = edge;
                float bw = s.badgeText.GetPreferredValues(badge).x + 12f;
                SetSize(s.badge, bw, 16f, -bw / 2f, top + 23f);
            }
        }

        static string BadgeOf(Item item, out Color bg, out Color fg, out Color edge)
        {
            bg = Coral; fg = Color.white; edge = CoralEdge;
            if (item.kind == Kind.Join) { bg = Blue; edge = BlueEdge; return "邀請"; }
            if (item.Hungry) return "肚子餓";
            return null;
        }

        // ─── 選中的空間 ────────────────────────────────────────────────

        void ShowSelected()
        {
            _shownSel = _sel;
            var item = ItemAt(_sel);
            bool isNew = item.kind == Kind.Add || item.kind == Kind.Empty;
            bool isJoin = item.kind == Kind.Join;

            // 中間的大格子
            _big.SetTexture(isNew ? null : item.photo);
            _big.color = isNew ? new Color(1f, 0.973f, 0.925f, 0.85f) : (item.photo != null ? (item.Hungry ? new Color(0.82f, 0.8f, 0.78f) : Color.white) : Grass);
            _bigLabelText.text = isNew ? "＋ 新增" : item.name;
            float lw = _bigLabelText.GetPreferredValues(_bigLabelText.text).x + 22f;
            SetSize((RectTransform)_bigLabelText.transform.parent, lw, 22f, -lw / 2f, -11f);

            // 左側資訊
            _place.text = isNew ? "新的空間" : "邀請碼 " + item.roomId;
            float pw = _place.GetPreferredValues(_place.text).x;
            SetSize(_place.rectTransform, pw + 4f, 18f, 0f, 0f);
            string badge = BadgeOf(item, out var bg, out var fg, out var edge);
            _infoBadge.gameObject.SetActive(badge != null);
            if (badge != null)
            {
                _infoBadgeText.text = badge;
                _infoBadgeText.color = fg;
                _infoBadgeBg.color = bg;
                _infoBadgeEdge.color = edge;
                float bw = _infoBadgeText.GetPreferredValues(badge).x + 16f;
                SetSize(_infoBadge, bw, 18f, pw + 8f, 0f);
            }
            _name.text = isNew ? "新增空間" : item.name;
            string age = isNew ? null : AgeText(item.createdAt);
            _ageChip.gameObject.SetActive(age != null);
            if (age != null)
            {
                _ageText.text = age;
                float nw = Mathf.Min(_name.GetPreferredValues(_name.text).x, 200f);
                float aw = _ageText.GetPreferredValues(age).x + 15f;
                _name.rectTransform.sizeDelta = new Vector2(nw + 2f, 42f);
                // 名字右邊、對齊基線（設計稿 translateY(-4)）
                SetSize(_ageChip, aw, 22f, nw + 8f, 24f + 12f);
            }
            else _name.rectTransform.sizeDelta = new Vector2(290f, 42f);
            ShowComments(isNew ? null : item.roomId);

            _heartsRow.gameObject.SetActive(!isNew);
            if (!isNew) SetHearts(item.hearts < 0f ? (config != null ? config.maxHearts : 5) : item.hearts);
            _evoRow.gameObject.SetActive(!isNew && item.evo >= 0f);
            if (!isNew && item.evo >= 0f) SetEvo(item.evo);
            _newText.gameObject.SetActive(isNew);
            _newText.text = "孵一顆新蛋，或用邀請碼加入朋友的空間";
            _infoTime = 0f;

            // 寵物：新增／空位顯示淡淡的剪影；肚子餓變灰、不跳
            _petImage.sprite = PetSprite(item.petIndex);
            _petImage.enabled = _petImage.sprite != null;
            _petImage.color = isNew ? new Color(1f, 1f, 1f, 0.35f) : (item.Hungry ? new Color(0.78f, 0.78f, 0.76f) : Color.white);

            _ctaText.text = isNew ? "新增" : (isJoin ? "加入" : "進去");
            UpdateHint();
        }

        Sprite PetSprite(int index)
        {
            if (petSprites == null || petSprites.Length == 0) return null;
            return petSprites[Mathf.Abs(index) % petSprites.Length];
        }

        void SetHearts(float value)
        {
            float v = Mathf.Round(value * 2f) / 2f;
            for (int i = 0; i < _hearts.Length; i++)
            {
                float fill = Mathf.Clamp01(v - i);
                var img = _hearts[i];
                if (config != null && config.heartFull != null)
                {
                    img.sprite = fill >= 1f ? config.heartFull : (fill >= 0.5f ? config.heartHalf : config.heartEmpty);
                    img.color = Color.white;
                }
                else img.color = fill >= 0.5f ? Hex(0xF06B5B) : new Color(1f, 0.973f, 0.925f, 0.55f);
            }
        }

        void SetEvo(float percent)
        {
            // 目前只有「收集進度」一個數字（與頂部狀態欄相同），整段用第一個類別色
            const float barW = 150f;
            float w = barW * Mathf.Clamp01(percent / 100f);
            _evoFill.sizeDelta = new Vector2(w, 8f);
            if (_evoSegs.Count > 0)
            {
                _evoSegs[0].rectTransform.sizeDelta = new Vector2(w, 8f);
                _evoSegs[0].color = config != null && config.segments.Count > 0 ? config.segments[0].color : Hex(0xF29A3A);
            }
            bool ready = percent >= 100f;
            _evoText.text = ready ? "可進化！" : "進化 " + Mathf.FloorToInt(percent) + "%";
            _evoText.color = ready ? ReadyText : Color.white;
        }

        void UpdateHint()
        {
            if (_toast != null) { _hint.text = _toast; return; }
            var item = ItemAt(_sel);
            _hint.text = item.kind == Kind.Join ? "加入後，" + item.name + " 會留在你的轉盤上"
                : (_items.FindAll(i => i.kind == Kind.Room).Count <= 1 ? "之後的寵物會排在轉盤上" : "點轉盤換寵物");
        }

        void Toast(string message, float seconds = 3f)
        {
            _toast = message;
            _toastUntil = Time.unscaledTime + seconds;
            UpdateHint();
        }

        void AnimatePet()
        {
            var item = ItemAt(_sel);
            bool bob = item.kind == Kind.Room && !item.Hungry || item.kind == Kind.Join;
            float y = bob ? -8f * (0.5f - 0.5f * Mathf.Cos(Time.unscaledTime / 2.4f * Mathf.PI * 2f)) : 0f;
            _pet.anchoredPosition = new Vector2(6f, -y);
        }

        /// <summary>「42 天大」；建立當天是「第 1 天」；沒有建立時間就不顯示。</summary>
        static string AgeText(string createdAt)
        {
            if (string.IsNullOrEmpty(createdAt) || !DateTime.TryParse(createdAt, out var created)) return null;
            int days = (int)(DateTime.Now - created.ToLocalTime()).TotalDays;
            if (days < 0) days = 0;
            return days == 0 ? "第 1 天" : days + " 天大";
        }

        // ─── 留言彈幕 ──────────────────────────────────────────────────

        void BuildDan()
        {
            // 寵物右邊 190×188，左邊 28px 淡出；最多 5 條軌道
            _dan = Place(_top, "Comments", 200, 212, DanW, DanH);
            var mask = _dan.gameObject.AddComponent<RectMask2D>();
            mask.softness = new Vector2Int(28, 0);
        }

        void ShowComments(string roomId)
        {
            if (roomId == _danRoomId) return;
            _danRoomId = roomId;
            ClearDan();
            if (string.IsNullOrEmpty(roomId)) return;

            if (_chatCache.TryGetValue(roomId, out var cached)) { BuildDanPills(cached); return; }
            var fb = FirebaseManager.Instance;
            if (fb == null) return;
            fb.GetRecentChat(roomId, 5, list =>
            {
                if (this == null) return;
                _chatCache[roomId] = list;
                if (_danRoomId == roomId) BuildDanPills(list);
            });
        }

        void ClearDan()
        {
            foreach (var p in _danPills) if (p.rt != null) Destroy(p.rt.gameObject);
            _danPills.Clear();
        }

        void BuildDanPills(List<ChatMessage> messages)
        {
            ClearDan();
            if (messages == null || messages.Count == 0) return;
            string me = FirebaseManager.Instance != null ? FirebaseManager.Instance.GetUserId() : null;
            int n = messages.Count;
            for (int i = 0; i < n; i++)
            {
                var m = messages[i];
                bool mine = !string.IsNullOrEmpty(me) && m.senderId == me;
                string who = !string.IsNullOrEmpty(m.displayName) ? m.displayName : m.userName;
                string ini = mine ? "我" : (string.IsNullOrEmpty(who) ? "?" : who.Substring(0, 1));
                Color bg = mine ? Orange : DanColors[Mathf.Abs((who ?? "").GetHashCode()) % DanColors.Length];
                string text = m.message.Replace("\n", " ").Replace("\r", "");
                if (text.Length > 14) text = text.Substring(0, 14) + "…";

                var rt = Place(_dan, "Pill", DanW, 0, 60, 30);
                UIShapes.AddShadow(rt, new Color(30f / 255f, 40f / 255f, 20f / 255f, 0.2f), 15, 8, 3);
                UIShapes.AddRounded(Place(rt, "Fill", 0, 0, 0, 0).gameObject, new Color(1f, 0.988f, 0.957f, 0.88f), 15).raycastTarget = false;
                StretchIn((RectTransform)rt.GetChild(1));
                UIShapes.AddRounded(Place(rt, "Avatar", 3, 3, 24, 24).gameObject, bg, 12).raycastTarget = false;
                var iniText = Label(rt, 3, 3, 24, 24, ini, 11, Color.white, TextAlignmentOptions.Center);
                iniText.enableWordWrapping = false;
                var t = Label(rt, 33, 0, 200, 30, text, 12, Ink, TextAlignmentOptions.Left);
                t.enableWordWrapping = false;
                float w = 33f + t.GetPreferredValues(text).x + 11f;
                rt.sizeDelta = new Vector2(w, 30f);

                // 設計稿：每則 9／10.5／12 秒飄過，錯開起點，軌道 0～160
                float dur = 9f + (i % 3) * 1.5f;
                _danPills.Add(new DanPill
                {
                    rt = rt, width = w, lane = DanLanes[i % DanLanes.Length], duration = dur,
                    delay = -(i * dur / Mathf.Max(n, 1)) - (i % 2) * 1.3f,
                });
            }
            AnimateDan();
        }

        void AnimateDan()
        {
            float now = Time.unscaledTime;
            foreach (var p in _danPills)
            {
                float f = Mathf.Repeat((now - p.delay) / p.duration, 1f);
                float x = DanW - (DanW + p.width) * f;
                p.rt.anchoredPosition = new Vector2(x, -p.lane);
            }
        }

        // ─── 操作 ──────────────────────────────────────────────────────

        internal void OnRingDown(Vector2 design)
        {
            _dragging = true;
            _dragMoved = false;
            _dragLastAngle = _hit.AngleOf(design);
        }

        internal void OnRingDrag(Vector2 design)
        {
            if (!_dragging) return;
            float a = _hit.AngleOf(design);
            float d = Mathf.DeltaAngle(_dragLastAngle, a);
            _dragLastAngle = a;
            if (Mathf.Abs(d) > 0.01f) _dragMoved = true;
            _rot += d;
            _sel = Mathf.RoundToInt(-_rot / Step);
        }

        internal void OnRingUp(Vector2 design, bool click)
        {
            if (!_dragging) return;
            _dragging = false;
            if (click && !_dragMoved)
            {
                // 點到哪一格就轉到那一格
                float a = _hit.AngleOf(design);
                _sel = Mathf.RoundToInt((a - Mid - _rot) / Step);
            }
            else _sel = Mathf.RoundToInt(-_rot / Step);
        }

        void OnCta()
        {
            if (_busy) return;
            var item = ItemAt(_sel);
            switch (item.kind)
            {
                case Kind.Room: EnterRoom(item.roomId); break;
                case Kind.Join: JoinPreview(item); break;
                default: OpenSheet(); break;
            }
        }

        void EnterRoom(string roomId)
        {
            var fb = FirebaseManager.Instance;
            if (fb == null) return;
            if (string.IsNullOrEmpty(fb.GetUserId())) { Toast("還沒登入，請重新登入"); return; }
            fb.SetRoomId(roomId);
            fb.JoinRoom(roomId, (ok, err) =>
            {
                if (!ok) Debug.LogWarning("[RoomRing] JoinRoom failed: " + err);
            });
            EnterGameAndGoHome();
        }

        void JoinPreview(Item item)
        {
            var fb = FirebaseManager.Instance;
            if (fb == null) return;
            _busy = true;
            // JoinRoom 會先確認房間存在；成功才切換，避免 SetRoomId 自動建出空房間
            fb.JoinRoom(item.roomId, (ok, err) =>
            {
                _busy = false;
                if (this == null) return;
                if (!ok)
                {
                    Debug.LogWarning("[RoomRing] JoinRoom failed: " + err);
                    Toast("加入失敗，請再試一次");
                    return;
                }
                _joinPreview = null;
                fb.SetRoomId(item.roomId);
                EnterGameAndGoHome();
            });
        }

        void EnterGameAndGoHome()
        {
            FindObjectOfType<LoginUIHandler>(true)?.EnterMainGame();
            if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
            pageManager?.ShowHomePage();
        }

        void OnSettings()
        {
            if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
            pageManager?.ShowSettingsPage();
        }

        void OnCreate()
        {
            var fb = FirebaseManager.Instance;
            if (fb == null || _busy) return;
            string code = GenerateCode(inviteCodeLength);
            _busy = true;
            // 先寫好 meta／petState／members 再顯示，避免出現沒有 meta 的半成品房間
            fb.CreateRoom(code, newRoomName, (ok, err) =>
            {
                _busy = false;
                if (this == null) return;
                if (!ok)
                {
                    Debug.LogWarning("[RoomRing] CreateRoom failed: " + err);
                    _joinHint.text = "建立失敗，請再試一次";
                    _joinHint.color = Coral;
                    return;
                }
                GUIUtility.systemCopyBuffer = code;
                CloseSheet();
                _pendingSelectId = code;
                Reload();
                Toast("已建立！邀請碼 " + code + " 已複製", 4f);
            });
        }

        void OnJoinCode()
        {
            var fb = FirebaseManager.Instance;
            if (fb == null || _busy) return;
            string code = (_codeInput.text ?? "").Trim().ToUpperInvariant();
            if (code.Length == 0) { JoinHint("請輸入邀請碼", true); return; }

            int mine = _items.FindIndex(i => i.kind == Kind.Room && i.roomId == code);
            if (mine >= 0)
            {
                CloseSheet();
                SelectIndex(mine, snap: false);
                return;
            }

            _busy = true;
            JoinHint("查詢中…", false);
            fb.GetRoomMeta(code, meta =>
            {
                _busy = false;
                if (this == null) return;
                if (meta == null) { JoinHint("找不到這個邀請碼", true); return; }
                _joinPreview = new Item
                {
                    kind = Kind.Join,
                    roomId = code,
                    name = string.IsNullOrEmpty(meta.name) ? code : meta.name,
                    createdAt = meta.createdAt,
                };
                _codeInput.text = "";
                CloseSheet();
                _pendingSelectId = code;
                Reload();
            });
        }

        void JoinHint(string text, bool error)
        {
            _joinHint.text = text;
            _joinHint.color = error ? Coral : Muted;
        }

        static string GenerateCode(int len)
        {
            const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // 不用 0/O/1/I
            len = Mathf.Clamp(len, 4, 12);
            var chars = new char[len];
            for (int i = 0; i < len; i++) chars[i] = alphabet[UnityEngine.Random.Range(0, alphabet.Length)];
            return new string(chars);
        }

        // ─── 新增空間面板 ──────────────────────────────────────────────

        void OpenSheet()
        {
            _sheetOpen = true;
            _sheetLayer.gameObject.SetActive(true);
            JoinHint("輸入朋友給你的 " + inviteCodeLength + " 碼邀請碼", false);
        }

        void CloseSheet(bool instant = false)
        {
            _sheetOpen = false;
            if (instant && _sheetLayer != null)
            {
                _sheetT = 0f;
                _sheetLayer.gameObject.SetActive(false);
            }
        }

        void AnimateSheet(float dt)
        {
            if (!_sheetLayer.gameObject.activeSelf) return;
            _sheetT = Mathf.MoveTowards(_sheetT, _sheetOpen ? 1f : 0f, dt / 0.3f);
            float e = 1f - Mathf.Pow(1f - _sheetT, 3f);
            _sheetGroup.alpha = e;
            _sheetGroup.blocksRaycasts = _sheetOpen;
            _sheet.anchoredPosition = new Vector2(0f, -(1f - e) * _sheet.rect.height * _sheet.localScale.y);
            if (!_sheetOpen && _sheetT <= 0f) _sheetLayer.gameObject.SetActive(false);
        }

        // ─── 版面 ──────────────────────────────────────────────────────

        void Relayout()
        {
            float w = _root.rect.width;
            var safe = Screen.safeArea;
            if (w <= 0f || (Mathf.Approximately(w, _lastWidth) && safe == _lastSafe)) return;
            _lastWidth = w;
            _lastSafe = safe;
            float k = w / DesignW;
            float scale = _canvas.rootCanvas.scaleFactor;
            float px = scale > 0f ? 1f / scale : 1f;
            float topInset = (Screen.height - safe.yMax) * px;
            float bottomInset = safe.yMin * px;

            _top.localScale = _bottom.localScale = Vector3.one * k;
            _top.anchoredPosition = new Vector2(0f, -topInset);
            _bottom.anchoredPosition = Vector2.zero;
            _sheet.localScale = Vector3.one * k;
            // 面板內容 322 + 底部留白 26，再加上 Home 指示條的安全區
            _sheet.sizeDelta = new Vector2(DesignW, SheetContentH + 26f + bottomInset / k);
            _ctaRoot.anchoredPosition = new Vector2(0f, bottomInset / k);
        }

        const float SheetContentH = 322f;
        RectTransform _ctaRoot;

        void Build()
        {
            var go = new GameObject("RoomRing (runtime)", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = sortingOrder;
            go.AddComponent<GraphicRaycaster>();
            _root = (RectTransform)go.transform;
            StretchIn(_root);

            // 背景：營地圖模糊＋一層暗綠
            var bgBase = Stretch(_root, "Background");
            bgBase.gameObject.AddComponent<Image>().color = Grass;
            _bg = Stretch(_root, "BackgroundPhoto").gameObject.AddComponent<RawImage>();
            _bg.raycastTarget = false;
            _bg.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            var tint = Stretch(_root, "Tint").gameObject.AddComponent<Image>();
            tint.color = new Color(40f / 255f, 52f / 255f, 30f / 255f, 0.2f);
            tint.raycastTarget = false;

            _bottom = DesignRoot("Bottom", top: false);
            _top = DesignRoot("Top", top: true);

            // 兩個柔光圓：寵物後面亮、轉盤後面暗
            var glow = Place(_top, "PetGlow", -16, 248, 240, 240);
            UIShapes.AddShadow(glow, new Color(1f, 0.973f, 0.925f, 0.45f), 120, 50, 0);
            UIShapes.AddRounded(Place(glow, "Disc", 0, 0, 240, 240).gameObject, new Color(1f, 0.973f, 0.925f, 0.42f), 120).raycastTarget = false;
            UIShapes.AddRounded(Place(_bottom, "RingShade", 210, 592, 360, 360).gameObject, new Color(36f / 255f, 48f / 255f, 26f / 255f, 0.35f), 180).raycastTarget = false;

            BuildRing();
            BuildPet();
            BuildDan();
            BuildHeader();
            BuildInfo();
            BuildHint();
            BuildCta();
            BuildSheet();
            Relayout();
        }

        RectTransform DesignRoot(string name, bool top)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_root, false);
            float y = top ? 1f : 0f;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, y);
            rt.pivot = new Vector2(0.5f, y);
            rt.sizeDelta = new Vector2(DesignW, DesignH);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        void BuildRing()
        {
            var ring = Place(_bottom, "Ring", 0, 0, DesignW, DesignH);
            for (int j = 0; j < SlotCount; j++)
            {
                var s = new Slot();
                s.sector = Sector(ring, "Segment" + j, R1, R2);
                s.sector.raycastTarget = false;
                _slots[j] = s;
            }
            // 格子標籤（名字＋徽章）畫在格子上面
            var labels = Place(ring, "Labels", 0, 0, DesignW, DesignH);
            foreach (var s in _slots)
            {
                s.label = Place(labels, "Label", 0, 0, 0, 0);
                s.labelGroup = s.label.gameObject.AddComponent<CanvasGroup>();
                s.labelGroup.blocksRaycasts = false;
                var tagRt = Place(s.label, "Tag", 0, 0, 40, 20);
                s.tagBg = UIShapes.AddRounded(tagRt.gameObject, Ink, 10);
                s.tag = Label(tagRt, 0, 0, 0, 0, "", 12, Paper, TextAlignmentOptions.Center);
                StretchIn(s.tag.rectTransform);
                s.badge = Place(s.label, "Badge", 0, 0, 30, 16);
                s.badgeEdge = UIShapes.AddRounded(Place(s.badge, "Edge", 0, 2, 0, 0).gameObject, CoralEdge, 8);
                StretchIn(s.badgeEdge.rectTransform, 0, 2, 0, -2);
                s.badgeBg = UIShapes.AddRounded(Place(s.badge, "Fill", 0, 0, 0, 0).gameObject, Coral, 8);
                StretchIn(s.badgeBg.rectTransform);
                s.badgeText = Label(s.badge, 0, 0, 0, 0, "", 10, Color.white, TextAlignmentOptions.Center);
                StretchIn(s.badgeText.rectTransform);
            }

            // 接收點擊／拖曳的透明扇形（只在轉盤視窗內）
            _hit = Sector(_bottom, "RingHit", R1, R2);
            _hit.color = new Color(1, 1, 1, 0);
            _hit.clipMin = Mid - WindowHalf;
            _hit.clipMax = Mid + WindowHalf;
            _hit.Set(-9999f, 9999f, 0f);
            var input = _hit.gameObject.AddComponent<RoomRingInput>();
            input.owner = this;

            // 視窗外框
            var window = Sector(_bottom, "WindowStroke", R1, R2);
            window.stroke = 4f;
            window.color = Cream;
            window.raycastTarget = false;
            window.Set(Mid - WindowHalf, Mid + WindowHalf, 0f);

            // 中間的大格子：陰影、照片、金框、指針
            for (int i = 3; i >= 1; i--)
            {
                var sh = Sector(_bottom, "BigShadow", BigR1 - i * 2f, BigR2 + i * 2f);
                sh.center = Center + new Vector2(0f, 8f);
                sh.color = new Color(0f, 0f, 0f, 0.12f);
                sh.raycastTarget = false;
                float da = i * 2f / BigR1 * Mathf.Rad2Deg;
                sh.Set(Mid - BigHalf - da, Mid + BigHalf + da, 0f);
            }
            _big = Sector(_bottom, "Big", BigR1, BigR2);
            _big.raycastTarget = false;
            _big.Set(Mid - BigHalf, Mid + BigHalf, 0f);
            var bigStroke = Sector(_bottom, "BigStroke", BigR1, BigR2);
            bigStroke.stroke = 5f;
            bigStroke.color = Yellow;
            bigStroke.raycastTarget = false;
            bigStroke.Set(Mid - BigHalf, Mid + BigHalf, 0f);

            var tick = AddGraphic<UIPolygon>(Place(_bottom, "Tick", 0, 0, DesignW, DesignH));
            tick.color = Yellow;
            tick.raycastTarget = false;
            tick.SetPoints(Pt(BigR1 - 3f, Mid), Pt(BigR1 - 18f, Mid - 5f), Pt(BigR1 - 18f, Mid + 5f));

            var lab = Pt(BigR2 - 24f, Mid);
            var labRoot = Place(_bottom, "BigLabel", lab.x, lab.y, 0, 0);
            _bigLabel = labRoot;
            var pill = Place(labRoot, "Pill", 0, 0, 60, 22);
            UIShapes.AddShadow(pill, new Color(0, 0, 0, 0.3f), 11, 8, 3);
            UIShapes.AddRounded(Place(pill, "Edge", 0, 2, 0, 0).gameObject, YellowEdge, 11);
            StretchIn((RectTransform)pill.GetChild(1), 0, 2, 0, -2);
            StretchIn(UIShapes.AddRounded(Place(pill, "Fill", 0, 0, 0, 0).gameObject, Yellow, 11).rectTransform);
            _bigLabelText = Label(pill, 0, 0, 0, 0, "", 13, Ink, TextAlignmentOptions.Center);
            StretchIn(_bigLabelText.rectTransform);
        }

        /// <summary>自訂 Graphic 要先有 CanvasRenderer，否則不會畫，GraphicRaycaster 也會每幀丟例外。</summary>
        static T AddGraphic<T>(RectTransform rt) where T : Graphic
        {
            if (rt.GetComponent<CanvasRenderer>() == null) rt.gameObject.AddComponent<CanvasRenderer>();
            return rt.gameObject.AddComponent<T>();
        }

        RingSectorGraphic Sector(RectTransform parent, string name, float r1, float r2)
        {
            var g = AddGraphic<RingSectorGraphic>(Place(parent, name, 0, 0, DesignW, DesignH));
            g.center = Center;
            g.r1 = r1;
            g.r2 = r2;
            return g;
        }

        void BuildPet()
        {
            var host = Place(_top, "Pet", 4, 250, 200, 200);
            var shadow = Place(host, "Shadow", 42, 168, 120, 20);
            UIShapes.AddRounded(shadow.gameObject, new Color(30f / 255f, 45f / 255f, 20f / 255f, 0.3f), 10).raycastTarget = false;
            _pet = Place(host, "Sprite", 6, 0, 192, 180);
            _petImage = _pet.gameObject.AddComponent<Image>();
            _petImage.preserveAspect = true;
            _petImage.raycastTarget = false;
        }

        void BuildHeader()
        {
            var btn = Place(_top, "Me", DesignW - 14 - 44, 20, 44, 44);
            UIShapes.AddShadow(btn, new Color(30f / 255f, 40f / 255f, 20f / 255f, 0.3f), 22, 10, 4);
            var ring = UIShapes.AddRounded(Place(btn, "Border", 0, 0, 44, 44).gameObject, Paper, 22);
            var fill = UIShapes.AddRounded(Place(btn, "Fill", 2.5f, 2.5f, 39, 39).gameObject, Orange, 19.5f);
            fill.raycastTarget = false;
            Label(btn, 0, 0, 44, 44, "我", 15, Color.white, TextAlignmentOptions.Center);
            var b = btn.gameObject.AddComponent<Button>();
            b.targetGraphic = ring;
            b.onClick.AddListener(OnSettings);
        }

        void BuildInfo()
        {
            _info = Place(_top, "Info", 18, 22, 290, 120);
            _infoGroup = _info.gameObject.AddComponent<CanvasGroup>();
            _infoGroup.blocksRaycasts = false;

            _place = Label(_info, 0, 0, 200, 18, "", 12, Color.white, TextAlignmentOptions.Left, onScene: true);
            _place.enableWordWrapping = false;
            _infoBadge = Place(_info, "Badge", 0, 0, 40, 18);
            _infoBadgeEdge = UIShapes.AddRounded(Place(_infoBadge, "Edge", 0, 2, 0, 0).gameObject, CoralEdge, 8);
            StretchIn(_infoBadgeEdge.rectTransform, 0, 2, 0, -2);
            _infoBadgeBg = UIShapes.AddRounded(Place(_infoBadge, "Fill", 0, 0, 0, 0).gameObject, Coral, 8);
            StretchIn(_infoBadgeBg.rectTransform);
            _infoBadgeText = Label(_infoBadge, 0, 0, 0, 0, "", 11, Color.white, TextAlignmentOptions.Center);
            StretchIn(_infoBadgeText.rectTransform);
            _infoBadge.gameObject.AddComponent<PulseScale>();

            _name = Label(_info, 0, 24, 290, 42, "", 38, Color.white, TextAlignmentOptions.Left, onScene: true);
            _name.enableWordWrapping = false;
            _name.overflowMode = TextOverflowModes.Ellipsis;

            // 幾天大：半透明米白膠囊、白框（設計稿用營火小圖示，這裡先只放字）
            _ageChip = Place(_info, "Age", 0, 36, 60, 22);
            UIShapes.AddRounded(_ageChip.gameObject, new Color(1f, 1f, 1f, 0.6f), 11).raycastTarget = false;
            var ageFill = UIShapes.AddRounded(Place(_ageChip, "Fill", 0, 0, 0, 0).gameObject, new Color(1f, 0.973f, 0.925f, 0.28f), 10);
            ageFill.raycastTarget = false;
            StretchIn(ageFill.rectTransform, 1, 1, 1, 1);
            _ageText = Label(_ageChip, 0, 0, 0, 0, "", 12, Color.white, TextAlignmentOptions.Center, onScene: true);
            StretchIn(_ageText.rectTransform);
            _ageText.enableWordWrapping = false;

            _heartsRow = Place(_info, "Hearts", 0, 72, 95, 16);
            for (int i = 0; i < _hearts.Length; i++)
            {
                _hearts[i] = Place(_heartsRow, "Heart", i * 19f, 0, 17, 16).gameObject.AddComponent<Image>();
                _hearts[i].preserveAspect = true;
                _hearts[i].raycastTarget = false;
            }

            _evoRow = Place(_info, "Evo", 0, 96, 210, 16);
            var track = Place(_evoRow, "Track", 0, 4, 150, 8);
            UIShapes.AddRounded(track.gameObject, new Color(1f, 1f, 1f, 0.45f), 4).raycastTarget = false;
            track.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            _evoFill = Place(track, "Fill", 0, 0, 0, 8);
            var seg = Place(_evoFill, "Seg", 0, 0, 0, 8).gameObject.AddComponent<Image>();
            seg.raycastTarget = false;
            _evoSegs.Add(seg);
            _evoText = Label(_evoRow, 158, 0, 60, 16, "", 11, Color.white, TextAlignmentOptions.Left, onScene: true);
            _evoText.enableWordWrapping = false;

            _newText = Label(_info, 0, 72, 260, 40, "", 13, Color.white, TextAlignmentOptions.TopLeft, onScene: true);
        }

        void BuildHint()
        {
            var host = Place(_top, "Hint", 16, 466, 300, 22);
            var arc = AddGraphic<RingSectorGraphic>(Place(host, "Icon", 0, 0, 22, 22));
            // 對應 svg：r=8、粗 2.2 的圓弧（0°→309°）＋右上的直角箭頭；22px 圖示、原 24 單位
            float u = 22f / 24f;
            arc.center = new Vector2(12f, 12f) * u;
            arc.r1 = (8f - 1.1f) * u;
            arc.r2 = (8f + 1.1f) * u;
            arc.color = Color.white;
            arc.raycastTarget = false;
            arc.Set(0f, 309f, 0f);
            var v = Place(host, "ArrowV", (17f - 1.1f) * u, 2.5f * u, 2.2f * u, 3.8f * u).gameObject.AddComponent<Image>();
            var h = Place(host, "ArrowH", 13.2f * u, (6.3f - 1.1f) * u, 3.8f * u + 1.1f * u, 2.2f * u).gameObject.AddComponent<Image>();
            v.raycastTarget = h.raycastTarget = false;
            _hint = Label(host, 28, 2, 280, 18, "", 12, Color.white, TextAlignmentOptions.Left, onScene: true);
            _hint.enableWordWrapping = false;
        }

        void BuildCta()
        {
            _ctaRoot = Place(_bottom, "CtaRoot", 0, 0, DesignW, DesignH);
            var btn = Place(_ctaRoot, "Cta", 270, 652, 112, 112);
            UIShapes.AddShadow(btn, new Color(0, 0, 0, 0.35f), 56, 20, 10);
            UIShapes.AddRounded(Place(btn, "Edge", 0, 4, 112, 112).gameObject, YellowEdge, 56).raycastTarget = false;
            var border = UIShapes.AddRounded(Place(btn, "Border", 0, 0, 112, 112).gameObject, Cream, 56);
            UIShapes.AddRounded(Place(btn, "Fill", 4, 4, 104, 104).gameObject, Yellow, 52).raycastTarget = false;
            var icon = AddGraphic<UIPolygon>(Place(btn, "Play", 41, 29, 30, 30));
            float s = 30f / 24f;
            icon.color = Ink;
            icon.raycastTarget = false;
            icon.SetPoints(new Vector2(8f, 5.5f) * s, new Vector2(18f, 12f) * s, new Vector2(8f, 18.5f) * s);
            _ctaText = Label(btn, 0, 61, 112, 22, "進去", 16, Ink, TextAlignmentOptions.Center);
            var b = btn.gameObject.AddComponent<Button>();
            b.targetGraphic = border;
            b.onClick.AddListener(OnCta);
            btn.gameObject.AddComponent<ButtonScalePress>();
        }

        void BuildSheet()
        {
            _sheetLayer = Stretch(_root, "SheetLayer");
            _sheetGroup = _sheetLayer.gameObject.AddComponent<CanvasGroup>();
            var dim = Stretch(_sheetLayer, "Dim").gameObject.AddComponent<Image>();
            dim.color = new Color(20f / 255f, 24f / 255f, 14f / 255f, 0.45f);
            dim.gameObject.AddComponent<Button>().onClick.AddListener(() => CloseSheet());

            var go = new GameObject("Sheet", typeof(RectTransform));
            _sheet = (RectTransform)go.transform;
            _sheet.SetParent(_sheetLayer, false);
            _sheet.anchorMin = _sheet.anchorMax = new Vector2(0.5f, 0f);
            _sheet.pivot = new Vector2(0.5f, 0f);
            _sheet.sizeDelta = new Vector2(DesignW, SheetContentH + 26f);

            // 面板底色撐滿整個面板、往下多 30 藏住下方圓角；底色會擋住點擊，不會穿到暗色背景
            var panel = Place(_sheet, "Panel", 0, 0, 0, 0);
            StretchIn(panel, 0, 0, 0, -30);
            UIShapes.AddShadow(panel, new Color(0, 0, 0, 0.25f), 26, 30, -10);
            StretchIn(UIShapes.AddRounded(Place(panel, "Fill", 0, 0, 0, 0).gameObject, Paper, 26).rectTransform);
            UIShapes.AddRounded(Place(_sheet, "Handle", 175, 12, 40, 5).gameObject, Line, 2.5f).raycastTarget = false;

            Label(_sheet, 18, 39, 200, 28, "新增空間", 21, Ink, TextAlignmentOptions.Left);
            var close = Place(_sheet, "Close", DesignW - 18 - 44, 31, 44, 44);
            var closeBg = UIShapes.AddRounded(close.gameObject, Beige, 22);
            foreach (float z in new[] { 45f, -45f })
            {
                var bar = Place(close, "X", 22 - 6.5f, 22 - 1.2f, 13, 2.4f);
                bar.pivot = new Vector2(0.5f, 0.5f);
                bar.anchoredPosition = new Vector2(22f, -22f);
                bar.localRotation = Quaternion.Euler(0, 0, z);
                var img = bar.gameObject.AddComponent<Image>();
                img.color = Ink;
                img.raycastTarget = false;
            }
            var cb = close.gameObject.AddComponent<Button>();
            cb.targetGraphic = closeBg;
            cb.onClick.AddListener(() => CloseSheet());

            // 建立新空間
            var egg = Place(_sheet, "Create", 18, 89, 354, 88);
            UIShapes.AddShadow(egg, new Color(30f / 255f, 30f / 255f, 20f / 255f, 0.12f), 18, 12, 4);
            var eggBg = UIShapes.AddRounded(Place(egg, "Fill", 0, 0, 354, 88).gameObject, Cream, 18);
            var tile = Place(egg, "Tile", 12, 12, 64, 64);
            UIShapes.AddRounded(tile.gameObject, EggTile, 16).raycastTarget = false;
            UIShapes.AddRounded(Place(tile, "EggLine", 16, 9, 32, 38).gameObject, Ink, 16).raycastTarget = false;
            UIShapes.AddRounded(Place(tile, "Egg", 18.2f, 11.2f, 27.6f, 33.6f).gameObject, Cream, 13.8f).raycastTarget = false;
            UIShapes.AddRounded(Place(tile, "DotG", 24, 34, 4, 4).gameObject, Hex(0x6FA45F), 2).raycastTarget = false;
            UIShapes.AddRounded(Place(tile, "DotB", 33, 36, 4, 4).gameObject, Blue, 2).raycastTarget = false;
            UIShapes.AddRounded(Place(tile, "Band", 21, 26, 22, 3).gameObject, Hex(0xF29A3A), 1.5f).raycastTarget = false;
            Label(egg, 88, 24, 230, 20, "建立新空間", 15, Ink, TextAlignmentOptions.Left);
            Label(egg, 88, 47, 230, 16, "孵一顆蛋，邀家人朋友一起養", 11, Muted, TextAlignmentOptions.Left);
            Label(egg, 322, 34, 20, 20, ">", 16, Ink, TextAlignmentOptions.Center);
            var eb = egg.gameObject.AddComponent<Button>();
            eb.targetGraphic = eggBg;
            eb.onClick.AddListener(OnCreate);

            // 用邀請碼加入
            var join = Place(_sheet, "Join", 18, 191, 354, 131);
            UIShapes.AddShadow(join, new Color(30f / 255f, 30f / 255f, 20f / 255f, 0.12f), 18, 12, 4);
            UIShapes.AddRounded(Place(join, "Fill", 0, 0, 354, 131).gameObject, Cream, 18).raycastTarget = false;
            Label(join, 12, 12, 300, 20, "用邀請碼加入", 15, Ink, TextAlignmentOptions.Left);
            _joinHint = Label(join, 12, 40, 320, 16, "", 11, Muted, TextAlignmentOptions.Left);
            _codeInput = BuildInput(join, 12, 64, 250, 44);
            var go2 = Place(join, "JoinButton", 270, 64, 72, 44);
            UIShapes.AddRounded(Place(go2, "Edge", 0, 3, 72, 44).gameObject, YellowEdge, 13).raycastTarget = false;
            var jbBg = UIShapes.AddRounded(Place(go2, "Fill", 0, 0, 72, 44).gameObject, Yellow, 13);
            Label(go2, 0, 0, 72, 44, "加入", 14, Ink, TextAlignmentOptions.Center);
            var jb = go2.gameObject.AddComponent<Button>();
            jb.targetGraphic = jbBg;
            jb.onClick.AddListener(OnJoinCode);

            _sheetLayer.gameObject.SetActive(false);
        }

        TMP_InputField BuildInput(RectTransform parent, float x, float y, float w, float h)
        {
            var rt = Place(parent, "CodeInput", x, y, w, h);
            rt.gameObject.SetActive(false); // 先設定好 TMP_InputField 的欄位再啟用
            UIShapes.AddRounded(rt.gameObject, Line, 13);
            var inner = UIShapes.AddRounded(Place(rt, "Fill", 2, 2, w - 4, h - 4).gameObject, Color.white, 11);

            var area = Place(rt, "Text Area", 12, 0, w - 24, h);
            area.gameObject.AddComponent<RectMask2D>();
            var placeholder = Label(area, 0, 0, w - 24, h, "例如 PAW123", 15, new Color(Muted.r, Muted.g, Muted.b, 0.5f), TextAlignmentOptions.Left);
            var text = Label(area, 0, 0, w - 24, h, "", 15, Ink, TextAlignmentOptions.Left);
            text.characterSpacing = 8f;
            placeholder.enableWordWrapping = text.enableWordWrapping = false;

            var input = rt.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = inner;
            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.characterLimit = inviteCodeLength;
            input.contentType = TMP_InputField.ContentType.Alphanumeric;
            input.onValidateInput = (s, i, c) => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '\0';
            input.onSubmit.AddListener(_ => OnJoinCode());
            var f = Font();
            if (f != null) input.fontAsset = f;
            rt.gameObject.SetActive(true);
            return input;
        }

        /// <summary>營地圖縮小成 48px 寬再放大，當作 blur(10px)。</summary>
        void MakeBlurredBackground()
        {
            if (background == null || _bg == null) return;
            if (_blurred == null || !_blurred.IsCreated())
            {
                int w = 48, h = Mathf.Max(1, Mathf.RoundToInt(48f * background.height / Mathf.Max(1, background.width)));
                var mid = RenderTexture.GetTemporary(192, Mathf.Max(1, h * 4), 0);
                mid.filterMode = FilterMode.Bilinear;
                Graphics.Blit(background, mid);
                if (_blurred == null)
                {
                    _blurred = new RenderTexture(w, h, 0) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                }
                Graphics.Blit(mid, _blurred);
                RenderTexture.ReleaseTemporary(mid);
            }
            _bg.texture = _blurred;
            _bg.GetComponent<AspectRatioFitter>().aspectRatio = (float)background.width / Mathf.Max(1, background.height);
        }

        // ─── UI helpers（與 HandController 相同的設計座標寫法）─────────

        TMP_FontAsset Font() => skin != null ? skin.font : null;

        TMP_Text Label(RectTransform parent, float left, float top, float w, float h, string value, float size,
                       Color color, TextAlignmentOptions align, bool onScene = false)
        {
            var rt = Place(parent, "Text", left, top, w, h);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            var f = Font();
            if (f != null) t.font = f;
            t.text = value;
            t.fontSize = size;
            t.color = color;
            // Noto Sans TC Black 本身就是粗體；只有退回其他字型時才加粗
            t.fontStyle = f != null ? FontStyles.Normal : FontStyles.Bold;
            t.alignment = align;
            t.raycastTarget = false;
            t.enableWordWrapping = true;
            if (onScene && skin != null && skin.labelShadowMaterial != null) t.fontSharedMaterial = skin.labelShadowMaterial;
            return t;
        }

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
            StretchIn(rt);
            return rt;
        }

        static void StretchIn(RectTransform rt, float l = 0, float t = 0, float r = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
        }

        /// <summary>以左上角 (x, y) 擺放、設定大小（設計座標，y 向下）。</summary>
        static void SetSize(RectTransform rt, float w, float h, float x, float y)
        {
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
        }

        static Vector2 Pt(float r, float deg)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector2(Center.x + r * Mathf.Cos(a), Center.y + r * Mathf.Sin(a));
        }

        static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
