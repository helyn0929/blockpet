using UnityEngine;
using UnityEngine.UI;
using BlockPet.Feed;

/// <summary>
/// 寵物愛心（開發順序第 6 步）：0～5 顆、半顆為單位，每 5 小時沒人餵掉半顆，以伺服器時間計算（規則見 <see cref="PetHearts"/>）。
/// 資料存在房間（FirebaseManager 的 petState.hearts／heartsAt），這裡只負責讀出現在的愛心、回心、復活，
/// 並把愛心星等即時套用到寵物的待機行為（AIController）。
/// 還沒進房間時（沒有房間寵物）一律當 5 顆滿。
/// </summary>
public class PetHealthManager : MonoBehaviour
{
    [Tooltip("舊版血條（可留空）；值為 0～maxHealth")]
    public Slider healthSlider;
    [Tooltip("舊版血條的最大值。愛心換算：CurrentHealth = 愛心 / 5 × maxHealth")]
    public float maxHealth = 86400f;

    /// <summary>目前愛心（0～5，半顆）。</summary>
    public float Hearts
    {
        get
        {
            float h = FirebaseManager.Instance != null ? FirebaseManager.Instance.GetRoomHeartsNow() : -1f;
            return h < 0f ? PetHearts.Max : h;
        }
    }

    /// <summary>愛心 0 顆：寵物死亡，要用房間金幣復活。</summary>
    public bool IsDead => Hearts <= 0f;

    /// <summary>舊介面相容：愛心換算成 0～maxHealth（頂部狀態欄、轉盤、分割畫面用 CurrentHealth / maxHealth × 5）。</summary>
    public float CurrentHealth => Hearts / PetHearts.Max * maxHealth;

    int _appliedStars;

    void Update()
    {
        float hearts = Hearts;
        if (healthSlider != null) healthSlider.value = hearts / PetHearts.Max * maxHealth;

        // 愛心星等即時影響寵物待機行為（★越高越活潑）
        int stars = PetHearts.Stars(hearts);
        var pet = AIController.Instance;
        if (pet != null && stars != _appliedStars)
        {
            _appliedStars = stars;
            pet.SetHeartLevel(stars);
        }
    }

    /// <summary>回半顆心（每餵一張照片，FeedSettlement 呼叫）。寵物死亡時不回心。</summary>
    public void AddHalfHeart()
    {
        FirebaseManager.Instance?.AddRoomHearts(PetHearts.Step);
    }

    /// <summary>復活：喝一瓶空間的復活藥水（商城買）。done(ok, 原因：potion／alive／error) 在主執行緒呼叫。</summary>
    public void Revive(System.Action<bool, string> done)
    {
        var fb = FirebaseManager.Instance;
        if (fb == null) { done?.Invoke(false, "error"); return; }
        fb.ReviveRoomPet(done);
    }
}
