namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// AnimationClip 由来の表情遷移カーブプリセット。
    /// AnimationEvent (FacialControlMeta_Set / "transitionCurvePreset") の floatParameter から
    /// int キャスト経由で復元される 4 種のプリセット値。
    /// <para>
    /// 既存 <see cref="TransitionCurveType"/> / <see cref="TransitionCurve"/> とは別の概念として共存させる。
    /// 本仕様（inspector-and-data-model-redesign）の Phase 3 で旧 <see cref="TransitionCurve"/> 側を
    /// 物理削除予定（research.md Topic 9 参照）。それまでの bridge 期間は両者を共存させる。
    /// </para>
    /// <para>
    /// Custom Hermite カーブは v2.1 以降のスコープ（Decision 3）。本 enum はプリセット 4 種のみ扱う。
    /// </para>
    /// </summary>
    public enum TransitionCurvePreset
    {
        /// <summary>線形補間（デフォルト）。</summary>
        Linear = 0,

        /// <summary>EaseIn (v * v) 相当。</summary>
        EaseIn = 1,

        /// <summary>EaseOut (1 - (1-v)^2) 相当。</summary>
        EaseOut = 2,

        /// <summary>EaseInOut 相当（中央点で対称）。</summary>
        EaseInOut = 3,
    }
}
