using NUnit.Framework;
using Hidano.FacialControl.Adapters.OSC;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.OSC
{
    public sealed class OscDatagramRingTests
    {
        private static OscReceiveOptions Options => new OscReceiveOptions(512, 4, 0);

        [Test]
        public void FullRing_DropsOldestCommittedSlot()
        {
            var diagnostics = new OscReceiveDiagnostics();
            var ring = new OscDatagramRing(Options, diagnostics);
            var drain = new OscDrainBuffer(Options);

            for (byte value = 1; value <= 4; value++)
            {
                Assert.That(ring.TryReserveSlot(out int slot), Is.True);
                ring.GetSlotBytes(slot)[0] = value;
                ring.Commit(slot, 1, 0, 1);
            }

            Assert.That(ring.TryReserveSlot(out int newest), Is.True);
            ring.GetSlotBytes(newest)[0] = 5;
            ring.Commit(newest, 1, 0, 1);

            Assert.That(diagnostics.DroppedDatagramCount, Is.EqualTo(1));
            Assert.That(ring.Drain(drain), Is.EqualTo(4));
            Assert.That(drain.DatagramCount, Is.EqualTo(4));
            Assert.That(ring.PendingDatagramCount, Is.Zero);
        }

        [Test]
        public void Abort_AllowsReservationAgain_AndDrainDoesNotTouchReserved()
        {
            var ring = new OscDatagramRing(Options, new OscReceiveDiagnostics());
            var drain = new OscDrainBuffer(Options);

            Assert.That(ring.TryReserveSlot(out int committed), Is.True);
            ring.GetSlotBytes(committed)[0] = 7;
            ring.Commit(committed, 1, 0, 1);
            Assert.That(ring.TryReserveSlot(out int reserved), Is.True);
            ring.GetSlotBytes(reserved)[0] = 9;

            Assert.That(ring.Drain(drain), Is.EqualTo(1));
            Assert.That(ring.PendingDatagramCount, Is.Zero);
            ring.Abort(reserved);
            Assert.That(ring.TryReserveSlot(out int reused), Is.True);
            ring.Abort(reused);
        }
    }
}
