#if FACIALCONTROL_HAS_OSC_MODULE
using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.InputSources
{
    [TestFixture]
    public class AnalogBlendShapeInputSourceMaskTests
    {
        private GameObject _receiverObject;
        private OscReceiver _receiver;
        private OscDoubleBuffer _buffer;

        [TearDown]
        public void TearDown()
        {
            if (_buffer != null)
            {
                _buffer.Dispose();
                _buffer = null;
            }

            if (_receiverObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_receiverObject);
                _receiverObject = null;
            }

            _receiver = null;
        }

        [Test]
        public void ContributeMask_BindingMapBlendShapeIndexes_MatchesTrueIndexSet()
        {
            var source = new FixedAnalogSource("manual", axisCount: 2);
            var sources = new Dictionary<string, IAnalogInputSource>(StringComparer.Ordinal)
            {
                { source.Id, source },
            };
            var blendShapeNames = new[]
            {
                "BrowInnerUp",
                "EyeBlinkLeft",
                "JawOpen",
                "MouthSmileLeft",
                "CheekPuff",
            };
            var bindings = new[]
            {
                new AnalogBindingEntry("manual", 0, AnalogBindingTargetKind.BlendShape, "JawOpen", AnalogTargetAxis.X),
                new AnalogBindingEntry("manual", 1, AnalogBindingTargetKind.BlendShape, "EyeBlinkLeft", AnalogTargetAxis.X),
                new AnalogBindingEntry("manual", 0, AnalogBindingTargetKind.BlendShape, "JawOpen", AnalogTargetAxis.X),
                new AnalogBindingEntry("manual", 0, AnalogBindingTargetKind.BonePose, "Head", AnalogTargetAxis.Y),
            };

            var inputSource = BuildSource(blendShapeNames, sources, bindings);

            AssertMaskMatchesIndexes(inputSource.ContributeMask, blendShapeNames.Length, 1, 2);
        }

        [Test]
        public void ContributeMask_OscScalarBindingScenario_MatchesBoundBlendShapeIndexes()
        {
            OscReceiver receiver = CreateReceiver();
            using var oscSource = new OscFloatAnalogSource(
                InputSourceId.Parse("osc-jaw"),
                receiver,
                "/avatar/parameters/JawOpen",
                stalenessSeconds: 0f);
            var sources = new Dictionary<string, IAnalogInputSource>(StringComparer.Ordinal)
            {
                { oscSource.Id, oscSource },
            };
            var blendShapeNames = new[]
            {
                "JawOpen",
                "MouthFunnel",
                "EyeBlinkLeft",
                "MouthPucker",
            };
            var bindings = new[]
            {
                new AnalogBindingEntry("osc-jaw", 0, AnalogBindingTargetKind.BlendShape, "JawOpen", AnalogTargetAxis.X),
                new AnalogBindingEntry("osc-jaw", 0, AnalogBindingTargetKind.BlendShape, "MouthPucker", AnalogTargetAxis.X),
            };

            var inputSource = BuildSource(blendShapeNames, sources, bindings);

            AssertMaskMatchesIndexes(inputSource.ContributeMask, blendShapeNames.Length, 0, 3);
        }

        [Test]
        public void ContributeMask_ArKitBindingScenario_MatchesBoundBlendShapeIndexes()
        {
            OscReceiver receiver = CreateReceiver();
            var arkitNames = new[] { "jawOpen", "eyeBlinkLeft", "mouthSmileLeft", "browInnerUp" };
            using var arkitSource = new ArKitOscAnalogSource(
                InputSourceId.Parse("arkit"),
                receiver,
                arkitNames,
                stalenessSeconds: 0f);
            var sources = new Dictionary<string, IAnalogInputSource>(StringComparer.Ordinal)
            {
                { arkitSource.Id, arkitSource },
            };
            var blendShapeNames = new[]
            {
                "JawOpen",
                "EyeBlinkLeft",
                "MouthSmileLeft",
                "BrowInnerUp",
                "NoseSneerLeft",
            };
            var bindings = new[]
            {
                new AnalogBindingEntry("arkit", 0, AnalogBindingTargetKind.BlendShape, "JawOpen", AnalogTargetAxis.X),
                new AnalogBindingEntry("arkit", 2, AnalogBindingTargetKind.BlendShape, "MouthSmileLeft", AnalogTargetAxis.X),
                new AnalogBindingEntry("arkit", 3, AnalogBindingTargetKind.BlendShape, "BrowInnerUp", AnalogTargetAxis.X),
            };

            var inputSource = BuildSource(blendShapeNames, sources, bindings);

            AssertMaskMatchesIndexes(inputSource.ContributeMask, blendShapeNames.Length, 0, 2, 3);
        }

        private static AnalogBlendShapeInputSource BuildSource(
            IReadOnlyList<string> blendShapeNames,
            IReadOnlyDictionary<string, IAnalogInputSource> sources,
            IReadOnlyList<AnalogBindingEntry> bindings)
        {
            return new AnalogBlendShapeInputSource(
                InputSourceId.Parse(AnalogBlendShapeInputSource.ReservedId),
                blendShapeNames.Count,
                blendShapeNames,
                sources,
                bindings);
        }

        private OscReceiver CreateReceiver()
        {
            _receiverObject = new GameObject("AnalogBlendShapeInputSourceMaskTests_OscReceiver");
            _receiver = _receiverObject.AddComponent<OscReceiver>();
            _buffer = new OscDoubleBuffer(0);
            _receiver.Initialize(_buffer, Array.Empty<OscMapping>());
            return _receiver;
        }

        private static void AssertMaskMatchesIndexes(BitArray mask, int blendShapeCount, params int[] expectedTrueIndexes)
        {
            Assert.That(mask, Is.Not.Null,
                "AnalogBlendShapeInputSource は binding map 由来の ContributeMask を公開する必要がある。");
            Assert.That(mask.Length, Is.EqualTo(blendShapeCount),
                "ContributeMask.Length は BlendShapeCount と一致する必要がある。");

            var expected = new bool[blendShapeCount];
            for (int i = 0; i < expectedTrueIndexes.Length; i++)
            {
                expected[expectedTrueIndexes[i]] = true;
            }

            for (int i = 0; i < blendShapeCount; i++)
            {
                Assert.That(mask[i], Is.EqualTo(expected[i]),
                    $"ContributeMask[{i}] は binding map の BlendShape index 集合と一致する必要がある。");
            }
        }

        private sealed class FixedAnalogSource : IAnalogInputSource
        {
            public FixedAnalogSource(string id, int axisCount)
            {
                Id = id;
                AxisCount = axisCount;
            }

            public string Id { get; }
            public bool IsValid => true;
            public int AxisCount { get; }

            public void Tick(float deltaTime) { }

            public bool TryReadScalar(out float value)
            {
                value = 1f;
                return true;
            }

            public bool TryReadVector2(out float x, out float y)
            {
                x = 1f;
                y = 1f;
                return AxisCount >= 2;
            }

            public bool TryReadAxes(Span<float> output)
            {
                int copyLength = output.Length < AxisCount ? output.Length : AxisCount;
                for (int i = 0; i < copyLength; i++)
                {
                    output[i] = 1f;
                }
                return copyLength > 0;
            }
        }
    }
}
#endif
