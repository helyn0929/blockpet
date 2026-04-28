using UnityEngine;
using UnityEngine.UI;
using System;

public class PetHealthManager : MonoBehaviour
{
    public Slider healthSlider;
    public float maxHealth = 86400f; 
    public float healAmount = 10800f; 

    void Start()
    {
        // Local mode only: shared room pet state is derived from Firebase timestamps.
        if (!IsSharedRoomPetActive())
            CalculateOfflineDecay();
    }

    void Update()
    {
        if (healthSlider == null) return;

        // Shared room pet: UI derives from room state; do not write local decay every frame.
        if (IsSharedRoomPetActive())
        {
            healthSlider.value = FirebaseManager.Instance.GetRoomHealthNow(maxHealth);
            return;
        }

        // Local mode: decay health over time and persist lastUpdateTime.
        if (SaveManager.Instance == null || SaveManager.Instance.data == null) return;
        if (SaveManager.Instance.data.currentHealth <= 0) return;

        SaveManager.Instance.data.currentHealth -= Time.deltaTime;
        SaveManager.Instance.data.lastUpdateTime = DateTime.Now.ToString();
        UpdateUI();
    }

    void CalculateOfflineDecay()
    {
        if (SaveManager.Instance == null || SaveManager.Instance.data == null) return;
        if (string.IsNullOrEmpty(SaveManager.Instance.data.lastUpdateTime)) return;

        // 這裡必須解析文字欄位 (lastUpdateTime)
        if (DateTime.TryParse(SaveManager.Instance.data.lastUpdateTime, out DateTime lastTime))
        {
            TimeSpan span = DateTime.Now - lastTime;
            float secondsPassed = (float)span.TotalSeconds;
            
            // 數字減數字
            SaveManager.Instance.data.currentHealth -= secondsPassed;
            
            if (SaveManager.Instance.data.currentHealth < 0) 
                SaveManager.Instance.data.currentHealth = 0;
        }
            
        UpdateUI();
    }

    public void AddHealth()
    {
        if (IsSharedRoomPetActive())
        {
            FirebaseManager.Instance.AddRoomHealth(healAmount, maxHealth);
            // UI will update from listener / GetRoomHealthNow.
            return;
        }

        if (SaveManager.Instance == null || SaveManager.Instance.data == null) return;
        SaveManager.Instance.data.currentHealth += healAmount;
        if (SaveManager.Instance.data.currentHealth > maxHealth)
            SaveManager.Instance.data.currentHealth = maxHealth;
        UpdateUI();
    }

    void UpdateUI()
    {
        if (healthSlider == null || SaveManager.Instance == null || SaveManager.Instance.data == null) return;
        healthSlider.value = SaveManager.Instance.data.currentHealth;
    }

    static bool IsSharedRoomPetActive()
    {
        return FirebaseManager.Instance != null && FirebaseManager.Instance.HasRoomPetState;
    }
}