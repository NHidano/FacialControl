using System;
using System.Buffers.Binary;

namespace Hidano.FacialControl.Adapters.OSC
{
    public static class OscMessageClassifier
    {
        public static int ParseAndClassify(ReadOnlySpan<byte> datagram, OscAddressKeyTable table,
            Span<OscResolvedMessage> records, OscReceiveDiagnostics diagnostics)
        {
            int count = 0;
            var reader = new OscPacketReader(datagram);
            while (reader.TryReadNext(out OscMessageView view))
            {
                if (!TryClassify(in view, table, out OscResolvedMessage record))
                {
                    continue;
                }

                if (count == records.Length)
                {
                    diagnostics?.IncrementTruncatedDatagrams();
                    break;
                }

                records[count++] = record;
            }

            if (reader.SkippedElementCount != 0)
            {
                diagnostics?.IncrementMalformedElements();
            }

            return count;
        }

        public static bool TryClassify(in OscMessageView view, OscAddressKeyTable table,
            out OscResolvedMessage record)
        {
            if (table == null || !table.TryResolve(view.Address, out var resolution) ||
                resolution.IsUnmapped)
            {
                record = default;
                return false;
            }

            OscResolvedKind kind = OscResolvedKind.None;
            if (resolution.MappingIndex >= 0) kind |= OscResolvedKind.BlendShape;
            if (resolution.GazeRouteSet >= 0) kind |= OscResolvedKind.Gaze;
            if (resolution.ListenerSlot >= 0) kind |= OscResolvedKind.Listener;
            if (resolution.Control != OscControlKind.None) kind |= OscResolvedKind.Control;

            bool hasFloat = view.TryGetFirstAsFloat(out float floatValue);
            Guid senderUuid = default;
            long senderStartedAt = 0L;
            bool senderValid = false;
            if (resolution.Control == OscControlKind.SenderId)
            {
                senderValid = TryParseSenderIdentity(view, out senderUuid, out senderStartedAt);
            }

            record = new OscResolvedMessage(
                kind, resolution.Control, hasFloat, floatValue,
                resolution.MappingIndex, resolution.GazeRouteSet, resolution.ListenerSlot,
                view.TimestampKey, table.Version, view.ElementOffset, view.Element.Length,
                senderUuid, senderStartedAt, senderValid);
            return true;
        }

        private static bool TryParseSenderIdentity(in OscMessageView view, out Guid uuid, out long startedAt)
        {
            uuid = default;
            startedAt = 0L;
            if (view.ArgumentCount != 2) return false;

            var reader = view.GetArgumentReader();
            if (!reader.TryReadNext(out var uuidArgument) ||
                !reader.TryReadNext(out var startedArgument) || !reader.IsFullyConsumed)
            {
                return false;
            }

            if (uuidArgument.Tag == OscTypeTag.Blob && uuidArgument.Bytes.Length == 16)
            {
                uuid = new Guid(uuidArgument.Bytes);
            }
            else if (uuidArgument.Tag == OscTypeTag.String && !TryParseGuidUtf8(uuidArgument.Bytes, out uuid))
            {
                return false;
            }
            else if (uuidArgument.Tag != OscTypeTag.Blob && uuidArgument.Tag != OscTypeTag.String)
            {
                return false;
            }

            if (startedArgument.Tag == OscTypeTag.Int32)
            {
                startedAt = BinaryPrimitives.ReadInt32BigEndian(startedArgument.Bytes);
            }
            else if (startedArgument.Tag == OscTypeTag.String && !TryParseInt64Utf8(startedArgument.Bytes, out startedAt))
            {
                return false;
            }
            else if (startedArgument.Tag != OscTypeTag.Int32 && startedArgument.Tag != OscTypeTag.String)
            {
                return false;
            }

            return uuid != Guid.Empty && startedAt >= 0L;
        }

        private static bool TryParseGuidUtf8(ReadOnlySpan<byte> bytes, out Guid value)
        {
            Span<char> chars = stackalloc char[64];
            if (bytes.Length > chars.Length) { value = default; return false; }
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] > 0x7f) { value = default; return false; }
                chars[i] = (char)bytes[i];
            }
            return Guid.TryParse(chars.Slice(0, bytes.Length), out value);
        }

        private static bool TryParseInt64Utf8(ReadOnlySpan<byte> bytes, out long value)
        {
            value = 0L;
            if (bytes.Length == 0) return false;
            int index = 0;
            bool negative = bytes[0] == (byte)'-';
            if (negative) { index++; if (index == bytes.Length) return false; }
            for (; index < bytes.Length; index++)
            {
                byte digit = bytes[index];
                if (digit < (byte)'0' || digit > (byte)'9') return false;
                int d = digit - (byte)'0';
                if (value > (long.MaxValue - d) / 10L) return false;
                value = value * 10L + d;
            }
            if (negative) value = -value;
            return true;
        }
    }
}
