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
#if UNITY_IOS
using AppleAuth;
using AppleAuth.Enums;
using AppleAuth.Extensions;
using AppleAuth.Interfaces;
using AppleAuth.Native;
#endif

public class FirebaseManager : MonoBehaviour
{
    public static FirebaseManager Instance;
    public static System.Action<bool> OnLoginSuccess;
    public static System.Action OnRoomChanged;
    public static System.Action OnLogout;
    bool _loginNotified;

    DatabaseReference dbRef;
    FirebaseAuth auth;
    bool _chatListening;
    Query _chatQuery;
    bool _chatInitialLoaded;
    EventHandler<ChildChangedEventArgs> _chatChildAddedHandler;
    bool _photoListening;
    Query _photoQuery;
    readonly HashSet<string> _seenPhotoFileNames = new HashSet<string>();
    string _roomId;
    const string PrefsRoomId = "Blockpet.RoomId";
    const string PrefsUserSignedOut = "Blockpet.UserSignedOut";
    public string RoomId => _roomId ?? "";

    bool _petListening;
    DatabaseReference _petRef;
    RoomPetState _petState;
    public bool HasRoomPetState => _petState != null;

    bool _equipListening;
    DatabaseReference _equipRef;

    bool _coinsListening;
    DatabaseReference _coinsRef;

    // Room membership (per user)
    const string UsersNode = "Users";
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

#if UNITY_IOS
    IAppleAuthManager _appleAuthManager;
#endif

    void Awake()
    {
#if UNITY_IOS
        if (AppleAuthManager.IsCurrentPlatformSupported)
            _appleAuthManager = new AppleAuthManager(new PayloadDeserializer());
#endif
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task => {
            if (task.IsFaulted) {
                Debug.LogError("[FirebaseManager] CheckAndFixDependencies faulted: " + task.Exception);
                return;
            }
            var dependencyStatus = task.Result;
            if (dependencyStatus == DependencyStatus.Available) {
                try {
                FirebaseApp app = FirebaseApp.DefaultInstance;
                var db = FirebaseDatabase.GetInstance(app, "https://blockpet-fc23b-default-rtdb.firebaseio.com/");
                db.SetPersistenceEnabled(true);
                dbRef = db.RootReference;
                auth = FirebaseAuth.GetAuth(app);
                Debug.Log($"[FirebaseManager] Init OK. CurrentUser={auth?.CurrentUser?.UserId ?? "null"}");
                _roomId = PlayerPrefs.GetString(PrefsRoomId, "");

                // Listen for auth state changes (sign-in, token refresh, sign-out).
                auth.StateChanged += OnAuthStateChanged;

                // If already signed in (returning user), skip the login screen automatically.
                // But respect explicit sign-out: if the user signed out last session, show the login screen.
                if (!_loginNotified && auth != null && auth.CurrentUser != null
                    && PlayerPrefs.GetInt(PrefsUserSignedOut, 0) == 0)
                {
                    _loginNotified = true;
                    lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => OnLoginSuccess?.Invoke(true)); }
                }

                // IMPORTANT: many RTDB security rules require an authenticated user.
                // Start listening after sign-in so global history actually loads.
                TryStartListeningForMessages();
                TryStartListeningForPhotos();
                TryStartListeningForPetState();
                TryStartListeningForRoomEquipState();
                TryStartListeningForRoomCoins();

                if (enableCloudPhotoSync && SaveManager.Instance != null)
                    SaveManager.OnPhotoSavedMeta += HandleLocalPhotoSaved;
                } catch (System.Exception ex) {
                    Debug.LogError("[FirebaseManager] Init exception: " + ex);
                }
            } else {
                Debug.LogError($"[FirebaseManager] Dependency error: {dependencyStatus}");
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

    /// <summary>True when the signed-in user has a non-empty display name set.</summary>
    public bool HasDisplayName =>
        auth?.CurrentUser != null && !string.IsNullOrEmpty(auth.CurrentUser.DisplayName);

    /// <summary>Updates the Firebase Auth display name and writes to RTDB Users/{uid}/nickname.</summary>
    public void SetDisplayName(string name, Action<bool> callback = null)
    {
        var user = auth?.CurrentUser;
        if (user == null) { callback?.Invoke(false); return; }

        string trimmed = name.Trim();
        var profile = new UserProfile { DisplayName = trimmed };
        user.UpdateUserProfileAsync(profile).ContinueWith(task =>
        {
            bool ok = task.IsCompleted && !task.IsFaulted && !task.IsCanceled;
            if (ok && dbRef != null)
                dbRef.Child(UsersNode).Child(user.UserId).Child("nickname").SetValueAsync(trimmed);
            lock (_mainThreadQueue)
            {
                _mainThreadQueue.Enqueue(() => callback?.Invoke(ok));
            }
        });
    }

    /// <summary>Checks RTDB Users/{uid}/nickname to determine if the user has completed registration.
    /// Works for all auth methods (Google users have Firebase display name but may not have RTDB nickname).</summary>
    public void CheckHasNickname(Action<bool> callback)
    {
        string uid = GetUserId();
        if (string.IsNullOrEmpty(uid) || dbRef == null)
        {
            lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => callback?.Invoke(false)); }
            return;
        }
        dbRef.Child(UsersNode).Child(uid).Child("nickname").GetValueAsync().ContinueWith(t =>
        {
            bool has = t.IsCompleted && !t.IsFaulted && !t.IsCanceled
                       && t.Result != null && t.Result.Exists
                       && !string.IsNullOrEmpty(t.Result.Value as string);
            lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => callback?.Invoke(has)); }
        });
    }

    /// <summary>Permanently deletes the current Firebase Auth account and signs out.</summary>
    public void DeleteAccount(Action<bool, string> callback = null)
    {
        var user = auth?.CurrentUser;
        if (user == null) { callback?.Invoke(false, "未登入"); return; }

        user.DeleteAsync().ContinueWith(task =>
        {
            bool ok = task.IsCompleted && !task.IsFaulted && !task.IsCanceled;
            string err = ok ? null : (task.Exception?.InnerException?.Message ?? "刪除失敗");
            lock (_mainThreadQueue)
            {
                _mainThreadQueue.Enqueue(() => callback?.Invoke(ok, err));
            }
        });
    }

    /// <summary>Signs out the current user.</summary>
    public void SignOut()
    {
        SetRoomId("");
        PlayerPrefs.SetInt(PrefsUserSignedOut, 1);
        PlayerPrefs.Save();
        auth?.SignOut();
        _loginNotified = false;
        lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => OnLogout?.Invoke()); }
    }

    void OnAuthStateChanged(object sender, System.EventArgs e)
    {
        if (auth == null) return;
        string uid = auth.CurrentUser?.UserId;
        Debug.Log($"[FirebaseManager] AuthStateChanged: uid={uid ?? "null"}");

        if (uid != null && !_loginNotified)
        {
            // User actively signed in — clear the signed-out flag so auto-login works next launch.
            PlayerPrefs.SetInt(PrefsUserSignedOut, 0);
            PlayerPrefs.Save();
            _loginNotified = true;
            lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => OnLoginSuccess?.Invoke(true)); }
        }

        // Restart listeners whenever auth becomes valid (e.g. after token refresh).
        if (uid != null)
        {
            lock (_mainThreadQueue)
            {
                _mainThreadQueue.Enqueue(() =>
                {
                    TryStartListeningForMessages();
                    TryStartListeningForPhotos();
                    TryStartListeningForPetState();
                    TryStartListeningForRoomEquipState();
                    TryStartListeningForRoomCoins();
                });
            }
        }
    }

    void OnDestroy()
    {
        if (auth != null) auth.StateChanged -= OnAuthStateChanged;
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
#if UNITY_IOS
        _appleAuthManager?.Update();
#endif
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
#if UNITY_IOS
        if (_appleAuthManager == null)
        {
            lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => OnLoginSuccess?.Invoke(false)); }
            return;
        }

        string rawNonce = GenerateNonce(32);
        string hashedNonce = HashNonce(rawNonce);

        var loginArgs = new AppleAuthLoginArgs(
            LoginOptions.IncludeEmail | LoginOptions.IncludeFullName, hashedNonce);

        _appleAuthManager.LoginWithAppleId(loginArgs,
            credential =>
            {
                var appleCredential = credential as IAppleIDCredential;
                if (appleCredential == null)
                {
                    lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => OnLoginSuccess?.Invoke(false)); }
                    return;
                }

                string identityToken = System.Text.Encoding.UTF8.GetString(appleCredential.IdentityToken);
                var firebaseCredential = OAuthProvider.GetCredential("apple.com", identityToken, rawNonce, null);

                auth.SignInWithCredentialAsync(firebaseCredential).ContinueWith(task =>
                {
                    lock (_mainThreadQueue)
                    {
                        _mainThreadQueue.Enqueue(() =>
                        {
                            bool success = task.IsCompleted && !task.IsFaulted && !task.IsCanceled;
                            OnLoginSuccess?.Invoke(success);
                        });
                    }
                });
            },
            error =>
            {
                Debug.LogError("Apple Sign-In failed: " + error.GetAuthorizationErrorCode() + " " + error);
                lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => OnLoginSuccess?.Invoke(false)); }
            });
#else
        Debug.LogWarning("Apple Sign-In is only supported on iOS.");
        lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => OnLoginSuccess?.Invoke(false)); }
#endif
    }

    static string GenerateNonce(int length)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var sb = new System.Text.StringBuilder();
        var rng = new System.Random();
        for (int i = 0; i < length; i++)
            sb.Append(chars[rng.Next(chars.Length)]);
        return sb.ToString();
    }

    static string HashNonce(string rawNonce)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rawNonce));
        var sb = new System.Text.StringBuilder();
        foreach (byte b in hash)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
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

    public void CreateUserWithEmail(string email, string password)
    {
        if (auth == null)
        {
            lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => OnLoginSuccess?.Invoke(false)); }
            return;
        }
        auth.CreateUserWithEmailAndPasswordAsync(email.Trim(), password).ContinueWith(task => {
            lock (_mainThreadQueue)
            {
                _mainThreadQueue.Enqueue(() => {
                    bool success = task.IsCompleted && !task.IsFaulted && !task.IsCanceled;
                    // On success, auth.StateChanged fires and raises OnLoginSuccess(true) automatically.
                    // Only need to explicitly notify on failure.
                    if (!success)
                        OnLoginSuccess?.Invoke(false);
                });
            }
        });
    }

    public void UpdatePassword(string newPassword, Action<bool, string> callback)
    {
        var user = auth?.CurrentUser;
        if (user == null) { callback?.Invoke(false, "未登入"); return; }
        user.UpdatePasswordAsync(newPassword).ContinueWith(task =>
        {
            lock (_mainThreadQueue)
            {
                _mainThreadQueue.Enqueue(() =>
                {
                    if (task.IsCompletedSuccessfully)
                        callback?.Invoke(true, null);
                    else
                        callback?.Invoke(false, task.Exception?.GetBaseException().Message ?? "更新失敗");
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

    /// <summary>
    /// Deletes a room for ALL members: reads the members list, removes Users/{uid}/rooms/{roomId}
    /// for every member, then wipes Rooms/{roomId}/ entirely. Callback runs on main thread.
    /// </summary>
    public void DeleteRoom(string roomId, Action<bool, string> done)
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

        // Read members list first so we can clean up every user's room index.
        dbRef.Child("Rooms").Child(id).Child("members").GetValueAsync().ContinueWith(tMembers =>
        {
            var removeTasks = new System.Collections.Generic.List<System.Threading.Tasks.Task>();

            if (tMembers.IsCompleted && !tMembers.IsFaulted && tMembers.Result != null && tMembers.Result.Exists)
            {
                foreach (var child in tMembers.Result.Children)
                {
                    string uid = child.Key;
                    removeTasks.Add(dbRef.Child(UsersNode).Child(uid).Child("rooms").Child(id).RemoveValueAsync());
                }
            }

            // Delete the entire room node.
            removeTasks.Add(dbRef.Child("Rooms").Child(id).RemoveValueAsync());

            System.Threading.Tasks.Task.WhenAll(removeTasks).ContinueWith(t =>
            {
                bool ok = t.IsCompleted && !t.IsFaulted && !t.IsCanceled;
                lock (_mainThreadQueue)
                {
                    _mainThreadQueue.Enqueue(() =>
                    {
                        if (_roomId == id)
                            SetRoomId("");
                        done?.Invoke(ok, ok ? null : t.Exception?.ToString());
                    });
                }
            });
        });
    }

    /// <summary>
    /// Leaves a room for the current user only: removes their membership and room index entry.
    /// The room and other members are untouched. Callback runs on main thread.
    /// </summary>
    public void LeaveRoom(string roomId, Action<bool, string> done)
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

        string uid = auth.CurrentUser.UserId;
        var tasks = new System.Threading.Tasks.Task[]
        {
            UserRoomsRoot()?.Child(id).RemoveValueAsync(),
            dbRef.Child("Rooms").Child(id).Child("members").Child(uid).RemoveValueAsync()
        };

        System.Threading.Tasks.Task.WhenAll(tasks).ContinueWith(t =>
        {
            bool ok = t.IsCompleted && !t.IsFaulted && !t.IsCanceled;
            lock (_mainThreadQueue)
            {
                _mainThreadQueue.Enqueue(() =>
                {
                    if (_roomId == id)
                        SetRoomId("");
                    done?.Invoke(ok, ok ? null : t.Exception?.ToString());
                });
            }
        });
    }

    /// <summary>Updates the room's display name. Callback runs on main thread.</summary>
    public void SetRoomName(string roomId, string newName, Action<bool> done)
    {
        if (dbRef == null || string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(newName))
        {
            done?.Invoke(false);
            return;
        }
        dbRef.Child("Rooms").Child(roomId).Child("meta").Child("name")
            .SetValueAsync(newName.Trim()).ContinueWith(t =>
        {
            bool ok = t.IsCompleted && !t.IsFaulted && !t.IsCanceled;
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(ok));
        });
    }

    /// <summary>Fetches meta (name, createdAt, ownerUserId) for a single room. Callback runs on main thread.</summary>
    public void GetRoomMeta(string roomId, Action<RoomMeta> done)
    {
        if (dbRef == null || string.IsNullOrEmpty(roomId)) { done?.Invoke(null); return; }
        dbRef.Child("Rooms").Child(roomId).Child("meta").GetValueAsync().ContinueWith(t =>
        {
            RoomMeta meta = null;
            if (t.IsCompleted && !t.IsFaulted && t.Result != null && t.Result.Exists)
            {
                string json = t.Result.GetRawJsonValue();
                if (!string.IsNullOrEmpty(json))
                    try { meta = JsonUtility.FromJson<RoomMeta>(json); } catch { }
            }
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(meta));
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

    // ── Room members ─────────────────────────────────────────────────────────

    [Serializable]
    public class RoomMemberInfo
    {
        public string uid;
        public string nickname;
    }

    /// <summary>Reads Rooms/{roomId}/members, fetches each member's nickname, returns on main thread.</summary>
    public void GetRoomMembers(string roomId, Action<List<RoomMemberInfo>> done)
    {
        if (dbRef == null || string.IsNullOrEmpty(roomId))
        {
            done?.Invoke(new List<RoomMemberInfo>());
            return;
        }

        dbRef.Child("Rooms").Child(roomId).Child("members").GetValueAsync().ContinueWith(t =>
        {
            if (t.IsFaulted || t.IsCanceled || t.Result == null || !t.Result.Exists)
            {
                lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(new List<RoomMemberInfo>()));
                return;
            }

            var uids = new List<string>();
            foreach (var child in t.Result.Children)
                if (child != null) uids.Add(child.Key);

            if (uids.Count == 0)
            {
                lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(new List<RoomMemberInfo>()));
                return;
            }

            var results  = new List<RoomMemberInfo>();
            int remaining = uids.Count;

            foreach (string uid in uids)
            {
                string capturedUid = uid;
                dbRef.Child(UsersNode).Child(capturedUid).Child("nickname").GetValueAsync().ContinueWith(tNick =>
                {
                    string nick = "?";
                    try
                    {
                        if (tNick.IsCompleted && !tNick.IsFaulted && tNick.Result != null && tNick.Result.Exists)
                            nick = tNick.Result.Value?.ToString() ?? "?";
                    }
                    catch { }

                    lock (results)
                    {
                        results.Add(new RoomMemberInfo { uid = capturedUid, nickname = nick });
                        if (System.Threading.Interlocked.Decrement(ref remaining) == 0)
                        {
                            var final = new List<RoomMemberInfo>(results);
                            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(final));
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
            Debug.LogError("[FirebaseManager] SendChatMessage: dbRef null");
            return;
        }
        if (auth?.CurrentUser == null) {
            Debug.LogError("[FirebaseManager] SendChatMessage: not authenticated (uid=null), dropping message");
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
        if (string.IsNullOrEmpty(_roomId)) return;
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

        // Capture the query reference so the async callback can verify it is still
        // the active query when it fires — guards against the race condition where
        // SetRoomId() switches rooms while GetValueAsync() is still in flight.
        var capturedQuery = _chatQuery;

        capturedQuery.GetValueAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                Debug.LogError("[FirebaseManager] Chat history GetValueAsync failed: " + (t.Exception?.Flatten().InnerException?.Message ?? t.Exception?.ToString()));
                lock (_mainThreadQueue)
                    _mainThreadQueue.Enqueue(() => { if (ReferenceEquals(_chatQuery, capturedQuery)) ResetChatListenState(); });
                return;
            }
            if (t.IsCanceled)
            {
                Debug.LogWarning("[FirebaseManager] Chat history GetValueAsync canceled.");
                lock (_mainThreadQueue)
                    _mainThreadQueue.Enqueue(() => { if (ReferenceEquals(_chatQuery, capturedQuery)) ResetChatListenState(); });
                return;
            }

            var snap = t.Result;
            lock (_mainThreadQueue)
            {
                _mainThreadQueue.Enqueue(() =>
                {
                    // If the query was replaced (room switched mid-flight), discard this result.
                    if (!_chatListening || !ReferenceEquals(_chatQuery, capturedQuery))
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

                    if (!_chatListening || !ReferenceEquals(_chatQuery, capturedQuery))
                        return;

                    _chatInitialLoaded = true;
                    // Capture the query so the ChildAdded callback can detect stale
                    // deliveries that arrive after a room switch. The lambda stores
                    // qForHandler and checks it on the main thread before displaying.
                    var qForHandler = capturedQuery;
                    _chatChildAddedHandler = (sender2, args2) => HandleChildAddedForQuery(args2, qForHandler);
                    _chatQuery.ChildAdded += _chatChildAddedHandler;
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
        if (string.IsNullOrEmpty(_roomId)) return;
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
        string normalized = roomId?.Trim() ?? "";
        if (normalized.Length > 32) normalized = normalized.Substring(0, 32);
        _roomId = normalized;
        PlayerPrefs.SetString(PrefsRoomId, _roomId);
        PlayerPrefs.Save();

        // Stop all listeners first so _petState = null before OnRoomSwitched fires.
        // This prevents PetCollectionManager.EnsureValidState() from publishing
        // stale data to Firebase while HasRoomPetState is still true.
        ResetChatListenState();
        StopPhotoListening();
        StopPetListening();
        StopEquipListening();
        StopCoinsListening();
        _seenPhotoFileNames.Clear();

        // Switch per-room save data — fires OnRoomSwitched with _petState already null.
        SaveManager.Instance?.SwitchRoom(_roomId);

        OnRoomChanged?.Invoke();

        TryStartListeningForMessages();
        TryStartListeningForPhotos();
        TryStartListeningForPetState();
        TryStartListeningForRoomEquipState();
        TryStartListeningForRoomCoins();

        // Write membership + user room index, then retry listeners.
        // Guards against race condition where Firebase rules deny listeners before membership is confirmed.
        if (dbRef != null && auth?.CurrentUser != null && !string.IsNullOrEmpty(_roomId))
        {
            string uid = auth.CurrentUser.UserId;
            string roomSnap = _roomId;
            // Also write to Users/{uid}/rooms/{roomId} so this room appears in the WebView room list.
            UserRoomsRoot()?.Child(roomSnap).SetValueAsync(true);
            dbRef.Child("Rooms").Child(roomSnap).Child("members").Child(uid)
                 .SetValueAsync(true)
                 .ContinueWith(t =>
                 {
                     if (t.IsFaulted || t.IsCanceled) return;
                     lock (_mainThreadQueue)
                     {
                         _mainThreadQueue.Enqueue(() =>
                         {
                             if (_roomId != roomSnap) return;
                             TryStartListeningForMessages();
                             TryStartListeningForPhotos();
                             TryStartListeningForPetState();
                             TryStartListeningForRoomEquipState();
                             TryStartListeningForRoomCoins();
                         });
                     }
                 });
        }
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
        if (string.IsNullOrEmpty(_roomId)) return;
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
                    currentHealth = 86400f,
                    lastUpdateTime = DateTime.Now.ToString(),
                    petIndex = 0,
                    startingPhotoCount = 0
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

    // ─── Room equip state (decoration / pet skin shared across members) ───

    void TryStartListeningForRoomEquipState()
    {
        if (_equipListening) return;
        if (string.IsNullOrEmpty(_roomId)) return;
        if (dbRef == null || auth?.CurrentUser == null) return;
        _equipListening = true;
        _equipRef = RoomRoot().Child("roomState");
        _equipRef.ValueChanged += HandleRoomEquipStateChanged;
    }

    void StopEquipListening()
    {
        if (_equipRef != null)
            _equipRef.ValueChanged -= HandleRoomEquipStateChanged;
        _equipRef = null;
        _equipListening = false;
    }

    void HandleRoomEquipStateChanged(object sender, ValueChangedEventArgs args)
    {
        if (!_equipListening) return;
        if (args.DatabaseError != null) return;
        string json = args.Snapshot?.GetRawJsonValue();
        if (string.IsNullOrEmpty(json)) return;

        RoomEquipState st = null;
        try { st = JsonUtility.FromJson<RoomEquipState>(json); } catch { }
        if (st == null) return;

        lock (_mainThreadQueue)
        {
            _mainThreadQueue.Enqueue(() =>
                MarketInventoryStore.ApplyFromFirebase(
                    st.equippedPetId, st.equippedBgId, st.equippedSpaceId,
                    st.equippedAccessoryIds, st.equippedFurnitureId));
        }
    }

    public void PublishRoomEquipment()
    {
        if (_equipRef == null || !_equipListening) return;
        var st = new RoomEquipState
        {
            equippedPetId        = MarketInventoryStore.GetEquippedPetId(),
            equippedBgId         = MarketInventoryStore.GetEquippedBackgroundId(),
            equippedSpaceId      = MarketInventoryStore.GetEquippedSpaceId(),
            equippedAccessoryIds = string.Join(",", MarketInventoryStore.GetEquippedAccessoryIds()),
            equippedFurnitureId  = MarketInventoryStore.GetEquippedFurnitureId(),
        };
        _equipRef.SetRawJsonValueAsync(JsonUtility.ToJson(st));
    }

    [Serializable]
    class RoomEquipState
    {
        public string equippedPetId;
        public string equippedBgId;
        public string equippedSpaceId;
        public string equippedAccessoryIds;
        public string equippedFurnitureId;
    }

    // ─── Shared room coins ────────────────────────────────────────────

    void TryStartListeningForRoomCoins()
    {
        if (_coinsListening) return;
        if (string.IsNullOrEmpty(_roomId)) return;
        if (dbRef == null || auth?.CurrentUser == null) return;
        _coinsListening = true;
        _coinsRef = RoomRoot().Child("economy").Child("coins");
        _coinsRef.ValueChanged += HandleRoomCoinsChanged;
    }

    void StopCoinsListening()
    {
        if (_coinsRef != null)
            _coinsRef.ValueChanged -= HandleRoomCoinsChanged;
        _coinsRef = null;
        _coinsListening = false;
    }

    void HandleRoomCoinsChanged(object sender, ValueChangedEventArgs args)
    {
        if (!_coinsListening) return;
        if (args.DatabaseError != null) { Debug.LogWarning("[FirebaseManager] coins listener error: " + args.DatabaseError.Message); return; }
        var snap = args.Snapshot;
        if (snap == null) return;
        int coins = 0;
        if (snap.Exists)
            try { coins = Convert.ToInt32(snap.Value); } catch { }
        lock (_mainThreadQueue)
        {
            _mainThreadQueue.Enqueue(() => EconomyManager.Instance?.SetRoomBalance(coins));
        }
    }

    /// <summary>Atomically adds coins to the shared room wallet (safe for concurrent calls).</summary>
    public void AddRoomCoins(int amount)
    {
        if (amount <= 0) return;
        if (_coinsRef == null || !_coinsListening)
        {
            Debug.LogWarning($"[FirebaseManager] AddRoomCoins skipped — ref={_coinsRef != null}, listening={_coinsListening}");
            return;
        }
        _coinsRef.RunTransaction(mutable =>
        {
            long cur = 0;
            if (mutable.Value != null)
                try { cur = Convert.ToInt64(mutable.Value); } catch { }
            mutable.Value = cur + amount;
            return TransactionResult.Success(mutable);
        }).ContinueWith(t =>
        {
            if (t.IsFaulted || t.IsCanceled)
                Debug.LogError("[FirebaseManager] AddRoomCoins transaction failed: " + t.Exception?.GetBaseException()?.Message);
            else
                Debug.Log($"[FirebaseManager] AddRoomCoins +{amount} committed");
        });
    }

    /// <summary>Writes the new balance after a local spend. Not a transaction — acceptable for small groups.</summary>
    public void WriteRoomCoins(int newBalance)
    {
        _coinsRef?.SetValueAsync((long)Mathf.Max(0, newBalance));
    }

    // ─── Avatar cloud backup ──────────────────────────────────────────

    DatabaseReference UserAvatarRef()
    {
        string uid = GetUserId();
        if (string.IsNullOrEmpty(uid) || dbRef == null) return null;
        return dbRef.Child(UsersNode).Child(uid).Child("avatarBase64");
    }

    public void UploadAvatarBase64(string base64)
    {
        var r = UserAvatarRef();
        if (r == null || string.IsNullOrEmpty(base64)) return;
        r.SetValueAsync(base64).ContinueWith(t =>
        {
            if (t.IsFaulted)
                Debug.LogWarning("[FirebaseManager] UploadAvatarBase64 failed: " + t.Exception?.GetBaseException()?.Message);
            else
                Debug.Log("[FirebaseManager] Avatar uploaded to Firebase.");
        });
    }

    public void DownloadAvatarBase64(System.Action<string> onResult)
    {
        var r = UserAvatarRef();
        if (r == null) { onResult?.Invoke(null); return; }
        r.GetValueAsync().ContinueWith(t =>
        {
            if (t.IsFaulted || t.IsCanceled || t.Result == null || !t.Result.Exists)
            {
                lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => onResult?.Invoke(null)); }
                return;
            }
            string b64 = t.Result.Value as string;
            lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => onResult?.Invoke(b64)); }
        });
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
                // Update SaveManager health snapshot so PetHealthManager can read it.
                if (SaveManager.Instance?.data != null)
                {
                    SaveManager.Instance.data.currentHealth = st.currentHealth;
                    SaveManager.Instance.data.lastUpdateTime = st.lastUpdateTime;
                }

                // Push pet index/progress directly into PetCollectionManager.
                PetCollectionManager.Instance?.ApplyRoomPetState(st.petIndex, st.startingPhotoCount);
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
        if (_chatQuery != null && _chatChildAddedHandler != null)
            _chatQuery.ChildAdded -= _chatChildAddedHandler;
        _chatChildAddedHandler = null;
        _chatQuery = null;
        _chatListening = false;
        _chatInitialLoaded = false;
    }

    void HandleChildAddedForQuery(ChildChangedEventArgs args, Query capturedQuery)
    {
        if (!_chatListening || !_chatInitialLoaded) return;
        if (args.DatabaseError != null) {
            Debug.LogError(args.DatabaseError.Message + "");
            return;
        }

        string json = args.Snapshot.GetRawJsonValue();
        ChatMessage msg = json != null ? JsonUtility.FromJson<ChatMessage>(json) : null;
        if (msg == null) return;

        lock (_mainThreadQueue) {
            _mainThreadQueue.Enqueue(() => {
                // Discard if the room has switched since this event was queued.
                if (!_chatListening || !ReferenceEquals(_chatQuery, capturedQuery) || ChatUIHandler.Instance == null)
                    return;
                ChatUIHandler.Instance.DisplayMessage(msg);
            });
        }
    }
}