using System;

namespace Hidano.FacialControl.Adapters.OSC
{
    public readonly ref struct OscMessageView
    {
        public ReadOnlySpan<byte> Address { get; }
        public ReadOnlySpan<byte> TypeTags { get; }
        public ReadOnlySpan<byte> Arguments { get; }
        public ReadOnlySpan<byte> Element { get; }
        public ulong TimestampKey { get; }
        public int ArgumentCount => TypeTags.Length;

        public OscMessageView(
            ReadOnlySpan<byte> address, ReadOnlySpan<byte> typeTags,
            ReadOnlySpan<byte> arguments, ReadOnlySpan<byte> element, ulong timestampKey)
        {
            Address = address;
            TypeTags = typeTags;
            Arguments = arguments;
            Element = element;
            TimestampKey = timestampKey;
        }
    }
}
