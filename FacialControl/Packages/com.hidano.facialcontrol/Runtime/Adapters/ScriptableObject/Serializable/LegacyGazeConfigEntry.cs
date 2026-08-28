using System;
using UnityEngine;
namespace Hidano.FacialControl.Adapters.ScriptableObject.Serializable
{
    [Serializable] public sealed class LegacyGazeConfigEntry
    {
        public string expressionId; public bool useDistinctLeftRight; public string sourceIdLeft = string.Empty; public string sourceIdRight = string.Empty;
        public string leftEyeBonePath; public Vector3 leftEyeInitialRotation; public Vector3 leftEyeYawAxisLocal = Vector3.up; public Vector3 leftEyePitchAxisLocal = Vector3.right;
        public string rightEyeBonePath; public Vector3 rightEyeInitialRotation; public Vector3 rightEyeYawAxisLocal = Vector3.up; public Vector3 rightEyePitchAxisLocal = Vector3.right;
        public float lookUpAngle = 15f, lookDownAngle = 9f, outerYawAngle = 15f, innerYawAngle = 18f;
        public GazeChannel ToChannel() => new GazeChannel { id = expressionId ?? string.Empty, useDistinctLeftRight = useDistinctLeftRight, sourceIdLeft = sourceIdLeft ?? string.Empty, sourceIdRight = sourceIdRight ?? string.Empty, leftEyeBonePath = leftEyeBonePath ?? string.Empty, leftEyeInitialRotation = leftEyeInitialRotation, leftEyeYawAxisLocal = leftEyeYawAxisLocal, leftEyePitchAxisLocal = leftEyePitchAxisLocal, rightEyeBonePath = rightEyeBonePath ?? string.Empty, rightEyeInitialRotation = rightEyeInitialRotation, rightEyeYawAxisLocal = rightEyeYawAxisLocal, rightEyePitchAxisLocal = rightEyePitchAxisLocal, lookUpAngle = lookUpAngle, lookDownAngle = lookDownAngle, outerYawAngle = outerYawAngle, innerYawAngle = innerYawAngle };
    }
}
