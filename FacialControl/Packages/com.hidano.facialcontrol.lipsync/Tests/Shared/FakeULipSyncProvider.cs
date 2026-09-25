using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Interfaces;

namespace Hidano.FacialControl.LipSync.Tests.Shared
{
    public sealed class FakeULipSyncProvider : ILipSyncProvider, ILipSyncContributeMaskProvider
    {
        private const float ContributeThreshold = 1e-4f;

        private readonly Dictionary<string, float[]> _weightsByPhoneme = new Dictionary<string, float[]>(StringComparer.Ordinal);
        private readonly List<string> _phonemeOrder = new List<string>();
        private readonly string[] _blendShapeNames;
        private readonly BitArray _contributeMask;
        private readonly Dictionary<string, BitArray> _maskByPhoneme = new Dictionary<string, BitArray>(StringComparer.Ordinal);

        public FakeULipSyncProvider(int blendShapeCount)
        {
            if (blendShapeCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(blendShapeCount));
            }

            _blendShapeNames = new string[blendShapeCount];
            for (int i = 0; i < _blendShapeNames.Length; i++)
            {
                _blendShapeNames[i] = string.Empty;
            }

            _contributeMask = new BitArray(blendShapeCount, false);
            IsActive = true;
        }

        public bool IsActive { get; private set; }

        public ReadOnlySpan<string> BlendShapeNames => _blendShapeNames;

        public BitArray ContributeMask => _contributeMask;

        public void SetActive(bool isActive)
        {
            IsActive = isActive;
        }

        public void SetPhonemeWeights(string phonemeId, params float[] weights)
        {
            if (string.IsNullOrEmpty(phonemeId))
            {
                throw new ArgumentException("Phoneme id must be non-empty.", nameof(phonemeId));
            }

            if (!_weightsByPhoneme.ContainsKey(phonemeId))
            {
                _phonemeOrder.Add(phonemeId);
            }

            _weightsByPhoneme[phonemeId] = CopyWeights(weights);
            RebuildMasks();
        }

        public bool TryGetPhonemeIndex(string phonemeId, out int index)
        {
            index = -1;
            if (string.IsNullOrEmpty(phonemeId))
            {
                return false;
            }

            for (int i = 0; i < _phonemeOrder.Count; i++)
            {
                if (string.Equals(_phonemeOrder[i], phonemeId, StringComparison.Ordinal))
                {
                    index = i;
                    return true;
                }
            }

            return false;
        }

        public BitArray GetPhonemeContributeMask(string phonemeId)
        {
            return _maskByPhoneme.TryGetValue(phonemeId, out BitArray mask)
                ? mask
                : null;
        }

        public bool TryComposePhonemeWeights(string phonemeId, Span<float> output)
        {
            output.Clear();
            if (!IsActive || !_weightsByPhoneme.TryGetValue(phonemeId, out float[] weights))
            {
                return false;
            }

            CopyTo(weights, output);
            return true;
        }

        public void GetLipSyncValues(Span<float> output)
        {
            output.Clear();
            if (!IsActive)
            {
                return;
            }

            for (int i = 0; i < _phonemeOrder.Count; i++)
            {
                float[] weights = _weightsByPhoneme[_phonemeOrder[i]];
                int count = weights.Length < output.Length ? weights.Length : output.Length;
                for (int k = 0; k < count; k++)
                {
                    output[k] += weights[k];
                }
            }
        }

        private float[] CopyWeights(float[] weights)
        {
            var copy = new float[_blendShapeNames.Length];
            if (weights == null)
            {
                return copy;
            }

            int count = weights.Length < copy.Length ? weights.Length : copy.Length;
            Array.Copy(weights, copy, count);
            return copy;
        }

        private void RebuildMasks()
        {
            _contributeMask.SetAll(false);
            _maskByPhoneme.Clear();

            for (int i = 0; i < _phonemeOrder.Count; i++)
            {
                string phonemeId = _phonemeOrder[i];
                float[] weights = _weightsByPhoneme[phonemeId];
                var mask = new BitArray(_blendShapeNames.Length, false);
                for (int k = 0; k < weights.Length; k++)
                {
                    float weight = weights[k];
                    if (weight < 0f) weight = -weight;
                    if (weight > ContributeThreshold)
                    {
                        mask[k] = true;
                        _contributeMask[k] = true;
                    }
                }

                _maskByPhoneme[phonemeId] = mask;
            }
        }

        private static void CopyTo(float[] source, Span<float> output)
        {
            int count = source.Length < output.Length ? source.Length : output.Length;
            for (int i = 0; i < count; i++)
            {
                output[i] = source[i];
            }
        }
    }
}
