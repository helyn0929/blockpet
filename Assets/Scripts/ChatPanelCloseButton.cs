using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Returns to Home in the page system, or deactivates a legacy chat panel root.
/// Works on Chat or Album back buttons when <see cref="pageManager"/> is assigned.
/// By default wires the <see cref="Button"/> on the <b>same</b> GameObject.
/// </summary>
public class ChatPanelCloseButton : MonoBehaviour
{
    [Tooltip("Preferred: assign PageManager so Chat/Album backs call ShowHomePage().")]
    [SerializeField] PageManager pageManager;

    [Tooltip("Legacy: chat panel root when not using PageManager.")]
    [SerializeField] GameObject chatPanel;

    [Tooltip("If this object has a Button, CloseChat runs on click automatically.")]
    [SerializeField] bool wireSameGameObjectButton = true;

    void Awake()
    {
        if (!wireSameGameObjectButton)
            return;
        Button btn = GetComponent<Button>();
        if (btn != null)
            btn.onClick.AddListener(CloseChat);
    }

    void OnDestroy()
    {
        Button btn = GetComponent<Button>();
        if (btn != null)
            btn.onClick.RemoveListener(CloseChat);
    }

    /// <summary>Also callable from Button OnClick () if you prefer the Inspector list.</summary>
    public void CloseChat()
    {
        if (pageManager != null)
            pageManager.ShowHomePage();
        else if (chatPanel != null)
            chatPanel.SetActive(false);
    }
}
