namespace BlockPet.Feed
{
    /// <summary>
    /// 每隻寵物偏愛的照片類別（暫定，2026-10-24；10-29 運動戶外併入自然、新增寵物）：決定「今天想吃」比較常出現哪幾類。
    /// 依 petIndex 對應首頁頭像的寵物圖（HomeTopBar.petSprites 的順序，petIndex 超過就循環）。
    /// 之後有正式寵物資料（進化分支）時改從那邊讀。
    /// </summary>
    public static class PetPreferences
    {
        public static readonly string[] Categories = { "美食", "自然", "城市", "生活人物", "寵物" };

        /// <summary>偏愛類別合計的機率；其他類別平分剩下的。</summary>
        public const float FavoriteWeight = 0.7f;

        static readonly string[][] Favorites =
        {
            new[] { "美食", "生活人物" },   // 0 kirby
            new[] { "自然", "城市" },       // 1 image
            new[] { "寵物", "美食" },       // 2 bull
            new[] { "自然", "美食" },       // 3 bear
            new[] { "城市", "自然" },       // 4 fox
            new[] { "生活人物", "寵物" },   // 5 shiba
            new[] { "美食", "城市" },       // 6 mouse
        };

        public static string[] FavoritesOf(int petIndex) => Favorites[((petIndex % Favorites.Length) + Favorites.Length) % Favorites.Length];
    }
}
