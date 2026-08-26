using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// Phoneme overlay slot の予約名を共有する Domain 定数。
    /// </summary>
    public static class PhonemeOverlaySlots
    {
        public const string A = "a";
        public const string I = "i";
        public const string U = "u";
        public const string E = "e";
        public const string O = "o";

        private static readonly string[] _reserved = { A, I, U, E, O };

        public static ReadOnlySpan<string> ReservedNames => _reserved;

        public static bool IsReserved(string slot)
        {
            for (int i = 0; i < _reserved.Length; i++)
            {
                if (string.Equals(_reserved[i], slot, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static string MapReservedToPhonemeId(string reservedSlot)
        {
            if (string.IsNullOrEmpty(reservedSlot))
            {
                return null;
            }

            return reservedSlot.ToUpperInvariant();
        }
    }
}
