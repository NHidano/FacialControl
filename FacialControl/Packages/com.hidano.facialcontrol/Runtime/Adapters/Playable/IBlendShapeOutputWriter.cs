using System;

namespace Hidano.FacialControl.Adapters.Playable
{
    /// <summary>
    /// BlendShape 出力の差し替え可能な書込み契約。
    /// </summary>
    /// <remarks>
    /// 入力は 0..1 に正規化された BlendShape 重みで、順序は
    /// <c>FacialController</c> が公開する BlendShape 名配列と一致しなければならない。
    /// 実装は Engine 側の反映方法と必要なマッピングの保持のみを担当し、
    /// 集約・ブレンド・遷移計算は行わない。
    /// </remarks>
    public interface IBlendShapeOutputWriter : IDisposable
    {
        /// <summary>
        /// 現フレームの正規化済み BlendShape 重みを出力先へ反映する。
        /// </summary>
        /// <param name="normalizedWeights">
        /// 0..1 の正規化値。要素数と順序は writer 構築時に確定した
        /// BlendShape ターゲット定義と一致している必要がある。
        /// </param>
        void Write(ReadOnlySpan<float> normalizedWeights);
    }
}
