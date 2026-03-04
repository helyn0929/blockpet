using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class ChatUIHandler : MonoBehaviour
{
    // 單例模式：讓 FirebaseManager 可以直接呼叫
    public static ChatUIHandler Instance;

    [Header("UI 連結")]
    public TMP_InputField inputField; 
    public Button sendButton;
    public Transform chatContent;    // 拖入 Scroll View 裡的 Content
    public ScrollRect scrollRect;    // 拖入 Scroll View 本身，用於自動捲動

    [Header("預製物")]
    public GameObject messagePrefab; // 拖入妳做的文字泡泡 Prefab

    void Awake()
    {
        // 初始化單例
        if (Instance == null) Instance = this;
    }

    void Start()
    {
        if (sendButton != null)
            sendButton.onClick.AddListener(OnSendMessage);
    }

    void OnSendMessage()
    {
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

        // 生成新訊息泡泡
        GameObject newMsg = Instantiate(messagePrefab, chatContent);
        
        // 設定文字內容
        TMP_Text textComponent = newMsg.GetComponentInChildren<TMP_Text>();
        if (textComponent != null)
        {
            textComponent.text = $"{msg.userName}: {msg.message}";
        }

        // 強制 UI 重新計算排版並捲動到底部
        Canvas.ForceUpdateCanvases();
        if (scrollRect != null)
        {
            scrollRect.verticalNormalizedPosition = 0f; // 0 代表最底部
        }
    }
}