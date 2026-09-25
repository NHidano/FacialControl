using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.Playable
{
    /// <summary>
    /// <see cref="SkinnedMeshRenderer"/> へ BlendShape を直書きする既定実装。
    /// </summary>
    public sealed class SkinnedMeshRendererBlendShapeWriter : IBlendShapeOutputWriter
    {
        private readonly BlendShapeTarget[][] _targetsByOutputIndex;

        private readonly struct BlendShapeTarget
        {
            public BlendShapeTarget(SkinnedMeshRenderer renderer, int blendShapeIndex)
            {
                Renderer = renderer;
                BlendShapeIndex = blendShapeIndex;
            }

            public SkinnedMeshRenderer Renderer { get; }

            public int BlendShapeIndex { get; }
        }

        public SkinnedMeshRendererBlendShapeWriter(
            SkinnedMeshRenderer[] renderers,
            IReadOnlyList<string> blendShapeNames)
        {
            if (blendShapeNames == null)
            {
                throw new ArgumentNullException(nameof(blendShapeNames));
            }

            _targetsByOutputIndex = BuildTargets(renderers, blendShapeNames);
        }

        public void Write(ReadOnlySpan<float> normalizedWeights)
        {
            int count = Math.Min(normalizedWeights.Length, _targetsByOutputIndex.Length);
            for (int outputIndex = 0; outputIndex < count; outputIndex++)
            {
                BlendShapeTarget[] targets = _targetsByOutputIndex[outputIndex];
                if (targets == null || targets.Length == 0)
                {
                    continue;
                }

                float weight = normalizedWeights[outputIndex] * 100f;
                for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
                {
                    BlendShapeTarget target = targets[targetIndex];
                    target.Renderer.SetBlendShapeWeight(target.BlendShapeIndex, weight);
                }
            }
        }

        public void Dispose()
        {
        }

        private static BlendShapeTarget[][] BuildTargets(
            SkinnedMeshRenderer[] renderers,
            IReadOnlyList<string> blendShapeNames)
        {
            var outputNameToIndex = new Dictionary<string, int>(blendShapeNames.Count);
            var targetsByOutputIndex = new List<BlendShapeTarget>[blendShapeNames.Count];

            for (int i = 0; i < blendShapeNames.Count; i++)
            {
                string blendShapeName = blendShapeNames[i];
                if (string.IsNullOrEmpty(blendShapeName) || outputNameToIndex.ContainsKey(blendShapeName))
                {
                    continue;
                }

                outputNameToIndex.Add(blendShapeName, i);
            }

            if (renderers != null)
            {
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    SkinnedMeshRenderer renderer = renderers[rendererIndex];
                    if (renderer == null || renderer.sharedMesh == null)
                    {
                        continue;
                    }

                    Mesh mesh = renderer.sharedMesh;
                    int blendShapeCount = mesh.blendShapeCount;
                    for (int meshBlendShapeIndex = 0; meshBlendShapeIndex < blendShapeCount; meshBlendShapeIndex++)
                    {
                        string blendShapeName = mesh.GetBlendShapeName(meshBlendShapeIndex);
                        if (!outputNameToIndex.TryGetValue(blendShapeName, out int outputIndex))
                        {
                            continue;
                        }

                        List<BlendShapeTarget> targets = targetsByOutputIndex[outputIndex];
                        if (targets == null)
                        {
                            targets = new List<BlendShapeTarget>();
                            targetsByOutputIndex[outputIndex] = targets;
                        }

                        targets.Add(new BlendShapeTarget(renderer, meshBlendShapeIndex));
                    }
                }
            }

            var result = new BlendShapeTarget[targetsByOutputIndex.Length][];
            for (int i = 0; i < targetsByOutputIndex.Length; i++)
            {
                result[i] = targetsByOutputIndex[i]?.ToArray() ?? Array.Empty<BlendShapeTarget>();
            }

            return result;
        }
    }
}
