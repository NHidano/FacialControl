using System;
using System.Buffers.Binary;

namespace Hidano.FacialControl.Adapters.OSC
{
    public readonly ref struct OscArgument
    {
        public byte Tag { get; }
        public ReadOnlySpan<byte> Bytes { get; }

        internal OscArgument(byte tag, ReadOnlySpan<byte> bytes)
        {
            Tag = tag;
            Bytes = bytes;
        }

        public bool TryGetFloat(out float value)
        {
            if (Tag == OscTypeTag.Float32 && Bytes.Length == 4)
            {
                value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(Bytes));
                return true;
            }

            if (Tag == OscTypeTag.Int32 && Bytes.Length == 4)
            {
                value = BinaryPrimitives.ReadInt32BigEndian(Bytes);
                return true;
            }

            value = default;
            return false;
        }

        public bool TryGetInt32(out int value)
        {
            if (Tag == OscTypeTag.Int32 && Bytes.Length == 4)
            {
                value = BinaryPrimitives.ReadInt32BigEndian(Bytes);
                return true;
            }

            value = default;
            return false;
        }

        public bool IsString => Tag == OscTypeTag.String;
        public bool IsBlob => Tag == OscTypeTag.Blob;
    }
}
