namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// カスタムカーブ用キーフレーム（Unity の Keyframe 全フィールドを保持）
    /// </summary>
    public readonly struct CurveKeyFrame
    {
        public float Time { get; }
        public float Value { get; }
        public float InTangent { get; }
        public float OutTangent { get; }
        public float InWeight { get; }
        public float OutWeight { get; }
        public int WeightedMode { get; }

        public CurveKeyFrame(
            float time,
            float value,
            float inTangent,
            float outTangent,
            float inWeight = 0f,
            float outWeight = 0f,
            int weightedMode = 0)
        {
            Time = time;
            Value = value;
            InTangent = inTangent;
            OutTangent = outTangent;
            InWeight = inWeight;
            OutWeight = outWeight;
            WeightedMode = weightedMode;
        }
    }
}
