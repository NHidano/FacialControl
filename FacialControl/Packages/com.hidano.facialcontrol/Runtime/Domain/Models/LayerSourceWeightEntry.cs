using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// 診断スナップショット API  の 1 件分の戻り値を表す readonly value-struct。
    /// レイヤー × 入力源単位の現在ウェイトと有効性、飽和フラグを持つ。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Invariants:
    /// <list type="bullet">
    /// <item><see cref="LayerIdx"/> は 0 以上（書込側で保証）。</item>
    /// <item><see cref="Weight"/> は 0〜1（WeightBuffer 側で silent clamp 済み）。</item>
    /// <item><see cref="Saturated"/> は該当レイヤーで Σw &gt; 1 の場合に true。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 等値性は 5 フィールド全ての構造的比較で判定する。Snapshot API から
    /// <c>Span&lt;LayerSourceWeightEntry&gt;</c> / <c>IReadOnlyList&lt;LayerSourceWeightEntry&gt;</c> として
    /// 受け取ることを想定している。
    /// </para>
    /// </remarks>
    public readonly struct LayerSourceWeightEntry : IEquatable<LayerSourceWeightEntry>
    {
        /// <summary>該当レイヤーのインデックス。</summary>
        public int LayerIdx { get; }

        /// <summary>該当入力源の識別子。</summary>
        public InputSourceId SourceId { get; }

        /// <summary>現在ウェイト（0〜1 に silent clamp 済み）。</summary>
        public float Weight { get; }

        /// <summary>直近の <c>TryWriteValues</c> が有効値を書いた場合 true。</summary>
        public bool IsValid { get; }

        /// <summary>該当レイヤーの重み合計が 1 を超えてクランプが発生した場合 true。</summary>
        public bool Saturated { get; }

        public LayerSourceWeightEntry(
            int layerIdx,
            InputSourceId sourceId,
            float weight,
            bool isValid,
            bool saturated)
        {
            LayerIdx = layerIdx;
            SourceId = sourceId;
            Weight = weight;
            IsValid = isValid;
            Saturated = saturated;
        }

        public bool Equals(LayerSourceWeightEntry other)
        {
            return LayerIdx == other.LayerIdx
                && SourceId.Equals(other.SourceId)
                && Weight.Equals(other.Weight)
                && IsValid == other.IsValid
                && Saturated == other.Saturated;
        }

        public override bool Equals(object obj)
        {
            return obj is LayerSourceWeightEntry other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + LayerIdx.GetHashCode();
                hash = hash * 31 + SourceId.GetHashCode();
                hash = hash * 31 + Weight.GetHashCode();
                hash = hash * 31 + IsValid.GetHashCode();
                hash = hash * 31 + Saturated.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(LayerSourceWeightEntry left, LayerSourceWeightEntry right)
            => left.Equals(right);

        public static bool operator !=(LayerSourceWeightEntry left, LayerSourceWeightEntry right)
            => !left.Equals(right);
    }
}
