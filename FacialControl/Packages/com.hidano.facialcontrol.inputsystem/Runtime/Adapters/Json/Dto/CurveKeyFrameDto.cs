namespace Hidano.FacialControl.Adapters.Json.Dto
{
    /// <summary>
    /// アナログマッピングの Custom カーブ用キーフレーム DTO。
    /// 既存 <see cref="Hidano.FacialControl.Domain.Models.CurveKeyFrame"/> と 1:1 対応する。
    /// JsonUtility 互換のため class とし、フィールドは public（camelCase で永続化キーに整合）。
    /// </summary>
    [System.Serializable]
    public sealed class CurveKeyFrameDto
    {
        /// <summary>キーフレームの時間位置。</summary>
        public float time;

        /// <summary>キーフレームの値。</summary>
        public float value;

        /// <summary>入力側タンジェント。</summary>
        public float inTangent;

        /// <summary>出力側タンジェント。</summary>
        public float outTangent;

        /// <summary>入力側ウェイト。</summary>
        public float inWeight;

        /// <summary>出力側ウェイト。</summary>
        public float outWeight;

        /// <summary>ウェイトモード（Unity の WeightedMode に対応）。</summary>
        public int weightedMode;
    }
}
