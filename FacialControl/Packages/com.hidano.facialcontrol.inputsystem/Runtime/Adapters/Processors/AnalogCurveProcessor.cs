using UnityEngine.InputSystem;

namespace Hidano.FacialControl.Adapters.Processors
{
    /// <summary>
    /// アナログ入力に preset 4 種（Linear / EaseIn / EaseOut / EaseInOut）の
    /// hard-coded カーブを適用する stateless な <see cref="InputProcessor{TValue}"/> 実装
    /// 。
    /// </summary>
    /// <remarks>
    /// AnimationCurve は InputProcessor の serializer 制約（PrimitiveValue のみ）で扱えないため、
    /// preset を <see cref="int"/> field のみで運搬する。Custom Hermite は v2.1 以降のスコープ
    /// （Decision 3）。
    /// </remarks>
    public sealed class AnalogCurveProcessor : InputProcessor<float>
    {
        private const int PresetLinear = 0;
        private const int PresetEaseIn = 1;
        private const int PresetEaseOut = 2;
        private const int PresetEaseInOut = 3;

        /// <summary>
        /// 適用するカーブプリセット。
        /// 0=Linear, 1=EaseIn (v*v), 2=EaseOut (1-(1-v)^2), 3=EaseInOut (SmoothStep)。
        /// 未知の値は Linear にフォールバックする。
        /// </summary>
        public int preset = PresetLinear;

        /// <inheritdoc />
        public override float Process(float value, InputControl control)
        {
            switch (preset)
            {
                case PresetEaseIn:
                    return value * value;
                case PresetEaseOut:
                    {
                        float inv = 1f - value;
                        return 1f - inv * inv;
                    }
                case PresetEaseInOut:
                    return value * value * (3f - 2f * value);
                case PresetLinear:
                default:
                    return value;
            }
        }
    }
}
