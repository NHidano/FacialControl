using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    public class LayerInputSourceAggregatorMaskTests
    {
        [Test]
        public void Aggregate_MultipleSourcesContributeSameIndex_OrsMaskIndexToTrue()
        {
            const int blendShapeCount = 4;
            var source0 = new MaskedValueSource("source0", blendShapeCount, true, 1);
            var source1 = new MaskedValueSource("source1", blendShapeCount, true, 1, 2);

            var outputPerLayer = AggregateSingleLayer(
                blendShapeCount,
                new List<(int, int, IInputSource)>
                {
                    (0, 0, source0),
                    (0, 1, source1),
                });

            BitArray mask = outputPerLayer[0].ContributeMask;
            Assert.That(mask, Is.Not.Null,
                "Aggregator は layer ごとの OR 集約済み ContributeMask を LayerInput に渡す必要がある。");
            Assert.That(mask[1], Is.True,
                "複数 source が同じ index に contribute する場合、layer mask は OR で true になること。");
        }

        [Test]
        public void Aggregate_AllSourcesDoNotContributeIndex_LeavesMaskIndexFalse()
        {
            const int blendShapeCount = 4;
            var source0 = new MaskedValueSource("source0", blendShapeCount, true, 0);
            var source1 = new MaskedValueSource("source1", blendShapeCount, true, 2);

            var outputPerLayer = AggregateSingleLayer(
                blendShapeCount,
                new List<(int, int, IInputSource)>
                {
                    (0, 0, source0),
                    (0, 1, source1),
                });

            BitArray mask = outputPerLayer[0].ContributeMask;
            Assert.That(mask, Is.Not.Null,
                "Aggregator は全 false を表現できる layer mask 参照を渡す必要がある。");
            Assert.That(mask[1], Is.False,
                "どの valid source も contribute しない index は false のまま残ること。");
            Assert.That(mask[3], Is.False,
                "全 source が非 contribute の index は OR 集約後も false のままであること。");
        }

        [Test]
        public void Aggregate_InvalidSourceHasMask_ExcludesMaskFromOrAggregation()
        {
            const int blendShapeCount = 3;
            var valid = new MaskedValueSource("valid", blendShapeCount, true, 0);
            var invalid = new MaskedValueSource("invalid", blendShapeCount, false, 1);

            var outputPerLayer = AggregateSingleLayer(
                blendShapeCount,
                new List<(int, int, IInputSource)>
                {
                    (0, 0, valid),
                    (0, 1, invalid),
                });

            BitArray mask = outputPerLayer[0].ContributeMask;
            Assert.That(mask, Is.Not.Null);
            Assert.That(mask[0], Is.True,
                "valid source の contribute index は layer mask に含まれること。");
            Assert.That(mask[1], Is.False,
                "TryWriteValues が false の source の mask は OR 集約対象外であること。");
        }

        [Test]
        public void Aggregate_NoSourcesRegistered_CreatesAllFalseLayerMask()
        {
            const int blendShapeCount = 3;

            LogAssert.Expect(LogType.Warning,
                new Regex("LayerInputSourceAggregator.*layer 0.*no valid input source"));

            var outputPerLayer = AggregateSingleLayer(
                blendShapeCount,
                new List<(int, int, IInputSource)>());

            BitArray mask = outputPerLayer[0].ContributeMask;
            Assert.That(mask, Is.Not.Null,
                "source 0 本の layer でも全 false の layer mask を渡す必要がある。");
            Assert.That(mask.Length, Is.EqualTo(blendShapeCount));
            for (int i = 0; i < blendShapeCount; i++)
            {
                Assert.That(mask[i], Is.False,
                    $"source 0 本の layer mask は全 false であること (index={i})。");
            }
        }

        private static LayerBlender.LayerInput[] AggregateSingleLayer(
            int blendShapeCount,
            IReadOnlyList<(int layerIdx, int sourceIdx, IInputSource source)> bindings)
        {
            var profile = BuildProfile(layerCount: 1);
            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount,
                Math.Max(1, registry.MaxSourcesPerLayer));

            for (int sourceIdx = 0; sourceIdx < registry.GetSourceCountForLayer(0); sourceIdx++)
            {
                weightBuffer.SetWeight(0, sourceIdx, 1f);
            }

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            var outputPerLayer = new LayerBlender.LayerInput[registry.LayerCount];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);
            return outputPerLayer;
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

        private sealed class MaskedValueSource : IInputSource
        {
            private readonly bool _isValid;

            public MaskedValueSource(string id, int blendShapeCount, bool isValid, params int[] contributeIndexes)
            {
                Id = id;
                BlendShapeCount = blendShapeCount;
                ContributeMask = new BitArray(blendShapeCount, false);
                for (int i = 0; i < contributeIndexes.Length; i++)
                {
                    int index = contributeIndexes[i];
                    if ((uint)index < (uint)blendShapeCount)
                    {
                        ContributeMask[index] = true;
                    }
                }
                _isValid = isValid;
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

                int len = Math.Min(output.Length, BlendShapeCount);
                for (int i = 0; i < len; i++)
                {
                    output[i] = 1f;
                }
                return true;
            }
        }
    }
}
