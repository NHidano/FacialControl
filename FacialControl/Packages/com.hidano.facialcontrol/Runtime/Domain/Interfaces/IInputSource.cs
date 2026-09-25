using System;
using System.Collections;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Domain.Interfaces
{
    /// <summary>
    /// D-1 ハイブリッドモデルにおける入力源の共通契約。
    /// Expression トリガー型 / BlendShape 値提供型のいずれのアダプタも本インターフェースを実装し、
    /// 固定長バッファへの書込と自身の有効性申告を提供する。
    /// Domain 層配置のため Unity API に非依存。
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// <list type="bullet">
    ///   <item><c>output.Length &gt; 0</c>（呼出側が保証）。</item>
    ///   <item><c>Tick</c> はメインスレッドから毎フレーム 1 回呼ばれる。</item>
    /// </list>
    /// Postconditions:
    /// <list type="bullet">
    ///   <item><c>TryWriteValues</c> が false を返した場合、<c>output</c> は変更されない。</item>
    ///   <item>true の場合、overlap 範囲のみ値を書込む（残余は呼出側が事前にゼロクリア）。</item>
    /// </list>
    /// Invariants:
    /// <list type="bullet">
    ///   <item><see cref="Id"/> と <see cref="Type"/> は生涯不変。</item>
    ///   <item><see cref="BlendShapeCount"/> はプロファイル有効期間中不変。</item>
    /// </list>
    /// </remarks>
    public interface IInputSource
    {
        /// <summary>
        /// 入力源識別子。<c>[a-zA-Z0-9_.\-:]{1,64}</c> 規約に従う。
        /// <c>:</c> は <c>slug:sub</c> 形式の合成キー区切り文字として使われる。
        /// </summary>
        string Id { get; }

        /// <summary>
        /// 入力源の種別（Expression トリガー型 / 値提供型）。
        /// </summary>
        InputSourceType Type { get; }

        /// <summary>
        /// 本入力源が書込む BlendShape の個数。
        /// </summary>
        int BlendShapeCount { get; }

        /// <summary>
        /// 1 フレーム分の時間進行。Expression トリガー型のみ実質的な work を行う
        /// （TransitionCalculator 状態の更新）。値提供型は通常空実装。
        /// </summary>
        /// <param name="deltaTime">前フレームからの経過秒数（&gt;= 0）</param>
        void Tick(float deltaTime);

        /// <summary>
        /// 自身の BlendShape 値を出力バッファに書込む。
        /// </summary>
        /// <param name="output">書込先バッファ。<c>Length &gt; 0</c>。長さ不足時は overlap のみ書込む。</param>
        /// <returns>有効なら true、無効/未接続なら false（false の場合 <paramref name="output"/> は変更されない）。</returns>
        bool TryWriteValues(Span<float> output);

        /// <summary>
        /// この入力源が contribute する BlendShape index 集合。
        /// </summary>
        /// <remarks>
        /// 返される <see cref="BitArray"/> は事前確保済み参照であり、<see cref="BitArray.Length"/> は
        /// <see cref="BlendShapeCount"/> と一致する。
        /// index 軸は mesh BlendShape index 空間であり、Aggregator 側の blendShapeCount と同一の軸である。
        /// mapping index や source 固有 index など入力源固有の index 空間を持つ実装は、構築時などに
        /// mesh index へ変換し、この mask を mesh-index 空間で立てる責務を持つ。
        /// 既存実装（AnalogBlendShapeInputSource, OscInputSource, ValueProviderInputSourceBase,
        /// ExpressionTriggerInputSourceBase など）はこの契約に従う。
        /// UnityEngine 型を含めず、実行中に新規確保しない。
        /// <see cref="TryWriteValues"/> が false を返す場合でも、構造上の contribute 集合として返す。
        /// </remarks>
        BitArray ContributeMask { get; }
    }
}
