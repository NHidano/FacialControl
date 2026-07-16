using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.EditorPreview;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using GazeBindingConfig = Hidano.FacialControl.Adapters.ScriptableObject.GazeBindingConfig;

namespace Hidano.FacialControl.Timeline.Editor
{
    [InitializeOnLoad]
    internal static class FacialTimelineEditorPreview
    {
        private const string MissingBakeMessage =
            "[FacialTimelineEditorPreview] BakeAsset is missing. Scrub preview is disabled.";

        private static readonly HashSet<int> MissingBakeWarnings = new HashSet<int>();

        static FacialTimelineEditorPreview()
        {
            FacialTimelineEditorPreviewBridge.ApplyPreview = ApplyPreview;
            FacialTimelineEditorPreviewBridge.GatherProperties = GatherProperties;
        }

        internal static void ApplyPreview(FacialTimelineReceiver receiver, TimelineAsset timeline, double timeSeconds)
        {
            if (receiver == null)
            {
                return;
            }

            FacialTimelineBakeAsset bakeAsset = receiver.BakeAsset;
            if (bakeAsset == null)
            {
                WarnMissingBake(receiver);
                return;
            }

            MissingBakeWarnings.Remove(receiver.GetInstanceID());

            FacialController controller = ResolveController(receiver);
            if (controller == null)
            {
                return;
            }

            ApplyBlendShapes(controller, bakeAsset, timeSeconds);
            ApplyGaze(controller, bakeAsset, timeSeconds);
        }

        internal static void GatherProperties(PlayableDirector director, TrackAsset track, IPropertyCollector collector)
        {
            if (director == null || track == null || collector == null)
            {
                return;
            }

            FacialTimelineReceiver receiver = ResolveBoundReceiver(director, track);
            if (receiver == null)
            {
                return;
            }

            FacialController controller = ResolveController(receiver);
            if (controller == null)
            {
                return;
            }

            RegisterBlendShapeProperties(controller, collector);
            RegisterGazeProperties(controller, collector);
        }

        private static void ApplyBlendShapes(FacialController controller, FacialTimelineBakeAsset bakeAsset, double timeSeconds)
        {
            SkinnedMeshRenderer[] renderers = controller.SkinnedMeshRenderers;
            if (renderers == null || renderers.Length == 0)
            {
                return;
            }

            var sampledWeights = new Dictionary<string, float>(StringComparer.Ordinal);
            ExpressionSourceBake[] expressionBakes = bakeAsset.ExpressionBakes ?? Array.Empty<ExpressionSourceBake>();
            for (int i = 0; i < expressionBakes.Length; i++)
            {
                BlendShapeCurve[] curves = expressionBakes[i]?.Curves ?? Array.Empty<BlendShapeCurve>();
                for (int j = 0; j < curves.Length; j++)
                {
                    BlendShapeCurve curve = curves[j];
                    if (curve == null || string.IsNullOrEmpty(curve.BlendShapeName) || curve.Curve == null)
                    {
                        continue;
                    }

                    float value = curve.Curve.Evaluate((float)timeSeconds);
                    if (sampledWeights.TryGetValue(curve.BlendShapeName, out float existing))
                    {
                        sampledWeights[curve.BlendShapeName] = existing + value;
                    }
                    else
                    {
                        sampledWeights[curve.BlendShapeName] = value;
                    }
                }
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                Mesh mesh = renderer != null ? renderer.sharedMesh : null;
                if (renderer == null || mesh == null)
                {
                    continue;
                }

                for (int shapeIndex = 0; shapeIndex < mesh.blendShapeCount; shapeIndex++)
                {
                    string blendShapeName = mesh.GetBlendShapeName(shapeIndex);
                    sampledWeights.TryGetValue(blendShapeName, out float weight);
                    renderer.SetBlendShapeWeight(shapeIndex, Mathf.Clamp01(weight) * 100f);
                }
            }
        }

        private static void ApplyGaze(FacialController controller, FacialTimelineBakeAsset bakeAsset, double timeSeconds)
        {
            IReadOnlyList<GazeBindingConfig> gazeConfigs = controller.CharacterSO != null
                ? controller.CharacterSO.GazeConfigs
                : Array.Empty<GazeBindingConfig>();
            if (gazeConfigs == null || gazeConfigs.Count == 0)
            {
                return;
            }

            ValueChannelBake[] gazeChannels = CollectGazeChannels(bakeAsset);
            if (gazeChannels.Length == 0)
            {
                return;
            }

            for (int i = 0; i < gazeConfigs.Count && i < gazeChannels.Length; i++)
            {
                GazeBindingConfig config = gazeConfigs[i];
                if (config == null)
                {
                    continue;
                }

                float x = EvaluateAxis(gazeChannels[i].Axes, 0, timeSeconds);
                float y = EvaluateAxis(gazeChannels[i].Axes, 1, timeSeconds);
                ApplyEyeRotation(controller.transform, config.leftEyeBonePath, true, config, x, y);
                ApplyEyeRotation(controller.transform, config.rightEyeBonePath, false, config, x, y);
            }
        }

        private static ValueChannelBake[] CollectGazeChannels(FacialTimelineBakeAsset bakeAsset)
        {
            ValueChannelBake[] channels = bakeAsset.ValueBakes ?? Array.Empty<ValueChannelBake>();
            if (channels.Length == 0)
            {
                return Array.Empty<ValueChannelBake>();
            }

            var gazeChannels = new List<ValueChannelBake>(channels.Length);
            for (int i = 0; i < channels.Length; i++)
            {
                if (channels[i] != null && channels[i].IsGaze)
                {
                    gazeChannels.Add(channels[i]);
                }
            }

            return gazeChannels.Count == 0 ? Array.Empty<ValueChannelBake>() : gazeChannels.ToArray();
        }

        private static float EvaluateAxis(AnimationCurve[] axes, int axisIndex, double timeSeconds)
        {
            if (axes == null || axisIndex >= axes.Length || axes[axisIndex] == null)
            {
                return 0f;
            }

            return Mathf.Clamp(axes[axisIndex].Evaluate((float)timeSeconds), -1f, 1f);
        }

        private static void ApplyEyeRotation(
            Transform root,
            string bonePath,
            bool isLeftEye,
            GazeBindingConfig config,
            float x,
            float y)
        {
            if (root == null || string.IsNullOrWhiteSpace(bonePath))
            {
                return;
            }

            Transform target = root.Find(bonePath);
            if (target == null)
            {
                return;
            }

            Vector3 yawAxis = SafeNormalize(
                isLeftEye ? config.leftEyeYawAxisLocal : config.rightEyeYawAxisLocal,
                Vector3.up);
            Vector3 pitchAxis = SafeNormalize(
                isLeftEye ? config.leftEyePitchAxisLocal : config.rightEyePitchAxisLocal,
                Vector3.right);
            Quaternion restRotation = Quaternion.Euler(
                isLeftEye ? config.leftEyeInitialRotation : config.rightEyeInitialRotation);

            float yaw = ComputeYawDegrees(isLeftEye, config, x);
            float pitch = y >= 0f ? y * Mathf.Max(0f, config.lookUpAngle) : y * Mathf.Max(0f, config.lookDownAngle);

            target.localRotation =
                Quaternion.AngleAxis(-yaw, yawAxis) *
                Quaternion.AngleAxis(-pitch, pitchAxis) *
                restRotation;
        }

        private static float ComputeYawDegrees(bool isLeftEye, GazeBindingConfig config, float x)
        {
            float outerYaw = Mathf.Max(0f, config.outerYawAngle);
            float innerYaw = Mathf.Max(0f, config.innerYawAngle);
            if (isLeftEye)
            {
                return x >= 0f ? x * outerYaw : x * innerYaw;
            }

            return x >= 0f ? x * innerYaw : x * outerYaw;
        }

        private static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
        {
            return value.sqrMagnitude < 1e-8f ? fallback : value.normalized;
        }

        private static void RegisterBlendShapeProperties(FacialController controller, IPropertyCollector collector)
        {
            SkinnedMeshRenderer[] renderers = controller.SkinnedMeshRenderers;
            if (renderers == null)
            {
                return;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                Mesh mesh = renderer != null ? renderer.sharedMesh : null;
                if (renderer == null || mesh == null)
                {
                    continue;
                }

                for (int shapeIndex = 0; shapeIndex < mesh.blendShapeCount; shapeIndex++)
                {
                    collector.AddFromName<SkinnedMeshRenderer>(renderer.gameObject, "blendShape." + mesh.GetBlendShapeName(shapeIndex));
                }
            }
        }

        private static void RegisterGazeProperties(FacialController controller, IPropertyCollector collector)
        {
            IReadOnlyList<GazeBindingConfig> gazeConfigs = controller.CharacterSO != null
                ? controller.CharacterSO.GazeConfigs
                : Array.Empty<GazeBindingConfig>();
            for (int i = 0; i < gazeConfigs.Count; i++)
            {
                GazeBindingConfig config = gazeConfigs[i];
                if (config == null)
                {
                    continue;
                }

                RegisterRotationProperties(controller.transform.Find(config.leftEyeBonePath), collector);
                RegisterRotationProperties(controller.transform.Find(config.rightEyeBonePath), collector);
            }
        }

        private static void RegisterRotationProperties(Transform target, IPropertyCollector collector)
        {
            if (target == null)
            {
                return;
            }

            collector.AddFromName<Transform>(target.gameObject, "m_LocalRotation.x");
            collector.AddFromName<Transform>(target.gameObject, "m_LocalRotation.y");
            collector.AddFromName<Transform>(target.gameObject, "m_LocalRotation.z");
            collector.AddFromName<Transform>(target.gameObject, "m_LocalRotation.w");
        }

        private static FacialTimelineReceiver ResolveBoundReceiver(PlayableDirector director, TrackAsset track)
        {
            for (TrackAsset current = track; current != null; current = current.parent as TrackAsset)
            {
                switch (director.GetGenericBinding(current))
                {
                    case FacialTimelineReceiver receiver:
                        return receiver;
                    case GameObject gameObject:
                        return gameObject.GetComponent<FacialTimelineReceiver>();
                    case Component component:
                        return component.GetComponent<FacialTimelineReceiver>();
                }
            }

            return null;
        }

        private static FacialController ResolveController(FacialTimelineReceiver receiver)
        {
            return receiver.GetComponent<FacialController>()
                   ?? receiver.GetComponentInParent<FacialController>()
                   ?? receiver.GetComponentInChildren<FacialController>();
        }

        private static void WarnMissingBake(FacialTimelineReceiver receiver)
        {
            if (receiver == null || !MissingBakeWarnings.Add(receiver.GetInstanceID()))
            {
                return;
            }

            Debug.LogWarning(MissingBakeMessage, receiver);
        }
    }
}
