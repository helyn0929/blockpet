using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Returns to Room selection page from Home. Attach to the back button on HomePage.
/// </summary>
public class HomeRoomButton : MonoBehaviour
{
    [SerializeField] Button backButton;
    [SerializeField] PageManager pageManager;

    void Awake()
    {
        if (pageManager == null)
            pageManager = FindObjectOfType<PageManager>(true);
        if (backButton == null)
            backButton = GetComponent<Button>();
    }

    void Start()
    {
        if (backButton == null)
        {
            Debug.LogWarning("[HomeRoomButton] Assign Back Button (or put this on the same GameObject as Button).");
            return;
        }

        backButton.onClick.RemoveAllListeners();
        backButton.onClick.AddListener(GoToRoomPage);
    }

    public void GoToRoomPage()
    {
        if (pageManager == null)
            pageManager = FindObjectOfType<PageManager>(true);
        pageManager?.ShowRoomPage();
    }
}
