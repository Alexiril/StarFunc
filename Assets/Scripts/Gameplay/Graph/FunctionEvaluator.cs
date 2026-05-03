using System;
using StarFunc.Data;
using UnityEngine;

namespace StarFunc.Gameplay
{
    public static class FunctionEvaluator
    {
        public static float Evaluate(FunctionDefinition function, float x)
        {
            return function.Type switch
            {
                FunctionType.Linear => EvaluateLinear(function.Coefficients, x),
                FunctionType.Quadratic => EvaluateQuadratic(function.Coefficients, x),
                FunctionType.Sinusoidal => EvaluateSinusoidal(function.Coefficients, x),
                // Mixed is reserved in the FunctionType enum but no seed level uses it,
                // and FunctionDefinition.Coefficients (flat float[]) carries no metadata
                // for compound expressions. Implementation deferred until a content
                // schema for Mixed lands.
                FunctionType.Mixed => throw new NotImplementedException(
                    "FunctionType.Mixed has no content schema yet; not used by any seed level."),
                _ => throw new ArgumentOutOfRangeException(nameof(function), $"Unknown FunctionType: {function.Type}")
            };
        }

        // y = a*x + b
        // Coefficients[0] = a (slope), Coefficients[1] = b (intercept)
        static float EvaluateLinear(float[] coefficients, float x)
        {
            float a = coefficients.Length > 0 ? coefficients[0] : 0f;
            float b = coefficients.Length > 1 ? coefficients[1] : 0f;
            return a * x + b;
        }

        // y = a*(x - h)^2 + k  (vertex form)
        // Coefficients[0] = a, [1] = h (vertex x), [2] = k (vertex y)
        static float EvaluateQuadratic(float[] coefficients, float x)
        {
            float a = coefficients.Length > 0 ? coefficients[0] : 0f;
            float h = coefficients.Length > 1 ? coefficients[1] : 0f;
            float k = coefficients.Length > 2 ? coefficients[2] : 0f;
            float dx = x - h;
            return a * dx * dx + k;
        }

        // y = a*sin(b*x + c) + d
        // Coefficients[0] = a (amplitude), [1] = b (angular frequency, radians),
        // [2] = c (phase, radians), [3] = d (vertical offset)
        static float EvaluateSinusoidal(float[] coefficients, float x)
        {
            float a = coefficients.Length > 0 ? coefficients[0] : 0f;
            float b = coefficients.Length > 1 ? coefficients[1] : 0f;
            float c = coefficients.Length > 2 ? coefficients[2] : 0f;
            float d = coefficients.Length > 3 ? coefficients[3] : 0f;
            return a * Mathf.Sin(b * x + c) + d;
        }
    }
}
