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
    public static System.Action OnRoomChanged;
    bool _loginNotified;

    DatabaseReference dbRef;
    FirebaseAuth auth;
    bool _chatListening;
    Query _chatQuery;
    bool _chatInitialLoaded;
    bool _photoListening;
    Query _photoQuery;
    readonly HashSet<string> _seenPhotoFileNames = new HashSet<string>();
    string _roomId;
    const string PrefsRoomId = "Blockpet.RoomId";
    public string RoomId => string.IsNullOrEmpty(_roomId) ? "global" : _roomId;

    bool _petListening;
    DatabaseReference _petRef;
    RoomPetState _petState;
    public bool HasRoomPetState => _petState != null;

    // Room membership (per user)
    const string UsersNode = "Users";
    [Header("Editor convenience")]
    [Tooltip("In Unity Editor, automatically sign in anonymously so chat history/listener works without a login UI.")]
    [SerializeField] bool autoAnonymousSignInInEditor = true;
    [Header("Device testing convenience")]
    [Tooltip("On iOS/Android, automatically sign in anonymously if not logged in yet. Useful for quick multi-device chat testing.")]
    [SerializeField] bool autoAnonymousSignInOnDevice = true;
    [Header("Cloud photo sync (MVP)")]
    [Tooltip("When enabled, saved photos will be uploaded (compressed) to RTDB and other devices will receive them into their local album. This is a quick MVP; for production prefer Firebase Storage + metadata.")]
    [SerializeField] bool enableCloudPhotoSync = true;
    [Tooltip("Maximum edge size for uploaded photos (pixels). Larger images are downscaled before upload.")]
    [SerializeField] int photoUploadMaxEdge = 1024;
    [Tooltip("Maximum encoded image size (bytes) for RTDB payload safety. If exceeded, quality is reduced.")]
    [SerializeField] int photoUploadMaxBytes = 450_000;

    [Header("Shared room pet")]
    [Tooltip("When enabled, the room shares one pet health/progress state across devices in the same roomId.")]
    [SerializeField] bool enableSharedRoomPet = true;

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
                _roomId = PlayerPrefs.GetString(PrefsRoomId, "global");

                // If the scene has an explicit login UI, do NOT auto-sign-in on device.
                // Otherwise the login page will be skipped immediately, confusing the intended flow:
                // Login -> RoomPage -> MainGame(Home).
                bool hasLoginUi =
                    FindObjectOfType<LoginUIHandler>(true) != null ||
                    FindObjectOfType<LoginScreenController>(true) != null;
#if UNITY_EDITOR
                if (autoAnonymousSignInInEditor && auth != null && auth.CurrentUser == null)
                    SignInAnonymously();
#elif UNITY_IOS || UNITY_ANDROID
                if (!hasLoginUi && autoAnonymousSignInOnDevice && auth != null && auth.CurrentUser == null)
                    SignInAnonymously();
#endif

                // If we are already signed in (e.g. returning user), no login flow will fire.
                // Notify once so UI can route to RoomPage consistently.
                if (!_loginNotified && !hasLoginUi && auth != null && auth.CurrentUser != null)
                {
                    _loginNotified = true;
                    lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => OnLoginSuccess?.Invoke(true)); }
                }

                // IMPORTANT: many RTDB security rules require an authenticated user.
                // Start listening after sign-in so global history actually loads.
                TryStartListeningForMessages();
                TryStartListeningForPhotos();
                TryStartListeningForPetState();

                if (enableCloudPhotoSync && SaveManager.Instance != null)
                    SaveManager.OnPhotoSavedMeta += HandleLocalPhotoSaved;
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
        if (enableCloudPhotoSync && SaveManager.Instance != null)
            SaveManager.OnPhotoSavedMeta -= HandleLocalPhotoSaved;

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
                    {
                        TryStartListeningForMessages();
                        TryStartListeningForPhotos();
                    }
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

    public void SignInWithEmail(string email, string password)
    {
        if (auth == null)
        {
            lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => OnLoginSuccess?.Invoke(false)); }
            return;
        }
        auth.SignInWithEmailAndPasswordAsync(email.Trim(), password).ContinueWith(task => {
            lock (_mainThreadQueue)
            {
                _mainThreadQueue.Enqueue(() => {
                    bool success = task.IsCompleted && !task.IsFaulted && !task.IsCanceled;
                    OnLoginSuccess?.Invoke(success);
                    if (success)
                    {
                        TryStartListeningForMessages();
                        TryStartListeningForPhotos();
                        TryStartListeningForPetState();
                    }
                });
            }
        });
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

        // If already signed in, reuse existing user instead of creating a new anonymous account.
        if (auth.CurrentUser != null)
        {
            Debug.Log("[FirebaseManager] Already signed in as " + auth.CurrentUser.UserId + ", reusing session.");
            lock (_mainThreadQueue)
            {
                _mainThreadQueue.Enqueue(() => {
                    OnLoginSuccess?.Invoke(true);
                    TryStartListeningForMessages();
                    TryStartListeningForPhotos();
                    TryStartListeningForPetState();
                });
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
                    {
                        TryStartListeningForMessages();
                        TryStartListeningForPhotos();
                    }
                });
            }
        });
    }

    [Serializable]
    public class RoomMeta
    {
        public string roomId;
        public string name;
        public string ownerUserId;
        public string createdAt;
        public string lastActiveAt;
    }

    [Serializable]
    public class RoomSummary
    {
        public string roomId;
        public string name;
        public string lastActiveAt;
        public int petIndex;
        public float currentHealth;
        public string lastUpdateTime;
    }

    DatabaseReference UserRoomsRoot()
    {
        string uid = GetUserId();
        return string.IsNullOrEmpty(uid) ? null : dbRef.Child(UsersNode).Child(uid).Child("rooms");
    }

    /// <summary>Returns the current user's rooms node in the same database instance used by this manager.</summary>
    public DatabaseReference GetUserRoomsRef() => UserRoomsRoot();

    /// <summary>Creates a room (meta + membership). Callback runs on main thread.</summary>
    public void CreateRoom(string roomId, string roomName, Action<bool, string> done)
    {
        if (dbRef == null || auth == null || auth.CurrentUser == null)
        {
            done?.Invoke(false, "not signed in");
            return;
        }

        string id = string.IsNullOrWhiteSpace(roomId) ? null : roomId.Trim();
        if (string.IsNullOrEmpty(id))
        {
            done?.Invoke(false, "roomId empty");
            return;
        }

        var meta = new RoomMeta
        {
            roomId = id,
            name = string.IsNullOrWhiteSpace(roomName) ? ("Room " + id) : roomName.Trim(),
            ownerUserId = auth.CurrentUser.UserId,
            createdAt = DateTime.UtcNow.ToString("o"),
            lastActiveAt = DateTime.UtcNow.ToString("o"),
        };

        var roomRoot = dbRef.Child("Rooms").Child(id);
        roomRoot.Child("meta").SetRawJsonValueAsync(JsonUtility.ToJson(meta)).ContinueWith(t =>
        {
            bool ok = t.IsCompleted && !t.IsFaulted && !t.IsCanceled;
            if (!ok)
            {
                lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(false, t.Exception?.ToString()));
                return;
            }

            // Join as a member + add to "my rooms" list.
            roomRoot.Child("members").Child(auth.CurrentUser.UserId).SetValueAsync(true).ContinueWith(tMember =>
            {
                bool okMember = tMember.IsCompleted && !tMember.IsFaulted && !tMember.IsCanceled;
                if (!okMember)
                {
                    lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(false, tMember.Exception?.ToString()));
                    return;
                }

                UserRoomsRoot()?.Child(id).SetValueAsync(true).ContinueWith(t2 =>
                {
                    bool ok2 = t2.IsCompleted && !t2.IsFaulted && !t2.IsCanceled;
                    lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(ok2, ok2 ? null : t2.Exception?.ToString()));
                });
            });
        });
    }

    /// <summary>Joins an existing room by adding membership. Callback runs on main thread.</summary>
    public void JoinRoom(string roomId, Action<bool, string> done)
    {
        if (dbRef == null || auth == null || auth.CurrentUser == null)
        {
            done?.Invoke(false, "not signed in");
            return;
        }
        string id = string.IsNullOrWhiteSpace(roomId) ? null : roomId.Trim();
        if (string.IsNullOrEmpty(id))
        {
            done?.Invoke(false, "roomId empty");
            return;
        }

        // Ensure room exists by reading meta once.
        dbRef.Child("Rooms").Child(id).Child("meta").GetValueAsync().ContinueWith(t =>
        {
            if (t.IsFaulted || t.IsCanceled || t.Result == null || !t.Result.Exists)
            {
                lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(false, "room not found"));
                return;
            }
            var roomRoot = dbRef.Child("Rooms").Child(id);
            roomRoot.Child("members").Child(auth.CurrentUser.UserId).SetValueAsync(true).ContinueWith(tMember =>
            {
                bool okMember = tMember.IsCompleted && !tMember.IsFaulted && !tMember.IsCanceled;
                if (!okMember)
                {
                    lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(false, tMember.Exception?.ToString()));
                    return;
                }

                UserRoomsRoot()?.Child(id).SetValueAsync(true).ContinueWith(t2 =>
                {
                    bool ok2 = t2.IsCompleted && !t2.IsFaulted && !t2.IsCanceled;
                    lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(ok2, ok2 ? null : t2.Exception?.ToString()));
                });
            });
        });
    }

    /// <summary>Fetches the signed-in user's rooms as summaries. Callback runs on main thread.</summary>
    public void GetMyRoomSummaries(Action<List<RoomSummary>> done)
    {
        if (dbRef == null || auth == null || auth.CurrentUser == null)
        {
            done?.Invoke(new List<RoomSummary>());
            return;
        }

        var userRooms = UserRoomsRoot();
        if (userRooms == null)
        {
            done?.Invoke(new List<RoomSummary>());
            return;
        }

        userRooms.GetValueAsync().ContinueWith(t =>
        {
            if (t.IsFaulted || t.IsCanceled || t.Result == null || !t.Result.Exists)
            {
                lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(new List<RoomSummary>()));
                return;
            }

            var ids = new List<string>();
            foreach (var c in t.Result.Children)
            {
                if (c == null) continue;
                ids.Add(c.Key);
            }

            if (ids.Count == 0)
            {
                lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(new List<RoomSummary>()));
                return;
            }

            // Fetch each room meta + petState (small count expected).
            var results = new List<RoomSummary>();
            int remaining = ids.Count;
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                dbRef.Child("Rooms").Child(id).GetValueAsync().ContinueWith(tRoom =>
                {
                    try
                    {
                        if (tRoom.IsCompleted && !tRoom.IsFaulted && !tRoom.IsCanceled && tRoom.Result != null && tRoom.Result.Exists)
                        {
                            var snap = tRoom.Result;
                            var metaSnap = snap.Child("meta");
                            RoomMeta meta = null;
                            if (metaSnap != null && metaSnap.Exists)
                            {
                                string mj = metaSnap.GetRawJsonValue();
                                meta = !string.IsNullOrEmpty(mj) ? JsonUtility.FromJson<RoomMeta>(mj) : null;
                            }

                            var petSnap = snap.Child("petState");
                            RoomPetState pet = null;
                            if (petSnap != null && petSnap.Exists)
                            {
                                string pj = petSnap.GetRawJsonValue();
                                pet = !string.IsNullOrEmpty(pj) ? JsonUtility.FromJson<RoomPetState>(pj) : null;
                            }

                            var sum = new RoomSummary
                            {
                                roomId = id,
                                name = meta != null ? meta.name : ("Room " + id),
                                lastActiveAt = meta != null ? meta.lastActiveAt : null,
                                petIndex = pet != null ? pet.petIndex : 0,
                                currentHealth = pet != null ? pet.currentHealth : 0f,
                                lastUpdateTime = pet != null ? pet.lastUpdateTime : null
                            };

                            lock (results) results.Add(sum);
                        }
                    }
                    catch { }
                    finally
                    {
                        if (System.Threading.Interlocked.Decrement(ref remaining) == 0)
                        {
                            // Stable order: lastActive desc, then roomId.
                            results.Sort((a, b) =>
                            {
                                int tcmp = string.CompareOrdinal(b.lastActiveAt ?? "", a.lastActiveAt ?? "");
                                if (tcmp != 0) return tcmp;
                                return string.CompareOrdinal(a.roomId ?? "", b.roomId ?? "");
                            });
                            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(results));
                        }
                    }
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
        
        // Under Rooms/{roomId}/Chat create a unique key.
        RoomRoot().Child("Chat").Push().SetRawJsonValueAsync(json).ContinueWith(t =>
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
        _chatQuery = RoomRoot().Child("Chat")
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

    [Serializable]
    class CloudPhotoRecord
    {
        public string fileName;
        public string timestamp;
        public string takerAvatarFileName;
        public string userId;
        public string displayName;
        public string imageBase64Jpg;
    }

    void TryStartListeningForPhotos()
    {
        if (_photoListening) return;
        if (!enableCloudPhotoSync) return;
        if (dbRef == null || auth == null) return;
        if (auth.CurrentUser == null) return;

        _photoListening = true;

        _photoQuery = RoomRoot().Child("SharedPhotos")
            .OrderByChild("timestamp")
            .LimitToLast(200);

        // Listen for new photos (we don't need a separate initial fetch; ChildAdded will include existing ones too).
        _photoQuery.ChildAdded += HandlePhotoChildAdded;
    }

    void HandleLocalPhotoSaved(PhotoMeta meta)
    {
        if (!enableCloudPhotoSync) return;
        if (meta == null || string.IsNullOrEmpty(meta.fileName)) return;
        if (dbRef == null || auth == null || auth.CurrentUser == null) return;

        // Dedupe local echo: if we already saw this fileName from cloud, don't re-upload.
        if (_seenPhotoFileNames.Contains(meta.fileName))
            return;

        // Load bytes from disk via SaveManager, then compress for RTDB.
        Texture2D tex = SaveManager.Instance != null ? SaveManager.Instance.LoadPhoto(meta) : null;
        if (tex == null) return;

        byte[] jpg = EncodeJpgForCloud(tex, photoUploadMaxEdge, photoUploadMaxBytes);
        if (jpg == null || jpg.Length == 0)
            return;

        var rec = new CloudPhotoRecord
        {
            fileName = meta.fileName,
            timestamp = meta.timestamp,
            takerAvatarFileName = meta.takerAvatarFileName,
            userId = auth.CurrentUser.UserId,
            displayName = GetDisplayName(),
            imageBase64Jpg = Convert.ToBase64String(jpg)
        };

        string json = JsonUtility.ToJson(rec);
        RoomRoot().Child("SharedPhotos").Push().SetRawJsonValueAsync(json).ContinueWith(t =>
        {
            if (t.IsFaulted)
                Debug.LogError("[FirebaseManager] Upload photo failed: " + (t.Exception?.Flatten().InnerException?.Message ?? t.Exception?.ToString()));
        });
    }

    void HandlePhotoChildAdded(object sender, ChildChangedEventArgs args)
    {
        if (!_photoListening || !enableCloudPhotoSync)
            return;
        if (args.DatabaseError != null)
        {
            Debug.LogError("[FirebaseManager] Photo listener error: " + args.DatabaseError.Message);
            return;
        }

        string json = args.Snapshot.GetRawJsonValue();
        if (string.IsNullOrEmpty(json)) return;

        CloudPhotoRecord rec = null;
        try { rec = JsonUtility.FromJson<CloudPhotoRecord>(json); } catch { rec = null; }
        if (rec == null || string.IsNullOrEmpty(rec.fileName) || string.IsNullOrEmpty(rec.imageBase64Jpg))
            return;

        // Mark seen before import to prevent re-upload echo.
        if (!_seenPhotoFileNames.Contains(rec.fileName))
            _seenPhotoFileNames.Add(rec.fileName);

        // If already have it locally, ignore.
        if (SaveManager.Instance?.data?.photos != null && SaveManager.Instance.data.photos.Exists(p => p != null && p.fileName == rec.fileName))
            return;

        byte[] bytes = null;
        try { bytes = Convert.FromBase64String(rec.imageBase64Jpg); } catch { bytes = null; }
        if (bytes == null || bytes.Length == 0) return;

        lock (_mainThreadQueue)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                if (SaveManager.Instance == null) return;
                var meta = new PhotoMeta(rec.fileName, rec.takerAvatarFileName);
                if (!string.IsNullOrEmpty(rec.timestamp))
                    meta.timestamp = rec.timestamp;
                SaveManager.Instance.ImportPhotoFromCloud(meta, bytes);
            });
        }
    }

    [Serializable]
    public class RoomPetState
    {
        public float currentHealth;
        public string lastUpdateTime;
        public int petIndex;
        public int startingPhotoCount;
    }

    DatabaseReference RoomRoot()
    {
        return dbRef.Child("Rooms").Child(RoomId);
    }

    public void SetRoomId(string roomId)
    {
        string normalized = string.IsNullOrWhiteSpace(roomId) ? "global" : roomId.Trim();
        if (normalized.Length > 32) normalized = normalized.Substring(0, 32);
        _roomId = normalized;
        PlayerPrefs.SetString(PrefsRoomId, _roomId);
        PlayerPrefs.Save();

        // Reset all listeners to re-bind under the new room.
        ResetChatListenState();
        StopPhotoListening();
        StopPetListening();
        _seenPhotoFileNames.Clear();

        OnRoomChanged?.Invoke();

        TryStartListeningForMessages();
        TryStartListeningForPhotos();
        TryStartListeningForPetState();
    }

    void StopPhotoListening()
    {
        if (_photoQuery != null)
        {
            _photoQuery.ChildAdded -= HandlePhotoChildAdded;
            _photoQuery = null;
        }
        _photoListening = false;
    }

    void TryStartListeningForPetState()
    {
        if (_petListening) return;
        if (!enableSharedRoomPet) return;
        if (dbRef == null || auth == null) return;
        if (auth.CurrentUser == null) return;

        _petListening = true;
        _petRef = RoomRoot().Child("petState");
        _petRef.ValueChanged += HandlePetStateChanged;

        // Ensure node exists (create default on first join).
        _petRef.GetValueAsync().ContinueWith(t =>
        {
            if (t.IsFaulted || t.IsCanceled) return;
            if (t.Result == null || !t.Result.Exists)
            {
                var init = new RoomPetState
                {
                    currentHealth = SaveManager.Instance?.data?.currentHealth ?? 86400f,
                    lastUpdateTime = DateTime.Now.ToString(),
                    petIndex = PlayerPrefs.GetInt("PetCollection_CurrentPetIndex", 0),
                    startingPhotoCount = PlayerPrefs.GetInt("PetCollection_StartingPhotoCount", 0)
                };
                _petRef.SetRawJsonValueAsync(JsonUtility.ToJson(init));
            }
        });
    }

    void StopPetListening()
    {
        if (_petRef != null)
            _petRef.ValueChanged -= HandlePetStateChanged;
        _petRef = null;
        _petListening = false;
        _petState = null;
    }

    void HandlePetStateChanged(object sender, ValueChangedEventArgs args)
    {
        if (!_petListening || !enableSharedRoomPet) return;
        if (args.DatabaseError != null)
        {
            Debug.LogError("[FirebaseManager] petState listener error: " + args.DatabaseError.Message);
            return;
        }
        string json = args.Snapshot?.GetRawJsonValue();
        if (string.IsNullOrEmpty(json)) return;

        RoomPetState st = null;
        try { st = JsonUtility.FromJson<RoomPetState>(json); } catch { st = null; }
        if (st == null) return;
        _petState = st;

        // Apply to local systems on main thread.
        lock (_mainThreadQueue)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                // Update SaveManager health snapshot so other UI can read it.
                if (SaveManager.Instance != null && SaveManager.Instance.data != null)
                {
                    SaveManager.Instance.data.currentHealth = st.currentHealth;
                    SaveManager.Instance.data.lastUpdateTime = st.lastUpdateTime;
                }

                // Mirror pet collection prefs so existing UI updates without refactor.
                PlayerPrefs.SetInt("PetCollection_CurrentPetIndex", st.petIndex);
                PlayerPrefs.SetInt("PetCollection_StartingPhotoCount", st.startingPhotoCount);
                PlayerPrefs.Save();
            });
        }
    }

    public float GetRoomHealthNow(float maxHealth)
    {
        if (_petState == null) return SaveManager.Instance?.data?.currentHealth ?? maxHealth;
        float baseHealth = _petState.currentHealth;
        if (string.IsNullOrEmpty(_petState.lastUpdateTime)) return Mathf.Clamp(baseHealth, 0, maxHealth);
        if (!DateTime.TryParse(_petState.lastUpdateTime, out var last)) return Mathf.Clamp(baseHealth, 0, maxHealth);
        float decayed = baseHealth - (float)(DateTime.Now - last).TotalSeconds;
        return Mathf.Clamp(decayed, 0, maxHealth);
    }

    public void AddRoomHealth(float healAmount, float maxHealth)
    {
        if (!enableSharedRoomPet || _petRef == null) return;
        _petRef.RunTransaction(mutable =>
        {
            var dict = mutable.Value as Dictionary<string, object> ?? new Dictionary<string, object>();
            float cur = 0f;
            if (dict.TryGetValue("currentHealth", out var curObj))
                cur = Convert.ToSingle(curObj);
            string lastStr = dict.TryGetValue("lastUpdateTime", out var lastObj) ? (lastObj as string) : null;
            if (!string.IsNullOrEmpty(lastStr) && DateTime.TryParse(lastStr, out var last))
            {
                cur -= (float)(DateTime.Now - last).TotalSeconds;
            }
            cur = Mathf.Clamp(cur + healAmount, 0, maxHealth);
            dict["currentHealth"] = cur;
            dict["lastUpdateTime"] = DateTime.Now.ToString();

            // Preserve pet progress keys if present.
            mutable.Value = dict;
            return TransactionResult.Success(mutable);
        });
    }

    public void PublishRoomPetProgress(int petIndex, int startingPhotoCount)
    {
        if (!enableSharedRoomPet || _petRef == null) return;
        _petRef.Child("petIndex").SetValueAsync(petIndex);
        _petRef.Child("startingPhotoCount").SetValueAsync(startingPhotoCount);
    }

    static byte[] EncodeJpgForCloud(Texture2D tex, int maxEdge, int maxBytes)
    {
        if (tex == null) return null;

        Texture2D working = tex;
        if (maxEdge > 0)
        {
            int w = tex.width;
            int h = tex.height;
            int edge = Mathf.Max(w, h);
            if (edge > maxEdge)
            {
                float scale = (float)maxEdge / edge;
                int nw = Mathf.Max(2, Mathf.RoundToInt(w * scale));
                int nh = Mathf.Max(2, Mathf.RoundToInt(h * scale));
                working = ResizeBilinear(tex, nw, nh);
            }
        }

        int quality = 80;
        byte[] jpg = working.EncodeToJPG(quality);
        while (jpg != null && maxBytes > 0 && jpg.Length > maxBytes && quality > 35)
        {
            quality -= 10;
            jpg = working.EncodeToJPG(quality);
        }

        if (working != tex)
            UnityEngine.Object.Destroy(working);

        return jpg;
    }

    static Texture2D ResizeBilinear(Texture2D src, int newW, int newH)
    {
        var dst = new Texture2D(newW, newH, TextureFormat.RGBA32, false);
        var srcPixels = src.GetPixels32();
        var dstPixels = new Color32[newW * newH];
        float xRatio = (src.width - 1f) / (newW - 1f);
        float yRatio = (src.height - 1f) / (newH - 1f);

        for (int y = 0; y < newH; y++)
        {
            float sy = y * yRatio;
            int y0 = Mathf.FloorToInt(sy);
            int y1 = Mathf.Min(y0 + 1, src.height - 1);
            float fy = sy - y0;

            for (int x = 0; x < newW; x++)
            {
                float sx = x * xRatio;
                int x0 = Mathf.FloorToInt(sx);
                int x1 = Mathf.Min(x0 + 1, src.width - 1);
                float fx = sx - x0;

                Color32 c00 = srcPixels[y0 * src.width + x0];
                Color32 c10 = srcPixels[y0 * src.width + x1];
                Color32 c01 = srcPixels[y1 * src.width + x0];
                Color32 c11 = srcPixels[y1 * src.width + x1];

                byte r = (byte)(
                    c00.r * (1 - fx) * (1 - fy) +
                    c10.r * fx * (1 - fy) +
                    c01.r * (1 - fx) * fy +
                    c11.r * fx * fy
                );
                byte g = (byte)(
                    c00.g * (1 - fx) * (1 - fy) +
                    c10.g * fx * (1 - fy) +
                    c01.g * (1 - fx) * fy +
                    c11.g * fx * fy
                );
                byte b = (byte)(
                    c00.b * (1 - fx) * (1 - fy) +
                    c10.b * fx * (1 - fy) +
                    c01.b * (1 - fx) * fy +
                    c11.b * fx * fy
                );
                byte a = (byte)(
                    c00.a * (1 - fx) * (1 - fy) +
                    c10.a * fx * (1 - fy) +
                    c01.a * (1 - fx) * fy +
                    c11.a * fx * fy
                );

                dstPixels[y * newW + x] = new Color32(r, g, b, a);
            }
        }

        dst.SetPixels32(dstPixels);
        dst.Apply(false, false);
        return dst;
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