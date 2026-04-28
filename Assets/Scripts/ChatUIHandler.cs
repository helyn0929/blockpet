using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;
using System.Text;

public class ChatUIHandler : MonoBehaviour
{
    /// <summary>
    /// Works even when the chat panel GameObject starts inactive: Unity does not run Awake/Start until activation,
    /// so Firebase can deliver messages first — lazy lookup includes inactive objects.
    /// </summary>
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

    const string ChatHistoryFileNamePrefix = "chat_history";

    [Header("UI 連結")]
    public TMP_InputField inputField; 
    public Button sendButton;
    public Transform chatContent;    // 拖入 Scroll View 裡的 Content
    public ScrollRect scrollRect;    // 拖入 Scroll View 本身，用於自動捲動

    [Header("Group chat header (new)")]
    [SerializeField] PageManager pageManager;
    [SerializeField] Button backButton;
    [SerializeField] TMP_Text roomNameText;
    [SerializeField] TMP_Text memberCountText;
    [Tooltip("Optional: header root button/area reserved for future room switching (no switching implemented).")]
    [SerializeField] Button roomSwitcherButton;
    [Tooltip("Shown once in Start() if room/member labels are assigned. Your game code can replace this anytime by calling SetRoomHeader (e.g. when joining a Firebase room).")]
    [SerializeField] string startupRoomDisplayName = "Chat";
    [Tooltip("Shown as \"N members\" when N > 0. Use SetRoomHeader from code when you have a live count.")]
    [SerializeField] int startupMemberCount;

    [Header("Reply UI (new)")]
    [SerializeField] GroupChatReplyPreview replyPreview;
    [Tooltip("Optional. Shown when no message is selected for reply (e.g. “Tap a message to reply”).")]
    [SerializeField] TMP_Text tapToReplyHintText;
    ChatMessage _activeReplyTarget;

    [Header("Modern full-screen chat (recommended hierarchy)")]
    [Tooltip("Uses layered layout: full-screen BG → semi-transparent message panel → Scroll View inside panel → header + input as overlays. Turn off to use legacy insets only.")]
    [SerializeField] bool useModernChatLayout;
    [Tooltip("Background → Message container → Header → Input (draw order).")]
    [SerializeField] bool enforceModernSiblingOrder = true;
    [SerializeField] RectTransform chatHeaderBar;
    [Tooltip("Used when header height cannot be read yet (e.g. first frame).")]
    [SerializeField] float referenceHeaderHeight = 56f;
    [Tooltip("Semi-transparent panel behind messages only (child: Scroll View). Assign Image + optional rounded 9-slice sprite.")]
    [SerializeField] Image messageContainerOverlay;
    [SerializeField] Color messageContainerTint = new Color(0.04f, 0.06f, 0.1f, 0.5f);
    [Tooltip("Only applied when Apply Rounded Sprite is enabled. Do not use the input-bar art (e.g. img_EnterMessageBox) here.")]
    [SerializeField] Sprite roundedMessageContainerSprite;
    [Tooltip("If off, Message Container keeps its Inspector sprite (or none) — avoids forcing e.g. img_EnterMessageBox on the panel.")]
    [SerializeField] bool applyRoundedMessageContainerSprite;
    [SerializeField] float messageContainerGapFromHeader = 10f;
    [SerializeField] float messageContainerGapFromInput = 10f;
    [SerializeField] float messageContainerHorizontalPadding = 16f;
    [SerializeField] float scrollInnerPadding = 12f;
    [Tooltip("If header/input RectTransforms are accidentally stretch-full-height, their rect.height equals the whole panel and the message band collapses. This caps each bar’s contribution.")]
    [SerializeField] bool clampBarHeightsToParent = true;
    [Tooltip("Header height and input-bar height each use at most this fraction of the panel height (e.g. 0.25 = 25%).")]
    [SerializeField] float maxBarHeightFractionOfParent = 0.28f;

    [Header("Scroll polish")]
    [SerializeField] bool smoothScrollToLatest = true;
    [SerializeField] float smoothScrollDuration = 0.15f;

    [Header("Chat room chrome (optional)")]
    [Tooltip("Assign sprite e.g. bg_chatRoom on this Image in the Inspector.")]
    [SerializeField] Image chatRoomBackground;
    [Tooltip("If true, stretches the background RectTransform to fill its parent (typical full-screen chat panel).")]
    [SerializeField] bool stretchBackgroundToParent = true;
    [Tooltip("If true, background uses the same RectTransform layout as the Scroll View (after insets). Use the same parent as the Scroll View. Disable Stretch Background To Parent when using this.")]
    [SerializeField] bool matchBackgroundToScrollView;
    [Tooltip("If true, moves the background to render under the Scroll View (lower sibling index). Same parent only.")]
    [SerializeField] bool placeBackgroundBehindScroll = true;
    [Tooltip("Root RectTransform for input field + send button row; pinned to bottom of parent when enabled below.")]
    [SerializeField] RectTransform enterMessageBar;
    [SerializeField] bool pinEnterMessageBarToBottom = true;
    [Tooltip("Adds bottom inset to the ScrollRect so messages scroll above the input bar.")]
    [SerializeField] bool resizeScrollAboveInputBar = true;

    [Header("Scroll area (bubbles only in this band)")]
    [Tooltip("ScrollRect should use stretch anchors on the same parent as your background. Insets shrink the scroll area from each edge.")]
    [SerializeField] bool applyScrollViewInsets = true;
    [Tooltip("Empty space at top of panel for close / title / other buttons (e.g. 100–160).")]
    [SerializeField] float scrollTopInset;
    [Tooltip("Extra empty space below the message list, after the input bar height (e.g. gap or room for bottom buttons).")]
    [SerializeField] float scrollBottomExtraInset;
    [Tooltip("Optional side margins for the bubble list.")]
    [SerializeField] float scrollLeftInset;
    [SerializeField] float scrollRightInset;

    [Header("預製物")]
    public GameObject messagePrefab; // 拖入妳做的文字泡泡 Prefab（根節點或子物件上有 Image 當泡泡底圖）

    [Header("Bubble sprites (9-slice / sliced)")]
    [Tooltip("Sprites should use Sprite Mode Single with borders set (3-grid); Image type will be Sliced.")]
    [SerializeField] Sprite chatBubbleSelf;
    [SerializeField] Sprite chatBubbleOther;
    [Tooltip("If set, applies sprites to this child by name instead of the first Image found.")]
    [SerializeField] string bubbleBackgroundImageName;

    [Header("Group chat avatars")]
    [Tooltip("Assign a sprite (e.g. from Art/UI/Chatroom_UI/avatar_test01 — set texture as Sprite 2D). Used when no per-user avatar is available.")]
    [SerializeField] Sprite defaultChatAvatarSprite;

    [Header("Bubble layout (Content Size Fitter)")]
    [Tooltip("Adds Content Size Fitter (Preferred) + LayoutElement on bubble root; sizes TMP to text with max width.")]
    [SerializeField] bool useBubbleAutoSize = true;
    [Tooltip("Maximum bubble width in canvas units; longer lines wrap.")]
    [SerializeField] float bubbleMaxWidth = 280f;
    [Tooltip("Inset of TMP text from bubble root (left/top used for anchored position).")]
    [SerializeField] Vector2 bubbleTextPadding = new Vector2(14f, 10f);
    [Tooltip("If chat Content has VerticalLayoutGroup, disable width stretch so bubbles stay narrow.")]
    [SerializeField] bool chatContentIntrinsicBubbleWidth = true;
    [Tooltip("Full-width row + flexible spacer so messages can sit on opposite sides of the thread.")]
    [SerializeField] bool alignBubblesBySenderSide = true;
    [Tooltip("When Align Bubbles By Sender Side is on: if true, yours on the right and others on the left; if false, yours on the left and others on the right.")]
    [SerializeField] bool mineMessagesOnRight = true;

    [Header("React WebView (primary UI)")]
    [Tooltip("When enabled with an assigned bridge, messages render in the WebView React UI instead of uGUI bubbles. Default on; disable only for legacy bubble debugging.")]
    [SerializeField] bool useWebViewUi = true;
    [SerializeField] ChatWebViewBridge webViewBridge;
    [Tooltip("Hidden while WebView UI is active (e.g. legacy header, message list, input bar).")]
    [SerializeField] GameObject[] legacyUiRootsToHideWhenWebView;
    [Header("Editor IME workaround")]
    [Tooltip("Unity Editor WebView text input often can't type Chinese IME reliably. When enabled, keep the native TMP composer visible (send still goes through Firebase) and ask the web UI to hide its composer.")]
    [SerializeField] bool keepNativeComposerInEditorForIme = true;

    string _headerRoomName;
    int _headerMemberCount;

    List<ChatMessage> localHistory = new List<ChatMessage>();
    string ChatHistoryPath => Path.Combine(Application.persistentDataPath, GetChatHistoryFileName());

    string GetChatHistoryFileName()
    {
        string room = FirebaseManager.Instance != null ? FirebaseManager.Instance.RoomId : "global";
        if (string.IsNullOrEmpty(room)) room = "global";
        // Keep file-system safe.
        room = room.Replace("/", "_").Replace("\\", "_").Replace("..", "_");
        return $"{ChatHistoryFileNamePrefix}_{room}.json";
    }
    Coroutine _smoothScrollRoutine;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning("[ChatUIHandler] Multiple ChatUIHandler instances; destroying duplicate.");
            Destroy(gameObject);
            return;
        }

        _instance = this;
        _headerRoomName = startupRoomDisplayName;
        _headerMemberCount = startupMemberCount;

    }

    bool IsWebViewActive()
    {
#if UNITY_EDITOR
        return false;
#else
        return useWebViewUi && webViewBridge != null;
#endif
    }

    /// <summary>Used by <see cref="ChatWebViewBridge"/> to skip native WebView setup when legacy UI is active.</summary>
    public bool IsWebViewChatEnabled()
    {
        return IsWebViewActive();
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
        if (_cachedAvatarSpriteFromTex != null)
        {
            Destroy(_cachedAvatarSpriteFromTex);
            _cachedAvatarSpriteFromTex = null;
            _cachedAvatarTex = null;
        }
    }

    void OnEnable()
    {
        if (useModernChatLayout)
            ApplyModernChatLayout();

        if (FirebaseManager.Instance != null)
            FirebaseManager.Instance.EnsureChatListening();

        if (FirebaseManager.Instance != null)
            SetRoomHeader($"Room: {FirebaseManager.Instance.RoomId}", _headerMemberCount);
    }

    void Start()
    {
        ApplyChatRoomLayout();

        // In some scenes these references might not be wired (or were wired to legacy objects that got replaced).
        // Auto-wire to ensure WebView mode can reliably hide legacy composer + bubble list.
        AutoWireLegacyUiReferencesIfMissing();

        if (IsWebViewActive() && legacyUiRootsToHideWhenWebView != null)
        {
            foreach (GameObject root in legacyUiRootsToHideWhenWebView)
            {
                if (root != null)
                {
                    if (ShouldKeepNativeComposerVisible() && (IsNativeComposerRoot(root) || IsNativeHeaderRoot(root)))
                        continue;
                    root.SetActive(false);
                }
            }
        }

        // Some scenes may not include the input bar root in legacyUiRootsToHideWhenWebView.
        // In WebView mode we never want the legacy composer visible (unless Editor IME workaround is enabled).
        if (IsWebViewActive() && !ShouldKeepNativeComposerVisible())
        {
            DisableAllLegacyComposersUnderThisPanel();
        }

        // In Editor IME workaround mode, allow the native Send button to send even while WebView renders the thread.
        if (sendButton != null && (!IsWebViewActive() || ShouldKeepNativeComposerVisible()))
            sendButton.onClick.AddListener(OnSendMessage);

        if (backButton != null)
            backButton.onClick.AddListener(OnBackClicked);

        if (roomNameText != null || memberCountText != null)
            SetRoomHeader(startupRoomDisplayName, startupMemberCount);

        LoadChatHistoryAndRebuildUI();

        if (useModernChatLayout)
        {
            // PageManager can toggle this panel off during the first frame; starting a coroutine on an inactive object throws.
            if (isActiveAndEnabled && gameObject.activeInHierarchy)
                StartCoroutine(ApplyModernLayoutAfterFirstFrame());
        }

        EnsureChatContentVerticalLayoutForBubbles();

        RefreshReplyUiHints();
    }

    void DisableAllLegacyComposersUnderThisPanel()
    {
        // If the legacy input bar remains visible in a scene, users might type into it and press Send,
        // but in WebView mode we intentionally route sending through the web UI.
        // So we hard-disable all TMP input fields under this chat panel.
        TMP_InputField[] fields = GetComponentsInChildren<TMP_InputField>(true);
        foreach (var f in fields)
        {
            if (f == null) continue;
            // Disable the whole input root (usually InputField(TMP)).
            f.gameObject.SetActive(false);
        }

        // Additionally, hide the known bar root if it's wired.
        if (enterMessageBar != null)
            enterMessageBar.gameObject.SetActive(false);
        if (inputField != null)
            inputField.gameObject.SetActive(false);
        if (sendButton != null)
            sendButton.gameObject.SetActive(false);
    }

    void AutoWireLegacyUiReferencesIfMissing()
    {
        // Only needed for hiding legacy UI; safe in both modes.
        if (inputField == null)
            inputField = GetComponentInChildren<TMP_InputField>(true);

        if (sendButton == null)
        {
            // Prefer a sibling button near the input field if possible.
            if (inputField != null && inputField.transform.parent != null)
                sendButton = inputField.transform.parent.GetComponentInChildren<Button>(true);
            if (sendButton == null)
                sendButton = GetComponentInChildren<Button>(true);
        }

        if (enterMessageBar == null && inputField != null)
        {
            // Typical hierarchy: InputField(TMP) is the root bar, or it sits under the bar container.
            enterMessageBar = inputField.transform as RectTransform;
            if (enterMessageBar != null && enterMessageBar.parent is RectTransform prt)
            {
                // If the input field is nested (e.g. Text Area), promote to the bar root.
                if (enterMessageBar.name.Contains("Text Area", StringComparison.OrdinalIgnoreCase) ||
                    enterMessageBar.name.Contains("Placeholder", StringComparison.OrdinalIgnoreCase) ||
                    enterMessageBar.name.Contains("Text", StringComparison.OrdinalIgnoreCase))
                {
                    enterMessageBar = prt;
                }
            }
        }
    }

    void OnBackClicked()
    {
        if (pageManager == null)
            pageManager = FindObjectOfType<PageManager>(true);
        if (pageManager != null)
            pageManager.ShowHomePage();
    }

    /// <summary>
    /// Updates the chat screen title and member subtitle. Call from your lobby / party / Firebase flow when the player enters a room.
    /// Example: <c>ChatUIHandler.Instance?.SetRoomHeader(teamName, onlineCount);</c>
    /// </summary>
    public void SetRoomHeader(string roomName, int memberCount)
    {
        _headerRoomName = roomName ?? string.Empty;
        _headerMemberCount = memberCount;

        if (roomNameText != null)
            roomNameText.text = roomName ?? string.Empty;
        if (memberCountText != null)
            memberCountText.text = memberCount <= 0 ? string.Empty : $"{memberCount} members";

        if (IsWebViewActive())
            webViewBridge.NotifyHeader(_headerRoomName, _headerMemberCount);
    }

    /// <summary>Called when the WebView page finishes loading so it can receive the current thread + header.</summary>
    public void PushFullStateToWebView()
    {
        if (!IsWebViewActive())
            return;
        webViewBridge.RequestFullSync(
            localHistory,
            _headerRoomName,
            _headerMemberCount,
            GetLocalSenderDisplayName(),
            mineMessagesOnRight,
            GetHeaderAnimalImageBase64Png(),
            ShouldKeepNativeComposerVisible());
    }

    /// <summary>Used by <see cref="ChatWebViewBridge"/> to avoid covering the native input bar when Editor IME workaround is active.</summary>
    public RectTransform GetNativeComposerRectTransform()
    {
        return enterMessageBar;
    }

    bool ShouldKeepNativeComposerVisible()
    {
#if UNITY_EDITOR
        return keepNativeComposerInEditorForIme;
#else
        return false;
#endif
    }

    bool IsNativeComposerRoot(GameObject go)
    {
        if (go == null) return false;
        Transform rt = go.transform;
        if (enterMessageBar != null && (go == enterMessageBar.gameObject || enterMessageBar.IsChildOf(rt))) return true;
        if (inputField != null && (go == inputField.gameObject || inputField.transform.IsChildOf(rt))) return true;
        if (sendButton != null && (go == sendButton.gameObject || sendButton.transform.IsChildOf(rt))) return true;
        return false;
    }

    bool IsNativeHeaderRoot(GameObject go)
    {
        if (go == null) return false;
        if (chatHeaderBar == null) return false;
        Transform rt = go.transform;
        return go == chatHeaderBar.gameObject || chatHeaderBar.IsChildOf(rt);
    }

    void EnsureActiveHierarchy(Transform t)
    {
        if (t == null) return;

        // If any parent was disabled (e.g. a legacy root got hidden), the TMP input can't receive focus.
        // Walk up until this ChatUIHandler root and re-enable everything on the path.
        Transform stop = transform;
        Transform cur = t;
        while (cur != null)
        {
            cur.gameObject.SetActive(true);
            if (cur == stop)
                break;
            cur = cur.parent;
        }
    }

    string GetHeaderAnimalImageBase64Png()
    {
        try
        {
            Texture2D tex = AvatarManager.Instance != null ? AvatarManager.Instance.CurrentAvatar : null;
            if (tex == null)
                return null;

            byte[] png = null;
            try
            {
                png = tex.EncodeToPNG();
            }
            catch
            {
                png = EncodeToPngViaReadback(tex);
            }

            if (png == null || png.Length == 0)
                return null;

            return Convert.ToBase64String(png);
        }
        catch
        {
            return null;
        }
    }

    static byte[] EncodeToPngViaReadback(Texture2D tex)
    {
        if (tex == null) return null;
        RenderTexture rt = null;
        RenderTexture prev = RenderTexture.active;
        try
        {
            rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            Texture2D copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply(false, false);
            byte[] png = copy.EncodeToPNG();
            Destroy(copy);
            return png;
        }
        catch
        {
            return null;
        }
        finally
        {
            RenderTexture.active = prev;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
        }
    }

    /// <summary>Invoked from <see cref="ChatWebViewBridge"/> when the user sends from the React composer.</summary>
    public void SendFromWebView(string text, string replyToMessageId, string replyToDisplayName, string replyToMessagePreview)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (FirebaseManager.Instance == null)
        {
            Debug.LogError("找不到 FirebaseManager 物件！");
            return;
        }

        ChatMessage outgoing = new ChatMessage(GetLocalSenderDisplayName(), text.Trim());
        if (!string.IsNullOrEmpty(replyToMessageId))
        {
            outgoing.replyToMessageId = replyToMessageId;
            outgoing.replyToDisplayName = replyToDisplayName ?? string.Empty;
            outgoing.replyToMessagePreview = replyToMessagePreview ?? string.Empty;
        }

        FirebaseManager.Instance.SendChatMessage(outgoing);
        ClearReply();
    }

    public void WebViewRequestBack()
    {
        OnBackClicked();
    }

    /// <summary>Stops Firebase chat listener, clears reply state, then navigates home (WebView <c>leaveChat</c>).</summary>
    public void WebViewRequestLeaveChat()
    {
        ClearReply();
        if (FirebaseManager.Instance != null)
            FirebaseManager.Instance.StopChatListening();
        OnBackClicked();
    }

    /// <summary>Syncs WebView reply selection to the same reply target used for TMP send (WebView <c>replySelect</c>). Empty id clears reply.</summary>
    public void WebViewSelectReply(string messageId, string userName, string displayName, string messageBody)
    {
        if (string.IsNullOrEmpty(messageId))
        {
            ClearReply();
            return;
        }

        ChatMessage match = null;
        for (int i = localHistory.Count - 1; i >= 0; i--)
        {
            ChatMessage m = localHistory[i];
            if (!string.IsNullOrEmpty(m.messageId) && m.messageId == messageId)
            {
                match = m;
                break;
            }
        }

        if (match != null)
            SetReply(match);
        else
        {
            var synthetic = new ChatMessage
            {
                messageId = messageId,
                userName = userName ?? string.Empty,
                displayName = string.IsNullOrEmpty(displayName) ? (userName ?? string.Empty) : displayName,
                message = messageBody ?? string.Empty,
                timestamp = 0
            };
            SetReply(synthetic);
        }
    }

    public void WebViewRequestOpenAlbum()
    {
        if (pageManager == null)
            pageManager = FindObjectOfType<PageManager>(true);
        if (pageManager != null)
            pageManager.ShowAlbumPage();
    }

    public void WebViewSetRoom(string roomId)
    {
        if (FirebaseManager.Instance == null)
            return;

        FirebaseManager.Instance.SetRoomId(roomId);
        SetRoomHeader($"Room: {FirebaseManager.Instance.RoomId}", 0);

        // Reload per-room local history (and push fresh init to web UI).
        LoadChatHistoryAndRebuildUI();
    }

    public void WebViewClearReply()
    {
        ClearReply();
    }

    IEnumerator ApplyModernLayoutAfterFirstFrame()
    {
        yield return null;
        ApplyModernChatLayout();
    }

    void ApplyChatRoomLayout()
    {
        if (useModernChatLayout)
        {
            ApplyModernChatLayout();
            return;
        }

        if (chatRoomBackground != null && stretchBackgroundToParent && !matchBackgroundToScrollView)
        {
            RectTransform brt = chatRoomBackground.rectTransform;
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;
        }

        float inputBarHeight = 0f;
        if (enterMessageBar != null && pinEnterMessageBarToBottom)
        {
            inputBarHeight = enterMessageBar.sizeDelta.y;
            if (inputBarHeight < 2f)
                inputBarHeight = enterMessageBar.rect.height;
            if (inputBarHeight < 2f)
                inputBarHeight = 120f;

            enterMessageBar.anchorMin = new Vector2(0f, 0f);
            enterMessageBar.anchorMax = new Vector2(1f, 0f);
            enterMessageBar.pivot = new Vector2(0.5f, 0f);
            enterMessageBar.sizeDelta = new Vector2(0f, inputBarHeight);
            enterMessageBar.anchoredPosition = Vector2.zero;
        }

        ApplyScrollViewAreaInsets(inputBarHeight);

        if (chatRoomBackground != null && matchBackgroundToScrollView && scrollRect != null)
            MatchBackgroundRectToScrollView();
    }

    void TryAutoWireModernLayoutReferences()
    {
        if (!useModernChatLayout)
            return;

        if (messageContainerOverlay == null)
        {
            Transform t = transform.Find("MessageContainer");
            if (t != null)
                messageContainerOverlay = t.GetComponent<Image>();
        }

        if (chatRoomBackground == null)
        {
            Transform t = transform.Find("BackGround");
            if (t == null)
                t = transform.Find("Background");
            if (t != null)
                chatRoomBackground = t.GetComponent<Image>();
        }

        if (chatHeaderBar == null)
        {
            Transform t = transform.Find("Header");
            if (t != null)
                chatHeaderBar = t as RectTransform;
        }

        if (enterMessageBar == null && inputField != null)
            enterMessageBar = inputField.transform as RectTransform;
    }

    /// <summary>
    /// Default Unity UI creates a small centered box; expand to fill the chat panel before applying insets.
    /// </summary>
    void EnsureMessageContainerStretchesPanel(RectTransform mrt)
    {
        if (mrt == null || mrt.parent is not RectTransform parentRt)
            return;

        float pw = Mathf.Max(10f, parentRt.rect.width);
        float ph = Mathf.Max(10f, parentRt.rect.height);
        bool centered = Mathf.Approximately(mrt.anchorMin.x, 0.5f) && Mathf.Approximately(mrt.anchorMax.x, 0.5f);
        bool tiny = mrt.rect.width < pw * 0.65f || mrt.rect.height < ph * 0.3f;
        if (!centered && !tiny)
            return;

        mrt.anchorMin = Vector2.zero;
        mrt.anchorMax = Vector2.one;
        mrt.pivot = new Vector2(0.5f, 0.5f);
        mrt.offsetMin = Vector2.zero;
        mrt.offsetMax = Vector2.zero;
    }

    void EnsureFullStretchIfUnderSized(RectTransform rt)
    {
        if (rt == null || rt.parent is not RectTransform parentRt)
            return;

        float pw = Mathf.Max(10f, parentRt.rect.width);
        float ph = Mathf.Max(10f, parentRt.rect.height);
        bool centered = Mathf.Approximately(rt.anchorMin.x, 0.5f) && Mathf.Approximately(rt.anchorMax.x, 0.5f);
        bool small = rt.rect.width < pw * 0.92f || rt.rect.height < ph * 0.92f;
        if (!centered && !small)
            return;

        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// Hierarchy: ChatPanel → Background (full) → MessageContainer (Image, translucent) → ScrollView → Header + InputBar (siblings, overlays).
    /// </summary>
    void ApplyModernChatLayout()
    {
        TryAutoWireModernLayoutReferences();

        if (chatRoomBackground != null)
        {
            RectTransform brt = chatRoomBackground.rectTransform;
            EnsureFullStretchIfUnderSized(brt);
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;
        }

        float headerH = GetBarHeight(chatHeaderBar, referenceHeaderHeight);
        float inputH = GetBarHeight(enterMessageBar, 120f);

        if (chatHeaderBar != null)
        {
            chatHeaderBar.anchorMin = new Vector2(0f, 1f);
            chatHeaderBar.anchorMax = new Vector2(1f, 1f);
            chatHeaderBar.pivot = new Vector2(0.5f, 1f);
            if (chatHeaderBar.sizeDelta.y < 2f && chatHeaderBar.rect.height < 2f)
                chatHeaderBar.sizeDelta = new Vector2(0f, referenceHeaderHeight);
            chatHeaderBar.anchoredPosition = Vector2.zero;
        }

        if (enterMessageBar != null)
        {
            enterMessageBar.anchorMin = new Vector2(0f, 0f);
            enterMessageBar.anchorMax = new Vector2(1f, 0f);
            enterMessageBar.pivot = new Vector2(0.5f, 0f);
            if (enterMessageBar.sizeDelta.y < 2f && enterMessageBar.rect.height < 2f)
                enterMessageBar.sizeDelta = new Vector2(0f, 120f);
            enterMessageBar.anchoredPosition = Vector2.zero;
            inputH = GetBarHeight(enterMessageBar, 120f);
        }

        headerH = GetBarHeight(chatHeaderBar, referenceHeaderHeight);

        float panelH = ResolveChatPanelContentHeight();
        if (clampBarHeightsToParent && panelH > 50f)
        {
            float cap = Mathf.Max(referenceHeaderHeight, panelH * maxBarHeightFractionOfParent);
            headerH = Mathf.Min(headerH, cap);
            inputH = Mathf.Min(inputH, cap);
        }

        if (messageContainerOverlay != null)
        {
            RectTransform mrt = messageContainerOverlay.rectTransform;
            EnsureMessageContainerStretchesPanel(mrt);
            mrt.anchorMin = Vector2.zero;
            mrt.anchorMax = Vector2.one;
            float top = headerH + messageContainerGapFromHeader;
            float bottom = inputH + messageContainerGapFromInput;
            float minMiddle = panelH > 50f ? panelH * 0.2f : 120f;
            if (top + bottom > panelH - minMiddle)
            {
                float excess = top + bottom - (panelH - minMiddle);
                float shrink = excess * 0.5f;
                top = Mathf.Max(messageContainerGapFromHeader, top - shrink);
                bottom = Mathf.Max(messageContainerGapFromInput, bottom - shrink);
            }

            mrt.offsetMin = new Vector2(messageContainerHorizontalPadding, bottom);
            mrt.offsetMax = new Vector2(-messageContainerHorizontalPadding, -top);

            messageContainerOverlay.color = messageContainerTint;
            messageContainerOverlay.raycastTarget = true;
            if (applyRoundedMessageContainerSprite && roundedMessageContainerSprite != null)
            {
                messageContainerOverlay.sprite = roundedMessageContainerSprite;
                messageContainerOverlay.type = Image.Type.Sliced;
            }
        }

        if (scrollRect != null)
        {
            RectTransform srt = scrollRect.transform as RectTransform;
            if (srt != null && srt.parent is RectTransform scrollParent)
            {
                EnsureMessageContainerStretchesPanel(scrollParent);
                bool scrollUnderOverlay = messageContainerOverlay != null &&
                                          scrollParent == messageContainerOverlay.rectTransform;
                if (messageContainerOverlay == null || scrollUnderOverlay)
                {
                    srt.anchorMin = Vector2.zero;
                    srt.anchorMax = Vector2.one;
                    srt.offsetMin = new Vector2(scrollInnerPadding, scrollInnerPadding);
                    srt.offsetMax = new Vector2(-scrollInnerPadding, -scrollInnerPadding);
                }
            }
        }

        if (enforceModernSiblingOrder)
            EnsureModernSiblingOrder();

        Canvas.ForceUpdateCanvases();
    }

    /// <summary>Height of the chat panel area used to cap header/input insets (message container’s parent).</summary>
    float ResolveChatPanelContentHeight()
    {
        RectTransform parent =
            messageContainerOverlay != null ? messageContainerOverlay.transform.parent as RectTransform :
            chatRoomBackground != null ? chatRoomBackground.transform.parent as RectTransform :
            chatHeaderBar != null ? chatHeaderBar.parent as RectTransform :
            enterMessageBar != null ? enterMessageBar.parent as RectTransform :
            null;
        if (parent != null && parent.rect.height > 10f)
            return parent.rect.height;
        return Mathf.Max(400f, Screen.height);
    }

    /// <summary>
    /// Prefer fixed top/bottom bar height from sizeDelta. If the bar is vertically stretch-anchored across the full panel,
    /// rect.height is misleading — use fallback instead.
    /// </summary>
    static float GetBarHeight(RectTransform rt, float fallback)
    {
        if (rt == null)
            return fallback;

        bool verticallyStretched = Mathf.Abs(rt.anchorMax.y - rt.anchorMin.y) > 0.01f;
        if (verticallyStretched)
        {
            if (rt.sizeDelta.y > 2f)
                return rt.sizeDelta.y;
            return fallback;
        }

        float sd = rt.sizeDelta.y;
        if (sd > 2f)
            return sd;

        float rh = rt.rect.height;
        if (rh > 2f)
            return rh;

        return fallback;
    }

    void EnsureModernSiblingOrder()
    {
        Transform root =
            chatRoomBackground != null ? chatRoomBackground.transform.parent :
            messageContainerOverlay != null ? messageContainerOverlay.transform.parent :
            chatHeaderBar != null ? chatHeaderBar.parent :
            enterMessageBar != null ? enterMessageBar.parent :
            null;
        if (root == null)
            return;

        int i = 0;
        void Place(Transform t)
        {
            if (t == null || t.parent != root)
                return;
            t.SetSiblingIndex(i++);
        }

        Place(chatRoomBackground != null ? chatRoomBackground.transform : null);
        Place(messageContainerOverlay != null ? messageContainerOverlay.transform : null);
        Place(chatHeaderBar);
        Place(enterMessageBar);
    }

    void MatchBackgroundRectToScrollView()
    {
        RectTransform scrollRt = scrollRect.transform as RectTransform;
        RectTransform brt = chatRoomBackground.rectTransform;
        if (scrollRt == null || brt == null)
            return;

        if (brt.parent != scrollRt.parent)
        {
            Debug.LogWarning("[ChatUIHandler] matchBackgroundToScrollView: assign the same parent for Chat Room Background and Scroll View so sizes match.");
            return;
        }

        CopyRectTransformLayout(scrollRt, brt);

        if (placeBackgroundBehindScroll && brt.GetSiblingIndex() > scrollRt.GetSiblingIndex())
            brt.SetSiblingIndex(scrollRt.GetSiblingIndex());
    }

    static void CopyRectTransformLayout(RectTransform from, RectTransform to)
    {
        to.anchorMin = from.anchorMin;
        to.anchorMax = from.anchorMax;
        to.pivot = from.pivot;
        to.anchoredPosition = from.anchoredPosition;
        to.sizeDelta = from.sizeDelta;
        to.offsetMin = from.offsetMin;
        to.offsetMax = from.offsetMax;
        to.localScale = from.localScale;
        to.localRotation = from.localRotation;
    }

    /// <summary>
    /// Keeps chat bubbles inside a rectangle inset from the panel edges so top/bottom (and sides) stay clear for other UI.
    /// Expects the ScrollRect's RectTransform to be parented under the same full-rect panel with stretch anchors.
    /// </summary>
    void ApplyScrollViewAreaInsets(float pinnedInputBarHeight)
    {
        if (!applyScrollViewInsets || scrollRect == null)
            return;

        RectTransform scrollRt = scrollRect.transform as RectTransform;
        if (scrollRt == null)
            return;

        float bottom = scrollBottomExtraInset;
        if (resizeScrollAboveInputBar)
        {
            if (pinnedInputBarHeight > 0f)
                bottom += pinnedInputBarHeight;
            bottom += GetActiveReplyPreviewHeight();
        }

        float top = Mathf.Max(0f, scrollTopInset);
        float left = Mathf.Max(0f, scrollLeftInset);
        float right = Mathf.Max(0f, scrollRightInset);

        scrollRt.offsetMin = new Vector2(left, bottom);
        scrollRt.offsetMax = new Vector2(-right, -top);
    }

    float GetActiveReplyPreviewHeight()
    {
        if (replyPreview == null) return 0f;
        if (!replyPreview.isActiveAndEnabled) return 0f;
        RectTransform rt = replyPreview.transform as RectTransform;
        if (rt == null) return 0f;
        float h = rt.rect.height;
        if (h < 1f) h = rt.sizeDelta.y;
        return Mathf.Max(0f, h);
    }

    string GetLocalSenderDisplayName()
    {
        if (FirebaseManager.Instance != null)
            return FirebaseManager.Instance.GetDisplayName();
        return "Guest";
    }

    bool IsMessageFromLocalUser(ChatMessage msg)
    {
        if (msg == null) return false;
        return msg.userName == GetLocalSenderDisplayName();
    }

    void OnSendMessage()
    {
        if (inputField == null) return;
        if (string.IsNullOrEmpty(inputField.text)) return;

        if (FirebaseManager.Instance != null)
        {
            ChatMessage outgoing = new ChatMessage(GetLocalSenderDisplayName(), inputField.text);
            if (_activeReplyTarget != null)
            {
                outgoing.replyToMessageId = _activeReplyTarget.messageId;
                outgoing.replyToDisplayName = ChatMessage.GetBestDisplayName(_activeReplyTarget);
                outgoing.replyToMessagePreview = MakeReplyPreviewSnippet(_activeReplyTarget.message);
            }

            FirebaseManager.Instance.SendChatMessage(outgoing);
            inputField.text = ""; 
            ClearReply();
        }
        else
        {
            Debug.LogError("找不到 FirebaseManager 物件！");
        }
    }

    static string MakeReplyPreviewSnippet(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        s = s.Replace("\n", " ").Replace("\r", " ");
        const int max = 90;
        if (s.Length <= max) return s;
        return s.Substring(0, max).TrimEnd() + "…";
    }

    void SetReply(ChatMessage target)
    {
        _activeReplyTarget = target;
        if (replyPreview != null && target != null)
            replyPreview.Set(ChatMessage.GetBestDisplayName(target), MakeReplyPreviewSnippet(target.message), ClearReply);
        RefreshReplyUiHints();
    }

    void ClearReply()
    {
        _activeReplyTarget = null;
        if (replyPreview != null)
            replyPreview.Clear();
        RefreshReplyUiHints();
        if (IsWebViewActive() && webViewBridge != null)
            webViewBridge.NotifyReplyCleared();
    }

    void RefreshReplyUiHints()
    {
        if (tapToReplyHintText == null)
            return;
        bool pickingReply = _activeReplyTarget != null;
        tapToReplyHintText.gameObject.SetActive(!pickingReply);
        if (!pickingReply && string.IsNullOrEmpty(tapToReplyHintText.text))
            tapToReplyHintText.text = "Tap a message to reply";
    }

    /// <summary>Call after rotation or safe-area changes if layout does not update automatically.</summary>
    public void RefreshChatLayout()
    {
        ApplyChatRoomLayout();
    }

    // 由 FirebaseManager 監聽到新訊息時呼叫
    public void DisplayMessage(ChatMessage msg)
    {
        if (!IsWebViewActive() && (messagePrefab == null || chatContent == null))
            return;
        if (IsWebViewActive() && webViewBridge == null)
            return;
        // Avoid duplicates when Firebase replays history or reconnects.
        if (!string.IsNullOrEmpty(msg.messageId))
        {
            if (localHistory.Exists(m => !string.IsNullOrEmpty(m.messageId) && m.messageId == msg.messageId))
                return;
        }
        else
        {
            if (localHistory.Exists(m => m.userName == msg.userName && m.message == msg.message && m.timestamp == msg.timestamp))
                return;
        }

        localHistory.Add(msg);
        SaveChatHistoryToFile();

        if (IsWebViewActive())
            webViewBridge.NotifyMessageAppended(msg);
        else
            AddMessageBubbleToUI(msg);

        ScrollToLatest();
    }

    void AddMessageBubbleToUI(ChatMessage msg)
    {
        if (IsWebViewActive())
            return;
        // May run before Start() if messages arrive while panel is still inactive.
        EnsureChatContentVerticalLayoutForBubbles();

        bool isSelf = IsMessageFromLocalUser(msg);
        GameObject row = null;
        if (alignBubblesBySenderSide)
        {
            row = CreateChatBubbleRowShell();
            bool spacerFirst = mineMessagesOnRight ? isSelf : !isSelf;
            if (spacerFirst)
                AddChatRowFlexSpacer(row.transform);
        }

        Transform bubbleParent = row != null ? row.transform : chatContent;
        GameObject newMsg = Instantiate(messagePrefab, bubbleParent);
        if (row != null)
        {
            bool spacerAfter = mineMessagesOnRight ? !isSelf : isSelf;
            if (spacerAfter)
                AddChatRowFlexSpacer(row.transform);
        }

        // New reusable prefab path (avatar + name + reply quote + bubble)
        GroupChatMessageView view = newMsg.GetComponentInChildren<GroupChatMessageView>(true);
        if (view != null)
        {
            Sprite avatar = ResolveAvatarSprite(msg);
            view.Bind(msg, avatar, isSelf);

            // Apply bubble sprites if configured
            Image bg = view.BubbleBackground;
            if (bg != null)
            {
                Sprite pick = isSelf ? chatBubbleSelf : chatBubbleOther;
                if (pick != null)
                {
                    bg.sprite = pick;
                    bg.type = Image.Type.Sliced;
                }
            }

            // Button makes taps reliable inside ScrollRect (IPointerClick alone is often swallowed).
            if (bg != null)
            {
                bg.raycastTarget = true;
                Button bubbleButton = view.GetComponent<Button>();
                if (bubbleButton == null)
                    bubbleButton = view.gameObject.AddComponent<Button>();
                bubbleButton.transition = Selectable.Transition.None;
                bubbleButton.navigation = new Navigation { mode = Navigation.Mode.None };
                bubbleButton.targetGraphic = bg;
                ChatMessage captured = msg;
                bubbleButton.onClick.RemoveAllListeners();
                bubbleButton.onClick.AddListener(() => OnMessageClickedForReply(captured));
            }

            // Let prefab handle its own layout; do not force TMP injection below.
            return;
        }

        Image bubbleImage = null;
        if (!string.IsNullOrEmpty(bubbleBackgroundImageName))
        {
            Transform t = newMsg.transform.Find(bubbleBackgroundImageName);
            if (t != null)
                bubbleImage = t.GetComponent<Image>();
        }
        if (bubbleImage == null)
            bubbleImage = newMsg.GetComponent<Image>();
        if (bubbleImage == null)
            bubbleImage = newMsg.GetComponentInChildren<Image>(true);

        if (bubbleImage != null)
        {
            Sprite pick = isSelf ? chatBubbleSelf : chatBubbleOther;
            if (pick != null)
            {
                bubbleImage.sprite = pick;
                bubbleImage.type = Image.Type.Sliced;
            }
        }

        TMP_Text textComponent = newMsg.GetComponentInChildren<TMP_Text>();
        if (textComponent != null)
            textComponent.text = isSelf ? msg.message : $"{msg.userName}: {msg.message}";

        ApplyBubbleAutoLayout(newMsg, textComponent);
    }

    void OnMessageClickedForReply(ChatMessage msg)
    {
        if (msg == null) return;
        if (IsWebViewActive())
            return;
        // Don’t allow replying to money/system empty messages if you add them later.
        SetReply(msg);
    }

    Texture2D _cachedAvatarTex;
    Sprite _cachedAvatarSpriteFromTex;

    Sprite ResolveAvatarSprite(ChatMessage msg)
    {
        if (IsMessageFromLocalUser(msg) && AvatarManager.Instance != null && AvatarManager.Instance.CurrentAvatar != null)
        {
            Texture2D tex = AvatarManager.Instance.CurrentAvatar;
            if (tex != _cachedAvatarTex || _cachedAvatarSpriteFromTex == null)
            {
                if (_cachedAvatarSpriteFromTex != null)
                    Destroy(_cachedAvatarSpriteFromTex);
                _cachedAvatarTex = tex;
                try
                {
                    _cachedAvatarSpriteFromTex = Sprite.Create(
                        tex,
                        new Rect(0f, 0f, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f),
                        100f);
                }
                catch
                {
                    _cachedAvatarSpriteFromTex = null;
                }
            }
            if (_cachedAvatarSpriteFromTex != null)
                return _cachedAvatarSpriteFromTex;
        }

        return defaultChatAvatarSprite;
    }

    void EnsureChatContentVerticalLayoutForBubbles()
    {
        if (!chatContentIntrinsicBubbleWidth || chatContent == null)
            return;

        VerticalLayoutGroup v = chatContent.GetComponent<VerticalLayoutGroup>();
        if (v == null)
            return;

        v.childControlWidth = false;
        v.childForceExpandWidth = false;
        v.childControlHeight = false;
        v.childForceExpandHeight = false;
    }

    GameObject CreateChatBubbleRowShell()
    {
        GameObject row = new GameObject("ChatBubbleRow", typeof(RectTransform));
        RectTransform rt = row.GetComponent<RectTransform>();
        rt.SetParent(chatContent, false);
        rt.localScale = Vector3.one;

        HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6f;
        h.padding = new RectOffset(6, 6, 4, 4);
        h.childAlignment = TextAnchor.UpperLeft;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;

        LayoutElement rowLe = row.AddComponent<LayoutElement>();
        rowLe.flexibleWidth = 1f;

        ContentSizeFitter rowFit = row.AddComponent<ContentSizeFitter>();
        rowFit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        rowFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return row;
    }

    static void AddChatRowFlexSpacer(Transform row)
    {
        GameObject sp = new GameObject("FlexSpacer", typeof(RectTransform));
        sp.transform.SetParent(row, false);
        LayoutElement le = sp.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.minWidth = 0f;
    }

    void ApplyBubbleAutoLayout(GameObject bubbleRoot, TMP_Text tmp)
    {
        if (!useBubbleAutoSize || bubbleRoot == null || tmp == null)
            return;

        RectTransform rootRt = bubbleRoot.transform as RectTransform;
        if (rootRt == null)
            return;

        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Overflow;

        RectTransform textRt = tmp.rectTransform;
        textRt.anchorMin = new Vector2(0f, 1f);
        textRt.anchorMax = new Vector2(0f, 1f);
        textRt.pivot = new Vector2(0f, 1f);
        textRt.anchoredPosition = new Vector2(bubbleTextPadding.x, -bubbleTextPadding.y);

        float innerMax = Mathf.Max(40f, bubbleMaxWidth - bubbleTextPadding.x * 2f);
        string s = tmp.text ?? string.Empty;

        tmp.enableWordWrapping = false;
        tmp.ForceMeshUpdate(true);
        float unwrappedW = Mathf.Max(tmp.GetPreferredValues(s).x, 8f);
        tmp.enableWordWrapping = true;

        float lineWidth = Mathf.Min(unwrappedW, innerMax);

        textRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, lineWidth);
        tmp.ForceMeshUpdate(true);
        float bodyH = tmp.preferredHeight + bubbleTextPadding.y;
        textRt.sizeDelta = new Vector2(lineWidth, Mathf.Max(bodyH, 18f));

        rootRt.anchorMin = new Vector2(0f, 1f);
        rootRt.anchorMax = new Vector2(0f, 1f);
        rootRt.pivot = new Vector2(0f, 1f);

        ContentSizeFitter fit = bubbleRoot.GetComponent<ContentSizeFitter>();
        if (fit == null)
            fit = bubbleRoot.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        LayoutElement le = bubbleRoot.GetComponent<LayoutElement>();
        if (le == null)
            le = bubbleRoot.AddComponent<LayoutElement>();
        le.flexibleWidth = 0f;
        le.flexibleHeight = 0f;

        LayoutRebuilder.ForceRebuildLayoutImmediate(rootRt);
    }

    void SaveChatHistoryToFile()
    {
        var wrapper = new ChatHistorySave { messages = localHistory };
        string json = JsonUtility.ToJson(wrapper, true);
        try
        {
            File.WriteAllText(ChatHistoryPath, json);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[ChatUIHandler] Save chat history failed: " + e.Message);
        }
    }

    void LoadChatHistoryAndRebuildUI()
    {
        if (!IsWebViewActive() && (chatContent == null || messagePrefab == null))
            return;
        if (IsWebViewActive() && webViewBridge == null)
            return;

        localHistory.Clear();
        if (File.Exists(ChatHistoryPath))
        {
            try
            {
                string json = File.ReadAllText(ChatHistoryPath);
                var wrapper = JsonUtility.FromJson<ChatHistorySave>(json);
                if (wrapper != null && wrapper.messages != null)
                    localHistory = wrapper.messages;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[ChatUIHandler] Load chat history failed: " + e.Message);
            }
        }
#if UNITY_EDITOR
        Debug.Log("[ChatUIHandler] Local history messages: " + (localHistory != null ? localHistory.Count : 0) + " (path: " + ChatHistoryPath + ")");
#endif

        if (!IsWebViewActive() && chatContent != null)
        {
            // 清除現有泡泡
            for (int i = chatContent.childCount - 1; i >= 0; i--)
                Destroy(chatContent.GetChild(i).gameObject);

            // 依序重建訊息泡泡，保留歷史
            foreach (ChatMessage msg in localHistory)
                AddMessageBubbleToUI(msg);
        }

        if (IsWebViewActive())
            PushFullStateToWebView();
        else
            ScrollToLatest();
    }

    void ScrollToLatest()
    {
        if (scrollRect == null)
            return;

        Canvas.ForceUpdateCanvases();

        if (!smoothScrollToLatest || !isActiveAndEnabled)
        {
            scrollRect.verticalNormalizedPosition = 0f;
            return;
        }

        if (_smoothScrollRoutine != null)
            StopCoroutine(_smoothScrollRoutine);
        _smoothScrollRoutine = StartCoroutine(SmoothScrollToBottom());
    }

    IEnumerator SmoothScrollToBottom()
    {
        float start = scrollRect.verticalNormalizedPosition;
        float t = 0f;
        float dur = Mathf.Max(0.02f, smoothScrollDuration);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            k = k * k * (3f - 2f * k);
            scrollRect.verticalNormalizedPosition = Mathf.Lerp(start, 0f, k);
            yield return null;
        }

        scrollRect.verticalNormalizedPosition = 0f;
        _smoothScrollRoutine = null;
    }
}