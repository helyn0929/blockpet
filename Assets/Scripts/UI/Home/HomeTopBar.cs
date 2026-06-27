using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Top-left pet avatar on the HomePage.
/// Shows the current pet's sprite (mapped from PetCollectionManager.CurrentPetIndex)
/// and the pet's display name. Tapping it navigates to the Room selection page.
/// Attach to the TopBar GameObject and wire references in the Inspector.
/// </summary>
public class HomeTopBar : MonoBehaviour
{
    [Header("Avatar button (top-left)")]
    [SerializeField] Button avatarButton;
    [SerializeField] Image avatarImage;
    [SerializeField] TextMeshProUGUI petNameLabel;

    [Header("Pet sprites (index matches PetCollectionManager.CurrentPetIndex)")]
    [Tooltip("Assign one sprite per pet index. If index >= array length the last sprite is used.")]
    [SerializeField] Sprite[] petSprites;

    [Header("Navigation")]
    [SerializeField] PageManager pageManager;

    void Awake()
    {
        if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
    }

    void OnEnable()
    {
        if (avatarButton != null)
            avatarButton.onClick.AddListener(OnAvatarTapped);

        AvatarManager.OnAvatarChanged += RefreshAvatar;
        RefreshAvatar();
    }

    void OnDisable()
    {
        if (avatarButton != null)
            avatarButton.onClick.RemoveListener(OnAvatarTapped);

        AvatarManager.OnAvatarChanged -= RefreshAvatar;
    }

    void RefreshAvatar()
    {
        int petIndex = PetCollectionManager.Instance != null
            ? PetCollectionManager.Instance.CurrentPetIndex
            : 0;

        // Set pet sprite — same mapping as RoomUIController (petIndex % length)
        if (avatarImage != null && petSprites != null && petSprites.Length > 0)
        {
            avatarImage.sprite = petSprites[petIndex % petSprites.Length];
            avatarImage.preserveAspect = true;
        }

        // Set pet name
        if (petNameLabel != null)
        {
            string name = PetCollectionManager.Instance != null
                ? PetCollectionManager.Instance.GetPetDisplayName()
                : "";
            petNameLabel.text = name;
        }
    }

    void OnAvatarTapped()
    {
        if (pageManager == null) pageManager = FindObjectOfType<PageManager>(true);
        pageManager?.ShowPetSettingPage();
    }
}
