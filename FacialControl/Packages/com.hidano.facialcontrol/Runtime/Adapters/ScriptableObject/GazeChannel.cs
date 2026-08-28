using System;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.ScriptableObject
{
    /// <summary>Gaze セクションで定義する 1 系統のチャネル。</summary>
    [Serializable]
    public sealed class GazeChannel
    {
        public string id = string.Empty;
        public string providerSlug = string.Empty;
        public bool useDistinctLeftRight;
        public string sourceIdLeft = string.Empty;
        public string sourceIdRight = string.Empty;

        public string leftEyeBonePath = string.Empty;
        public Vector3 leftEyeInitialRotation;
        public Vector3 leftEyeYawAxisLocal = Vector3.up;
        public Vector3 leftEyePitchAxisLocal = Vector3.right;
        public string rightEyeBonePath = string.Empty;
        public Vector3 rightEyeInitialRotation;
        public Vector3 rightEyeYawAxisLocal = Vector3.up;
        public Vector3 rightEyePitchAxisLocal = Vector3.right;

        [Range(0f, 90f)] public float lookUpAngle = 15f;
        [Range(0f, 90f)] public float lookDownAngle = 9f;
        [Range(0f, 90f)] public float outerYawAngle = 15f;
        [Range(0f, 90f)] public float innerYawAngle = 18f;
    }
}
