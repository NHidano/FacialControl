using System;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.ScriptableObject.Serializable
{
    /// <summary>
    /// The serialized shape of a pre-gaze-channel entry.  This type exists only
    /// to receive old <c>_gazeConfigs</c> YAML during the one-way migration.
    /// </summary>
    [Serializable]
    public sealed class LegacyGazeConfigEntry
    {
        public string expressionId;
        public bool useDistinctLeftRight;
        public string sourceIdLeft = string.Empty;
        public string sourceIdRight = string.Empty;
        public string leftEyeBonePath;
        public Vector3 leftEyeInitialRotation;
        public Vector3 leftEyeYawAxisLocal = Vector3.up;
        public Vector3 leftEyePitchAxisLocal = Vector3.right;
        public string rightEyeBonePath;
        public Vector3 rightEyeInitialRotation;
        public Vector3 rightEyeYawAxisLocal = Vector3.up;
        public Vector3 rightEyePitchAxisLocal = Vector3.right;
        public float lookUpAngle = 15f;
        public float lookDownAngle = 9f;
        public float outerYawAngle = 15f;
        public float innerYawAngle = 18f;

        public GazeChannel ToChannel()
        {
            return new GazeChannel
            {
                id = expressionId ?? string.Empty,
                useDistinctLeftRight = useDistinctLeftRight,
                sourceIdLeft = sourceIdLeft ?? string.Empty,
                sourceIdRight = sourceIdRight ?? string.Empty,
                leftEyeBonePath = leftEyeBonePath ?? string.Empty,
                leftEyeInitialRotation = leftEyeInitialRotation,
                leftEyeYawAxisLocal = leftEyeYawAxisLocal,
                leftEyePitchAxisLocal = leftEyePitchAxisLocal,
                rightEyeBonePath = rightEyeBonePath ?? string.Empty,
                rightEyeInitialRotation = rightEyeInitialRotation,
                rightEyeYawAxisLocal = rightEyeYawAxisLocal,
                rightEyePitchAxisLocal = rightEyePitchAxisLocal,
                lookUpAngle = lookUpAngle,
                lookDownAngle = lookDownAngle,
                outerYawAngle = outerYawAngle,
                innerYawAngle = innerYawAngle
            };
        }
    }
}
