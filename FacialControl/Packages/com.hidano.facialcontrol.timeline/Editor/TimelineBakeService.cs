using System;
using System.Collections.Generic;
using Hidano.FacialControl.Timeline.Clips;
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
            var valueBakes = new List<ValueChannelBake>();
            var stateEvents = new List<TimelineStateEvent>();
            foreach (TrackAsset rootTrack in timeline.GetOutputTracks())
            {
                if (rootTrack is FacialExpressionTrack expressionTrack)
                {
                    expressionBakes.Add(harness.BakeExpressionTrack(expressionTrack));
                    stateEvents.AddRange(TimelineStateEventCollector.Collect(expressionTrack));
                }

                if (rootTrack is FacialValueTrack valueTrack)
                {
                    valueBakes.Add(BakeValueTrack(valueTrack, sampleRate));
                }
            }

            var bake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
            bake.SourceHashHex = FacialTimelineHashCalculator.ComputeHashHex(timeline, profile, sampleRate);
            bake.SampleRate = sampleRate;
            bake.ExpressionBakes = expressionBakes.ToArray();
            bake.ValueBakes = valueBakes.ToArray();
            bake.StateEvents = stateEvents.ToArray();
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

        private static ValueChannelBake BakeValueTrack(FacialValueTrack track, float sampleRate)
        {
            if (track == null)
            {
                throw new ArgumentNullException(nameof(track));
            }

            ValueClipSample[] clips = CollectValueClips(track);
            int axisCount = ResolveAxisCount(clips);
            var curveKeys = new List<Keyframe>[axisCount];
            for (int i = 0; i < axisCount; i++)
            {
                curveKeys[i] = new List<Keyframe>();
            }

            if (axisCount > 0)
            {
                double sampleInterval = 1d / sampleRate;
                double lastBoundary = CaptureValueTrackSamples(clips, curveKeys, sampleInterval);
                if (lastBoundary <= 0d && curveKeys.Length > 0)
                {
                    CaptureValueState(clips, curveKeys, axisCount, 0d, 0d);
                }
            }

            var axes = new AnimationCurve[axisCount];
            for (int i = 0; i < axisCount; i++)
            {
                axes[i] = new AnimationCurve(curveKeys[i].ToArray());
            }

            return new ValueChannelBake
            {
                Sub = track.ChannelSubId,
                IsGaze = track.ChannelKind == FacialValueChannelKind.Gaze,
                Axes = axes,
            };
        }

        private static double CaptureValueTrackSamples(
            ValueClipSample[] clips,
            List<Keyframe>[] curveKeys,
            double sampleInterval)
        {
            const double boundaryTolerance = 1e-9d;
            double[] boundaries = CollectBoundaries(clips);
            double currentTime = 0d;
            int axisCount = curveKeys.Length;

            CaptureValueState(clips, curveKeys, axisCount, 0d, 0d);

            for (int boundaryIndex = 0; boundaryIndex < boundaries.Length; boundaryIndex++)
            {
                double boundaryTime = boundaries[boundaryIndex];
                while ((currentTime + sampleInterval) < (boundaryTime - boundaryTolerance))
                {
                    currentTime += sampleInterval;
                    CaptureValueState(clips, curveKeys, axisCount, currentTime, currentTime);
                }

                if (boundaryTime > 0d)
                {
                    double preBoundaryTime = Math.Max(0d, boundaryTime - Math.Min(sampleInterval * 0.25d, 1e-4d));
                    CaptureValueState(clips, curveKeys, axisCount, preBoundaryTime, boundaryTime);
                }

                currentTime = boundaryTime;
                CaptureValueState(clips, curveKeys, axisCount, currentTime, currentTime);
            }

            return boundaries.Length == 0 ? 0d : boundaries[boundaries.Length - 1];
        }

        private static void CaptureValueState(
            ValueClipSample[] clips,
            List<Keyframe>[] curveKeys,
            int axisCount,
            double sampleTime,
            double keyTime)
        {
            ValueClipSample? activeClip = FindActiveClip(clips, sampleTime);
            for (int axisIndex = 0; axisIndex < axisCount; axisIndex++)
            {
                float value = 0f;
                if (activeClip.HasValue)
                {
                    AnimationCurve curve = activeClip.Value.GetAxis(axisIndex);
                    if (curve != null)
                    {
                        float clipTime = (float)(sampleTime - activeClip.Value.StartTime);
                        value = curve.Evaluate(clipTime);
                    }
                }

                AddKey(curveKeys[axisIndex], (float)keyTime, value);
            }
        }

        private static ValueClipSample? FindActiveClip(ValueClipSample[] clips, double timeSeconds)
        {
            for (int i = clips.Length - 1; i >= 0; i--)
            {
                ValueClipSample clip = clips[i];
                if (timeSeconds < clip.StartTime || timeSeconds >= clip.EndTime)
                {
                    continue;
                }

                return clip;
            }

            return null;
        }

        private static double[] CollectBoundaries(ValueClipSample[] clips)
        {
            if (clips.Length == 0)
            {
                return Array.Empty<double>();
            }

            var boundaries = new SortedSet<double>();
            for (int i = 0; i < clips.Length; i++)
            {
                boundaries.Add(clips[i].StartTime);
                boundaries.Add(clips[i].EndTime);
            }

            var ordered = new double[boundaries.Count];
            boundaries.CopyTo(ordered);
            return ordered;
        }

        private static int ResolveAxisCount(ValueClipSample[] clips)
        {
            int axisCount = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                axisCount = Math.Max(axisCount, clips[i].AxisCount);
            }

            return axisCount;
        }

        private static ValueClipSample[] CollectValueClips(FacialValueTrack track)
        {
            var clips = new List<ValueClipSample>();
            foreach (TimelineClip clip in track.GetClips())
            {
                if (!(clip.asset is FacialValueClip valueClip))
                {
                    continue;
                }

                clips.Add(new ValueClipSample(clip.start, clip.end, valueClip.Axes));
            }

            return clips.Count == 0 ? Array.Empty<ValueClipSample>() : clips.ToArray();
        }

        private static void AddKey(List<Keyframe> keys, float time, float value)
        {
            if (keys.Count > 0)
            {
                Keyframe last = keys[keys.Count - 1];
                if (Mathf.Abs(last.time - time) <= 1e-6f && Mathf.Abs(last.value - value) <= 1e-6f)
                {
                    return;
                }
            }

            keys.Add(new Keyframe(time, value));
        }

        private readonly struct ValueClipSample
        {
            private readonly AnimationCurve[] _axes;

            public ValueClipSample(double startTime, double endTime, AnimationCurve[] axes)
            {
                StartTime = startTime;
                EndTime = endTime;
                _axes = axes ?? Array.Empty<AnimationCurve>();
            }

            public double StartTime { get; }

            public double EndTime { get; }

            public int AxisCount => _axes.Length;

            public AnimationCurve GetAxis(int axisIndex)
            {
                return (uint)axisIndex < (uint)_axes.Length ? _axes[axisIndex] : null;
            }
        }
    }
}
