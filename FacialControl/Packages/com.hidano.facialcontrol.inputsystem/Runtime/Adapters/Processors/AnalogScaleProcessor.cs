using UnityEngine.InputSystem;

namespace Hidano.FacialControl.Adapters.Processors
{
    /// <summary>
    /// アナログ入力を倍率 <see cref="factor"/> で線形スケールする stateless な
    /// <see cref="InputProcessor{TValue}"/> 実装。
    /// </summary>
    /// <remarks>
    /// 共通の <see cref="InputProcessor{TValue}"/> 派生定型として、
    /// public な <see cref="float"/> field のみを serialize する。
    /// </remarks>
    public sealed class AnalogScaleProcessor : InputProcessor<float>
    {
        /// <summary>
        /// 入力値に乗算するスケール係数。
        /// </summary>
        public float factor = 1f;

        /// <inheritdoc />
        public override float Process(float value, InputControl control)
        {
            return value * factor;
        }
    }
}
