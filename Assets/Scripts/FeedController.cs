using UnityEngine;

public class FeedController : MonoBehaviour
{
    public Transform pet;
    public Animator petAnimator;
    public float feedScale = 0.15f;


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
        if (petAnimator != null)
        {
            petAnimator.SetTrigger("Eat"); // connection
        }
        // TODO：之後存進相簿
        Debug.Log("Pet eats photo!");

        Feed(); // 直接沿用你已完成的 Feed 動畫與 UI
    }

}


