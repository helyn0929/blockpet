using UnityEngine;
using Firebase;
using Firebase.Database;
using System;

public class FirebaseManager : MonoBehaviour
{
    public static FirebaseManager Instance; // 方便其他腳本直接呼叫
    DatabaseReference dbRef;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        
        // 初始化 Firebase
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task => {
            var dependencyStatus = task.Result;
            if (dependencyStatus == DependencyStatus.Available) {
                // 取得資料庫根節點引用
                string databaseUrl = "https://blockpet-fc23b-default-rtdb.firebaseio.com/";
                dbRef = FirebaseDatabase.GetInstance(databaseUrl).RootReference;
                Debug.Log("Firebase Initialized Successfully!");

                StartListeningForMessages();
                
            } else {
                Debug.LogError($"Could not resolve all Firebase dependencies: {dependencyStatus}");
            }
        });
    }

    // 發送訊息到資料庫
    public void SendChatMessage(string name, string message)
    {
        if (dbRef == null) {
        Debug.LogError("資料庫尚未連線成功，請稍候！");
        return;
    }
        ChatMessage newMessage = new ChatMessage(name, message);
        string json = JsonUtility.ToJson(newMessage);
        
        // 在 "ChatRoom" 下建立一個唯一的 Key 並存入資料
        dbRef.Child("ChatRoom").Push().SetRawJsonValueAsync(json);
    }

    // 在 FirebaseManager.cs 裡增加以下內容
    void StartListeningForMessages()
    {
    // 監聽 ChatRoom 下的所有子節點增加事件
    dbRef.Child("ChatRoom").ChildAdded += HandleChildAdded;
    }
    void HandleChildAdded(object sender, ChildChangedEventArgs args)
    {
    if (args.DatabaseError != null) {
        Debug.LogError(args.DatabaseError.Message);
        return;
    }

    // 將資料轉回 ChatMessage 物件
    string json = args.Snapshot.GetRawJsonValue();
    ChatMessage msg = JsonUtility.FromJson<ChatMessage>(json);

    // 呼叫 UI 腳本在畫面上產生泡泡
    ChatUIHandler.Instance.DisplayMessage(msg);
    }
}