using System;
using UnityEngine;

namespace Hidano.FacialControl.LipSync.Adapters.PhonemeEntries
{
    [Serializable]
    public sealed class AnimationClipPhonemeEntry : PhonemeEntryBase
    {
        public AnimationClip Clip;
    }
}
