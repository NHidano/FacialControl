using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// 遷移カーブ設定。プリセットカーブまたはカスタムキーフレーム配列を保持する。
    /// </summary>
    public readonly struct TransitionCurve
    {
        /// <summary>
        /// カーブ種類
        /// </summary>
        public TransitionCurveType Type { get; }

        /// <summary>
        /// カスタムカーブ用キーフレーム配列。プリセットカーブの場合は空配列。
        /// </summary>
        public ReadOnlyMemory<CurveKeyFrame> Keys { get; }

        public TransitionCurve(TransitionCurveType type, CurveKeyFrame[] keys = null)
        {
            Type = type;
            Keys = keys ?? Array.Empty<CurveKeyFrame>();
        }

        /// <summary>
        /// デフォルトの線形補間カーブを返す
        /// </summary>
        public static TransitionCurve Linear => new TransitionCurve(TransitionCurveType.Linear);
    }
}
