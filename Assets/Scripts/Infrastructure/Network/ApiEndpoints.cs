namespace StarFunc.Infrastructure
{
    /// <summary>
    /// Static constants for all API endpoints (API.md §6).
    /// </summary>
    public static class ApiEndpoints
    {
        public const string BaseUrl = "https://api.starfunc.app/api/v1";

        // Auth (§6.1)
        public const string AuthRegister = "/auth/register";
        public const string AuthRefresh = "/auth/refresh";
        public const string AuthLink = "/auth/link";

        // Save (§6.2)
        public const string Save = "/save";

        // Economy (§6.3)
        public const string EconomyBalance = "/economy/balance";
        public const string EconomyTransaction = "/economy/transaction";

        // Lives (§6.4)
        public const string Lives = "/lives";
        public const string LivesRestore = "/lives/restore";
        public const string LivesRestoreAll = "/lives/restore-all";

        // Shop (§6.5)
        public const string ShopItems = "/shop/items";
        public const string ShopPurchase = "/shop/purchase";

        // Content (§6.6)
        public const string ContentManifest = "/content/manifest";
        public const string ContentSectors = "/content/sectors";
        public const string ContentLevels = "/content/levels"; // append /{id}
        public const string ContentBalance = "/content/balance";

        // Level Check (§6.7)
        public const string CheckLevel = "/check/level";

        // Analytics (§6.8)
        public const string AnalyticsEvents = "/analytics/events";

        // Health (§6.9)
        public const string Health = "/health";
    }
}
