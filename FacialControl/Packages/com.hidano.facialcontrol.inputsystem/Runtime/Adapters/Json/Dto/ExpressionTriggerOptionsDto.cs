namespace Hidano.FacialControl.Adapters.Json.Dto
{
    /// <summary>
    /// 予約 ID <c>input</c> の入力源固有 options。
    /// <see cref="maxStackDepth"/> が 0 の場合は既定値 8 を使用する（D-14）。
    /// </summary>
    [System.Serializable]
    public sealed class ExpressionTriggerOptionsDto : InputSourceOptionsDto
    {
        /// <summary>
        /// Expression スタックの最大深度。0 のとき呼出側で既定値 8 を採用する。
        /// </summary>
        public int maxStackDepth;
    }
}
