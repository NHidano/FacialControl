namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// 遷移カーブの種類
    /// </summary>
    public enum TransitionCurveType
    {
        /// <summary>
        /// 線形補間（デフォルト）
        /// </summary>
        Linear,

        /// <summary>
        /// 開始が緩やか
        /// </summary>
        EaseIn,

        /// <summary>
        /// 終了が緩やか
        /// </summary>
        EaseOut,

        /// <summary>
        /// 開始と終了が緩やか
        /// </summary>
        EaseInOut,

        /// <summary>
        /// ユーザー定義のカスタムカーブ
        /// </summary>
        Custom
    }
}
