using System;
using System.Buffers.Binary;
using System.Text;

namespace Hidano.FacialControl.Adapters.OSC
{
    internal static class OscMessageSerializer
    {
        private const int BundleHeaderLength = 16;

        public static bool TryWrite(uOSC.Message message, byte[] destination, out int length)
        {
            length = 0;
            if (!TryGetMessageLength(message, out var messageLength))
            {
                return false;
            }

            var bundled = OscBundleAccumulator.IsBundleTimestamp(message.timestamp.value);
            var requiredLength = bundled
                ? BundleHeaderLength + 4 + messageLength
                : messageLength;
            if (destination == null || requiredLength < 0 || destination.Length < requiredLength)
            {
                return false;
            }

            var offset = 0;
            if (bundled)
            {
                destination[offset++] = (byte)'#';
                destination[offset++] = (byte)'b';
                destination[offset++] = (byte)'u';
                destination[offset++] = (byte)'n';
                destination[offset++] = (byte)'d';
                destination[offset++] = (byte)'l';
                destination[offset++] = (byte)'e';
                destination[offset++] = 0;
                BinaryPrimitives.WriteUInt64BigEndian(destination.AsSpan(offset, 8), message.timestamp.value);
                offset += 8;
                BinaryPrimitives.WriteInt32BigEndian(destination.AsSpan(offset, 4), messageLength);
                offset += 4;
            }

            WritePaddedString(message.address, destination, ref offset);
            var typeTagStart = offset;
            destination[offset++] = (byte)',';
            for (var i = 0; i < message.values.Length; i++)
            {
                destination[offset++] = GetTypeTag(message.values[i]);
            }

            // OSC 文字列は必ず NUL 終端を含めて 4 byte 境界へ揃える。
            // 終端を数えずに揃えると ",fff" のように長さが 4 の倍数のとき NUL が消え、
            // リーダーが次の NUL まで型タグを読み進めてしまう。
            destination[offset++] = 0;
            PadString(destination, typeTagStart, offset - typeTagStart, ref offset);
            for (var i = 0; i < message.values.Length; i++)
            {
                WriteValue(message.values[i], destination, ref offset);
            }

            length = offset;
            return true;
        }

        public static int GetRequiredLength(uOSC.Message message)
        {
            if (!TryGetMessageLength(message, out var messageLength))
            {
                return 0;
            }

            if (!OscBundleAccumulator.IsBundleTimestamp(message.timestamp.value))
            {
                return messageLength;
            }

            try
            {
                return checked(BundleHeaderLength + 4 + messageLength);
            }
            catch (OverflowException)
            {
                return 0;
            }
        }

        private static bool TryGetMessageLength(uOSC.Message message, out int length)
        {
            length = 0;
            if (string.IsNullOrEmpty(message.address) || message.values == null)
            {
                return false;
            }

            try
            {
                var addressLength = PaddedStringLength(message.address);
                // ',' + 型タグ + NUL 終端
                var typeTagLength = PaddedStringLengthForPayload(message.values.Length + 2);
                var payloadLength = 0;
                for (var i = 0; i < message.values.Length; i++)
                {
                    var value = message.values[i];
                    if (value is int || value is float)
                    {
                        payloadLength = checked(payloadLength + 4);
                    }
                    else if (value is string text)
                    {
                        if (text == null)
                        {
                            return false;
                        }

                        payloadLength = checked(payloadLength + PaddedStringLength(text));
                    }
                    else if (value is byte[] blob)
                    {
                        if (blob == null)
                        {
                            return false;
                        }

                        payloadLength = checked(payloadLength + 4 + Align4(blob.Length));
                    }
                    else if (value is long int64)
                    {
                        // 実ワイヤ形式（OscSender / OscBundleBuilder）は sender_id の startedAtUnixMs を
                        // 10 進文字列で送るため、facade の long も同じ表現へ写像する。
                        payloadLength = checked(payloadLength + PaddedStringLength(FormatInt64(int64)));
                    }
                    else if (!(value is bool))
                    {
                        return false;
                    }
                }

                length = checked(addressLength + typeTagLength + payloadLength);
                return true;
            }
            catch (OverflowException)
            {
                length = 0;
                return false;
            }
        }

        private static byte GetTypeTag(object value)
        {
            if (value is int) return OscTypeTag.Int32;
            if (value is float) return OscTypeTag.Float32;
            if (value is string || value is long) return OscTypeTag.String;
            if (value is byte[]) return OscTypeTag.Blob;
            return (bool)value ? OscTypeTag.True : OscTypeTag.False;
        }

        private static string FormatInt64(long value)
        {
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void WriteValue(object value, byte[] destination, ref int offset)
        {
            if (value is int intValue)
            {
                BinaryPrimitives.WriteInt32BigEndian(destination.AsSpan(offset, 4), intValue);
                offset += 4;
            }
            else if (value is float floatValue)
            {
                BinaryPrimitives.WriteInt32BigEndian(destination.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(floatValue));
                offset += 4;
            }
            else if (value is string text)
            {
                WritePaddedString(text, destination, ref offset);
            }
            else if (value is long int64)
            {
                WritePaddedString(FormatInt64(int64), destination, ref offset);
            }
            else if (value is byte[] blob)
            {
                BinaryPrimitives.WriteInt32BigEndian(destination.AsSpan(offset, 4), blob.Length);
                offset += 4;
                blob.AsSpan().CopyTo(destination.AsSpan(offset, blob.Length));
                offset += blob.Length;
                var paddedLength = Align4(blob.Length);
                destination.AsSpan(offset, paddedLength - blob.Length).Clear();
                offset += paddedLength - blob.Length;
            }
        }

        private static void WritePaddedString(string value, byte[] destination, ref int offset)
        {
            var start = offset;
            offset += Encoding.UTF8.GetBytes(value, 0, value.Length, destination, offset);
            destination[offset++] = 0;
            PadString(destination, start, offset - start, ref offset);
        }

        private static void PadString(byte[] destination, int start, int length, ref int offset)
        {
            var paddedLength = Align4(length);
            destination.AsSpan(start + length, paddedLength - length).Clear();
            offset = start + paddedLength;
        }

        private static int PaddedStringLength(string value)
        {
            return PaddedStringLengthForPayload(Encoding.UTF8.GetByteCount(value) + 1);
        }

        private static int PaddedStringLengthForPayload(int length)
        {
            return checked((length + 3) & ~3);
        }

        private static int Align4(int length)
        {
            return checked((length + 3) & ~3);
        }
    }
}
