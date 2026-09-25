using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using NUnit.Framework;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    public class LayerInputSourceAggregatorObserverTests
    {
        private sealed class RecordingObserver : ILayerSourceValueObserver
        {
            public readonly List<Call> Calls = new List<Call>();

            public void OnSourceValuesObserved(
                int layerIdx,
                int sourceIdx,
                InputSourceId sourceId,
                bool isValid,
                ReadOnlySpan<float> preWeightValues)
            {
                var copy = new float[preWeightValues.Length];
                preWeightValues.CopyTo(copy);
                Calls.Add(new Call(layerIdx, sourceIdx, sourceId.Value, isValid, copy));
            }

            public readonly struct Call
            {
                public Call(int layerIdx, int sourceIdx, string sourceId, bool isValid, float[] values)
                {
                    LayerIdx = layerIdx;
                    SourceIdx = sourceIdx;
                    SourceId = sourceId;
                    IsValid = isValid;
                    Values = values;
                }

                public int LayerIdx { get; }
                public int SourceIdx { get; }
                public string SourceId { get; }
                public bool IsValid { get; }
                public float[] Values { get; }
            }
        }

        private sealed class FixedValueSource : IInputSource
        {
            private readonly float[] _values;
            private readonly bool _isValid;

            public FixedValueSource(string id, bool isValid, params float[] values)
            {
                Id = id;
                _isValid = isValid;
                _values = values ?? Array.Empty<float>();
                BlendShapeCount = _values.Length;
                ContributeMask = new BitArray(BlendShapeCount, true);
            }

            public string Id { get; }
            public InputSourceType Type => InputSourceType.ValueProvider;
            public int BlendShapeCount { get; }
            public BitArray ContributeMask { get; }

            public void Tick(float deltaTime) { }

            public bool TryWriteValues(Span<float> output)
            {
                if (!_isValid)
                {
                    return false;
                }

                _values.AsSpan().CopyTo(output);
                return true;
            }
        }

        [Test]
        public void Aggregate_WithObserver_ReportsPreWeightValuesPerSource()
        {
            const int blendShapeCount = 3;
            var profile = BuildProfile(layerCount: 1);
            var source0 = new FixedValueSource("osc", isValid: true, 0.2f, 0.4f, 0.6f);
            var source1 = new FixedValueSource("lipsync", isValid: true, 0.9f, 0.1f, 0.3f);

            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, source0),
                (0, 1, source1),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(registry.LayerCount, registry.MaxSourcesPerLayer);
            weightBuffer.SetWeight(0, 0, 0.25f);
            weightBuffer.SetWeight(0, 1, 0.75f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            var observer = new RecordingObserver();
            aggregator.SetSourceValueObserver(observer);

            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            Assert.That(observer.Calls.Count, Is.EqualTo(2));
            Assert.That(observer.Calls[0].LayerIdx, Is.EqualTo(0));
            Assert.That(observer.Calls[0].SourceIdx, Is.EqualTo(0));
            Assert.That(observer.Calls[0].SourceId, Is.EqualTo("osc"));
            Assert.That(observer.Calls[0].IsValid, Is.True);
            Assert.That(observer.Calls[0].Values, Is.EqualTo(new[] { 0.2f, 0.4f, 0.6f }));

            Assert.That(observer.Calls[1].LayerIdx, Is.EqualTo(0));
            Assert.That(observer.Calls[1].SourceIdx, Is.EqualTo(1));
            Assert.That(observer.Calls[1].SourceId, Is.EqualTo("lipsync"));
            Assert.That(observer.Calls[1].IsValid, Is.True);
            Assert.That(observer.Calls[1].Values, Is.EqualTo(new[] { 0.9f, 0.1f, 0.3f }));
        }

        [Test]
        public void Aggregate_InvalidSource_ObserverSeesFalseAndZeroedScratch()
        {
            const int blendShapeCount = 2;
            var profile = BuildProfile(layerCount: 1);
            var invalid = new FixedValueSource("invalid", isValid: false, 1f, 1f);

            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, invalid),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(registry.LayerCount, registry.MaxSourcesPerLayer);
            weightBuffer.SetWeight(0, 0, 1f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            var observer = new RecordingObserver();
            aggregator.SetSourceValueObserver(observer);

            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            Assert.That(observer.Calls.Count, Is.EqualTo(1));
            Assert.That(observer.Calls[0].SourceId, Is.EqualTo("invalid"));
            Assert.That(observer.Calls[0].IsValid, Is.False);
            Assert.That(observer.Calls[0].Values, Is.EqualTo(new[] { 0f, 0f }));
        }

        [Test]
        public void Aggregate_WithoutObserver_StillAggregatesNormally()
        {
            const int blendShapeCount = 2;
            var profile = BuildProfile(layerCount: 1);
            var source = new FixedValueSource("osc", isValid: true, 0.5f, 1f);

            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, source),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(registry.LayerCount, registry.MaxSourcesPerLayer);
            weightBuffer.SetWeight(0, 0, 0.5f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);

            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            Assert.That(outputPerLayer[0].BlendShapeValues.Span.ToArray(), Is.EqualTo(new[] { 0.25f, 0.5f }));
        }

        private static FacialProfile BuildProfile(int layerCount)
        {
            var layers = new LayerDefinition[layerCount];
            for (int i = 0; i < layerCount; i++)
            {
                layers[i] = new LayerDefinition($"layer{i}", priority: i, ExclusionMode.LastWins);
            }

            return new FacialProfile("1.0", layers: layers);
        }
    }
}
