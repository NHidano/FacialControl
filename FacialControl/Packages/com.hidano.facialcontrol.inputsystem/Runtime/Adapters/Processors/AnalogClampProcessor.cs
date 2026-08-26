using UnityEngine;
using UnityEngine.InputSystem;

namespace Hidano.FacialControl.Adapters.Processors
{
    /// <summary>
    /// アナログ入力を <see cref="min"/>..<see cref="max"/> の閉区間にクランプする stateless な
    /// <see cref="InputProcessor{TValue}"/> 実装。
    /// </summary>
    /// <remarks>
    /// 共通の <see cref="InputProcessor{TValue}"/> 派生定型として、
    /// public な <see cref="float"/> field のみを serialize する。
    /// </remarks>
    public sealed class AnalogClampProcessor : InputProcessor<float>
    {
        /// <summary>
        /// 出力下限。
        /// </summary>
        public float min = 0f;

        /// <summary>
        /// 出力上限。
        /// </summary>
        public float max = 1f;

        /// <inheritdoc />
        public override float Process(float value, InputControl control)
        {
            return Mathf.Clamp(value, min, max);
        }
    }
}
