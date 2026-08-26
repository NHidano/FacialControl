using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// 1 つのアナログ入力源軸 → 1 つのターゲット (BlendShape または BonePose 軸) の
    /// 宣言的バインディング (Domain 値型)。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SourceId"/> は <see cref="InputSourceId"/> 規約に従う識別子（実体検証は呼出側責務）。
    /// <see cref="SourceAxis"/> は scalar=0 / Vector2 (X=0, Y=1) / N-axis のチャンネル番号（&gt;= 0）。
    /// <see cref="TargetAxis"/> は <see cref="AnalogBindingTargetKind.BonePose"/> のときのみ意味を持ち、
    /// <see cref="AnalogBindingTargetKind.BlendShape"/> では無視される。
    /// </para>
    /// <para>
    /// <see cref="Scale"/> と <see cref="Direction"/> は「1 軸入力で複数 BlendShape を異なる weight ・符号で
    /// 駆動する」用途のために導入した（gaze の 4 系統 LookLeft/Right/Up/Down 等）。
    /// 現状の従来呼出側は default 値（Scale=1, Direction=Bipolar）で従来挙動と完全互換になる。
    /// BonePose ターゲットでは原則 Bipolar / Scale=1 で使う想定。
    /// </para>
    /// <para>
    /// 5 値（SourceId / SourceAxis / TargetKind / TargetIdentifier / TargetAxis）に加え、
    /// BlendShape clip 駆動の必要性により Scale / Direction を持つ（gaze-blendshape-clip 系）。
    /// dead-zone / scale (axis 全体) / offset / curve / invert / clamp の値変換は
    /// Adapters 側 InputProcessor 経路（<c>InputActionReference</c>）で扱う。
    /// </para>
    /// </remarks>
    public readonly struct AnalogBindingEntry
    {
        /// <summary>入力源識別子（<see cref="InputSourceId.Value"/> 文字列形）。</summary>
        public string SourceId { get; }

        /// <summary>入力源側のチャンネル番号（&gt;= 0）。scalar の場合は 0、Vector2 では 0=X / 1=Y。</summary>
        public int SourceAxis { get; }

        /// <summary>ターゲット種別（BlendShape / BonePose）。</summary>
        public AnalogBindingTargetKind TargetKind { get; }

        /// <summary>ターゲット識別子（BlendShape 名 または bone 名）。null / 空 / 全空白は不可。</summary>
        public string TargetIdentifier { get; }

        /// <summary>BonePose ターゲットでの Euler 軸（X/Y/Z）。BlendShape ターゲットでは未使用。</summary>
        public AnalogTargetAxis TargetAxis { get; }

        /// <summary>
        /// ターゲットに反映する倍率（既定 1.0）。BlendShape clip 由来の binding では
        /// keyframe weight (例: clip 内で 0.8) を保持して runtime で <c>raw * Scale</c> を加算する。
        /// </summary>
        public float Scale { get; }

        /// <summary>入力 raw 値の符号フィルタ。既定 <see cref="AnalogBindingDirection.Bipolar"/>。</summary>
        public AnalogBindingDirection Direction { get; }

        /// <summary>
        /// バインディングエントリを構築する（Scale=1, Direction=Bipolar の互換 ctor）。
        /// </summary>
        /// <param name="sourceId">入力源識別子（呼出側で <see cref="InputSourceId"/> 規約に従っていることを保証）。</param>
        /// <param name="sourceAxis">入力源軸 (&gt;= 0)。</param>
        /// <param name="targetKind">ターゲット種別。</param>
        /// <param name="targetIdentifier">ターゲット識別子（BlendShape 名 / bone 名）。</param>
        /// <param name="targetAxis">BonePose ターゲットの Euler 軸（BlendShape では無視）。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="sourceAxis"/> が負の場合。</exception>
        /// <exception cref="ArgumentException"><paramref name="targetIdentifier"/> が null / 空 / 全空白の場合。</exception>
        public AnalogBindingEntry(
            string sourceId,
            int sourceAxis,
            AnalogBindingTargetKind targetKind,
            string targetIdentifier,
            AnalogTargetAxis targetAxis)
            : this(sourceId, sourceAxis, targetKind, targetIdentifier, targetAxis,
                   scale: 1f, direction: AnalogBindingDirection.Bipolar)
        {
        }

        /// <summary>
        /// バインディングエントリを構築する（Scale / Direction を明示する完全 ctor）。
        /// </summary>
        public AnalogBindingEntry(
            string sourceId,
            int sourceAxis,
            AnalogBindingTargetKind targetKind,
            string targetIdentifier,
            AnalogTargetAxis targetAxis,
            float scale,
            AnalogBindingDirection direction)
        {
            if (sourceAxis < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sourceAxis),
                    sourceAxis,
                    "AnalogBindingEntry.sourceAxis must be >= 0.");
            }

            if (string.IsNullOrWhiteSpace(targetIdentifier))
            {
                throw new ArgumentException(
                    "AnalogBindingEntry.targetIdentifier must be a non-empty, non-whitespace string.",
                    nameof(targetIdentifier));
            }

            SourceId = sourceId ?? string.Empty;
            SourceAxis = sourceAxis;
            TargetKind = targetKind;
            TargetIdentifier = targetIdentifier;
            TargetAxis = targetAxis;
            Scale = scale;
            Direction = direction;
        }
    }
}
