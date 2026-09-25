namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// アナログ入力ソースの形状種別。<see cref="Hidano.FacialControl.Domain.Interfaces.IAnalogInputSource"/>
    /// 実装が公開する読取変種を選択するための補助型。
    /// </summary>
    /// <remarks>
    /// N-axis ソース（ARKit 52ch 等）は形状ではなく <see cref="Hidano.FacialControl.Domain.Interfaces.IAnalogInputSource.AxisCount"/>
    /// で表現するため、本 enum には N-axis を含めない。
    /// </remarks>
    public enum AnalogInputShape
    {
        /// <summary>1 軸の float 値。</summary>
        Scalar = 0,

        /// <summary>2 軸の Vector2 値。</summary>
        Vector2 = 1
    }
}
