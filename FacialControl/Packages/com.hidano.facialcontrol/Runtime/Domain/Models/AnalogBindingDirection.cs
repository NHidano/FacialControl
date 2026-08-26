namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// アナログ入力 binding が反応する入力符号フィルタ。
    /// 1 つの軸を符号で振り分けて複数 BlendShape clip に向かわせる用途で使う
    /// （例: input.x &gt; 0 で LookRight clip、input.x &lt; 0 で LookLeft clip）。
    /// </summary>
    /// <remarks>
    /// 列挙値は JSON / 永続化フォーマットの安定性のため固定（Bipolar=0, Positive=1, Negative=2）。
    /// </remarks>
    public enum AnalogBindingDirection
    {
        /// <summary>入力符号に関係なく raw 値をそのまま反映（既存挙動・既定値）。</summary>
        Bipolar = 0,

        /// <summary>入力 raw &gt;= 0 のときのみ反映（負値は 0 として扱う）。</summary>
        Positive = 1,

        /// <summary>入力 raw &lt; 0 のときのみ |raw| を反映（正値は 0 として扱う）。</summary>
        Negative = 2,
    }
}
