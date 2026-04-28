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
    [SerializeField] GameObject photoDetailPage;
    [SerializeField] GameObject marketPage;

    [Header("Startup")]
    [Tooltip("If set, runs once in Start() so play mode always begins on that page.")]
    [SerializeField] InitialPage initialPage = InitialPage.Home;

    [Header("HUD (home only)")]
    [Tooltip("Hide wallet / pet progress while Chat or Album page is open. They live on the Canvas, not under HomePage, so they stay visible unless we toggle them here.")]
    [SerializeField] bool hideWalletAndProgressOnChatAlbum = true;
    [SerializeField] EconomyManager economyManager;
    [SerializeField] PetCollectionManager petCollectionManager;

    public enum InitialPage { Home, Chat, Album, None }

    void Awake()
    {
        ResolveHudManagers();
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
        var login = FindObjectOfType<LoginUIHandler>(true);
        if (login != null && !LoginUIHandler.GameplayHudReleased)
        {
            ShowRoomPage();
            return;
        }
        ShowOnly(albumPage, false);
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

    /// <summary>True when the Photo Detail root is assigned (for diagnostics).</summary>
    public bool HasPhotoDetailPage => photoDetailPage != null;

    /// <summary>Full-screen single photo (from album). HUD stays off like other sub-pages.</summary>
    public void ShowPhotoDetailPage()
    {
        if (photoDetailPage == null)
        {
            Debug.LogWarning("[PageManager] Assign Photo Detail Page in the Inspector (sibling of AlbumPage under MainGame, not nested inside AlbumPage).");
            return;
        }

        HoistPhotoDetailPageIfNestedUnderAlbum();
        ShowOnly(photoDetailPage, false);
    }

    /// <summary>
    /// If PhotoDetailPage is a child of AlbumPage, turning Album off would keep Detail off too.
    /// Move it to the same parent as AlbumPage (e.g. MainGame) so it can show alone.
    /// </summary>
    void HoistPhotoDetailPageIfNestedUnderAlbum()
    {
        if (photoDetailPage == null || albumPage == null)
            return;

        Transform detail = photoDetailPage.transform;
        Transform album = albumPage.transform;
        if (detail == album || !detail.IsChildOf(album))
            return;

        Transform sharedParent = album.parent;
        if (sharedParent == null)
            return;

        detail.SetParent(sharedParent, false);
        detail.SetAsLastSibling();

        if (detail is RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        Debug.Log("[PageManager] Moved PhotoDetailPage out from under AlbumPage so it can display when the album is hidden.");
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

    void DeactivateAllPageRoots()
    {
        SetActiveSafe(homePage, false);
        SetActiveSafe(roomPage, false);
        SetActiveSafe(chatPage, false);
        SetActiveSafe(albumPage, false);
        SetActiveSafe(photoDetailPage, false);
        SetActiveSafe(marketPage, false);
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
