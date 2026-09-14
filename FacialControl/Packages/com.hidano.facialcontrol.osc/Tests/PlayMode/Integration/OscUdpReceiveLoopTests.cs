using System.Collections;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.OSC;

namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    public sealed class OscUdpReceiveLoopTests
    {
        [UnityTest]
        public IEnumerator Start_ReceivesDatagramDirectlyIntoRing()
        {
            var diagnostics = new OscReceiveDiagnostics();
            var options = new OscReceiveOptions(512, 4, 0);
            var ring = new OscDatagramRing(options, diagnostics);
            var loop = new OscUdpReceiveLoop(ring, diagnostics);
            int started = 0;
            int committed = 0;
            loop.ThreadHooks = new OscReceiveThreadHooks
            {
                OnThreadStarted = () => started++,
                OnDatagramCommitted = () => committed++
            };

            int port = OscPortResolver.ResolveAvailablePort(38000);
            Assert.That(port, Is.GreaterThan(0));
            loop.Start(port, options);
            using (var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                byte[] payload = { 1, 2, 3, 4 };
                sender.SendTo(payload, new IPEndPoint(IPAddress.Loopback, port));
            }

            for (int i = 0; i < 60 && diagnostics.ReceivedDatagramCount == 0; i++)
                yield return null;

            Assert.That(loop.IsRunning, Is.True);
            Assert.That(started, Is.EqualTo(1));
            Assert.That(committed, Is.EqualTo(1));
            Assert.That(diagnostics.ReceivedDatagramCount, Is.EqualTo(1));
            var drain = new OscDrainBuffer(options);
            Assert.That(ring.Drain(drain), Is.EqualTo(1));
            Assert.That(drain.GetDatagram(0).ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));

            loop.Stop();
            Assert.That(loop.IsRunning, Is.False);
            loop.Dispose();
        }

        [UnityTest]
        public IEnumerator Start_IsIdempotent()
        {
            var options = OscReceiveOptions.Default;
            var ring = new OscDatagramRing(options, new OscReceiveDiagnostics());
            using (var loop = new OscUdpReceiveLoop(ring, new OscReceiveDiagnostics()))
            {
                int port = OscPortResolver.ResolveAvailablePort(38100);
                loop.Start(port, options);
                int boundPort = loop.BoundPort;
                loop.Start(port, options);
                Assert.That(loop.BoundPort, Is.EqualTo(boundPort));
                yield return null;
            }
        }
    }
}
