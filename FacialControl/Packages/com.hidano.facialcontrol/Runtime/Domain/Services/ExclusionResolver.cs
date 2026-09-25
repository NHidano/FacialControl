using System;

namespace Hidano.FacialControl.Domain.Services
{
    /// <summary>
    /// レイヤー内の排他ロジック（LastWins / Blend）と遷移割込のスナップショット処理を行う静的サービス。
    /// 全メソッドは GC フリーで動作する（Span / 配列の再利用を前提）。
    /// </summary>
    public static class ExclusionResolver
    {
        /// <summary>
        /// LastWins モード: 旧 Expression (from) から新 Expression (to) へのクロスフェードを計算する。
        /// weight=0 で from の値、weight=1 で to の値を返す。結果は 0〜1 にクランプされる。
        /// </summary>
        /// <param name="fromValues">遷移元の BlendShape ウェイト配列</param>
        /// <param name="toValues">遷移先の BlendShape ウェイト配列</param>
        /// <param name="weight">遷移ウェイト（0〜1、範囲外はクランプ）</param>
        /// <param name="output">出力先 BlendShape ウェイト配列</param>
        public static void ResolveLastWins(ReadOnlySpan<float> fromValues, ReadOnlySpan<float> toValues, float weight, Span<float> output)
        {
            weight = Clamp01(weight);

            int length = output.Length;
            for (int i = 0; i < length; i++)
            {
                float from = fromValues[i];
                float to = toValues[i];
                output[i] = Clamp01(from + (to - from) * weight);
            }
        }

        /// <summary>
        /// LastWins モード: 配列ベースのオーバーロード。
        /// </summary>
        public static void ResolveLastWins(float[] fromValues, float[] toValues, float weight, float[] output)
        {
            ResolveLastWins(
                new ReadOnlySpan<float>(fromValues),
                new ReadOnlySpan<float>(toValues),
                weight,
                new Span<float>(output));
        }

        /// <summary>
        /// Blend モード: Expression のウェイト付き値を出力配列に加算する。
        /// 複数回呼び出すことで複数 Expression のブレンドが実現される。
        /// 加算結果は 0〜1 にクランプされる。
        /// </summary>
        /// <param name="values">Expression の BlendShape ウェイト配列</param>
        /// <param name="weight">Expression のブレンドウェイト（0〜1、範囲外はクランプ）</param>
        /// <param name="output">出力先（既存値に加算される）</param>
        public static void ResolveBlend(ReadOnlySpan<float> values, float weight, Span<float> output)
        {
            weight = Clamp01(weight);

            int length = output.Length;
            for (int i = 0; i < length; i++)
            {
                output[i] = Clamp01(output[i] + values[i] * weight);
            }
        }

        /// <summary>
        /// Blend モード: 配列ベースのオーバーロード。
        /// </summary>
        public static void ResolveBlend(float[] values, float weight, float[] output)
        {
            ResolveBlend(
                new ReadOnlySpan<float>(values),
                weight,
                new Span<float>(output));
        }

        /// <summary>
        /// 遷移割込用スナップショット: 現在の BlendShape 値をスナップショットバッファにコピーする。
        /// コピー先バッファの再利用により GC フリーで動作する。
        /// </summary>
        /// <param name="currentValues">現在の BlendShape ウェイト値</param>
        /// <param name="snapshot">スナップショット保存先バッファ</param>
        public static void TakeSnapshot(ReadOnlySpan<float> currentValues, Span<float> snapshot)
        {
            currentValues.CopyTo(snapshot);
        }

        /// <summary>
        /// 遷移割込用スナップショット: 配列ベースのオーバーロード。
        /// </summary>
        public static void TakeSnapshot(float[] currentValues, float[] snapshot)
        {
            TakeSnapshot(
                new ReadOnlySpan<float>(currentValues),
                new Span<float>(snapshot));
        }

        /// <summary>
        /// 出力配列をゼロクリアする。Blend モードの計算開始前に使用する。
        /// </summary>
        /// <param name="output">クリア対象の配列</param>
        public static void ClearOutput(Span<float> output)
        {
            output.Clear();
        }

        /// <summary>
        /// 出力配列をゼロクリアする: 配列ベースのオーバーロード。
        /// </summary>
        public static void ClearOutput(float[] output)
        {
            ClearOutput(new Span<float>(output));
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
