using System;
using System.Collections.Generic;
using UnityEngine;

public class ChatUIHandler : MonoBehaviour
{
    static ChatUIHandler _instance;

    public static ChatUIHandler Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindObjectOfType<ChatUIHandler>(true);
            return _instance;
        }
    }

    [Header("Navigation")]
    [SerializeField] PageManager pageManager;
    [SerializeField] string startupRoomDisplayName = "Chat";
    [SerializeField] int startupMemberCount;

    [Header("UI Toolkit chat")]
    [SerializeField] ChatUIController chatUIController;
    [SerializeField] bool mineMessagesOnRight = true;

    string _headerRoomName;
    int _headerMemberCount;
    readonly List<ChatMessage> localHistory = new List<ChatMessage>();

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning("[ChatUIHandler] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        _instance = this;
        _headerRoomName = startupRoomDisplayName;
        _headerMemberCount = startupMemberCount;
        SaveManager.OnRoomSwitched += OnRoomSwitched;
    }

    void OnDestroy()
    {
        SaveManager.OnRoomSwitched -= OnRoomSwitched;
        if (_instance == this) _instance = null;
    }

    void OnEnable()
    {
        FirebaseManager.Instance?.EnsureChatListening();
        if (FirebaseManager.Instance != null)
        {
            SetRoomHeader($"Room: {FirebaseManager.Instance.RoomId}", _headerMemberCount);
            FetchAndUpdateMembers();
        }
    }

    void Start()
    {
        SetRoomHeader(startupRoomDisplayName, startupMemberCount);
        LoadChatHistoryAndRebuildUI();
    }

    void OnRoomSwitched()
    {
        localHistory.Clear();
        PushFullStateToWebView();
        if (FirebaseManager.Instance != null)
        {
            SetRoomHeader($"Room: {FirebaseManager.Instance.RoomId}", 0);
            FetchAndUpdateMembers();
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void SetRoomHeader(string roomName, int memberCount)
    {
        _headerRoomName = roomName ?? string.Empty;
        _headerMemberCount = memberCount;
        chatUIController?.NotifyHeader(_headerRoomName, _headerMemberCount);
    }

    public void PushFullStateToWebView()
    {
        if (chatUIController == null) return;
        string roomId      = FirebaseManager.Instance?.RoomId ?? string.Empty;
        string localName   = GetLocalSenderDisplayName();
        string animalBase64 = GetHeaderAnimalImageBase64Png();
        chatUIController.RequestFullSync(
            localHistory, _headerRoomName, roomId, _headerMemberCount,
            localName, mineMessagesOnRight, animalBase64, false);
    }

    public void DisplayMessage(ChatMessage msg)
    {
        if (chatUIController == null || msg == null) return;

        if (!string.IsNullOrEmpty(msg.messageId))
        {
            if (localHistory.Exists(m => !string.IsNullOrEmpty(m.messageId) && m.messageId == msg.messageId))
                return;
        }
        else
        {
            if (localHistory.Exists(m => m.userName == msg.userName &&
                                         m.message  == msg.message  &&
                                         m.timestamp == msg.timestamp))
                return;
        }

        localHistory.Add(msg);
        chatUIController.NotifyMessageAppended(msg);
    }

    public void SendFromWebView(string text, string replyToMessageId, string replyToDisplayName, string replyToMessagePreview)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (FirebaseManager.Instance == null)
        {
            Debug.LogError("[ChatUIHandler] FirebaseManager not found.");
            return;
        }

        var outgoing = new ChatMessage(GetLocalSenderDisplayName(), text.Trim())
        {
            senderId = FirebaseManager.Instance.GetUserId() ?? string.Empty
        };

        if (!string.IsNullOrEmpty(replyToMessageId))
        {
            outgoing.replyToMessageId      = replyToMessageId;
            outgoing.replyToDisplayName    = replyToDisplayName    ?? string.Empty;
            outgoing.replyToMessagePreview = replyToMessagePreview ?? string.Empty;
        }

        FirebaseManager.Instance.SendChatMessage(outgoing);
        chatUIController?.NotifyReplyCleared();
    }

    public void WebViewRequestBack() => OnBackClicked();

    public void WebViewRequestLeaveChat()
    {
        FirebaseManager.Instance?.StopChatListening();
        OnBackClicked();
    }

    // Reply state is tracked entirely in ChatUIController; this is kept for call-site compatibility.
    public void WebViewSelectReply(string messageId, string userName, string displayName, string messageBody) { }

    public void WebViewClearReply() => chatUIController?.NotifyReplyCleared();

    public void WebViewRequestOpenAlbum()
    {
        if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
        pageManager?.ShowAlbumFromChat();
    }

    public void WebViewSetRoom(string roomId)
    {
        if (FirebaseManager.Instance == null) return;
        FirebaseManager.Instance.SetRoomId(roomId);
        SetRoomHeader($"Room: {FirebaseManager.Instance.RoomId}", 0);
    }

    public bool IsWebViewChatEnabled() => false;

    public void RefreshChatLayout() { }

    // ── Private ───────────────────────────────────────────────────────────────

    void FetchAndUpdateMembers()
    {
        string roomId = FirebaseManager.Instance?.RoomId;
        if (string.IsNullOrEmpty(roomId)) return;

        FirebaseManager.Instance.GetRoomMembers(roomId, members =>
        {
            if (members == null || members.Count == 0) return;
            _headerMemberCount = members.Count;
            SetRoomHeader(_headerRoomName, _headerMemberCount);
            chatUIController?.NotifyMembers(members);
        });
    }

    void OnBackClicked()
    {
        if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
        pageManager?.ShowHomePage();
    }

    void LoadChatHistoryAndRebuildUI()
    {
        localHistory.Clear();
        PushFullStateToWebView();
    }

    string GetLocalSenderDisplayName() =>
        FirebaseManager.Instance != null ? FirebaseManager.Instance.GetDisplayName() : "Guest";

    string GetHeaderAnimalImageBase64Png()
    {
        try
        {
            Texture2D tex = AvatarManager.Instance?.CurrentAvatar;
            if (tex == null) return null;
            byte[] png;
            try { png = tex.EncodeToPNG(); }
            catch { png = EncodeToPngViaReadback(tex); }
            if (png == null || png.Length == 0) return null;
            return Convert.ToBase64String(png);
        }
        catch { return null; }
    }

    static byte[] EncodeToPngViaReadback(Texture2D tex)
    {
        if (tex == null) return null;
        RenderTexture rt   = null;
        RenderTexture prev = RenderTexture.active;
        try
        {
            rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply(false, false);
            byte[] png = copy.EncodeToPNG();
            Destroy(copy);
            return png;
        }
        catch { return null; }
        finally
        {
            RenderTexture.active = prev;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
        }
    }
}
