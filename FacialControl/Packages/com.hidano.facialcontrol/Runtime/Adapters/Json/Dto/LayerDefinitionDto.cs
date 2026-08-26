using System.Collections.Generic;

namespace Hidano.FacialControl.Adapters.Json.Dto
{
    /// <summary>
    /// 中間 JSON Schema v2.0: <c>layers[]</c> の 1 エントリ。
    /// レイヤー名 / 優先度 / 排他モード / 入力源宣言を保持する。
    /// JsonUtility 互換のため <see cref="System.SerializableAttribute"/> を付与する。
    /// <para>
    /// schema v1.0 と field 構造は同一（Phase 2 では layer 構造の破壊変更なし）だが、
    /// v1.0 で <see cref="SystemTextJsonParser"/> 内の private DTO として閉じていたものを、
    /// v2.0 では <see cref="ProfileSnapshotDto"/> から参照する公開 DTO として再定義する。
    /// </para>
    /// </summary>
    [System.Serializable]
    public sealed class LayerDefinitionDto
    {
        /// <summary>レイヤー名。</summary>
        public string name;

        /// <summary>優先度（0 以上の整数）。</summary>
        public int priority;

        /// <summary>排他モード（"lastWins" | "blend"）。</summary>
        public string exclusionMode;

        /// <summary>
        /// 入力源宣言。preview 破壊的変更 D-5, 3.2 により必須・非空配列。
        /// </summary>
        public List<InputSourceDto> inputSources;
    }
}
