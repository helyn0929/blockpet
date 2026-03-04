using System;

// 加上 [Serializable] 讓 Unity 的 JsonUtility 可以處理它
[Serializable]
public class ChatMessage
{
    public string userName;
    public string message;
    public long timestamp;

    // Firebase 必須要有一個無參數的建構子
    public ChatMessage() { }

    public ChatMessage(string name, string msg)
    {
        this.userName = name;
        this.message = msg;
        // 取得當前 UTC 時間的 Unix 秒數
        this.timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}