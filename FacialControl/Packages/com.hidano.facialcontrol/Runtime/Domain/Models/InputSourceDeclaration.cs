namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// <c>layers[].inputSources[]</c> の 1 宣言を表す Domain 値オブジェクト。
    /// <para>
    /// JSON round-trip 安定性  を実現するため、
    /// <see cref="FacialProfile"/> が <c>inputSources</c> データを Parse → Serialize 経路で保持する際の担体。
    /// </para>
    /// <para>
    /// <see cref="OptionsJson"/> は JSON 上の <c>options</c> フィールドの生 JSON サブ文字列
    /// （<c>{ ... }</c> ブロックそのもの）を保持する。空 / null の場合は options 未指定として扱う。
    /// </para>
    /// </summary>
    public readonly struct InputSourceDeclaration
    {
        /// <summary>入力源 ID（予約 ID または <c>x-</c> プレフィックス拡張）。</summary>
        public string Id { get; }

        /// <summary>ソースウェイト（0〜1、省略時 1.0）。</summary>
        public float Weight { get; }

        /// <summary><c>options</c> フィールドの生 JSON サブ文字列（null / 空なら未指定）。</summary>
        public string OptionsJson { get; }

        public InputSourceDeclaration(string id, float weight, string optionsJson)
        {
            Id = id;
            Weight = weight;
            OptionsJson = optionsJson;
        }
    }
}
