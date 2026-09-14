using System;
using System.Collections.Generic;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.OSC;

namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.OSC
{
    public sealed class OscPacketReaderTests
    {
        [Test]
        public void TryReadNext_ReadsJapaneseAddressAndSequentialBlobStringArguments()
        {
            var packet = Message("/表情", ",bs", Blob(1, 2, 3), String("hello"));
            var reader = new OscPacketReader(packet);

            Assert.That(reader.TryReadNext(out var message), Is.True);
            Assert.That(message.Address.SequenceEqual(Utf8("/表情")), Is.True);
            Assert.That(message.TypeTags.SequenceEqual(new byte[] { (byte)'b', (byte)'s' }), Is.True);

            var arguments = message.GetArgumentReader();
            Assert.That(arguments.TryReadNext(out var blob), Is.True);
            Assert.That(blob.IsBlob, Is.True);
            Assert.That(blob.Bytes.SequenceEqual(new byte[] { 1, 2, 3 }), Is.True);
            Assert.That(arguments.TryReadNext(out var text), Is.True);
            Assert.That(text.IsString, Is.True);
            Assert.That(text.Bytes.SequenceEqual(Utf8("hello")), Is.True);
            Assert.That(arguments.TryReadNext(out _), Is.False);
            Assert.That(reader.TryReadNext(out _), Is.False);
        }

        [Test]
        public void TryReadNext_ReadsFloatIntAndPayloadlessBoolean()
        {
            var packet = Message("/value", ",fiT", Float(0.5f), Int(7));
            var reader = new OscPacketReader(packet);

            Assert.That(reader.TryReadNext(out var message), Is.True);
            Assert.That(message.TryGetFirstAsFloat(out var first), Is.True);
            Assert.That(first, Is.EqualTo(0.5f));
            var arguments = message.GetArgumentReader();
            Assert.That(arguments.TryReadNext(out var floatArgument), Is.True);
            Assert.That(floatArgument.TryGetFloat(out var floatValue), Is.True);
            Assert.That(floatValue, Is.EqualTo(0.5f));
            Assert.That(arguments.TryReadNext(out var intArgument), Is.True);
            Assert.That(intArgument.TryGetInt32(out var intValue), Is.True);
            Assert.That(intValue, Is.EqualTo(7));
            Assert.That(arguments.TryReadNext(out var boolean), Is.True);
            Assert.That(boolean.Tag, Is.EqualTo((byte)'T'));
            Assert.That(boolean.Bytes.Length, Is.EqualTo(0));
        }

        [Test]
        public void TryReadNext_RejectsMalformedMessageWithoutThrowing()
        {
            var packet = new byte[] { (byte)'/', 0, 0, 0, (byte)',', (byte)'f', 0, 0, 0, 1 };
            var reader = new OscPacketReader(packet);

            Assert.That(reader.TryReadNext(out _), Is.False);
            Assert.That(reader.SkippedElementCount, Is.EqualTo(1));
            Assert.That(reader.LastError, Is.EqualTo(OscPacketError.Truncated));
        }

        [Test]
        public void TryReadNext_DoesNotAllocateWhileScanning()
        {
            var packet = Message("/value", ",f", Float(0.25f));
            var before = GC.GetAllocatedBytesForCurrentThread();
            var reader = new OscPacketReader(packet);
            Assert.That(reader.TryReadNext(out var message), Is.True);
            Assert.That(message.TryGetFirstAsFloat(out _), Is.True);
            var after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.EqualTo(0));
        }

        private static byte[] Message(string address, string tags, params byte[][] arguments)
        {
            var bytes = new List<byte>();
            AddPaddedString(bytes, address);
            AddPaddedString(bytes, tags);
            foreach (var argument in arguments)
            {
                bytes.AddRange(argument);
            }

            return bytes.ToArray();
        }

        private static byte[] Blob(params byte[] value)
        {
            var result = new List<byte>();
            AddInt(result, value.Length);
            result.AddRange(value);
            while ((result.Count & 3) != 0) result.Add(0);
            return result.ToArray();
        }

        private static byte[] String(string value)
        {
            var result = new List<byte>();
            AddPaddedString(result, value);
            return result.ToArray();
        }

        private static byte[] Float(float value)
        {
            var bits = BitConverter.SingleToInt32Bits(value);
            return new[] { (byte)(bits >> 24), (byte)(bits >> 16), (byte)(bits >> 8), (byte)bits };
        }

        private static byte[] Int(int value)
        {
            return new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
        }

        private static void AddPaddedString(List<byte> bytes, string value)
        {
            bytes.AddRange(Utf8(value));
            bytes.Add(0);
            while ((bytes.Count & 3) != 0) bytes.Add(0);
        }

        private static byte[] Utf8(string value)
        {
            return System.Text.Encoding.UTF8.GetBytes(value);
        }

        private static void AddInt(List<byte> bytes, int value)
        {
            bytes.AddRange(Int(value));
        }
    }
}
