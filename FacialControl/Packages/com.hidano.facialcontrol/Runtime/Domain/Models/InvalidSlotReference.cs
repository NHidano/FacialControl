using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// Slots 宣言に対する不正な参照を表す Domain 値型。
    /// </summary>
    public readonly struct InvalidSlotReference : IEquatable<InvalidSlotReference>
    {
        public const string DuplicateReason = "Duplicate";
        public const string UndeclaredReason = "Undeclared";

        /// <summary>
        /// 対象 slot 識別子。
        /// </summary>
        public string Slot { get; }

        /// <summary>
        /// 不正理由。<c>Duplicate</c> または <c>Undeclared</c> のみ。
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// 不正 slot 参照情報を生成する。
        /// </summary>
        /// <param name="slot">対象 slot 識別子。</param>
        /// <param name="reason">不正理由。<c>Duplicate</c> または <c>Undeclared</c>。</param>
        /// <exception cref="ArgumentException"><paramref name="reason"/> が許可された理由ではない場合。</exception>
        public InvalidSlotReference(string slot, string reason)
        {
            if (!IsKnownReason(reason))
            {
                throw new ArgumentException(
                    "Reason must be \"Duplicate\" or \"Undeclared\".",
                    nameof(reason));
            }

            Slot = slot ?? string.Empty;
            Reason = reason;
        }

        public bool Equals(InvalidSlotReference other)
        {
            return string.Equals(Slot, other.Slot, StringComparison.Ordinal)
                && string.Equals(Reason, other.Reason, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is InvalidSlotReference other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Slot != null ? StringComparer.Ordinal.GetHashCode(Slot) : 0;
                hash = (hash * 397) ^ (Reason != null ? StringComparer.Ordinal.GetHashCode(Reason) : 0);
                return hash;
            }
        }

        public static bool operator ==(InvalidSlotReference left, InvalidSlotReference right) => left.Equals(right);

        public static bool operator !=(InvalidSlotReference left, InvalidSlotReference right) => !left.Equals(right);

        private static bool IsKnownReason(string reason)
        {
            return string.Equals(reason, DuplicateReason, StringComparison.Ordinal)
                || string.Equals(reason, UndeclaredReason, StringComparison.Ordinal);
        }
    }
}
