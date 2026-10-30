using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using BlockPet.Decoration;
using BlockPet.UI.Market;

namespace BlockPet.UI.Home
{
    [RequireComponent(typeof(UIDocument))]
    public class DecorationPickerController : MonoBehaviour
    {
        [SerializeField] DecorationDatabase database;
        [SerializeField] RoomEditorController roomEditor;

        UIDocument _doc;
        VisualElement _overlay;
        VisualElement _itemContainer;
        Button _doneBtn;
        Button _selectedBtn;
        VisualElement _panel, _actionBar;
        Button _actionMain, _actionAlt;
        Label _title, _emptyHint;
        bool _actionIsNew;

        // ─── Lifecycle ─────────────────────────────────────────────────

        void Awake()
        {
            _doc = GetComponent<UIDocument>();
        }

        void OnEnable()
        {
            var root = _doc.rootVisualElement;
            _overlay       = root.Q("overlay");
            _itemContainer = root.Q("item-container");
            _doneBtn       = root.Q<Button>("done-btn");
            _panel         = root.Q("picker-panel");
            _actionBar     = root.Q("action-bar");
            _actionMain    = root.Q<Button>("action-main");
            _actionAlt     = root.Q<Button>("action-alt");
            _title         = root.Q<Label>("picker-title");
            _emptyHint     = root.Q<Label>("empty-hint");

            _doneBtn.clicked += OnDoneClicked;
            if (_actionMain != null) _actionMain.clicked += OnActionMain;
            if (_actionAlt != null) _actionAlt.clicked += OnActionAlt;

            BuildItemList();
            Hide();
        }

        void OnDisable()
        {
            if (_doneBtn != null)
                _doneBtn.clicked -= OnDoneClicked;
            if (_actionMain != null) _actionMain.clicked -= OnActionMain;
            if (_actionAlt != null) _actionAlt.clicked -= OnActionAlt;
        }

        // ─── Show / Hide ───────────────────────────────────────────────

        public void Show()
        {
            _selectedBtn = null;
            _overlay.style.display = DisplayStyle.Flex;
            HideActions();
            if (_title != null)
            {
                string pet = FirebaseManager.Instance?.CurrentPetName;
                _title.text = string.IsNullOrEmpty(pet) ? "佈置營地" : $"佈置 {pet} 的家";
            }
            // 商店家具要空間買過才列出來；每次打開重新讀一次（其他成員可能剛買）
            if (FirebaseManager.Instance != null)
                FirebaseManager.Instance.GetRoomShop(owned => { _owned = owned; BuildItemList(); });
        }

        Dictionary<string, int> _owned = new Dictionary<string, int>();

        public void Hide()
        {
            _overlay.style.display = DisplayStyle.None;
            HideActions();
        }

        // ─── Touch routing ─────────────────────────────────────────────

        /// <summary>這個螢幕座標（左下原點，Input 的座標）是否按在家具列或浮動按鈕上。</summary>
        public bool IsOverUI(Vector2 screenPos)
        {
            var panel = _doc != null ? _doc.rootVisualElement?.panel : null;
            if (panel == null || _overlay.resolvedStyle.display == DisplayStyle.None) return false;
            var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
            // 根節點可能也能被點到，所以只算家具列和浮動按鈕本身
            for (var e = panel.Pick(p); e != null; e = e.parent)
                if (e == _panel || e == _actionBar) return true;
            return false;
        }

        // ─── Floating actions ──────────────────────────────────────────

        /// <summary>
        /// 把浮動按鈕放在家具上方。isNew：新家具（放這裡／取消）；否則是選中的已擺家具（收回／好了）。
        /// 太靠上就改放在下面一點，不要被頂部狀態欄蓋住。
        /// </summary>
        public void ShowActions(Vector3 worldTop, Camera cam, bool isNew)
        {
            if (_actionBar == null || cam == null) return;
            var panel = _doc.rootVisualElement.panel;
            if (panel == null) return;

            if (_actionBar.resolvedStyle.display == DisplayStyle.None || _actionIsNew != isNew)
            {
                _actionIsNew = isNew;
                _actionMain.text = isNew ? "放這裡" : "收回";
                _actionAlt.text = isNew ? "取消" : "好了";
                _actionMain.EnableInClassList("action-btn--main", isNew);
                _actionMain.EnableInClassList("action-btn--danger", !isNew);
                _actionBar.style.display = DisplayStyle.Flex;
            }

            Vector2 p = RuntimePanelUtils.CameraTransformWorldToPanel(panel, worldTop, cam);
            float w = _actionBar.resolvedStyle.width;
            float h = _actionBar.resolvedStyle.height;
            if (float.IsNaN(w)) w = 0f;
            if (float.IsNaN(h)) h = 0f;
            var rootRect = _doc.rootVisualElement.worldBound;
            float panelTop = _panel != null ? _panel.worldBound.yMin : rootRect.height;

            float x = Mathf.Clamp(p.x - w * 0.5f, 16f, Mathf.Max(16f, rootRect.width - w - 16f));
            float y = p.y - h - 24f;
            if (y < 260f) y = p.y + 24f;                 // 頂部狀態欄底下
            y = Mathf.Min(y, panelTop - h - 16f);        // 不要壓到家具列
            _actionBar.style.left = x;
            _actionBar.style.top = y;
        }

        public void HideActions()
        {
            if (_actionBar != null) _actionBar.style.display = DisplayStyle.None;
        }

        void OnActionMain()
        {
            if (_actionIsNew) { roomEditor.OnActionConfirm(); ClearSelectedButton(); }
            else roomEditor.OnActionRemove();
        }

        void OnActionAlt()
        {
            if (_actionIsNew) { roomEditor.OnActionCancel(); ClearSelectedButton(); }
            else roomEditor.OnActionDone();
        }

        void ClearSelectedButton()
        {
            if (_selectedBtn != null) _selectedBtn.RemoveFromClassList("item-btn--selected");
            _selectedBtn = null;
        }

        // ─── Build item list ───────────────────────────────────────────

        void BuildItemList()
        {
            _itemContainer.Clear();
            if (database == null) return;

            foreach (var item in database.GetAll())
            {
                if (item == null) continue;
                bool shop = MarketCatalog.IsShopFurniture(item.itemId);
                if (shop && !_owned.ContainsKey(item.itemId)) continue;
                _itemContainer.Add(CreateItemButton(item, shop));
            }
            if (_emptyHint != null)
                _emptyHint.style.display = _itemContainer.childCount == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        VisualElement CreateItemButton(DecorationItem item, bool fromShop)
        {
            var btn = new Button();
            btn.AddToClassList("item-btn");

            var icon = new VisualElement();
            icon.AddToClassList("item-icon");
            if (item.icon != null)
                icon.style.backgroundImage = new StyleBackground(item.icon);
            btn.Add(icon);

            var nameLabel = new Label(item.displayName);
            nameLabel.AddToClassList("item-name");
            btn.Add(nameLabel);

            // 商店買來的家具已經付過錢，擺放不用再顯示價格
            var priceLabel = new Label(fromShop ? "已擁有" : $"$ {item.price}");
            priceLabel.AddToClassList("item-price");
            btn.Add(priceLabel);

            btn.clicked += () => OnItemClicked(item, btn);
            return btn;
        }

        // ─── Interaction ───────────────────────────────────────────────

        void OnItemClicked(DecorationItem item, Button btn)
        {
            if (_selectedBtn != null)
                _selectedBtn.RemoveFromClassList("item-btn--selected");

            _selectedBtn = btn;
            btn.AddToClassList("item-btn--selected");

            roomEditor.OnItemSelected(item);
        }

        void OnDoneClicked()
        {
            roomEditor.OnExitEditModeButton();
        }
    }
}
