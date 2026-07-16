using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Application.UseCases;
using Hidano.FacialControl.Rec.Domain.Interfaces;
using Hidano.FacialControl.Rec.Domain.Models;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    public class PlaybackUseCaseTests
    {
        [Test]
        public void Load_ValidTimelineAndProfile_ReturnsMissingExpressionIdsAndResetsState()
        {
            var triggerPort = new FakeTriggerInjectionPort();
            var analogPort = new FakeAnalogInjectionPort();
            var useCase = new PlaybackUseCase(triggerPort, analogPort);
            RecTimeline timeline = CreateTimelineWithMissingExpression();
            FacialProfile profile = CreateProfileWithoutMissingExpression();

            RecLoadResult result = useCase.Load(timeline, profile);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Timeline, Is.SameAs(timeline));
            Assert.That(result.MissingExpressionIds, Is.EqualTo(new[] { "missing" }));
            Assert.That(useCase.State, Is.EqualTo(RecPlaybackState.Idle));
        }

        [Test]
        public void StartPlayback_WhenLoaded_EstablishesFilteredBaselineAndLogsDistinctMissingIds()
        {
            var triggerPort = new FakeTriggerInjectionPort();
            var analogPort = new FakeAnalogInjectionPort();
            var useCase = new PlaybackUseCase(triggerPort, analogPort);
            useCase.Load(CreateTimelineWithMissingExpression(), CreateProfileWithoutMissingExpression());

            LogAssert.Expect(LogType.Warning, "Playback skipped missing expressionId 'missing'.");

            bool started = useCase.StartPlayback();

            Assert.That(started, Is.True);
            Assert.That(useCase.State, Is.EqualTo(RecPlaybackState.Playing));
            Assert.That(triggerPort.EstablishBaselineCallCount, Is.EqualTo(1));
            Assert.That(analogPort.BeginInjectionCallCount, Is.EqualTo(1));
            Assert.That(triggerPort.Baseline.TryGetTriggerStack("input:trigger", out IReadOnlyList<string> expressionIds), Is.True);
            Assert.That(expressionIds, Is.EqualTo(new[] { "smile" }));
            Assert.That(analogPort.Baseline.TryGetAnalogAxes("input:gaze", out IReadOnlyList<float> axes), Is.True);
            Assert.That(axes, Is.EqualTo(new[] { 0.25f, -0.5f }));
        }

        [Test]
        public void StartPlayback_WhenAlreadyPlaying_LogsWarningAndReturnsFalse()
        {
            var triggerPort = new FakeTriggerInjectionPort();
            var analogPort = new FakeAnalogInjectionPort();
            var useCase = new PlaybackUseCase(triggerPort, analogPort);
            useCase.Load(CreateSimpleTimeline(), CreateFullProfile());
            useCase.StartPlayback();

            LogAssert.Expect(LogType.Warning, "Playback is already active. StartPlayback was ignored.");

            bool started = useCase.StartPlayback();

            Assert.That(started, Is.False);
            Assert.That(triggerPort.EstablishBaselineCallCount, Is.EqualTo(1));
            Assert.That(analogPort.BeginInjectionCallCount, Is.EqualTo(1));
        }

        [Test]
        public void Tick_WhenPlaying_InjectsNonMissingEventsAndCompletesOnce()
        {
            var triggerPort = new FakeTriggerInjectionPort();
            var analogPort = new FakeAnalogInjectionPort();
            var useCase = new PlaybackUseCase(triggerPort, analogPort);
            useCase.Load(CreateTimelineWithMissingExpression(), CreateProfileWithoutMissingExpression());
            useCase.StartPlayback();
            int completedCallCount = 0;
            useCase.Completed += () => completedCallCount++;

            useCase.Tick(0.30f);

            Assert.That(triggerPort.TriggerOnEvents, Is.EqualTo(new[] { ("input:trigger", "smile") }));
            Assert.That(triggerPort.TriggerOffEvents.Count, Is.EqualTo(0));
            Assert.That(analogPort.AnalogSamples.Count, Is.EqualTo(1));
            Assert.That(analogPort.AnalogSamples[0].sourceId, Is.EqualTo("input:gaze"));
            Assert.That(analogPort.AnalogSamples[0].axes, Is.EqualTo(new[] { 0.5f, -0.25f }));
            Assert.That(useCase.State, Is.EqualTo(RecPlaybackState.Completed));
            Assert.That(completedCallCount, Is.EqualTo(1));

            useCase.Tick(0.10f);

            Assert.That(completedCallCount, Is.EqualTo(1));
        }

        [Test]
        public void StopPlayback_WhenCompleted_EndsInjectionAndReturnsToIdle()
        {
            var triggerPort = new FakeTriggerInjectionPort();
            var analogPort = new FakeAnalogInjectionPort();
            var useCase = new PlaybackUseCase(triggerPort, analogPort);
            useCase.Load(CreateSimpleTimeline(), CreateFullProfile());
            useCase.StartPlayback();
            useCase.Tick(0.20f);

            useCase.StopPlayback();

            Assert.That(analogPort.EndInjectionCallCount, Is.EqualTo(1));
            Assert.That(useCase.State, Is.EqualTo(RecPlaybackState.Idle));
        }

        [Test]
        public void StopPlayback_WhenIdle_IsQuietNoOp()
        {
            var triggerPort = new FakeTriggerInjectionPort();
            var analogPort = new FakeAnalogInjectionPort();
            var useCase = new PlaybackUseCase(triggerPort, analogPort);

            useCase.StopPlayback();

            Assert.That(analogPort.EndInjectionCallCount, Is.EqualTo(0));
            Assert.That(useCase.State, Is.EqualTo(RecPlaybackState.Idle));
        }

        private static RecTimeline CreateTimelineWithMissingExpression()
        {
            var baseline = new RecBaselineState(
                new[]
                {
                    new RecBaselineState.TriggerEntry("input:trigger", new[] { "smile", "missing" }),
                },
                new[]
                {
                    new RecBaselineState.AnalogEntry("input:gaze", new[] { 0.25f, -0.5f }),
                });

            return new RecTimeline(
                baseline,
                new[]
                {
                    RecEvent.CreateTriggerOn(0.10d, 0, 0),
                    RecEvent.CreateTriggerOff(0.20d, 0, 1),
                    RecEvent.CreateAnalogSample(0.30d, 1, 2),
                },
                new[] { "input:trigger", "input:gaze" },
                new[] { "smile", "missing" },
                0.30d,
                new IReadOnlyList<float>[]
                {
                    Array.Empty<float>(),
                    Array.Empty<float>(),
                    new float[] { 0.5f, -0.25f },
                });
        }

        private static RecTimeline CreateSimpleTimeline()
        {
            return new RecTimeline(
                RecBaselineState.Empty,
                new[]
                {
                    RecEvent.CreateTriggerOn(0.10d, 0, 0),
                },
                new[] { "input:trigger" },
                new[] { "smile" },
                0.10d,
                new[] { Array.Empty<float>() });
        }

        private static FacialProfile CreateProfileWithoutMissingExpression()
        {
            return new FacialProfile(
                "1.0.0",
                layers: new[] { new LayerDefinition("emotion", 0, ExclusionMode.Blend) },
                expressions: new[]
                {
                    new Expression("smile", "Smile", "emotion"),
                });
        }

        private static FacialProfile CreateFullProfile()
        {
            return new FacialProfile(
                "1.0.0",
                layers: new[] { new LayerDefinition("emotion", 0, ExclusionMode.Blend) },
                expressions: new[]
                {
                    new Expression("smile", "Smile", "emotion"),
                });
        }

        private sealed class FakeTriggerInjectionPort : ITriggerInjectionPort
        {
            public int EstablishBaselineCallCount { get; private set; }

            public RecBaselineState Baseline { get; private set; }

            public List<(string sourceId, string expressionId)> TriggerOnEvents { get; } = new List<(string sourceId, string expressionId)>();

            public List<(string sourceId, string expressionId)> TriggerOffEvents { get; } = new List<(string sourceId, string expressionId)>();

            public void EstablishBaseline(RecBaselineState baseline)
            {
                EstablishBaselineCallCount++;
                Baseline = baseline;
            }

            public void InjectTriggerOn(string sourceId, string expressionId)
            {
                TriggerOnEvents.Add((sourceId, expressionId));
            }

            public void InjectTriggerOff(string sourceId, string expressionId)
            {
                TriggerOffEvents.Add((sourceId, expressionId));
            }
        }

        private sealed class FakeAnalogInjectionPort : IAnalogInjectionPort
        {
            public int BeginInjectionCallCount { get; private set; }

            public int EndInjectionCallCount { get; private set; }

            public RecBaselineState Baseline { get; private set; }

            public List<(string sourceId, float[] axes)> AnalogSamples { get; } = new List<(string sourceId, float[] axes)>();

            public void BeginInjection(RecBaselineState baseline)
            {
                BeginInjectionCallCount++;
                Baseline = baseline;
            }

            public void InjectAnalogSample(string sourceId, ReadOnlySpan<float> axes)
            {
                AnalogSamples.Add((sourceId, axes.ToArray()));
            }

            public void EndInjection()
            {
                EndInjectionCallCount++;
            }
        }
    }
}
