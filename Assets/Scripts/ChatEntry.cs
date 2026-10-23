using System.Collections.Generic;

/// <summary>
/// 聊天快取的一則（<see cref="FirebaseManager.ChatEntries"/>）：Firebase key、訊息內容、誰按了哪個 emoji。
/// emoji 存在 Rooms/{roomId}/Chat/{key}/reactions/{emoji}/{uid} = true（emoji 用 heart／laugh／yum／wow／like／paw）。
/// </summary>
public class ChatEntry
{
    public string key;
    public ChatMessage msg;
    public Dictionary<string, List<string>> reactions = new Dictionary<string, List<string>>();

    public bool IsPost => msg != null && msg.kind == "post";
}
