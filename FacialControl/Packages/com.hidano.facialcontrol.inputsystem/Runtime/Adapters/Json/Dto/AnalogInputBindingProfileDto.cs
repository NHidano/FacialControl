using System.Collections.Generic;

namespace Hidano.FacialControl.Adapters.Json.Dto
{
    /// <summary>
    /// <see cref="Hidano.FacialControl.Domain.Models.AnalogInputBindingProfile"/> の JSON 永続化 DTO。
    /// </summary>
    /// <remarks>
    /// <see cref="version"/> は文字列保持のみで分岐に使用しない（将来マイグレーションパスを設ける）。
    /// </remarks>
    [System.Serializable]
    public sealed class AnalogInputBindingProfileDto
    {
        /// <summary>プロファイルバージョン文字列（現状は分岐なし）。</summary>
        public string version;

        /// <summary>バインディングエントリ一覧。null / 空双方が空プロファイル扱い。</summary>
        public List<AnalogBindingEntryDto> bindings;
    }
}
