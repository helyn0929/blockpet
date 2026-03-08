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
    [SerializeField] GameObject chatPanel;

    void Start()
    {
        if (chatButton == null || chatPanel == null)
        {
            Debug.LogWarning("[ChatPanelToggle] Assign Chat Button and Chat Panel in the Inspector.");
            return;
        }

        chatButton.onClick.RemoveAllListeners();
        chatButton.onClick.AddListener(ToggleChat);
    }

    public void ToggleChat()
    {
        chatPanel.SetActive(!chatPanel.activeSelf);
    }
}
