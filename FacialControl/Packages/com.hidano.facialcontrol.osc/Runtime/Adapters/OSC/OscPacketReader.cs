using System;

namespace Hidano.FacialControl.Adapters.OSC
{
    public ref struct OscPacketReader
    {
        private readonly ReadOnlySpan<byte> _packet;
        private bool _read;

        public int SkippedElementCount { get; private set; }
        public OscPacketError LastError { get; private set; }

        public OscPacketReader(ReadOnlySpan<byte> packet)
        {
            _packet = packet;
            _read = false;
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
            if (_read)
            {
                return false;
            }

            _read = true;
            if (IsBundle(_packet))
            {
                return Fail(OscPacketError.ArgumentOutOfRange);
            }

            var offset = 0;
            if (!TryReadPaddedString(_packet, ref offset, out var address))
            {
                return Fail(OscPacketError.Truncated);
            }

            if (address.Length == 0)
            {
                return Fail(OscPacketError.BadAddress);
            }

            if (!TryReadPaddedString(_packet, ref offset, out var rawTypeTags))
            {
                return Fail(OscPacketError.Truncated);
            }

            if (rawTypeTags.Length == 0 || rawTypeTags[0] != (byte)',' || !IsValidTypeTagList(rawTypeTags))
            {
                return Fail(rawTypeTags.Length == 0 || rawTypeTags[0] != (byte)','
                    ? OscPacketError.BadTypeTags : OscPacketError.UnknownTypeTag);
            }

            var typeTags = rawTypeTags.Slice(1);
            var arguments = _packet.Slice(offset);
            var validator = new OscArgumentReader(typeTags, arguments);
            while (validator.TryReadNext(out _))
            {
            }

            if (!validator.IsFullyConsumed)
            {
                return Fail(validator.Error == OscPacketError.None ? OscPacketError.ArgumentOutOfRange : validator.Error);
            }

            message = new OscMessageView(address, typeTags, arguments, _packet, 0x1);
            return true;
        }

        private bool Fail(OscPacketError error)
        {
            LastError = error;
            SkippedElementCount = 1;
            return false;
        }

        private static bool IsValidTypeTagList(ReadOnlySpan<byte> typeTags)
        {
            for (var i = 1; i < typeTags.Length; i++)
            {
                if (!OscTypeTag.IsKnown(typeTags[i]))
                {
                    return false;
                }
            }

            return true;
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
