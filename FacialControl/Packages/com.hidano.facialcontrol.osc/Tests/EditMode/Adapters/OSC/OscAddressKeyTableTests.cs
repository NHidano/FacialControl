using System;
using System.Collections.Generic;
using System.Text;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;

namespace Hidano.FacialControl.Adapters.OSC.Tests
{
    public sealed class OscAddressKeyTableTests
    {
        [Test]
        public void Builder_UsesExactAddressBeforeFallbackAndLastMappingWins()
        {
            var pool = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var mappings = new[]
            {
                new OscMapping("/avatar/parameters/Smile", "Smile", "layer"),
                new OscMapping("/custom/Smile", "Smile", "layer"),
                new OscMapping("/custom/Smile", "Smile", "layer")
            };

            var table = new OscAddressKeyTable.Builder(pool)
                .SetMappings(mappings)
                .Build(7);

            Assert.That(table.TryResolve(Utf8("/custom/Smile"), out var exact), Is.True);
            Assert.That(exact.MappingIndex, Is.EqualTo(2));
            Assert.That(table.TryResolve(Utf8("/ARKit/Smile"), out var fallback), Is.True);
            Assert.That(fallback.MappingIndex, Is.EqualTo(2));
            Assert.That(table.TryResolve(Utf8("/avatar/parameters/Smile"), out var exactPrefix), Is.True);
            Assert.That(exactPrefix.MappingIndex, Is.EqualTo(0));
        }

        [Test]
        public void Builder_SharesUtf8AndCombinesMappingGazeListenerAndControlFlags()
        {
            var pool = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var table = new OscAddressKeyTable.Builder(pool)
                .SetMappings(new[] { new OscMapping("/shared", "shape", "layer") })
                .SetGazeAddresses(new[] { "/shared" })
                .SetListenerAddresses(new[] { "/shared" })
                .Build(11);

            Assert.That(table.TryResolve(Utf8("/shared"), out var resolution), Is.True);
            Assert.That(resolution.MappingIndex, Is.EqualTo(0));
            Assert.That(resolution.GazeRouteSet, Is.EqualTo(0));
            Assert.That(resolution.ListenerSlot, Is.EqualTo(0));
            Assert.That(resolution.Control, Is.EqualTo(OscControlKind.None));

            var controlTable = new OscAddressKeyTable.Builder(pool).Build(12);
            Assert.That(controlTable.TryResolve(Utf8(OscControlAddresses.SenderId), out var sender), Is.True);
            Assert.That(sender.Control, Is.EqualTo(OscControlKind.SenderId));
            Assert.That(ReferenceEquals(pool["/shared"], pool["/shared"]), Is.True);
        }

        [Test]
        public void TryResolve_DoesNotAllocateForKnownOrUnknownAddress()
        {
            var pool = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var table = new OscAddressKeyTable.Builder(pool)
                .SetMappings(new[] { new OscMapping("/known", "known", "layer") })
                .Build(1);
            byte[] known = Utf8("/known");
            byte[] unknown = Utf8("/unknown");

            table.TryResolve(known, out _);
            table.TryResolve(unknown, out _);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                table.TryResolve(known, out _);
                table.TryResolve(unknown, out _);
            }

            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
        }

        private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
    }
}
