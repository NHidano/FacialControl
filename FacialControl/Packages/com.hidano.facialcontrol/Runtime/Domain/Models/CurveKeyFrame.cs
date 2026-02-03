namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// カスタムカーブ用キーフレーム（Unity の Keyframe 全フィールドを保持）
    /// </summary>
    public readonly struct CurveKeyFrame
    {
        /// <summary>キーフレームの時間位置</summary>
        public float Time { get; }

        /// <summary>キーフレームの値</summary>
        public float Value { get; }

        /// <summary>入力側タンジェント</summary>
        public float InTangent { get; }

        /// <summary>出力側タンジェント</summary>
        public float OutTangent { get; }

        /// <summary>入力側ウェイト</summary>
        public float InWeight { get; }

        /// <summary>出力側ウェイト</summary>
        public float OutWeight { get; }

        /// <summary>ウェイトモード（Unity の WeightedMode に対応）</summary>
        public int WeightedMode { get; }

        /// <summary>
        /// カスタムカーブ用キーフレームを生成する。
        /// </summary>
        /// <param name="time">時間位置</param>
        /// <param name="value">値</param>
        /// <param name="inTangent">入力側タンジェント</param>
        /// <param name="outTangent">出力側タンジェント</param>
        /// <param name="inWeight">入力側ウェイト</param>
        /// <param name="outWeight">出力側ウェイト</param>
        /// <param name="weightedMode">ウェイトモード</param>
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
