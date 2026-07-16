using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Timeline;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.Tracks;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    public sealed class TimelineBakeServiceTests
    {
        [Test]
        public void Bake_WhenClipBoundariesAreOffGrid_ContainsKeysAtExactEventTimes()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            try
            {
                FacialProfile profile = CreateProfile(transitionDuration: 0.25f);
                FacialExpressionTrack track = CreateExpressionTrack(timeline, start: 0.025d, duration: 0.25d);

                var bake = Editor.TimelineBakeService.Bake(timeline, profile);
                BlendShapeCurve curve = FindCurve(bake, track.name, "Smile");

                Assert.That(ContainsKeyAtTime(curve.Curve, 0.025f), Is.True);
                Assert.That(ContainsKeyAtTime(curve.Curve, 0.275f), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Bake_WithLinearTransition_MatchesLiveTransitionAtArbitrarySampleTimes()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            FacialTimelineBakeAsset bake = null;
            try
            {
                const float transitionDuration = 0.25f;
                FacialProfile profile = CreateProfile(transitionDuration);
                CreateExpressionTrack(timeline, start: 0.025d, duration: 0.50d);

                bake = Editor.TimelineBakeService.Bake(timeline, profile);
                BlendShapeCurve curve = FindCurve(bake, "Expressions", "Smile");

                float[] sampleTimes =
                {
                    0.025f,
                    0.0833333f,
                    0.1500000f,
                    0.2416667f,
                    0.3000000f,
                    0.4416667f,
                    0.5250000f,
                    0.6083333f,
                    0.7083333f,
                    0.7750000f,
                };

                for (int i = 0; i < sampleTimes.Length; i++)
                {
                    float time = sampleTimes[i];
                    float expected = SimulateLiveLinearValue(profile, time, transitionDuration);
                    float actual = curve.Curve.Evaluate(time);
                    Assert.That(actual, Is.EqualTo(expected).Within(0.0001f), $"t={time}");
                }
            }
            finally
            {
                if (bake != null)
                {
                    UnityEngine.Object.DestroyImmediate(bake);
                }

                UnityEngine.Object.DestroyImmediate(timeline);
            }
        }

        private static float SimulateLiveLinearValue(FacialProfile profile, float time, float transitionDuration)
        {
            string[] blendShapeNames = { "Smile" };
            var source = new TestExpressionSource(
                InputSourceId.Parse("timeline:test"),
                blendShapeNames,
                profile,
                maxStackDepth: 4,
                exclusionMode: ExclusionMode.LastWins);

            if (time >= 0.025f)
            {
                source.TriggerOn("smile");
                source.Tick(Mathf.Min(time - 0.025f, transitionDuration));
            }

            if (time >= 0.525f)
            {
                source.TriggerOff("smile");
                source.Tick(Mathf.Min(time - 0.525f, transitionDuration));
            }
            else if (time > 0.275f)
            {
                source.Tick(time - 0.275f);
            }

            Span<float> values = stackalloc float[1];
            return source.TryWriteValues(values) ? values[0] : 0f;
        }

        private static FacialProfile CreateProfile(float transitionDuration)
        {
            return new FacialProfile(
                schemaVersion: "1.0.0",
                layers: new[]
                {
                    new LayerDefinition("Expressions", 0, ExclusionMode.LastWins),
                },
                expressions: new[]
                {
                    new Expression(
                        id: "smile",
                        name: "Smile",
                        layer: "Expressions",
                        transitionDuration: transitionDuration,
                        transitionCurve: TransitionCurve.Linear,
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("Smile", 1f),
                        }),
                });
        }

        private static FacialExpressionTrack CreateExpressionTrack(TimelineAsset timeline, double start, double duration)
        {
            FacialExpressionTrack track = timeline.CreateTrack<FacialExpressionTrack>(null, "Expressions");
            TimelineClip clip = track.CreateClip<FacialExpressionClip>();
            clip.start = start;
            clip.duration = duration;
            ((FacialExpressionClip)clip.asset).ExpressionId = "smile";
            return track;
        }

        private static BlendShapeCurve FindCurve(FacialTimelineBakeAsset bake, string layerName, string blendShapeName)
        {
            Assert.That(bake, Is.Not.Null);
            Assert.That(bake.ExpressionBakes, Is.Not.Null);

            for (int bakeIndex = 0; bakeIndex < bake.ExpressionBakes.Length; bakeIndex++)
            {
                ExpressionSourceBake expressionBake = bake.ExpressionBakes[bakeIndex];
                if (!string.Equals(expressionBake.LayerName, layerName, StringComparison.Ordinal))
                {
                    continue;
                }

                BlendShapeCurve[] curves = expressionBake.Curves ?? Array.Empty<BlendShapeCurve>();
                for (int curveIndex = 0; curveIndex < curves.Length; curveIndex++)
                {
                    if (string.Equals(curves[curveIndex].BlendShapeName, blendShapeName, StringComparison.Ordinal))
                    {
                        return curves[curveIndex];
                    }
                }
            }

            Assert.Fail($"Curve not found. layer={layerName}, blendShape={blendShapeName}");
            return null;
        }

        private static bool ContainsKeyAtTime(AnimationCurve curve, float time)
        {
            Keyframe[] keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                if (Mathf.Abs(keys[i].time - time) <= 0.0001f)
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class TestExpressionSource : ExpressionTriggerInputSourceBase
        {
            public TestExpressionSource(
                InputSourceId id,
                IReadOnlyList<string> blendShapeNames,
                FacialProfile profile,
                int maxStackDepth,
                ExclusionMode exclusionMode)
                : base(
                    id,
                    blendShapeCount: blendShapeNames.Count,
                    maxStackDepth: maxStackDepth,
                    exclusionMode: exclusionMode,
                    blendShapeNames: blendShapeNames,
                    profile: profile)
            {
            }
        }
    }
}
