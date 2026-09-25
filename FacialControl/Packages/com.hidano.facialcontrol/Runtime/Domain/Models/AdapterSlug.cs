using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// Adapter slug の値オブジェクト。
    /// パターン <c>^[a-zA-Z0-9_.-]{1,64}$</c> を満たす ASCII 文字列のみを受理する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 旧 <see cref="InputSourceId"/> の責務分離後継。reserved id (<c>osc</c> 等) や
    /// <c>legacy</c> 禁止のような追加制約は持たない（D-13 廃止）。
    /// </para>
    /// <para>
    /// 区切り文字 <c>:</c> は slug 内には含めない（<see cref="CompositeSeparator"/> として
    /// <c>slug:sub</c> 合成キーの境界に予約）。合成済み id 文字列を扱うのは
    /// <see cref="InputSourceId"/> 側で、そちらの regex は <c>:</c> を許容する。
    /// </para>
    /// <para>
    /// Invariants: 構築後の <see cref="Value"/> は常にパターンを満たす ASCII 文字列。
    /// 未初期化（<c>default</c>）では <see cref="Value"/> は <c>null</c>。
    /// </para>
    /// </remarks>
    public readonly struct AdapterSlug : IEquatable<AdapterSlug>
    {
        private const int MaxLength = 64;
        private const char CompositeSeparator = ':';

        private static readonly Regex SlugPattern =
            new Regex("^[a-zA-Z0-9_.-]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// 文字列としての slug 値。未初期化インスタンスでは <c>null</c>。
        /// </summary>
        public string Value { get; }

        private AdapterSlug(string validatedValue)
        {
            Value = validatedValue;
        }

        /// <summary>
        /// 入力文字列を slug 規約に照らして検証し、合格時のみ <see cref="AdapterSlug"/> を構築する。
        /// </summary>
        public static bool TryParse(string input, out AdapterSlug slug)
        {
            if (!IsValid(input))
            {
                slug = default;
                return false;
            }

            slug = new AdapterSlug(input);
            return true;
        }

        /// <summary>
        /// 入力文字列を slug として構築する。規約を満たさない場合は <see cref="FormatException"/>。
        /// </summary>
        public static AdapterSlug Parse(string input)
        {
            if (!TryParse(input, out var slug))
            {
                throw new FormatException($"Invalid AdapterSlug: '{input ?? "<null>"}'.");
            }
            return slug;
        }

        /// <summary>
        /// displayName から kebab-case の slug を自動生成する。
        /// 空白・記号は <c>-</c> に置換し、連続する <c>-</c> は 1 文字に圧縮、最終的に
        /// <see cref="char.ToLowerInvariant(char)"/> 相当で正規化する。
        /// </summary>
        public static AdapterSlug FromDisplayName(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                throw new ArgumentException(
                    "displayName must not be null or empty.", nameof(displayName));
            }

            var sb = new StringBuilder(displayName.Length);
            bool lastWasDash = false;

            for (int i = 0; i < displayName.Length; i++)
            {
                char c = displayName[i];
                if (IsAllowedSlugChar(c))
                {
                    char lower = ToAsciiLower(c);
                    if (lower == '-')
                    {
                        if (lastWasDash) continue;
                        sb.Append(lower);
                        lastWasDash = true;
                    }
                    else
                    {
                        sb.Append(lower);
                        lastWasDash = false;
                    }
                }
                else
                {
                    if (lastWasDash) continue;
                    sb.Append('-');
                    lastWasDash = true;
                }
            }

            while (sb.Length > 0 && sb[sb.Length - 1] == '-')
            {
                sb.Length--;
            }

            int start = 0;
            while (start < sb.Length && sb[start] == '-')
            {
                start++;
            }

            string normalized = start > 0 ? sb.ToString(start, sb.Length - start) : sb.ToString();

            if (normalized.Length > MaxLength)
            {
                normalized = normalized.Substring(0, MaxLength);
                while (normalized.Length > 0 && normalized[normalized.Length - 1] == '-')
                {
                    normalized = normalized.Substring(0, normalized.Length - 1);
                }
            }

            if (!IsValid(normalized))
            {
                throw new FormatException(
                    $"AdapterSlug.FromDisplayName produced invalid slug from '{displayName}'.");
            }

            return new AdapterSlug(normalized);
        }

        /// <summary>
        /// <c>&lt;slug&gt;</c> または <c>&lt;slug&gt;:&lt;sub&gt;</c> 形式の複合 id を解析する。
        /// sub が空文字列となるケース（<c>"osc:"</c>）や slug 部分が不正な場合は false。
        /// </summary>
        public static bool TryParseComposite(string input, out AdapterSlug slug, out string sub)
        {
            slug = default;
            sub = string.Empty;

            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            int colonIndex = input.IndexOf(CompositeSeparator);
            if (colonIndex < 0)
            {
                if (!TryParse(input, out var simple))
                {
                    return false;
                }
                slug = simple;
                sub = string.Empty;
                return true;
            }

            string slugPart = input.Substring(0, colonIndex);
            string subPart = input.Substring(colonIndex + 1);

            if (string.IsNullOrEmpty(subPart))
            {
                return false;
            }

            if (!TryParse(slugPart, out var parsedSlug))
            {
                return false;
            }

            slug = parsedSlug;
            sub = subPart;
            return true;
        }

        private static bool IsValid(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }
            return SlugPattern.IsMatch(input);
        }

        private static bool IsAllowedSlugChar(char c)
        {
            return (c >= 'a' && c <= 'z')
                || (c >= 'A' && c <= 'Z')
                || (c >= '0' && c <= '9')
                || c == '_'
                || c == '.'
                || c == '-';
        }

        private static char ToAsciiLower(char c)
        {
            if (c >= 'A' && c <= 'Z') return (char)(c + ('a' - 'A'));
            return c;
        }

        public bool Equals(AdapterSlug other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is AdapterSlug other && Equals(other);

        public override int GetHashCode() =>
            Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(AdapterSlug left, AdapterSlug right) => left.Equals(right);
        public static bool operator !=(AdapterSlug left, AdapterSlug right) => !left.Equals(right);
    }
}
