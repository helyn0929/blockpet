using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Toggle album panel open/closed when the album entry button is pressed.
/// Album cannot be opened while the camera is in Preview or Frozen state.
/// </summary>
public class AlbumPanelToggle : MonoBehaviour
{
    [SerializeField] Button albumButton;
    [SerializeField] GameObject albumPanel;
    [SerializeField] CameraController cameraController;

    void Start()
    {
        if (albumButton == null || albumPanel == null)
        {
            Debug.LogWarning("[AlbumPanelToggle] Assign Album Button and Album Panel in the Inspector.");
            return;
        }

        if (cameraController == null)
            cameraController = FindObjectOfType<CameraController>();

        albumButton.onClick.RemoveAllListeners();
        albumButton.onClick.AddListener(ToggleAlbum);
    }

    public void ToggleAlbum()
    {
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
