using UnityEngine;
using Firebase;
using Firebase.Database;
using Firebase.Auth;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
#if (UNITY_ANDROID || UNITY_IOS) && GOOGLE_SIGN_IN
using Google;
#endif

public class FirebaseManager : MonoBehaviour
{
    public static FirebaseManager Instance;
    public static System.Action<bool> OnLoginSuccess;

    DatabaseReference dbRef;
    FirebaseAuth auth;
    bool _chatListening;
    Query _chatQuery;
    bool _chatInitialLoaded;
    [Header("Editor convenience")]
    [Tooltip("In Unity Editor, automatically sign in anonymously so chat history/listener works without a login UI.")]
    [SerializeField] bool autoAnonymousSignInInEditor = true;

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
                // IMPORTANT: many RTDB security rules require an authenticated user.
                // Start listening after sign-in so global history actually loads.
                TryStartListeningForMessages();
#if UNITY_EDITOR
                if (autoAnonymousSignInInEditor && auth != null && auth.CurrentUser == null)
                    SignInAnonymously();
#endif
            } else {
                Debug.LogError($"Could not resolve all Firebase dependencies: {dependencyStatus}");
            }
        });
    }

    /// <summary>Returns the current Firebase user ID, or null if not signed in.</summary>
    public string GetUserId()
    {
        return auth?.CurrentUser?.UserId;
    }

    /// <summary>Returns the display name from the Firebase user, or "Guest".</summary>
    public string GetDisplayName()
    {
        var user = auth?.CurrentUser;
        if (user == null) return "Guest";
        return string.IsNullOrEmpty(user.DisplayName) ? "Guest" : user.DisplayName;
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
#if (UNITY_ANDROID || UNITY_IOS) && GOOGLE_SIGN_IN
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
        Debug.LogWarning("Google Sign-In is unavailable: define GOOGLE_SIGN_IN and install the Google Sign-In SDK (Android/iOS), or use another sign-in method.");
        lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => OnLoginSuccess?.Invoke(false)); }
#endif
    }

#if (UNITY_ANDROID || UNITY_IOS) && GOOGLE_SIGN_IN
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
                    if (success)
                        TryStartListeningForMessages();
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
                    if (success)
                        TryStartListeningForMessages();
                });
            }
        });
    }

    // 發送訊息到資料庫 (legacy signature)
    public void SendChatMessage(string name, string message)
    {
        if (dbRef == null) {
            Debug.LogError("資料庫尚未連線成功，請稍候！");
            return;
        }
        
        ChatMessage newMessage = new ChatMessage(name, message);
        SendChatMessage(newMessage);
    }

    // 發送訊息到資料庫 (supports replies / richer fields)
    public void SendChatMessage(ChatMessage message)
    {
        if (dbRef == null) {
            Debug.LogError("資料庫尚未連線成功，請稍候！");
            return;
        }

        if (message == null) return;
        if (string.IsNullOrEmpty(message.messageId))
            message.messageId = System.Guid.NewGuid().ToString("N");
        if (string.IsNullOrEmpty(message.displayName))
            message.displayName = message.userName;

        string json = JsonUtility.ToJson(message);
        
        // 在 "ChatRoom" 下建立唯一 Key 並存入
        dbRef.Child("ChatRoom").Push().SetRawJsonValueAsync(json).ContinueWith(t =>
        {
            if (t.IsFaulted)
                Debug.LogError("[FirebaseManager] SendChatMessage failed: " + (t.Exception?.Flatten().InnerException?.Message ?? t.Exception?.ToString()));
            else if (t.IsCanceled)
                Debug.LogWarning("[FirebaseManager] SendChatMessage canceled.");
        });
    }

    void TryStartListeningForMessages()
    {
        if (_chatListening) return;
        if (dbRef == null || auth == null)
            return;
        if (auth.CurrentUser == null)
        {
#if UNITY_EDITOR
            Debug.Log("[FirebaseManager] Chat listen skipped: not signed in (auth.CurrentUser is null).");
#endif
            return;
        }

        _chatListening = true;

        // Global history: explicitly fetch, then start live updates.
        _chatQuery = dbRef.Child("ChatRoom")
            .OrderByChild("timestamp")
            .LimitToLast(200);

        _chatQuery.GetValueAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                Debug.LogError("[FirebaseManager] Chat history GetValueAsync failed: " + (t.Exception?.Flatten().InnerException?.Message ?? t.Exception?.ToString()));
                lock (_mainThreadQueue)
                    _mainThreadQueue.Enqueue(ResetChatListenState);
                return;
            }
            if (t.IsCanceled)
            {
                Debug.LogWarning("[FirebaseManager] Chat history GetValueAsync canceled.");
                lock (_mainThreadQueue)
                    _mainThreadQueue.Enqueue(ResetChatListenState);
                return;
            }

            var snap = t.Result;
            lock (_mainThreadQueue)
            {
                _mainThreadQueue.Enqueue(() =>
                {
                    if (_chatQuery == null || !_chatListening)
                        return;

                    if (snap != null && snap.ChildrenCount > 0)
                    {
                        foreach (var child in snap.Children)
                        {
                            string json = child.GetRawJsonValue();
                            ChatMessage msg = json != null ? JsonUtility.FromJson<ChatMessage>(json) : null;
                            if (msg != null && ChatUIHandler.Instance != null)
                                ChatUIHandler.Instance.DisplayMessage(msg);
                        }
                    }

                    if (_chatQuery == null || !_chatListening)
                        return;

                    _chatInitialLoaded = true;
                    // After initial load, listen for new messages. StartAt is not used because timestamps can collide; ChatUIHandler dedupes by messageId.
                    _chatQuery.ChildAdded += HandleChildAdded;
                });
            }
        });
    }

    /// <summary>Unsubscribes from live chat updates (e.g. user left chat page intentionally). Call <see cref="EnsureChatListening"/> when opening chat again.</summary>
    public void StopChatListening()
    {
        ResetChatListenState();
    }

    /// <summary>Starts RTDB chat listener if signed in and not already listening.</summary>
    public void EnsureChatListening()
    {
        TryStartListeningForMessages();
    }

    void ResetChatListenState()
    {
        if (_chatQuery != null)
        {
            _chatQuery.ChildAdded -= HandleChildAdded;
            _chatQuery = null;
        }

        _chatListening = false;
        _chatInitialLoaded = false;
    }

    void HandleChildAdded(object sender, ChildChangedEventArgs args)
    {
        if (!_chatListening || !_chatInitialLoaded)
            return;
        if (args.DatabaseError != null) {
            Debug.LogError(args.DatabaseError.Message + "");
            return;
        }

        string json = args.Snapshot.GetRawJsonValue();
        ChatMessage msg = json != null ? JsonUtility.FromJson<ChatMessage>(json) : null;
        if (msg == null) return;

        lock (_mainThreadQueue) {
            _mainThreadQueue.Enqueue(() => {
                if (!_chatListening || ChatUIHandler.Instance == null)
                    return;
                ChatUIHandler.Instance.DisplayMessage(msg);
            });
        }
    }
}