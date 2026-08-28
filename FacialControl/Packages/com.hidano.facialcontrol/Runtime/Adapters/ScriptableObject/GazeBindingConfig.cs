using System;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.ScriptableObject
{
    /// <summary>目線入力と目ボーン制御の旧設定モデル。</summary>
    [Serializable]
    public class GazeBindingConfig
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

        [Range(0f, 90f)]
        public float lookUpAngle = 15f;
        [Range(0f, 90f)]
        public float lookDownAngle = 9f;
        [Range(0f, 90f)]
        public float outerYawAngle = 15f;
        [Range(0f, 90f)]
        public float innerYawAngle = 18f;
    }
}
