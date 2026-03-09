using UnityEngine;
using Firebase;
using Firebase.Database;
using Firebase.Auth;
using System;
using System.Collections;
using System.Collections.Generic;

public class FirebaseManager : MonoBehaviour
{
    public static FirebaseManager Instance;
    public static event Action AuthStateChanged;

    DatabaseReference dbRef;
    FirebaseAuth auth;

    // 用來存放待處理的訊息隊列，解決多執行緒 UI 更新問題
    private Queue<Action> _mainThreadQueue = new Queue<Action>();

    public bool IsLoggedIn => auth != null && auth.CurrentUser != null;
    public string UserId => auth?.CurrentUser?.UserId ?? string.Empty;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        // 1. 設定 AppOptions
        AppOptions options = new AppOptions();
        options.DatabaseUrl = new Uri("https://blockpet-fc23b-default-rtdb.firebaseio.com/");

        // 2. 初始化 Firebase
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task => {
            var dependencyStatus = task.Result;
            if (dependencyStatus == DependencyStatus.Available) {
                FirebaseApp app = FirebaseApp.Create(options);
                dbRef = FirebaseDatabase.GetInstance(app).RootReference;
                auth = FirebaseAuth.GetAuth(app);

                Debug.Log("Firebase Initialized Successfully!");
                StartListeningForMessages();
                SignInAnonymouslyIfNeeded();
            } else {
                Debug.LogError($"Could not resolve all Firebase dependencies: {dependencyStatus}");
            }
        });
    }

    void SignInAnonymouslyIfNeeded()
    {
        if (auth == null) return;
        if (auth.CurrentUser != null) {
            EnqueueMainThread(() => AuthStateChanged?.Invoke());
            return;
        }
        auth.SignInAnonymouslyAsync().ContinueWith(loginTask => {
            if (loginTask.IsCanceled || loginTask.IsFaulted) {
                Debug.LogWarning("[Firebase] Anonymous sign-in failed: " + (loginTask.Exception?.Message ?? "canceled"));
                return;
            }
            Debug.Log("[Firebase] Signed in anonymously: " + auth.CurrentUser?.UserId);
            EnqueueMainThread(() => AuthStateChanged?.Invoke());
        });
    }

    void EnqueueMainThread(Action action)
    {
        lock (_mainThreadQueue) _mainThreadQueue.Enqueue(action);
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

    public DatabaseReference GetDatabaseRoot()
    {
        return dbRef;
    }

    void HandleChildAdded(object sender, ChildChangedEventArgs args)
    {
        if (args.DatabaseError != null) {
            Debug.LogError(args.DatabaseError.Message + "");
            return;
        }

        string json = args.Snapshot.GetRawJsonValue();
        ChatMessage msg = JsonUtility.FromJson<ChatMessage>(json);

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