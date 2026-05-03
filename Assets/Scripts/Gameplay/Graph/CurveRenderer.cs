using StarFunc.Core;
using StarFunc.Data;
using UnityEngine;

namespace StarFunc.Gameplay
{
    [RequireComponent(typeof(LineRenderer))]
    public class CurveRenderer : MonoBehaviour
    {
        [Header("Sample Counts")]
        [SerializeField, Range(10, 200)] int _linearSamples = 40;
        [SerializeField, Range(10, 200)] int _quadraticSamples = 80;
        [SerializeField, Range(10, 200)] int _sinusoidalSamples = 140;
        [SerializeField, Range(10, 200)] int _mixedSamples = 140;

        LineRenderer _line;
        int _lastFullSampleCount;

        void Awake()
        {
            _line = GetComponent<LineRenderer>();
            ConfigureLineRenderer(_line);
        }

        public void Draw(FunctionDefinition function)
        {
            int samples = SamplesFor(function.Type);
            _lastFullSampleCount = samples;

            float xMin = function.DomainRange.x;
            float xMax = function.DomainRange.y;

            _line.positionCount = samples;

            float step = (xMax - xMin) / (samples - 1);
            for (int i = 0; i < samples; i++)
            {
                float x = xMin + step * i;
                float y = FunctionEvaluator.Evaluate(function, x);
                _line.SetPosition(i, new Vector3(x, y, 0f));
            }

            _line.enabled = true;
        }

        /// <summary>
        /// Truncate the curve to the first <paramref name="segmentCount"/> segments
        /// (i.e. <c>segmentCount + 1</c> position samples). Pass <c>0</c> to hide
        /// entirely, or any value &gt;= the full sample count to show the whole curve.
        /// Used by <c>GraphVisibility.PartialReveal</c>.
        /// </summary>
        public void SetVisibleSegments(int segmentCount)
        {
            if (_line == null) return;

            if (segmentCount <= 0)
            {
                _line.positionCount = 0;
                _line.enabled = false;
                return;
            }

            int maxPositions = _lastFullSampleCount > 0 ? _lastFullSampleCount : _line.positionCount;
            int positions = Mathf.Min(segmentCount + 1, maxPositions);
            _line.positionCount = positions;
            _line.enabled = positions > 1;
        }

        int SamplesFor(FunctionType type) => type switch
        {
            FunctionType.Linear => _linearSamples,
            FunctionType.Quadratic => _quadraticSamples,
            FunctionType.Sinusoidal => _sinusoidalSamples,
            FunctionType.Mixed => _mixedSamples,
            _ => _quadraticSamples
        };

        public void Clear()
        {
            _line.positionCount = 0;
            _line.enabled = false;
        }

        public void SetColor(Color color)
        {
            _line.startColor = color;
            _line.endColor = color;
        }

        public void SetWidth(float width)
        {
            _line.startWidth = width;
            _line.endWidth = width;
        }

        static void ConfigureLineRenderer(LineRenderer lr)
        {
            lr.useWorldSpace = false;
            lr.sortingOrder = 5;
            lr.startWidth = 0.06f;
            lr.endWidth = 0.06f;
            lr.startColor = ColorTokens.LINE_PRIMARY;
            lr.endColor = ColorTokens.LINE_PRIMARY;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 4;
            lr.positionCount = 0;
            lr.enabled = false;
        }
    }
}
