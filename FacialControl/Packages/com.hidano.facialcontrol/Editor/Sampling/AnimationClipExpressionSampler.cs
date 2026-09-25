using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.Editor.Sampling
{
    /// <summary>
    /// <see cref="IExpressionAnimationClipSampler"/> の唯一の実装。
    /// stateless、同一 clip に対し常に同一結果。
    /// <para>
    /// AnimationUtility.GetCurveBindings + GetEditorCurve.Evaluate(0f) で
    /// 時刻 0 の BlendShape / Transform 値を取得する（research.md Topic 5、Decision 2）。
    /// AnimationEvent 経由のメタデータ抽出（<see cref="MetaSetFunctionName"/>）で
    /// TransitionDuration / TransitionCurvePreset を上書きする。
    /// 不在時は TransitionDuration=<see cref="Expression.DefaultTransitionDuration"/> (1/15), TransitionCurvePreset=Linear をデフォルトとして返す。
    /// </para>
    /// <para>
    /// BlendShape: <c>propertyName.StartsWith("blendShape.")</c> で判別。
    /// AnimationClip 上の <c>blendShape.*</c> カーブは Unity 標準の 0..100 スケールで記録されるため、
    /// snapshot へは <see cref="BlendShapeWeightScale"/> (=100) で除算した正規化値 0..1 として格納する
    /// （ドメイン <c>BlendShapeMapping.Value</c> / runtime apply 側の <c>×100</c> と整合させる）。
    /// Transform: <c>m_LocalPosition.{x,y,z}</c> / <c>m_LocalScale.{x,y,z}</c> /
    /// <c>localEulerAnglesRaw.{x,y,z}</c> / <c>localEulerAngles.{x,y,z}</c> /
    /// <c>m_LocalRotation.{x,y,z,w}</c>（quaternion → euler 変換）で判別。
    /// それ以外は <c>Debug.LogWarning</c> + skip。
    /// </para>
    /// </summary>
    public sealed class AnimationClipExpressionSampler : IExpressionAnimationClipSampler
    {
        /// <summary>
        /// AnimationClip メタデータ運搬用の予約 AnimationEvent functionName。
        /// stringParameter で key 識別、floatParameter で値運搬する。
        /// </summary>
        /// <remarks>
        /// preview.1 以降の sampler はこの AnimationEvent メタを読み取らない。
        /// 既存 Clip や外部参照との互換のため定数の公開だけを維持する。
        /// </remarks>
        public const string MetaSetFunctionName = "FacialControlMeta_Set";

        private const float DefaultTransitionDuration = Expression.DefaultTransitionDuration;
        private const TransitionCurvePreset DefaultTransitionCurve = TransitionCurvePreset.Linear;

        private const string BlendShapePropertyPrefix = "blendShape.";

        /// <summary>
        /// Unity の <c>blendShape.*</c> アニメーションカーブ / <c>SkinnedMeshRenderer</c> weight のスケール (0..100)。
        /// snapshot / ドメインは正規化 0..1 を採用するため、サンプリング時に本値で除算する。
        /// </summary>
        private const float BlendShapeWeightScale = 100f;

        private const string LocalPositionPrefix = "m_LocalPosition.";
        private const string LocalScalePrefix = "m_LocalScale.";
        private const string LocalRotationPrefix = "m_LocalRotation.";
        private const string LocalEulerAnglesPrefix = "localEulerAngles.";
        private const string LocalEulerAnglesRawPrefix = "localEulerAnglesRaw.";

        public ExpressionSnapshot SampleSnapshot(string snapshotId, AnimationClip clip)
        {
            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip));
            }

            var bindings = AnimationUtility.GetCurveBindings(clip);

            var blendShapes = new List<BlendShapeSnapshot>(bindings.Length);
            var rendererPaths = new List<string>();
            var rendererPathSet = new HashSet<string>(StringComparer.Ordinal);
            var bonesByPath = new Dictionary<string, BoneAccumulator>(StringComparer.Ordinal);

            for (int i = 0; i < bindings.Length; i++)
            {
                var binding = bindings[i];
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null)
                {
                    continue;
                }

                float value = curve.Evaluate(0f);

                if (TryParseBlendShape(binding, out var blendShapeName))
                {
                    // blendShape カーブは 0..100 スケール。snapshot / ドメインの正規化 0..1 へ変換する。
                    blendShapes.Add(new BlendShapeSnapshot(binding.path, blendShapeName, value / BlendShapeWeightScale));
                    if (rendererPathSet.Add(binding.path))
                    {
                        rendererPaths.Add(binding.path);
                    }
                }
                else if (TryParseTransform(binding, out var component))
                {
                    if (!bonesByPath.TryGetValue(binding.path, out var accumulator))
                    {
                        accumulator = BoneAccumulator.CreateDefault();
                    }
                    accumulator.Set(component, value);
                    bonesByPath[binding.path] = accumulator;
                }
                else
                {
                    Debug.LogWarning(
                        $"[AnimationClipExpressionSampler] Unsupported binding skipped: " +
                        $"path='{binding.path}', type={binding.type?.Name}, propertyName='{binding.propertyName}'.");
                }
            }

            var bones = new List<BoneSnapshot>(bonesByPath.Count);
            foreach (var kvp in bonesByPath)
            {
                bones.Add(kvp.Value.ToBoneSnapshot(kvp.Key));
            }

            ExtractTransitionMetadata(clip, out var transitionDuration, out var transitionCurve);

            return new ExpressionSnapshot(
                snapshotId,
                transitionDuration,
                transitionCurve,
                blendShapes.ToArray(),
                bones.ToArray(),
                rendererPaths.ToArray());
        }

        public ClipSummary SampleSummary(AnimationClip clip)
        {
            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip));
            }

            var bindings = AnimationUtility.GetCurveBindings(clip);

            var rendererPaths = new List<string>();
            var rendererPathSet = new HashSet<string>(StringComparer.Ordinal);
            var blendShapeNames = new List<string>();

            for (int i = 0; i < bindings.Length; i++)
            {
                var binding = bindings[i];
                if (!TryParseBlendShape(binding, out var blendShapeName))
                {
                    continue;
                }
                blendShapeNames.Add(blendShapeName);
                if (rendererPathSet.Add(binding.path))
                {
                    rendererPaths.Add(binding.path);
                }
            }

            ExtractTransitionMetadata(clip, out var transitionDuration, out var transitionCurve);

            return new ClipSummary(
                rendererPaths,
                blendShapeNames,
                transitionDuration,
                transitionCurve);
        }

        public bool TryResolveContributeIndices(
            AnimationClip clip,
            IReadOnlyList<string> blendShapeNames,
            BitArray output)
        {
            if (blendShapeNames == null)
            {
                throw new ArgumentNullException(nameof(blendShapeNames));
            }
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }
            if (output.Length != blendShapeNames.Count)
            {
                throw new ArgumentException(
                    "Output mask length must match blendShapeNames count.",
                    nameof(output));
            }
            if (clip == null || blendShapeNames.Count == 0)
            {
                return false;
            }

            var nameToIndex = new Dictionary<string, int>(blendShapeNames.Count, StringComparer.Ordinal);
            for (int i = 0; i < blendShapeNames.Count; i++)
            {
                var name = blendShapeNames[i];
                if (!string.IsNullOrEmpty(name) && !nameToIndex.ContainsKey(name))
                {
                    nameToIndex.Add(name, i);
                }
            }
            if (nameToIndex.Count == 0)
            {
                return false;
            }

            var indices = new List<int>();
            var bindings = AnimationUtility.GetCurveBindings(clip);
            for (int i = 0; i < bindings.Length; i++)
            {
                if (!TryParseBlendShape(bindings[i], out var blendShapeName))
                {
                    continue;
                }

                if (nameToIndex.TryGetValue(blendShapeName, out int index))
                {
                    indices.Add(index);
                }
            }

            if (indices.Count == 0)
            {
                return false;
            }

            output.SetAll(false);
            for (int i = 0; i < indices.Count; i++)
            {
                output.Set(indices[i], true);
            }

            return true;
        }

        private static void ExtractTransitionMetadata(
            AnimationClip clip,
            out float transitionDuration,
            out TransitionCurvePreset transitionCurve)
        {
            transitionDuration = DefaultTransitionDuration;
            transitionCurve = DefaultTransitionCurve;
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

        private static bool TryParseTransform(EditorCurveBinding binding, out TransformComponent component)
        {
            var propertyName = binding.propertyName;
            if (propertyName == null)
            {
                component = default;
                return false;
            }

            if (propertyName.StartsWith(LocalPositionPrefix, StringComparison.Ordinal))
            {
                return TryParseAxis(propertyName, LocalPositionPrefix.Length,
                    TransformComponent.PositionX, TransformComponent.PositionY, TransformComponent.PositionZ,
                    out component);
            }
            if (propertyName.StartsWith(LocalScalePrefix, StringComparison.Ordinal))
            {
                return TryParseAxis(propertyName, LocalScalePrefix.Length,
                    TransformComponent.ScaleX, TransformComponent.ScaleY, TransformComponent.ScaleZ,
                    out component);
            }
            if (propertyName.StartsWith(LocalEulerAnglesRawPrefix, StringComparison.Ordinal))
            {
                return TryParseAxis(propertyName, LocalEulerAnglesRawPrefix.Length,
                    TransformComponent.EulerX, TransformComponent.EulerY, TransformComponent.EulerZ,
                    out component);
            }
            if (propertyName.StartsWith(LocalEulerAnglesPrefix, StringComparison.Ordinal))
            {
                return TryParseAxis(propertyName, LocalEulerAnglesPrefix.Length,
                    TransformComponent.EulerX, TransformComponent.EulerY, TransformComponent.EulerZ,
                    out component);
            }
            if (propertyName.StartsWith(LocalRotationPrefix, StringComparison.Ordinal))
            {
                if (propertyName.Length != LocalRotationPrefix.Length + 1)
                {
                    component = default;
                    return false;
                }
                char axis = propertyName[LocalRotationPrefix.Length];
                switch (axis)
                {
                    case 'x': component = TransformComponent.QuatX; return true;
                    case 'y': component = TransformComponent.QuatY; return true;
                    case 'z': component = TransformComponent.QuatZ; return true;
                    case 'w': component = TransformComponent.QuatW; return true;
                    default:
                        component = default;
                        return false;
                }
            }

            component = default;
            return false;
        }

        private static bool TryParseAxis(
            string propertyName,
            int axisIndex,
            TransformComponent x,
            TransformComponent y,
            TransformComponent z,
            out TransformComponent component)
        {
            if (propertyName.Length != axisIndex + 1)
            {
                component = default;
                return false;
            }
            char axis = propertyName[axisIndex];
            switch (axis)
            {
                case 'x': component = x; return true;
                case 'y': component = y; return true;
                case 'z': component = z; return true;
                default:
                    component = default;
                    return false;
            }
        }

        private enum TransformComponent
        {
            PositionX,
            PositionY,
            PositionZ,
            EulerX,
            EulerY,
            EulerZ,
            ScaleX,
            ScaleY,
            ScaleZ,
            QuatX,
            QuatY,
            QuatZ,
            QuatW,
        }

        private struct BoneAccumulator
        {
            public float PositionX;
            public float PositionY;
            public float PositionZ;
            public float EulerX;
            public float EulerY;
            public float EulerZ;
            public float ScaleX;
            public float ScaleY;
            public float ScaleZ;
            public float QuatX;
            public float QuatY;
            public float QuatZ;
            public float QuatW;
            public bool HasEuler;
            public bool HasQuat;

            public static BoneAccumulator CreateDefault()
            {
                return new BoneAccumulator
                {
                    ScaleX = 1f,
                    ScaleY = 1f,
                    ScaleZ = 1f,
                    QuatW = 1f,
                };
            }

            public void Set(TransformComponent component, float value)
            {
                switch (component)
                {
                    case TransformComponent.PositionX: PositionX = value; break;
                    case TransformComponent.PositionY: PositionY = value; break;
                    case TransformComponent.PositionZ: PositionZ = value; break;
                    case TransformComponent.EulerX: EulerX = value; HasEuler = true; break;
                    case TransformComponent.EulerY: EulerY = value; HasEuler = true; break;
                    case TransformComponent.EulerZ: EulerZ = value; HasEuler = true; break;
                    case TransformComponent.ScaleX: ScaleX = value; break;
                    case TransformComponent.ScaleY: ScaleY = value; break;
                    case TransformComponent.ScaleZ: ScaleZ = value; break;
                    case TransformComponent.QuatX: QuatX = value; HasQuat = true; break;
                    case TransformComponent.QuatY: QuatY = value; HasQuat = true; break;
                    case TransformComponent.QuatZ: QuatZ = value; HasQuat = true; break;
                    case TransformComponent.QuatW: QuatW = value; HasQuat = true; break;
                }
            }

            public BoneSnapshot ToBoneSnapshot(string bonePath)
            {
                float ex = EulerX;
                float ey = EulerY;
                float ez = EulerZ;

                if (!HasEuler && HasQuat)
                {
                    var quat = new Quaternion(QuatX, QuatY, QuatZ, QuatW);
                    var euler = quat.eulerAngles;
                    ex = euler.x;
                    ey = euler.y;
                    ez = euler.z;
                }

                return new BoneSnapshot(
                    bonePath,
                    PositionX, PositionY, PositionZ,
                    ex, ey, ez,
                    ScaleX, ScaleY, ScaleZ);
            }
        }
    }
}
