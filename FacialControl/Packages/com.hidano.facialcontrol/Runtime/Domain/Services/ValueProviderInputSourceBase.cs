using System;
using System.Collections;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Domain.Services
{
    /// <summary>
    /// BlendShape 値をそのまま提供する入力源型の共通基底 (D-1 ハイブリッドモデル)。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Expression トリガー型と異なり、独立した Expression スタックや
    /// <see cref="TransitionCalculator"/> 状態を持たず、外部データソース
    /// (<c>OscDoubleBuffer</c> / <c>ILipSyncProvider</c> 等) から取得した値を
    /// 直接 <c>output</c> に書込むことだけを責務とする。<br/>
    /// 時間進行を必要としないため <see cref="Tick"/> は no-op である。
    /// </para>
    /// <para>
    /// 派生クラスは <see cref="TryWriteValues"/> の override のみで成立する。
    /// 有効性判定 (<c>_cachedIsValid</c> 等) は派生側で定義する。
    /// </para>
    /// <para>
    /// Invariants: <see cref="Id"/> / <see cref="Type"/> / <see cref="BlendShapeCount"/>
    /// は構築後不変。<see cref="Type"/> は常に <see cref="InputSourceType.ValueProvider"/>。
    /// </para>
    /// </remarks>
    public abstract class ValueProviderInputSourceBase : IInputSource
    {
        /// <summary>入力源識別子 (生涯不変)。<see cref="IInputSource.Id"/> の実体。</summary>
        public string Id { get; }

        /// <summary>入力源種別。本基底では常に <see cref="InputSourceType.ValueProvider"/>。</summary>
        public InputSourceType Type => InputSourceType.ValueProvider;

        /// <summary>本入力源が書込む BlendShape の個数 (構築後不変)。</summary>
        public int BlendShapeCount { get; }

        /// <inheritdoc />
        public virtual BitArray ContributeMask { get; }

        /// <summary>
        /// 値提供型の基底を構築する。
        /// </summary>
        /// <param name="id">入力源識別子 (<see cref="InputSourceId"/> 規約に従う)。</param>
        /// <param name="blendShapeCount">書込む BlendShape 個数。0 以上。</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="blendShapeCount"/> が負の場合。
        /// </exception>
        protected ValueProviderInputSourceBase(InputSourceId id, int blendShapeCount)
        {
            if (blendShapeCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(blendShapeCount), blendShapeCount,
                    "blendShapeCount は 0 以上を指定してください。");
            }

            Id = id.Value;
            BlendShapeCount = blendShapeCount;
            ContributeMask = new BitArray(blendShapeCount, true);
        }

        /// <summary>
        /// 時間進行 (既定は no-op)。値提供型は原則として時間依存状態を持たないが、
        /// クロスフェード等の内部補間を持つ派生 (<c>OverlayInputSource</c> 等) は override して進行させる。
        /// </summary>
        /// <param name="deltaTime">前フレームからの経過秒数。</param>
        public virtual void Tick(float deltaTime)
        {
            // no-op: value provider 型は既定では時間進行を伴う内部状態を持たない。
        }

        /// <summary>
        /// 派生クラスが外部データソースの現在値を <paramref name="output"/> に書込む。
        /// </summary>
        /// <param name="output">書込先バッファ。長さ不足時は overlap のみ書込む。</param>
        /// <returns>有効なら true (書込済)、無効なら false (<paramref name="output"/> 非変更)。</returns>
        public abstract bool TryWriteValues(Span<float> output);
    }
}
