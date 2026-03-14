using UnityEngine;
using Firebase;
using Firebase.Database;
using Firebase.Auth;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
#if UNITY_ANDROID || UNITY_IOS
using Google;
#endif

public class FirebaseManager : MonoBehaviour
{
    public static FirebaseManager Instance;
    public static System.Action<bool> OnLoginSuccess;

    DatabaseReference dbRef;
    FirebaseAuth auth;

    private Queue<Action> _mainThreadQueue = new Queue<Action>();

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        AppOptions options = new AppOptions();
        options.DatabaseUrl = new Uri("https://blockpet-fc23b-default-rtdb.firebaseio.com/");

        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task => {
            var dependencyStatus = task.Result;
            if (dependencyStatus == DependencyStatus.Available) {
                FirebaseApp app = FirebaseApp.Create(options);
                dbRef = FirebaseDatabase.GetInstance(app).RootReference;
                auth = FirebaseAuth.GetAuth(app);
                Debug.Log("Firebase Initialized Successfully!");
                StartListeningForMessages();
            } else {
                Debug.LogError($"Could not resolve all Firebase dependencies: {dependencyStatus}");
            }
        });
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        lock (_mainThreadQueue) {
            while (_mainThreadQueue.Count > 0) {
                _mainThreadQueue.Dequeue().Invoke();
            }
        }
    }

    // Web Client ID from Google Cloud Console (OAuth 2.0)
    private const string GoogleWebClientId = "39782728703-9kf4c6p1lggr12b4t78nblohavu226td.apps.googleusercontent.com";

    public void SignInWithGoogle()
    {
#if UNITY_ANDROID || UNITY_IOS
        if (auth == null)
        {
            lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => OnLoginSuccess?.Invoke(false)); }
            return;
        }
        var configuration = new GoogleSignInConfiguration
        {
            WebClientId = GoogleWebClientId,
            RequestIdToken = true
        };
        GoogleSignIn.Configuration = configuration;
        GoogleSignIn.DefaultInstance.SignIn().ContinueWith(OnGoogleSignInFinished);
#else
        Debug.LogWarning("Google Sign-In is only supported on Android and iOS.");
        lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => OnLoginSuccess?.Invoke(false)); }
#endif
    }

#if UNITY_ANDROID || UNITY_IOS
    void OnGoogleSignInFinished(Task<GoogleSignInUser> task)
    {
        if (task.IsCanceled)
        {
            Debug.Log("Google Sign-In was canceled.");
            lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => OnLoginSuccess?.Invoke(false)); }
            return;
        }
        if (task.IsFaulted)
        {
            Debug.LogError("Google Sign-In error: " + (task.Exception?.Flatten()?.InnerException?.Message ?? task.Exception?.ToString()));
            lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => OnLoginSuccess?.Invoke(false)); }
            return;
        }
        GoogleSignInUser user = task.Result;
        string idToken = user?.IdToken;
        if (string.IsNullOrEmpty(idToken))
        {
            Debug.LogError("Google Sign-In: IdToken is null or empty.");
            lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => OnLoginSuccess?.Invoke(false)); }
            return;
        }
        Credential credential = GoogleAuthProvider.GetCredential(idToken, null);
        auth.SignInWithCredentialAsync(credential).ContinueWith(firebaseTask =>
        {
            lock (_mainThreadQueue)
            {
                _mainThreadQueue.Enqueue(() =>
                {
                    bool success = firebaseTask.IsCompleted && !firebaseTask.IsFaulted && !firebaseTask.IsCanceled;
                    OnLoginSuccess?.Invoke(success);
                });
            }
        });
    }
#endif

    public void SignInWithApple()
    {
        Debug.Log("Apple Sign In Called");
        // TODO: Implement real Apple Sign-In and then enqueue OnLoginSuccess(true/false) on _mainThreadQueue.
    }

    public void SignInAnonymously()
    {
        Debug.Log("Guest Sign In Starting...");
        if (auth == null)
        {
            lock (_mainThreadQueue)
            {
                _mainThreadQueue.Enqueue(() => OnLoginSuccess?.Invoke(false));
            }
            return;
        }

        auth.SignInAnonymouslyAsync().ContinueWith(task => {
            lock (_mainThreadQueue)
            {
                _mainThreadQueue.Enqueue(() => {
                    bool success = task.IsCompleted && !task.IsFaulted && !task.IsCanceled;
                    OnLoginSuccess?.Invoke(success);
                });
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
        ChatMessage msg = json != null ? JsonUtility.FromJson<ChatMessage>(json) : null;
        if (msg == null) return;

        lock (_mainThreadQueue) {
            _mainThreadQueue.Enqueue(() => {
                if (ChatUIHandler.Instance != null)
                    ChatUIHandler.Instance.DisplayMessage(msg);
            });
        }
    }
}