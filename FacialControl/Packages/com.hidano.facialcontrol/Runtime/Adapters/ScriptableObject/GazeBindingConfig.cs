using System;
using UnityEngine;
namespace Hidano.FacialControl.Adapters.ScriptableObject
{
    [Obsolete("Use GazeChannel.")]
    public class GazeBindingConfig
    {
        public string expressionId; public bool useDistinctLeftRight; public string sourceIdLeft = string.Empty; public string sourceIdRight = string.Empty;
        public string leftEyeBonePath; public Vector3 leftEyeInitialRotation; public Vector3 leftEyeYawAxisLocal = Vector3.up; public Vector3 leftEyePitchAxisLocal = Vector3.right;
        public string rightEyeBonePath; public Vector3 rightEyeInitialRotation; public Vector3 rightEyeYawAxisLocal = Vector3.up; public Vector3 rightEyePitchAxisLocal = Vector3.right;
        public float lookUpAngle = 15f, lookDownAngle = 9f, outerYawAngle = 15f, innerYawAngle = 18f;
    }
}
