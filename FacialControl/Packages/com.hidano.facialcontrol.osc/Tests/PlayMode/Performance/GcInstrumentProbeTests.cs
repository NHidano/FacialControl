using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    /// <summary>
    /// 計測器の自己検証（spec osc-receive-zero-alloc 8.1 の M2 代替候補の選定用）。
    /// ワーカースレッド（受信ループと同じ plain Thread）で 64 KB × N 回確保したとき、
    /// 各計測器がそれを検出できるかを 1 回の PlayMode 実行で比較する。
    /// 2026-09-15 Unity 6000.3.19f1 の実測: ProfilerRecorder の GC.Alloc マーカーは受信スレッドを集計しない
    /// （BeginThreadProfiling で登録しても同じ）。GC.GetTotalAllocatedBytes は存在しない。
    /// 検出できたのは Memory カウンタ「GC Allocated In Frame」（全スレッド・フレーム単位）と
    /// Mono ヒープ使用量（GC.GetTotalMemory(false)、ブロック粒度）。
    /// </summary>
    [TestFixture]
    [Explicit("計測器の較正記録。通常スイートでは実行せず、Unity 更新時などに手動で再確認する。")]
    public sealed class GcInstrumentProbeTests
    {
        private const int AllocationBytes = 64 * 1024;
        private const int BaselineFrames = 10;
        private const int PulseFrames = 10;

        private static readonly List<byte[]> s_sinks = new List<byte[]>();

        [UnityTest]
        public IEnumerator WorkerThreadAllocation_WhichInstrumentsObserveIt_ProfilerDisabled()
        {
            var inner = Run(false);
            while (inner.MoveNext()) yield return inner.Current;
        }

        [UnityTest]
        public IEnumerator WorkerThreadAllocation_WhichInstrumentsObserveIt_ProfilerEnabled()
        {
            var inner = Run(true);
            while (inner.MoveNext()) yield return inner.Current;
        }

        /// <summary>
        /// 計測ループ自身の確保を切り分ける: (A) GC.Alloc マーカー recorder の LastValue だけ読む、
        /// (B) 加えて「GC Allocated In Frame」カウンタ recorder の LastValue も読む、(C) 何も読まず GetSample で後読みする。
        /// 各フェーズのメインスレッド GC.Alloc と Time.frameCount の進みを記録する。
        /// </summary>
        [UnityTest]
        public IEnumerator ReadingRecorderLastValue_DoesItAllocateOnMainThread()
        {
            const int Frames = 10;
            var markerA = new long[Frames];
            var markerB = new long[Frames];
            var counterB = new long[Frames];
            var frameDeltaFixed = new int[Frames];
            var fixedWait = new WaitForFixedUpdate();

            using (var marker = ProfilerRecorder.StartNew(
                       ProfilerCategory.Memory, "GC.Alloc", 256,
                       ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            using (var counter = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 256))
            {
                GC.Collect();
                yield return null;
                yield return null;

                // (A) マーカーだけ読む
                for (int i = 0; i < Frames; i++)
                {
                    yield return null;
                    markerA[i] = marker.LastValue;
                }

                // (B) マーカー + カウンタを読む
                for (int i = 0; i < Frames; i++)
                {
                    yield return null;
                    markerB[i] = marker.LastValue;
                    counterB[i] = counter.LastValue;
                }

                // (C) 何も読まない 10 フレーム → 後で GetSample で取り出す
                for (int i = 0; i < Frames; i++)
                {
                    yield return null;
                }
                var samplesC = new StringBuilder();
                int count = marker.Count;
                for (int i = Math.Max(0, count - Frames); i < count; i++)
                {
                    samplesC.Append(marker.GetSample(i).Value).Append(',');
                }
                var counterSamplesC = new StringBuilder();
                int countC = counter.Count;
                for (int i = Math.Max(0, countC - Frames); i < countC; i++)
                {
                    counterSamplesC.Append(counter.GetSample(i).Value).Append(',');
                }

                // (D) WaitForFixedUpdate → null の 1 イテレーションが何フレームか
                for (int i = 0; i < Frames; i++)
                {
                    int before = Time.frameCount;
                    yield return fixedWait;
                    yield return null;
                    frameDeltaFixed[i] = Time.frameCount - before;
                }

                string report =
                    "[GcInstrumentProbe/ReadCost] A(markerOnly) main=" + string.Join(",", markerA) +
                    " | B(marker+counter) main=" + string.Join(",", markerB) + " counter=" + string.Join(",", counterB) +
                    " | C(noRead, GetSample afterwards) main=" + samplesC + " counter=" + counterSamplesC +
                    " | D framesPerFixedIteration=" + string.Join(",", frameDeltaFixed) +
                    " | fixedDeltaTime=" + Time.fixedDeltaTime + " targetFrameRate=" + UnityEngine.Application.targetFrameRate;
                TestContext.Out.WriteLine(report);
                Debug.Log(report);
            }
        }

        private static IEnumerator Run(bool enableProfiler)
        {
            var pulse = new AutoResetEvent(false);
            var done = new AutoResetEvent(false);
            int stop = 0;

            var worker = new Thread(() =>
            {
                while (Volatile.Read(ref stop) == 0)
                {
                    if (!pulse.WaitOne(100)) continue;
                    if (Volatile.Read(ref stop) != 0) break;
                    byte[] sink = new byte[AllocationBytes];
                    lock (s_sinks) s_sinks.Add(sink);
                    done.Set();
                }
            })
            {
                IsBackground = true,
                Name = "GcInstrumentProbe"
            };

            lock (s_sinks) s_sinks.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            bool profilerWasEnabled = UnityEngine.Profiling.Profiler.enabled;
            UnityEngine.Profiling.Profiler.enabled = enableProfiler;
            worker.Start();
            yield return null;

            var perFrame = new StringBuilder();
            using (var allRecorder = ProfilerRecorder.StartNew(
                       ProfilerCategory.Memory, "GC.Alloc", 64, ProfilerRecorderOptions.SumAllSamplesInFrame))
            using (var mainRecorder = ProfilerRecorder.StartNew(
                       ProfilerCategory.Memory, "GC.Alloc", 64,
                       ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            using (var frameCounter = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 64))
            {
                yield return null;
                long m1Baseline = 0, m1MainBaseline = 0, counterBaseline = 0;
                for (int i = 0; i < BaselineFrames; i++)
                {
                    yield return null;
                    m1Baseline += allRecorder.LastValue;
                    m1MainBaseline += mainRecorder.LastValue;
                    counterBaseline += frameCounter.LastValue;
                    perFrame.Append(" b").Append(i).Append(":counter=").Append(frameCounter.LastValue)
                        .Append("/main=").Append(mainRecorder.LastValue).Append("/all=").Append(allRecorder.LastValue);
                }

                long totalMemoryBefore = GC.GetTotalMemory(false);
                long m1Pulse = 0, m1MainPulse = 0, counterPulse = 0;
                for (int i = 0; i < PulseFrames; i++)
                {
                    pulse.Set();
                    Assert.That(done.WaitOne(2000), Is.True, "worker が 2 秒以内に確保を完了しなかった。");
                    yield return null;
                    m1Pulse += allRecorder.LastValue;
                    m1MainPulse += mainRecorder.LastValue;
                    counterPulse += frameCounter.LastValue;
                    perFrame.Append(" p").Append(i).Append(":counter=").Append(frameCounter.LastValue)
                        .Append("/main=").Append(mainRecorder.LastValue).Append("/all=").Append(allRecorder.LastValue);
                }
                long totalMemoryAfter = GC.GetTotalMemory(false);
                UnityEngine.Profiling.Profiler.enabled = profilerWasEnabled;

                Volatile.Write(ref stop, 1);
                pulse.Set();
                worker.Join(2000);

                long injected = (long)AllocationBytes * PulseFrames;
                long counterDelta = counterPulse - counterBaseline;
                string report =
                    "[GcInstrumentProbe] profilerEnabled=" + enableProfiler + " injected=" + injected +
                    " | 'GC Allocated In Frame' valid=" + frameCounter.Valid + " baseline=" + counterBaseline + " pulse=" + counterPulse + " delta=" + counterDelta + " seen=" + (counterDelta >= injected) +
                    " | GC.Alloc allThreads baseline=" + m1Baseline + " pulse=" + m1Pulse + " delta=" + (m1Pulse - m1Baseline) + " seen=" + (m1Pulse - m1Baseline >= injected) +
                    " | GC.Alloc mainOnly baseline=" + m1MainBaseline + " pulse=" + m1MainPulse +
                    " | GC.GetTotalMemory delta=" + (totalMemoryAfter - totalMemoryBefore) + " seen=" + (totalMemoryAfter - totalMemoryBefore >= injected) +
                    " | perFrame:" + perFrame;
                TestContext.Out.WriteLine(report);
                Debug.Log(report);

                lock (s_sinks) s_sinks.Clear();

                Assert.That(counterDelta >= injected || totalMemoryAfter - totalMemoryBefore >= injected, Is.True,
                    "どの計測器もワーカースレッドの確保を検出できなかった。\n" + report);
            }
        }
    }
}
