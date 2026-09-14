using System;
using System.Buffers.Binary;

namespace Hidano.FacialControl.Adapters.OSC
{
    public ref struct OscPacketReader
    {
        private readonly ReadOnlySpan<byte> _packet;
        private bool _initialized;
        private bool _bareRead;
        private int _depth;
        private Frame _frame0;
        private Frame _frame1;
        private Frame _frame2;
        private Frame _frame3;
        private Frame _frame4;
        private Frame _frame5;
        private Frame _frame6;
        private Frame _frame7;

        private struct Frame
        {
            public int Next;
            public readonly int End;
            public readonly ulong Timestamp;

            public Frame(int next, int end, ulong timestamp)
            {
                Next = next;
                End = end;
                Timestamp = timestamp;
            }
        }

        public int SkippedElementCount { get; private set; }
        public OscPacketError LastError { get; private set; }

        public OscPacketReader(ReadOnlySpan<byte> packet)
        {
            _packet = packet;
            _initialized = false;
            _bareRead = false;
            _depth = 0;
            _frame0 = default;
            _frame1 = default;
            _frame2 = default;
            _frame3 = default;
            _frame4 = default;
            _frame5 = default;
            _frame6 = default;
            _frame7 = default;
            SkippedElementCount = 0;
            LastError = OscPacketError.None;
        }

        public static bool IsBundle(ReadOnlySpan<byte> packet)
        {
            return packet.Length >= 8 && packet[0] == (byte)'#' && packet[1] == (byte)'b' &&
                   packet[2] == (byte)'u' && packet[3] == (byte)'n' && packet[4] == (byte)'d' &&
                   packet[5] == (byte)'l' && packet[6] == (byte)'e' && packet[7] == 0;
        }

        public bool TryReadNext(out OscMessageView message)
        {
            message = default;
            if (!_initialized)
            {
                _initialized = true;
                if (IsBundle(_packet))
                {
                    if (!TryOpenBundle(_packet, 0, _packet.Length, out var frame))
                    {
                        return Fail(OscPacketError.Truncated);
                    }

                    SetFrame(0, frame);
                    _depth = 1;
                }
            }

            if (_depth == 0)
            {
                if (_bareRead)
                {
                    return false;
                }

                _bareRead = true;
                if (!TryParseMessage(_packet, OscBundleAccumulatorImmediateTimestamp, out message, out var error))
                {
                    return Fail(error);
                }

                return true;
            }

            while (_depth > 0)
            {
                var frame = GetFrame(_depth - 1);
                if (frame.Next >= frame.End)
                {
                    _depth--;
                    continue;
                }

                if (frame.End - frame.Next < 4)
                {
                    SetError(OscPacketError.Truncated);
                    _depth--;
                    continue;
                }

                var elementSize = BinaryPrimitives.ReadInt32BigEndian(_packet.Slice(frame.Next, 4));
                frame.Next += 4;
                if (elementSize < 0 || elementSize > frame.End - frame.Next || (elementSize & 3) != 0)
                {
                    SetError(elementSize >= 0 && elementSize <= frame.End - frame.Next
                        ? OscPacketError.Misaligned : OscPacketError.ArgumentOutOfRange);
                    _depth--;
                    continue;
                }

                var elementStart = frame.Next;
                var element = _packet.Slice(elementStart, elementSize);
                frame.Next += elementSize;
                SetFrame(_depth - 1, frame);

                if (IsBundle(element))
                {
                    if (_depth >= 8)
                    {
                        SetError(OscPacketError.BundleTooDeep);
                        continue;
                    }

                    if (element.Length < 16)
                    {
                        SetError(OscPacketError.Truncated);
                        continue;
                    }

                    var nested = new Frame(elementStart + 16, elementStart + element.Length,
                        BinaryPrimitives.ReadUInt64BigEndian(element.Slice(8, 8)));
                    SetFrame(_depth, nested);
                    _depth++;
                    continue;
                }

                if (TryParseMessage(element, frame.Timestamp, out message, out var error))
                {
                    return true;
                }

                SetError(error);
            }

            return false;
        }

        private const ulong OscBundleAccumulatorImmediateTimestamp = 0x1UL;

        private bool TryParseMessage(ReadOnlySpan<byte> packet, ulong timestamp, out OscMessageView message,
            out OscPacketError error)
        {
            message = default;
            error = OscPacketError.None;
            var offset = 0;
            if (!TryReadPaddedString(packet, ref offset, out var address))
            {
                error = OscPacketError.Truncated;
                return false;
            }

            if (address.Length == 0)
            {
                error = OscPacketError.BadAddress;
                return false;
            }

            if (!TryReadPaddedString(packet, ref offset, out var rawTypeTags))
            {
                error = OscPacketError.Truncated;
                return false;
            }

            if (rawTypeTags.Length == 0 || rawTypeTags[0] != (byte)',')
            {
                error = OscPacketError.BadTypeTags;
                return false;
            }

            for (var i = 1; i < rawTypeTags.Length; i++)
            {
                if (!OscTypeTag.IsKnown(rawTypeTags[i]))
                {
                    error = OscPacketError.UnknownTypeTag;
                    return false;
                }
            }

            var typeTags = rawTypeTags.Slice(1);
            var arguments = packet.Slice(offset);
            var validator = new OscArgumentReader(typeTags, arguments);
            while (validator.TryReadNext(out _))
            {
            }

            if (!validator.IsFullyConsumed)
            {
                error = validator.Error == OscPacketError.None ? OscPacketError.ArgumentOutOfRange : validator.Error;
                return false;
            }

            message = new OscMessageView(address, typeTags, arguments, packet, timestamp);
            return true;
        }

        private static bool TryOpenBundle(ReadOnlySpan<byte> packet, int start, int end, out Frame frame)
        {
            frame = default;
            if (end - start < 16 || !IsBundle(packet.Slice(start, end - start)))
            {
                return false;
            }

            var timestamp = BinaryPrimitives.ReadUInt64BigEndian(packet.Slice(start + 8, 8));
            frame = new Frame(start + 16, end, timestamp);
            return true;
        }

        private Frame GetFrame(int index)
        {
            switch (index)
            {
                case 0: return _frame0;
                case 1: return _frame1;
                case 2: return _frame2;
                case 3: return _frame3;
                case 4: return _frame4;
                case 5: return _frame5;
                case 6: return _frame6;
                default: return _frame7;
            }
        }

        private void SetFrame(int index, Frame frame)
        {
            switch (index)
            {
                case 0: _frame0 = frame; break;
                case 1: _frame1 = frame; break;
                case 2: _frame2 = frame; break;
                case 3: _frame3 = frame; break;
                case 4: _frame4 = frame; break;
                case 5: _frame5 = frame; break;
                case 6: _frame6 = frame; break;
                default: _frame7 = frame; break;
            }
        }

        private bool Fail(OscPacketError error)
        {
            SetError(error);
            return false;
        }

        private void SetError(OscPacketError error)
        {
            LastError = error;
            SkippedElementCount++;
        }

        private static bool TryReadPaddedString(ReadOnlySpan<byte> packet, ref int offset, out ReadOnlySpan<byte> value)
        {
            value = default;
            if (offset >= packet.Length)
            {
                return false;
            }

            var remainder = packet.Slice(offset);
            var terminator = remainder.IndexOf((byte)0);
            if (terminator < 0)
            {
                return false;
            }

            var paddedLength = (terminator + 4) & ~3;
            if (paddedLength > remainder.Length)
            {
                return false;
            }

            value = remainder.Slice(0, terminator);
            offset += paddedLength;
            return true;
        }
    }
}
