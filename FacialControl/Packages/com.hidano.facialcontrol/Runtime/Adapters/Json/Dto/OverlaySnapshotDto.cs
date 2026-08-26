using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.Json.Dto
{
    /// <summary>
    /// overlay slot binding が保持する snapshot の DTO（再帰終端型）。
    /// <para>
    /// <see cref="ExpressionSnapshotDto"/> と異なり <c>overlays</c> フィールドを持たない。
    /// これにより <c>OverlaySlotBindingDto.snapshot</c> の宣言型を本型にすると、
    /// <c>ExpressionSnapshotDto.overlays → OverlaySlotBindingDto.snapshot → OverlaySnapshotDto(終端)</c>
    /// と参照グラフが 1 段で止まり、JsonUtility / Unity シリアライザの自己再帰
    /// （Serialization depth limit 10 exceeded）を型レベルで断つ。
    /// </para>
    /// <para>
    /// JsonUtility 互換のため <see cref="System.SerializableAttribute"/> を付与する。
    /// Domain 側の対応値型は <see cref="Hidano.FacialControl.Domain.Models.ExpressionSnapshot"/>。
    /// </para>
    /// </summary>
    [System.Serializable]
    public class OverlaySnapshotDto
    {
        /// <summary>表情遷移時間（秒）。0〜1 秒、デフォルトは <see cref="Expression.DefaultTransitionDuration"/>（1/15 秒）。</summary>
        public float transitionDuration = Expression.DefaultTransitionDuration;

        /// <summary>
        /// 遷移カーブプリセット名（"Linear" / "EaseIn" / "EaseOut" / "EaseInOut"）。
        /// デフォルト "Linear"。
        /// 空文字 / null は Linear として解釈される。
        /// </summary>
        public string transitionCurvePreset = "Linear";

        /// <summary>BlendShape スナップショット配列。</summary>
        public List<BlendShapeSnapshotDto> blendShapes;

        /// <summary>Bone 姿勢スナップショット配列。</summary>
        public List<BoneSnapshotDto> bones;

        /// <summary>
        /// この snapshot 内に登場する SkinnedMeshRenderer Transform 階層パスのサマリ。
        /// トップレベル <see cref="ProfileSnapshotDto.rendererPaths"/> の subset である必要がある。
        /// </summary>
        public List<string> rendererPaths;
    }
}
