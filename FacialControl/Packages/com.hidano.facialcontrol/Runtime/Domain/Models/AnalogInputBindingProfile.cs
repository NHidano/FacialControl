using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// アナログ入力バインディング集合の Domain ルート 。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 0 個以上の <see cref="AnalogBindingEntry"/> を <see cref="ReadOnlyMemory{T}"/> として保持する readonly struct。
    /// 構築時に防御的コピーを行い、外部配列の後続書換えに対して不変性を保つ。
    /// </para>
    /// </remarks>
    public readonly struct AnalogInputBindingProfile
    {
        private readonly string _version;
        private readonly ReadOnlyMemory<AnalogBindingEntry> _bindings;

        /// <summary>プロファイルバージョン文字列（永続化のため、空文字許容）。</summary>
        public string Version => _version ?? string.Empty;

        /// <summary>バインディングエントリの読取専用ビュー。</summary>
        public ReadOnlyMemory<AnalogBindingEntry> Bindings => _bindings;

        /// <summary>
        /// <see cref="AnalogInputBindingProfile"/> を構築する。
        /// </summary>
        /// <param name="version">プロファイルバージョン文字列（null は空文字扱い）。</param>
        /// <param name="bindings">エントリ配列（防御的コピーされる、null は空コレクション扱い）。</param>
        public AnalogInputBindingProfile(string version, AnalogBindingEntry[] bindings)
        {
            _version = version ?? string.Empty;

            if (bindings == null || bindings.Length == 0)
            {
                _bindings = ReadOnlyMemory<AnalogBindingEntry>.Empty;
                return;
            }

            var copy = new AnalogBindingEntry[bindings.Length];
            for (int i = 0; i < bindings.Length; i++)
            {
                copy[i] = bindings[i];
            }
            _bindings = copy;
        }
    }
}
