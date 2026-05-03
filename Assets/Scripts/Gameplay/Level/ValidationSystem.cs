using System.Collections.Generic;
using System.Linq;
using StarFunc.Data;
using UnityEngine;

namespace StarFunc.Gameplay
{
    /// <summary>
    /// Validates player answers against expected solutions.
    /// Plain C# class — not a MonoBehaviour.
    /// </summary>
    public class ValidationSystem
    {
        /// <summary>
        /// Check whether a selected coordinate is close enough to the expected one.
        /// </summary>
        public bool ValidateCoordinate(Vector2 selected, Vector2 expected, float threshold)
        {
            return Vector2.Distance(selected, expected) <= threshold;
        }

        /// <summary>
        /// Compare a selected function against a reference function.
        /// For linear functions compares coefficients within threshold.
        /// </summary>
        public bool ValidateFunction(FunctionDefinition player, FunctionDefinition reference, float threshold)
        {
            if (player == null || reference == null) return false;
            if (player.Type != reference.Type) return false;

            if (player.Coefficients == null || reference.Coefficients == null) return false;
            if (player.Coefficients.Length != reference.Coefficients.Length) return false;

            for (int i = 0; i < reference.Coefficients.Length; i++)
            {
                if (Mathf.Abs(player.Coefficients[i] - reference.Coefficients[i]) > threshold)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Compare placed control-point stars against reference stars by StarId and coordinate.
        /// </summary>
        public ValidationResult ValidateControlPoints(StarConfig[] placed, StarConfig[] reference)
        {
            var result = new ValidationResult
            {
                IsValid = true,
                Errors = new List<string>(),
                MatchPercentage = 0f
            };

            if (reference == null || reference.Length == 0)
            {
                result.MatchPercentage = 1f;
                return result;
            }

            var referenceById = new Dictionary<string, StarConfig>();
            foreach (var r in reference)
                referenceById[r.StarId] = r;

            int matched = 0;

            foreach (var p in placed)
            {
                if (!referenceById.TryGetValue(p.StarId, out var expected))
                {
                    result.Errors.Add($"Star '{p.StarId}' has no matching reference.");
                    result.IsValid = false;
                    continue;
                }

                if (Vector2.Distance(p.Coordinate, expected.Coordinate) > 0.01f)
                {
                    result.Errors.Add($"Star '{p.StarId}' at ({p.Coordinate.x}, {p.Coordinate.y}) " +
                                      $"expected at ({expected.Coordinate.x}, {expected.Coordinate.y}).");
                    result.IsValid = false;
                }
                else
                {
                    matched++;
                }
            }

            // Check for missing stars.
            foreach (var r in reference)
            {
                if (!placed.Any(p => p.StarId == r.StarId))
                {
                    result.Errors.Add($"Missing star '{r.StarId}'.");
                    result.IsValid = false;
                }
            }

            result.MatchPercentage = reference.Length > 0
                ? (float)matched / reference.Length
                : 1f;

            return result;
        }

        /// <summary>
        /// Top-level validation dispatcher. Routes by TaskType.
        /// Phase 1: only ChooseCoordinate is fully implemented.
        /// Phase 3.1 adds AdjustGraph (RMS over control points).
        /// </summary>
        public LevelResult ValidateLevel(LevelData level, PlayerAnswer answer)
        {
            switch (level.TaskType)
            {
                case TaskType.ChooseCoordinate:
                    return ValidateLevelChooseCoordinate(level, answer);

                case TaskType.ChooseFunction:
                    return ValidateLevelChooseFunction(level, answer);

                case TaskType.AdjustGraph:
                    return ValidateLevelAdjustGraph(level, answer);

                case TaskType.BuildFunction:
                    return ValidateLevelBuildFunction(level, answer);

                case TaskType.IdentifyError:
                    return ValidateLevelIdentifyError(level, answer);

                // RestoreConstellation is per-step; LevelController handles it inline.
                default:
                    Debug.LogWarning($"[ValidationSystem] ValidateLevel not implemented for TaskType.{level.TaskType}.");
                    return new LevelResult { IsValid = false, Stars = 0, ErrorCount = 0, Time = 0f, FragmentsEarned = 0, MatchPercentage = 0f, Errors = System.Array.Empty<string>() };
            }
        }

        /// <summary>
        /// BuildFunction validation: sample player's function at every control-point
        /// star's x and compare to that star's y (no reference function needed —
        /// the stars themselves define the target curve).
        /// </summary>
        static LevelResult ValidateLevelBuildFunction(LevelData level, PlayerAnswer answer)
        {
            var calculator = new LevelResultCalculator();

            if (level.Stars == null || answer.Coefficients == null)
                return calculator.Calculate(level, errors: 1, time: 0f);

            var player = ScriptableObject.CreateInstance<FunctionDefinition>();
            try
            {
                player.Type = answer.FunctionType;
                player.Coefficients = answer.Coefficients;
                player.DomainRange = level.PlaneMin.x < level.PlaneMax.x
                    ? new Vector2(level.PlaneMin.x, level.PlaneMax.x)
                    : new Vector2(-5f, 5f);

                int controlPoints = 0;
                int hits = 0;
                float threshold = level.AccuracyThreshold > 0f ? level.AccuracyThreshold : 0.5f;

                foreach (var star in level.Stars)
                {
                    if (!star.IsControlPoint) continue;
                    controlPoints++;
                    try
                    {
                        float yPlay = FunctionEvaluator.Evaluate(player, star.Coordinate.x);
                        if (Mathf.Abs(yPlay - star.Coordinate.y) <= threshold) hits++;
                    }
                    catch (System.NotImplementedException)
                    {
                        // Evaluator unavailable for this function type — abort validation.
                        return calculator.Calculate(level, errors: 1, time: 0f);
                    }
                }

                int errors = (controlPoints == 0 || hits < controlPoints) ? 1 : 0;
                return calculator.Calculate(level, errors, 0f);
            }
            finally
            {
                Object.Destroy(player);
            }
        }

        /// <summary>
        /// IdentifyError validation: the player's selected star ids must match
        /// exactly the level's distractor stars (every distractor selected,
        /// no non-distractor selected).
        /// </summary>
        static LevelResult ValidateLevelIdentifyError(LevelData level, PlayerAnswer answer)
        {
            var calculator = new LevelResultCalculator();

            if (level.Stars == null)
                return calculator.Calculate(level, errors: 1, time: 0f);

            var distractorIds = new HashSet<string>(
                level.Stars.Where(s => s.IsDistractor).Select(s => s.StarId));
            var selected = answer.SelectedStarIds != null
                ? new HashSet<string>(answer.SelectedStarIds)
                : new HashSet<string>();

            int errors = selected.SetEquals(distractorIds) ? 0 : 1;
            return calculator.Calculate(level, errors, 0f);
        }

        /// <summary>
        /// Compute the root-mean-square deviation between player and reference functions
        /// sampled at the control-point x-coordinates. Returns -1 if RMS is uncomputable
        /// (no control points, or evaluator threw — e.g. unimplemented function type).
        /// </summary>
        public static float ComputeControlPointRms(FunctionDefinition player, FunctionDefinition reference,
                                                   StarConfig[] stars)
        {
            if (player == null || reference == null || stars == null) return -1f;

            float sumSq = 0f;
            int n = 0;

            foreach (var star in stars)
            {
                if (!star.IsControlPoint) continue;
                try
                {
                    float yRef = FunctionEvaluator.Evaluate(reference, star.Coordinate.x);
                    float yPlay = FunctionEvaluator.Evaluate(player, star.Coordinate.x);
                    float d = yPlay - yRef;
                    sumSq += d * d;
                    n++;
                }
                catch (System.NotImplementedException)
                {
                    return -1f;
                }
            }

            if (n == 0) return -1f;
            return Mathf.Sqrt(sumSq / n);
        }

        LevelResult ValidateLevelAdjustGraph(LevelData level, PlayerAnswer answer)
        {
            var calculator = new LevelResultCalculator();

            if (level.ReferenceFunctions == null || level.ReferenceFunctions.Length == 0
                || answer.Coefficients == null)
            {
                return calculator.Calculate(level, errors: 1, time: 0f);
            }

            var reference = level.ReferenceFunctions[0];
            var player = ScriptableObject.CreateInstance<FunctionDefinition>();
            try
            {
                player.Type = answer.FunctionType;
                player.Coefficients = answer.Coefficients;
                player.DomainRange = reference.DomainRange;

                float rms = ComputeControlPointRms(player, reference, level.Stars);

                int errors;
                if (rms < 0f)
                {
                    // No control points or evaluator unsupported — fall back to coefficient comparison.
                    bool ok = ValidateFunction(player, reference, level.AccuracyThreshold);
                    errors = ok ? 0 : 1;
                }
                else
                {
                    errors = rms <= level.AccuracyThreshold ? 0 : 1;
                }

                return calculator.Calculate(level, errors, 0f);
            }
            finally
            {
                Object.Destroy(player);
            }
        }

        LevelResult ValidateLevelChooseCoordinate(LevelData level, PlayerAnswer answer)
        {
            // For ChooseCoordinate the authoritative check is AnswerOption.IsCorrect,
            // handled by LevelController / AnswerSystem. This method provides a
            // coordinate-distance fallback for programmatic validation.
            var solutionStars = level.Stars
                .Where(s => s.BelongsToSolution)
                .ToArray();

            int errors = 0;
            foreach (var star in solutionStars)
            {
                if (!ValidateCoordinate(answer.SelectedCoordinate, star.Coordinate, level.AccuracyThreshold))
                    errors++;
            }

            var calculator = new LevelResultCalculator();
            return calculator.Calculate(level, errors, 0f);
        }

        LevelResult ValidateLevelChooseFunction(LevelData level, PlayerAnswer answer)
        {
            // Primary check: compare selected function coefficients against the first reference.
            int errors = 0;
            if (level.ReferenceFunctions != null && level.ReferenceFunctions.Length > 0
                && answer.Coefficients != null)
            {
                var reference = level.ReferenceFunctions[0];
                if (reference.Coefficients == null
                    || reference.Coefficients.Length != answer.Coefficients.Length)
                {
                    errors++;
                }
                else
                {
                    for (int i = 0; i < reference.Coefficients.Length; i++)
                    {
                        if (Mathf.Abs(answer.Coefficients[i] - reference.Coefficients[i]) > level.AccuracyThreshold)
                        {
                            errors++;
                            break;
                        }
                    }
                }
            }

            var calculator = new LevelResultCalculator();
            return calculator.Calculate(level, errors, 0f);
        }
    }
}
