using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reusable chat message row view: avatar + display name + bubble text + optional reply quote.
/// Attach this to your message prefab root. Reply tap is handled by a Button on this row (wired from ChatUIHandler).
/// </summary>
public class GroupChatMessageView : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] Image avatarImage;
    [SerializeField] TMP_Text displayNameText;

    [Header("Message")]
    [SerializeField] TMP_Text messageText;
    [Tooltip("Optional bubble background Image (used by ChatUIHandler to set self/other sprites).")]
    [SerializeField] Image bubbleBackground;

    [Header("Reply quote (optional)")]
    [SerializeField] GameObject replyQuoteRoot;
    [SerializeField] TMP_Text replyQuoteNameText;
    [SerializeField] TMP_Text replyQuotePreviewText;

    public Image BubbleBackground => bubbleBackground;

    public void Bind(
        ChatMessage msg,
        Sprite avatarSprite,
        bool isSelf)
    {
        if (avatarImage != null)
        {
            avatarImage.sprite = avatarSprite;
            avatarImage.enabled = avatarSprite != null;
        }

        if (displayNameText != null)
            displayNameText.text = ChatMessage.GetBestDisplayName(msg);

        if (messageText != null)
            messageText.text = msg?.message ?? string.Empty;

        bool hasReply = msg != null && !string.IsNullOrEmpty(msg.replyToDisplayName) && !string.IsNullOrEmpty(msg.replyToMessagePreview);
        if (replyQuoteRoot != null)
            replyQuoteRoot.SetActive(hasReply);
        if (hasReply)
        {
            if (replyQuoteNameText != null)
                replyQuoteNameText.text = msg.replyToDisplayName;
            if (replyQuotePreviewText != null)
                replyQuotePreviewText.text = msg.replyToMessagePreview;
        }
    }
}

