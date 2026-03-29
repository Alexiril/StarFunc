using System;
using System.Collections.Generic;
using UnityEngine;

namespace StarFunc.Data
{
    /// <summary>
    /// Player answer payload sent to POST /check/level.
    /// Supports 4 answer types per API §5.5.
    /// </summary>
    [Serializable]
    public class PlayerAnswer
    {
        public TaskType TaskType;
        public AnswerType AnswerType;

        // ChooseOption (ChooseCoordinate / ChooseFunction)
        public string SelectedOptionId;

        // Function (AdjustGraph / BuildFunction)
        public FunctionType FunctionType;
        public float[] Coefficients;

        // IdentifyStars (IdentifyError)
        public List<string> SelectedStarIds;

        // PlaceStars (RestoreConstellation)
        public List<StarPlacement> Placements;

        // Legacy — kept for local coordinate-based validation
        public Vector2 SelectedCoordinate;
    }
}
