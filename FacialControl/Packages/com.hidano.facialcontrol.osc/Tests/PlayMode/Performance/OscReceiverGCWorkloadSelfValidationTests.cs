using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Services;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    public sealed class OscReceiverGCWorkloadSelfValidationTests
    {
        private const int MaxPacketSize = OscBundleBuilder.DefaultMaxPacketSize;

        [UnityTest]
        public IEnumerator WorkloadSenderAndMeasurementInstruments_SelfValidate()
        {
            byte[][] floatAddresses = new byte[54][];
            float[] floatValues = new float[54];
            string[] heartbeatNames = ARKitDetector.ARKit52Names;
            for (int i = 0; i < 52; i++)
            {
                string name = heartbeatNames[i];
                floatAddresses[i] = Encoding.UTF8.GetBytes("/avatar/parameters/" + name);
                floatValues[i] = i / 52f;
            }

            floatAddresses[52] = Encoding.UTF8.GetBytes("/avatar/parameters/eyeX");
            floatAddresses[53] = Encoding.UTF8.GetBytes("/avatar/parameters/eyeY");
            floatValues[52] = -0.25f;
            floatValues[53] = 0.5f;

            byte[] senderAddress = Encoding.UTF8.GetBytes("/_facialcontrol/sender_id");
            byte[] senderUuid = new byte[16];
            for (int i = 0; i < senderUuid.Length; i++) senderUuid[i] = (byte)(i + 1);
            byte[] heartbeatAddress = Encoding.UTF8.GetBytes("/_facialcontrol/blendshape_names");
            byte[] presetAddress = Encoding.UTF8.GetBytes("/_facialcontrol/preset");
            string[] gazePairs = { "eye", GazeAdvertisementResolver.VrChatXyFormat };
            byte[] gazeAddress = Encoding.UTF8.GetBytes("/_facialcontrol/gaze");
            byte[][] normalPackets;
            byte[][] heartbeatPackets;

            using (var builder = new OscBundleBuilder(MaxPacketSize))
            {
                LogAssert.Expect(LogType.Warning, "[OscBundleBuilder] OSC bundle exceeded MTU payload 1472 bytes and was split into 2 bundles with the same timestamp.");
                int normalCount = builder.BuildFrameBundle(
                    1UL,
                    senderAddress,
                    senderUuid,
                    "1700000000000",
                    floatAddresses,
                    floatValues,
                    floatAddresses.Length);
                Assert.That(normalCount, Is.EqualTo(2), "定常フレームは MTU 分割された 2 パケットであること");
                AssertPacketSizes(builder, normalCount);
                normalPackets = CopyPackets(builder, normalCount);

                int heartbeatCount = builder.BuildFrameBundle(
                    2UL,
                    senderAddress,
                    senderUuid,
                    "1700000000000",
                    floatAddresses,
                    floatValues,
                    floatAddresses.Length,
                    heartbeatAddress,
                    heartbeatNames,
                    heartbeatNames.Length,
                    presetAddress,
                    AddressPresetEstimator.PresetVrChat,
                    null,
                    gazeAddress,
                    gazePairs,
                    1);
                Assert.That(heartbeatCount, Is.GreaterThanOrEqualTo(2), "heartbeat フレームも有効な MTU パケット列であること");
                AssertPacketSizes(builder, heartbeatCount);
                heartbeatPackets = CopyPackets(builder, builder.PacketCount);
            }

            int port = OscPortResolver.ResolveAvailablePort(39700);
            Assert.That(port, Is.GreaterThan(0));
            var options = new OscReceiveOptions(2048, 32, 0, captureThreadAllocationStats: true);
            var diagnostics = new OscReceiveDiagnostics();
            var ring = new OscDatagramRing(options, diagnostics);
            using (var loop = new OscUdpReceiveLoop(ring, diagnostics))
            using (var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            using (var recorder = ProfilerRecorder.StartNew(
                       ProfilerCategory.Memory,
                       "GC.Alloc",
                       256,
                       ProfilerRecorderOptions.SumAllSamplesInFrame))
            {
                long m3Before = TryGetTotalAllocatedBytes();
                loop.Start(port, options);
                sender.Connect(new IPEndPoint(IPAddress.Loopback, port));

                long expectedPackets = normalPackets.Length + heartbeatPackets.Length;
                SendPackets(sender, normalPackets);
                SendPackets(sender, heartbeatPackets);

                    for (int i = 0; i < 120 && diagnostics.ReceivedDatagramCount < expectedPackets; i++)
                        yield return null;

                Assert.That(diagnostics.ReceivedDatagramCount, Is.EqualTo(expectedPackets), "事前構築パケットが全て受信されること");
                long m3After = TryGetTotalAllocatedBytes();
                TestContext.Out.WriteLine(
                        "[OscReceiverGCWorkloadSelfValidation] normalPackets=" + expectedPackets +
                        ", received=" + diagnostics.ReceivedDatagramCount +
                        ", m1LastValue=" + recorder.LastValue +
                        ", profilerSeesWorkerThread=" + (recorder.LastValue > 0) +
                        ", m2ReceiveThreadAllocatedBytes=" + diagnostics.ReceiveThreadAllocatedBytes +
                        ", m3TotalAllocatedDeltaBytes=" + (m3After - m3Before));
                loop.Stop();
            }

            long positiveControlBytes = 0;
            var positiveDiagnostics = new OscReceiveDiagnostics();
            var positiveRing = new OscDatagramRing(options, positiveDiagnostics);
            using (var positiveLoop = new OscUdpReceiveLoop(positiveRing, positiveDiagnostics))
            using (var positiveSender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            using (var positiveRecorder = ProfilerRecorder.StartNew(
                       ProfilerCategory.Memory,
                       "GC.Alloc",
                       256,
                       ProfilerRecorderOptions.SumAllSamplesInFrame))
            {
                positiveLoop.ThreadHooks = new OscReceiveThreadHooks
                {
                    OnDatagramCommitted = () =>
                    {
                        byte[] calibrationAllocation = new byte[1024];
                        positiveControlBytes += calibrationAllocation.Length;
                    }
                };
                int positivePort = OscPortResolver.ResolveAvailablePort(39800);
                positiveLoop.Start(positivePort, options);
                positiveSender.Connect(new IPEndPoint(IPAddress.Loopback, positivePort));
                yield return null;
                long m2Before = positiveDiagnostics.ReceiveThreadAllocatedBytes;
                byte[] calibrationPacket = { 1, 2, 3, 4 };
                for (int i = 0; i < 5; i++)
                    positiveSender.Send(calibrationPacket, 0, calibrationPacket.Length, SocketFlags.None);

                for (int i = 0; i < 120 && positiveDiagnostics.ReceivedDatagramCount < 5; i++)
                    yield return null;
                yield return null;

                Assert.That(positiveControlBytes, Is.GreaterThan(0), "positive control が受信スレッド上で確実に確保すること");
                long m2After = positiveDiagnostics.ReceiveThreadAllocatedBytes;
                long m2Delta = m2After - m2Before;
                TestContext.Out.WriteLine(
                    "[OscReceiverGCWorkloadSelfValidation] positiveControlBytes=" + positiveControlBytes +
                    ", m1PositiveControlLastValue=" + positiveRecorder.LastValue +
                    ", profilerSeesWorkerThread=" + (positiveRecorder.LastValue > 0) +
                    ", m2Before=" + m2Before +
                    ", m2After=" + m2After +
                    ", m2Delta=" + m2Delta);
                if (m2Delta <= 0)
                    Assert.Inconclusive("M2 (GC.GetAllocatedBytesForCurrentThread) は positive control の受信スレッド確保を観測できませんでした。");
                positiveLoop.Stop();
            }
        }

        private static void AssertPacketSizes(OscBundleBuilder builder, int packetCount)
        {
            for (int i = 0; i < packetCount; i++)
                Assert.That(builder.GetPacket(i).Length, Is.LessThanOrEqualTo(MaxPacketSize), "packet=" + i);
        }

        private static byte[][] CopyPackets(OscBundleBuilder builder, int packetCount)
        {
            var packets = new byte[packetCount][];
            for (int i = 0; i < packetCount; i++)
            {
                OscBundlePacket packet = builder.GetPacket(i);
                packets[i] = new byte[packet.Length];
                Buffer.BlockCopy(packet.Buffer, 0, packets[i], 0, packet.Length);
            }
            return packets;
        }

        private static void SendPackets(Socket sender, byte[][] packets)
        {
            for (int i = 0; i < packets.Length; i++)
                sender.Send(packets[i], 0, packets[i].Length, SocketFlags.None);
        }

        private static long TryGetTotalAllocatedBytes()
        {
            MethodInfo method = typeof(GC).GetMethod(
                "GetTotalAllocatedBytes",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(bool) },
                null);
            return method == null ? -1L : (long)method.Invoke(null, new object[] { true });
        }
    }
}
