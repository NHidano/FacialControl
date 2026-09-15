using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.OSC
{
    /// <summary>
    /// 受信経路の確保源の切り分け（spec osc-receive-zero-alloc 8.2）。
    /// 実 UDP ゲートで観測した「データグラム 1 個あたり約 1040 byte」が
    /// Socket.Receive（Mono）なのか、受信スレッドの解析（ParseAndClassify）なのかを単体で測る。
    /// </summary>
    public sealed class ReceivePathAllocationProbeTests
    {
        private const int Datagrams = 20;

        private static byte[][] BuildNormalPackets()
        {
            byte[][] addresses =
            {
                Encoding.UTF8.GetBytes("/avatar/parameters/smile"),
                Encoding.UTF8.GetBytes("/avatar/parameters/frown")
            };
            float[] values = { 0.25f, 0.75f };
            byte[] senderAddress = Encoding.UTF8.GetBytes("/_facialcontrol/sender_id");
            byte[] senderUuid = new byte[16];
            for (int i = 0; i < senderUuid.Length; i++) senderUuid[i] = (byte)(i + 1);

            using (var builder = new OscBundleBuilder())
            {
                int count = builder.BuildFrameBundle(1, senderAddress, senderUuid, "1700000000000", addresses, values, 2);
                var packets = new byte[count][];
                for (int i = 0; i < count; i++)
                {
                    OscBundlePacket packet = builder.GetPacket(i);
                    packets[i] = new byte[packet.Length];
                    System.Buffer.BlockCopy(packet.Buffer, 0, packets[i], 0, packet.Length);
                }
                return packets;
            }
        }

        [Test]
        public void ParseAndClassify_RealNormalPacket_DoesNotAllocate()
        {
            byte[] packet = BuildNormalPackets()[0];
            var table = new OscAddressKeyTable.Builder(new Dictionary<string, byte[]>())
                .SetMappings(new[]
                {
                    new OscMapping("/avatar/parameters/smile", "smile", "layer"),
                    new OscMapping("/avatar/parameters/frown", "frown", "layer")
                })
                .Build(1);
            var diagnostics = new OscReceiveDiagnostics();
            var records = new OscResolvedMessage[64];

            int lastCount = -1;
            long allocated = ManagedAllocationProbe.MeasureAllocatedBytes(() =>
            {
                lastCount = OscMessageClassifier.ParseAndClassify(packet, table, records, diagnostics);
            }, Datagrams);

            TestContext.Out.WriteLine("[ReceivePathAllocationProbe] parseAndClassify packetBytes=" + packet.Length +
                                      " records=" + lastCount + " allocatedOver" + Datagrams + "Calls=" + allocated);
            Assert.That(lastCount, Is.GreaterThan(0));
            Assert.That(allocated, Is.EqualTo(0));
        }

        [Test]
        public void SocketReceive_LoopbackDatagrams_ReportsAllocation()
        {
            byte[] packet = BuildNormalPackets()[0];
            int port = OscPortResolver.ResolveAvailablePort(39900);
            Assert.That(port, Is.GreaterThan(0));

            using (var receiver = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp))
            using (var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                receiver.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                receiver.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
                receiver.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
                receiver.ReceiveTimeout = 2000;
                sender.Connect(new IPEndPoint(IPAddress.Loopback, port));
                var buffer = new byte[2048];

                // ウォームアップ 1 往復
                sender.Send(packet, 0, packet.Length, SocketFlags.None);
                receiver.Receive(buffer, SocketFlags.None);

                long sendAllocated = ManagedAllocationProbe.MeasureAllocatedBytes(() =>
                {
                    for (int i = 0; i < Datagrams; i++)
                        sender.Send(packet, 0, packet.Length, SocketFlags.None);
                }, 1, 0);

                int received = 0;
                long receiveAllocated = ManagedAllocationProbe.MeasureAllocatedBytes(() =>
                {
                    for (int i = 0; i < Datagrams; i++)
                    {
                        if (receiver.Receive(buffer, SocketFlags.None) > 0) received++;
                    }
                }, 1, 0);

                int receivedOffset = 0;
                sender.Send(packet, 0, packet.Length, SocketFlags.None);
                receiver.Receive(buffer, SocketFlags.None);
                for (int i = 0; i < Datagrams; i++)
                    sender.Send(packet, 0, packet.Length, SocketFlags.None);
                long receiveOffsetAllocated = ManagedAllocationProbe.MeasureAllocatedBytes(() =>
                {
                    for (int i = 0; i < Datagrams; i++)
                    {
                        if (receiver.Receive(buffer, 0, buffer.Length, SocketFlags.None) > 0) receivedOffset++;
                    }
                }, 1, 0);

                int receivedErr = 0;
                for (int i = 0; i < Datagrams; i++)
                    sender.Send(packet, 0, packet.Length, SocketFlags.None);
                long receiveErrAllocated = ManagedAllocationProbe.MeasureAllocatedBytes(() =>
                {
                    for (int i = 0; i < Datagrams; i++)
                    {
                        if (receiver.Receive(buffer, 0, buffer.Length, SocketFlags.None, out SocketError _) > 0) receivedErr++;
                    }
                }, 1, 0);

                int receivedSpan = 0;
                for (int i = 0; i < Datagrams; i++)
                    sender.Send(packet, 0, packet.Length, SocketFlags.None);
                long receiveSpanAllocated = ManagedAllocationProbe.MeasureAllocatedBytes(() =>
                {
                    for (int i = 0; i < Datagrams; i++)
                    {
                        if (receiver.Receive(new System.Span<byte>(buffer), SocketFlags.None) > 0) receivedSpan++;
                    }
                }, 1, 0);

                int polled = 0;
                for (int i = 0; i < Datagrams; i++)
                    sender.Send(packet, 0, packet.Length, SocketFlags.None);
                long pollAllocated = ManagedAllocationProbe.MeasureAllocatedBytes(() =>
                {
                    for (int i = 0; i < Datagrams; i++)
                    {
                        if (receiver.Poll(1000000, SelectMode.SelectRead)) polled++;
                        receiver.Receive(buffer, 0, buffer.Length, SocketFlags.None, out SocketError _);
                    }
                }, 1, 0);

                TestContext.Out.WriteLine("[ReceivePathAllocationProbe] socket datagrams=" + Datagrams +
                                          " sendAllocated=" + sendAllocated +
                                          " receiveAllocated(byte[],SocketFlags)=" + receiveAllocated + " received=" + received +
                                          " receiveAllocated(byte[],int,int,SocketFlags)=" + receiveOffsetAllocated + " received=" + receivedOffset +
                                          " receiveAllocated(...,out SocketError)=" + receiveErrAllocated + " received=" + receivedErr +
                                          " receiveAllocated(Span<byte>)=" + receiveSpanAllocated + " received=" + receivedSpan +
                                          " pollThenReceiveAllocated=" + pollAllocated + " polled=" + polled);
                Assert.That(received, Is.EqualTo(Datagrams));
                Assert.That(receivedOffset, Is.EqualTo(Datagrams));
                Assert.That(receivedErr, Is.EqualTo(Datagrams));
                Assert.That(receivedSpan, Is.EqualTo(Datagrams));
                // 受信ループが使う byte[] オーバーロードは確保ゼロであること（2026-09-15 実測: Span<byte> 版のみ 2080 byte/回）
                Assert.That(sendAllocated, Is.EqualTo(0), "Socket.Send(byte[],int,int,SocketFlags) が確保した");
                Assert.That(receiveOffsetAllocated, Is.EqualTo(0), "Socket.Receive(byte[],int,int,SocketFlags) が確保した");
                Assert.That(receiveErrAllocated, Is.EqualTo(0), "Socket.Receive(...,out SocketError) が確保した");
            }
        }
    }
}
