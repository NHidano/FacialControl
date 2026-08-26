using UnityEngine.InputSystem;

namespace Hidano.FacialControl.Adapters.Processors
{
    /// <summary>
    /// アナログ入力の符号を反転する stateless な <see cref="InputProcessor{TValue}"/> 実装
    /// 。
    /// </summary>
    /// <remarks>
    /// 共通の <see cref="InputProcessor{TValue}"/> 派生定型として、serialize する field は持たない。
    /// </remarks>
    public sealed class AnalogInvertProcessor : InputProcessor<float>
    {
        /// <inheritdoc />
        public override float Process(float value, InputControl control)
        {
            return -value;
        }
    }
}
