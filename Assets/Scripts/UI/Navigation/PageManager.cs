using UnityEngine;

/// <summary>
/// Shows one full-screen UI page at a time (Home / Chat / Album) under a single Canvas.
/// Assign the three page roots; each should use stretch anchors to fill the canvas.
/// </summary>
public class PageManager : MonoBehaviour
{
    [Header("Pages (full-screen roots under Canvas)")]
    [SerializeField] GameObject homePage;
    [SerializeField] GameObject chatPage;
    [SerializeField] GameObject albumPage;

    [Header("Startup")]
    [Tooltip("If set, runs once in Start() so play mode always begins on that page.")]
    [SerializeField] InitialPage initialPage = InitialPage.Home;

    public enum InitialPage { Home, Chat, Album, None }

    void Start()
    {
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
        SetActiveSafe(homePage, false);
        SetActiveSafe(chatPage, false);
        SetActiveSafe(albumPage, false);
    }

    public void ShowHomePage() => ShowOnly(homePage);

    public void ShowChatPage() => ShowOnly(chatPage);

    public void ShowAlbumPage() => ShowOnly(albumPage);

    void ShowOnly(GameObject page)
    {
        if (page == null)
        {
            Debug.LogWarning("[PageManager] ShowOnly called with null page.");
            return;
        }

        HideAllPages();
        page.SetActive(true);
    }

    static void SetActiveSafe(GameObject go, bool active)
    {
        if (go != null)
            go.SetActive(active);
    }
}
