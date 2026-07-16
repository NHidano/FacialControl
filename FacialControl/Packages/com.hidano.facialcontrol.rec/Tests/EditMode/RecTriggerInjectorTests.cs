using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Rec.Adapters.Playback;
using Hidano.FacialControl.Rec.Domain.Models;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    public class RecTriggerInjectorTests
    {
        [Test]
        public void InjectTriggerOnAndOff_WhenSourceResolved_DrivesOriginalTriggerSource()
        {
            var source = CreateTriggerSource("input:trigger");
            var injector = CreateInjector(
                id => id == source.Id ? source : null,
                () => new[] { source });

            injector.InjectTriggerOn("input:trigger", "smile");
            injector.InjectTriggerOn("input:trigger", "angry");
            injector.InjectTriggerOff("input:trigger", "smile");

            Assert.That(source.ActiveExpressionIds, Is.EqualTo(new[] { "angry" }));
        }

        [Test]
        public void InjectTriggerEvents_WhenSourceMissing_LogsDistinctWarningAndSkips()
        {
            var injector = CreateInjector(
                _ => null,
                () => Array.Empty<TestTriggerSource>());

            LogAssert.Expect(LogType.Warning, "Playback skipped trigger injection because sourceId 'missing:trigger' could not be resolved.");

            injector.InjectTriggerOn("missing:trigger", "smile");
            injector.InjectTriggerOff("missing:trigger", "smile");
        }

        [Test]
        public void EstablishBaseline_AppliesStacksToResolvedSourcesAndClearsUnspecifiedSources()
        {
            var primary = CreateTriggerSource("input:primary");
            var secondary = CreateTriggerSource("input:secondary");
            primary.TriggerOn("smile");
            secondary.TriggerOn("angry");

            var injector = CreateInjector(
                id =>
                {
                    if (id == primary.Id)
                    {
                        return primary;
                    }

                    if (id == secondary.Id)
                    {
                        return secondary;
                    }

                    return null;
                },
                () => new[] { primary, secondary });

            var baseline = new RecBaselineState(
                new[]
                {
                    new RecBaselineState.TriggerEntry("input:primary", new[] { "smile", "angry" }),
                    new RecBaselineState.TriggerEntry("missing:trigger", new[] { "smile" }),
                },
                null);

            LogAssert.Expect(LogType.Warning, "Playback skipped trigger injection because sourceId 'missing:trigger' could not be resolved.");

            injector.EstablishBaseline(baseline);

            Assert.That(primary.ActiveExpressionIds, Is.EqualTo(new[] { "smile", "angry" }));
            Assert.That(primary.ReadCurrentValues(), Is.EqualTo(new[] { 0.75f }));
            Assert.That(secondary.ActiveExpressionIds, Is.Empty);
            Assert.That(secondary.TryWriteValues(new float[1]), Is.False);
        }

        private static RecTriggerInjector CreateInjector(
            Func<string, TestTriggerSource> resolveSource,
            Func<IReadOnlyList<TestTriggerSource>> getAllSources)
        {
            return new RecTriggerInjector(
                sourceId => resolveSource(sourceId),
                () => getAllSources());
        }

        private static TestTriggerSource CreateTriggerSource(string sourceId)
        {
            return new TestTriggerSource(
                sourceId,
                new FacialProfile(
                    "1.0.0",
                    new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) },
                    new[]
                    {
                        new Expression("smile", "Smile", "emotion", blendShapeValues: new[] { new BlendShapeMapping("face", 0.25f) }),
                        new Expression("angry", "Angry", "emotion", blendShapeValues: new[] { new BlendShapeMapping("face", 0.75f) }),
                    }));
        }

        private sealed class TestTriggerSource : ExpressionTriggerInputSourceBase
        {
            public TestTriggerSource(string sourceId, FacialProfile profile)
                : base(
                    InputSourceId.Parse(sourceId),
                    blendShapeCount: 1,
                    maxStackDepth: 4,
                    exclusionMode: ExclusionMode.LastWins,
                    blendShapeNames: new[] { "face" },
                    profile: profile)
            {
            }

            public float[] ReadCurrentValues()
            {
                var values = new float[BlendShapeCount];
                TryWriteValues(values);
                return values;
            }
        }
    }
}
