using System;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Domain.Services
{
    /// <summary>
    /// 遷移カーブの評価と遷移進行度の計算を行う静的サービス。
    /// 遷移カーブ種類（Linear / EaseIn / EaseOut / EaseInOut / Custom）に基づき
    /// 遷移進行度(t)からブレンドウェイトを算出する。
    /// </summary>
    public static class TransitionCalculator
    {
        /// <summary>
        /// 遷移カーブを評価し、進行度 t に対応するブレンドウェイトを返す。
        /// t は 0〜1 にクランプされる。
        /// </summary>
        /// <param name="curve">遷移カーブ設定</param>
        /// <param name="t">遷移進行度（0〜1）</param>
        /// <returns>ブレンドウェイト（0〜1）</returns>
        public static float Evaluate(in TransitionCurve curve, float t)
        {
            t = Clamp01(t);

            switch (curve.Type)
            {
                case TransitionCurveType.Linear:
                    return t;

                case TransitionCurveType.EaseIn:
                    return EaseIn(t);

                case TransitionCurveType.EaseOut:
                    return EaseOut(t);

                case TransitionCurveType.EaseInOut:
                    return EaseInOut(t);

                case TransitionCurveType.Custom:
                    return EvaluateCustom(curve.Keys.Span, t);

                default:
                    return t;
            }
        }

        /// <summary>
        /// 経過時間と遷移時間から遷移進行度（0〜1）を計算する。
        /// 遷移時間が 0 の場合は即座に完了（1.0 を返す）。
        /// </summary>
        /// <param name="elapsedTime">経過時間（秒）</param>
        /// <param name="duration">遷移時間（秒）</param>
        /// <returns>遷移進行度（0〜1）</returns>
        public static float ComputeProgress(float elapsedTime, float duration)
        {
            if (duration <= 0f)
                return 1f;

            return Clamp01(elapsedTime / duration);
        }

        /// <summary>
        /// カーブと経過時間からブレンドウェイトを一括計算する便利メソッド。
        /// ComputeProgress → Evaluate の順に処理する。
        /// </summary>
        /// <param name="curve">遷移カーブ設定</param>
        /// <param name="elapsedTime">経過時間（秒）</param>
        /// <param name="duration">遷移時間（秒）</param>
        /// <returns>ブレンドウェイト（0〜1）</returns>
        public static float ComputeBlendWeight(in TransitionCurve curve, float elapsedTime, float duration)
        {
            float progress = ComputeProgress(elapsedTime, duration);
            return Evaluate(curve, progress);
        }

        /// <summary>
        /// EaseIn: t^2（二次関数、開始が緩やか）
        /// </summary>
        private static float EaseIn(float t)
        {
            return t * t;
        }

        /// <summary>
        /// EaseOut: 1 - (1-t)^2（二次関数、終了が緩やか）
        /// EaseIn の対称カーブ: EaseOut(t) = 1 - EaseIn(1-t)
        /// </summary>
        private static float EaseOut(float t)
        {
            float invT = 1f - t;
            return 1f - invT * invT;
        }

        /// <summary>
        /// EaseInOut: SmoothStep 相当。t=0.5 で正確に 0.5 を返す対称カーブ。
        /// 前半は EaseIn、後半は EaseOut の特性を持つ。
        /// </summary>
        private static float EaseInOut(float t)
        {
            // SmoothStep: 3t^2 - 2t^3
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// カスタムキーフレーム配列によるエルミート補間評価。
        /// キーフレームが空の場合は Linear にフォールバック。
        /// キーフレームが 1 つの場合はその値を返す。
        /// </summary>
        private static float EvaluateCustom(ReadOnlySpan<CurveKeyFrame> keys, float t)
        {
            if (keys.Length == 0)
                return t;

            if (keys.Length == 1)
                return keys[0].Value;

            // t が最初のキーフレーム以前の場合
            if (t <= keys[0].Time)
                return keys[0].Value;

            // t が最後のキーフレーム以降の場合
            if (t >= keys[keys.Length - 1].Time)
                return keys[keys.Length - 1].Value;

            // 該当セグメントの探索
            for (int i = 0; i < keys.Length - 1; i++)
            {
                if (t >= keys[i].Time && t <= keys[i + 1].Time)
                {
                    return HermiteInterpolate(keys[i], keys[i + 1], t);
                }
            }

            // フォールバック（到達しないはず）
            return keys[keys.Length - 1].Value;
        }

        /// <summary>
        /// エルミート補間（Unity AnimationCurve 互換）。
        /// 2 つのキーフレーム間をタンジェント付きで補間する。
        /// </summary>
        private static float HermiteInterpolate(in CurveKeyFrame k0, in CurveKeyFrame k1, float t)
        {
            float dt = k1.Time - k0.Time;
            if (dt <= 0f)
                return k0.Value;

            float localT = (t - k0.Time) / dt;

            float m0 = k0.OutTangent * dt;
            float m1 = k1.InTangent * dt;

            float t2 = localT * localT;
            float t3 = t2 * localT;

            // エルミート基底関数
            float h00 = 2f * t3 - 3f * t2 + 1f;
            float h10 = t3 - 2f * t2 + localT;
            float h01 = -2f * t3 + 3f * t2;
            float h11 = t3 - t2;

            return h00 * k0.Value + h10 * m0 + h01 * k1.Value + h11 * m1;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
