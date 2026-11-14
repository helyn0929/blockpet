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
    /// - 入口：右上選單「佈置」（MenuToggle）、商店「去擺放」（EnterEditModeAndPlace）；平常營地不顯示格子。
    /// - 佈置中：暗色遮罩＋所有格子浮出、寵物半透明；狀態欄、照片貼紙、底部按鈕收起來（DecorModeSettings）；
    ///   上方「佈置營地・已用 N / M 格・完成」；下方「還沒擺出來的家具」。
    /// - 拖曳（2026-11-08 使用者要求，比設計的「點綠色格子」直觀）：按住場上的家具拖，或把面板裡的卡片往上拖到營地；
    ///   一路吸附在格子上，底座白框＝放得下、紅框＝放不下；放開在放得下的地方就移過去，否則回原位。
    /// - 也能用點的：點一件家具 → 浮起發黃光、上方小標；放得下的格子閃綠色（1.4 秒）；點綠色格子 → 半透明預覽＋白框再移過去。
    /// - 收起來（5.28 ④-5，2026-11-09；decorate_spec.json put_away）：場上的家具往下拖到「還沒擺出來的家具」面板；
    ///   移動不到 8 算點一下。拖曳中放大 1.08 倍、面板淡綠＋綠色陰影；家具中心進到面板 → 縮到 0.6 倍、綠色虛線框「放開就收起來」；
    ///   在面板放開＝收起來（DecorationManager.PutAway，格子空出來、存檔），在別處放開＝回原位。不做「全部收起來」。
    /// - 按「完成」→ 回到平常的營地。
    /// 編輯鎖、Firebase 讀寫在 DecorationManager；格子規則在 GridMath；格子表 CampGrid（Resources/Camp）。
    /// </summary>
    public class RoomEditorController : MonoBehaviour
    {
        const float DesignW = 390f;
        const float TopH = 64f, PanelH = 150f;
        const float CardW = 80f, CardH = 92f, CardGap = 10f;
        const float PreviewSeconds = 0.35f, OkPulse = 1.4f;
        const int DimOrder = -30000, TileOrder = -29000, LiftOrder = 5000;
        const float TapMovePx = 8f;          // 移動不到 8（設計 px）就當作點一下（5.28 ④-5 put_away.tap_threshold_px）
        const float DragScale = 1.08f, PutAwayScale = 0.6f;
        const float CardDragLift = 40f; // 從卡片拖出來時家具畫在手指上方多少（設計 px），手指不會擋住

        static readonly Color Ink = new Color(0x3A / 255f, 0x2E / 255f, 0x22 / 255f);
        static readonly Color Sub = new Color(0x6B / 255f, 0x5D / 255f, 0x4E / 255f);
        static readonly Color Paper = new Color(0xFF / 255f, 0xF8 / 255f, 0xEC / 255f);
        static readonly Color Green = new Color(0x6F / 255f, 0xA4 / 255f, 0x5F / 255f);
        static readonly Color GreenEdge = new Color(0x4A / 255f, 0x7A / 255f, 0x3F / 255f);
        // 收起來（decorate_spec.json put_away）：拖曳時面板淡綠＋綠色陰影；進入面板時綠色虛線框「放開就收起來」
        static readonly Color PanelDrag = new Color(0xF1 / 255f, 0xF7 / 255f, 0xEA / 255f);
        static readonly Color PanelDragShadow = new Color(60f / 255f, 110f / 255f, 40f / 255f, 0.35f);
        static readonly Color PutAwayFill = new Color(227f / 255f, 238f / 255f, 217f / 255f, 0.92f);
        static readonly Color PutAwayLine = new Color(0x6F / 255f, 0xA4 / 255f, 0x5F / 255f);
        static readonly Color PutAwayText = new Color(0x3F / 255f, 0x6E / 255f, 0x33 / 255f);

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
        Cell _targetAnchor;
        float _targetT, _pulseT;
        DecorationItem _queuedItem; // 商店「去擺放」：拿到編輯鎖後直接選這件

        // 按下／拖曳
        bool _down, _dragging, _dragFits, _dragOverPanel, _draggingPlaced;
        Image _panelBg, _panelShadow, _dragUi;
        Color _panelBgColor = Color.white;
        RectTransform _putAwayBox;
        Vector2 _downPos;
        DecorationObject _downDecor;  // 按下時點到的場上家具
        int _downCard = -1;           // 按下時點到的卡片
        Vector3 _grabOffset;          // 底座中心 − 手指（世界座標）
        Cell _dragAnchor;
        ScrollRect _scroll;

        // 畫面
        Canvas _canvas;
        RectTransform _root, _top, _panel, _cards, _tag, _toast;
        TMP_Text _usedText, _tagText, _toastText, _emptyText;
        float _toastUntil;
        readonly List<(RectTransform rt, DecorationItem item, string legacyId, Image ring)> _cardViews = new List<(RectTransform, DecorationItem, string, Image)>();
        GameObject _world;
        SpriteRenderer _dim, _ghost;
        readonly Dictionary<Cell, SpriteRenderer> _tiles = new Dictionary<Cell, SpriteRenderer>();
        float _lastK = 1f;

        // ─── Lifecycle ─────────────────────────────────────────────────

        void Start()
        {
            if (worldCamera == null) worldCamera = Camera.main;
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
            if (!IsEditing || _dragging) return;
            RefreshAll();
        }

        void Update()
        {
            Relayout();
            _toast.gameObject.SetActive(Time.unscaledTime < _toastUntil);
            if (!IsEditing) return;

            float dt = Time.unscaledDeltaTime;
            _pulseT += dt;

            // 點綠色格子後：先顯示半透明預覽＋白框，再移過去
            if (_hasTarget)
            {
                _targetT += dt;
                if (_targetT >= PreviewSeconds) CommitTarget();
            }
            else HandlePointer();

            UpdateTiles();
            UpdateSelectedFloat();
            UpdateTag();
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
            CancelDrag();
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

        // ─── 資料 ──────────────────────────────────────────────────────

        DecorationManager DM => DecorationManager.Instance;

        static int W(DecorationItem it) => it != null ? Mathf.Max(1, it.footprintW) : 1;
        static int D(DecorationItem it) => it != null ? Mathf.Max(1, it.footprintD) : 1;

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

        bool Fits(DecorationItem item, Cell anchor, string self) =>
            GridMath.Fits(anchor, W(item), D(item), _cells, _blocked, Taken(), self);

        bool TryGetSpawned(string id, out DecorationObject o)
        {
            o = null;
            return id != null && DM != null && DM.Spawned.TryGetValue(id, out o) && o != null;
        }

        void RefreshAll()
        {
            var owned = new List<string>(_owned.Keys);
            _cells = _grid.ActiveCells(owned);
            _blocked = _grid.ActiveBlocked(owned);
            BuildTiles();
            var keepItem = _selDecorId == null ? _selItem : null;
            BuildCards();
            if (keepItem != null) SelectPanelItem(keepItem); // 卡片重建後選回同一件
            RecomputeOk();
            _usedText.text = $"已用 {Taken().Count} / {_grid.PlaceableCount(owned)} 格";
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
            if (TryGetSpawned(_selDecorId, out var o))
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
            foreach (var c in _cells)
            {
                if (_blocked.Contains(c)) continue;
                var anchor = GridMath.AnchorForTap(c, W(_selItem), D(_selItem));
                if (GridMath.Fits(anchor, W(_selItem), D(_selItem), _cells, _blocked, taken, _selDecorId)) _okTaps.Add(c);
            }
        }

        // ─── 點擊與拖曳 ────────────────────────────────────────────────

        void HandlePointer()
        {
            if (!ReadPointer(out bool down, out bool held, out bool up, out Vector2 screen)) return;

            if (down)
            {
                _down = true;
                _dragging = false;
                _downPos = screen;
                _downCard = CardAt(screen);
                _downDecor = _downCard < 0 && !OverUI(screen) ? FindDecorAt(ScreenToWorld(screen), true) : null;
                return;
            }
            if (!_down) return;

            if (held && !_dragging)
            {
                var delta = screen - _downPos;
                if (delta.magnitude > TapMovePx * _lastK * Mathf.Max(0.01f, _canvas.scaleFactor)) // 設計 px → 螢幕像素
                {
                    // 場上的家具：往哪拖都算；卡片：往上拖才算（左右滑是捲動面板）
                    if (_downDecor != null) BeginDragPlaced(_downDecor, screen);
                    else if (_downCard >= 0 && delta.y > Mathf.Abs(delta.x)) BeginDragCard(_downCard, screen);
                    else { _down = false; return; } // 面板捲動或在空地上滑：這次不算點
                }
            }
            if (_dragging)
            {
                if (held) UpdateDrag(screen);
                if (up) EndDrag();
                return;
            }
            if (!up) return;
            _down = false;
            OnTap(screen);
        }

        void OnTap(Vector2 screen)
        {
            if (_downCard >= 0)
            {
                if (_selCard == _downCard) Deselect(); else SelectCard(_downCard);
                return;
            }
            if (OverUI(screen)) return;
            var world = ScreenToWorld(screen);
            var hit = FindDecorAt(world, false);
            if (hit != null) { SelectPlaced(hit); return; }

            var cell = _grid.CellAtWorld(world);
            if (_selItem != null && _okTaps.Contains(cell))
            {
                _targetAnchor = GridMath.AnchorForTap(cell, W(_selItem), D(_selItem));
                _hasTarget = true;
                _targetT = 0f;
                ShowGhostAt(_targetAnchor, 0.55f);
                return;
            }
            Deselect();
        }

        void BeginDragPlaced(DecorationObject o, Vector2 screen)
        {
            if (_selDecorId != o.DecorId) SelectPlaced(o);
            _dragging = true;
            _draggingPlaced = true;
            _grabOffset = DecorationLayout.CenterWorld(_selItem, o.Anchor, _grid) - ScreenToWorld(_downPos);
            UpdateDrag(screen);
        }

        void BeginDragCard(int index, Vector2 screen)
        {
            if (_selCard != index) SelectCard(index);
            _dragging = true;
            _draggingPlaced = false;
            _grabOffset = new Vector3(0f, CardDragLift * _grid.WorldPerPx, 0f);
            if (_scroll != null) { _scroll.StopMovement(); _scroll.enabled = false; } // 拖出來時面板不要跟著捲
            UpdateDrag(screen);
        }

        /// <summary>
        /// 跟著手指：底座中心吸附到最近的一格；放不放得下用底座框的顏色表示。
        /// 場上的家具：拖曳中放大 1.08 倍、畫在最上層；家具中心進到下方面板 → 縮到 0.6 倍、面板出現「放開就收起來」（5.28 ④-5）。
        /// </summary>
        void UpdateDrag(Vector2 screen)
        {
            if (_selItem == null) { CancelDrag(); return; }
            var center = ScreenToWorld(screen) + _grabOffset;
            bool placed = TryGetSpawned(_selDecorId, out var o);
            // 場上的家具看「家具中心」有沒有進面板；從卡片拖出來的看手指
            _dragOverPanel = placed ? OverPanel(worldCamera.WorldToScreenPoint(center)) : OverPanel(screen);
            _dragAnchor = GridMath.AnchorAt(_grid.ToDesign(center), W(_selItem), D(_selItem), Vector2.zero);
            _dragFits = !_dragOverPanel && Fits(_selItem, _dragAnchor, _selDecorId);

            if (placed)
            {
                o.gameObject.SetActive(true);
                if (_dragOverPanel)
                {
                    // 面板是畫在最上面的介面，場上的家具會被蓋住：換成介面上的圖跟著手指（0.6 倍）
                    DecorationLayout.ApplyAt(o, _selItem, _grid, o.Anchor);
                    ShowDragUi(o, center, PutAwayScale);
                    o.gameObject.SetActive(false);
                }
                else
                {
                    HideDragUi();
                    DecorationLayout.ApplyAt(o, _selItem, _grid, _dragAnchor);
                    o.transform.position += new Vector3(0f, 6f * _grid.WorldPerPx, 0f); // 拿起來時浮一點
                    ScaleAroundCenter(o, DragScale);
                    o.Renderer.sortingOrder += LiftOrder;
                }
                SetPutAwayLook(true, _dragOverPanel);
            }
            else if (_dragOverPanel) _ghost.enabled = false;
            else ShowGhostAt(_dragAnchor, 0.85f);
        }

        /// <summary>以圖的中心放大／縮小（不改底座位置的算法，只改看起來的大小）。</summary>
        static void ScaleAroundCenter(DecorationObject o, float f)
        {
            var r = o.Renderer;
            if (r == null) return;
            var c = r.bounds.center;
            var t = o.transform;
            t.position = c + (t.position - c) * f;
            t.localScale *= f;
        }

        /// <summary>拖到面板上時，家具的圖畫在介面上（中心在 center、大小＝場上大小 × scale）。</summary>
        void ShowDragUi(DecorationObject o, Vector3 center, float scale)
        {
            var r = o.Renderer;
            if (_dragUi == null || r == null || r.sprite == null || worldCamera == null) return;
            var b = r.bounds;
            Vector2 a = worldCamera.WorldToScreenPoint(b.min), z = worldCamera.WorldToScreenPoint(b.max);
            float px = _canvas.scaleFactor > 0f ? 1f / _canvas.scaleFactor : 1f;
            _dragUi.sprite = r.sprite;
            _dragUi.rectTransform.sizeDelta = new Vector2(Mathf.Abs(z.x - a.x), Mathf.Abs(z.y - a.y)) * px * scale;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, worldCamera.WorldToScreenPoint(center), null, out var local);
            _dragUi.rectTransform.anchoredPosition = local - _root.rect.min;
            _dragUi.transform.SetAsLastSibling();
            _dragUi.enabled = true;
        }

        void HideDragUi() { if (_dragUi != null) _dragUi.enabled = false; }

        /// <summary>拖場上的家具時：面板淡綠＋綠色陰影；家具進到面板裡時再蓋綠色虛線框「放開就收起來」。</summary>
        void SetPutAwayLook(bool dragging, bool over)
        {
            if (_panelBg != null) _panelBg.color = dragging ? PanelDrag : _panelBgColor;
            if (_panelShadow != null) _panelShadow.enabled = dragging;
            if (_putAwayBox != null) _putAwayBox.gameObject.SetActive(dragging && over);
        }

        void EndDrag()
        {
            _dragging = false;
            _down = false;
            if (_scroll != null) _scroll.enabled = true;
            bool wasPlaced = _draggingPlaced;
            _draggingPlaced = false;
            SetPutAwayLook(false, false);
            HideDragUi();
            if (_selItem == null) return;
            // 場上的家具在面板上放開：收起來（格子空出來、存檔，營地成員都看得到）
            if (wasPlaced && _dragOverPanel && _selDecorId != null)
            {
                string id = _selDecorId;
                Deselect();
                DM?.PutAway(id, ok => { if (!ok) Toast("沒有存到，請檢查網路"); });
                return;
            }
            bool moved = !(TryGetSpawned(_selDecorId, out var o) && o.Anchor == _dragAnchor);
            if (_dragFits && moved)
            {
                _targetAnchor = _dragAnchor;
                CommitTarget();
                return;
            }
            // 放不下或沒動：回原位（場上的）／收回面板（卡片），仍然選著
            if (o != null) DecorationLayout.Apply(o, _selItem, _grid);
            if (_ghost != null) _ghost.enabled = false;
            if (!_dragFits && !_dragOverPanel && moved) Toast("這裡放不下");
        }

        void CancelDrag()
        {
            if (!_dragging) return;
            _dragging = false;
            _down = false;
            _draggingPlaced = false;
            SetPutAwayLook(false, false);
            HideDragUi();
            if (_scroll != null) _scroll.enabled = true;
            if (TryGetSpawned(_selDecorId, out var o)) DecorationLayout.Apply(o, _selItem, _grid);
            if (_ghost != null) _ghost.enabled = false;
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
        DecorationObject FindDecorAt(Vector3 world, bool quiet)
        {
            DecorationObject best = null;
            int bestOrder = int.MinValue;
            bool others = false;
            foreach (var col in Physics2D.OverlapPointAll(world))
            {
                var d = col.GetComponent<DecorationObject>();
                if (d == null || !d.gameObject.activeInHierarchy || !d.HasGrid) continue;
                if (!d.CanDelete) { others = true; continue; }
                int order = d.Renderer.sortingOrder;
                if (best == null || order > bestOrder) { best = d; bestOrder = order; }
            }
            if (best == null && others && !quiet) Toast("這件是別人擺的，只有擺的人能移動");
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
            var drop = new List<Cell>();
            foreach (var kv in _tiles) if (!_cells.Contains(kv.Key)) drop.Add(kv.Key);
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

        /// <summary>
        /// 格子顏色（設計文件 5.28 ⑤）：要放的那格（拖曳中放得下＝白框、放不下＝紅）／不能放／選到的家具原本佔的／
        /// 家具佔的／放得下（閃）／一般。
        /// </summary>
        void UpdateTiles()
        {
            if (_skin == null) return;
            var taken = Taken();
            HashSet<Cell> selCells = null, targetCells = null;
            bool targetOk = true;
            if (TryGetSpawned(_selDecorId, out var so) && _selItem != null)
                selCells = new HashSet<Cell>(GridMath.Footprint(so.Anchor, W(_selItem), D(_selItem)));
            if (_dragging && !_dragOverPanel && _selItem != null)
            {
                targetCells = new HashSet<Cell>(GridMath.Footprint(_dragAnchor, W(_selItem), D(_selItem)));
                targetOk = _dragFits;
            }
            else if (_hasTarget && _selItem != null)
                targetCells = new HashSet<Cell>(GridMath.Footprint(_targetAnchor, W(_selItem), D(_selItem)));
            float pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Cos(_pulseT / OkPulse * Mathf.PI * 2f));
            foreach (var kv in _tiles)
            {
                var c = kv.Key;
                var sr = kv.Value;
                Sprite sp;
                float a = 1f;
                if (targetCells != null && targetCells.Contains(c)) sp = targetOk ? _skin.tileTarget : _skin.tileBlocked;
                else if (_blocked.Contains(c)) sp = _skin.tileBlocked;
                else if (selCells != null && selCells.Contains(c)) sp = _skin.tileSelected;
                else if (taken.ContainsKey(c)) sp = _skin.tileOccupied;
                else if (_okTaps.Contains(c) && !_hasTarget) { sp = _skin.tileOk; a = pulse; }
                else sp = _skin.tileNormal;
                if (sr.sprite != sp) sr.sprite = sp;
                sr.color = new Color(1f, 1f, 1f, a);
            }
            // 底座框外面放不下的格子也要看得到（拖到格子外面時）
        }

        /// <summary>選到的場上家具：浮起來一點、上下輕輕晃（發黃光由 DecorationObject.SetSelected 做）。</summary>
        void UpdateSelectedFloat()
        {
            if (_dragging || _hasTarget || !TryGetSpawned(_selDecorId, out var o)) return;
            DecorationLayout.Apply(o, _selItem, _grid);
            float lift = (8f + 3f * Mathf.Sin(_pulseT * 4f)) * _grid.WorldPerPx;
            o.transform.position += new Vector3(0f, lift, 0f);
            o.Renderer.sortingOrder += LiftOrder; // 浮起來時畫在最前面
        }

        /// <summary>家具的半透明預覽放在 anchor（點綠色格子後、或從卡片拖出來時）。</summary>
        void ShowGhostAt(Cell anchor, float alpha)
        {
            if (_ghost == null || _selItem == null) return;
            var sprite = _selItem.worldSprite != null ? _selItem.worldSprite : _selItem.icon;
            _ghost.sprite = sprite;
            _ghost.color = new Color(1f, 1f, 1f, alpha);
            DecorationLayout.PlaceSprite(_ghost.transform, sprite, _selItem, _grid, anchor);
            _ghost.sortingOrder = AIController.SortOrderForY(DecorationLayout.FootWorld(_selItem, anchor, _grid).y) + LiftOrder;
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
            _canvas.sortingOrder = 12; // 狀態欄（10）之上
            go.AddComponent<GraphicRaycaster>();
            _root = (RectTransform)go.transform;
            if (!isActiveAndEnabled) go.SetActive(false);

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

            // 下方面板：deco_panel 高 150；「還沒擺出來的家具」；卡片 deco_card 左右滑，往上拖到營地就擺出來
            _panel = Anchored(_root, "Panel", new Vector2(0.5f, 0f), DesignW, PanelH);
            // 陰影只在拖場上家具時出現（綠色，0 -6px 22px）；陰影要在底圖下面，所以底圖放在後面的子物件
            _panelShadow = UIShapes.AddShadow(_panel, PanelDragShadow, 24, 22, -6);
            _panelShadow.enabled = false;
            _panelBg = Fill(Place(_panel, "Bg", 0, 0, DesignW, PanelH), _skin?.panel, true, Paper, 24);
            _panelBg.raycastTarget = true;
            _panelBgColor = _panelBg.color;
            Label(_panel, 16, 12, 260, 20, "還沒擺出來的家具", 13, Ink, TextAlignmentOptions.Left);
            var view = Place(_panel, "Cards", 0, 38, DesignW, CardH + 10f);
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            _cards = Place(view, "Content", 0, 4, DesignW, CardH);
            _scroll = view.gameObject.AddComponent<ScrollRect>();
            _scroll.content = _cards; _scroll.viewport = view; _scroll.vertical = false; _scroll.movementType = ScrollRect.MovementType.Elastic;
            _emptyText = Label(_panel, 0, 70, DesignW, 20, "家具都擺出來了！去商店可以買新的", 12, Sub, TextAlignmentOptions.Center);

            // 拖到面板上：綠色虛線框（2.5 #6FA45F、底 rgba(227,238,217,0.92)）＋往下箭頭「放開就收起來」16 Black #3F6E33
            _putAwayBox = Place(_panel, "PutAway", 10, 10, DesignW - 20f, PanelH - 20f);
            UIShapes.AddRounded(_putAwayBox.gameObject, PutAwayFill, 18).raycastTarget = false;
            UIShapes.AddRing(Place(_putAwayBox, "Line", 0, 0, DesignW - 20f, PanelH - 20f).gameObject, PutAwayLine, 18).raycastTarget = false;
            Label(_putAwayBox, 0, 0, DesignW - 20f, PanelH - 20f, "↓  放開就收起來", 16, PutAwayText, TextAlignmentOptions.Center);
            _putAwayBox.gameObject.SetActive(false);

            // 拖到面板上時家具的圖（介面最上層）
            var dragRt = new GameObject("DragItem", typeof(RectTransform)).GetComponent<RectTransform>();
            dragRt.SetParent(_root, false);
            dragRt.anchorMin = dragRt.anchorMax = Vector2.zero;
            dragRt.pivot = new Vector2(0.5f, 0.5f);
            _dragUi = dragRt.gameObject.AddComponent<Image>();
            _dragUi.preserveAspect = true;
            _dragUi.raycastTarget = false;
            _dragUi.enabled = false;

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
            _selCard = -1;
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
        }

        (RectTransform, DecorationItem, string, Image) BuildCard(DecorationItem item, string legacyId, float x)
        {
            var c = Place(_cards, "Card_" + item.itemId, x, 0, CardW, CardH);
            // 選到的外框 deco_card_ring（86 × 98，外擴 3）
            var ringRt = Place(c, "Ring", -3, -3, CardW + 6f, CardH + 6f);
            var ring = Fill(ringRt, _skin?.cardRing, false, new Color(0.965f, 0.788f, 0.271f), 16);
            ring.raycastTarget = false;
            ring.enabled = false;
            Fill(Place(c, "Bg", 0, 0, CardW, CardH), _skin?.card, false, Color.white, 14).raycastTarget = true;
            var pic = Img(c, item.icon != null ? item.icon : item.worldSprite, 10, 8, CardW - 20f, 50);
            pic.preserveAspect = true;
            var name = Label(c, 4, 60, CardW - 8f, 16, item.displayName, 11, Ink, TextAlignmentOptions.Center);
            name.overflowMode = TextOverflowModes.Ellipsis;
            Label(c, 4, 74, CardW - 8f, 16, $"佔 {W(item) * D(item)} 格", 10, Sub, TextAlignmentOptions.Center);
            return (c, item, legacyId, ring);
        }

        /// <summary>選到的家具上方小標「營火・拖到綠色格子」：跟著場上的家具，或在面板裡那張卡上面；拖曳中不顯示。</summary>
        void UpdateTag()
        {
            if (_selItem == null || _dragging || _hasTarget) { _tag.gameObject.SetActive(false); return; }
            string text = _okTaps.Count == 0 ? $"{_selItem.displayName}・沒有放得下的地方" : $"{_selItem.displayName}・拖到綠色格子";
            if (_tagText.text != text)
            {
                _tagText.text = text;
                float tw = Mathf.Ceil(_tagText.GetPreferredValues(text).x);
                _tagText.rectTransform.sizeDelta = new Vector2(tw, 26f);
                _tag.sizeDelta = new Vector2(tw + 20f, 26f);
            }
            Vector2 screen;
            if (TryGetSpawned(_selDecorId, out var o))
                screen = worldCamera.WorldToScreenPoint(o.TopCenter);
            else if (_selCard >= 0 && _selCard < _cardViews.Count)
            {
                var corners = new Vector3[4];
                _cardViews[_selCard].rt.GetWorldCorners(corners);
                screen = RectTransformUtility.WorldToScreenPoint(null, (corners[1] + corners[2]) / 2f);
            }
            else { _tag.gameObject.SetActive(false); return; }
            screen.y += 6f * _lastK * _canvas.scaleFactor;
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
            _top.localScale = _panel.localScale = _toast.localScale = Vector3.one * k;
            _top.anchoredPosition = new Vector2(0f, -top);
            _panel.anchoredPosition = new Vector2(0f, bottom);
        }

        void Toast(string msg)
        {
            _toastText.text = msg;
            _toastUntil = Time.unscaledTime + 2f;
        }

        // ─── 小工具 ────────────────────────────────────────────────────

        List<RaycastResult> RaycastUI(Vector2 screen)
        {
            var hits = new List<RaycastResult>();
            var es = EventSystem.current;
            if (es == null) return hits;
            es.RaycastAll(new PointerEventData(es) { position = screen }, hits);
            return hits;
        }

        bool OverUI(Vector2 screen) => RaycastUI(screen).Count > 0;

        bool OverPanel(Vector2 screen) =>
            _panel.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(_panel, screen, null);

        /// <summary>手指下是哪張卡片（沒有回 −1）。</summary>
        int CardAt(Vector2 screen)
        {
            if (!OverPanel(screen)) return -1;
            for (int i = 0; i < _cardViews.Count; i++)
                if (RectTransformUtility.RectangleContainsScreenPoint(_cardViews[i].rt, screen, null)) return i;
            return -1;
        }

        static bool ReadPointer(out bool down, out bool held, out bool up, out Vector2 pos)
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            down = Input.GetMouseButtonDown(0);
            held = Input.GetMouseButton(0);
            up = Input.GetMouseButtonUp(0);
            pos = Input.mousePosition;
            return down || held || up;
#else
            down = held = up = false;
            pos = default;
            if (Input.touchCount == 0) return false;
            var t = Input.GetTouch(0);
            pos = t.position;
            down = t.phase == TouchPhase.Began;
            up = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
            held = !up;
            return true;
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
            if (sprite == null)
            {
                if (radius > 0f) return UIShapes.AddRounded(rt.gameObject, fallback, radius);
                var plain = rt.gameObject.AddComponent<Image>();
                plain.color = fallback;
                return plain;
            }
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
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
