using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.Editor.Sampling
{
    /// <summary>
    /// Gaze 用 4 系統 (LookLeft / LookRight / LookUp / LookDown) AnimationClip から
    /// BlendShape curve の time=0 における値を取り出して
    /// <see cref="Hidano.FacialControl.Domain.Models.AnalogBindingEntry"/> に変換するための
    /// Editor 専用ヘルパ。
    /// </summary>
    /// <remarks>
    /// <para>
    /// AnimationClip から取り出すのは「BlendShape 名」と「time=0 における weight 値」のみ。
    /// renderer path は無視する（runtime 側の <c>AnalogBlendShapeInputSource</c> は BlendShape 名で逆引きするため）。
    /// 同名 BS が複数 renderer に出現する場合は最初に見つかった weight を採用（後勝ちにしない）。
    /// </para>
    /// <para>
    /// Editor 専用 (UnityEditor.AnimationUtility 参照)。runtime asmdef からは不可視。
    /// </para>
    /// </remarks>
    public static class GazeClipBlendShapeSampler
    {
        private const string BlendShapePropertyPrefix = "blendShape.";

        /// <summary>
        /// AnimationClip 内の BlendShape curve を time=0 でサンプルして
        /// (blendShapeName, weight) ペア列を返す。
        /// </summary>
        /// <param name="clip">サンプル対象の AnimationClip。null の場合は空列を返す。</param>
        /// <returns>BlendShape 名と weight の組。weight が 0 のものも含む（呼出側で間引く想定）。</returns>
        public static IReadOnlyList<BlendShapeSample> Sample(AnimationClip clip)
        {
            if (clip == null) return Array.Empty<BlendShapeSample>();

            var bindings = AnimationUtility.GetCurveBindings(clip);
            if (bindings == null || bindings.Length == 0) return Array.Empty<BlendShapeSample>();

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<BlendShapeSample>(bindings.Length);

            for (int i = 0; i < bindings.Length; i++)
            {
                var binding = bindings[i];
                if (!TryParseBlendShape(binding, out var blendShapeName)) continue;
                if (!seen.Add(blendShapeName)) continue;

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null) continue;

                float weight = curve.Evaluate(0f);
                result.Add(new BlendShapeSample(blendShapeName, weight));
            }

            return result;
        }

        private static bool TryParseBlendShape(EditorCurveBinding binding, out string blendShapeName)
        {
            var propertyName = binding.propertyName;
            if (propertyName != null && propertyName.StartsWith(BlendShapePropertyPrefix, StringComparison.Ordinal))
            {
                blendShapeName = propertyName.Substring(BlendShapePropertyPrefix.Length);
                return blendShapeName.Length > 0;
            }
            blendShapeName = null;
            return false;
        }

        /// <summary>BlendShape 名と sampling 結果 weight のペア。</summary>
        public readonly struct BlendShapeSample
        {
            public string BlendShapeName { get; }
            public float Weight { get; }

            public BlendShapeSample(string blendShapeName, float weight)
            {
                BlendShapeName = blendShapeName;
                Weight = weight;
            }
        }
    }
}
