using System;
using System.Globalization;

namespace Hidano.FacialControl.Adapters.IFacialMocap
{
    /// <summary>
    /// iFacialMocap の UDP/TCP テキストパケットを <see cref="IFacialMocapFrame"/> に解析する
    /// UnityEngine 非依存の純粋パーサ。
    /// </summary>
    /// <remarks>
    /// 形式: <c>name-value|...|=head#ex,ey,ez,px,py,pz|rightEye#ex,ey,ez|leftEye#ex,ey,ez|</c>
    /// 標準は name-value 区切り <c>-</c>、v2 は <c>&amp;</c>（負値対応）。角度は度、BlendShape は 0〜100。
    /// </remarks>
    public static class IFacialMocapPacketParser
    {
        private const int MaxTransformComponents = 6;

        /// <summary>
        /// パケット文字列を解析し <paramref name="frame"/> に書き込む。
        /// </summary>
        /// <returns>BlendShape か Transform を 1 つ以上解析できたとき true。</returns>
        public static bool TryParse(string packet, IFacialMocapDataVersion version, IFacialMocapFrame frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            frame.Clear();
            if (string.IsNullOrEmpty(packet))
            {
                return false;
            }

            // TCP モードの終端マーカーを除去
            int terminatorIndex = packet.IndexOf(IFacialMocapProtocol.TcpTerminator, StringComparison.Ordinal);
            if (terminatorIndex >= 0)
            {
                packet = packet.Substring(0, terminatorIndex);
            }

            int sectionIndex = packet.IndexOf(IFacialMocapProtocol.SectionSeparator);
            string blendShapeSection = sectionIndex >= 0 ? packet.Substring(0, sectionIndex) : packet;
            string transformSection = sectionIndex >= 0 ? packet.Substring(sectionIndex + 1) : string.Empty;

            ParseBlendShapes(blendShapeSection, version, frame);
            if (transformSection.Length > 0)
            {
                ParseTransforms(transformSection, frame);
            }

            return frame.BlendShapes.Count > 0
                || frame.Head.HasValue
                || frame.RightEye.HasValue
                || frame.LeftEye.HasValue;
        }

        private static void ParseBlendShapes(string section, IFacialMocapDataVersion version, IFacialMocapFrame frame)
        {
            char separator = IFacialMocapProtocol.NameValueSeparator(version);
            int start = 0;
            int length = section.Length;
            while (start < length)
            {
                int bar = section.IndexOf(IFacialMocapProtocol.EntrySeparator, start);
                int end = bar < 0 ? length : bar;
                if (end > start)
                {
                    ParseBlendShapeEntry(section, start, end, separator, frame);
                }

                if (bar < 0)
                {
                    break;
                }

                start = bar + 1;
            }
        }

        private static void ParseBlendShapeEntry(string s, int start, int end, char separator, IFacialMocapFrame frame)
        {
            int separatorIndex = s.IndexOf(separator, start, end - start);
            // 区切りが無い / 名前が空 = BlendShape ではない（例: 制御トークン）→ skip
            if (separatorIndex <= start)
            {
                return;
            }

            string name = s.Substring(start, separatorIndex - start);
            int valueStart = separatorIndex + 1;
            int valueLength = end - valueStart;
            if (valueLength <= 0)
            {
                return;
            }

            string valueText = s.Substring(valueStart, valueLength);
            if (float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                frame.BlendShapes.Add(new IFacialMocapBlendShapeSample(name, value));
            }
        }

        private static void ParseTransforms(string section, IFacialMocapFrame frame)
        {
            int start = 0;
            int length = section.Length;
            while (start < length)
            {
                int bar = section.IndexOf(IFacialMocapProtocol.EntrySeparator, start);
                int end = bar < 0 ? length : bar;
                if (end > start)
                {
                    ParseTransformEntry(section, start, end, frame);
                }

                if (bar < 0)
                {
                    break;
                }

                start = bar + 1;
            }
        }

        private static void ParseTransformEntry(string s, int start, int end, IFacialMocapFrame frame)
        {
            int hashIndex = s.IndexOf(IFacialMocapProtocol.TransformSeparator, start, end - start);
            if (hashIndex <= start)
            {
                return;
            }

            string key = s.Substring(start, hashIndex - start);

            Span<float> components = stackalloc float[MaxTransformComponents];
            int count = ParseFloats(s, hashIndex + 1, end, components);
            if (count == 0)
            {
                return;
            }

            var sample = new IFacialMocapTransformSample
            {
                HasValue = true,
                EulerX = count > 0 ? components[0] : 0f,
                EulerY = count > 1 ? components[1] : 0f,
                EulerZ = count > 2 ? components[2] : 0f,
                PositionX = count > 3 ? components[3] : 0f,
                PositionY = count > 4 ? components[4] : 0f,
                PositionZ = count > 5 ? components[5] : 0f,
            };

            if (string.Equals(key, IFacialMocapProtocol.HeadKey, StringComparison.Ordinal))
            {
                frame.Head = sample;
            }
            else if (string.Equals(key, IFacialMocapProtocol.RightEyeKey, StringComparison.Ordinal))
            {
                frame.RightEye = sample;
            }
            else if (string.Equals(key, IFacialMocapProtocol.LeftEyeKey, StringComparison.Ordinal))
            {
                frame.LeftEye = sample;
            }
        }

        private static int ParseFloats(string s, int start, int end, Span<float> buffer)
        {
            int count = 0;
            int index = start;
            while (index < end && count < buffer.Length)
            {
                int comma = s.IndexOf(IFacialMocapProtocol.ComponentSeparator, index, end - index);
                int tokenEnd = comma < 0 ? end : comma;
                string token = s.Substring(index, tokenEnd - index);
                buffer[count++] = float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
                    ? v
                    : 0f;

                if (comma < 0)
                {
                    break;
                }

                index = comma + 1;
            }

            return count;
        }
    }
}
