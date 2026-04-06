using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Opens the Market (shop) page from Home. Assign the button and optionally <see cref="PageManager"/>;
/// if PageManager is empty, it is resolved at runtime.
/// </summary>
public class HomeShopButton : MonoBehaviour
{
    [SerializeField] Button shopButton;
    [SerializeField] PageManager pageManager;

    void Awake()
    {
        if (pageManager == null)
            pageManager = FindObjectOfType<PageManager>(true);
        if (shopButton == null)
            shopButton = GetComponent<Button>();
    }

    void Start()
    {
        if (shopButton == null)
        {
            Debug.LogWarning("[HomeShopButton] Assign Shop Button (or put this on the same GameObject as Button).");
            return;
        }

        shopButton.onClick.RemoveAllListeners();
        shopButton.onClick.AddListener(OpenShop);
    }

    public void OpenShop()
    {
        if (pageManager == null)
            pageManager = FindObjectOfType<PageManager>(true);
        pageManager?.ShowMarketPage();
    }
}
