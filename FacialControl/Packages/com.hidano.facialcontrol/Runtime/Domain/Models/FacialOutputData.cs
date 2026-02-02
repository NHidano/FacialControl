using Unity.Collections;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// ランタイム出力データ構造体（技術仕様書 §3.7 準拠）。
    /// BlendShape 配列として最終的な表情制御結果を保持する。
    /// </summary>
    public struct FacialOutputData
    {
        /// <summary>
        /// BlendShape ウェイト配列（0〜1 正規化値）。
        /// NativeArray を使用し、毎フレームのヒープ確保を回避する。
        /// </summary>
        public NativeArray<float> BlendShapeWeights;
    }
}
