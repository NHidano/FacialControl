namespace Hidano.FacialControl.LipSync.Adapters.PhonemeEntries
{
    public readonly struct PhonemeSnapshot
    {
        public readonly string PhonemeId;
        public readonly float[] Weights;

        public PhonemeSnapshot(string phonemeId, float[] weights)
        {
            PhonemeId = phonemeId;
            Weights = weights;
        }
    }
}
