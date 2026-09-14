using NUnit.Framework;
using uOSC;
using Hidano.FacialControl.Adapters.OSC;

namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.OSC
{
    public sealed class OscMessageSerializerTests
    {
        [Test]
        public void TryWrite_AllSupportedTypes_RoundTripsThroughPacketReader()
        {
            var blob = new byte[] { 1, 2, 3, 0xff };
            var message = new Message("/values", 1.25f, 7, "hello", blob, true, false);

            var destination = new byte[OscMessageSerializer.GetRequiredLength(message)];
            Assert.That(OscMessageSerializer.TryWrite(message, destination, out var length), Is.True);

            var reader = new OscPacketReader(new System.ReadOnlySpan<byte>(destination, 0, length));
            Assert.That(reader.TryReadNext(out var view), Is.True);
            Assert.That(view.TimestampKey, Is.EqualTo(1UL));
            Assert.That(view.TypeTags.ToArray(), Is.EqualTo(new[] { (byte)'f', (byte)'i', (byte)'s', (byte)'b', (byte)'T', (byte)'F' }));

            var arguments = view.GetArgumentReader();
            Assert.That(arguments.TryReadNext(out var floatValue), Is.True);
            Assert.That(floatValue.TryGetFloat(out var actualFloat), Is.True);
            Assert.That(actualFloat, Is.EqualTo(1.25f));
            Assert.That(arguments.TryReadNext(out var intValue), Is.True);
            Assert.That(intValue.TryGetInt32(out var actualInt), Is.True);
            Assert.That(actualInt, Is.EqualTo(7));
            Assert.That(arguments.TryReadNext(out var stringValue), Is.True);
            Assert.That(stringValue.Bytes.ToArray(), Is.EqualTo(new byte[] { (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o' }));
            Assert.That(arguments.TryReadNext(out var blobValue), Is.True);
            Assert.That(blobValue.Bytes.ToArray(), Is.EqualTo(blob));
            Assert.That(arguments.TryReadNext(out var trueValue), Is.True);
            Assert.That(trueValue.Tag, Is.EqualTo((byte)'T'));
            Assert.That(arguments.TryReadNext(out var falseValue), Is.True);
            Assert.That(falseValue.Tag, Is.EqualTo((byte)'F'));
            Assert.That(arguments.TryReadNext(out _), Is.False);
            Assert.That(reader.TryReadNext(out _), Is.False);
        }

        [Test]
        public void TryWrite_BundleTimestamp_RoundTripsTimestampAndMessage()
        {
            const ulong timestamp = 0x0000000200000003UL;
            var message = new Message("/bundle", 0.5f) { timestamp = new Timestamp(timestamp) };
            var destination = new byte[OscMessageSerializer.GetRequiredLength(message)];

            Assert.That(OscMessageSerializer.TryWrite(message, destination, out var length), Is.True);
            var reader = new OscPacketReader(new System.ReadOnlySpan<byte>(destination, 0, length));
            Assert.That(reader.TryReadNext(out var view), Is.True);
            Assert.That(view.TimestampKey, Is.EqualTo(timestamp));
            Assert.That(view.Address.ToArray(), Is.EqualTo(new[] { (byte)'/', (byte)'b', (byte)'u', (byte)'n', (byte)'d', (byte)'l', (byte)'e' }));
            Assert.That(reader.TryReadNext(out _), Is.False);
        }

        [Test]
        public void TryWrite_InvalidOrUnsupportedMessage_ReturnsFalse()
        {
            Assert.That(OscMessageSerializer.TryWrite(new Message("", 1), new byte[32], out _), Is.False);
            Assert.That(OscMessageSerializer.TryWrite(new Message("/unsupported", 1L), new byte[64], out _), Is.False);
            Assert.That(OscMessageSerializer.TryWrite(new Message("/unsupported", null), new byte[64], out _), Is.False);
            Assert.That(OscMessageSerializer.TryWrite(new Message("/value", 1), new byte[1], out _), Is.False);
            Assert.That(OscMessageSerializer.GetRequiredLength(new Message("/unsupported", 1L)), Is.EqualTo(0));
        }
    }
}
