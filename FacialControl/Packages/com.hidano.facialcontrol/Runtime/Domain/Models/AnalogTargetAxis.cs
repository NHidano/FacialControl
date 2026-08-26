namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// BonePose ターゲットでの Euler 軸選択。
    /// 列挙値は JSON / 永続化フォーマットの安定性のため固定（X=0, Y=1, Z=2）。
    /// BlendShape ターゲットでは無視される。
    /// </summary>
    public enum AnalogTargetAxis
    {
        /// <summary>X 軸（Euler 度）。</summary>
        X = 0,

        /// <summary>Y 軸（Euler 度）。</summary>
        Y = 1,

        /// <summary>Z 軸（Euler 度）。</summary>
        Z = 2
    }
}
