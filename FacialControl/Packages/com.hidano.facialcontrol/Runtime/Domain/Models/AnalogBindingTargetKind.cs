namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// アナログバインディングのターゲット種別。
    /// 列挙値は JSON / 永続化フォーマットの安定性のため固定（BlendShape=0, BonePose=1）。
    /// </summary>
    public enum AnalogBindingTargetKind
    {
        /// <summary>BlendShape 値を駆動するターゲット。</summary>
        BlendShape = 0,

        /// <summary>BonePose（顔ボーンの相対 Euler）を駆動するターゲット。</summary>
        BonePose = 1
    }
}
