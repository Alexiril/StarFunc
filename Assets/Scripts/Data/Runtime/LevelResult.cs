using System;

namespace StarFunc.Data
{
    /// <summary>
    /// Matches the server's CheckResult.result structure (API §5.6, §6.7).
    /// </summary>
    [Serializable]
    public class LevelResult
    {
        public bool IsValid;
        public int Stars;
        public int FragmentsEarned;
        public float Time;
        public int ErrorCount;
        public float MatchPercentage;
        public string[] Errors;

        /// <summary>True when the level is considered failed (no_lives or max_attempts_reached).</summary>
        public bool LevelFailed;

        /// <summary>"no_lives" | "max_attempts_reached" | null.</summary>
        public string FailReason;

        /// <summary>Bonus fragments earned for improving a previously completed level.</summary>
        public int ImprovementBonus;

        /// <summary>Updated best star count (max of old and new).</summary>
        public int BestStars;

        /// <summary>Updated best time (min of old and new, only when valid).</summary>
        public float BestTime;
    }
}
