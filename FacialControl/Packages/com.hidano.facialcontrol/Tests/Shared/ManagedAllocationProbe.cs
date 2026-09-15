using System;
using Unity.Profiling;

namespace Hidano.FacialControl.Tests.Shared
{
    /// <summary>
    /// 全スレッドのマネージド確保量を Unity Memory カウンタ「GC Allocated In Frame」で測るテスト用ヘルパー。
    ///
    /// 背景（2026-09-15 Unity 6000.3.19f1 実測）:
    /// - <c>GC.GetAllocatedBytesForCurrentThread()</c> は Mono 上で全スレッド常に 0 を返す。
    /// - <c>GC.GetTotalAllocatedBytes(bool)</c> は存在しない。
    /// - ProfilerRecorder の <c>GC.Alloc</c> マーカーはメインスレッド以外を集計せず、値は 100 byte 単位に丸められる。
    /// - 「GC Allocated In Frame」カウンタは <c>Profiler.enabled</c> に関係なく、plain <c>Thread</c> 上の確保も
    ///   フレーム単位・byte 精度で計上する。
    /// </summary>
    public static class ManagedAllocationProbe
    {
        public const string CounterName = "GC Allocated In Frame";

        /// <summary>
        /// カウンタの recorder を開始する。PlayMode ではフレームごとに <see cref="ProfilerRecorder.LastValue"/> を読む。
        /// </summary>
        public static ProfilerRecorder Start(int capacity = 64)
        {
            return ProfilerRecorder.StartNew(ProfilerCategory.Memory, CounterName, capacity);
        }

        /// <summary>
        /// 同期コード用（EditMode）。<paramref name="body"/> を <paramref name="warmupIterations"/> 回ウォームアップした後、
        /// <paramref name="iterations"/> 回実行した間に全スレッドで確保された byte 数を返す。
        /// 計測は <see cref="ProfilerRecorder.CurrentValue"/> の差分で行うため、Editor のフレーム境界をまたがない範囲で使う。
        /// </summary>
        public static long MeasureAllocatedBytes(Action body, int iterations = 100, int warmupIterations = 1)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            using (var recorder = Start())
            {
                for (int i = 0; i < warmupIterations; i++) body();
                long before = recorder.CurrentValue;
                for (int i = 0; i < iterations; i++) body();
                return recorder.CurrentValue - before;
            }
        }
    }
}
