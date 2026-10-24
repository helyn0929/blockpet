using UnityEditor;
using UnityEngine;

namespace BlockPet.EditorTools.Feed
{
    /// <summary>
    /// 愛心測試（Play 中、已進房間時使用）：直接改房間的愛心，房間其他成員也會看到。
    /// </summary>
    public static class PetHeartsDebugMenu
    {
        [MenuItem("BlockPet/Debug/愛心設成 5 顆")] static void Full() => Set(5f);
        [MenuItem("BlockPet/Debug/愛心設成 2 顆")] static void Two() => Set(2f);
        [MenuItem("BlockPet/Debug/愛心設成半顆")] static void Half() => Set(0.5f);
        [MenuItem("BlockPet/Debug/愛心設成 0（死亡）")] static void Dead() => Set(0f);

        [MenuItem("BlockPet/Debug/時間快轉 5 小時")]
        static void Age()
        {
            if (!Check()) return;
            FirebaseManager.Instance.DebugAgeRoomHearts(5);
        }

        [MenuItem("BlockPet/Debug/房間金幣 +100")]
        static void Coins()
        {
            if (!Check()) return;
            FirebaseManager.Instance.AddRoomCoins(100, "debug");
        }

        static void Set(float hearts)
        {
            if (!Check()) return;
            FirebaseManager.Instance.DebugSetRoomHearts(hearts);
        }

        static bool Check()
        {
            if (Application.isPlaying && FirebaseManager.Instance != null) return true;
            Debug.LogWarning("[PetHeartsDebug] 要在 Play 中、進房間後使用");
            return false;
        }
    }
}
