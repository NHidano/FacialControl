using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEngine;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor
{
    public static class TimelineBakeService
    {
        public static FacialTimelineBakeAsset Bake(
            TimelineAsset timeline,
            FacialProfile profile,
            float sampleRate = FacialTimelineHashCalculator.DefaultSampleRate)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            if (sampleRate <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "sampleRate must be greater than zero.");
            }

            var harness = new BakeSimulationHarness(profile, sampleRate);
            var expressionBakes = new List<ExpressionSourceBake>();
            foreach (TrackAsset rootTrack in timeline.GetOutputTracks())
            {
                if (rootTrack is FacialExpressionTrack expressionTrack)
                {
                    expressionBakes.Add(harness.BakeExpressionTrack(expressionTrack));
                }
            }

            var bake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
            bake.SourceHashHex = FacialTimelineHashCalculator.ComputeHashHex(timeline, profile, sampleRate);
            bake.SampleRate = sampleRate;
            bake.ExpressionBakes = expressionBakes.ToArray();
            bake.ValueBakes = Array.Empty<ValueChannelBake>();
            bake.StateEvents = Array.Empty<TimelineStateEvent>();
            return bake;
        }

        public static bool IsStale(
            TimelineAsset timeline,
            FacialProfile profile,
            FacialTimelineBakeAsset bake)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            if (bake == null)
            {
                return true;
            }

            float sampleRate = bake.SampleRate > 0f
                ? bake.SampleRate
                : FacialTimelineHashCalculator.DefaultSampleRate;

            string expected = FacialTimelineHashCalculator.ComputeHashHex(timeline, profile, sampleRate);
            return !string.Equals(expected, bake.SourceHashHex, StringComparison.Ordinal);
        }
    }
}
