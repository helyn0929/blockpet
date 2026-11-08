using System.Collections.Generic;
using BlockPet.Core;
using BlockPet.Decoration;
using BlockPet.Hand;
using BlockPet.UI.Decorate;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlockPet.UI.Home
{
    /// <summary>
    /// 佈置營地：斜角格子（設計文件 5.28；docs/decorate/decorate_spec.json；2026-11-08 取代原本的自由擺放）。
    /// - 平常：營地右下「佈置」按鈕；格子不顯示。
    /// - 佈置中：暗色遮罩＋所有格子浮出、寵物半透明；上方「佈置營地・已用 N / M 格・完成」；下方「還沒擺出來的家具」。
    /// - 點一件家具（場上的或面板裡的）→ 浮起發黃光、上方小標「營火・點綠色格子移過去」；放得下的格子閃綠色（1.4 秒）。
    /// - 點綠色格子 → 先顯示半透明預覽＋白框，再移過去（寫進營地資料，大家看到一樣的擺法）。
    /// - 按「完成」→ 回到平常的營地。
    /// 編輯鎖、Firebase 讀寫在 DecorationManager；格子規則在 GridMath；格子表 CampGrid（Resources/Camp）。
    /// 公開方法維持原本的名字（選單「佈置」、商店「去擺放」照舊呼叫）。
    /// </summary>
    public class RoomEditorController : MonoBehaviour
    {
        const float DesignW = 390f;
        const float TopH = 64f, PanelH = 150f;
        const float CardW = 80f, CardH = 92f, CardGap = 10f;
        const float PreviewSeconds = 0.35f, OkPulse = 1.4f;
        const int DimOrder = -30000, TileOrder = -29000;
        const float TapMovePx = 14f;

        static readonly Color Ink = new Color(0x3A / 255f, 0x2E / 255f, 0x22 / 255f);
        static readonly Color Sub = new Color(0x6B / 255f, 0x5D / 255f, 0x4E / 255f);
        static readonly Color Paper = new Color(0xFF / 255f, 0xF8 / 255f, 0xEC / 255f);
        static readonly Color Green = new Color(0x6F / 255f, 0xA4 / 255f, 0x5F / 255f);
        static readonly Color GreenEdge = new Color(0x4A / 255f, 0x7A / 255f, 0x3F / 255f);

        // ─── Inspector（舊的自由擺放用，現在不用；保留讓場景接線不壞）──────
        [Header("Legacy (free placement, no longer used)")]
        [SerializeField] DecorationPickerController picker;
        [SerializeField] SpriteRenderer placementPreview;
        [SerializeField] Camera worldCamera;

        public bool IsEditing { get; private set; }

        /// <summary>佈置中（首頁其他互動讓開）。</summary>
        public static bool AnyEditModeActive { get; private set; }

        // ─── 狀態 ──────────────────────────────────────────────────────

        DecorateSkin _skin;
        TMP_FontAsset _font;
        CampGrid _grid;
        HashSet<Cell> _cells = new HashSet<Cell>(), _blocked = new HashSet<Cell>();
        Dictionary<string, int> _owned = new Dictionary<string, int>();

        // 選到的家具：場上的（_selDecorId）或面板裡的（_selItem，可能對到一筆還沒擺到格子上的舊資料 _selLegacyId）
        string _selDecorId, _selLegacyId;
        DecorationItem _selItem;
        int _selCard = -1;
        readonly HashSet<Cell> _okTaps = new HashSet<Cell>();
        bool _hasTarget;
        Cell _targetTap, _targetAnchor;
        float _targetT, _pulseT;
        DecorationItem _queuedItem; // 商店「去擺放」：拿到編輯鎖後直接選這件

        // 畫面
        Canvas _canvas;
        RectTransform _root, _home, _top, _panel, _cards, _tag, _toast;
        TMP_Text _usedText, _tagText, _toastText, _emptyText;
        float _toastUntil;
        readonly List<(RectTransform rt, DecorationItem item, string legacyId, Image ring)> _cardViews = new List<(RectTransform, DecorationItem, string, Image)>();
        GameObject _world;
        SpriteRenderer _dim, _ghost;
        readonly Dictionary<Cell, SpriteRenderer> _tiles = new Dictionary<Cell, SpriteRenderer>();
        HandController _hand;
        PageManager _pages;
        CameraUIManager _cameraUI;
        Vector2 _downPos;
        bool _down;
        float _lastK = -1f;

        // ─── Lifecycle ─────────────────────────────────────────────────

        void Start()
        {
            if (worldCamera == null) worldCamera = Camera.main;
            if (placementPreview != null) placementPreview.enabled = false;
            picker?.Hide();
            _skin = DecorateSkin.Load();
            _font = _skin != null ? _skin.font : null;
            BuildUI();
            if (DecorationManager.Instance != null) DecorationManager.Instance.OnLayoutChanged += OnLayoutChanged;
        }

        // 這個元件掛在 HomePage 上：換到別頁時 HomePage 會關掉，畫面（獨立的 Canvas）跟著藏
        void OnEnable() { if (_canvas != null) _canvas.gameObject.SetActive(true); }
        void OnDisable() { if (_canvas != null) _canvas.gameObject.SetActive(false); }

        void OnDestroy()
        {
            if (DecorationManager.Instance != null) DecorationManager.Instance.OnLayoutChanged -= OnLayoutChanged;
            if (_canvas != null) Destroy(_canvas.gameObject);
            if (_world != null) Destroy(_world);
        }

        void OnLayoutChanged()
        {
            if (!IsEditing) return;
            RefreshAll();
        }

        void Update()
        {
            Relayout();
            _home.gameObject.SetActive(!IsEditing && ShowHomeButton());
            _toast.gameObject.SetActive(Time.unscaledTime < _toastUntil);
            if (!IsEditing) return;

            float dt = Time.unscaledDeltaTime;
            _pulseT += dt;
            UpdateTiles();
            UpdateSelectedFloat();
            UpdateTag();

            // 點綠色格子後：先顯示半透明預覽＋白框，再移過去
            if (_hasTarget)
            {
                _targetT += dt;
                if (_targetT >= PreviewSeconds) CommitTarget();
                return;
            }
            HandlePointer();
        }

        // ─── 進入／離開 ────────────────────────────────────────────────

        public void EnterEditMode()
        {
            if (IsEditing) return;
            var dm = DecorationManager.Instance;
            if (dm == null) return;
            if (!dm.IsListening)
            {
                string roomId = FirebaseManager.Instance?.RoomId ?? "";
                if (string.IsNullOrEmpty(roomId)) { Toast("先進入一個空間再佈置"); return; }
                dm.StartListening(roomId);
            }
            _grid = dm.Grid;
            if (_grid == null) { Toast("這個營地還沒有格子表"); Debug.LogWarning("[RoomEditor] 找不到 Resources/Camp/CampGrid_forest"); return; }

            dm.AcquireEditLock(ok =>
            {
                if (!ok) { Toast("別人正在佈置，等一下再試"); return; }
                IsEditing = true;
                AnyEditModeActive = true;
                SetFrozen(true);
                SetPetFaded(true);
                dm.SetAllDecorationsEditMode(true);
                FirebaseManager.Instance?.GetRoomShop(owned =>
                {
                    _owned = owned ?? new Dictionary<string, int>();
                    if (!IsEditing) return;
                    RefreshAll();
                    if (_queuedItem != null) { var q = _queuedItem; _queuedItem = null; SelectPanelItem(q); }
                });
                ShowEditUI(true);
                RefreshAll();
            });
        }

        /// <summary>進入佈置並直接選這件家具（商店的「去擺放」）。</summary>
        public void EnterEditModeAndPlace(DecorationItem item)
        {
            if (IsEditing) { SelectPanelItem(item); return; }
            _queuedItem = item;
            EnterEditMode();
        }

        public void ExitEditMode(bool save)
        {
            if (!IsEditing) return;
            Deselect();
            var dm = DecorationManager.Instance;
            dm?.ReleaseEditLock();
            dm?.SetAllDecorationsEditMode(false);
            IsEditing = false;
            AnyEditModeActive = false;
            SetFrozen(false);
            SetPetFaded(false);
            ShowEditUI(false);
        }

        // 舊介面（DecorationPickerController）呼叫的方法：保留名字
        public void OnItemSelected(DecorationItem item) => SelectPanelItem(item);
        public void OnExitEditModeButton() => ExitEditMode(true);
        public void OnCancelEditModeButton() => ExitEditMode(false);
        public void OnActionConfirm() { }
        public void OnActionCancel() => Deselect();
        public void OnActionRemove() { }
        public void OnActionDone() => Deselect();

        // ─── 資料 ──────────────────────────────────────────────────────

        DecorationManager DM => DecorationManager.Instance;

        int W(DecorationItem it) => it != null ? Mathf.Max(1, it.footprintW) : 1;
        int D(DecorationItem it) => it != null ? Mathf.Max(1, it.footprintD) : 1;

        /// <summary>場上（有格子座標）的家具佔哪些格：格子 → decorId。</summary>
        Dictionary<Cell, string> Taken()
        {
            var taken = new Dictionary<Cell, string>();
            if (DM == null) return taken;
            foreach (var kv in DM.Spawned)
            {
                var o = kv.Value;
                if (o == null || !o.HasGrid) continue;
                var it = DM.Database.GetById(o.ItemId);
                if (it == null || it.hangPoint) continue;
                foreach (var c in GridMath.Footprint(o.Anchor, W(it), D(it))) taken[c] = kv.Key;
            }
            return taken;
        }

        void RefreshAll()
        {
            var owned = new List<string>(_owned.Keys);
            _cells = _grid.ActiveCells(owned);
            _blocked = _grid.ActiveBlocked(owned);
            BuildTiles();
            BuildCards();
            RecomputeOk();
            // 已用 N / M 格
            int used = 0;
            foreach (var kv in Taken()) used++;
            _usedText.text = $"已用 {used} / {_grid.PlaceableCount(owned)} 格";
        }

        // ─── 選取 ──────────────────────────────────────────────────────

        void SelectPlaced(DecorationObject o)
        {
            Deselect();
            _selDecorId = o.DecorId;
            _selItem = DM.Database.GetById(o.ItemId);
            o.SetSelected(true);
            RecomputeOk();
        }

        void SelectCard(int index)
        {
            if (index < 0 || index >= _cardViews.Count) return;
            Deselect();
            _selCard = index;
            _selItem = _cardViews[index].item;
            _selLegacyId = _cardViews[index].legacyId;
            _cardViews[index].ring.enabled = true;
            RecomputeOk();
        }

        void SelectPanelItem(DecorationItem item)
        {
            if (item == null) return;
            for (int i = 0; i < _cardViews.Count; i++)
                if (_cardViews[i].item == item) { SelectCard(i); return; }
            // 面板裡沒有（例如已經全部擺出來）：選場上第一件
            if (DM != null)
                foreach (var kv in DM.Spawned)
                    if (kv.Value != null && kv.Value.HasGrid && kv.Value.ItemId == item.itemId) { SelectPlaced(kv.Value); return; }
        }

        void Deselect()
        {
            if (_selDecorId != null && DM != null && DM.Spawned.TryGetValue(_selDecorId, out var o) && o != null)
            {
                o.SetSelected(false);
                DecorationLayout.Apply(o, DM.Database.GetById(o.ItemId), _grid); // 放回原本的高度
            }
            if (_selCard >= 0 && _selCard < _cardViews.Count) _cardViews[_selCard].ring.enabled = false;
            _selDecorId = _selLegacyId = null;
            _selItem = null;
            _selCard = -1;
            _okTaps.Clear();
            _hasTarget = false;
            if (_ghost != null) _ghost.enabled = false;
        }

        /// <summary>選了家具時，哪些格子點下去放得下（點的那格當底座中心附近）。</summary>
        void RecomputeOk()
        {
            _okTaps.Clear();
            if (_selItem == null || _grid == null) return;
            var taken = Taken();
            string self = _selDecorId;
            foreach (var c in _cells)
            {
                if (_blocked.Contains(c)) continue;
                var anchor = GridMath.AnchorForTap(c, W(_selItem), D(_selItem));
                if (GridMath.Fits(anchor, W(_selItem), D(_selItem), _cells, _blocked, taken, self)) _okTaps.Add(c);
            }
        }

        // ─── 點擊 ──────────────────────────────────────────────────────

        void HandlePointer()
        {
            if (!ReadPointer(out bool down, out bool up, out Vector2 screen)) return;
            if (down) { _down = !OverUI(screen); _downPos = screen; return; }
            if (!up || !_down) return;
            _down = false;
            if ((screen - _downPos).magnitude > TapMovePx) return;

            var world = ScreenToWorld(screen);
            var hit = FindDecorAt(world);
            if (hit != null) { SelectPlaced(hit); return; }

            var cell = _grid.CellAtWorld(world);
            if (_selItem != null && _okTaps.Contains(cell))
            {
                _targetTap = cell;
                _targetAnchor = GridMath.AnchorForTap(cell, W(_selItem), D(_selItem));
                _hasTarget = true;
                _targetT = 0f;
                ShowGhost();
                return;
            }
            Deselect();
        }

        void CommitTarget()
        {
            _hasTarget = false;
            if (_ghost != null) _ghost.enabled = false;
            var item = _selItem;
            var anchor = _targetAnchor;
            string moveId = _selDecorId ?? _selLegacyId;
            Deselect();
            if (item == null || DM == null) return;
            if (moveId != null)
                DM.MoveOnGrid(moveId, anchor, ok => { if (!ok) Toast("沒有存到，請檢查網路"); });
            else
                DM.PlaceOnGrid(item, anchor, (ok, id) => { if (!ok) Toast("沒有存到，請檢查網路"); });
        }

        /// <summary>點到哪件家具（排在最前面的那件）；只能移自己擺的（同原本的規則）。</summary>
        DecorationObject FindDecorAt(Vector3 world)
        {
            DecorationObject best = null;
            int bestOrder = int.MinValue;
            foreach (var col in Physics2D.OverlapPointAll(world))
            {
                var d = col.GetComponent<DecorationObject>();
                if (d == null || !d.gameObject.activeInHierarchy || !d.HasGrid) continue;
                if (!d.CanDelete) { Toast("這件是別人擺的，只有擺的人能移動"); continue; }
                int order = d.Renderer.sortingOrder;
                if (best == null || order > bestOrder) { best = d; bestOrder = order; }
            }
            return best;
        }

        // ─── 世界：遮罩、格子、預覽 ────────────────────────────────────

        void EnsureWorld()
        {
            if (_world != null) return;
            _world = new GameObject("DecorateGrid (runtime)");
            _dim = new GameObject("Dim").AddComponent<SpriteRenderer>();
            _dim.transform.SetParent(_world.transform, false);
            _dim.sprite = _skin != null ? _skin.dim : null;
            _dim.sortingLayerName = "Pet";
            _dim.sortingOrder = DimOrder;
            if (_dim.sprite == null) _dim.enabled = false;
            _ghost = new GameObject("Ghost").AddComponent<SpriteRenderer>();
            _ghost.transform.SetParent(_world.transform, false);
            _ghost.sortingLayerName = "Pet";
            _ghost.enabled = false;
        }

        void BuildTiles()
        {
            EnsureWorld();
            _world.SetActive(true);
            // 遮罩蓋滿相機畫面
            if (_dim.sprite != null && worldCamera != null)
            {
                float h = worldCamera.orthographicSize * 2f, w = h * worldCamera.aspect;
                var b = _dim.sprite.bounds.size;
                _dim.transform.position = new Vector3(worldCamera.transform.position.x, worldCamera.transform.position.y, 0f);
                _dim.transform.localScale = new Vector3(w * 1.2f / b.x, h * 1.2f / b.y, 1f);
                _dim.enabled = true;
            }
            var keep = new HashSet<Cell>(_cells);
            var drop = new List<Cell>();
            foreach (var kv in _tiles) if (!keep.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (var c in drop) { Destroy(_tiles[c].gameObject); _tiles.Remove(c); }
            var normal = _skin != null ? _skin.tileNormal : null;
            float scale = normal != null && normal.bounds.size.x > 0f ? _grid.tileWorldWidth / normal.bounds.size.x : 1f;
            foreach (var c in _cells)
            {
                if (_tiles.ContainsKey(c)) continue;
                var sr = new GameObject($"Tile{c}").AddComponent<SpriteRenderer>();
                sr.transform.SetParent(_world.transform, false);
                sr.transform.position = _grid.CellCenterWorld(c);
                sr.transform.localScale = Vector3.one * scale;
                sr.sortingLayerName = "Pet";
                sr.sortingOrder = TileOrder;
                _tiles[c] = sr;
            }
        }

        /// <summary>格子顏色（設計文件 5.28 ⑤）：不能放／選到的家具佔的／家具佔的／放得下（閃）／要放的那格／一般。</summary>
        void UpdateTiles()
        {
            if (_skin == null) return;
            var taken = Taken();
            HashSet<Cell> selCells = null, targetCells = null;
            if (_selDecorId != null && _selItem != null && DM.Spawned.TryGetValue(_selDecorId, out var so) && so != null)
                selCells = new HashSet<Cell>(GridMath.Footprint(so.Anchor, W(_selItem), D(_selItem)));
            if (_hasTarget && _selItem != null)
                targetCells = new HashSet<Cell>(GridMath.Footprint(_targetAnchor, W(_selItem), D(_selItem)));
            float pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Cos(_pulseT / OkPulse * Mathf.PI * 2f));
            foreach (var kv in _tiles)
            {
                var c = kv.Key;
                var sr = kv.Value;
                Sprite sp;
                float a = 1f;
                if (targetCells != null && targetCells.Contains(c)) sp = _skin.tileTarget;
                else if (_blocked.Contains(c)) sp = _skin.tileBlocked;
                else if (selCells != null && selCells.Contains(c)) sp = _skin.tileSelected;
                else if (taken.ContainsKey(c)) sp = _skin.tileOccupied;
                else if (_okTaps.Contains(c) && !_hasTarget) { sp = _skin.tileOk; a = pulse; }
                else sp = _skin.tileNormal;
                if (sr.sprite != sp) sr.sprite = sp;
                sr.color = new Color(1f, 1f, 1f, a);
            }
        }

        /// <summary>選到的場上家具：浮起來一點、上下輕輕晃（發黃光由 DecorationObject.SetSelected 做）。</summary>
        void UpdateSelectedFloat()
        {
            if (_selDecorId == null || !DM.Spawned.TryGetValue(_selDecorId, out var o) || o == null || _hasTarget) return;
            DecorationLayout.Apply(o, _selItem, _grid);
            float lift = (8f + 3f * Mathf.Sin(_pulseT * 4f)) * _grid.WorldPerPx;
            o.transform.position += new Vector3(0f, lift, 0f);
            o.Renderer.sortingOrder += 5000; // 浮起來時畫在最前面
        }

        /// <summary>點了綠色格子：家具的半透明預覽放在目標位置。</summary>
        void ShowGhost()
        {
            if (_ghost == null || _selItem == null) return;
            var sprite = _selItem.worldSprite != null ? _selItem.worldSprite : _selItem.icon;
            _ghost.sprite = sprite;
            _ghost.color = new Color(1f, 1f, 1f, 0.55f);
            var place = GridMath.ImagePlacement(_targetAnchor, W(_selItem), D(_selItem), Vector2.zero);
            var bottom = _grid.ToWorld(new Vector2(place.centerX, place.bottom));
            float s = sprite != null && sprite.bounds.size.x > 0f ? place.width * _grid.WorldPerPx / sprite.bounds.size.x : 1f;
            _ghost.transform.localScale = Vector3.one * s;
            _ghost.transform.position = sprite != null ? bottom - new Vector3(sprite.bounds.center.x, sprite.bounds.min.y, 0f) * s : bottom;
            _ghost.sortingOrder = AIController.SortOrderForY(DecorationLayout.FootWorld(_selItem, _targetAnchor, _grid).y) + 1;
            _ghost.enabled = true;
        }

        // ─── 寵物、凍結 ────────────────────────────────────────────────

        void SetPetFaded(bool faded)
        {
            var pet = AIController.Instance;
            if (pet == null) return;
            foreach (var sr in pet.GetComponentsInChildren<SpriteRenderer>(true))
            {
                var c = sr.color;
                sr.color = new Color(c.r, c.g, c.b, faded ? 0.5f : 1f);
            }
        }

        void SetFrozen(bool frozen)
        {
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb is IEditModeFreezable f) f.SetEditModeFrozen(frozen);
        }

        // ─── 畫面 ──────────────────────────────────────────────────────

        bool ShowHomeButton()
        {
            if (!LoginUIHandler.GameplayHudReleased) return false;
            var fb = FirebaseManager.Instance;
            if (fb == null || string.IsNullOrEmpty(fb.RoomId)) return false;
            if (_pages == null) _pages = FindObjectOfType<PageManager>(true);
            if (_cameraUI == null) _cameraUI = FindObjectOfType<CameraUIManager>(true);
            if (_hand == null) _hand = FindObjectOfType<HandController>(true);
            return _pages != null && _pages.IsHomePageActive
                   && (_cameraUI == null || _cameraUI.CurrentMode == UIMode.Home)
                   && (_hand == null || !_hand.IsDealt);
        }

        void ShowEditUI(bool on)
        {
            _top.gameObject.SetActive(on);
            _panel.gameObject.SetActive(on);
            _tag.gameObject.SetActive(false);
            if (_world != null) _world.SetActive(on);
        }

        void BuildUI()
        {
            var go = new GameObject("DecorateScreen (runtime)", typeof(RectTransform));
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 12; // 狀態欄（10）之上：佈置中的上方列蓋住狀態欄
            go.AddComponent<GraphicRaycaster>();
            _root = (RectTransform)go.transform;

            // 平常營地右下「佈置」：deco_btn_pill＋deco_icon_build＋「佈置」15 Black
            _home = Anchored(_root, "DecorateButton", new Vector2(1f, 0f), 96, 46);
            UIShapes.AddShadow(_home, new Color(30f / 255f, 40f / 255f, 20f / 255f, 0.3f), 23, 12, 4);
            var hb = Fill(_home, _skin?.btnPill, true, Color.white, 23);
            var icon = Img(_home, _skin?.iconBuild, 14, 13, 20, 20);
            icon.color = Color.white;
            Label(_home, 40, 0, 46, 46, "佈置", 15, Ink, TextAlignmentOptions.Left);
            Btn(_home, hb, EnterEditMode);

            // 上方列：deco_topbar 高 64；「佈置營地」「已用 N / M 格」；右邊綠色「完成」
            _top = Anchored(_root, "TopBar", new Vector2(0.5f, 1f), DesignW, TopH);
            Fill(_top, _skin?.topbar, false, new Color(Paper.r, Paper.g, Paper.b, 0.96f), 0).raycastTarget = true;
            Label(_top, 16, 12, 140, 26, "佈置營地", 18, Ink, TextAlignmentOptions.Left);
            _usedText = Label(_top, 16, 36, 200, 18, "", 12, Sub, TextAlignmentOptions.Left);
            var done = Place(_top, "Done", DesignW - 16f - 76f, 13, 76, 38);
            UIShapes.AddRounded(Place(done, "Edge", 0, 3, 76, 38).gameObject, GreenEdge, 19).raycastTarget = false;
            var db = Fill(Place(done, "Fill", 0, 0, 76, 38), _skin?.btnDone, true, Green, 19);
            Label(done, 0, 0, 76, 38, "完成", 15, Color.white, TextAlignmentOptions.Center);
            Btn(done, db, () => ExitEditMode(true));

            // 下方面板：deco_panel 高 150；「還沒擺出來的家具」；卡片 deco_card 左右滑
            _panel = Anchored(_root, "Panel", new Vector2(0.5f, 0f), DesignW, PanelH);
            Fill(_panel, _skin?.panel, true, Paper, 24).raycastTarget = true;
            Label(_panel, 16, 12, 260, 20, "還沒擺出來的家具", 13, Ink, TextAlignmentOptions.Left);
            var view = Place(_panel, "Cards", 0, 38, DesignW, CardH + 10f);
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            _cards = Place(view, "Content", 0, 4, DesignW, CardH);
            var sc = view.gameObject.AddComponent<ScrollRect>();
            sc.content = _cards; sc.viewport = view; sc.vertical = false; sc.movementType = ScrollRect.MovementType.Elastic;
            _emptyText = Label(_panel, 0, 70, DesignW, 20, "家具都擺出來了！去商店可以買新的", 12, Sub, TextAlignmentOptions.Center);

            // 選到的家具上方小標（deco_tag_dark，11 Black #FFF8EC）
            _tag = Place(_root, "Tag", 0, 0, 160, 26);
            _tag.anchorMin = _tag.anchorMax = Vector2.zero;
            _tag.pivot = new Vector2(0.5f, 0f);
            Fill(_tag, _skin?.tagDark, true, new Color(Ink.r, Ink.g, Ink.b, 0.9f), 13).raycastTarget = false;
            _tagText = Label(_tag, 10, 0, 140, 26, "", 11, Paper, TextAlignmentOptions.Center);

            _toast = Anchored(_root, "Toast", new Vector2(0.5f, 0.5f), 260, 40);
            UIShapes.AddRounded(_toast.gameObject, new Color(Ink.r, Ink.g, Ink.b, 0.9f), 20).raycastTarget = false;
            _toastText = Label(_toast, 0, 0, 260, 40, "", 13, Color.white, TextAlignmentOptions.Center);
            _toast.gameObject.SetActive(false);

            ShowEditUI(false);
        }

        void BuildCards()
        {
            foreach (var v in _cardViews) Destroy(v.rt.gameObject);
            _cardViews.Clear();
            if (DM == null || DM.Database == null) return;
            // 每件家具：買了幾個 − 已經擺在格子上的；舊的自由擺放（沒有格子座標）也算還沒擺出來
            float x = 16f;
            foreach (var item in DM.Database.GetAll())
            {
                if (item == null || item.category != DecorationCategory.Furniture || item.hangPoint) continue;
                int placed = 0;
                var legacy = new List<string>();
                foreach (var kv in DM.Spawned)
                {
                    if (kv.Value == null || kv.Value.ItemId != item.itemId) continue;
                    if (kv.Value.HasGrid) placed++; else legacy.Add(kv.Key);
                }
                int bought = _owned.TryGetValue(item.itemId, out var n) ? n : 0;
                int unplaced = Mathf.Max(bought - placed, legacy.Count);
                for (int u = 0; u < unplaced; u++)
                {
                    string legacyId = u < legacy.Count ? legacy[u] : null;
                    _cardViews.Add(BuildCard(item, legacyId, x));
                    x += CardW + CardGap;
                }
            }
            _cards.sizeDelta = new Vector2(Mathf.Max(DesignW, x + 6f), CardH);
            _emptyText.gameObject.SetActive(_cardViews.Count == 0);
            _selCard = -1;
        }

        (RectTransform, DecorationItem, string, Image) BuildCard(DecorationItem item, string legacyId, float x)
        {
            var c = Place(_cards, "Card_" + item.itemId, x, 0, CardW, CardH);
            // 選到的外框 deco_card_ring（86 × 98，外擴 3）
            var ringRt = Place(c, "Ring", -3, -3, CardW + 6f, CardH + 6f);
            var ring = Fill(ringRt, _skin?.cardRing, false, new Color(0.965f, 0.788f, 0.271f), 16);
            ring.raycastTarget = false;
            ring.enabled = false;
            var bg = Fill(Place(c, "Bg", 0, 0, CardW, CardH), _skin?.card, false, Color.white, 14);
            var pic = Img(c, item.icon != null ? item.icon : item.worldSprite, 10, 8, CardW - 20f, 50);
            pic.preserveAspect = true;
            var name = Label(c, 4, 60, CardW - 8f, 16, item.displayName, 11, Ink, TextAlignmentOptions.Center);
            name.overflowMode = TextOverflowModes.Ellipsis;
            Label(c, 4, 74, CardW - 8f, 16, $"佔 {W(item) * D(item)} 格", 10, Sub, TextAlignmentOptions.Center);
            int index = _cardViews.Count;
            Btn(c, bg, () => { if (_selCard == index) Deselect(); else SelectCard(index); });
            return (c, item, legacyId, ring);
        }

        /// <summary>選到的家具上方小標「營火・點綠色格子移過去」：跟著場上的家具，或在面板裡那張卡上面。</summary>
        void UpdateTag()
        {
            if (_selItem == null) { _tag.gameObject.SetActive(false); return; }
            string text = $"{_selItem.displayName}・點綠色格子移過去";
            if (_okTaps.Count == 0) text = $"{_selItem.displayName}・沒有放得下的地方";
            if (_tagText.text != text)
            {
                _tagText.text = text;
                float tw = Mathf.Ceil(_tagText.GetPreferredValues(text).x);
                _tagText.rectTransform.sizeDelta = new Vector2(tw, 26f);
                _tag.sizeDelta = new Vector2(tw + 20f, 26f);
            }
            Vector2 screen;
            if (_selDecorId != null && DM.Spawned.TryGetValue(_selDecorId, out var o) && o != null)
            {
                var top = o.TopCenter;
                screen = worldCamera.WorldToScreenPoint(top);
                screen.y += 6f * _lastK;
            }
            else if (_selCard >= 0 && _selCard < _cardViews.Count)
            {
                var corners = new Vector3[4];
                _cardViews[_selCard].rt.GetWorldCorners(corners);
                screen = RectTransformUtility.WorldToScreenPoint(null, (corners[1] + corners[2]) / 2f);
                screen.y += 6f * _lastK;
            }
            else { _tag.gameObject.SetActive(false); return; }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out var local);
            _tag.anchoredPosition = local - _root.rect.min;
            _tag.localScale = Vector3.one * _lastK;
            _tag.gameObject.SetActive(true);
        }

        void Relayout()
        {
            float w = _root.rect.width;
            if (w <= 0f) return;
            float k = w / DesignW;
            var safe = Screen.safeArea;
            float px = _canvas.scaleFactor > 0f ? 1f / _canvas.scaleFactor : 1f;
            float top = (Screen.height - safe.yMax) * px, bottom = safe.yMin * px;
            _lastK = k;
            _top.localScale = _panel.localScale = _home.localScale = _toast.localScale = Vector3.one * k;
            // 上方列連瀏海一起蓋（底圖往上延伸）
            _top.anchoredPosition = new Vector2(0f, -top);
            _panel.anchoredPosition = new Vector2(0f, bottom);
            // 「佈置」：右 14，在底部三顆按鈕上方
            _home.anchoredPosition = new Vector2(-14f * k, bottom + 120f * k);
        }

        void Toast(string msg)
        {
            _toastText.text = msg;
            _toastUntil = Time.unscaledTime + 2f;
        }

        // ─── 小工具 ────────────────────────────────────────────────────

        bool OverUI(Vector2 screen)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var data = new PointerEventData(es) { position = screen };
            var hits = new List<RaycastResult>();
            es.RaycastAll(data, hits);
            return hits.Count > 0;
        }

        static bool ReadPointer(out bool down, out bool up, out Vector2 pos)
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            down = Input.GetMouseButtonDown(0);
            up = Input.GetMouseButtonUp(0);
            pos = Input.mousePosition;
            return down || up;
#else
            down = up = false;
            pos = default;
            if (Input.touchCount == 0) return false;
            var t = Input.GetTouch(0);
            pos = t.position;
            down = t.phase == TouchPhase.Began;
            up = t.phase == TouchPhase.Ended;
            return down || up;
#endif
        }

        Vector3 ScreenToWorld(Vector2 screen)
        {
            var w = worldCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10f));
            w.z = 0f;
            return w;
        }

        static RectTransform Anchored(RectTransform parent, string name, Vector2 anchor, float w, float h)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        static RectTransform Place(Transform parent, string name, float left, float top, float w, float h)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(left, -top);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        static Image Fill(RectTransform rt, Sprite sprite, bool sliced, Color fallback, float radius)
        {
            if (sprite == null) return radius > 0f ? UIShapes.AddRounded(rt.gameObject, fallback, radius) : AddColor(rt, fallback);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            return img;
        }

        static Image AddColor(RectTransform rt, Color c)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            return img;
        }

        static Image Img(RectTransform parent, Sprite sprite, float left, float top, float w, float h)
        {
            var img = Place(parent, "Img", left, top, w, h).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.enabled = sprite != null;
            return img;
        }

        TMP_Text Label(RectTransform parent, float left, float top, float w, float h, string value, float size, Color color, TextAlignmentOptions align)
        {
            var t = Place(parent, "Text", left, top, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.text = value;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        static void Btn(RectTransform rt, Graphic target, UnityEngine.Events.UnityAction onClick)
        {
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = target;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(onClick);
        }
    }
}
