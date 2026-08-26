using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Editor.Sampling
{
    /// <summary>
    /// AnimationClip → <see cref="ExpressionSnapshot"/> サンプラ。
    /// Editor 専用。Runtime asmdef からは不可視。
    /// <para>
    /// 実装は stateless で、同一 clip に対して常に同一結果を返す。
    /// Domain は <c>UnityEngine.AnimationClip</c> を直接参照しないため、本 interface が
    /// AnimationClip と Domain snapshot の境界を担う。
    /// </para>
    /// </summary>
    public interface IExpressionAnimationClipSampler
    {
        /// <summary>
        /// AnimationClip 内の全 binding を時刻 0 で評価し、ExpressionSnapshot を返す。
        /// </summary>
        /// <param name="snapshotId">生成する snapshot の Id（通常 Expression.Id と同値）</param>
        /// <param name="clip">サンプリング対象の AnimationClip</param>
        /// <returns>時刻 0 における BlendShape 値 + Bone 姿勢 + RendererPath サマリを保持する snapshot</returns>
        ExpressionSnapshot SampleSnapshot(string snapshotId, AnimationClip clip);

        /// <summary>
        /// Inspector の read-only summary 表示用に AnimationClip の概要を返す。
        /// </summary>
        /// <param name="clip">サンプリング対象の AnimationClip</param>
        /// <returns>RendererPath / BlendShape 名 / 遷移メタの summary</returns>
        ClipSummary SampleSummary(AnimationClip clip);
    }

    /// <summary>
    /// Inspector 表示用の AnimationClip 概要。
    /// </summary>
    public readonly struct ClipSummary
    {
        public IReadOnlyList<string> RendererPaths { get; }
        public IReadOnlyList<string> BlendShapeNames { get; }
        public float TransitionDuration { get; }
        public TransitionCurvePreset TransitionCurve { get; }

        public ClipSummary(
            IReadOnlyList<string> rendererPaths,
            IReadOnlyList<string> blendShapeNames,
            float transitionDuration,
            TransitionCurvePreset transitionCurve)
        {
            RendererPaths = rendererPaths;
            BlendShapeNames = blendShapeNames;
            TransitionDuration = transitionDuration;
            TransitionCurve = transitionCurve;
        }
    }
}
