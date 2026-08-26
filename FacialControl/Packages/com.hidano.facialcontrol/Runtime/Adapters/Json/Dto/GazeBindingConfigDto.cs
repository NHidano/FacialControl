using UnityEngine;

namespace Hidano.FacialControl.Adapters.Json.Dto
{
    [System.Serializable]
    public sealed class GazeBindingConfigDto
    {
        public string expressionId;
        public bool useDistinctLeftRight;
        public string sourceIdLeft;
        public string sourceIdRight;

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
    }
}
