using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Toggle album panel open/closed when the album entry button is pressed.
/// Assign the album button and album panel in the Inspector; the button's onClick will toggle the panel.
/// </summary>
public class AlbumPanelToggle : MonoBehaviour
{
    [SerializeField] Button albumButton;
    [SerializeField] GameObject albumPanel;

    void Start()
    {
        if (albumButton == null || albumPanel == null)
        {
            Debug.LogWarning("[AlbumPanelToggle] Assign Album Button and Album Panel in the Inspector.");
            return;
        }

        albumButton.onClick.RemoveAllListeners();
        albumButton.onClick.AddListener(ToggleAlbum);
    }

    public void ToggleAlbum()
    {
        albumPanel.SetActive(!albumPanel.activeSelf);
    }
}
