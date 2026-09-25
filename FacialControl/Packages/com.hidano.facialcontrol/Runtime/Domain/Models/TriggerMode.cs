namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// ボタン入力で表情をトリガーする際の動作モード。
    /// </summary>
    public enum TriggerMode
    {
        /// <summary>
        /// 押している間だけ表情が ON になり、ボタンを離すと OFF に戻る (新規バインディングの既定)。
        /// </summary>
        Hold = 0,

        /// <summary>
        /// 1 度押すと ON、もう 1 度押すと OFF になるトグル動作。
        /// </summary>
        Toggle = 1,
    }
}
