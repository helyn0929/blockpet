using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Toggle chat panel open/closed when the chat button is pressed.
/// Add this to an always-active GameObject (e.g. Canvas or the chat button),
/// assign the chat button and chat panel, and it will replace the button's
/// onClick with a toggle so pressing again closes the chat.
/// </summary>
public class ChatPanelToggle : MonoBehaviour
{
    [SerializeField] Button chatButton;
    [Tooltip("Legacy: chat panel root. Not needed if Page Manager is set.")]
    [SerializeField] GameObject chatPanel;
    [Tooltip("When set, opens Chat page (no toggle). Use with CameraUIManager disabled.")]
    [SerializeField] PageManager pageManager;

    void Start()
    {
        if (chatButton == null)
        {
            Debug.LogWarning("[ChatPanelToggle] Assign Chat Button in the Inspector.");
            return;
        }

        if (pageManager == null && chatPanel == null)
        {
            Debug.LogWarning("[ChatPanelToggle] Assign Chat Panel or Page Manager.");
            return;
        }

        chatButton.onClick.RemoveAllListeners();
        chatButton.onClick.AddListener(ToggleChat);
    }

    public void ToggleChat()
    {
        if (pageManager != null)
            pageManager.ShowChatPage();
        else if (chatPanel != null)
            chatPanel.SetActive(!chatPanel.activeSelf);
    }
}
