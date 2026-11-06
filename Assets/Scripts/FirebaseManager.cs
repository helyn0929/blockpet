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
    EventHandler<ChildChangedEventArgs> _chatChildChangedHandler;
    EventHandler<ChildChangedEventArgs> _chatChildRemovedHandler;
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

    long _serverOffsetMs;
    /// <summary>估計的伺服器現在時間（Unix 毫秒）：手機時間加上 Firebase 回報的時間差，改手機時間不影響。</summary>
    public long ServerNowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _serverOffsetMs;

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
        OnLoginSuccess += PublishProfileOnLogin;

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
                // 伺服器時間（愛心計時用）：.info/serverTimeOffset 是「伺服器 − 手機」的毫秒差
                db.GetReference(".info/serverTimeOffset").ValueChanged += (s, e) =>
                {
                    try { _serverOffsetMs = Convert.ToInt64(e.Snapshot?.Value ?? 0L); } catch { }
                };
                auth = FirebaseAuth.GetAuth(app);
                Debug.Log($"[FirebaseManager] Init OK. CurrentUser={auth?.CurrentUser?.UserId ?? "null"}");
                lock (_mainThreadQueue)
                {
                    _mainThreadQueue.Enqueue(() =>
                    {
                        _roomId = PlayerPrefs.GetString(PrefsRoomId, "");
                        // 上次的空間：存檔也要切過去（不然照片、類別都存在沒有空間的暫存裡，關掉遊戲就不見）
                        if (!string.IsNullOrEmpty(_roomId)) SaveManager.Instance?.SwitchRoom(_roomId);
                    });
                }

                // Listen for auth state changes (sign-in, token refresh, sign-out).
                auth.StateChanged += OnAuthStateChanged;

                // If already signed in (returning user), skip the login screen automatically.
                // But respect explicit sign-out: if the user signed out last session, show the login screen.
                if (!_loginNotified && auth != null && auth.CurrentUser != null)
                {
                    _loginNotified = true;
                    lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => {
                        if (PlayerPrefs.GetInt(PrefsUserSignedOut, 0) == 0)
                            OnLoginSuccess?.Invoke(true);
                    }); }
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
            _loginNotified = true;
            lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() =>
            {
                // Must run on main thread — PlayerPrefs is not thread-safe.
                PlayerPrefs.SetInt(PrefsUserSignedOut, 0);
                PlayerPrefs.Save();
                OnLoginSuccess?.Invoke(true);
            }); }
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
        if (Instance == this) OnLoginSuccess -= PublishProfileOnLogin;
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
        if ((task.IsCanceled || task.IsFaulted) && _googleLinkDone != null)
        {
            var linkDone = _googleLinkDone;
            _googleLinkDone = null;
            lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => linkDone(false, "Google 登入沒有完成")); }
            return;
        }
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
        if (_googleLinkDone != null)
        {
            var linkDone = _googleLinkDone;
            _googleLinkDone = null;
            LinkCredential(credential, linkDone);
            return;
        }
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
        RequestAppleCredential(
            firebaseCredential =>
            {
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
            () => { lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => OnLoginSuccess?.Invoke(false)); } });
    }

    /// <summary>跑一次「用 Apple 登入」取得 Firebase 憑證（登入與訪客綁定共用）。失敗時 onFail 在背景執行緒。</summary>
    void RequestAppleCredential(Action<Credential> onCredential, Action onFail)
    {
#if UNITY_IOS
        if (_appleAuthManager == null || auth == null)
        {
            onFail();
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
                    onFail();
                    return;
                }

                string identityToken = System.Text.Encoding.UTF8.GetString(appleCredential.IdentityToken);
                onCredential(OAuthProvider.GetCredential("apple.com", identityToken, rawNonce, null));
            },
            error =>
            {
                Debug.LogError("Apple Sign-In failed: " + error.GetAuthorizationErrorCode() + " " + error);
                onFail();
            });
#else
        Debug.LogWarning("Apple Sign-In is only supported on iOS.");
        onFail();
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

    // ── 帳號種類與訪客綁定 ───────────────────────────────────────

    /// <summary>目前是訪客（匿名登入）。</summary>
    public bool IsGuest => auth?.CurrentUser != null && auth.CurrentUser.IsAnonymous;

    public string GetEmail() => auth?.CurrentUser?.Email;

    // ─── 給其他成員看的名字／帳號 ────────────────────────────────────

    void PublishProfileOnLogin(bool ok)
    {
        if (ok) PublishProfile();
    }

    /// <summary>
    /// 登入後把「其他成員看得到的資料」寫到 Users/{uid}：
    /// - account：帳號的顯示文字（Email 只留前 3 個字＋網域，例如 nay***@gmail.com；Apple 隱藏信箱、沒有 Email 顯示「Apple 帳號」等；訪客「訪客」）。
    /// - nickname：Auth 有名字但資料庫還沒有時補上（例：Google 登入）。
    /// 空間資訊頁的成員列表沒有名字時顯示 account。
    /// </summary>
    public void PublishProfile()
    {
        var user = auth?.CurrentUser;
        if (user == null || dbRef == null) return;
        var node = dbRef.Child(UsersNode).Child(user.UserId);
        node.Child("account").SetValueAsync(AccountLabel()).ContinueWith(t =>
        {
            if (t.IsFaulted) Debug.LogWarning("[FirebaseManager] 寫入 account 失敗：" + t.Exception?.GetBaseException()?.Message);
        });
        if (!string.IsNullOrEmpty(user.DisplayName))
            node.Child("nickname").SetValueAsync(user.DisplayName.Trim());
    }

    string AccountLabel()
    {
        string provider = GetSignInProvider();
        if (provider == null || provider == "anonymous") return "訪客";
        string email = GetEmail();
        if (string.IsNullOrEmpty(email))
        {
            foreach (var info in auth.CurrentUser.ProviderData)
                if (!string.IsNullOrEmpty(info?.Email)) { email = info.Email; break; }
        }
        if (string.IsNullOrEmpty(email) || email.EndsWith("privaterelay.appleid.com", StringComparison.OrdinalIgnoreCase))
            return provider == "apple.com" ? "Apple 帳號" : provider == "google.com" ? "Google 帳號" : "帳號";
        int at = email.IndexOf('@');
        if (at <= 0) return email;
        string local = email.Substring(0, at);
        string keep = local.Length <= 3 ? local.Substring(0, 1) : local.Substring(0, 3);
        return keep + "***" + email.Substring(at);
    }

    /// <summary>登入方式："password"（Email）、"apple.com"、"google.com"；訪客回傳 "anonymous"；未登入回傳 null。</summary>
    public string GetSignInProvider()
    {
        var user = auth?.CurrentUser;
        if (user == null) return null;
        if (user.IsAnonymous) return "anonymous";
        foreach (var info in user.ProviderData)
        {
            string id = info?.ProviderId;
            if (id == "password" || id == "apple.com" || id == "google.com") return id;
        }
        return "password";
    }

    /// <summary>訪客綁定 Email 帳號（保留同一個 uid，寵物與空間都不會不見）。callback 在主執行緒。</summary>
    public void LinkWithEmail(string email, string password, Action<bool, string> done)
    {
        if (auth == null) { done?.Invoke(false, "系統還沒準備好"); return; }
        LinkCredential(EmailAuthProvider.GetCredential(email.Trim(), password), done);
    }

    /// <summary>訪客綁定 Apple 帳號。callback 在主執行緒。</summary>
    public void LinkWithApple(Action<bool, string> done)
    {
        RequestAppleCredential(
            cred => LinkCredential(cred, done),
            () => { lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => done?.Invoke(false, "Apple 登入沒有完成")); } });
    }

    /// <summary>訪客綁定 Google 帳號（這個版本沒有 Google 登入時直接回傳失敗）。callback 在主執行緒。</summary>
    public void LinkWithGoogle(Action<bool, string> done)
    {
#if (UNITY_ANDROID || UNITY_IOS) && GOOGLE_SIGN_IN
        if (auth == null) { done?.Invoke(false, "系統還沒準備好"); return; }
        _googleLinkDone = done;
        GoogleSignIn.Configuration = new GoogleSignInConfiguration { WebClientId = GoogleWebClientId, RequestIdToken = true };
        GoogleSignIn.DefaultInstance.SignIn().ContinueWith(OnGoogleSignInFinished);
#else
        done?.Invoke(false, "這個版本還不能用 Google 登入");
#endif
    }

#if (UNITY_ANDROID || UNITY_IOS) && GOOGLE_SIGN_IN
    Action<bool, string> _googleLinkDone;
#endif

    void LinkCredential(Credential credential, Action<bool, string> done)
    {
        var user = auth?.CurrentUser;
        if (user == null)
        {
            lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => done?.Invoke(false, "未登入")); }
            return;
        }
        user.LinkWithCredentialAsync(credential).ContinueWith(task =>
        {
            bool ok = task.IsCompleted && !task.IsFaulted && !task.IsCanceled;
            string err = ok ? null : (task.Exception?.GetBaseException()?.Message ?? "綁定失敗");
            lock (_mainThreadQueue) { _mainThreadQueue.Enqueue(() => done?.Invoke(ok, err)); }
        });
    }

    /// <summary>最近一次登入是否在 window 之內（Firebase 刪除帳號、改密碼需要最近登入過）。</summary>
    public bool SignedInWithin(TimeSpan window)
    {
        var meta = auth?.CurrentUser?.Metadata;
        if (meta == null) return false;
        var last = DateTimeOffset.FromUnixTimeMilliseconds((long)meta.LastSignInTimestamp);
        return DateTimeOffset.UtcNow - last <= window;
    }

    /// <summary>刪除 Users/{uid}（暱稱、頭像、空間清單）。要在刪除 Auth 帳號之前呼叫。callback 在主執行緒。</summary>
    public void DeleteUserData(Action<bool> done)
    {
        string uid = GetUserId();
        if (dbRef == null || string.IsNullOrEmpty(uid)) { done?.Invoke(false); return; }
        dbRef.Child(UsersNode).Child(uid).RemoveValueAsync().ContinueWith(t =>
        {
            bool ok = t.IsCompleted && !t.IsFaulted && !t.IsCanceled;
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(ok));
        });
    }

    /// <summary>寄送重設密碼信。callback 在主執行緒。</summary>
    public void SendPasswordReset(string email, Action<bool> done)
    {
        if (auth == null || string.IsNullOrWhiteSpace(email)) { done?.Invoke(false); return; }
        auth.SendPasswordResetEmailAsync(email.Trim()).ContinueWith(task =>
        {
            bool ok = task.IsCompleted && !task.IsFaulted && !task.IsCanceled;
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(ok));
        });
    }

    /// <summary>這個版本有沒有 Google 登入（iOS 目前因與 Firebase 12 衝突而關閉，見 GOOGLE_SIGN_IN）。</summary>
    public static bool GoogleSignInAvailable =>
#if (UNITY_ANDROID || UNITY_IOS) && GOOGLE_SIGN_IN
        true;
#else
        false;
#endif

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
        /// <summary>寵物名字（空間資訊可改）；舊空間沒有，沿用 name。</summary>
        public string petName;
        public string ownerUserId;
        public string createdAt;
        public string lastActiveAt;
        /// <summary>營地代碼（藍色 6 碼，設計文件 5.15 ③-2）：只用來加營地好友、當旅行地圖的種子；和邀請碼（空間 id）分開。舊空間沒有，進空間時補上。</summary>
        public string campCode;
    }

    [Serializable]
    public class RoomSummary
    {
        public string roomId;
        public string name;
        public string lastActiveAt;
        public string createdAt;
        public string petName;
        public int petIndex;
        public float currentHealth;
        public string lastUpdateTime;
        public float hearts;      // 上次餵食（或復活）時的愛心
        public long heartsAt;     // 那時的伺服器時間（Unix 毫秒）；0＝舊資料還沒搬移
        public int startingPhotoCount; // 這隻寵物從相簿第幾張開始算進化
    }

    DatabaseReference UserRoomsRoot()
    {
        string uid = GetUserId();
        return string.IsNullOrEmpty(uid) ? null : dbRef.Child(UsersNode).Child(uid).Child("rooms");
    }

    /// <summary>Returns the current user's rooms node in the same database instance used by this manager.</summary>
    public DatabaseReference GetUserRoomsRef() => UserRoomsRoot();

    /// <summary>
    /// 我還在不在這個空間（Users/{uid}/rooms/{roomId} 存在）。開遊戲直接回上次的空間前用，空間被刪掉就不要進去。
    /// 用一次性的監聽而不是 GetValueAsync（選空間頁可能正在聽 rooms，同一路徑再 Get 會觸發 desktop SDK 斷言）；
    /// 有離線快取時馬上回答。回傳在主執行緒；還沒登入回傳 false。
    /// </summary>
    public void CheckMyRoom(string roomId, Action<bool> done)
    {
        var rooms = UserRoomsRoot();
        if (rooms == null || string.IsNullOrWhiteSpace(roomId)) { done?.Invoke(false); return; }
        var r = rooms.Child(roomId.Trim());
        bool answered = false;
        EventHandler<ValueChangedEventArgs> h = null;
        h = (sender, e) =>
        {
            if (answered) return;
            answered = true;
            r.ValueChanged -= h;
            bool ok = e.DatabaseError == null && e.Snapshot != null && e.Snapshot.Exists;
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(ok));
        };
        r.ValueChanged += h;
    }

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

        var initPet = new RoomPetState
        {
            petIndex = 0,
            startingPhotoCount = 0,
            hearts = BlockPet.Feed.PetHearts.Max,
            heartsAt = ServerNowMs,
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

            // Initialize pet as kirby (petIndex 0). Fire-and-forget.
            roomRoot.Child("petState").SetRawJsonValueAsync(JsonUtility.ToJson(initPet));

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
    /// <summary>改寵物名字（Rooms/{roomId}/meta/petName），所有成員都看得到。callback 在主執行緒。</summary>
    public void SetRoomPetName(string roomId, string newName, Action<bool> done)
    {
        if (dbRef == null || string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(newName))
        {
            done?.Invoke(false);
            return;
        }
        dbRef.Child("Rooms").Child(roomId).Child("meta").Child("petName")
            .SetValueAsync(newName.Trim()).ContinueWith(t =>
        {
            bool ok = t.IsCompleted && !t.IsFaulted && !t.IsCanceled;
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(ok));
        });
    }

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

        string currentRoom = _roomId;
        RoomPetState cachedPet = _petState;
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

            // 每個房間只讀 meta 與 petState（不讀整個房間：聊天、照片 base64 很大；
            // 而且讀目前房間的上層節點會讓 desktop SDK 重新監聽子路徑，觸發 persistent_connection 斷言）。
            // 目前房間的 petState 已在監聽，直接用快取，不再讀一次。
            // 每個房間的讀取回到主執行緒才發出（desktop 版 Firebase 在背景執行緒同時開查詢會崩潰）
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => FetchRoomSummaries(ids, currentRoom, cachedPet, done));
        });
    }

    void FetchRoomSummaries(List<string> ids, string currentRoom, RoomPetState cachedPet, Action<List<RoomSummary>> done)
    {
        {
            var results = new List<RoomSummary>();
            int remaining = ids.Count;
            for (int i = 0; i < ids.Count; i++)
            {
                FetchRoomSummary(ids[i], currentRoom, cachedPet, sum =>
                {
                    if (sum != null) lock (results) results.Add(sum);
                    if (System.Threading.Interlocked.Decrement(ref remaining) != 0) return;
                    // Stable order: lastActive desc, then roomId.
                    results.Sort((x, y) =>
                    {
                        int tcmp = string.CompareOrdinal(y.lastActiveAt ?? "", x.lastActiveAt ?? "");
                        if (tcmp != 0) return tcmp;
                        return string.CompareOrdinal(x.roomId ?? "", y.roomId ?? "");
                    });
                    lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(results));
                });
            }
        }
    }

    /// <summary>讀單一房間的 meta＋petState 組成摘要；房間不存在時回傳 null。callback 在背景執行緒。</summary>
    void FetchRoomSummary(string id, string currentRoom, RoomPetState cachedPet, Action<RoomSummary> done)
    {
        var room = dbRef.Child("Rooms").Child(id);
        // 目前空間的 meta 已在監聽（StartMetaListening）：直接用快取，不再讀一次
        //（desktop SDK 對已監聽的路徑再讀會觸發 persistent_connection 斷言；名字載入後 OnRoomMetaChanged 會讓轉盤重新整理）
        var metaTask = id == currentRoom && _metaRef != null
            ? Task.FromResult<DataSnapshot>(null)
            : room.Child("meta").GetValueAsync();
        RoomMeta cachedMeta = id == currentRoom ? _roomMeta : null;
        metaTask.ContinueWith(tMeta =>
        {
            RoomMeta meta = cachedMeta;
            bool exists = cachedMeta != null || (id == currentRoom && _metaRef != null);
            try
            {
                if (tMeta.IsCompleted && !tMeta.IsFaulted && !tMeta.IsCanceled && tMeta.Result != null && tMeta.Result.Exists)
                {
                    exists = true;
                    string mj = tMeta.Result.GetRawJsonValue();
                    meta = !string.IsNullOrEmpty(mj) ? JsonUtility.FromJson<RoomMeta>(mj) : null;
                }
            }
            catch { }

            Action<RoomPetState> finish = pet =>
            {
                if (!exists && pet == null) { done(null); return; }
                done(new RoomSummary
                {
                    roomId = id,
                    name = meta != null ? meta.name : ("Room " + id),
                    lastActiveAt = meta != null ? meta.lastActiveAt : null,
                    createdAt = meta != null ? meta.createdAt : null,
                    petName = meta != null ? meta.petName : null,
                    petIndex = pet != null ? pet.petIndex : 0,
                    currentHealth = pet != null ? pet.currentHealth : 0f,
                    lastUpdateTime = pet != null ? pet.lastUpdateTime : null,
                    hearts = pet != null ? pet.hearts : 0f,
                    heartsAt = pet != null ? pet.heartsAt : 0,
                    startingPhotoCount = pet != null ? pet.startingPhotoCount : 0
                });
            };

            if (id == currentRoom)
            {
                finish(cachedPet);
                return;
            }
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => room.Child("petState").GetValueAsync().ContinueWith(tPet =>
            {
                RoomPetState pet = null;
                try
                {
                    if (tPet.IsCompleted && !tPet.IsFaulted && !tPet.IsCanceled && tPet.Result != null && tPet.Result.Exists)
                    {
                        string pj = tPet.Result.GetRawJsonValue();
                        pet = !string.IsNullOrEmpty(pj) ? JsonUtility.FromJson<RoomPetState>(pj) : null;
                    }
                }
                catch { }
                finish(pet);
            }));
        });
    }

    // ── 空間商店（買過的家具／擴建／寵物蛋，房間成員共用）──────────────
    // Rooms/{roomId}/shop/{itemId} = 數量

    /// <summary>讀目前空間買過的商品與數量。callback 在主執行緒。</summary>
    public void GetRoomShop(Action<Dictionary<string, int>> done)
    {
        var result = new Dictionary<string, int>();
        if (dbRef == null || auth?.CurrentUser == null || string.IsNullOrEmpty(_roomId)) { done?.Invoke(result); return; }
        RoomRoot().Child("shop").GetValueAsync().ContinueWith(t =>
        {
            if (t.IsCompleted && !t.IsFaulted && !t.IsCanceled && t.Result != null)
            {
                foreach (var c in t.Result.Children)
                {
                    int n = 0;
                    try { n = Convert.ToInt32(c.Value); } catch { }
                    if (n > 0) result[c.Key] = n;
                }
            }
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(result));
        });
    }

    // ─── 圖鑑（設計文件 5.23）：Rooms/{room}/dex/{系列}/{外型}/{紀錄} ─────

    /// <summary>圖鑑記一筆的結果。</summary>
    public struct DexRecordResult
    {
        public bool ok;
        /// <summary>這一格之前沒收過（會發獎勵、跳「圖鑑新增！」）。</summary>
        public bool isNewCell;
        /// <summary>這一筆讓系列收滿 7 格。</summary>
        public bool seriesComplete;
        /// <summary>記完之後這個系列收到幾格。</summary>
        public int got;
        /// <summary>這一筆是營地的第幾代。</summary>
        public int gen;
    }

    /// <summary>讀目前空間的圖鑑（營地共用一本）。callback 在主執行緒；讀不到回傳空的圖鑑。</summary>
    public void LoadRoomDex(Action<BlockPet.Feed.DexBook> done)
    {
        if (dbRef == null || auth?.CurrentUser == null || string.IsNullOrEmpty(_roomId)) { done?.Invoke(new BlockPet.Feed.DexBook()); return; }
        RoomRoot().Child("dex").GetValueAsync().ContinueWith(t =>
        {
            var book = t.IsCompleted && !t.IsFaulted && !t.IsCanceled && t.Result != null ? ParseDex(t.Result.Value) : new BlockPet.Feed.DexBook();
            if (t.IsFaulted) Debug.LogWarning("[FirebaseManager] LoadRoomDex failed: " + t.Exception?.GetBaseException()?.Message);
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(book));
        });
    }

    /// <summary>
    /// 圖鑑記一筆（進化那一刻呼叫；進化系統還沒做，目前由 Debug 選單呼叫）。
    /// 用交易寫，兩個人同時記也不會蓋掉；entry.gen ≤ 0 時自動算成「營地第幾代」（目前總筆數 + 1）。
    /// 新的一格：全營地 +<see cref="BlockPet.Feed.EconomyRules.DexNewCellCoins"/> 金幣（帳本記 dex_new_cell）。callback 在主執行緒。
    /// </summary>
    public void RecordDex(string seriesId, string formKey, BlockPet.Feed.DexEntry entry, Action<DexRecordResult> done)
    {
        if (dbRef == null || auth?.CurrentUser == null || string.IsNullOrEmpty(_roomId) || entry == null
            || BlockPet.Feed.DexRules.FindSeries(seriesId) == null || BlockPet.Feed.DexRules.Form(formKey) == null)
        {
            done?.Invoke(new DexRecordResult());
            return;
        }
        var result = new DexRecordResult();
        RoomRoot().Child("dex").RunTransaction(mutable =>
        {
            var root = mutable.Value as Dictionary<string, object> ?? new Dictionary<string, object>();
            var book = ParseDex(root);
            var r = new DexRecordResult { ok = true, isNewCell = book.IsNewCell(seriesId, formKey) };
            r.gen = entry.gen > 0 ? entry.gen : book.TotalEntries() + 1;

            if (!(root.TryGetValue(seriesId, out var so) && so is Dictionary<string, object> series)) root[seriesId] = series = new Dictionary<string, object>();
            if (!(series.TryGetValue(formKey, out var fo) && fo is Dictionary<string, object> form)) series[formKey] = form = new Dictionary<string, object>();
            long at = entry.evolvedAt > 0 ? entry.evolvedAt : ServerNowMs;
            form["g" + r.gen + "_" + at] = new Dictionary<string, object>
            {
                { "gen", (long)r.gen },
                { "at", at },
                { "days", (long)Mathf.Max(0, entry.days) },
                { "pal", string.Join(",", entry.palette ?? new int[5]) },
            };
            book.Add(seriesId, formKey, new BlockPet.Feed.DexEntry { gen = r.gen, evolvedAt = at, days = entry.days, palette = entry.palette });
            r.got = book.Got(seriesId);
            r.seriesComplete = r.isNewCell && book.IsComplete(seriesId);
            result = r;
            mutable.Value = root;
            return TransactionResult.Success(mutable);
        }).ContinueWith(t =>
        {
            bool ok = t.IsCompleted && !t.IsFaulted && !t.IsCanceled;
            if (!ok) Debug.LogWarning("[FirebaseManager] RecordDex failed: " + t.Exception?.GetBaseException()?.Message);
            var r = ok ? result : new DexRecordResult();
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() =>
            {
                if (r.ok && r.isNewCell) AddRoomCoins(BlockPet.Feed.EconomyRules.DexNewCellCoins, "dex_new_cell");
                // 收滿一個系列：送免費抽一次寵物蛋（先記張數，抽蛋 5.24 做好時扣）
                if (r.ok && r.seriesComplete) AddRoomShopItem(BlockPet.Feed.DexRules.FreeEggDrawId, 1, null);
                done?.Invoke(r);
            });
        });
    }

    /// <summary>把 Rooms/{room}/dex 的資料轉成圖鑑（DataSnapshot.Value 或交易裡的 Dictionary 都可以）。</summary>
    static BlockPet.Feed.DexBook ParseDex(object value)
    {
        var book = new BlockPet.Feed.DexBook();
        if (!(value is Dictionary<string, object> root)) return book;
        foreach (var s in root)
        {
            if (!(s.Value is Dictionary<string, object> forms)) continue;
            foreach (var f in forms)
            {
                if (!(f.Value is Dictionary<string, object> entries)) continue;
                foreach (var e in entries)
                {
                    if (!(e.Value is Dictionary<string, object> d)) continue;
                    var entry = new BlockPet.Feed.DexEntry { gen = (int)ReadLong(d, "gen"), evolvedAt = ReadLong(d, "at"), days = (int)ReadLong(d, "days") };
                    if (d.TryGetValue("pal", out var pal) && pal != null)
                    {
                        var parts = pal.ToString().Split(',');
                        for (int i = 0; i < entry.palette.Length && i < parts.Length; i++) int.TryParse(parts[i], out entry.palette[i]);
                    }
                    book.Add(s.Key, f.Key, entry);
                }
            }
        }
        return book;
    }

    /// <summary>目前空間的某個商品數量 +count（交易，避免兩個成員同時買時互相覆蓋）。callback 在主執行緒。</summary>
    public void AddRoomShopItem(string itemId, int count, Action<bool> done)
    {
        if (dbRef == null || auth?.CurrentUser == null || string.IsNullOrEmpty(_roomId) || string.IsNullOrEmpty(itemId))
        {
            done?.Invoke(false);
            return;
        }
        RoomRoot().Child("shop").Child(itemId).RunTransaction(data =>
        {
            long cur = 0;
            try { if (data.Value != null) cur = Convert.ToInt64(data.Value); } catch { }
            data.Value = cur + count;
            return TransactionResult.Success(data);
        }).ContinueWith(t =>
        {
            bool ok = t.IsCompleted && !t.IsFaulted && !t.IsCanceled;
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(ok));
        });
    }

    /// <summary>讀某個空間最新的幾則聊天（舊到新），給選擇空間頁的留言彈幕用。callback 在主執行緒。</summary>
    /// <summary>
    /// 某個空間最新的 count 則文字留言（舊到新），給選空間頁的彈幕用。
    /// 餵食照片的貼文多半沒有文字，所以往前多抓一些再挑有文字的（只抓最後 count 則時，常常全是照片貼文、一則都不剩）。
    /// 還沒登入或查詢失敗時回傳 null（呼叫端不要快取，之後再查）。
    /// </summary>
    public void GetRecentChat(string roomId, int count, Action<List<ChatMessage>> done)
    {
        if (dbRef == null || auth?.CurrentUser == null || string.IsNullOrEmpty(roomId))
        {
            done?.Invoke(null);
            return;
        }
        // 目前空間的聊天室已經在監聽，直接用記憶體裡的（同一路徑再 GetValueAsync 會觸發桌面版 SDK 的斷言）
        if (roomId == RoomId && _chatEntries.Count > 0)
        {
            var mine = new List<ChatMessage>();
            foreach (var e in _chatEntries)
                if (e?.msg != null && !string.IsNullOrWhiteSpace(e.msg.message)) mine.Add(e.msg);
            mine.Sort((a, b) => a.timestamp.CompareTo(b.timestamp));
            if (mine.Count > count) mine.RemoveRange(0, mine.Count - count);
            done?.Invoke(mine);
            return;
        }
        int scan = Mathf.Max(count * 8, 40);
        dbRef.Child("Rooms").Child(roomId).Child("Chat").OrderByChild("timestamp").LimitToLast(scan)
            .GetValueAsync().ContinueWith(t =>
            {
                if (t.IsFaulted || t.IsCanceled || t.Result == null)
                {
                    Debug.LogWarning("[FirebaseManager] GetRecentChat failed: " + (t.Exception?.Flatten().InnerException?.Message ?? "canceled"));
                    lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(null));
                    return;
                }
                var list = new List<ChatMessage>();
                foreach (var child in t.Result.Children)
                {
                    try
                    {
                        string json = child.GetRawJsonValue();
                        var msg = json != null ? JsonUtility.FromJson<ChatMessage>(json) : null;
                        if (msg != null && !string.IsNullOrWhiteSpace(msg.message)) list.Add(msg);
                    }
                    catch { }
                }
                list.Sort((a, b) => a.timestamp.CompareTo(b.timestamp));
                if (list.Count > count) list.RemoveRange(0, list.Count - count);
                lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(list));
            });
    }

    // ── Room members ─────────────────────────────────────────────────────────

    [Serializable]
    public class RoomMemberInfo
    {
        public string uid;
        /// <summary>名字；沒有設定或讀不到是 null。</summary>
        public string nickname;
        /// <summary>帳號顯示文字（已遮掉部分 Email）；舊帳號還沒寫過是 null。</summary>
        public string account;
        /// <summary>列表要顯示的名字：名字 → 帳號 → 「成員」。</summary>
        public string Label => !string.IsNullOrEmpty(nickname) ? nickname : !string.IsNullOrEmpty(account) ? account : "成員";
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

            // 接下來的讀取回到主執行緒才發出：desktop 版 Firebase 在背景執行緒同時開查詢會崩潰
            // （Query.GetValue → DatabaseInternal::AddEventRegistration，2026-10-24 Editor 當掉的原因）。
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => FetchMemberNicknames(uids, done));
        });
    }

    /// <summary>每個成員讀 Users/{uid}/nickname 和 account（Firebase 規則要允許登入的人讀這兩個欄位）。</summary>
    void FetchMemberNicknames(List<string> uids, Action<List<RoomMemberInfo>> done)
    {
        var results = new List<RoomMemberInfo>();
        int remaining = uids.Count;
        foreach (string uid in uids)
        {
            var info = new RoomMemberInfo { uid = uid };
            var user = dbRef.Child(UsersNode).Child(uid);
            int parts = 2;
            void Part()
            {
                if (System.Threading.Interlocked.Decrement(ref parts) != 0) return;
                lock (results)
                {
                    results.Add(info);
                    if (System.Threading.Interlocked.Decrement(ref remaining) == 0)
                    {
                        var final = new List<RoomMemberInfo>(results);
                        lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(final));
                    }
                }
            }
            user.Child("nickname").GetValueAsync().ContinueWith(t =>
            {
                info.nickname = ReadString(t);
                Part();
            });
            user.Child("account").GetValueAsync().ContinueWith(t =>
            {
                info.account = ReadString(t);
                Part();
            });
        }
    }

    static string ReadString(System.Threading.Tasks.Task<DataSnapshot> t)
    {
        try
        {
            if (t.IsCompleted && !t.IsFaulted && !t.IsCanceled && t.Result != null && t.Result.Exists)
            {
                string v = t.Result.Value?.ToString()?.Trim();
                return string.IsNullOrEmpty(v) ? null : v;
            }
        }
        catch { }
        return null;
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
                            UpsertChatEntry(ParseChatEntry(child));
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
                    // 按 emoji（reactions 變了）與收回訊息
                    _chatChildChangedHandler = (s3, a3) => HandleChatChildChanged(a3, qForHandler);
                    _chatChildRemovedHandler = (s4, a4) => HandleChatChildRemoved(a4, qForHandler);
                    _chatQuery.ChildChanged += _chatChildChangedHandler;
                    _chatQuery.ChildRemoved += _chatChildRemovedHandler;
                });
            }
        });
    }

    // SharedPhotos 底下 fileName → push key（補寫類別用）
    readonly Dictionary<string, string> _photoKeys = new Dictionary<string, string>();

    /// <summary>
    /// 舊照片補辨識（或類別版本更新後重新辨識）後，把類別寫回營地照片記錄（SharedPhotos）與聊天室的餵食貼文，房間其他人同步下來就有類別。
    /// 每個人辨識的結果都一樣（同一個模型），重複寫也沒關係。
    /// </summary>
    public void UpdatePhotoCategory(PhotoMeta meta)
    {
        if (dbRef == null || string.IsNullOrEmpty(_roomId) || meta == null || string.IsNullOrEmpty(meta.category)) return;
        string key;
        lock (_photoKeys) _photoKeys.TryGetValue(meta.fileName, out key);
        if (!string.IsNullOrEmpty(key))
        {
            var rec = RoomRoot().Child("SharedPhotos").Child(key);
            rec.Child("category").SetValueAsync(meta.category);
            rec.Child("categoryConfidence").SetValueAsync((double)meta.categoryConfidence);
            rec.Child("categoryVersion").SetValueAsync(meta.categoryVersion ?? "");
        }
        foreach (var e in ChatEntries)
        {
            if (e == null || !e.IsPost || e.msg.photoFileName != meta.fileName || e.msg.category == meta.category) continue;
            RoomRoot().Child("Chat").Child(e.key).Child("category").SetValueAsync(meta.category);
        }
    }

    [Serializable]
    class CloudPhotoRecord
    {
        public string fileName;
        public string timestamp;
        public string takerAvatarFileName;
        public string userId;
        public string displayName;
        public string category;
        public float categoryConfidence;
        public string categoryVersion;
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
        // 記下是誰拍的（相簿「誰拍的」篩選）
        meta.takerUserId = auth.CurrentUser.UserId;
        meta.takerName = GetDisplayName();
        SaveManager.Instance?.Save();

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
            category = meta.category,
            categoryConfidence = meta.categoryConfidence,
            categoryVersion = meta.categoryVersion,
            imageBase64Jpg = Convert.ToBase64String(jpg)
        };

        string json = JsonUtility.ToJson(rec);
        RoomRoot().Child("SharedPhotos").Push().SetRawJsonValueAsync(json).ContinueWith(t =>
        {
            if (t.IsFaulted)
                Debug.LogError("[FirebaseManager] Upload photo failed: " + (t.Exception?.Flatten().InnerException?.Message ?? t.Exception?.ToString()));
        });

        // 聊天室也出現一則「餵了 寵物 一張照片」，大家可以按 emoji、回覆
        SendChatMessage(new ChatMessage(GetDisplayName(), meta.caption ?? "")
        {
            kind = "post",
            photoFileName = meta.fileName,
            category = meta.category,
            senderId = auth.CurrentUser.UserId,
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

        lock (_photoKeys) _photoKeys[rec.fileName] = args.Snapshot.Key;

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
                meta.takerUserId = rec.userId;
                meta.takerName = rec.displayName;
                meta.category = rec.category;
                meta.categoryConfidence = rec.categoryConfidence;
                meta.categoryVersion = rec.categoryVersion;
                SaveManager.Instance.ImportPhotoFromCloud(meta, bytes);
            });
        }
    }

    [Serializable]
    public class RoomPetState
    {
        public float currentHealth;   // 舊版 24 小時秒數制（已不用，保留讀舊資料）
        public string lastUpdateTime;
        public int petIndex;
        public int startingPhotoCount;
        public float hearts;          // 上次餵食（或復活）時的愛心（0～5，半顆）
        public long heartsAt;         // 那時的伺服器時間（Unix 毫秒）；0＝舊資料，載入時補成 5 顆
    }

    DatabaseReference RoomRoot()
    {
        return dbRef.Child("Rooms").Child(RoomId);
    }

    public void SetRoomId(string roomId)
    {
        string normalized = roomId?.Trim() ?? "";
        if (normalized.Length > 32) normalized = normalized.Substring(0, 32);

        // 同一個房間（例如從房間列表點回目前的房間）：不要停掉再重開監聽。
        // 同一路徑立刻 unlisten + listen 會觸發 Firebase desktop SDK 的
        // persistent_connection.cc listens_.find(query_spec) 斷言，也是多餘的重新下載。
        if (normalized == _roomId && !string.IsNullOrEmpty(normalized))
        {
            SaveManager.Instance?.SwitchRoom(normalized); // 保險：存檔停在別的空間時切回來（同一個空間不會做事）
            TryStartListeningForMessages();
            TryStartListeningForPhotos();
            TryStartListeningForPetState();
            TryStartListeningForRoomEquipState();
            TryStartListeningForRoomCoins();
            OnRoomChanged?.Invoke();
            return;
        }

        _roomId = normalized;
        PlayerPrefs.SetString(PrefsRoomId, _roomId);
        PlayerPrefs.Save();

        // Stop all listeners first so _petState = null before OnRoomSwitched fires.
        // This prevents PetCollectionManager.EnsureValidState() from publishing
        // stale data to Firebase while HasRoomPetState is still true.
        ResetChatListenState();
        StopCampFriends();
        StopTravelState();
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
        _petInitRequested = false;
        _petRef = RoomRoot().Child("petState");
        // 節點不存在時由 HandlePetStateChanged 建立預設值。不要在同一路徑另外 GetValueAsync：
        // desktop SDK 對已監聽的路徑再讀一次會觸發 persistent_connection.cc listens_.find 斷言。
        _petRef.ValueChanged += HandlePetStateChanged;
        StartMetaListening();
    }

    // ─── 目前空間的名字（寵物名字、空間名稱）：空間資訊改名後首頁、聊天室、轉盤立刻更新 ───

    DatabaseReference _metaRef;
    volatile RoomMeta _roomMeta;
    /// <summary>目前空間的寵物名字（空間資訊可改）；還沒設定時是 null。</summary>
    public string CurrentPetName => _roomMeta != null && !string.IsNullOrWhiteSpace(_roomMeta.petName) ? _roomMeta.petName : null;
    /// <summary>目前空間的名稱；還沒載入時是 null。</summary>
    public string CurrentRoomName => _roomMeta != null && !string.IsNullOrWhiteSpace(_roomMeta.name) ? _roomMeta.name : null;
    /// <summary>目前營地的營地代碼；還沒載入或還在產生時是 null。</summary>
    public string CurrentCampCode => _roomMeta != null && !string.IsNullOrWhiteSpace(_roomMeta.campCode) ? _roomMeta.campCode : null;
    /// <summary>目前空間的名字有變（主執行緒）。</summary>
    public static event Action OnRoomMetaChanged;

    void StartMetaListening()
    {
        if (_metaRef != null || string.IsNullOrEmpty(_roomId) || dbRef == null) return;
        _metaRef = RoomRoot().Child("meta");
        _metaRef.ValueChanged += HandleMetaChanged;
    }

    void StopMetaListening()
    {
        if (_metaRef != null) _metaRef.ValueChanged -= HandleMetaChanged;
        _metaRef = null;
        _roomMeta = null;
    }

    void HandleMetaChanged(object sender, ValueChangedEventArgs args)
    {
        if (args.DatabaseError != null || args.Snapshot == null || !args.Snapshot.Exists) return;
        RoomMeta meta = null;
        try { meta = JsonUtility.FromJson<RoomMeta>(args.Snapshot.GetRawJsonValue()); } catch { }
        if (meta == null) return;
        _roomMeta = meta;
        lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() =>
        {
            // 舊空間（或剛建立的空間）還沒有營地代碼：補一個
            if (string.IsNullOrWhiteSpace(meta.campCode)) EnsureCampCode(_roomId);
            OnRoomMetaChanged?.Invoke();
        });
    }

    // ─── 營地代碼（設計文件 5.15 ③-2、5.21 ①）────────────────────────
    // CampCodes/{代碼} = 空間 id（加營地好友時用代碼找營地；交易保證不重複）；Rooms/{room}/meta/campCode = 代碼。

    const string CampCodesNode = "CampCodes";
    const string CampCodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // 和邀請碼一樣不用 0/O/1/I
    public const int CampCodeLength = 6;
    string _ensuringCampCodeFor;

    static string NewCampCode()
    {
        var chars = new char[CampCodeLength];
        for (int i = 0; i < chars.Length; i++) chars[i] = CampCodeAlphabet[UnityEngine.Random.Range(0, CampCodeAlphabet.Length)];
        return new string(chars);
    }

    /// <summary>幫空間產生營地代碼（搶到沒人用的代碼才寫進 meta；最多試 5 次）。</summary>
    void EnsureCampCode(string roomId, int attempt = 0)
    {
        if (dbRef == null || auth?.CurrentUser == null || string.IsNullOrEmpty(roomId)) return;
        if (attempt == 0) { if (_ensuringCampCodeFor == roomId) return; _ensuringCampCodeFor = roomId; }
        if (attempt >= 5) { _ensuringCampCodeFor = null; Debug.LogWarning("[FirebaseManager] 營地代碼產生失敗（試了 5 次）"); return; }
        string code = NewCampCode();
        bool claimed = false;
        dbRef.Child(CampCodesNode).Child(code).RunTransaction(data =>
        {
            if (data.Value != null && data.Value.ToString() != roomId) { claimed = false; return TransactionResult.Abort(); }
            data.Value = roomId;
            claimed = true;
            return TransactionResult.Success(data);
        }).ContinueWith(t =>
        {
            bool ok = t.IsCompleted && !t.IsFaulted && !t.IsCanceled && claimed;
            if (t.IsFaulted) Debug.LogWarning("[FirebaseManager] 營地代碼登記失敗（Firebase 規則要允許寫 CampCodes）：" + t.Exception?.GetBaseException()?.Message);
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() =>
            {
                if (t.IsFaulted) { _ensuringCampCodeFor = null; return; }
                if (!ok) { EnsureCampCode(roomId, attempt + 1); return; } // 代碼被用了：換一組
                dbRef.Child("Rooms").Child(roomId).Child("meta").Child("campCode").SetValueAsync(code).ContinueWith(t2 =>
                {
                    if (t2.IsFaulted) Debug.LogWarning("[FirebaseManager] 寫入營地代碼失敗：" + t2.Exception?.GetBaseException()?.Message);
                    lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => _ensuringCampCodeFor = null);
                });
            });
        });
    }

    // ─── 營地好友（設計文件 5.21 ①、流程 1～3）────────────────────────
    // Rooms/{對方}/friendInvites/{我} = 邀請；Rooms/{A}/friends/{B} = 好友（slot＝地圖上的格子，放好就固定）。
    // 同意時兩邊一起寫；對方那邊的 slot 先寫 -1，由對方營地的成員下次載入時自己分格子（看不到別的營地的好友清單）。

    [Serializable]
    public class CampFriend
    {
        public string roomId;
        /// <summary>地圖上的格子 0～9；-1＝還沒分（對方同意後，等我們這邊分）。</summary>
        public int slot = -1;
        public long since;
        /// <summary>加好友時的營地名字（之後從對方 meta 更新顯示）。</summary>
        public string name;
        /// <summary>去過幾趟（派寵物旅行做好後才會增加）。</summary>
        public int trips;
    }

    [Serializable]
    public class FriendInvite
    {
        public string fromRoom;
        public string roomName;
        public string petName;
        public int petIndex;
        public int members;
        public string sender;
        public long sentAt;
    }

    public static event Action OnCampFriendsChanged;
    public static event Action OnFriendInvitesChanged;

    readonly List<CampFriend> _campFriends = new List<CampFriend>();
    readonly List<FriendInvite> _friendInvites = new List<FriendInvite>();
    DatabaseReference _friendsRef, _invitesRef;
    string _friendsRoom;
    bool _assigningSlots;

    /// <summary>目前營地的營地好友（依格子排）。要先 <see cref="StartCampFriends"/>。</summary>
    public IReadOnlyList<CampFriend> CampFriends => _campFriends;
    /// <summary>目前營地收到的好友邀請（新的在前）。</summary>
    public IReadOnlyList<FriendInvite> FriendInvites => _friendInvites;
    /// <summary>好友資料是否已經讀到（避免一開始空清單時誤顯示「剛開始」）。</summary>
    public bool CampFriendsLoaded { get; private set; }

    /// <summary>開始聽目前營地的好友與好友邀請（打開旅行地圖時呼叫；換空間會自動換）。</summary>
    public void StartCampFriends()
    {
        if (dbRef == null || auth?.CurrentUser == null || string.IsNullOrEmpty(_roomId)) return;
        if (_friendsRoom == _roomId && _friendsRef != null) return;
        StopCampFriends();
        _friendsRoom = _roomId;
        _friendsRef = RoomRoot().Child("friends");
        _invitesRef = RoomRoot().Child("friendInvites");
        _friendsRef.ValueChanged += HandleFriendsChanged;
        _invitesRef.ValueChanged += HandleInvitesChanged;
    }

    void StopCampFriends()
    {
        if (_friendsRef != null) _friendsRef.ValueChanged -= HandleFriendsChanged;
        if (_invitesRef != null) _invitesRef.ValueChanged -= HandleInvitesChanged;
        _friendsRef = _invitesRef = null;
        _friendsRoom = null;
        _campFriends.Clear();
        _friendInvites.Clear();
        CampFriendsLoaded = false;
    }

    static long ReadLongObj(object v) { try { return v == null ? 0 : Convert.ToInt64(v); } catch { return 0; } }

    void HandleFriendsChanged(object sender, ValueChangedEventArgs e)
    {
        if (e.DatabaseError != null) { Debug.LogWarning("[FirebaseManager] 讀營地好友失敗（Firebase 規則）：" + e.DatabaseError.Message); return; }
        var list = new List<CampFriend>();
        if (e.Snapshot != null && e.Snapshot.Exists)
            foreach (var c in e.Snapshot.Children)
            {
                var d = c.Value as Dictionary<string, object>;
                list.Add(new CampFriend
                {
                    roomId = c.Key,
                    slot = d != null && d.ContainsKey("slot") ? (int)ReadLongObj(d["slot"]) : -1,
                    since = d != null && d.ContainsKey("since") ? ReadLongObj(d["since"]) : 0,
                    name = d != null && d.TryGetValue("name", out var n) ? n?.ToString() : null,
                    trips = d != null && d.ContainsKey("trips") ? (int)ReadLongObj(d["trips"]) : 0,
                });
            }
        list.Sort((a, b) => a.slot != b.slot ? a.slot.CompareTo(b.slot) : a.since.CompareTo(b.since));
        string room = _friendsRoom;
        lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() =>
        {
            if (room != _friendsRoom) return;
            _campFriends.Clear();
            _campFriends.AddRange(list);
            CampFriendsLoaded = true;
            if (list.Exists(f => f.slot < 0)) AssignPendingSlots();
            OnCampFriendsChanged?.Invoke();
        });
    }

    void HandleInvitesChanged(object sender, ValueChangedEventArgs e)
    {
        if (e.DatabaseError != null) { Debug.LogWarning("[FirebaseManager] 讀好友邀請失敗：" + e.DatabaseError.Message); return; }
        var list = new List<FriendInvite>();
        if (e.Snapshot != null && e.Snapshot.Exists)
            foreach (var c in e.Snapshot.Children)
            {
                FriendInvite inv = null;
                try { inv = JsonUtility.FromJson<FriendInvite>(c.GetRawJsonValue()); } catch { }
                if (inv == null) continue;
                inv.fromRoom = c.Key;
                list.Add(inv);
            }
        list.Sort((a, b) => b.sentAt.CompareTo(a.sentAt));
        string room = _friendsRoom;
        lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() =>
        {
            if (room != _friendsRoom) return;
            _friendInvites.Clear();
            _friendInvites.AddRange(list);
            OnFriendInvitesChanged?.Invoke();
        });
    }

    /// <summary>對方同意後，我們這邊的 slot 是 -1：照加入時間依序分到還沒用的最小格子（交易，避免兩台手機分到同一格）。</summary>
    void AssignPendingSlots()
    {
        if (_assigningSlots || _friendsRef == null) return;
        _assigningSlots = true;
        _friendsRef.RunTransaction(data =>
        {
            var root = data.Value as Dictionary<string, object>;
            if (root == null) return TransactionResult.Success(data);
            var used = new List<int>();
            var pending = new List<KeyValuePair<string, Dictionary<string, object>>>();
            foreach (var kv in root)
            {
                if (!(kv.Value is Dictionary<string, object> d)) continue;
                int slot = d.ContainsKey("slot") ? (int)ReadLongObj(d["slot"]) : -1;
                if (slot >= 0) used.Add(slot); else pending.Add(new KeyValuePair<string, Dictionary<string, object>>(kv.Key, d));
            }
            pending.Sort((a, b) => ReadLongObj(a.Value.ContainsKey("since") ? a.Value["since"] : null).CompareTo(ReadLongObj(b.Value.ContainsKey("since") ? b.Value["since"] : null)));
            foreach (var p in pending)
            {
                int slot = BlockPet.Travel.CampFriendRules.NextFreeSlot(used);
                if (slot < 0) break; // 滿了：留著 -1，不顯示在地圖上
                p.Value["slot"] = (long)slot;
                used.Add(slot);
            }
            data.Value = root;
            return TransactionResult.Success(data);
        }).ContinueWith(t =>
        {
            if (t.IsFaulted) Debug.LogWarning("[FirebaseManager] 分配好友格子失敗：" + t.Exception?.GetBaseException()?.Message);
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => _assigningSlots = false);
        });
    }

    /// <summary>
    /// 送出營地好友邀請（輸入對方營地代碼）。done(錯誤提示)：成功是 null；失敗是要顯示的文字（CampFriendRules 的提示）。主執行緒。
    /// </summary>
    public void SendFriendInvite(string code, Action<string> done)
    {
        string me = _roomId;
        if (dbRef == null || auth?.CurrentUser == null || string.IsNullOrEmpty(me)) { done?.Invoke("還沒登入或沒有進空間"); return; }
        FindRoomByCampCode(code, target =>
        {
            var friendIds = new List<string>();
            foreach (var f in _campFriends) friendIds.Add(f.roomId);
            string err = BlockPet.Travel.CampFriendRules.Check(target, me, friendIds, false);
            if (err != null) { done?.Invoke(err); return; }
            var inviteRef = dbRef.Child("Rooms").Child(target).Child("friendInvites").Child(me);
            inviteRef.GetValueAsync().ContinueWith(t =>
            {
                bool invited = t.IsCompleted && !t.IsFaulted && t.Result != null && t.Result.Exists;
                lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() =>
                {
                    if (invited) { done?.Invoke(BlockPet.Travel.CampFriendRules.MsgAlreadyInvited); return; }
                    GetRoomMembers(me, members =>
                    {
                        var inv = new FriendInvite
                        {
                            fromRoom = me,
                            roomName = CurrentRoomName ?? me,
                            petName = CurrentPetName ?? "",
                            petIndex = PetCollectionManager.Instance != null ? PetCollectionManager.Instance.CurrentPetIndex : 0,
                            members = members != null ? members.Count : 1,
                            sender = HasDisplayName ? GetDisplayName() : "",
                            sentAt = ServerNowMs,
                        };
                        inviteRef.SetRawJsonValueAsync(JsonUtility.ToJson(inv)).ContinueWith(t2 =>
                        {
                            bool ok = t2.IsCompleted && !t2.IsFaulted && !t2.IsCanceled;
                            if (!ok) Debug.LogWarning("[FirebaseManager] 送出好友邀請失敗（Firebase 規則要允許 friendInvites）：" + t2.Exception?.GetBaseException()?.Message);
                            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(ok ? null : "送出失敗，請再試一次"));
                        });
                    });
                });
            });
        });
    }

    /// <summary>同意好友邀請：我們這邊分一格、對方那邊先寫 -1（對方自己分），刪掉邀請。done(成功) 在主執行緒。</summary>
    public void AcceptFriendInvite(FriendInvite inv, Action<bool> done)
    {
        string me = _roomId;
        if (dbRef == null || inv == null || string.IsNullOrEmpty(me) || string.IsNullOrEmpty(inv.fromRoom)) { done?.Invoke(false); return; }
        var used = new List<int>();
        foreach (var f in _campFriends) used.Add(f.slot);
        if (_campFriends.Count >= BlockPet.Travel.CampFriendRules.MaxFriends) { done?.Invoke(false); return; }
        int slot = BlockPet.Travel.CampFriendRules.NextFreeSlot(used);
        long now = ServerNowMs;
        var updates = new Dictionary<string, object>
        {
            [$"Rooms/{me}/friends/{inv.fromRoom}"] = new Dictionary<string, object> { { "slot", (long)slot }, { "since", now }, { "name", inv.roomName ?? "" }, { "trips", 0L } },
            [$"Rooms/{inv.fromRoom}/friends/{me}"] = new Dictionary<string, object> { { "slot", -1L }, { "since", now }, { "name", CurrentRoomName ?? me }, { "trips", 0L } },
            [$"Rooms/{me}/friendInvites/{inv.fromRoom}"] = null,
        };
        dbRef.UpdateChildrenAsync(updates).ContinueWith(t =>
        {
            bool ok = t.IsCompleted && !t.IsFaulted && !t.IsCanceled;
            if (!ok) Debug.LogWarning("[FirebaseManager] 同意好友邀請失敗（Firebase 規則要允許 friends）：" + t.Exception?.GetBaseException()?.Message);
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(ok));
        });
    }

    /// <summary>不要：直接刪掉邀請（不通知對方，2026-11-06 定）。</summary>
    public void DeclineFriendInvite(FriendInvite inv, Action<bool> done)
    {
        if (dbRef == null || inv == null || string.IsNullOrEmpty(_roomId)) { done?.Invoke(false); return; }
        RoomRoot().Child("friendInvites").Child(inv.fromRoom).RemoveValueAsync().ContinueWith(t =>
        {
            bool ok = t.IsCompleted && !t.IsFaulted && !t.IsCanceled;
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(ok));
        });
    }

    // ─── 派寵物旅行（設計文件 5.21 ③；流程 5～12）──────────────────────
    // Rooms/{我}/trip：旅行中（一次只能一趟）；Rooms/{對方}/guests/{我}：對方營地看到的客人；
    // Rooms/{我}/passport/{對方}：護照的章；Rooms/{我}/tripLast：最近一次回家（每個成員各看一次蓋章卡）。

    [Serializable]
    public class TravelTrip
    {
        public string to;
        public string toName;
        public string toPet;
        public int toSlot;
        public long departAt;
        public int oneWayMin;
        public int stayMin = BlockPet.Travel.TripRules.StayMinutes;
        public string by;
        public string petName;
        public long HomeAt => BlockPet.Travel.TripRules.HomeAt(departAt, oneWayMin, stayMin);
    }

    [Serializable]
    public class TravelGuest
    {
        public string from;
        public string fromName;
        public string petName;
        public int petIndex;
        public long arriveAt;
        public long leaveAt;
    }

    [Serializable]
    public class PassportStamp
    {
        public string roomId;
        public string name;
        public long first;
        public int times;
    }

    [Serializable]
    public class TripReturn
    {
        public string to;
        public string toName;
        public long at;
        public int photos;
        public int stampNo;
    }

    public static event Action OnTravelChanged;

    DatabaseReference _tripRef, _guestsRef, _passportRef, _tripLastRef;
    string _travelRoom;
    public TravelTrip CurrentTrip { get; private set; }
    public TripReturn LastTripReturn { get; private set; }
    readonly List<TravelGuest> _guests = new List<TravelGuest>();
    readonly List<PassportStamp> _passport = new List<PassportStamp>();
    public IReadOnlyList<TravelGuest> Guests => _guests;
    public IReadOnlyList<PassportStamp> Passport => _passport;
    bool _finishingTrip;

    /// <summary>開始聽目前營地的旅行、客人、護照（首頁和旅行地圖都會用；換空間會自動換）。</summary>
    public void StartTravelState()
    {
        if (dbRef == null || auth?.CurrentUser == null || string.IsNullOrEmpty(_roomId)) return;
        if (_travelRoom == _roomId && _tripRef != null) return;
        StopTravelState();
        _travelRoom = _roomId;
        _tripRef = RoomRoot().Child("trip");
        _guestsRef = RoomRoot().Child("guests");
        _passportRef = RoomRoot().Child("passport");
        _tripLastRef = RoomRoot().Child("tripLast");
        _tripRef.ValueChanged += HandleTripChanged;
        _guestsRef.ValueChanged += HandleGuestsChanged;
        _passportRef.ValueChanged += HandlePassportChanged;
        _tripLastRef.ValueChanged += HandleTripLastChanged;
    }

    void StopTravelState()
    {
        if (_tripRef != null) _tripRef.ValueChanged -= HandleTripChanged;
        if (_guestsRef != null) _guestsRef.ValueChanged -= HandleGuestsChanged;
        if (_passportRef != null) _passportRef.ValueChanged -= HandlePassportChanged;
        if (_tripLastRef != null) _tripLastRef.ValueChanged -= HandleTripLastChanged;
        _tripRef = _guestsRef = _passportRef = _tripLastRef = null;
        _travelRoom = null;
        CurrentTrip = null;
        LastTripReturn = null;
        _guests.Clear();
        _passport.Clear();
    }

    void TravelEvent(string room, Action apply)
    {
        lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() =>
        {
            if (room != _travelRoom) return;
            apply();
            OnTravelChanged?.Invoke();
        });
    }

    void HandleTripChanged(object sender, ValueChangedEventArgs e)
    {
        if (e.DatabaseError != null) { Debug.LogWarning("[FirebaseManager] 讀旅行失敗：" + e.DatabaseError.Message); return; }
        TravelTrip trip = null;
        if (e.Snapshot != null && e.Snapshot.Exists) try { trip = JsonUtility.FromJson<TravelTrip>(e.Snapshot.GetRawJsonValue()); } catch { }
        TravelEvent(_travelRoom, () => CurrentTrip = trip);
    }

    void HandleGuestsChanged(object sender, ValueChangedEventArgs e)
    {
        if (e.DatabaseError != null) return;
        var list = new List<TravelGuest>();
        if (e.Snapshot != null && e.Snapshot.Exists)
            foreach (var c in e.Snapshot.Children)
            {
                TravelGuest g = null;
                try { g = JsonUtility.FromJson<TravelGuest>(c.GetRawJsonValue()); } catch { }
                if (g == null) continue;
                g.from = c.Key;
                list.Add(g);
            }
        list.Sort((a, b) => a.arriveAt.CompareTo(b.arriveAt)); // 最早到的在前（只顯示這一隻）
        TravelEvent(_travelRoom, () => { _guests.Clear(); _guests.AddRange(list); });
    }

    void HandlePassportChanged(object sender, ValueChangedEventArgs e)
    {
        if (e.DatabaseError != null) return;
        var list = new List<PassportStamp>();
        if (e.Snapshot != null && e.Snapshot.Exists)
            foreach (var c in e.Snapshot.Children)
            {
                PassportStamp st = null;
                try { st = JsonUtility.FromJson<PassportStamp>(c.GetRawJsonValue()); } catch { }
                if (st == null) continue;
                st.roomId = c.Key;
                list.Add(st);
            }
        list.Sort((a, b) => a.first.CompareTo(b.first));
        TravelEvent(_travelRoom, () => { _passport.Clear(); _passport.AddRange(list); });
    }

    void HandleTripLastChanged(object sender, ValueChangedEventArgs e)
    {
        if (e.DatabaseError != null) return;
        TripReturn r = null;
        if (e.Snapshot != null && e.Snapshot.Exists) try { r = JsonUtility.FromJson<TripReturn>(e.Snapshot.GetRawJsonValue()); } catch { }
        TravelEvent(_travelRoom, () => LastTripReturn = r);
    }

    /// <summary>
    /// 派寵物去好友營地。先用交易佔住 trip（已經有人派出去就失敗），再在對方營地留客人資料。done(錯誤提示) 主執行緒，成功是 null。
    /// </summary>
    public void SendPetOnTrip(CampFriend friend, int oneWayMin, Action<string> done)
    {
        string me = _roomId;
        if (dbRef == null || auth?.CurrentUser == null || string.IsNullOrEmpty(me) || friend == null) { done?.Invoke("還沒登入或沒有進空間"); return; }
        // 對方寵物的名字（給「在對方營地」畫面用）
        var petNameTask = friend.roomId.StartsWith("DEBUG_")
            ? System.Threading.Tasks.Task.FromResult<DataSnapshot>(null)
            : dbRef.Child("Rooms").Child(friend.roomId).Child("meta").Child("petName").GetValueAsync();
        petNameTask.ContinueWith(tp =>
        {
            string toPet = tp.IsCompleted && !tp.IsFaulted && tp.Result != null && tp.Result.Exists ? tp.Result.Value?.ToString() : null;
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() =>
            {
                long now = ServerNowMs;
                var trip = new TravelTrip
                {
                    to = friend.roomId, toName = friend.name, toPet = toPet ?? "", toSlot = friend.slot,
                    departAt = now, oneWayMin = oneWayMin, stayMin = BlockPet.Travel.TripRules.StayMinutes,
                    by = GetUserId(), petName = CurrentPetName ?? "",
                };
                var tripDict = TripToDict(trip);
                bool took = false;
                RoomRoot().Child("trip").RunTransaction(data =>
                {
                    if (data.Value != null) { took = false; return TransactionResult.Abort(); }
                    data.Value = tripDict;
                    took = true;
                    return TransactionResult.Success(data);
                }).ContinueWith(t =>
                {
                    bool ok = t.IsCompleted && !t.IsFaulted && !t.IsCanceled && took;
                    if (t.IsFaulted) Debug.LogWarning("[FirebaseManager] 派寵物失敗：" + t.Exception?.GetBaseException()?.Message);
                    lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() =>
                    {
                        if (!ok) { done?.Invoke(t.IsFaulted ? "出發失敗，請再試一次" : "寵物已經出門了"); return; }
                        var guest = new TravelGuest
                        {
                            fromName = CurrentRoomName ?? me, petName = trip.petName,
                            petIndex = PetCollectionManager.Instance != null ? PetCollectionManager.Instance.CurrentPetIndex : 0,
                            arriveAt = BlockPet.Travel.TripRules.ArriveAt(now, oneWayMin),
                            leaveAt = BlockPet.Travel.TripRules.LeaveAt(now, oneWayMin),
                        };
                        if (!friend.roomId.StartsWith("DEBUG_")) // 測試用的假好友沒有真的營地
                        dbRef.Child("Rooms").Child(friend.roomId).Child("guests").Child(me).SetRawJsonValueAsync(JsonUtility.ToJson(guest)).ContinueWith(tg =>
                        {
                            if (tg.IsFaulted) Debug.LogWarning("[FirebaseManager] 對方營地的客人資料寫入失敗（Firebase 規則要允許 guests）：" + tg.Exception?.GetBaseException()?.Message);
                        });
                        done?.Invoke(null);
                    });
                });
            });
        });
    }

    /// <summary>（測試用）把目前的旅行往前推 ms 毫秒（出發時間提早），不用真的等 1～3 小時。</summary>
    public void DebugShiftTrip(long ms)
    {
        var t = CurrentTrip;
        if (t == null || dbRef == null || string.IsNullOrEmpty(_roomId)) return;
        RoomRoot().Child("trip").Child("departAt").SetValueAsync(t.departAt - ms);
    }

    /// <summary>（測試用）不用另一個營地，直接加一個假的營地好友（id 以 DEBUG_ 開頭）；clear＝清掉全部假好友。</summary>
    public void DebugFriend(bool clear)
    {
        if (dbRef == null || string.IsNullOrEmpty(_roomId)) return;
        var friends = RoomRoot().Child("friends");
        if (clear)
        {
            foreach (var f in _campFriends) if (f.roomId.StartsWith("DEBUG_")) friends.Child(f.roomId).RemoveValueAsync();
            return;
        }
        var used = new List<int>();
        foreach (var f in _campFriends) used.Add(f.slot);
        int slot = BlockPet.Travel.CampFriendRules.NextFreeSlot(used);
        if (slot < 0) { Debug.LogWarning("[FirebaseManager] 營地好友已滿 10 個"); return; }
        string[] names = { "小黑的辦公室營地", "海邊小屋", "山上的家", "阿嬤的菜園", "團子的小窩" };
        string id = "DEBUG_FRIEND_" + slot;
        friends.Child(id).SetValueAsync(new Dictionary<string, object>
        {
            { "slot", (long)slot }, { "since", ServerNowMs }, { "name", "（測試）" + names[slot % names.Length] }, { "trips", 0L },
        });
    }

    /// <summary>（測試用）假裝有別的營地的寵物來玩 30 分鐘；clear＝清掉。</summary>
    public void DebugGuest(bool clear)
    {
        if (dbRef == null || string.IsNullOrEmpty(_roomId)) return;
        var r = RoomRoot().Child("guests").Child("DEBUG_GUEST");
        if (clear) { r.RemoveValueAsync(); return; }
        long now = ServerNowMs;
        var g = new TravelGuest { fromName = "測試營地", petName = "小黑", arriveAt = now, leaveAt = now + 30L * 60 * 1000 };
        r.SetRawJsonValueAsync(JsonUtility.ToJson(g));
    }

    static Dictionary<string, object> TripToDict(TravelTrip t) => new Dictionary<string, object>
    {
        { "to", t.to ?? "" }, { "toName", t.toName ?? "" }, { "toPet", t.toPet ?? "" }, { "toSlot", (long)t.toSlot },
        { "departAt", t.departAt }, { "oneWayMin", (long)t.oneWayMin }, { "stayMin", (long)t.stayMin },
        { "by", t.by ?? "" }, { "petName", t.petName ?? "" },
    };

    static TravelTrip TripFromDict(Dictionary<string, object> d)
    {
        if (d == null) return null;
        string Str(string k) => d.TryGetValue(k, out var v) && v != null ? v.ToString() : "";
        return new TravelTrip
        {
            to = Str("to"), toName = Str("toName"), toPet = Str("toPet"), by = Str("by"), petName = Str("petName"),
            toSlot = (int)ReadLong(d, "toSlot"), departAt = ReadLong(d, "departAt"),
            oneWayMin = (int)ReadLong(d, "oneWayMin"), stayMin = (int)Math.Max(1, ReadLong(d, "stayMin")),
        };
    }

    /// <summary>
    /// 回家蓋章：旅行時間到了，誰先打開 App 就由誰結算一次（交易拿走 trip，拿到的人才做）。
    /// 好友路 +1 趟、護照 +1 章、刪掉對方營地的客人、寫 tripLast（每個成員各看一次蓋章卡）。
    /// photos＝旅行時大家餵的照片張數（呼叫的人從相簿算好）。
    /// </summary>
    public void FinishTripIfHome(Func<long, long, int> countPhotos)
    {
        var trip = CurrentTrip;
        if (trip == null || _finishingTrip || dbRef == null || string.IsNullOrEmpty(_roomId)) return;
        long now = ServerNowMs;
        if (now < trip.HomeAt) return;
        _finishingTrip = true;
        string me = _roomId;
        bool claimed = false;
        TravelTrip claimedTrip = null;
        RoomRoot().Child("trip").RunTransaction(data =>
        {
            claimed = false;
            if (data.Value == null) return TransactionResult.Abort();
            var t0 = TripFromDict(data.Value as Dictionary<string, object>);
            if (t0 == null || ServerNowMs < t0.HomeAt) return TransactionResult.Abort();
            claimedTrip = t0;
            claimed = true;
            data.Value = null;
            return TransactionResult.Success(data);
        }).ContinueWith(t =>
        {
            bool ok = t.IsCompleted && !t.IsFaulted && !t.IsCanceled && claimed && claimedTrip != null;
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() =>
            {
                _finishingTrip = false;
                if (!ok || me != _roomId) return;
                var tr = claimedTrip;
                int photos = countPhotos != null ? countPhotos(tr.departAt, tr.HomeAt) : 0;
                // 好友路 +1 趟
                RoomRoot().Child("friends").Child(tr.to).Child("trips").RunTransaction(d =>
                {
                    long cur = 0; try { if (d.Value != null) cur = Convert.ToInt64(d.Value); } catch { }
                    d.Value = cur + 1;
                    return TransactionResult.Success(d);
                });
                // 護照 +1 章，順便算出這是第幾個章
                int stampNo = 1;
                foreach (var st in _passport) stampNo += Mathf.Max(0, st.times);
                RoomRoot().Child("passport").Child(tr.to).RunTransaction(d =>
                {
                    var dict = d.Value as Dictionary<string, object> ?? new Dictionary<string, object>();
                    long times = 0; try { if (dict.ContainsKey("times")) times = Convert.ToInt64(dict["times"]); } catch { }
                    dict["times"] = times + 1;
                    if (!dict.ContainsKey("first")) dict["first"] = tr.departAt;
                    dict["name"] = tr.toName ?? "";
                    d.Value = dict;
                    return TransactionResult.Success(d);
                });
                if (!tr.to.StartsWith("DEBUG_")) dbRef.Child("Rooms").Child(tr.to).Child("guests").Child(me).RemoveValueAsync();
                var last = new TripReturn { to = tr.to, toName = tr.toName, at = tr.HomeAt, photos = photos, stampNo = stampNo };
                RoomRoot().Child("tripLast").SetRawJsonValueAsync(JsonUtility.ToJson(last));
            });
        });
    }

    /// <summary>用營地代碼找營地（加營地好友用）。回傳空間 id，找不到是 null。callback 在主執行緒。</summary>
    public void FindRoomByCampCode(string code, Action<string> done)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        if (dbRef == null || auth?.CurrentUser == null || code.Length != CampCodeLength) { done?.Invoke(null); return; }
        dbRef.Child(CampCodesNode).Child(code).GetValueAsync().ContinueWith(t =>
        {
            string room = t.IsCompleted && !t.IsFaulted && !t.IsCanceled && t.Result != null && t.Result.Exists ? t.Result.Value?.ToString() : null;
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(room));
        });
    }

    bool _petInitRequested;

    void CreateDefaultPetState(DatabaseReference petRef)
    {
        if (_petInitRequested || petRef == null) return;
        _petInitRequested = true;
        var init = new RoomPetState
        {
            petIndex = 0,
            startingPhotoCount = 0,
            hearts = BlockPet.Feed.PetHearts.Max,
            heartsAt = ServerNowMs,
        };
        petRef.SetRawJsonValueAsync(JsonUtility.ToJson(init));
    }

    void StopPetListening()
    {
        StopMetaListening();
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

    /// <summary>Atomically adds coins to the shared room wallet (safe for concurrent calls).
    /// 成功後寫一筆帳本 Rooms/{roomId}/economy/ledger，記錄是誰賺的。</summary>
    public void AddRoomCoins(int amount, string reason = "")
    {
        if (amount <= 0) return;
        if (_coinsRef == null || !_coinsListening)
        {
            Debug.LogWarning($"[FirebaseManager] AddRoomCoins skipped — ref={_coinsRef != null}, listening={_coinsListening}");
            return;
        }
        string roomSnap = _roomId;
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
            {
                Debug.LogError("[FirebaseManager] AddRoomCoins transaction failed: " + t.Exception?.GetBaseException()?.Message);
                return;
            }
            Debug.Log($"[FirebaseManager] AddRoomCoins +{amount} committed");
            WriteCoinLedger(roomSnap, amount, reason, ToLong(t.Result?.Value));
        });
    }

    /// <summary>
    /// 從房間帳戶扣款（transaction，餘額不足時中止，不會扣成負數）。
    /// 成功後寫帳本記錄是誰花的。done(ok) 在主執行緒呼叫。
    /// </summary>
    public void SpendRoomCoins(int amount, string reason, Action<bool> done = null)
    {
        if (amount <= 0) { done?.Invoke(true); return; }
        if (_coinsRef == null || !_coinsListening)
        {
            Debug.LogWarning("[FirebaseManager] SpendRoomCoins skipped — not listening to room coins");
            done?.Invoke(false);
            return;
        }
        string roomSnap = _roomId;
        // handler 可能因衝突重跑多次，以最後一次的判斷為準。
        bool aborted = false;
        _coinsRef.RunTransaction(mutable =>
        {
            long cur = ToLong(mutable.Value);
            aborted = cur < amount;
            if (aborted) return TransactionResult.Abort();
            mutable.Value = cur - amount;
            return TransactionResult.Success(mutable);
        }).ContinueWith(t =>
        {
            bool ok = t.IsCompleted && !t.IsFaulted && !t.IsCanceled && !aborted;
            if (ok)
                WriteCoinLedger(roomSnap, -amount, reason, ToLong(t.Result.Value));
            else
                Debug.LogWarning($"[FirebaseManager] SpendRoomCoins -{amount} 未成功（餘額不足或連線失敗）");
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(ok));
        });
    }

    void WriteCoinLedger(string roomId, int amount, string reason, long balanceAfter)
    {
        if (dbRef == null || string.IsNullOrEmpty(roomId)) return;
        var entry = new Dictionary<string, object>
        {
            ["uid"] = GetUserId() ?? "",
            ["displayName"] = GetDisplayName(),
            ["amount"] = amount,
            ["reason"] = reason ?? "",
            ["balanceAfter"] = balanceAfter,
            ["timestamp"] = ServerValue.Timestamp,
        };
        dbRef.Child("Rooms").Child(roomId).Child("economy").Child("ledger").Push().SetValueAsync(entry).ContinueWith(t =>
        {
            if (t.IsFaulted)
                Debug.LogWarning("[FirebaseManager] Write coin ledger failed: " + t.Exception?.GetBaseException()?.Message);
        });
    }

    static long ToLong(object v)
    {
        if (v == null) return 0;
        try { return Convert.ToInt64(v); } catch { return 0; }
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

    /// <summary>其他成員看的頭貼小縮圖（128px JPG）：Users/{uid}/avatarThumb。</summary>
    public void UploadAvatarThumb(string base64)
    {
        string uid = GetUserId();
        if (string.IsNullOrEmpty(uid) || dbRef == null || string.IsNullOrEmpty(base64)) return;
        dbRef.Child(UsersNode).Child(uid).Child("avatarThumb").SetValueAsync(base64).ContinueWith(t =>
        {
            if (t.IsFaulted)
                Debug.LogWarning("[FirebaseManager] UploadAvatarThumb failed: " + t.Exception?.GetBaseException()?.Message);
        });
    }

    /// <summary>
    /// 一直聽某個成員的頭貼小縮圖（Users/{uid}/avatarThumb，base64）：一開始回傳目前的值，之後對方換頭貼會再回傳（主執行緒）。
    /// 沒有縮圖回傳 null。回傳的 Action 呼叫後停止聽；還沒登入時回傳 null（等登入後再呼叫）。
    /// </summary>
    public Action ListenUserAvatarThumb(string uid, Action<string> onValue)
    {
        if (string.IsNullOrEmpty(uid) || dbRef == null || auth?.CurrentUser == null) return null;
        var r = dbRef.Child(UsersNode).Child(uid).Child("avatarThumb");
        bool warned = false;
        EventHandler<ValueChangedEventArgs> h = (sender, e) =>
        {
            if (e.DatabaseError != null)
            {
                if (!warned) Debug.LogWarning($"[FirebaseManager] 讀不到成員頭貼 {uid}（Firebase 規則要允許登入的人讀 Users/*/avatarThumb）：{e.DatabaseError.Message}");
                warned = true;
                return;
            }
            string v = e.Snapshot != null && e.Snapshot.Exists ? e.Snapshot.Value as string : null;
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => onValue?.Invoke(v));
        };
        r.ValueChanged += h;
        return () => r.ValueChanged -= h;
    }

    /// <summary>舊資料（還沒有小縮圖）的頭貼備份 Users/{uid}/avatarBase64；沒有回傳 null（主執行緒）。</summary>
    public void DownloadUserAvatarBackup(string uid, Action<string> onResult)
    {
        if (string.IsNullOrEmpty(uid) || dbRef == null || auth?.CurrentUser == null) { onResult?.Invoke(null); return; }
        dbRef.Child(UsersNode).Child(uid).Child("avatarBase64").GetValueAsync().ContinueWith(t =>
        {
            string v = !t.IsFaulted && !t.IsCanceled && t.Result != null && t.Result.Exists ? t.Result.Value as string : null;
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => onResult?.Invoke(v));
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
        if (args.Snapshot != null && !args.Snapshot.Exists)
        {
            CreateDefaultPetState(_petRef); // 第一次進房：建立預設寵物狀態
            return;
        }
        string json = args.Snapshot?.GetRawJsonValue();
        if (string.IsNullOrEmpty(json)) return;

        RoomPetState st = null;
        try { st = JsonUtility.FromJson<RoomPetState>(json); } catch { st = null; }
        if (st == null) return;
        _petState = st;
        if (st.heartsAt <= 0) MigratePetHearts(); // 舊房間：第一次載入補成 5 顆

        // Apply to local systems on main thread.
        lock (_mainThreadQueue)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                // Push pet index/progress directly into PetCollectionManager.
                PetCollectionManager.Instance?.ApplyRoomPetState(st.petIndex, st.startingPhotoCount);
            });
        }
    }

    /// <summary>目前房間寵物的愛心（0～5，半顆）；還沒載入房間寵物時回傳 -1。</summary>
    public float GetRoomHeartsNow()
    {
        var st = _petState;
        if (st == null) return -1f;
        if (st.heartsAt <= 0) return BlockPet.Feed.PetHearts.Max; // 搬移中
        return BlockPet.Feed.PetHearts.At(st.hearts, st.heartsAt, ServerNowMs);
    }

    /// <summary>下一次掉半顆還要多久。</summary>
    public TimeSpan GetRoomHeartsNextDrop()
    {
        var st = _petState;
        if (st == null || st.heartsAt <= 0) return TimeSpan.Zero;
        return BlockPet.Feed.PetHearts.UntilNextDrop(st.hearts, st.heartsAt, ServerNowMs);
    }

    static float ReadFloat(Dictionary<string, object> d, string key)
    {
        if (d != null && d.TryGetValue(key, out var v) && v != null) try { return Convert.ToSingle(v); } catch { }
        return 0f;
    }

    static long ReadLong(Dictionary<string, object> d, string key)
    {
        if (d != null && d.TryGetValue(key, out var v) && v != null) try { return Convert.ToInt64(v); } catch { }
        return 0;
    }

    /// <summary>
    /// 餵食回心（房間任何成員餵都算）：先算出現在的愛心再加，並重新開始 5 小時計時。
    /// 寵物已經死亡（0 顆）時不回心，要先復活。用 transaction，兩個人同時餵不會互相蓋掉。
    /// </summary>
    public void AddRoomHearts(float amount)
    {
        if (!enableSharedRoomPet || _petRef == null || amount <= 0f) return;
        _petRef.RunTransaction(mutable =>
        {
            var dict = mutable.Value as Dictionary<string, object> ?? new Dictionary<string, object>();
            long now = ServerNowMs;
            long at = ReadLong(dict, "heartsAt");
            float h = at <= 0 ? BlockPet.Feed.PetHearts.Max : BlockPet.Feed.PetHearts.At(ReadFloat(dict, "hearts"), at, now);
            if (h <= 0f) return TransactionResult.Abort();
            dict["hearts"] = (double)Mathf.Min(BlockPet.Feed.PetHearts.Max, h + amount);
            dict["heartsAt"] = now;
            mutable.Value = dict;
            return TransactionResult.Success(mutable);
        }).ContinueWith(t =>
        {
            if (t.IsFaulted) Debug.LogWarning("[FirebaseManager] AddRoomHearts failed: " + t.Exception?.GetBaseException()?.Message);
        });
    }

    /// <summary>
    /// 復活：用掉空間的一瓶復活藥水（<see cref="BlockPet.Feed.PetHearts.PotionId"/>），愛心回到 5 顆滿。
    /// 先扣藥水再復活；如果扣了之後發現別人已經先復活了，把藥水還回去。
    /// done(ok, 原因) 在主執行緒呼叫，原因：potion（沒有藥水）／alive（已經活著）／error。
    /// </summary>
    public void ReviveRoomPet(Action<bool, string> done)
    {
        if (!enableSharedRoomPet || _petRef == null) { done?.Invoke(false, "error"); return; }
        if (GetRoomHeartsNow() > 0f) { done?.Invoke(false, "alive"); return; }
        string potion = BlockPet.Feed.PetHearts.PotionId;
        var petRef = _petRef;
        UseRoomShopItem(potion, ok =>
        {
            if (!ok) { done?.Invoke(false, "potion"); return; }
            bool revived = false;
            petRef.RunTransaction(mutable =>
            {
                var dict = mutable.Value as Dictionary<string, object> ?? new Dictionary<string, object>();
                long now = ServerNowMs;
                long at = ReadLong(dict, "heartsAt");
                float h = at <= 0 ? BlockPet.Feed.PetHearts.Max : BlockPet.Feed.PetHearts.At(ReadFloat(dict, "hearts"), at, now);
                revived = false;
                if (h > 0f) return TransactionResult.Abort(); // 別人先復活了
                dict["hearts"] = (double)BlockPet.Feed.PetHearts.Max;
                dict["heartsAt"] = now;
                mutable.Value = dict;
                revived = true;
                return TransactionResult.Success(mutable);
            }).ContinueWith(t =>
            {
                bool success = !t.IsFaulted && !t.IsCanceled && revived;
                lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() =>
                {
                    if (!success) AddRoomShopItem(potion, 1, null); // 藥水還回去
                    done?.Invoke(success, success ? null : (revived ? "error" : "alive"));
                });
            });
        });
    }

    /// <summary>用掉空間的一個道具（數量 −1，沒有就失敗）。done(ok) 在主執行緒呼叫。</summary>
    public void UseRoomShopItem(string itemId, Action<bool> done)
    {
        if (dbRef == null || auth?.CurrentUser == null || string.IsNullOrEmpty(_roomId) || string.IsNullOrEmpty(itemId))
        {
            done?.Invoke(false);
            return;
        }
        bool empty = false;
        RoomRoot().Child("shop").Child(itemId).RunTransaction(data =>
        {
            long cur = 0;
            try { if (data.Value != null) cur = Convert.ToInt64(data.Value); } catch { }
            empty = cur <= 0;
            if (empty) return TransactionResult.Abort();
            data.Value = cur - 1;
            return TransactionResult.Success(data);
        }).ContinueWith(t =>
        {
            bool ok = t.IsCompleted && !t.IsFaulted && !t.IsCanceled && !empty;
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(ok));
        });
    }

    /// <summary>測試用（Editor 選單 BlockPet/Debug/愛心）：直接把房間愛心設成 hearts，從現在開始計時。</summary>
    public void DebugSetRoomHearts(float hearts)
    {
        if (_petRef == null) { Debug.LogWarning("[FirebaseManager] 還沒進房間"); return; }
        _petRef.Child("hearts").SetValueAsync((double)BlockPet.Feed.PetHearts.Snap(hearts));
        _petRef.Child("heartsAt").SetValueAsync(ServerNowMs);
    }

    /// <summary>測試用：把計時往前推 hours 小時（模擬很久沒人餵）。</summary>
    public void DebugAgeRoomHearts(double hours)
    {
        var st = _petState;
        if (_petRef == null || st == null || st.heartsAt <= 0) { Debug.LogWarning("[FirebaseManager] 還沒進房間"); return; }
        _petRef.Child("heartsAt").SetValueAsync(st.heartsAt - (long)(hours * 3600000.0));
    }

    bool _heartsMigrating;

    /// <summary>舊房間（24 小時秒數制）搬到愛心制：直接補成 5 顆滿、從現在開始計時（只做一次，誰先載入誰做）。</summary>
    void MigratePetHearts()
    {
        if (_heartsMigrating || _petRef == null) return;
        _heartsMigrating = true;
        _petRef.RunTransaction(mutable =>
        {
            var dict = mutable.Value as Dictionary<string, object>;
            if (dict == null || ReadLong(dict, "heartsAt") > 0) return TransactionResult.Abort();
            dict["hearts"] = (double)BlockPet.Feed.PetHearts.Max;
            dict["heartsAt"] = ServerNowMs;
            mutable.Value = dict;
            return TransactionResult.Success(mutable);
        }).ContinueWith(t => { _heartsMigrating = false; });
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
    // ── 聊天快取（新版聊天室與相簿用：保留 Firebase key、emoji、收回）──────────

    /// <summary>目前空間的聊天（舊到新，最多 200 則），含每則的 key 與 emoji。</summary>
    public IReadOnlyList<ChatEntry> ChatEntries => _chatEntries;
    readonly List<ChatEntry> _chatEntries = new List<ChatEntry>();
    public static event Action<ChatEntry> OnChatEntryAdded;
    public static event Action<ChatEntry> OnChatEntryChanged;
    public static event Action<string> OnChatEntryRemoved;
    /// <summary>換空間或停止監聽，快取清空。</summary>
    public static event Action OnChatEntriesReset;

    static ChatEntry ParseChatEntry(DataSnapshot snap)
    {
        if (snap == null || !snap.Exists) return null;
        ChatMessage msg = null;
        try
        {
            string json = snap.GetRawJsonValue();
            msg = json != null ? JsonUtility.FromJson<ChatMessage>(json) : null;
        }
        catch { }
        if (msg == null) return null;
        var entry = new ChatEntry { key = snap.Key, msg = msg };
        var re = snap.Child("reactions");
        if (re.Exists)
        {
            foreach (var emoji in re.Children)
            {
                var uids = new List<string>();
                foreach (var u in emoji.Children) uids.Add(u.Key);
                if (uids.Count > 0) entry.reactions[emoji.Key] = uids;
            }
        }
        return entry;
    }

    void UpsertChatEntry(ChatEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.key)) return;
        int i = _chatEntries.FindIndex(e => e.key == entry.key);
        if (i >= 0)
        {
            _chatEntries[i] = entry;
            OnChatEntryChanged?.Invoke(entry);
            return;
        }
        // 依時間插入（Firebase key 也是時間順序，時間相同時照 key）
        int at = _chatEntries.Count;
        while (at > 0 && (_chatEntries[at - 1].msg.timestamp > entry.msg.timestamp
               || (_chatEntries[at - 1].msg.timestamp == entry.msg.timestamp && string.CompareOrdinal(_chatEntries[at - 1].key, entry.key) > 0)))
            at--;
        _chatEntries.Insert(at, entry);
        OnChatEntryAdded?.Invoke(entry);
    }

    void HandleChatChildChanged(ChildChangedEventArgs args, Query capturedQuery)
    {
        if (args.DatabaseError != null) return;
        var entry = ParseChatEntry(args.Snapshot);
        lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() =>
        {
            if (!_chatListening || !ReferenceEquals(_chatQuery, capturedQuery)) return;
            UpsertChatEntry(entry);
        });
    }

    void HandleChatChildRemoved(ChildChangedEventArgs args, Query capturedQuery)
    {
        if (args.DatabaseError != null || args.Snapshot == null) return;
        string key = args.Snapshot.Key;
        lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() =>
        {
            if (!_chatListening || !ReferenceEquals(_chatQuery, capturedQuery)) return;
            if (_chatEntries.RemoveAll(e => e.key == key) > 0) OnChatEntryRemoved?.Invoke(key);
        });
    }

    /// <summary>按／取消某則訊息的 emoji（Rooms/{roomId}/Chat/{key}/reactions/{emoji}/{uid}）。</summary>
    public void ToggleChatReaction(string key, string emoji)
    {
        string uid = GetUserId();
        if (dbRef == null || string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(emoji)) return;
        var entry = _chatEntries.Find(e => e.key == key);
        bool mine = entry != null && entry.reactions.TryGetValue(emoji, out var uids) && uids.Contains(uid);
        var r = RoomRoot().Child("Chat").Child(key).Child("reactions").Child(emoji).Child(uid);
        if (mine) r.RemoveValueAsync();
        else r.SetValueAsync(true);
    }

    /// <summary>收回自己的訊息（直接刪掉）。callback 在主執行緒。</summary>
    public void RecallChatMessage(string key, Action<bool> done = null)
    {
        if (dbRef == null || string.IsNullOrEmpty(key)) { done?.Invoke(false); return; }
        RoomRoot().Child("Chat").Child(key).RemoveValueAsync().ContinueWith(t =>
        {
            bool ok = t.IsCompleted && !t.IsFaulted && !t.IsCanceled;
            lock (_mainThreadQueue) _mainThreadQueue.Enqueue(() => done?.Invoke(ok));
        });
    }

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
        if (_chatQuery != null && _chatChildChangedHandler != null)
            _chatQuery.ChildChanged -= _chatChildChangedHandler;
        if (_chatQuery != null && _chatChildRemovedHandler != null)
            _chatQuery.ChildRemoved -= _chatChildRemovedHandler;
        _chatChildAddedHandler = null;
        _chatChildChangedHandler = null;
        _chatChildRemovedHandler = null;
        if (_chatEntries.Count > 0)
        {
            _chatEntries.Clear();
            OnChatEntriesReset?.Invoke();
        }
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
        var entry = ParseChatEntry(args.Snapshot);

        lock (_mainThreadQueue) {
            _mainThreadQueue.Enqueue(() => {
                // Discard if the room has switched since this event was queued.
                if (!_chatListening || !ReferenceEquals(_chatQuery, capturedQuery))
                    return;
                UpsertChatEntry(entry);
                if (ChatUIHandler.Instance != null)
                    ChatUIHandler.Instance.DisplayMessage(msg);
            });
        }
    }
}