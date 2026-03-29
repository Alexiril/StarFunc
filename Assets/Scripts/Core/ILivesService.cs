namespace StarFunc.Core
{
    /// <summary>
    /// Service contract for the lives system.
    /// Implemented by HybridLivesService (Phase 2).
    /// Lives are deducted server-side via POST /check/level;
    /// client receives updated count from the response.
    /// </summary>
    public interface ILivesService
    {
        int GetCurrentLives();
        int GetMaxLives();
        bool HasLives();
        void RestoreLife();
        void RestoreAllLives();
        float GetTimeUntilNextRestore();
    }
}
