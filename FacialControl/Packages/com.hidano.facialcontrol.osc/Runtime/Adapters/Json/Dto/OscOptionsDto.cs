namespace Hidano.FacialControl.Adapters.Json.Dto
{
    /// <summary>
    /// 予約 ID <c>osc</c> の入力源固有 options。
    /// <see cref="stalenessSeconds"/> が 0 の場合は無制限保持（D-8 の従来挙動）。
    /// </summary>
    [System.Serializable]
    public sealed class OscOptionsDto : InputSourceOptionsDto
    {
        /// <summary>
        /// OSC 受信データの有効期限（秒）。0 のとき staleness 判定を行わない。
        /// </summary>
        public float stalenessSeconds;
    }
}
