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
        /// Compare a player-built function against a reference function.
        /// Stub — full implementation in Phase 2.
        /// </summary>
        public bool ValidateFunction(FunctionDefinition player, FunctionDefinition reference, float threshold)
        {
            Debug.LogWarning("[ValidationSystem] ValidateFunction is not implemented until Phase 2.");
            return false;
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
        /// </summary>
        public LevelResult ValidateLevel(LevelData level, PlayerAnswer answer)
        {
            switch (level.TaskType)
            {
                case TaskType.ChooseCoordinate:
                    return ValidateLevelChooseCoordinate(level, answer);

                default:
                    Debug.LogWarning($"[ValidationSystem] ValidateLevel not implemented for TaskType.{level.TaskType}.");
                    return new LevelResult { IsValid = false, Stars = 0, ErrorCount = 0, Time = 0f, FragmentsEarned = 0, MatchPercentage = 0f, Errors = System.Array.Empty<string>() };
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
    }
}
