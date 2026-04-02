using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.IO;

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

    const string ChatHistoryFileName = "chat_history.json";

    [Header("UI 連結")]
    public TMP_InputField inputField; 
    public Button sendButton;
    public Transform chatContent;    // 拖入 Scroll View 裡的 Content
    public ScrollRect scrollRect;    // 拖入 Scroll View 本身，用於自動捲動

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

    [Header("Bubble layout (Content Size Fitter)")]
    [Tooltip("Adds Content Size Fitter (Preferred) + LayoutElement on bubble root; sizes TMP to text with max width.")]
    [SerializeField] bool useBubbleAutoSize = true;
    [Tooltip("Maximum bubble width in canvas units; longer lines wrap.")]
    [SerializeField] float bubbleMaxWidth = 280f;
    [Tooltip("Inset of TMP text from bubble root (left/top used for anchored position).")]
    [SerializeField] Vector2 bubbleTextPadding = new Vector2(14f, 10f);
    [Tooltip("If chat Content has VerticalLayoutGroup, disable width stretch so bubbles stay narrow.")]
    [SerializeField] bool chatContentIntrinsicBubbleWidth = true;
    [Tooltip("Others’ bubbles on the left, yours on the right (full-width row + flexible spacer).")]
    [SerializeField] bool alignBubblesBySenderSide = true;

    List<ChatMessage> localHistory = new List<ChatMessage>();
    string ChatHistoryPath => Path.Combine(Application.persistentDataPath, ChatHistoryFileName);
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
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    void OnEnable()
    {
        if (useModernChatLayout)
            ApplyModernChatLayout();
    }

    void Start()
    {
        ApplyChatRoomLayout();

        if (sendButton != null)
            sendButton.onClick.AddListener(OnSendMessage);

        LoadChatHistoryAndRebuildUI();

        if (useModernChatLayout)
            StartCoroutine(ApplyModernLayoutAfterFirstFrame());

        EnsureChatContentVerticalLayoutForBubbles();
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
        if (resizeScrollAboveInputBar && pinnedInputBarHeight > 0f)
            bottom += pinnedInputBarHeight;

        float top = Mathf.Max(0f, scrollTopInset);
        float left = Mathf.Max(0f, scrollLeftInset);
        float right = Mathf.Max(0f, scrollRightInset);

        scrollRt.offsetMin = new Vector2(left, bottom);
        scrollRt.offsetMax = new Vector2(-right, -top);
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
            FirebaseManager.Instance.SendChatMessage(GetLocalSenderDisplayName(), inputField.text);
            inputField.text = ""; 
        }
        else
        {
            Debug.LogError("找不到 FirebaseManager 物件！");
        }
    }

    /// <summary>Call after rotation or safe-area changes if layout does not update automatically.</summary>
    public void RefreshChatLayout()
    {
        ApplyChatRoomLayout();
    }

    // 由 FirebaseManager 監聽到新訊息時呼叫
    public void DisplayMessage(ChatMessage msg)
    {
        if (messagePrefab == null || chatContent == null) return;
        // 避免 Firebase 重連時重複加入已從本地載入的訊息
        if (localHistory.Exists(m => m.userName == msg.userName && m.message == msg.message && m.timestamp == msg.timestamp))
            return;

        localHistory.Add(msg);
        SaveChatHistoryToFile();

        AddMessageBubbleToUI(msg);

        ScrollToLatest();
    }

    void AddMessageBubbleToUI(ChatMessage msg)
    {
        // May run before Start() if messages arrive while panel is still inactive.
        EnsureChatContentVerticalLayoutForBubbles();

        bool isSelf = IsMessageFromLocalUser(msg);
        GameObject row = null;
        if (alignBubblesBySenderSide)
        {
            row = CreateChatBubbleRowShell();
            if (isSelf)
                AddChatRowFlexSpacer(row.transform);
        }

        Transform bubbleParent = row != null ? row.transform : chatContent;
        GameObject newMsg = Instantiate(messagePrefab, bubbleParent);
        if (row != null && !isSelf)
            AddChatRowFlexSpacer(row.transform);

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
        if (chatContent == null || messagePrefab == null) return;

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

        // 清除現有泡泡
        for (int i = chatContent.childCount - 1; i >= 0; i--)
            Destroy(chatContent.GetChild(i).gameObject);

        // 依序重建訊息泡泡，保留歷史
        foreach (ChatMessage msg in localHistory)
            AddMessageBubbleToUI(msg);

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