using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Events;

/// <summary>
/// One cell in the shop grid. Binds to <see cref="ShopItemData"/> and reports clicks to the market controller.
/// </summary>
public class ShopItemCard : MonoBehaviour
{
    public enum CardState
    {
        Locked,
        Buy,
        Owned,
        Equipped
    }

    [Header("UI")]
    [SerializeField] Button button;
    [SerializeField] Image iconImage;
    [SerializeField] TMP_Text nameText;
    [SerializeField] TMP_Text priceText;
    [SerializeField] TMP_Text stateLabel;
    [SerializeField] GameObject ownedBadge;

    ShopItemData _data;
    UnityAction<ShopItemData> _onClick;

    void Awake()
    {
        if (button == null)
            button = GetComponent<Button>();
        if (button != null)
            button.onClick.AddListener(OnButtonClicked);
    }

    void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(OnButtonClicked);
    }

    public void Bind(ShopItemData data, CardState state, UnityAction<ShopItemData> onClick)
    {
        _data = data;
        _onClick = onClick;

        if (iconImage != null)
        {
            iconImage.sprite = data != null ? data.icon : null;
            iconImage.enabled = data != null && data.icon != null;
        }
        if (nameText != null)
            nameText.text = data != null ? data.itemName : "";

        if (priceText != null)
        {
            if (data == null) { priceText.text = ""; }
            else if (data.category == MarketCategory.Money)
                priceText.text = data.price > 0 ? $"{data.price} coins" : "Free";
            else if (data.gemPrice > 0 && data.price > 0)
                priceText.text = $"{data.price} + {data.gemPrice} gems";
            else if (data.gemPrice > 0)
                priceText.text = $"{data.gemPrice} gems";
            else
                priceText.text = data.price > 0 ? $"{data.price} coins" : "Free";
        }

        if (ownedBadge != null)
            ownedBadge.SetActive(data != null && data.isOwned && state != CardState.Equipped);

        if (stateLabel != null)
        {
            switch (state)
            {
                case CardState.Locked:
                    stateLabel.text = "Locked";
                    stateLabel.gameObject.SetActive(true);
                    break;
                case CardState.Buy:
                    stateLabel.text = "Buy";
                    stateLabel.gameObject.SetActive(true);
                    break;
                case CardState.Owned:
                    stateLabel.text = "Owned";
                    stateLabel.gameObject.SetActive(true);
                    break;
                case CardState.Equipped:
                    stateLabel.text = "Equipped";
                    stateLabel.gameObject.SetActive(true);
                    break;
            }
        }

        if (button != null)
            button.interactable = state != CardState.Locked;
    }

    void OnButtonClicked()
    {
        if (_data != null)
            _onClick?.Invoke(_data);
    }
}
