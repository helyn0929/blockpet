using System;
using System.Collections.Generic;

// 加上 [Serializable] 讓 Unity 的 JsonUtility 可以處理它
[Serializable]
public class ChatMessage
{
    // Stable client-side id (lets us support replies + dedupe without relying on Firebase push key).
    // Older saved/chat history may not have this field; treat null/empty as "unknown".
    public string messageId;
    public string userName;
    // Optional: explicit display name (if you later decouple account id from display name).
    public string displayName;
    // Optional: avatar key / id (resolved by UI, not stored as Sprite).
    public string avatarId;
    public string message;
    public long timestamp;

    // Reply metadata (optional)
    public string replyToMessageId;
    public string replyToDisplayName;
    public string replyToMessagePreview;

    // Firebase 必須要有一個無參數的建構子
    public ChatMessage() { }

    public ChatMessage(string name, string msg)
    {
        this.userName = name;
        this.displayName = name;
        this.message = msg;
        this.messageId = Guid.NewGuid().ToString("N");
        // 取得當前 UTC 時間的 Unix 秒數
        this.timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public static string GetBestDisplayName(ChatMessage msg)
    {
        if (msg == null) return string.Empty;
        if (!string.IsNullOrEmpty(msg.displayName)) return msg.displayName;
        return msg.userName ?? string.Empty;
    }
}

/// <summary>Wrapper for saving/loading chat history as JSON (sender + content per message).</summary>
[Serializable]
public class ChatHistorySave
{
    public List<ChatMessage> messages = new List<ChatMessage>();
}