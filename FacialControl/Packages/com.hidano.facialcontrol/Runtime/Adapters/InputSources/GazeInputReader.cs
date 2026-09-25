using Hidano.FacialControl.Domain.Interfaces;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.InputSources
{
    /// <summary>
    /// gaze 入力を Vector2 として解釈する共通実装。
    /// </summary>
    public static class GazeInputReader
    {
        /// <summary>
        /// 入力源を読み取り、成功時は両軸を [-1, 1] に clamp して返す。
        /// scalar 入力は x に値を割り当て、y は 0 とする。
        /// </summary>
        public static bool TryReadXY(IAnalogInputSource source, out float x, out float y)
        {
            x = 0f;
            y = 0f;
            if (source == null || !source.IsValid)
            {
                return false;
            }

            bool hasValue;
            if (source.AxisCount >= 2)
            {
                hasValue = source.TryReadVector2(out x, out y);
            }
            else
            {
                hasValue = source.TryReadScalar(out x);
            }

            if (!hasValue)
            {
                x = 0f;
                y = 0f;
                return false;
            }

            x = Mathf.Clamp(x, -1f, 1f);
            y = Mathf.Clamp(y, -1f, 1f);
            return true;
        }
    }
}
