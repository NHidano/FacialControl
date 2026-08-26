using System;
using System.Collections.Generic;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.InputSources
{
    /// <summary>
    /// <see cref="AnalogBlendShapeInputSource"/> の direction filter / scale 適用契約テスト。
    /// gaze 4 系統 (LookLeft / LookRight / LookUp / LookDown) を 1 軸入力で振り分けるための
    /// <see cref="AnalogBindingDirection"/> および <see cref="AnalogBindingEntry.Scale"/> の挙動を観測する。
    /// </summary>
    [TestFixture]
    public class AnalogBlendShapeInputSourceDirectionTests
    {
        /// <summary>固定値を返すフェイク <see cref="IAnalogInputSource"/>。Vector2 想定。</summary>
        private sealed class FakeVector2Source : IAnalogInputSource
        {
            private readonly float _x;
            private readonly float _y;
            public FakeVector2Source(string id, float x, float y)
            {
                Id = id;
                _x = x;
                _y = y;
            }

            public string Id { get; }
            public bool IsValid => true;
            public int AxisCount => 2;
            public void Tick(float deltaTime) { }

            public bool TryReadScalar(out float value)
            {
                value = _x;
                return true;
            }

            public bool TryReadVector2(out float x, out float y)
            {
                x = _x;
                y = _y;
                return true;
            }

            public bool TryReadAxes(Span<float> output)
            {
                if (output.Length >= 1) output[0] = _x;
                if (output.Length >= 2) output[1] = _y;
                return true;
            }
        }

        private static AnalogBlendShapeInputSource BuildSource(
            FakeVector2Source fakeSource,
            IReadOnlyList<string> blendShapeNames,
            params AnalogBindingEntry[] bindings)
        {
            var sources = new Dictionary<string, IAnalogInputSource>(StringComparer.Ordinal)
            {
                { fakeSource.Id, fakeSource },
            };
            return new AnalogBlendShapeInputSource(
                InputSourceId.Parse(AnalogBlendShapeInputSource.ReservedId),
                blendShapeNames.Count,
                blendShapeNames,
                sources,
                bindings);
        }

        [Test]
        public void Bipolar_BackwardCompatible_RawIsAddedDirectly()
        {
            var fake = new FakeVector2Source("gaze", x: 0.5f, y: 0f);
            var entry = new AnalogBindingEntry(
                "gaze", 0, AnalogBindingTargetKind.BlendShape, "EyeLook", AnalogTargetAxis.X);
            var src = BuildSource(fake, new[] { "EyeLook" }, entry);

            Span<float> output = stackalloc float[1];
            output[0] = 0f;
            bool wrote = src.TryWriteValues(output);

            Assert.IsTrue(wrote);
            Assert.AreEqual(0.5f, output[0], 1e-6f, "Bipolar の場合は raw 値が無加工で加算される (Scale=1, default ctor)。");
        }

        [Test]
        public void Bipolar_NegativeRaw_AddsNegative()
        {
            var fake = new FakeVector2Source("gaze", x: -0.7f, y: 0f);
            var entry = new AnalogBindingEntry(
                "gaze", 0, AnalogBindingTargetKind.BlendShape, "EyeLook", AnalogTargetAxis.X);
            var src = BuildSource(fake, new[] { "EyeLook" }, entry);

            Span<float> output = stackalloc float[1];
            output[0] = 0f;
            src.TryWriteValues(output);

            Assert.AreEqual(-0.7f, output[0], 1e-6f, "Bipolar は符号もそのまま反映する。");
        }

        [Test]
        public void Positive_NegativeRaw_IsIgnored()
        {
            var fake = new FakeVector2Source("gaze", x: -0.5f, y: 0f);
            var entry = new AnalogBindingEntry(
                "gaze", 0, AnalogBindingTargetKind.BlendShape, "LookRight", AnalogTargetAxis.X,
                scale: 1f, direction: AnalogBindingDirection.Positive);
            var src = BuildSource(fake, new[] { "LookRight" }, entry);

            Span<float> output = stackalloc float[1];
            output[0] = 0f;
            src.TryWriteValues(output);

            Assert.AreEqual(0f, output[0], 1e-6f, "Positive direction は raw < 0 を無視して 0 を加算する。");
        }

        [Test]
        public void Positive_PositiveRaw_AppliesScale()
        {
            var fake = new FakeVector2Source("gaze", x: 0.5f, y: 0f);
            var entry = new AnalogBindingEntry(
                "gaze", 0, AnalogBindingTargetKind.BlendShape, "LookRight", AnalogTargetAxis.X,
                scale: 80f, direction: AnalogBindingDirection.Positive);
            var src = BuildSource(fake, new[] { "LookRight" }, entry);

            Span<float> output = stackalloc float[1];
            output[0] = 0f;
            src.TryWriteValues(output);

            Assert.AreEqual(40f, output[0], 1e-5f, "Positive direction は raw>0 のとき raw * scale (= 0.5 * 80 = 40)。");
        }

        [Test]
        public void Negative_NegativeRaw_AppliesAbsoluteScale()
        {
            var fake = new FakeVector2Source("gaze", x: -0.25f, y: 0f);
            var entry = new AnalogBindingEntry(
                "gaze", 0, AnalogBindingTargetKind.BlendShape, "LookLeft", AnalogTargetAxis.X,
                scale: 100f, direction: AnalogBindingDirection.Negative);
            var src = BuildSource(fake, new[] { "LookLeft" }, entry);

            Span<float> output = stackalloc float[1];
            output[0] = 0f;
            src.TryWriteValues(output);

            Assert.AreEqual(25f, output[0], 1e-5f, "Negative direction は raw<0 のとき |raw| * scale (= 0.25 * 100 = 25)。");
        }

        [Test]
        public void Negative_PositiveRaw_IsIgnored()
        {
            var fake = new FakeVector2Source("gaze", x: 0.9f, y: 0f);
            var entry = new AnalogBindingEntry(
                "gaze", 0, AnalogBindingTargetKind.BlendShape, "LookLeft", AnalogTargetAxis.X,
                scale: 100f, direction: AnalogBindingDirection.Negative);
            var src = BuildSource(fake, new[] { "LookLeft" }, entry);

            Span<float> output = stackalloc float[1];
            output[0] = 0f;
            src.TryWriteValues(output);

            Assert.AreEqual(0f, output[0], 1e-6f, "Negative direction は raw>0 を無視して 0 を加算する。");
        }

        [Test]
        public void FourDirections_VectorXPositive_DrivesOnlyLookRightAndUpdatesNothingElse()
        {
            // Gaze 4 系統相当: input.x=+0.6, input.y=0 → LookRight だけが反応すべき。
            var fake = new FakeVector2Source("gaze", x: 0.6f, y: 0f);
            var bsNames = new[] { "LookLeft", "LookRight", "LookUp", "LookDown" };
            var bindings = new[]
            {
                new AnalogBindingEntry("gaze", 0, AnalogBindingTargetKind.BlendShape, "LookRight", AnalogTargetAxis.X, 100f, AnalogBindingDirection.Positive),
                new AnalogBindingEntry("gaze", 0, AnalogBindingTargetKind.BlendShape, "LookLeft", AnalogTargetAxis.X, 100f, AnalogBindingDirection.Negative),
                new AnalogBindingEntry("gaze", 1, AnalogBindingTargetKind.BlendShape, "LookUp", AnalogTargetAxis.X, 100f, AnalogBindingDirection.Positive),
                new AnalogBindingEntry("gaze", 1, AnalogBindingTargetKind.BlendShape, "LookDown", AnalogTargetAxis.X, 100f, AnalogBindingDirection.Negative),
            };
            var src = BuildSource(fake, bsNames, bindings);

            Span<float> output = stackalloc float[bsNames.Length];
            for (int i = 0; i < output.Length; i++) output[i] = 0f;
            src.TryWriteValues(output);

            Assert.AreEqual(0f, output[0], 1e-6f, "LookLeft は raw>0 で反応しない。");
            Assert.AreEqual(60f, output[1], 1e-5f, "LookRight は 0.6 * 100 = 60。");
            Assert.AreEqual(0f, output[2], 1e-6f, "LookUp は input.y=0 で反応しない。");
            Assert.AreEqual(0f, output[3], 1e-6f, "LookDown は input.y=0 で反応しない。");
        }

        [Test]
        public void FourDirections_VectorYNegative_DrivesOnlyLookDown()
        {
            var fake = new FakeVector2Source("gaze", x: 0f, y: -0.4f);
            var bsNames = new[] { "LookLeft", "LookRight", "LookUp", "LookDown" };
            var bindings = new[]
            {
                new AnalogBindingEntry("gaze", 0, AnalogBindingTargetKind.BlendShape, "LookRight", AnalogTargetAxis.X, 100f, AnalogBindingDirection.Positive),
                new AnalogBindingEntry("gaze", 0, AnalogBindingTargetKind.BlendShape, "LookLeft", AnalogTargetAxis.X, 100f, AnalogBindingDirection.Negative),
                new AnalogBindingEntry("gaze", 1, AnalogBindingTargetKind.BlendShape, "LookUp", AnalogTargetAxis.X, 100f, AnalogBindingDirection.Positive),
                new AnalogBindingEntry("gaze", 1, AnalogBindingTargetKind.BlendShape, "LookDown", AnalogTargetAxis.X, 100f, AnalogBindingDirection.Negative),
            };
            var src = BuildSource(fake, bsNames, bindings);

            Span<float> output = stackalloc float[bsNames.Length];
            for (int i = 0; i < output.Length; i++) output[i] = 0f;
            src.TryWriteValues(output);

            Assert.AreEqual(0f, output[0], 1e-6f);
            Assert.AreEqual(0f, output[1], 1e-6f);
            Assert.AreEqual(0f, output[2], 1e-6f, "LookUp は raw<0 で反応しない。");
            Assert.AreEqual(40f, output[3], 1e-5f, "LookDown は |raw| * scale = 0.4 * 100 = 40。");
        }
    }
}
