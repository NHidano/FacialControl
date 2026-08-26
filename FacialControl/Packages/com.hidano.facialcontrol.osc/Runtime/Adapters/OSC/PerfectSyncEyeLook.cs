using System;
using System.Text;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// PerfectSync 互換の eyeLook 系 8 BlendShape と左右別 Gaze Vector2 の相互変換を行う。
    /// </summary>
    public static class PerfectSyncEyeLook
    {
        public const int Count = 8;
        public const string ArKitAddressPrefix = "/ARKit/";

        public const int EyeLookInLeftIndex = 0;
        public const int EyeLookOutLeftIndex = 1;
        public const int EyeLookUpLeftIndex = 2;
        public const int EyeLookDownLeftIndex = 3;
        public const int EyeLookInRightIndex = 4;
        public const int EyeLookOutRightIndex = 5;
        public const int EyeLookUpRightIndex = 6;
        public const int EyeLookDownRightIndex = 7;

        public const string EyeLookInLeft = "eyeLookInLeft";
        public const string EyeLookOutLeft = "eyeLookOutLeft";
        public const string EyeLookUpLeft = "eyeLookUpLeft";
        public const string EyeLookDownLeft = "eyeLookDownLeft";
        public const string EyeLookInRight = "eyeLookInRight";
        public const string EyeLookOutRight = "eyeLookOutRight";
        public const string EyeLookUpRight = "eyeLookUpRight";
        public const string EyeLookDownRight = "eyeLookDownRight";

        public static readonly string[] Names =
        {
            EyeLookInLeft,
            EyeLookOutLeft,
            EyeLookUpLeft,
            EyeLookDownLeft,
            EyeLookInRight,
            EyeLookOutRight,
            EyeLookUpRight,
            EyeLookDownRight
        };

        private static readonly byte[][] ArKitAddressUtf8Bytes = BuildArKitAddressUtf8Bytes();

        public static ReadOnlySpan<byte> GetArKitAddressUtf8Bytes(int index)
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index,
                    $"インデックスは 0〜{Count - 1} の範囲で指定してください。");
            }

            return ArKitAddressUtf8Bytes[index];
        }

        public static void Compose(Vector2 left, Vector2 right, Span<float> output)
        {
            if (output.Length < Count)
            {
                throw new ArgumentException($"出力先は {Count} 要素以上である必要があります。", nameof(output));
            }

            output[EyeLookInLeftIndex] = NegativePart(left.x);
            output[EyeLookOutLeftIndex] = PositivePart(left.x);
            output[EyeLookUpLeftIndex] = PositivePart(left.y);
            output[EyeLookDownLeftIndex] = NegativePart(left.y);
            output[EyeLookInRightIndex] = NegativePart(right.x);
            output[EyeLookOutRightIndex] = PositivePart(right.x);
            output[EyeLookUpRightIndex] = PositivePart(right.y);
            output[EyeLookDownRightIndex] = NegativePart(right.y);
        }

        public static void Decompose(ReadOnlySpan<float> input, out Vector2 left, out Vector2 right)
        {
            if (input.Length < Count)
            {
                throw new ArgumentException($"入力元は {Count} 要素以上である必要があります。", nameof(input));
            }

            left = new Vector2(
                input[EyeLookOutLeftIndex] - input[EyeLookInLeftIndex],
                input[EyeLookUpLeftIndex] - input[EyeLookDownLeftIndex]);
            right = new Vector2(
                input[EyeLookOutRightIndex] - input[EyeLookInRightIndex],
                input[EyeLookUpRightIndex] - input[EyeLookDownRightIndex]);
        }

        private static float PositivePart(float value)
        {
            return value > 0f ? value : 0f;
        }

        private static float NegativePart(float value)
        {
            return value < 0f ? -value : 0f;
        }

        private static byte[][] BuildArKitAddressUtf8Bytes()
        {
            var result = new byte[Count][];

            for (int i = 0; i < Count; i++)
            {
                result[i] = Encoding.UTF8.GetBytes(ArKitAddressPrefix + Names[i]);
            }

            return result;
        }
    }
}
