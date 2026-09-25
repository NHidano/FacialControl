using System;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.ScriptableObject.Serializable
{
    /// <summary>
    /// カスタム遷移カーブのキーフレーム (旧 JSON: transitionCurve.keys[])。
    /// <see cref="Hidano.FacialControl.Domain.Models.CurveKeyFrame"/> の Unity Serializable 投影。
    /// </summary>
    [Serializable]
    public sealed class CurveKeyFrameSerializable
    {
        [Tooltip("時刻 (0〜1 正規化)。")]
        public float time;

        [Tooltip("値。")]
        public float value;

        [Tooltip("接線 (in)。")]
        public float inTangent;

        [Tooltip("接線 (out)。")]
        public float outTangent;

        [Tooltip("ウェイト (in)。weightedMode を使うときのみ意味を持つ。")]
        public float inWeight;

        [Tooltip("ウェイト (out)。weightedMode を使うときのみ意味を持つ。")]
        public float outWeight;

        [Tooltip("Unity AnimationCurve の WeightedMode (0=None, 1=In, 2=Out, 3=Both)。")]
        public int weightedMode;
    }
}
