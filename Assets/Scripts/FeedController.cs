using UnityEngine;

public class FeedController : MonoBehaviour
{
    [Header("真正的那隻狗 (PetBase)")]
    public Transform pet;
    public Animator petAnimator;
    
    [Header("那張會飛的照片 (FlyingPhoto)")]
    public GameObject flyingPhotoObject; 

    [Header("UI 引用")]
    public AlbumUI albumUI;
    public GameObject feedButton;   
    public GameObject fedText;     

    private Vector3 _petStartPos;

    void Awake()
    {
        // 紀錄初始位置，防止連續點擊導致位置偏移
        if (pet != null) _petStartPos = pet.position;
    }

    public void FeedWithPhoto(Texture2D photo)
    {
        // 1. 觸發動畫
        if (petAnimator != null) petAnimator.SetTrigger("Eat");

        SaveManager.Instance.SavePhoto(photo);

        // 2. 顯示飛行的照片
        if (flyingPhotoObject != null) flyingPhotoObject.SetActive(true);

        // 3. 存入資料並更新相簿 UI(short memory)
        //if (AlbumManager.Instance != null) AlbumManager.Instance.AddPhoto(photo);
        //if (albumUI != null) albumUI.AddPhotoItem(photo);

        // 4. 執行餵食回饋 (僅跳動)
        ApplyFeedEffect();
    }

    void ApplyFeedEffect()
    {
        if (pet == null) return;

        // 【已移除放大邏輯】狗狗現在不會變大
        
        // 跳動效果：使用絕對座標，確保狗狗會回到正確位置
        pet.position = _petStartPos + Vector3.up * 0.2f;
        Invoke(nameof(ResetPetPos), 0.2f);

        // UI 狀態切換
        if(feedButton != null) feedButton.SetActive(false);
        if(fedText != null) fedText.SetActive(true);
        
        // 1.5 秒後重置按鈕，可以繼續拍照
        Invoke(nameof(ResetFlow), 1.5f);
    }

    void ResetPetPos() => pet.position = _petStartPos;

    void ResetFlow()
    {
        if(feedButton != null) feedButton.SetActive(true);
        if(fedText != null) fedText.SetActive(false);
    }
}