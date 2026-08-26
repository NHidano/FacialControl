using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// slot 名と overlay 適用状態の対応を表す値型。
    /// <para>
    /// <see cref="Suppress"/> が false かつ <see cref="Snapshot"/> が null の場合は
    /// profile の default overlay へ fallback する。
    /// <see cref="Suppress"/> が true の場合は当該 slot の overlay を明示的に抑制する。
    /// <see cref="Snapshot"/> が非 null の場合は当該 snapshot を個別 override として適用する。
    /// </para>
    /// <para>
    /// <see cref="Expression.Overlays"/> および <see cref="FacialProfile.DefaultOverlays"/> の要素として用いられる。
    /// </para>
    /// </summary>
    public readonly struct OverlaySlotBinding : IEquatable<OverlaySlotBinding>
    {
        /// <summary>slot 識別子。空文字不可。</summary>
        public string Slot { get; }

        /// <summary>
        /// この binding が当該 slot の overlay を明示的に抑制するかどうか。
        /// </summary>
        public bool Suppress { get; }

        /// <summary>
        /// 個別 override として適用する snapshot。null の場合は default fallback または suppress。
        /// </summary>
        public ExpressionSnapshot? Snapshot { get; }

        /// <summary>
        /// 当該 slot を profile の default overlay へ fallback させる状態かどうか。
        /// </summary>
        public bool IsDefaultFallback => !Suppress && !Snapshot.HasValue;

        /// <summary>
        /// OverlaySlotBinding を生成する。
        /// </summary>
        /// <param name="slot">slot 識別子（空文字不可）。</param>
        /// <param name="suppress">当該 slot の overlay を明示的に抑制する場合 true。</param>
        /// <param name="snapshot">個別 override として適用する snapshot。default fallback / suppress では null。</param>
        /// <exception cref="ArgumentException"><paramref name="slot"/> が null / 空 / 全空白の場合。</exception>
        /// <exception cref="ArgumentException"><paramref name="suppress"/> が true かつ <paramref name="snapshot"/> が非 null の場合。</exception>
        public OverlaySlotBinding(string slot, bool suppress, ExpressionSnapshot? snapshot)
        {
            if (string.IsNullOrWhiteSpace(slot))
            {
                throw new ArgumentException("slot は空にできません。", nameof(slot));
            }

            if (suppress && snapshot.HasValue)
            {
                throw new ArgumentException(
                    "OverlaySlotBinding cannot be both Suppress and have a non-null Snapshot.",
                    nameof(snapshot));
            }

            Slot = slot;
            Suppress = suppress;
            Snapshot = snapshot;
        }

        public bool Equals(OverlaySlotBinding other)
        {
            return string.Equals(Slot, other.Slot, StringComparison.Ordinal)
                && Suppress == other.Suppress
                && Nullable.Equals(Snapshot, other.Snapshot);
        }

        public override bool Equals(object obj) => obj is OverlaySlotBinding b && Equals(b);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Slot != null ? StringComparer.Ordinal.GetHashCode(Slot) : 0;
                h = (h * 397) ^ Suppress.GetHashCode();
                h = (h * 397) ^ (Snapshot.HasValue ? Snapshot.Value.GetHashCode() : 0);
                return h;
            }
        }

        public override string ToString()
        {
            if (Suppress)
            {
                return $"OverlaySlotBinding(slot='{Slot}', SUPPRESS)";
            }

            return Snapshot.HasValue
                ? $"OverlaySlotBinding(slot='{Slot}', SNAPSHOT='{Snapshot.Value.Id}')"
                : $"OverlaySlotBinding(slot='{Slot}', DEFAULT)";
        }
    }
}
