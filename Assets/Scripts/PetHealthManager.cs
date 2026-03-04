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
        CalculateOfflineDecay();
    }

    void Update()
    {
        // 確保數據存在
        if (SaveManager.Instance == null || SaveManager.Instance.data == null) return;

        if (SaveManager.Instance.data.currentHealth > 0)
        {
            // 這裡必須是數字減數字
            SaveManager.Instance.data.currentHealth -= Time.deltaTime;
            
            // 這裡必須是文字欄位 = 時間轉文字
            SaveManager.Instance.data.lastUpdateTime = DateTime.Now.ToString(); 
            
            UpdateUI();
        }
    }

    void CalculateOfflineDecay()
    {
        // 檢查文字欄位是否為空
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
        SaveManager.Instance.data.currentHealth += healAmount;
        if (SaveManager.Instance.data.currentHealth > maxHealth)
            SaveManager.Instance.data.currentHealth = maxHealth;
        
        UpdateUI();
    }

    void UpdateUI()
    {
        if (healthSlider != null)
            healthSlider.value = SaveManager.Instance.data.currentHealth;
    }
}