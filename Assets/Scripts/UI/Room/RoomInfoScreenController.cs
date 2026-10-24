using System;
using System.Collections.Generic;
using BlockPet.Hand;
using BlockPet.UI.Chat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Room
{
    /// <summary>
    /// 空間資訊（設計稿「設定」頁的 RoomInfo.dc.html、RoomInfoRename、RoomInfoLeave；取代舊的 Pet Settings）：
    /// 點狀態欄的寵物頭像打開（PageManager.ShowPetSettingPage）。
    /// - 寵物：頭像、名字、「空間名稱・出生 N 天」。
    /// - 邀請朋友一起養：6 格邀請碼（＝空間 id）、複製、分享給朋友（手機分享面板，見 <see cref="ShareText"/>）。
    /// - 成員：自己標「你」、建立者標「建立者」；最後一列「邀請朋友」。
    /// - 寵物與空間：寵物名字、空間名稱（點了從下方改名，10 字，所有成員都看得到）、生日。
    /// - 離開這個空間：再確認一次；寵物留給其他成員。只剩自己一個人時不能離開（要先邀請朋友加入）。
    /// 介面在執行時以 390 寬設計座標建立，依螢幕寬度等比縮放，內容可上下捲動。
    /// 由「BlockPet/Build Room Info」掛到 PetSettingPage，並關掉舊的 UI Toolkit 頁面。
    /// </summary>
    public class RoomInfoScreenController : MonoBehaviour
    {
        const float DesignW = 390f;
        const float SheetTop = 34f;
        const float HeaderH = 58f;
        const float PadX = 16f;
        const float ContentW = DesignW - PadX * 2f;
        const int NameMax = 10;

        static readonly Color Ink = Hex(0x3A2E22);
        static readonly Color Paper = Hex(0xFFF8EC);
        static readonly Color Card = Hex(0xFFFCF4);
        static readonly Color Beige = Hex(0xF2E9D8);
        static readonly Color Line = Hex(0xE2D6C0);
        static readonly Color Divider = Hex(0xEFE6D4);
        static readonly Color Muted = Hex(0x6B5D4E);
        static readonly Color Muted2 = Hex(0x8A6A4E);
        static readonly Color Chevron = Hex(0xB9AC96);
        static readonly Color Yellow = Hex(0xF6C945);
        static readonly Color YellowEdge = Hex(0xD9A92C);
        static readonly Color YellowSoft = Hex(0xFFF1C9);
        static readonly Color Coral = Hex(0xE86B4F);
        static readonly Color Red = Hex(0xC2452B);
        static readonly Color Grass = Hex(0x8FA868);

        [Header("Style")]
        [SerializeField] HandSkin skin;
        [SerializeField] Texture background;
        [SerializeField] Sprite[] petSprites;
        [SerializeField] int sortingOrder = 35;

        [Header("Links")]
        [SerializeField] PageManager pageManager;

        // 資料
        string _roomId = "";
        string _petName = "";
        string _roomName = "";
        string _ownerId;
        DateTime? _born;
        List<FirebaseManager.RoomMemberInfo> _members = new List<FirebaseManager.RoomMemberInfo>();
        bool _busy;
        bool _renamePet;

        // 版面
        Canvas _canvas;
        RectTransform _root, _design, _sheet, _viewport, _content;
        ScrollRect _scroll;
        RenderTexture _blurred;
        float _lastW = -1f, _lastH = -1f;
        Rect _lastSafe;
        bool _dirty = true;
        float _k = 1f, _screenH = 772f, _topInset, _bottomInset, _openT;

        // 內容
        RectTransform _petImg;
        Image _petImage;
        TMP_Text _petNameText, _petSub;
        readonly List<TMP_Text> _codeChars = new List<TMP_Text>();
        TMP_Text _inviteNote;
        TMP_Text _membersTitle;
        RectTransform _membersCard;
        TMP_Text _petNameValue, _roomNameValue, _birthValue;
        TMP_Text _leaveSub;

        // 改名
        RectTransform _renameLayer, _renamePanel;
        CanvasGroup _renameGroup;
        TMP_Text _renameTitle, _renameHint, _renameCount, _renameStatus;
        TMP_InputField _renameInput;
        bool _renameOpen;
        float _renameT;

        // 離開
        RectTransform _leaveLayer, _leaveCard;
        CanvasGroup _leaveGroup;
        TMP_Text _leaveTitle, _leaveBody, _leaveButtonText, _keepText;
        bool _leaveOpen;
        float _leaveT;
        bool _lastMember;

        RectTransform _toast;
        CanvasGroup _toastGroup;
        TMP_Text _toastText;
        float _toastUntil;

        // ─── Lifecycle ─────────────────────────────────────────────────

        void Awake()
        {
            if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
            Build();
        }

        void OnEnable()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(true);
            MakeBackground();
            _busy = false;
            _openT = 0f;
            CloseRename(instant: true);
            CloseLeave(instant: true);
            _toastGroup.alpha = 0f;
            _scroll.verticalNormalizedPosition = 1f;
            Load();
        }

        void OnDisable()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_blurred != null) { _blurred.Release(); Destroy(_blurred); }
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            Relayout();

            _openT = Mathf.MoveTowards(_openT, 1f, dt / 0.3f);
            float e = 1f - Mathf.Pow(1f - _openT, 3f);
            _sheet.anchoredPosition = new Vector2(0f, -(_topInset + SheetTop + (1f - e) * 200f));
            _sheet.GetComponent<CanvasGroup>().alpha = Mathf.Clamp01(_openT * 2f);

            // 寵物輕輕跳（sBob 2.4s 4px）
            float bob = 4f * (0.5f - 0.5f * Mathf.Cos(Time.unscaledTime / 2.4f * Mathf.PI * 2f));
            _petImg.anchoredPosition = new Vector2(9f, -14f + bob);

            AnimateRename(dt);
            AnimateLeave(dt);
            _toastGroup.alpha = Mathf.MoveTowards(_toastGroup.alpha, Time.unscaledTime < _toastUntil ? 1f : 0f, dt / 0.2f);
        }

        // ─── 資料 ──────────────────────────────────────────────────────

        void Load()
        {
            var fb = FirebaseManager.Instance;
            _roomId = fb != null ? fb.RoomId : "";
            _petImage.sprite = PetSprite();
            _petImage.enabled = _petImage.sprite != null;
            for (int i = 0; i < _codeChars.Count; i++)
                _codeChars[i].text = i < _roomId.Length ? _roomId.Substring(i, 1) : "";
            Refresh();
            if (fb == null || string.IsNullOrEmpty(_roomId)) return;

            string room = _roomId;
            fb.GetRoomMeta(room, meta =>
            {
                if (this == null || room != _roomId) return;
                _roomName = meta != null && !string.IsNullOrEmpty(meta.name) ? meta.name : room;
                _petName = meta != null && !string.IsNullOrEmpty(meta.petName) ? meta.petName : _roomName;
                _ownerId = meta?.ownerUserId;
                _born = meta != null && DateTime.TryParse(meta.createdAt, out var t) ? t.ToLocalTime() : (DateTime?)null;
                Refresh();
                BuildMembers();
            });
            fb.GetRoomMembers(room, list =>
            {
                if (this == null || room != _roomId) return;
                _members = list ?? new List<FirebaseManager.RoomMemberInfo>();
                BuildMembers();
            });
        }

        void Refresh()
        {
            string pet = string.IsNullOrEmpty(_petName) ? "寵物" : _petName;
            _petNameText.text = pet;
            int days = _born.HasValue ? Mathf.Max(0, (int)(DateTime.Now - _born.Value).TotalDays) : 0;
            _petSub.text = string.IsNullOrEmpty(_roomName) ? "" : $"{_roomName}・出生 {days} 天";
            _inviteNote.text = $"朋友在「新增空間」輸入這組碼，就能一起養 {pet}";
            _petNameValue.text = pet;
            _roomNameValue.text = _roomName;
            _birthValue.text = _born.HasValue ? _born.Value.ToString("yyyy/MM/dd") : "—";
            _leaveSub.text = $"離開後就看不到 {pet} 和營地照片";
        }

        Sprite PetSprite()
        {
            if (petSprites == null || petSprites.Length == 0) return null;
            int i = PetCollectionManager.Instance != null ? PetCollectionManager.Instance.CurrentPetIndex : 0;
            return petSprites[Mathf.Abs(i) % petSprites.Length];
        }

        // ─── 操作 ──────────────────────────────────────────────────────

        void Close()
        {
            if (_busy) return;
            if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
            pageManager?.ShowHomePage();
        }

        void CopyCode()
        {
            if (string.IsNullOrEmpty(_roomId)) return;
            GUIUtility.systemCopyBuffer = _roomId;
            Toast("已複製邀請碼 " + _roomId);
        }

        void Share()
        {
            if (string.IsNullOrEmpty(_roomId)) return;
            string pet = string.IsNullOrEmpty(_petName) ? "寵物" : _petName;
            string text = $"來 BlockPet 和我一起養 {pet}！打開 App 到「新增空間」，輸入邀請碼：{_roomId}";
            if (!ShareText.Send(text)) Toast("已複製邀請訊息，貼給朋友吧");
        }

        void OpenRename(bool pet)
        {
            _renamePet = pet;
            _renameTitle.text = pet ? "寵物名字" : "空間名稱";
            _renameHint.text = pet ? "大家都會這樣叫牠" : "轉盤上會顯示這個名字";
            string cur = pet ? _petName : _roomName;
            _renameInput.text = cur.Length > NameMax ? cur.Substring(0, NameMax) : cur;
            _renameStatus.text = "改名後，所有成員都會看到新名字";
            _renameStatus.color = Muted2;
            _renameOpen = true;
            _renameLayer.gameObject.SetActive(true);
        }

        void SaveName()
        {
            if (_busy) return;
            string v = (_renameInput.text ?? "").Trim();
            if (v.Length == 0) { CloseRename(); return; }
            var fb = FirebaseManager.Instance;
            if (fb == null || string.IsNullOrEmpty(_roomId)) return;
            _busy = true;
            Action<bool> done = ok =>
            {
                _busy = false;
                if (this == null) return;
                if (!ok)
                {
                    _renameStatus.text = "儲存失敗，請再試一次";
                    _renameStatus.color = Red;
                    return;
                }
                if (_renamePet) _petName = v; else _roomName = v;
                Refresh();
                CloseRename();
                Toast("已改名，成員都會看到");
            };
            if (_renamePet) fb.SetRoomPetName(_roomId, v, done);
            else fb.SetRoomName(_roomId, v, done);
        }

        void OpenLeave()
        {
            _lastMember = _members.Count <= 1;
            string pet = string.IsNullOrEmpty(_petName) ? "寵物" : _petName;
            // 只剩自己一個人：不能離開（不然寵物就沒人養了），只有「知道了」
            _leaveTitle.text = _lastMember ? "現在還不能離開" : $"確定要離開{_roomName}？";
            _leaveBody.text = _lastMember
                ? $"・你是這個空間唯一的成員，離開後 {pet} 就沒人養了。\n・先用上方的「邀請朋友一起養」找朋友加入，之後就能離開。"
                : $"・{pet} 會留給其他成員繼續養。\n・你餵過的照片會留在營地相簿。\n・想回來的話，請成員再給你一次邀請碼。";
            _keepText.text = _lastMember ? "知道了" : "先不要";
            _leaveCard.Find("Leave").gameObject.SetActive(!_lastMember);
            float th = Mathf.Ceil(_leaveBody.GetPreferredValues(_leaveBody.text, _leaveBody.rectTransform.rect.width, 0f).y);
            ((RectTransform)_leaveBody.transform.parent).sizeDelta = new Vector2(((RectTransform)_leaveBody.transform.parent).sizeDelta.x, th + 24f);
            _leaveBody.rectTransform.sizeDelta = new Vector2(_leaveBody.rectTransform.sizeDelta.x, th);
            _leaveButtonText.text = "離開空間";
            LayoutLeave();
            _leaveOpen = true;
            _leaveLayer.gameObject.SetActive(true);
        }

        void ConfirmLeave()
        {
            var fb = FirebaseManager.Instance;
            if (fb == null || _busy || string.IsNullOrEmpty(_roomId) || _lastMember) return;
            _busy = true;
            _leaveButtonText.text = "處理中…";
            string room = _roomId;
            Action<bool, string> done = (ok, err) =>
            {
                _busy = false;
                if (this == null) return;
                if (!ok)
                {
                    Debug.LogWarning("[RoomInfo] leave failed: " + err);
                    _leaveButtonText.text = "離開空間";
                    Toast("離開失敗，請再試一次");
                    return;
                }
                // 回到選擇空間（轉盤）
                fb.SetRoomId("");
                CloseLeave(instant: true);
                if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
                pageManager?.ShowRoomPage();
            };
            fb.LeaveRoom(room, done);
        }

        void Toast(string msg)
        {
            _toastText.text = msg;
            float w = Mathf.Min(DesignW - 40f, _toastText.GetPreferredValues(msg).x + 32f);
            _toast.sizeDelta = new Vector2(w, 40f);
            _toast.anchoredPosition = new Vector2((DesignW - w) / 2f, _toast.anchoredPosition.y);
            _toastUntil = Time.unscaledTime + 1.8f;
        }

        void CloseRename(bool instant = false)
        {
            if (_busy && !instant) return;
            _renameOpen = false;
            if (instant && _renameLayer != null) { _renameT = 0f; _renameLayer.gameObject.SetActive(false); }
        }

        void CloseLeave(bool instant = false)
        {
            if (_busy && !instant) return;
            _leaveOpen = false;
            if (instant && _leaveLayer != null) { _leaveT = 0f; _leaveLayer.gameObject.SetActive(false); }
        }

        void AnimateRename(float dt)
        {
            if (!_renameLayer.gameObject.activeSelf) return;
            _renameT = Mathf.MoveTowards(_renameT, _renameOpen ? 1f : 0f, dt / 0.3f);
            float e = 1f - Mathf.Pow(1f - _renameT, 3f);
            _renameGroup.alpha = e;
            _renameGroup.blocksRaycasts = _renameOpen;
            _renamePanel.anchoredPosition = new Vector2(0f, -(1f - e) * _renamePanel.rect.height * _renamePanel.localScale.y);
            if (!_renameOpen && _renameT <= 0f) _renameLayer.gameObject.SetActive(false);
        }

        void AnimateLeave(float dt)
        {
            if (!_leaveLayer.gameObject.activeSelf) return;
            _leaveT = Mathf.MoveTowards(_leaveT, _leaveOpen ? 1f : 0f, dt / 0.25f);
            _leaveGroup.alpha = _leaveT;
            _leaveGroup.blocksRaycasts = _leaveOpen;
            _leaveCard.localScale = Vector3.one * _k * Mathf.Lerp(0.9f, 1f, _leaveT);
            if (!_leaveOpen && _leaveT <= 0f) _leaveLayer.gameObject.SetActive(false);
        }

        // ─── 成員 ──────────────────────────────────────────────────────

        void BuildMembers()
        {
            for (int i = _membersCard.childCount - 1; i >= 2; i--) Destroy(_membersCard.GetChild(i).gameObject);
            string me = FirebaseManager.Instance != null ? FirebaseManager.Instance.GetUserId() : null;
            // 自己排第一、建立者第二
            var list = new List<FirebaseManager.RoomMemberInfo>(_members);
            list.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
            int Rank(FirebaseManager.RoomMemberInfo m) => m.uid == me ? 0 : (m.uid == _ownerId ? 1 : 2);

            float y = 0f;
            foreach (var m in list)
            {
                bool mine = m.uid == me;
                string name = mine ? "我" : (string.IsNullOrEmpty(m.nickname) ? "成員" : m.nickname);
                var row = Place(_membersCard, "Member", 0, y, ContentW, 56);
                var av = Place(row, "Avatar", 14, 10, 36, 36);
                UIShapes.AddRounded(av.gameObject, ChatScreenController.PersonColor(name, mine), 18).raycastTarget = false;
                Label(av, 0, 0, 36, 36, ChatScreenController.Initial(name, mine), 14, Color.white, TextAlignmentOptions.Center);
                var n = Label(row, 62, 0, ContentW - 62f - 90f, 56, name, 15, Ink, TextAlignmentOptions.Left);
                n.enableWordWrapping = false;
                n.overflowMode = TextOverflowModes.Ellipsis;
                string tag = mine ? "你" : (m.uid == _ownerId ? "建立者" : null);
                if (mine && m.uid == _ownerId) tag = "你・建立者";
                if (tag != null)
                {
                    bool owner = tag != "你";
                    var t = Label(row, 0, 0, 80, 18, tag, 11, owner ? Hex(0x4A7A3F) : Muted, TextAlignmentOptions.Center);
                    float tw = t.GetPreferredValues(tag).x + 16f;
                    var chip = Place(row, "Tag", ContentW - 14f - tw, 19, tw, 18);
                    UIShapes.AddRounded(chip.gameObject, owner ? Hex(0xE3EED9) : Beige, 8).raycastTarget = false;
                    t.rectTransform.SetParent(chip, false);
                    t.rectTransform.anchoredPosition = Vector2.zero;
                    t.rectTransform.sizeDelta = new Vector2(tw, 18f);
                }
                var d = Place(row, "Line", 0, 55, ContentW, 1).gameObject.AddComponent<Image>();
                d.color = Divider;
                d.raycastTarget = false;
                y += 56f;
            }

            // 邀請朋友（虛線圓圈＋）
            var invite = Place(_membersCard, "Invite", 0, y, ContentW, 56);
            var hit = invite.gameObject.AddComponent<Image>();
            hit.color = new Color(1, 1, 1, 0);
            UIShapes.AddRing(Place(invite, "Ring", 14, 10, 36, 36).gameObject, Hex(0xC9BB9F), 18);
            foreach (var (w, h) in new[] { (14f, 2.6f), (2.6f, 14f) })
            {
                var bar = Place(invite, "Plus", 14f + (36f - w) / 2f, 10f + (36f - h) / 2f, w, h).gameObject.AddComponent<Image>();
                bar.color = Muted2;
                bar.raycastTarget = false;
            }
            Label(invite, 62, 0, 200, 56, "邀請朋友", 15, Muted2, TextAlignmentOptions.Left);
            var b = invite.gameObject.AddComponent<Button>();
            b.targetGraphic = hit;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(Share);
            y += 56f;

            _membersCard.sizeDelta = new Vector2(ContentW, y);
            ((RectTransform)_membersCard.parent).sizeDelta = new Vector2(ContentW, 24f + y);
            _membersTitle.text = $"成員 {list.Count}";
            _dirty = true;
        }

        // ─── 版面 ──────────────────────────────────────────────────────

        void Relayout()
        {
            float w = _root.rect.width, h = _root.rect.height;
            var safe = Screen.safeArea;
            if (w <= 0f || h <= 0f) return;
            if (!_dirty && Mathf.Approximately(w, _lastW) && Mathf.Approximately(h, _lastH) && safe == _lastSafe) return;
            _dirty = false;
            _lastW = w; _lastH = h; _lastSafe = safe;

            _k = w / DesignW;
            float scale = _canvas.rootCanvas.scaleFactor;
            float px = scale > 0f ? 1f / scale : 1f;
            _topInset = (Screen.height - safe.yMax) * px / _k;
            _bottomInset = safe.yMin * px / _k;
            _screenH = h / _k;

            _design.localScale = Vector3.one * _k;
            _design.sizeDelta = new Vector2(DesignW, _screenH);
            float sheetH = _screenH - _topInset - SheetTop;
            _sheet.sizeDelta = new Vector2(DesignW, sheetH);
            _viewport.sizeDelta = new Vector2(DesignW, sheetH - HeaderH);

            float y = Stack(_content, 18f, 2f);
            _content.sizeDelta = new Vector2(DesignW, y + 28f + _bottomInset);

            _renamePanel.localScale = Vector3.one * _k;
            _renamePanel.sizeDelta = new Vector2(DesignW, RenameH + _bottomInset);
            _leaveCard.localScale = Vector3.one * _k;
            _toast.anchoredPosition = new Vector2(_toast.anchoredPosition.x, -(_screenH - 40f - 40f - _bottomInset));
        }

        static float Stack(RectTransform parent, float gap, float top)
        {
            float y = top;
            bool first = true;
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = (RectTransform)parent.GetChild(i);
                if (!c.gameObject.activeSelf) continue;
                if (!first) y += gap;
                first = false;
                c.anchoredPosition = new Vector2(c.anchoredPosition.x, -y);
                y += c.sizeDelta.y;
            }
            return y;
        }

        // ─── 建立介面 ──────────────────────────────────────────────────

        void Build()
        {
            // 獨立的最上層 Canvas（不受頁面上層 CanvasGroup 影響），顯示／隱藏由 OnEnable／OnDisable 同步
            var go = new GameObject("RoomInfoScreen (runtime)", typeof(RectTransform));
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = sortingOrder;
            go.AddComponent<GraphicRaycaster>();
            _root = (RectTransform)go.transform;

            Stretch(_root, "Background").gameObject.AddComponent<Image>().color = Grass;
            var photo = Stretch(_root, "BackgroundPhoto").gameObject.AddComponent<RawImage>();
            photo.raycastTarget = false;
            photo.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            Stretch(_root, "Dim").gameObject.AddComponent<Image>().color = new Color(20f / 255f, 24f / 255f, 14f / 255f, 0.4f);

            _design = new GameObject("Design", typeof(RectTransform)).GetComponent<RectTransform>();
            _design.SetParent(_root, false);
            _design.anchorMin = _design.anchorMax = new Vector2(0.5f, 1f);
            _design.pivot = new Vector2(0.5f, 1f);
            _design.sizeDelta = new Vector2(DesignW, 772f);

            BuildSheet();
            BuildContent();
            BuildRename();
            BuildLeave();
            BuildToast();
            go.SetActive(false); // 元件啟用時（OnEnable）才顯示
        }

        void BuildSheet()
        {
            _sheet = Place(_design, "Sheet", 0, SheetTop, DesignW, 700);
            _sheet.gameObject.AddComponent<CanvasGroup>();
            UIShapes.AddShadow(_sheet, new Color(0, 0, 0, 0.25f), 26, 30, -10);
            var fill = UIShapes.AddRounded(Place(_sheet, "Fill", 0, 0, 0, 0).gameObject, Paper, 26);
            StretchIn(fill.rectTransform, 0, 0, 0, -30);
            UIShapes.AddRounded(Place(_sheet, "Handle", 175, 8, 40, 5).gameObject, Line, 2.5f).raycastTarget = false;
            Label(_sheet, 0, 14, DesignW, 30, "空間資訊", 22, Ink, TextAlignmentOptions.Center);
            CloseButton(_sheet, DesignW - 12f - 44f, 8f, Close);

            _viewport = Place(_sheet, "Viewport", 0, HeaderH, DesignW, 600);
            _viewport.gameObject.AddComponent<RectMask2D>();
            _viewport.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0);
            _content = Place(_viewport, "Content", 0, 0, DesignW, 1000);
            _scroll = _viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = _viewport;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Elastic;
            _scroll.scrollSensitivity = 20f;
        }

        void BuildContent()
        {
            // 寵物
            var pet = Place(_content, "Pet", 0, 0, DesignW, 88 + 6 + 30 + 6 + 17);
            var av = Place(pet, "Avatar", (DesignW - 88f) / 2f, 0, 88, 88);
            UIShapes.AddShadow(av, new Color(30f / 255f, 30f / 255f, 20f / 255f, 0.15f), 44, 12, 4);
            UIShapes.AddRounded(Place(av, "Border", 0, 0, 88, 88).gameObject, Card, 44).raycastTarget = false;
            var disc = Place(av, "Disc", 3, 3, 82, 82);
            UIShapes.AddRounded(disc.gameObject, Hex(0xFCE7A6), 41).raycastTarget = false;
            disc.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            _petImg = Place(disc, "Img", 9, 14, 64, 56);
            _petImage = _petImg.gameObject.AddComponent<Image>();
            _petImage.preserveAspect = true;
            _petImage.raycastTarget = false;
            _petNameText = Label(pet, 0, 94, DesignW, 30, "", 26, Ink, TextAlignmentOptions.Center);
            _petNameText.enableWordWrapping = false;
            _petSub = Label(pet, 0, 130, DesignW, 17, "", 12, Muted, TextAlignmentOptions.Center);
            _petSub.enableWordWrapping = false;

            // 邀請朋友一起養
            var inv = Place(_content, "Invite", PadX, 0, ContentW, 188);
            UIShapes.AddRounded(inv.gameObject, Yellow, 18).raycastTarget = false;
            StretchIn(UIShapes.AddRounded(Place(inv, "Fill", 0, 0, 0, 0).gameObject, YellowSoft, 16.5f).rectTransform, 1.5f, 1.5f, 1.5f, 1.5f);
            Label(inv, 14, 14, 200, 20, "邀請朋友一起養", 15, Ink, TextAlignmentOptions.Left);
            Label(inv, 14, 16, ContentW - 28f, 18, "邀請碼", 11, Muted2, TextAlignmentOptions.Right);
            float codeW = 6 * 42f + 5 * 6f, cx = (ContentW - codeW) / 2f;
            for (int i = 0; i < 6; i++)
            {
                var tile = Place(inv, "Code" + i, cx + i * 48f, 44, 42, 50);
                UIShapes.AddRounded(Place(tile, "Edge", 0, 2, 42, 50).gameObject, Hex(0xE8D9A8), 12).raycastTarget = false;
                UIShapes.AddRounded(Place(tile, "Fill", 0, 0, 42, 50).gameObject, Card, 12).raycastTarget = false;
                _codeChars.Add(Label(tile, 0, 0, 42, 50, "", 26, Ink, TextAlignmentOptions.Center));
            }
            float bw = ContentW - 28f - 8f, copyW = bw / 2.4f, shareW = bw - copyW;
            var copy = Place(inv, "Copy", 14, 104, copyW, 44);
            UIShapes.AddRounded(copy.gameObject, Line, 13).raycastTarget = false;
            var cbg = UIShapes.AddRounded(Place(copy, "Fill", 1.5f, 1.5f, copyW - 3f, 41).gameObject, Color.white, 11.5f);
            Label(copy, 0, 0, copyW, 44, "複製", 14, Ink, TextAlignmentOptions.Center);
            var cb = copy.gameObject.AddComponent<Button>();
            cb.targetGraphic = cbg;
            cb.onClick.AddListener(CopyCode);
            var share = Place(inv, "Share", 14 + copyW + 8f, 104, shareW, 44);
            UIShapes.AddRounded(Place(share, "Edge", 0, 3, shareW, 44).gameObject, YellowEdge, 13).raycastTarget = false;
            var sbg = UIShapes.AddRounded(Place(share, "Fill", 0, 0, shareW, 44).gameObject, Yellow, 13);
            Label(share, 0, 0, shareW, 44, "分享給朋友", 14, Ink, TextAlignmentOptions.Center);
            var sb = share.gameObject.AddComponent<Button>();
            sb.targetGraphic = sbg;
            sb.onClick.AddListener(Share);
            share.gameObject.AddComponent<ButtonScalePress>();
            _inviteNote = Label(inv, 14, 158, ContentW - 28f, 16, "", 11, Muted, TextAlignmentOptions.Center);
            _inviteNote.enableWordWrapping = false;

            // 成員
            var mem = Place(_content, "Members", PadX, 0, ContentW, 100);
            _membersTitle = Label(mem, 6, 0, ContentW - 6f, 17, "成員", 12, Muted, TextAlignmentOptions.Left);
            _membersCard = CardBox(mem, 24f);

            // 寵物與空間
            var info = Place(_content, "Info", PadX, 0, ContentW, 24 + 56 * 3);
            Label(info, 6, 0, ContentW - 6f, 17, "寵物與空間", 12, Muted, TextAlignmentOptions.Left);
            var card = CardBox(info, 24f);
            _petNameValue = InfoRow(card, 0, "寵物名字", () => OpenRename(true), true);
            _roomNameValue = InfoRow(card, 56, "空間名稱", () => OpenRename(false), true);
            _birthValue = InfoRow(card, 112, "生日", null, false);
            card.sizeDelta = new Vector2(ContentW, 168f);

            // 離開
            var leave = Place(_content, "Leave", PadX, 0, ContentW, 56);
            UIShapes.AddShadow(leave, new Color(30f / 255f, 30f / 255f, 20f / 255f, 0.08f), 18, 10, 3);
            var lbg = UIShapes.AddRounded(Place(leave, "Fill", 0, 0, ContentW, 56).gameObject, Card, 18);
            var icon = Place(leave, "Icon", 14, 11, 34, 34);
            UIShapes.AddRounded(icon.gameObject, Coral, 11).raycastTarget = false;
            Label(icon, 0, 0, 34, 34, "離", 15, Color.white, TextAlignmentOptions.Center);
            Label(leave, 60, 9, ContentW - 100f, 22, "離開這個空間", 15, Red, TextAlignmentOptions.Left);
            _leaveSub = Label(leave, 60, 31, ContentW - 100f, 16, "", 11, Muted2, TextAlignmentOptions.Left);
            _leaveSub.enableWordWrapping = false;
            Label(leave, ContentW - 30f, 0, 16, 56, ">", 14, Chevron, TextAlignmentOptions.Center);
            var lb = leave.gameObject.AddComponent<Button>();
            lb.targetGraphic = lbg;
            lb.transition = Selectable.Transition.None;
            lb.onClick.AddListener(OpenLeave);
        }

        RectTransform CardBox(RectTransform parent, float top)
        {
            var card = Place(parent, "Card", 0, top, ContentW, 56);
            UIShapes.AddShadow(card, new Color(30f / 255f, 30f / 255f, 20f / 255f, 0.08f), 18, 10, 3);
            var fill = UIShapes.AddRounded(Place(card, "Fill", 0, 0, 0, 0).gameObject, Card, 18);
            fill.raycastTarget = false;
            StretchIn(fill.rectTransform);
            return card;
        }

        TMP_Text InfoRow(RectTransform card, float y, string label, Action onClick, bool arrow)
        {
            var row = Place(card, label, 0, y, ContentW, 56);
            Label(row, 14, 0, 120, 56, label, 15, Ink, TextAlignmentOptions.Left);
            var value = Label(row, ContentW - 14f - 18f - 160f, 0, 160, 56, "", 13, Muted, TextAlignmentOptions.Right);
            value.enableWordWrapping = false;
            value.overflowMode = TextOverflowModes.Ellipsis;
            if (arrow) Label(row, ContentW - 14f - 12f, 0, 12, 56, ">", 14, Chevron, TextAlignmentOptions.Center);
            if (y > 0f)
            {
                var d = Place(row, "Line", 0, 0, ContentW, 1).gameObject.AddComponent<Image>();
                d.color = Divider;
                d.raycastTarget = false;
            }
            if (onClick != null)
            {
                var hit = row.gameObject.AddComponent<Image>();
                hit.color = new Color(1, 1, 1, 0);
                var b = row.gameObject.AddComponent<Button>();
                b.targetGraphic = hit;
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => onClick());
            }
            return value;
        }

        const float RenameH = 12f + 5f + 12f + 28f + 12f + 17f + 12f + 48f + 12f + 50f + 12f + 16f + 26f;

        void BuildRename()
        {
            _renameLayer = Stretch(_root, "RenameLayer");
            _renameGroup = _renameLayer.gameObject.AddComponent<CanvasGroup>();
            var dim = Stretch(_renameLayer, "Dim").gameObject.AddComponent<Image>();
            dim.color = new Color(20f / 255f, 24f / 255f, 14f / 255f, 0.45f);
            dim.gameObject.AddComponent<Button>().onClick.AddListener(() => CloseRename());

            _renamePanel = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
            _renamePanel.SetParent(_renameLayer, false);
            _renamePanel.anchorMin = _renamePanel.anchorMax = new Vector2(0.5f, 0f);
            _renamePanel.pivot = new Vector2(0.5f, 0f);
            _renamePanel.sizeDelta = new Vector2(DesignW, RenameH);
            var bg = Place(_renamePanel, "Bg", 0, 0, 0, 0);
            StretchIn(bg, 0, 0, 0, -30);
            UIShapes.AddShadow(bg, new Color(0, 0, 0, 0.25f), 26, 30, -10);
            StretchIn(UIShapes.AddRounded(Place(bg, "Fill", 0, 0, 0, 0).gameObject, Paper, 26).rectTransform);
            UIShapes.AddRounded(Place(_renamePanel, "Handle", 175, 12, 40, 5).gameObject, Line, 2.5f).raycastTarget = false;

            float w = DesignW - 36f, y = 29f;
            _renameTitle = Label(_renamePanel, 18, y, w, 28, "", 21, Ink, TextAlignmentOptions.Left);
            y += 28f + 12f;
            _renameHint = Label(_renamePanel, 18, y, w, 17, "", 12, Muted, TextAlignmentOptions.Left);
            _renameCount = Label(_renamePanel, 18, y, w, 17, "0 / " + NameMax, 12, Muted, TextAlignmentOptions.Right);
            y += 17f + 12f;
            _renameInput = Input(_renamePanel, 18, y, w);
            _renameInput.characterLimit = NameMax;
            _renameInput.onValueChanged.AddListener(v => _renameCount.text = (v ?? "").Length + " / " + NameMax);
            y += 48f + 12f;

            float cancelW = (w - 8f) / 2.5f, saveW = w - cancelW - 8f;
            var cancel = Place(_renamePanel, "Cancel", 18, y, cancelW, 50);
            var cbg = UIShapes.AddRounded(cancel.gameObject, Beige, 15);
            Label(cancel, 0, 0, cancelW, 50, "取消", 15, Ink, TextAlignmentOptions.Center);
            var cb = cancel.gameObject.AddComponent<Button>();
            cb.targetGraphic = cbg;
            cb.onClick.AddListener(() => CloseRename());
            var save = Place(_renamePanel, "Save", 18 + cancelW + 8f, y, saveW, 50);
            UIShapes.AddRounded(Place(save, "Edge", 0, 3, saveW, 50).gameObject, YellowEdge, 15).raycastTarget = false;
            var sbg = UIShapes.AddRounded(Place(save, "Fill", 0, 0, saveW, 50).gameObject, Yellow, 15);
            Label(save, 0, 0, saveW, 50, "儲存", 15, Ink, TextAlignmentOptions.Center);
            var sb = save.gameObject.AddComponent<Button>();
            sb.targetGraphic = sbg;
            sb.onClick.AddListener(SaveName);
            y += 50f + 12f;
            _renameStatus = Label(_renamePanel, 18, y, w, 16, "", 11, Muted2, TextAlignmentOptions.Center);

            _renameLayer.gameObject.SetActive(false);
        }

        void BuildLeave()
        {
            _leaveLayer = Stretch(_root, "LeaveLayer");
            _leaveGroup = _leaveLayer.gameObject.AddComponent<CanvasGroup>();
            var dim = Stretch(_leaveLayer, "Dim").gameObject.AddComponent<Image>();
            dim.color = new Color(20f / 255f, 24f / 255f, 14f / 255f, 0.55f);
            dim.gameObject.AddComponent<Button>().onClick.AddListener(() => CloseLeave());

            const float w = DesignW - 48f, inner = w - 36f;
            _leaveCard = new GameObject("Dialog", typeof(RectTransform)).GetComponent<RectTransform>();
            _leaveCard.SetParent(_leaveLayer, false);
            _leaveCard.anchorMin = _leaveCard.anchorMax = new Vector2(0.5f, 0.5f);
            _leaveCard.pivot = new Vector2(0.5f, 0.5f);
            UIShapes.AddShadow(_leaveCard, new Color(0, 0, 0, 0.35f), 24, 40, 16);
            var bgRt = Place(_leaveCard, "Bg", 0, 0, 0, 0);
            StretchIn(bgRt);
            UIShapes.AddRounded(bgRt.gameObject, Paper, 24);

            var icon = Place(_leaveCard, "Icon", (w - 56f) / 2f, 0, 56, 56);
            UIShapes.AddRounded(icon.gameObject, Hex(0xFCE3DC), 28).raycastTarget = false;
            Label(icon, 0, 0, 56, 56, "離", 22, Red, TextAlignmentOptions.Center);
            var title = Place(_leaveCard, "Title", 18, 0, inner, 28);
            _leaveTitle = Label(title, 0, 0, inner, 28, "", 21, Ink, TextAlignmentOptions.Center);
            _leaveTitle.enableWordWrapping = false;
            _leaveTitle.enableAutoSizing = true;
            _leaveTitle.fontSizeMin = 15;
            _leaveTitle.fontSizeMax = 21;
            var box = Place(_leaveCard, "Box", 18, 0, inner, 100);
            UIShapes.AddRounded(box.gameObject, Card, 14).raycastTarget = false;
            _leaveBody = Label(box, 14, 12, inner - 28f, 76, "", 13, Muted, TextAlignmentOptions.TopLeft);
            _leaveBody.richText = true;
            _leaveBody.lineSpacing = 10f;
            var keep = Place(_leaveCard, "Keep", 18, 0, inner, 53);
            UIShapes.AddRounded(Place(keep, "Edge", 0, 3, inner, 50).gameObject, YellowEdge, 15).raycastTarget = false;
            var kbg = UIShapes.AddRounded(Place(keep, "Fill", 0, 0, inner, 50).gameObject, Yellow, 15);
            _keepText = Label(keep, 0, 0, inner, 50, "先不要", 15, Ink, TextAlignmentOptions.Center);
            var kb = keep.gameObject.AddComponent<Button>();
            kb.targetGraphic = kbg;
            kb.onClick.AddListener(() => CloseLeave());
            var go = Place(_leaveCard, "Leave", 18, 0, inner, 48);
            UIShapes.AddRounded(go.gameObject, Red, 15);
            var gbg = UIShapes.AddRounded(Place(go, "Fill", 2, 2, inner - 4f, 44).gameObject, Paper, 13);
            _leaveButtonText = Label(go, 0, 0, inner, 48, "離開空間", 15, Red, TextAlignmentOptions.Center);
            var gb = go.gameObject.AddComponent<Button>();
            gb.targetGraphic = gbg;
            gb.onClick.AddListener(ConfirmLeave);

            LayoutLeave();
            _leaveLayer.gameObject.SetActive(false);
        }

        /// <summary>確認框由上往下排：上 22、間距 12、下 18。</summary>
        void LayoutLeave()
        {
            float y = 22f;
            string[] order = { "Icon", "Title", "Box", "Keep", "Leave" };
            foreach (var n in order)
            {
                var c = (RectTransform)_leaveCard.Find(n);
                if (!c.gameObject.activeSelf) continue;
                if (n != "Icon") y += 12f;
                c.anchoredPosition = new Vector2(c.anchoredPosition.x, -y);
                y += c.sizeDelta.y;
            }
            _leaveCard.sizeDelta = new Vector2(DesignW - 48f, y + 18f);
        }

        void BuildToast()
        {
            _toast = Place(_design, "Toast", 20, 700, DesignW - 40f, 40);
            _toastGroup = _toast.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.blocksRaycasts = false;
            _toastGroup.alpha = 0f;
            UIShapes.AddRounded(_toast.gameObject, new Color(Ink.r, Ink.g, Ink.b, 0.94f), 14).raycastTarget = false;
            _toastText = Label(_toast, 0, 0, 0, 0, "", 13, Color.white, TextAlignmentOptions.Center);
            StretchIn(_toastText.rectTransform, 16, 0, 16, 0);
        }

        void CloseButton(RectTransform parent, float x, float y, Action onClick)
        {
            var close = Place(parent, "Close", x, y, 44, 44);
            var bg = UIShapes.AddRounded(close.gameObject, Beige, 22);
            foreach (float z in new[] { 45f, -45f })
            {
                var bar = Place(close, "X", 0, 0, 13, 2.4f);
                bar.pivot = new Vector2(0.5f, 0.5f);
                bar.anchoredPosition = new Vector2(22f, -22f);
                bar.localRotation = Quaternion.Euler(0, 0, z);
                var img = bar.gameObject.AddComponent<Image>();
                img.color = Ink;
                img.raycastTarget = false;
            }
            var b = close.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            b.onClick.AddListener(() => onClick());
        }

        TMP_InputField Input(RectTransform parent, float x, float y, float w)
        {
            var rt = Place(parent, "Input", x, y, w, 48);
            rt.gameObject.SetActive(false); // 先設定好 TMP_InputField 的欄位再啟用
            UIShapes.AddRounded(rt.gameObject, Line, 13);
            var fill = UIShapes.AddRounded(Place(rt, "Fill", 2, 2, w - 4f, 44).gameObject, Color.white, 11);
            var area = Place(rt, "Text Area", 14, 0, w - 28f, 48);
            area.gameObject.AddComponent<RectMask2D>();
            var ph = Label(area, 0, 0, w - 28f, 48, "", 16, new Color(Muted.r, Muted.g, Muted.b, 0.5f), TextAlignmentOptions.Left);
            var text = Label(area, 0, 0, w - 28f, 48, "", 16, Ink, TextAlignmentOptions.Left);
            ph.enableWordWrapping = text.enableWordWrapping = false;
            var input = rt.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = fill;
            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = ph;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.onSubmit.AddListener(_ => SaveName());
            if (skin != null && skin.font != null) input.fontAsset = skin.font;
            rt.gameObject.SetActive(true);
            return input;
        }

        void MakeBackground()
        {
            var photo = _root.Find("BackgroundPhoto").GetComponent<RawImage>();
            if (background == null) { photo.enabled = false; return; }
            if (_blurred == null || !_blurred.IsCreated())
            {
                int w = 48, h = Mathf.Max(1, Mathf.RoundToInt(48f * background.height / Mathf.Max(1, background.width)));
                var mid = RenderTexture.GetTemporary(192, Mathf.Max(1, h * 4), 0);
                mid.filterMode = FilterMode.Bilinear;
                Graphics.Blit(background, mid);
                if (_blurred == null)
                    _blurred = new RenderTexture(w, h, 0) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                Graphics.Blit(mid, _blurred);
                RenderTexture.ReleaseTemporary(mid);
            }
            photo.texture = _blurred;
            photo.GetComponent<AspectRatioFitter>().aspectRatio = (float)background.width / Mathf.Max(1, background.height);
        }

        // ─── UI helpers（與其他新版頁面相同的設計座標寫法）────────────

        TMP_Text Label(RectTransform parent, float left, float top, float w, float h, string value, float size,
                       Color color, TextAlignmentOptions align)
        {
            var rt = Place(parent, "Text", left, top, w, h);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            var f = skin != null ? skin.font : null;
            if (f != null) t.font = f;
            t.text = value;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = f != null ? FontStyles.Normal : FontStyles.Bold;
            t.alignment = align;
            t.raycastTarget = false;
            t.richText = false;
            t.enableWordWrapping = true;
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

        static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
