using System;

namespace Hidano.FacialControl.LipSync.Adapters.PhonemeEntries
{
    [Serializable]
    public abstract class PhonemeEntryBase
    {
        public string PhonemeId;
        public float MaxWeight;
    }
}
