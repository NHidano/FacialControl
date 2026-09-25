using System;

namespace Hidano.FacialControl.Adapters.OSC
{
    [Flags]
    public enum OscResolvedKind : byte
    {
        None = 0,
        BlendShape = 1,
        Gaze = 2,
        Listener = 4,
        Control = 8
    }

    public readonly struct OscResolvedMessage
    {
        public readonly OscResolvedKind Kind;
        public readonly OscControlKind Control;
        public readonly bool HasFloat;
        public readonly float FloatValue;
        public readonly int MappingIndex;
        public readonly int GazeRouteSet;
        public readonly int ListenerSlot;
        public readonly ulong TimestampKey;
        public readonly int TableVersion;
        public readonly int ElementOffset;
        public readonly int ElementLength;
        public readonly Guid SenderUuid;
        public readonly long SenderStartedAtUnixMs;
        public readonly bool SenderIdentityValid;

        public OscResolvedMessage(
            OscResolvedKind kind, OscControlKind control, bool hasFloat, float floatValue,
            int mappingIndex, int gazeRouteSet, int listenerSlot, ulong timestampKey,
            int tableVersion, int elementOffset, int elementLength, Guid senderUuid,
            long senderStartedAtUnixMs, bool senderIdentityValid)
        {
            Kind = kind;
            Control = control;
            HasFloat = hasFloat;
            FloatValue = floatValue;
            MappingIndex = mappingIndex;
            GazeRouteSet = gazeRouteSet;
            ListenerSlot = listenerSlot;
            TimestampKey = timestampKey;
            TableVersion = tableVersion;
            ElementOffset = elementOffset;
            ElementLength = elementLength;
            SenderUuid = senderUuid;
            SenderStartedAtUnixMs = senderStartedAtUnixMs;
            SenderIdentityValid = senderIdentityValid;
        }
    }
}
