using System;
using System.Buffers.Binary;

namespace Hidano.FacialControl.Adapters.OSC
{
    public ref struct OscArgumentReader
    {
        private readonly ReadOnlySpan<byte> _typeTags;
        private readonly ReadOnlySpan<byte> _arguments;
        private int _tagIndex;
        private int _offset;

        public OscPacketError Error { get; private set; }

        internal int ConsumedBytes => _offset;

        internal OscArgumentReader(ReadOnlySpan<byte> typeTags, ReadOnlySpan<byte> arguments)
        {
            _typeTags = typeTags;
            _arguments = arguments;
            _tagIndex = 0;
            _offset = 0;
            Error = OscPacketError.None;
        }

        public bool TryReadNext(out OscArgument argument)
        {
            argument = default;
            if (_tagIndex >= _typeTags.Length)
            {
                return false;
            }

            var tag = _typeTags[_tagIndex++];
            if (!OscTypeTag.IsKnown(tag))
            {
                Error = OscPacketError.UnknownTypeTag;
                return false;
            }

            if (!OscTypeTag.HasPayload(tag))
            {
                argument = new OscArgument(tag, ReadOnlySpan<byte>.Empty);
                return true;
            }

            if (tag == OscTypeTag.Int32 || tag == OscTypeTag.Float32)
            {
                if (!TryTake(4, out var numeric))
                {
                    return false;
                }

                argument = new OscArgument(tag, numeric);
                return true;
            }

            if (tag == OscTypeTag.String)
            {
                if (!TryReadPaddedString(out var value))
                {
                    return false;
                }

                argument = new OscArgument(tag, value);
                return true;
            }

            if (_offset > _arguments.Length - 4)
            {
                Error = OscPacketError.Truncated;
                return false;
            }

            var length = BinaryPrimitives.ReadInt32BigEndian(_arguments.Slice(_offset, 4));
            if (length < 0 || length > _arguments.Length - _offset - 4)
            {
                Error = OscPacketError.ArgumentOutOfRange;
                return false;
            }

            var start = _offset + 4;
            var paddedLength = Align4(length);
            if (paddedLength > _arguments.Length - start)
            {
                Error = OscPacketError.Misaligned;
                return false;
            }

            argument = new OscArgument(tag, _arguments.Slice(start, length));
            _offset = start + paddedLength;
            return true;
        }

        internal bool IsFullyConsumed => Error == OscPacketError.None &&
            _tagIndex == _typeTags.Length && _offset == _arguments.Length;

        private bool TryReadPaddedString(out ReadOnlySpan<byte> value)
        {
            var remaining = _arguments.Slice(_offset);
            var terminator = remaining.IndexOf((byte)0);
            if (terminator < 0)
            {
                value = default;
                Error = OscPacketError.Truncated;
                return false;
            }

            var paddedLength = Align4(terminator + 1);
            if (paddedLength > remaining.Length)
            {
                value = default;
                Error = OscPacketError.Misaligned;
                return false;
            }

            value = remaining.Slice(0, terminator);
            _offset += paddedLength;
            return true;
        }

        private bool TryTake(int length, out ReadOnlySpan<byte> value)
        {
            if (length > _arguments.Length - _offset)
            {
                value = default;
                Error = OscPacketError.Truncated;
                return false;
            }

            value = _arguments.Slice(_offset, length);
            _offset += length;
            return true;
        }

        private static int Align4(int length)
        {
            return (length + 3) & ~3;
        }
    }
}
