using System;
using StarFunc.Data;

namespace StarFunc.Gameplay
{
    /// <summary>
    /// Calculates the final result (star rating + fragment reward) for a completed level.
    /// Plain C# class — not a MonoBehaviour.
    /// </summary>
    public class LevelResultCalculator
    {
        /// <summary>
        /// Determine star rating from error count, elapsed time, and level thresholds.
        /// </summary>
        public LevelResult Calculate(LevelData level, int errors, float time)
        {
            return Calculate(level, errors, time, null, 0, null);
        }

        /// <summary>
        /// Full calculation with repeat-play improvement bonus and fail-reason support.
        /// </summary>
        /// <param name="previousProgress">Previous progress for this level (null on first play).</param>
        /// <param name="improvementBonusPerStar">Bonus fragments per newly earned star (from balance config).</param>
        /// <param name="failReason">"no_lives", "max_attempts_reached", or null.</param>
        public LevelResult Calculate(
            LevelData level,
            int errors,
            float time,
            LevelProgress previousProgress,
            int improvementBonusPerStar,
            string failReason)
        {
            bool levelFailed = failReason != null;

            // When the level is failed, result is always 0 stars / 0 fragments.
            if (levelFailed)
            {
                return new LevelResult
                {
                    IsValid = false,
                    Stars = 0,
                    Time = time,
                    ErrorCount = errors,
                    FragmentsEarned = 0,
                    MatchPercentage = 0f,
                    Errors = Array.Empty<string>(),
                    LevelFailed = true,
                    FailReason = failReason,
                    ImprovementBonus = 0,
                    BestStars = previousProgress?.BestStars ?? 0,
                    BestTime = previousProgress?.BestTime ?? 0f
                };
            }

            var rating = level.StarRating;

            int stars;
            if (errors <= rating.ThreeStarMaxErrors)
                stars = 3;
            else if (errors <= rating.TwoStarMaxErrors)
                stars = 2;
            else if (errors <= rating.OneStarMaxErrors)
                stars = 1;
            else
                stars = 0; // 0 stars = level not passed

            // Downgrade from 3 to 2 stars if time exceeds threshold.
            if (rating.TimerAffectsRating && stars == 3
                && rating.ThreeStarMaxTime > 0f && time > rating.ThreeStarMaxTime)
            {
                stars = 2;
            }

            int fragments = stars > 0 ? level.FragmentReward : 0;

            // Repeat-play improvement bonus.
            int improvementBonus = 0;
            int bestStars = stars;
            float bestTime = time;

            if (previousProgress != null && previousProgress.IsCompleted)
            {
                bestStars = Math.Max(previousProgress.BestStars, stars);
                bestTime = Math.Min(previousProgress.BestTime, time);

                int newStars = stars - previousProgress.BestStars;
                if (newStars > 0)
                    improvementBonus = improvementBonusPerStar * newStars;

                // Don't re-award base fragments on repeat if the level was already completed.
                // Only the improvement bonus is earned.
                fragments = improvementBonus;
            }

            return new LevelResult
            {
                IsValid = stars > 0,
                Stars = stars,
                Time = time,
                ErrorCount = errors,
                FragmentsEarned = fragments + improvementBonus,
                MatchPercentage = errors == 0 ? 1f : 0f,
                Errors = Array.Empty<string>(),
                LevelFailed = false,
                FailReason = null,
                ImprovementBonus = improvementBonus,
                BestStars = bestStars,
                BestTime = bestTime
            };
        }

        /// <summary>
        /// Compute matchPercentage for AdjustGraph / BuildFunction modes.
        /// Uses root-mean-square error normalised to [0, 1] where 1 = perfect match.
        /// </summary>
        /// <param name="playerValues">Player-submitted sample values.</param>
        /// <param name="referenceValues">Reference (correct) sample values.</param>
        /// <param name="normalisationRange">Value range for normalisation (e.g. PlaneMax.y - PlaneMin.y).</param>
        public static float CalculateMatchPercentage(
            float[] playerValues,
            float[] referenceValues,
            float normalisationRange)
        {
            if (playerValues == null || referenceValues == null) return 0f;
            int count = Math.Min(playerValues.Length, referenceValues.Length);
            if (count == 0 || normalisationRange <= 0f) return 0f;

            float sumSqError = 0f;
            for (int i = 0; i < count; i++)
            {
                float diff = playerValues[i] - referenceValues[i];
                sumSqError += diff * diff;
            }

            float rmse = (float)Math.Sqrt(sumSqError / count);
            float normalisedError = Math.Min(rmse / normalisationRange, 1f);

            return 1f - normalisedError;
        }
    }
}
