namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// レイヤー内の表情排他モード
    /// </summary>
    public enum ExclusionMode
    {
        /// <summary>
        /// 後勝ち: 新しい Expression がアクティブになると旧 Expression からクロスフェード
        /// </summary>
        LastWins,

        /// <summary>
        /// ブレンド: 複数 Expression のウェイトを加算し 0〜1 にクランプ
        /// </summary>
        Blend
    }
}
