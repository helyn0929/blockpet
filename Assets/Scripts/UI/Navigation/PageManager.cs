using UnityEngine;

/// <summary>
/// Shows one full-screen UI page at a time (Home / Chat / Album / Photo detail) under a single Canvas.
/// Assign the three page roots; each should use stretch anchors to fill the canvas.
/// </summary>
public class PageManager : MonoBehaviour
{
    [Header("Pages (full-screen roots under Canvas)")]
    [SerializeField] GameObject homePage;
    [SerializeField] GameObject roomPage;
    [SerializeField] GameObject chatPage;
    [SerializeField] GameObject albumPage;
    [SerializeField] GameObject marketPage;
    [SerializeField] GameObject settingsPage;
    [SerializeField] GameObject petSettingPage;

    [Header("Startup")]
    [Tooltip("If set, runs once in Start() so play mode always begins on that page.")]
    [SerializeField] InitialPage initialPage = InitialPage.Home;

    [Header("HUD (home only)")]
    [Tooltip("Hide wallet / pet progress while Chat or Album page is open. They live on the Canvas, not under HomePage, so they stay visible unless we toggle them here.")]
    [SerializeField] bool hideWalletAndProgressOnChatAlbum = true;
    [SerializeField] EconomyManager economyManager;
    [SerializeField] PetCollectionManager petCollectionManager;

    public enum InitialPage { Home, Chat, Album, None }

    bool _albumOpenedFromChat;

    /// <summary>首頁目前是否顯示中（手牌只在首頁出現）。</summary>
    public bool IsHomePageActive => homePage != null && homePage.activeInHierarchy;

    // 開遊戲時的「回上次空間」（2026-11-08）：手機記得上次的空間、也沒有登出時，先不顯示選空間轉盤，
    // 等自動登入＋確認空間有結果再決定（進得去直接到首頁），不然轉盤會先閃一下。
    // 最多等 LaunchWaitSeconds 秒（例如沒網路、沒登入），之後照常顯示轉盤。
    const float LaunchWaitSeconds = 6f;
    static bool _launchWait;
    static float _launchWaitUntil;

    /// <summary>還在等「回上次空間」的結果（這時不顯示轉盤）。</summary>
    public static bool WaitingForLaunchResume => _launchWait && Time.realtimeSinceStartup < _launchWaitUntil;

    void Awake()
    {
        ResolveHudManagers();
        bool hasRoom = !string.IsNullOrEmpty(PlayerPrefs.GetString("Blockpet.RoomId", ""));
        bool signedOut = PlayerPrefs.GetInt("Blockpet.UserSignedOut", 0) == 1;
        _launchWait = hasRoom && !signedOut;
        _launchWaitUntil = Time.realtimeSinceStartup + LaunchWaitSeconds;
    }

    /// <summary>「回上次空間」有結果了（進去了，或要去選空間）：之後照常顯示頁面。</summary>
    public void EndLaunchWait() => _launchWait = false;

    void Update()
    {
        // 等太久（沒網路、沒自動登入）：照常顯示選空間轉盤
        if (_launchWait && Time.realtimeSinceStartup >= _launchWaitUntil)
        {
            _launchWait = false;
            if (!LoginUIHandler.GameplayHudReleased) ShowRoomPage();
        }
    }

    void OnEnable() => FirebaseManager.OnLoginSuccess += OnLoginAtLaunch;
    void OnDisable() => FirebaseManager.OnLoginSuccess -= OnLoginAtLaunch;

    /// <summary>
    /// 開 App 自動登入：LoginPanel（LoginUIHandler 和登入頁）在場景裡是關著的，收不到登入事件，
    /// 由這裡請它進上次的空間。LoginPanel 開著時（登出後重新登入）由登入頁自己處理，這裡不重複做。
    /// </summary>
    void OnLoginAtLaunch(bool success)
    {
        if (!success || LoginUIHandler.GameplayHudReleased) return;
        var login = FindObjectOfType<LoginUIHandler>(true);
        if (login == null || login.isActiveAndEnabled) return;
        login.ResumeLastRoomOnLaunch(this);
    }

    void Start()
    {
        // Login flow controls the first page. Before a room is selected, we must not auto-switch
        // to the initial page (Home/Chat/Album), otherwise it can override LoginUIHandler's
        // ShowRoomPage() on the first frame after MainGame becomes active.
        var login = FindObjectOfType<LoginUIHandler>(true);
        if (login != null && !LoginUIHandler.GameplayHudReleased)
            return;

        switch (initialPage)
        {
            case InitialPage.Home:
                ShowHomePage();
                break;
            case InitialPage.Chat:
                ShowChatPage();
                break;
            case InitialPage.Album:
                ShowAlbumPage();
                break;
            case InitialPage.None:
                break;
        }
    }

    /// <summary>Deactivates every registered page.</summary>
    public void HideAllPages()
    {
        DeactivateAllPageRoots();
        ApplyHomeHudVisibility(false);
    }

    public void ShowHomePage()
    {
        var login = FindObjectOfType<LoginUIHandler>(true);
        if (login != null && !LoginUIHandler.GameplayHudReleased)
        {
            ShowRoomPage();
            return;
        }
        ShowOnly(homePage, true);
    }

    public void ShowRoomPage()
    {
        if (roomPage == null)
        {
            // Best-effort auto-discovery (optional): name-based lookup so scenes can adopt without re-wiring immediately.
            roomPage = FindByNameIncludingInactive("RoomPage") ?? GameObject.Find("RoomPage");
        }
        if (roomPage == null)
        {
            Debug.LogWarning("[PageManager] Assign Room Page in the Inspector (GameObject named 'RoomPage' also works).");
            return;
        }
        if (WaitingForLaunchResume && !LoginUIHandler.GameplayHudReleased)
        {
            // 還在等「回上次空間」：先什麼頁都不顯示，避免轉盤閃一下
            HideAllPages();
            return;
        }
        // Ensure its parents are active so the page can actually render.
        EnsureActiveUpwards(roomPage.transform);
        ShowOnly(roomPage, false);
    }

    public void ShowChatPage()
    {
        var login = FindObjectOfType<LoginUIHandler>(true);
        if (login != null && !LoginUIHandler.GameplayHudReleased)
        {
            // Before the player selects a room, RoomPage is the only allowed page.
            ShowRoomPage();
            return;
        }
        ShowOnly(chatPage, false);
    }

    public void ShowAlbumPage()
    {
        _albumOpenedFromChat = false;
        var login = FindObjectOfType<LoginUIHandler>(true);
        if (login != null && !LoginUIHandler.GameplayHudReleased)
        {
            ShowRoomPage();
            return;
        }
        ShowOnly(albumPage, false);
    }

    public void ShowAlbumFromChat()
    {
        _albumOpenedFromChat = true;
        var login = FindObjectOfType<LoginUIHandler>(true);
        if (login != null && !LoginUIHandler.GameplayHudReleased)
        {
            ShowRoomPage();
            return;
        }
        ShowOnly(albumPage, false);
    }

    public void CloseAlbumPage()
    {
        if (_albumOpenedFromChat)
            ShowChatPage();
        else
            ShowHomePage();
    }

    /// <summary>Full-screen market (shop + dressing preview). HUD hidden like other sub-pages.</summary>
    public void ShowMarketPage()
    {
        if (marketPage == null)
        {
            Debug.LogWarning("[PageManager] Assign Market Page in the Inspector.");
            return;
        }
        ShowOnly(marketPage, false);
    }

    void ResolveHudManagers()
    {
        if (economyManager == null)
            economyManager = FindObjectOfType<EconomyManager>();
        if (petCollectionManager == null)
            petCollectionManager = FindObjectOfType<PetCollectionManager>();
    }

    /// <summary>On Home after login (or when there is no login screen), show HUD; hide on Chat/Album.</summary>
    void ApplyHomeHudVisibility(bool onHomePage)
    {
        if (!hideWalletAndProgressOnChatAlbum)
            return;

        ResolveHudManagers();

        bool show = onHomePage && ShouldShowHudOnHome();
        SetActiveSafe(economyManager != null ? economyManager.gameObject : null, show);
        SetActiveSafe(petCollectionManager != null ? petCollectionManager.gameObject : null, show);
    }

    static bool ShouldShowHudOnHome()
    {
        var login = FindObjectOfType<LoginUIHandler>();
        if (login == null)
            return true;
        return LoginUIHandler.GameplayHudReleased;
    }

    public void ShowSettingsPage()
    {
        if (settingsPage == null)
        {
            Debug.LogWarning("[PageManager] Assign Settings Page in the Inspector.");
            return;
        }
        ShowOnly(settingsPage, false);
    }

    public void ShowPetSettingPage()
    {
        if (petSettingPage == null)
        {
            Debug.LogWarning("[PageManager] Assign Pet Setting Page in the Inspector.");
            return;
        }
        ShowOnly(petSettingPage, false);
    }

    void DeactivateAllPageRoots()
    {
        SetActiveSafe(homePage, false);
        SetActiveSafe(roomPage, false);
        SetActiveSafe(chatPage, false);
        SetActiveSafe(albumPage, false);
        SetActiveSafe(marketPage, false);
        SetActiveSafe(settingsPage, false);
        SetActiveSafe(petSettingPage, false);
    }

    void ShowOnly(GameObject page, bool isHomePage)
    {
        if (page == null)
        {
            Debug.LogWarning("[PageManager] ShowOnly called with null page.");
            return;
        }

        Debug.Log($"[PageManager] ShowOnly -> {(page != null ? page.name : "null")} (isHome={isHomePage}, hudReleased={LoginUIHandler.GameplayHudReleased})");
        DeactivateAllPageRoots();
        page.SetActive(true);
        ApplyHomeHudVisibility(isHomePage);
    }

    static void SetActiveSafe(GameObject go, bool active)
    {
        if (go != null)
            go.SetActive(active);
    }

    static void EnsureActiveUpwards(Transform t)
    {
        if (t == null) return;
        Transform cur = t;
        while (cur != null)
        {
            cur.gameObject.SetActive(true);
            cur = cur.parent;
        }
    }

    static GameObject FindByNameIncludingInactive(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        // Works in player too. Includes inactive objects.
        var all = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < all.Length; i++)
        {
            var go = all[i];
            if (go != null && go.name == name)
                return go;
        }
        return null;
    }
}
