namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// 入力源アダプタの内部分類。D-1 ハイブリッドモデルの 2 系統を表す。
    /// </summary>
    public enum InputSourceType
    {
        /// <summary>
        /// Expression トリガー型。内部に専用の Expression スタックと TransitionCalculator を持つ
        /// （例: <c>input</c>）。
        /// </summary>
        ExpressionTrigger,

        /// <summary>
        /// BlendShape 値提供型。外部から受信した値をそのまま書込む
        /// （例: <c>osc</c>, <c>lipsync</c>）。
        /// </summary>
        ValueProvider
    }
}
