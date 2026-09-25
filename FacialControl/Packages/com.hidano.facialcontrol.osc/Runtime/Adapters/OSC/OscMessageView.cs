using System;

namespace Hidano.FacialControl.Adapters.OSC
{
    public readonly ref struct OscMessageView
    {
        public ReadOnlySpan<byte> Address { get; }
        public ReadOnlySpan<byte> TypeTags { get; }
        public ReadOnlySpan<byte> Arguments { get; }
        public ReadOnlySpan<byte> Element { get; }
        public int ElementOffset { get; }
        public ulong TimestampKey { get; }
        public int ArgumentCount => TypeTags.Length;

        public bool TryGetFirstAsFloat(out float value)
        {
            var reader = new OscArgumentReader(TypeTags, Arguments);
            if (!reader.TryReadNext(out var argument))
            {
                value = default;
                return false;
            }

            return argument.TryGetFloat(out value);
        }

        public OscArgumentReader GetArgumentReader()
        {
            return new OscArgumentReader(TypeTags, Arguments);
        }

        public OscMessageView(
            ReadOnlySpan<byte> address, ReadOnlySpan<byte> typeTags,
            ReadOnlySpan<byte> arguments, ReadOnlySpan<byte> element, ulong timestampKey, int elementOffset = 0)
        {
            Address = address;
            TypeTags = typeTags;
            Arguments = arguments;
            Element = element;
            TimestampKey = timestampKey;
            ElementOffset = elementOffset;
        }
    }
}
