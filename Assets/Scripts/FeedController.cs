using UnityEngine;

public class FeedController : MonoBehaviour
{
    public Transform pet;
    public Animator petAnimator;
    public float feedScale = 0.15f;
    public AlbumUI albumUI;


    public GameObject feedButton;   
    public GameObject fedText;     

    public void Feed()
    {
        if (pet == null) return;

        //fake eating animation
        pet.localScale += Vector3.one * feedScale;
        pet.localScale = Vector3.Min(pet.localScale, Vector3.one * 1.5f);

        pet.position += Vector3.up * 0.2f;
        Invoke(nameof(ResetPetPosition), 0.2f);

        // UI 狀態切換
        feedButton.SetActive(false);
        fedText.SetActive(true);
    }

    void ResetPetPosition()
    {
        pet.position -= Vector3.up * 0.2f;
    }
    public void FeedWithPhoto(Texture2D photo)
    {
        // 1. 播放吃動畫
        if (petAnimator != null)
            petAnimator.SetTrigger("Eat");

        // 2. 存進相簿資料
        AlbumManager.Instance.AddPhoto(photo);

        // 3. 更新相簿 UI

        if (albumUI != null)
            albumUI.AddPhotoItem(photo);
        else
            Debug.LogError("AlbumUI not assigned");

        // 4. 顯示 Fed!
        Feed();
    }

}


