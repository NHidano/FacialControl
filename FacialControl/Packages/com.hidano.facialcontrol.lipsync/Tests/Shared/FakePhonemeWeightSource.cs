using System;
using System.Collections.Generic;
using Hidano.FacialControl.LipSync.Adapters;

namespace Hidano.FacialControl.LipSync.Tests.Shared
{
    /// <summary>
    /// <see cref="IPhonemeWeightSource"/> のテスト用 fake。
    /// 音量と音素別ウェイトを直接指定する。uLipSync 公式の音量正規化・SmoothDamp・
    /// sum=1 正規化を委譲した後の「確定値」を模す。
    /// </summary>
    public sealed class FakePhonemeWeightSource : IPhonemeWeightSource
    {
        private readonly Dictionary<string, float> _weights =
            new Dictionary<string, float>(StringComparer.Ordinal);

        public float CurrentVolume { get; set; }

        public void SetPhonemeWeight(string phonemeId, float weight)
        {
            if (string.IsNullOrEmpty(phonemeId))
            {
                throw new ArgumentException("Phoneme id must be non-empty.", nameof(phonemeId));
            }

            _weights[phonemeId] = weight;
        }

        public void SetFrame(float volume, params (string PhonemeId, float Weight)[] weights)
        {
            CurrentVolume = volume;
            for (int i = 0; i < weights.Length; i++)
            {
                SetPhonemeWeight(weights[i].PhonemeId, weights[i].Weight);
            }
        }

        public void Clear()
        {
            _weights.Clear();
            CurrentVolume = 0f;
        }

        public bool TryGetPhonemeWeight(string phonemeId, out float weight)
        {
            if (phonemeId != null && _weights.TryGetValue(phonemeId, out weight))
            {
                return true;
            }

            weight = 0f;
            return false;
        }
    }
}
