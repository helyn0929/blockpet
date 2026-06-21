using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class ChatUIController : MonoBehaviour
{
    [SerializeField] ChatUIHandler chatUIHandler;
    [SerializeField] PageManager pageManager;

    UIDocument _doc;
    VisualElement _root;

    Label _roomName;
    Label _memberCount;
    ScrollView _messageScroll;
    VisualElement _replyBar;
    Label _replyText;
    TextField _messageInput;

    string _localUserId;
    bool _mineOnRight = true;

    string _pendingReplyId;
    string _pendingReplyDisplayName;
    string _pendingReplyPreview;

    void Awake()
    {
        _doc = GetComponent<UIDocument>();
        if (chatUIHandler == null) chatUIHandler = FindObjectOfType<ChatUIHandler>(true);
        if (pageManager == null)   pageManager   = FindObjectOfType<PageManager>(true);
    }

    void OnEnable()
    {
        _root = _doc.rootVisualElement;

        _roomName      = _root.Q<Label>("room-name");
        _memberCount   = _root.Q<Label>("member-count");
        _messageScroll = _root.Q<ScrollView>("message-scroll");
        _replyBar      = _root.Q<VisualElement>("reply-bar");
        _replyText     = _root.Q<Label>("reply-text");
        _messageInput  = _root.Q<TextField>("message-input");

        WireButton("btn-back",         OnClickBack);
        WireButton("btn-send",         OnClickSend);
        WireButton("btn-clear-reply",  OnClickClearReply);

        if (_messageInput != null)
            _messageInput.RegisterCallback<KeyDownEvent>(OnInputKeyDown);
    }

    // ── Public API — called by ChatUIHandler ──────────────────────────────────

    public void RequestFullSync(
        IReadOnlyList<ChatMessage> messages,
        string roomName,
        string roomId,
        int memberCount,
        string localDisplayName,
        bool mineOnRight,
        string animalImageBase64,
        bool useNativeComposer)
    {
        _localUserId = FirebaseManager.Instance?.GetUserId() ?? string.Empty;
        _mineOnRight = mineOnRight;

        ClearMessages();
        if (messages != null)
            foreach (var msg in messages)
                AppendBubble(msg);

        UpdateHeader(roomName, memberCount);
        ScrollToBottom();
    }

    public void NotifyMessageAppended(ChatMessage msg)
    {
        if (msg == null) return;
        AppendBubble(msg);
        ScrollToBottom();
    }

    public void NotifyHeader(string roomName, int memberCount)
    {
        UpdateHeader(roomName, memberCount);
    }

    public void NotifyReplyCleared()
    {
        ClearReplyUI();
    }

    // ── UI building ───────────────────────────────────────────────────────────

    void ClearMessages()
    {
        _messageScroll?.contentContainer?.Clear();
    }

    void AppendBubble(ChatMessage msg)
    {
        if (_messageScroll == null || msg == null) return;

        bool isMine = !string.IsNullOrEmpty(_localUserId) && msg.senderId == _localUserId;

        // Row
        var row = new VisualElement();
        row.AddToClassList("msg-row");
        if (isMine) row.AddToClassList("msg-row--mine");

        // Avatar (others only)
        var avatar = new VisualElement();
        avatar.AddToClassList("msg-avatar");
        if (isMine) avatar.AddToClassList("msg-avatar--mine");
        string senderName = ChatMessage.GetBestDisplayName(msg);
        var avatarLabel = new Label(GetInitial(senderName));
        avatarLabel.AddToClassList("msg-avatar-label");
        avatar.Add(avatarLabel);
        row.Add(avatar);

        // Column
        var col = new VisualElement();
        col.AddToClassList("msg-col");
        if (isMine) col.AddToClassList("msg-col--mine");

        // Sender name (others only)
        if (!isMine && !string.IsNullOrEmpty(senderName))
        {
            var senderLabel = new Label(senderName);
            senderLabel.AddToClassList("msg-sender");
            col.Add(senderLabel);
        }

        // Bubble
        var bubble = new VisualElement();
        bubble.AddToClassList("msg-bubble");
        if (isMine) bubble.AddToClassList("msg-bubble--mine");

        // Reply quote
        if (!string.IsNullOrEmpty(msg.replyToMessageId) && !string.IsNullOrEmpty(msg.replyToDisplayName))
        {
            var quote = new VisualElement();
            quote.AddToClassList("reply-quote");

            var accent = new VisualElement();
            accent.AddToClassList("reply-quote-accent");
            if (isMine) accent.AddToClassList("reply-quote-accent--mine");
            quote.Add(accent);

            var quoteBody = new VisualElement();
            quoteBody.AddToClassList("reply-quote-body");

            var quoteName = new Label(msg.replyToDisplayName);
            quoteName.AddToClassList("reply-quote-name");
            if (isMine) quoteName.AddToClassList("reply-quote-name--mine");
            quoteBody.Add(quoteName);

            if (!string.IsNullOrEmpty(msg.replyToMessagePreview))
            {
                var quoteText = new Label(msg.replyToMessagePreview);
                quoteText.AddToClassList("reply-quote-text");
                if (isMine) quoteText.AddToClassList("reply-quote-text--mine");
                quoteBody.Add(quoteText);
            }

            quote.Add(quoteBody);
            bubble.Add(quote);
        }

        // Message text
        var msgText = new Label(msg.message ?? string.Empty);
        msgText.AddToClassList("msg-text");
        if (isMine) msgText.AddToClassList("msg-text--mine");
        bubble.Add(msgText);

        // Tap to reply
        string capturedId          = msg.messageId;
        string capturedSenderName  = senderName;
        string capturedText        = msg.message ?? string.Empty;
        bubble.RegisterCallback<ClickEvent>(_ => OnTapBubble(capturedId, capturedSenderName, capturedText));

        col.Add(bubble);

        // Timestamp
        if (msg.timestamp > 0)
        {
            var time = new Label(FormatTime(msg.timestamp));
            time.AddToClassList("msg-time");
            col.Add(time);
        }

        row.Add(col);
        _messageScroll.contentContainer.Add(row);
    }

    void UpdateHeader(string roomName, int memberCount)
    {
        if (_roomName != null)
            _roomName.text = string.IsNullOrEmpty(roomName) ? "Chat" : roomName;
        if (_memberCount != null)
            _memberCount.text = memberCount > 0 ? $"{memberCount} members" : string.Empty;
    }

    void ScrollToBottom()
    {
        // Defer one frame so layout is computed before scrolling.
        _messageScroll?.schedule.Execute(() =>
        {
            if (_messageScroll != null)
                _messageScroll.scrollOffset = new Vector2(0, float.MaxValue);
        });
    }

    void ShowReplyPreview(string replyId, string displayName, string preview)
    {
        _pendingReplyId          = replyId;
        _pendingReplyDisplayName = displayName;
        _pendingReplyPreview     = preview;

        if (_replyText != null)
            _replyText.text = $"{displayName}: {preview}";
        _replyBar?.AddToClassList("reply-bar--visible");
    }

    void ClearReplyUI()
    {
        _pendingReplyId          = null;
        _pendingReplyDisplayName = null;
        _pendingReplyPreview     = null;
        _replyBar?.RemoveFromClassList("reply-bar--visible");
        if (_replyText != null) _replyText.text = string.Empty;
    }

    // ── Button callbacks ──────────────────────────────────────────────────────

    void OnClickBack()
    {
        chatUIHandler?.WebViewRequestBack();
        if (pageManager != null) pageManager.ShowRoomPage();
    }

    void OnClickSend()
    {
        string text = _messageInput?.value?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(text)) return;

        chatUIHandler?.SendFromWebView(text, _pendingReplyId, _pendingReplyDisplayName, _pendingReplyPreview);
        if (_messageInput != null) _messageInput.value = string.Empty;
        ClearReplyUI();
    }

    void OnClickClearReply()
    {
        chatUIHandler?.WebViewClearReply();
        ClearReplyUI();
    }

    void OnTapBubble(string messageId, string senderName, string messageText)
    {
        if (string.IsNullOrEmpty(messageId)) return;
        chatUIHandler?.WebViewSelectReply(messageId, senderName, senderName, messageText);
        ShowReplyPreview(messageId, senderName, messageText);
    }

    void OnInputKeyDown(KeyDownEvent e)
    {
        if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            OnClickSend();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void WireButton(string name, Action callback)
    {
        var btn = _root?.Q<Button>(name);
        if (btn != null) btn.clicked += callback;
        else Debug.LogWarning($"[ChatUIController] Button '{name}' not found.");
    }

    static string GetInitial(string name)
    {
        if (string.IsNullOrEmpty(name)) return "?";
        return name.Substring(0, 1).ToUpper();
    }

    static string FormatTime(long unixSeconds)
    {
        try
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime();
            return dt.ToString("HH:mm");
        }
        catch { return string.Empty; }
    }
}
