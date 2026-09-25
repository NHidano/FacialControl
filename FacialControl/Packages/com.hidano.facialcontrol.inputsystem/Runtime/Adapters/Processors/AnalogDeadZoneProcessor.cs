using UnityEngine;
using UnityEngine.InputSystem;

namespace Hidano.FacialControl.Adapters.Processors
{
    /// <summary>
    /// アナログ入力に対するデッドゾーン処理を提供する stateless な <see cref="InputProcessor{TValue}"/> 実装。
    /// 入力絶対値が <see cref="min"/> 以下なら 0、<see cref="max"/> 以上なら ±1 にクランプし、
    /// 中間値は <c>(abs - min) / (max - min)</c> に正規化する。
    /// </summary>
    /// <remarks>
    /// 共通の <see cref="InputProcessor{TValue}"/> 派生定型として、
    /// public な <see cref="float"/> field のみを serialize する（AnimationCurve / string 不可）。
    /// </remarks>
    public sealed class AnalogDeadZoneProcessor : InputProcessor<float>
    {
        /// <summary>
        /// デッドゾーンの下限（絶対値）。これ以下の入力は 0 にクランプされる。
        /// </summary>
        public float min = 0f;

        /// <summary>
        /// 飽和点（絶対値）。これ以上の入力は ±1 にクランプされる。
        /// </summary>
        public float max = 1f;

        /// <inheritdoc />
        public override float Process(float value, InputControl control)
        {
            float abs = Mathf.Abs(value);
            if (abs <= min)
            {
                return 0f;
            }

            float range = max - min;
            if (range <= 0f)
            {
                return Mathf.Sign(value);
            }

            float normalized = (abs - min) / range;
            return Mathf.Sign(value) * Mathf.Clamp01(normalized);
        }
    }
}
