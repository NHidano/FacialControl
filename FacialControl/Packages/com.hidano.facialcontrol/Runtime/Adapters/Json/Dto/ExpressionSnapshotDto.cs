using System.Collections.Generic;

namespace Hidano.FacialControl.Adapters.Json.Dto
{
    /// <summary>
    /// 中間 JSON Schema v2.0: <c>expressions[].snapshot</c> オブジェクト。
    /// AnimationClip サンプリング結果（時刻 0 の BlendShape 値 + Bone 姿勢 + 遷移メタ）を
    /// JSON へ運搬する DTO。JsonUtility 互換のため <see cref="System.SerializableAttribute"/> を付与する。
    /// <para>
    /// 共通フィールド（transition メタ・blendShapes・bones・rendererPaths）は再帰終端型
    /// <see cref="OverlaySnapshotDto"/> に切り出し、本型はそれを継承して expression レベル固有の
    /// <see cref="overlays"/> のみを追加する。overlay binding 側の snapshot 型は
    /// <see cref="OverlaySnapshotDto"/>（overlays なし）なので、自己再帰は 1 段で止まる。
    /// </para>
    /// <para>
    /// Domain 側の対応値型は <see cref="Hidano.FacialControl.Domain.Models.ExpressionSnapshot"/>。
    /// </para>
    /// </summary>
    [System.Serializable]
    public sealed class ExpressionSnapshotDto : OverlaySnapshotDto
    {
        /// <summary>
        /// この Expression が宣言する slot 別 overlay binding。
        /// 各 binding は default fallback / suppress / snapshot override の 3 状態を表現する。
        /// </summary>
        public List<OverlaySlotBindingDto> overlays;
    }
}
