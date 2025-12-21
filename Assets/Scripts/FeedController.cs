using UnityEngine;

public class FeedController : MonoBehaviour
{
    public Transform pet;
    public float feedScale = 0.15f;


    public GameObject feedButton;   // ← 新增
    public GameObject fedText;      // ← 新增

    public void Feed()
    {
        if (pet == null) return;

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
        // TODO：之後存進相簿
        Debug.Log("Pet eats photo!");

        Feed(); // 直接沿用你已完成的 Feed 動畫與 UI
    }

}


