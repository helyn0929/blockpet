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

            _doneBtn.clicked += OnDoneClicked;

            BuildItemList();
            Hide();
        }

        void OnDisable()
        {
            if (_doneBtn != null)
                _doneBtn.clicked -= OnDoneClicked;
        }

        // ─── Show / Hide ───────────────────────────────────────────────

        public void Show()
        {
            _selectedBtn = null;
            _overlay.style.display = DisplayStyle.Flex;
            // 商店家具要空間買過才列出來；每次打開重新讀一次（其他成員可能剛買）
            if (FirebaseManager.Instance != null)
                FirebaseManager.Instance.GetRoomShop(owned => { _owned = owned; BuildItemList(); });
        }

        Dictionary<string, int> _owned = new Dictionary<string, int>();

        public void Hide()
        {
            _overlay.style.display = DisplayStyle.None;
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
