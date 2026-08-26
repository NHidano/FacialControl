using System;
using System.Text.RegularExpressions;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// 入力源識別子の value-object。
    /// パターン <c>[a-zA-Z0-9_.\-:]{1,64}</c> を満たす ASCII 文字列のみを受理する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 識別子の意味付けは <see cref="AdapterSlug"/> 経由で各 binding 側が担う。
    /// 識別子 <c>legacy</c> は legacy フォールバック廃止に伴い受理されない。
    /// 旧 <c>controller-expr</c> / <c>keyboard-expr</c> は廃止され受理されない。
    /// サードパーティ拡張は <c>x-</c> プレフィックス推奨。
    /// </para>
    /// <para>
    /// 文字 <c>:</c> は <see cref="InputSourceRegistry"/> が <c>slug:sub</c> 形式で合成キーを
    /// 構築するための区切り文字として使われる（例: <c>input:analog-expression</c>）。
    /// <see cref="AdapterSlug"/> 自身は <c>:</c> を含めない（slug の regex 側で禁止）が、
    /// レイヤーの <c>inputSources[].id</c> として永続化される文字列は合成済みキーであり
    /// <c>:</c> を含みうるため、本識別子の regex でのみ <c>:</c> を許容する。
    /// </para>
    /// <para>
    /// Invariants: 構築後の <see cref="Value"/> は常にパターンを満たし、<c>legacy</c> ではない。
    /// </para>
    /// </remarks>
    public readonly struct InputSourceId : IEquatable<InputSourceId>
    {
        private static readonly Regex IdPattern =
            new Regex(@"^[a-zA-Z0-9_.\-:]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private const string ForbiddenLegacyId = "legacy";
        private const string ThirdPartyPrefix = "x-";

        /// <summary>
        /// 文字列としての識別子。未初期化インスタンスでは <c>null</c> となる。
        /// </summary>
        public string Value { get; }

        private InputSourceId(string validatedValue)
        {
            Value = validatedValue;
        }

        /// <summary>
        /// <c>x-</c> プレフィックス付きのサードパーティ拡張識別子なら true。
        /// </summary>
        public bool IsThirdPartyExtension =>
            !string.IsNullOrEmpty(Value) && Value.StartsWith(ThirdPartyPrefix, StringComparison.Ordinal);

        /// <summary>
        /// 識別子規約に照らして入力文字列を検証し、合格時のみ <see cref="InputSourceId"/> を構築する。
        /// </summary>
        /// <param name="input">検証対象の文字列</param>
        /// <param name="id">検証に成功した場合の識別子。失敗時は default 値。</param>
        /// <returns>規約を満たし <c>legacy</c> でもない場合 true。</returns>
        public static bool TryParse(string input, out InputSourceId id)
        {
            if (!IsValidIdentifier(input))
            {
                id = default;
                return false;
            }

            id = new InputSourceId(input);
            return true;
        }

        /// <summary>
        /// 識別子規約を満たす場合に <see cref="InputSourceId"/> を返す。満たさない場合は例外を投げる。
        /// </summary>
        /// <exception cref="FormatException">規約を満たさない、または <c>legacy</c> が指定された場合。</exception>
        public static InputSourceId Parse(string input)
        {
            if (!TryParse(input, out var id))
            {
                throw new FormatException($"Invalid InputSourceId: '{input ?? "<null>"}'.");
            }
            return id;
        }

        private static bool IsValidIdentifier(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            if (string.Equals(input, ForbiddenLegacyId, StringComparison.Ordinal))
            {
                return false;
            }

            return IdPattern.IsMatch(input);
        }

        public bool Equals(InputSourceId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is InputSourceId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value ?? string.Empty;
        }

        public static bool operator ==(InputSourceId left, InputSourceId right) => left.Equals(right);
        public static bool operator !=(InputSourceId left, InputSourceId right) => !left.Equals(right);
    }
}
