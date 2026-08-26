using System;

namespace Hidano.FacialControl.Adapters.ScriptableObject
{
    /// <summary>
    /// Gaze 4 系統 (LookLeft / LookRight / LookUp / LookDown) AnimationClip の time=0 サンプル結果 1 件。
    /// AnimationClip の curve は runtime API で列挙できないため、Editor の AutoExporter で
    /// <see cref="UnityEditor.AnimationUtility"/> 経由でサンプルし、本配列に焼き付けて永続化する。
    /// runtime の <c>BuildAnalogProfile</c> は本配列から
    /// <see cref="Hidano.FacialControl.Domain.Models.AnalogBindingEntry"/> を生成する。
    /// </summary>
    [Serializable]
    public sealed class GazeBlendShapeSampleEntry
    {
        /// <summary>BlendShape 名。</summary>
        public string blendShapeName;

        /// <summary>clip の time=0 における weight（keyframe 値）。</summary>
        public float weight;
    }
}
