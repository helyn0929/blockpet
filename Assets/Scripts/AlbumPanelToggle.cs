using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Toggle album panel open/closed when the album entry button is pressed.
/// Album cannot be opened while the camera is in Preview or Frozen state.
/// </summary>
public class AlbumPanelToggle : MonoBehaviour
{
    [SerializeField] Button albumButton;
    [Tooltip("Legacy: album panel root. Not needed if Page Manager is set.")]
    [SerializeField] GameObject albumPanel;
    [Tooltip("When set, opens Album page (no toggle from Home). Use with CameraUIManager disabled.")]
    [SerializeField] PageManager pageManager;
    [SerializeField] CameraController cameraController;

    void Start()
    {
        if (albumButton == null)
        {
            Debug.LogWarning("[AlbumPanelToggle] Assign Album Button in the Inspector.");
            return;
        }

        if (pageManager == null && albumPanel == null)
        {
            Debug.LogWarning("[AlbumPanelToggle] Assign Album Panel or Page Manager.");
            return;
        }

        if (cameraController == null)
            cameraController = FindObjectOfType<CameraController>();

        albumButton.onClick.RemoveAllListeners();
        albumButton.onClick.AddListener(ToggleAlbum);
    }

    public void ToggleAlbum()
    {
        if (pageManager != null)
        {
            if (cameraController != null && cameraController.IsCameraActive())
                return;
            pageManager.ShowAlbumPage();
            return;
        }

        if (albumPanel == null) return;

        if (albumPanel.activeSelf)
        {
            albumPanel.SetActive(false);
            return;
        }

        if (cameraController != null && cameraController.IsCameraActive())
            return;

        albumPanel.SetActive(true);
    }
}
