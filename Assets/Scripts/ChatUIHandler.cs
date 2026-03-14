using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using System.IO;

public class ChatUIHandler : MonoBehaviour
{
    // 單例模式：讓 FirebaseManager 可以直接呼叫
    public static ChatUIHandler Instance;

    const string ChatHistoryFileName = "chat_history.json";

    [Header("UI 連結")]
    public TMP_InputField inputField; 
    public Button sendButton;
    public Transform chatContent;    // 拖入 Scroll View 裡的 Content
    public ScrollRect scrollRect;    // 拖入 Scroll View 本身，用於自動捲動

    [Header("預製物")]
    public GameObject messagePrefab; // 拖入妳做的文字泡泡 Prefab

    List<ChatMessage> localHistory = new List<ChatMessage>();
    string ChatHistoryPath => Path.Combine(Application.persistentDataPath, ChatHistoryFileName);

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (sendButton != null)
            sendButton.onClick.AddListener(OnSendMessage);

        LoadChatHistoryAndRebuildUI();
    }

    void OnSendMessage()
    {
        if (inputField == null) return;
        if (string.IsNullOrEmpty(inputField.text)) return;

        if (FirebaseManager.Instance != null)
        {
            FirebaseManager.Instance.SendChatMessage("HelynWang", inputField.text);
            inputField.text = ""; 
        }
        else
        {
            Debug.LogError("找不到 FirebaseManager 物件！");
        }
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

        Canvas.ForceUpdateCanvases();
        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 0f; // 0 代表最底部
    }

    void AddMessageBubbleToUI(ChatMessage msg)
    {
        GameObject newMsg = Instantiate(messagePrefab, chatContent);
        TMP_Text textComponent = newMsg.GetComponentInChildren<TMP_Text>();
        if (textComponent != null)
            textComponent.text = $"{msg.userName}: {msg.message}";
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

        Canvas.ForceUpdateCanvases();
        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 0f;
    }
}