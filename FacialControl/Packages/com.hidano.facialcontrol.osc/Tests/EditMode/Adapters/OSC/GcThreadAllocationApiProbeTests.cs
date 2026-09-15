using System;
using System.Threading;
using NUnit.Framework;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.OSC
{
    /// <summary>
    /// 計測器の自己検証（spec osc-receive-zero-alloc 8.1 の M2）。
    /// <see cref="GC.GetAllocatedBytesForCurrentThread"/> が Unity の Mono ランタイム上で
    /// 実際の確保量を返すかを、メインスレッドとワーカースレッドの両方で確認する。
    /// 0 を返すなら、この API を前提にした「差分 0」の assert は空振りしている。
    /// </summary>
    public sealed class GcThreadAllocationApiProbeTests
    {
        private const int AllocationBytes = 64 * 1024;

        [Test]
        public void GetAllocatedBytesForCurrentThread_MainThread_ObservesAllocation()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            byte[] sink = new byte[AllocationBytes];
            long after = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(sink);

            long delta = after - before;
            TestContext.Out.WriteLine(
                "[GcThreadAllocationApiProbe] main before=" + before + " after=" + after + " delta=" + delta);

            Assert.That(delta, Is.GreaterThanOrEqualTo(AllocationBytes),
                "メインスレッドで " + AllocationBytes + " byte 確保したが API の差分が " + delta + " byte。");
        }

        [Test]
        public void GetAllocatedBytesForCurrentThread_WorkerThread_ObservesAllocation()
        {
            long before = -1;
            long after = -1;
            Exception failure = null;

            var thread = new Thread(() =>
            {
                try
                {
                    before = GC.GetAllocatedBytesForCurrentThread();
                    byte[] sink = new byte[AllocationBytes];
                    after = GC.GetAllocatedBytesForCurrentThread();
                    GC.KeepAlive(sink);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            })
            {
                IsBackground = true,
                Name = "GcThreadAllocationApiProbe"
            };
            thread.Start();
            Assert.That(thread.Join(5000), Is.True, "ワーカースレッドが 5 秒以内に終了しなかった。");
            Assert.That(failure, Is.Null, "ワーカースレッドで例外: " + failure);

            long delta = after - before;
            TestContext.Out.WriteLine(
                "[GcThreadAllocationApiProbe] worker before=" + before + " after=" + after + " delta=" + delta);

            Assert.That(delta, Is.GreaterThanOrEqualTo(AllocationBytes),
                "ワーカースレッドで " + AllocationBytes + " byte 確保したが API の差分が " + delta + " byte。");
        }

        [Test]
        public void GetAllocatedBytesForCurrentThread_WorkerThreadHook_ObservedFromSameThreadAfterHook()
        {
            // 受信ループと同じ順序（フックで確保 → 直後に同スレッドで読む）を再現する。
            long before = -1;
            long after = -1;
            Action hook = () =>
            {
                byte[] sink = new byte[1024];
                GC.KeepAlive(sink);
            };

            var thread = new Thread(() =>
            {
                before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 5; i++) hook();
                after = GC.GetAllocatedBytesForCurrentThread();
            })
            {
                IsBackground = true
            };
            thread.Start();
            Assert.That(thread.Join(5000), Is.True);

            long delta = after - before;
            TestContext.Out.WriteLine(
                "[GcThreadAllocationApiProbe] hook before=" + before + " after=" + after + " delta=" + delta);

            Assert.That(delta, Is.GreaterThanOrEqualTo(5 * 1024),
                "フックで 5 KB 確保したが API の差分が " + delta + " byte。");
        }
    }
}
