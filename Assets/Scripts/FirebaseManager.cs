using UnityEngine;
using Firebase;
using Firebase.Database;
using System;
using System.Collections;
using System.Collections.Generic;

public class FirebaseManager : MonoBehaviour
{
    public static FirebaseManager Instance; 
    DatabaseReference dbRef;

    // 用來存放待處理的訊息隊列，解決多執行緒 UI 更新問題
    private Queue<Action> _mainThreadQueue = new Queue<Action>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
        
        // 1. 設定 AppOptions，使用妳截圖中的專屬 URL
        AppOptions options = new AppOptions();
        options.DatabaseUrl = new Uri("https://blockpet-fc23b-default-rtdb.firebaseio.com/");       
        
        // 2. 初始化 Firebase
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task => {
            var dependencyStatus = task.Result;
            if (dependencyStatus == DependencyStatus.Available) {
                // 建立 App 實例並取得資料庫引用
                FirebaseApp app = FirebaseApp.Create(options); 
                dbRef = FirebaseDatabase.GetInstance(app).RootReference;
                
                Debug.Log("Firebase Initialized Successfully!");
                StartListeningForMessages();
            } else {
                Debug.LogError($"Could not resolve all Firebase dependencies: {dependencyStatus}");
            }
        });
    }

    void Update()
    {
        // 每一幀檢查是否有需要回到主執行緒執行的 UI 任務
        lock (_mainThreadQueue) {
            while (_mainThreadQueue.Count > 0) {
                _mainThreadQueue.Dequeue().Invoke();
            }
        }
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
        
        // 在 "ChatRoom" 下建立唯一 Key 並存入
        dbRef.Child("ChatRoom").Push().SetRawJsonValueAsync(json);
    }

    void StartListeningForMessages()
    {
        // 監聽新訊息事件
        dbRef.Child("ChatRoom").ChildAdded += HandleChildAdded;
    }

    void HandleChildAdded(object sender, ChildChangedEventArgs args)
    {
        if (args.DatabaseError != null) {
            Debug.LogError(args.DatabaseError.Message + "");
            return;
        }

        string json = args.Snapshot.GetRawJsonValue();
        ChatMessage msg = JsonUtility.FromJson<ChatMessage>(json);

        Debug.Log($"[Firebase] New message from : {json}");

        // 將 UI 更新任務排入主執行緒隊列
        lock (_mainThreadQueue) {
            _mainThreadQueue.Enqueue(() => {
                if (ChatUIHandler.Instance != null) {
                    ChatUIHandler.Instance.DisplayMessage(msg);
                }
            });
        }
    }
}