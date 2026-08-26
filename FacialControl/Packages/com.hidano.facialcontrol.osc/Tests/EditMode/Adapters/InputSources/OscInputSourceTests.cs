using NUnit.Framework;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Tests.Shared;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.InputSources
{
    /// <summary>
    /// <see cref="OscInputSource"/> の EditMode 契約テスト 。
    /// </summary>
    /// <remarks>
    /// 観測完了条件:
    /// <list type="bullet">
    ///   <item>予約 id <c>osc</c> と <see cref="InputSourceType.ValueProvider"/> を持つ。</item>
    ///   <item><see cref="OscDoubleBuffer"/> の読み取りバッファ内容を <c>output</c> にコピーする。</item>
    ///   <item><c>stalenessSeconds = 0</c> のとき staleness 判定は恒常 true。</item>
    ///   <item>受信直後 (同フレーム) は true。</item>
    ///   <item><c>stalenessSeconds = 1.0</c> / time=0→write→time=2.0 で false を返す (Critical 3)。</item>
    /// </list>
    /// </remarks>
    [TestFixture]
    public class OscInputSourceTests
    {
        [Test]
        public void Id_MatchesOscReservedId()
        {
            using var buffer = new OscDoubleBuffer(4);
            var time = new ManualTimeProvider();

            var source = new OscInputSource(buffer, stalenessSeconds: 0f, timeProvider: time);

            Assert.AreEqual(OscInputSource.ReservedId, source.Id);
            Assert.AreEqual("osc", source.Id);
        }

        [Test]
        public void Type_IsValueProviderViaIInputSource()
        {
            using var buffer = new OscDoubleBuffer(4);
            var time = new ManualTimeProvider();

            IInputSource source = new OscInputSource(buffer, stalenessSeconds: 0f, timeProvider: time);

            Assert.AreEqual(InputSourceType.ValueProvider, source.Type);
        }

        [Test]
        public void BlendShapeCount_MatchesBufferSize()
        {
            using var buffer = new OscDoubleBuffer(7);
            var time = new ManualTimeProvider();

            var source = new OscInputSource(buffer, stalenessSeconds: 0f, timeProvider: time);

            Assert.AreEqual(7, source.BlendShapeCount);
        }

        [Test]
        public void Tick_IsNoOp_DoesNotThrow()
        {
            using var buffer = new OscDoubleBuffer(4);
            var time = new ManualTimeProvider();

            var source = new OscInputSource(buffer, stalenessSeconds: 0f, timeProvider: time);

            Assert.DoesNotThrow(() => source.Tick(0.016f));
        }

        [Test]
        public void TryWriteValues_AfterWrite_WritesBufferContents()
        {
            using var buffer = new OscDoubleBuffer(4);
            var time = new ManualTimeProvider();
            var source = new OscInputSource(buffer, stalenessSeconds: 0f, timeProvider: time);

            buffer.Write(0, 0.25f);
            buffer.Write(1, 0.5f);
            buffer.Write(2, 0.75f);
            buffer.Write(3, 1.0f);
            buffer.Swap();

            var output = new float[4];
            bool wrote = source.TryWriteValues(output);

            Assert.IsTrue(wrote);
            Assert.AreEqual(0.25f, output[0], 1e-5f);
            Assert.AreEqual(0.5f, output[1], 1e-5f);
            Assert.AreEqual(0.75f, output[2], 1e-5f);
            Assert.AreEqual(1.0f, output[3], 1e-5f);
        }

        [Test]
        public void TryWriteValues_StalenessZero_AlwaysReturnsTrue()
        {
            using var buffer = new OscDoubleBuffer(2);
            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0.0 };
            var source = new OscInputSource(buffer, stalenessSeconds: 0f, timeProvider: time);

            // 一度も Write がなくても、staleness 無効時は常に true。
            var output = new float[2];
            Assert.IsTrue(source.TryWriteValues(output));

            // 長時間経過しても true のまま。
            time.UnscaledTimeSeconds = 1000.0;
            Assert.IsTrue(source.TryWriteValues(output));
        }

        [Test]
        public void TryWriteValues_ImmediatelyAfterWrite_ReturnsTrue()
        {
            using var buffer = new OscDoubleBuffer(2);
            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0.0 };
            var source = new OscInputSource(buffer, stalenessSeconds: 1.0f, timeProvider: time);

            buffer.Write(0, 0.42f);
            buffer.Swap();

            // 受信直後は経過時間 0 なので有効。
            var output = new float[2];
            Assert.IsTrue(source.TryWriteValues(output));
            Assert.AreEqual(0.42f, output[0], 1e-5f);
        }

        /// <summary>
        /// Critical 3: staleness 判定の決定論シナリオ。
        /// time=0 で write → time=2.0 で閾値 1.0 を超過 → IsValid=false。
        /// </summary>
        [Test]
        public void TryWriteValues_ExceedsStalenessThreshold_ReturnsFalse()
        {
            using var buffer = new OscDoubleBuffer(2);
            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0.0 };
            var source = new OscInputSource(buffer, stalenessSeconds: 1.0f, timeProvider: time);

            buffer.Write(0, 0.5f);
            buffer.Swap();

            // time=0 で一度観測させ _lastDataTime を 0 に固定。
            var output = new float[2];
            Assert.IsTrue(source.TryWriteValues(output));

            // 閾値超過 → false かつ output 非変更。
            output[0] = 99f;
            time.UnscaledTimeSeconds = 2.0;
            bool wrote = source.TryWriteValues(output);

            Assert.IsFalse(wrote);
            Assert.AreEqual(99f, output[0], 1e-5f,
                "false を返した場合は output を変更しないこと (IInputSource 契約)。");
        }

        [Test]
        public void TryWriteValues_WithinStalenessThreshold_ReturnsTrue()
        {
            using var buffer = new OscDoubleBuffer(2);
            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0.0 };
            var source = new OscInputSource(buffer, stalenessSeconds: 1.0f, timeProvider: time);

            buffer.Write(0, 0.5f);
            buffer.Swap();

            var output = new float[2];
            Assert.IsTrue(source.TryWriteValues(output));

            // 0.5 秒経過 (閾値 1.0 以内) → 引き続き true。
            time.UnscaledTimeSeconds = 0.5;
            Assert.IsTrue(source.TryWriteValues(output));
            Assert.AreEqual(0.5f, output[0], 1e-5f);
        }

        [Test]
        public void TryWriteValues_NewWriteResetsStaleness()
        {
            using var buffer = new OscDoubleBuffer(2);
            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0.0 };
            var source = new OscInputSource(buffer, stalenessSeconds: 1.0f, timeProvider: time);

            buffer.Write(0, 0.1f);
            buffer.Swap();

            var output = new float[2];
            Assert.IsTrue(source.TryWriteValues(output));

            // 閾値超過で一度 false に。
            time.UnscaledTimeSeconds = 2.0;
            Assert.IsFalse(source.TryWriteValues(output));

            // 新規受信: WriteTick が進む → _lastDataTime が更新され true に復帰。
            buffer.Write(0, 0.9f);
            buffer.Swap();
            bool wrote = source.TryWriteValues(output);

            Assert.IsTrue(wrote);
            Assert.AreEqual(0.9f, output[0], 1e-5f);
        }

        [Test]
        public void TryWriteValues_OutputShorterThanBuffer_WritesOverlapOnly()
        {
            using var buffer = new OscDoubleBuffer(4);
            var time = new ManualTimeProvider();
            var source = new OscInputSource(buffer, stalenessSeconds: 0f, timeProvider: time);

            buffer.Write(0, 0.1f);
            buffer.Write(1, 0.2f);
            buffer.Write(2, 0.3f);
            buffer.Write(3, 0.4f);
            buffer.Swap();

            var output = new float[2];
            bool wrote = source.TryWriteValues(output);

            Assert.IsTrue(wrote);
            Assert.AreEqual(0.1f, output[0], 1e-5f);
            Assert.AreEqual(0.2f, output[1], 1e-5f);
        }

        [Test]
        public void TryWriteValues_OutputLongerThanBuffer_WritesOverlapOnly()
        {
            using var buffer = new OscDoubleBuffer(2);
            var time = new ManualTimeProvider();
            var source = new OscInputSource(buffer, stalenessSeconds: 0f, timeProvider: time);

            buffer.Write(0, 0.1f);
            buffer.Write(1, 0.2f);
            buffer.Swap();

            var output = new float[4] { 7f, 7f, 7f, 7f };
            bool wrote = source.TryWriteValues(output);

            Assert.IsTrue(wrote);
            Assert.AreEqual(0.1f, output[0], 1e-5f);
            Assert.AreEqual(0.2f, output[1], 1e-5f);
            Assert.AreEqual(7f, output[2], 1e-5f, "残余は呼出側責務で保持 (IInputSource 契約)。");
            Assert.AreEqual(7f, output[3], 1e-5f);
        }
    }
}
