using System.Collections.Generic;

namespace Hidano.FacialControl.Adapters.Json.Dto
{
    /// <summary>
    /// 中間 JSON Schema v2.0: <c>expressions[]</c> の 1 エントリ。
    /// 旧 schema v1.0 の (transitionDuration / transitionCurve / blendShapeValues / layerSlots)
    /// は撤去され、サンプリング結果は <see cref="ExpressionSnapshotDto"/> 配下にまとめられる。
    /// JsonUtility 互換のため <see cref="System.SerializableAttribute"/> を付与する。
    /// <para>
    /// 注意: 本クラスは v2.0 schema 専用。v1.0 schema の Expression DTO は
    /// <see cref="SystemTextJsonParser"/> 内部の private DTO に残されている（将来物理削除予定）。
    /// </para>
    /// </summary>
    [System.Serializable]
    public sealed class ExpressionDto
    {
        /// <summary>表情を一意に識別する Id（GUID 文字列）。</summary>
        public string id;

        /// <summary>表情名（多バイト文字許容）。</summary>
        public string name;

        /// <summary>
        /// 所属レイヤー名。<see cref="ProfileSnapshotDto.layers"/> 内の name のいずれかと一致必須。
        /// </summary>
        public string layer;

        /// <summary>
        /// 同時に上書き対象とする layer 名配列。
        /// Domain 側の <see cref="Hidano.FacialControl.Domain.Models.LayerOverrideMask"/> へ
        /// Adapters 層のヘルパーが bit position と layer 名を対応付けて変換する。
        /// </summary>
        public List<string> layerOverrideMask;

        /// <summary>
        /// AnimationClip サンプリング結果の snapshot。
        /// 欠落時はパーサが空 snapshot（デフォルト値 + 空配列）に正規化する。
        /// </summary>
        public ExpressionSnapshotDto snapshot;
    }
}
