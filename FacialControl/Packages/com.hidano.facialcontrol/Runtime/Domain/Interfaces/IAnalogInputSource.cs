using System;

namespace Hidano.FacialControl.Domain.Interfaces
{
    /// <summary>
    /// アナログ入力源の Unity 非依存契約。scalar / 2-axis / N-axis のいずれかを返す。
    /// Domain 層配置のため Unity API に非依存。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Preconditions:
    /// <list type="bullet">
    ///   <item><c>Tick</c> はメインスレッドから毎フレーム 1 回呼ばれる。</item>
    ///   <item><c>output</c> は呼出側が確保する（<see cref="TryReadAxes"/>）。</item>
    /// </list>
    /// </para>
    /// <para>
    /// Postconditions:
    /// <list type="bullet">
    ///   <item><see cref="IsValid"/> が false のとき <c>TryRead*</c> は false を返し、out 引数 / output は不変。</item>
    ///   <item>true でも実装側のサポート外形（例: scalar 専用ソースに <see cref="TryReadAxes"/>）は false を許容。</item>
    ///   <item><see cref="TryReadAxes"/> は overlap 範囲のみ書込み、output が AxisCount より長い場合の残余は呼出側責務。</item>
    /// </list>
    /// </para>
    /// <para>
    /// Invariants:
    /// <list type="bullet">
    ///   <item><see cref="Id"/> と <see cref="AxisCount"/> は構築後不変。</item>
    ///   <item><see cref="Id"/> は <c>[a-zA-Z0-9_.\-:]{1,64}</c> 規約を満たす。<c>:</c> は <c>slug:sub</c> 合成キーの区切り文字。</item>
    /// </list>
    /// </para>
    /// </remarks>
    public interface IAnalogInputSource
    {
        /// <summary>
        /// 入力源識別子。<c>[a-zA-Z0-9_.\-:]{1,64}</c> 規約に従う。
        /// <c>:</c> は <c>slug:sub</c> 形式の合成キー区切り文字として使われる。
        /// </summary>
        string Id { get; }

        /// <summary>
        /// 有効性フラグ。false の間 <c>TryRead*</c> は false を返してよい（last-valid policy）。
        /// </summary>
        bool IsValid { get; }

        /// <summary>
        /// 本ソースが提供する N-axis のチャンネル数（scalar=1, Vector2=2, ARKit=52 等）。
        /// </summary>
        int AxisCount { get; }

        /// <summary>
        /// 1 フレーム分の時間進行。Tick 内でフレッシュなキャッシュへ更新する実装が一般的。
        /// </summary>
        /// <param name="deltaTime">前フレームからの経過秒数（&gt;= 0）</param>
        void Tick(float deltaTime);

        /// <summary>
        /// scalar 値の取得。<see cref="AxisCount"/> &gt;= 1 のとき axis 0 の値を返す。
        /// </summary>
        /// <returns>有効なら true、無効/未対応なら false（false の場合 <paramref name="value"/> は不変）。</returns>
        bool TryReadScalar(out float value);

        /// <summary>
        /// 2-axis 値の取得。<see cref="AxisCount"/> &gt;= 2 のとき axis 0/1 を返す。
        /// </summary>
        /// <returns>有効なら true、無効/未対応なら false（false の場合 <paramref name="x"/> / <paramref name="y"/> は不変）。</returns>
        bool TryReadVector2(out float x, out float y);

        /// <summary>
        /// N-axis 値の取得。<paramref name="output"/> の長さが <see cref="AxisCount"/> より小さい場合は overlap のみ書込み true。
        /// 長い場合は <see cref="AxisCount"/> まで書込み、残余は呼出側責務。
        /// </summary>
        /// <param name="output">書込先バッファ。Length &gt; 0。</param>
        /// <returns>有効なら true、無効なら false（false の場合 <paramref name="output"/> は不変）。</returns>
        bool TryReadAxes(Span<float> output);
    }
}
