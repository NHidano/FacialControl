using UnityEngine.InputSystem;

namespace Hidano.FacialControl.Adapters.Processors
{
    /// <summary>
    /// アナログ入力に定数 <see cref="offset"/> を加算する stateless な
    /// <see cref="InputProcessor{TValue}"/> 実装。
    /// </summary>
    /// <remarks>
    /// 共通の <see cref="InputProcessor{TValue}"/> 派生定型として、
    /// public な <see cref="float"/> field のみを serialize する。
    /// </remarks>
    public sealed class AnalogOffsetProcessor : InputProcessor<float>
    {
        /// <summary>
        /// 入力値に加算するオフセット。
        /// </summary>
        public float offset = 0f;

        /// <inheritdoc />
        public override float Process(float value, InputControl control)
        {
            return value + offset;
        }
    }
}
