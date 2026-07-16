using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEngine;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor
{
    public static class RecToTimelineExporter
    {
        private const string DefaultFallbackLayerName = "Expressions";
        private const double MinimumClipDuration = 1d / 60d;

        public static TimelineAsset CreateTimelineAsset(
            IRecordedEventSequence sequence,
            FacialCharacterProfileSO profileAsset)
        {
            if (profileAsset == null)
            {
                throw new ArgumentNullException(nameof(profileAsset));
            }

            return CreateTimelineAsset(
                sequence,
                profileAsset.BuildFallbackProfile(),
                CollectGazeSourceIds(profileAsset.GazeConfigs));
        }

        public static TimelineAsset CreateTimelineAsset(
            IRecordedEventSequence sequence,
            FacialProfile profile,
            IReadOnlyCollection<string> gazeSourceIds = null)
        {
            ValidateSequence(sequence);

            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            PopulateTimeline(timeline, sequence, profile, gazeSourceIds);
            return timeline;
        }

        public static void PopulateTimeline(
            TimelineAsset timeline,
            IRecordedEventSequence sequence,
            FacialProfile profile,
            IReadOnlyCollection<string> gazeSourceIds = null)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            ValidateSequence(sequence);

            List<ExpressionClipInfo> expressionClips = BuildExpressionClips(sequence, profile);
            CreateExpressionTracks(timeline, expressionClips);

            List<AnalogTrackInfo> analogTracks = BuildAnalogTracks(sequence, gazeSourceIds);
            CreateAnalogTracks(timeline, analogTracks, sequence.DurationSeconds);
        }

        private static void ValidateSequence(IRecordedEventSequence sequence)
        {
            if (sequence == null)
            {
                throw new ArgumentNullException(nameof(sequence));
            }

            if (sequence.DurationSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence), "Duration must be non-negative.");
            }

            double lastTime = 0d;
            bool first = true;
            for (int i = 0; i < sequence.Count; i++)
            {
                RecordedEvent evt = sequence[i];
                if (!first && evt.TimeSeconds < lastTime)
                {
                    throw new ArgumentException("Recorded events must be sorted by non-decreasing time.", nameof(sequence));
                }

                if (evt.TimeSeconds > sequence.DurationSeconds)
                {
                    throw new ArgumentException("Recorded event time exceeds the declared duration.", nameof(sequence));
                }

                if (evt.Kind == RecordedEventKind.AnalogValue && evt.AxisCount <= 0)
                {
                    throw new ArgumentException("Analog events must have at least one axis.", nameof(sequence));
                }

                lastTime = evt.TimeSeconds;
                first = false;
            }
        }

        private static List<ExpressionClipInfo> BuildExpressionClips(IRecordedEventSequence sequence, FacialProfile profile)
        {
            var clips = new List<ExpressionClipInfo>();
            var openClipsByExpression = new Dictionary<string, Stack<OpenExpressionClip>>(StringComparer.Ordinal);
            string fallbackLayerName = ResolveFallbackLayerName(profile);

            for (int i = 0; i < sequence.Count; i++)
            {
                RecordedEvent evt = sequence[i];
                if (evt.Kind == RecordedEventKind.TriggerOn)
                {
                    Expression? expression = profile.FindExpressionById(evt.ExpressionId);
                    string layerName;
                    if (expression.HasValue)
                    {
                        layerName = profile.GetEffectiveLayer(expression.Value);
                    }
                    else
                    {
                        layerName = fallbackLayerName;
                        Debug.LogWarning(
                            $"[RecToTimelineExporter] Missing expression '{evt.ExpressionId}' at {evt.TimeSeconds:0.###}s. The clip is exported to layer '{layerName}'.");
                    }

                    if (!openClipsByExpression.TryGetValue(evt.ExpressionId, out Stack<OpenExpressionClip> openClips))
                    {
                        openClips = new Stack<OpenExpressionClip>();
                        openClipsByExpression.Add(evt.ExpressionId, openClips);
                    }

                    openClips.Push(new OpenExpressionClip(evt.ExpressionId, layerName, evt.TimeSeconds, i));
                    continue;
                }

                if (evt.Kind != RecordedEventKind.TriggerOff)
                {
                    continue;
                }

                if (!openClipsByExpression.TryGetValue(evt.ExpressionId, out Stack<OpenExpressionClip> pendingClips)
                    || pendingClips.Count == 0)
                {
                    continue;
                }

                OpenExpressionClip openClip = pendingClips.Pop();
                clips.Add(new ExpressionClipInfo(
                    openClip.ExpressionId,
                    openClip.LayerName,
                    openClip.StartTime,
                    evt.TimeSeconds,
                    openClip.Sequence));
            }

            foreach (KeyValuePair<string, Stack<OpenExpressionClip>> pair in openClipsByExpression)
            {
                foreach (OpenExpressionClip openClip in pair.Value)
                {
                    clips.Add(new ExpressionClipInfo(
                        openClip.ExpressionId,
                        openClip.LayerName,
                        openClip.StartTime,
                        sequence.DurationSeconds,
                        openClip.Sequence));
                }
            }

            clips.Sort(ExpressionClipInfoComparer.Instance);
            return clips;
        }

        private static void CreateExpressionTracks(TimelineAsset timeline, List<ExpressionClipInfo> clips)
        {
            if (clips == null || clips.Count == 0)
            {
                return;
            }

            var clipsByLayer = new Dictionary<string, List<ExpressionClipInfo>>(StringComparer.Ordinal);
            for (int i = 0; i < clips.Count; i++)
            {
                ExpressionClipInfo clip = clips[i];
                if (!clipsByLayer.TryGetValue(clip.LayerName, out List<ExpressionClipInfo> layerClips))
                {
                    layerClips = new List<ExpressionClipInfo>();
                    clipsByLayer.Add(clip.LayerName, layerClips);
                }

                layerClips.Add(clip);
            }

            foreach (KeyValuePair<string, List<ExpressionClipInfo>> pair in clipsByLayer)
            {
                pair.Value.Sort(ExpressionClipInfoComparer.Instance);

                FacialExpressionTrack rootTrack = timeline.CreateTrack<FacialExpressionTrack>(null, pair.Key);
                var lanes = new List<ExpressionLane>
                {
                    new ExpressionLane(rootTrack),
                };

                for (int i = 0; i < pair.Value.Count; i++)
                {
                    ExpressionClipInfo clip = pair.Value[i];
                    int laneIndex = FindAvailableLane(lanes, clip.StartTime);
                    if (laneIndex < 0)
                    {
                        laneIndex = lanes.Count;
                        lanes.Add(new ExpressionLane(
                            timeline.CreateTrack<FacialExpressionTrack>(rootTrack, $"{rootTrack.name} Lane {laneIndex}")));
                    }

                    TimelineClip timelineClip = lanes[laneIndex].Track.CreateClip<FacialExpressionClip>();
                    timelineClip.start = clip.StartTime;
                    timelineClip.duration = Math.Max(0d, clip.EndTime - clip.StartTime);
                    ((FacialExpressionClip)timelineClip.asset).ExpressionId = clip.ExpressionId;
                    lanes[laneIndex].LastEndTime = clip.EndTime;
                }
            }
        }

        private static int FindAvailableLane(List<ExpressionLane> lanes, double clipStartTime)
        {
            for (int i = 0; i < lanes.Count; i++)
            {
                if (lanes[i].LastEndTime <= clipStartTime)
                {
                    return i;
                }
            }

            return -1;
        }

        private static List<AnalogTrackInfo> BuildAnalogTracks(
            IRecordedEventSequence sequence,
            IReadOnlyCollection<string> gazeSourceIds)
        {
            var analogEventsBySource = new Dictionary<string, List<AnalogEventInfo>>(StringComparer.Ordinal);

            for (int i = 0; i < sequence.Count; i++)
            {
                RecordedEvent evt = sequence[i];
                if (evt.Kind != RecordedEventKind.AnalogValue)
                {
                    continue;
                }

                if (!analogEventsBySource.TryGetValue(evt.SourceId, out List<AnalogEventInfo> sourceEvents))
                {
                    sourceEvents = new List<AnalogEventInfo>();
                    analogEventsBySource.Add(evt.SourceId, sourceEvents);
                }

                sourceEvents.Add(new AnalogEventInfo(evt.TimeSeconds, evt.Axes));
            }

            var tracks = new List<AnalogTrackInfo>(analogEventsBySource.Count);
            foreach (KeyValuePair<string, List<AnalogEventInfo>> pair in analogEventsBySource)
            {
                bool isConfiguredAsGaze = ContainsSourceId(gazeSourceIds, pair.Key);
                int maxAxisCount = 0;
                bool hasNonGazeAxisCount = false;
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    int axisCount = pair.Value[i].Axes.Length;
                    maxAxisCount = Math.Max(maxAxisCount, axisCount);
                    hasNonGazeAxisCount |= axisCount != 2;
                }

                FacialValueChannelKind channelKind = FacialValueChannelKind.Analog;
                if (isConfiguredAsGaze && !hasNonGazeAxisCount)
                {
                    channelKind = FacialValueChannelKind.Gaze;
                }
                else if (isConfiguredAsGaze && hasNonGazeAxisCount)
                {
                    Debug.LogWarning(
                        $"[RecToTimelineExporter] Gaze source '{pair.Key}' has non-2D samples. It is exported as Analog instead.");
                }

                tracks.Add(new AnalogTrackInfo(pair.Key, channelKind, maxAxisCount, pair.Value));
            }

            tracks.Sort((left, right) => string.CompareOrdinal(left.SourceId, right.SourceId));
            return tracks;
        }

        private static void CreateAnalogTracks(
            TimelineAsset timeline,
            List<AnalogTrackInfo> tracks,
            double durationSeconds)
        {
            if (tracks == null || tracks.Count == 0)
            {
                return;
            }

            for (int i = 0; i < tracks.Count; i++)
            {
                AnalogTrackInfo trackInfo = tracks[i];
                var track = timeline.CreateTrack<FacialValueTrack>(null, trackInfo.SourceId);
                track.ChannelSubId = trackInfo.SourceId;
                track.ChannelKind = trackInfo.ChannelKind;

                TimelineClip clip = track.CreateClip<FacialValueClip>();
                double clipStart = trackInfo.Events[0].TimeSeconds;
                clip.start = clipStart;
                clip.duration = Math.Max(MinimumClipDuration, durationSeconds - clipStart);

                var axes = new AnimationCurve[trackInfo.AxisCount];
                for (int axisIndex = 0; axisIndex < trackInfo.AxisCount; axisIndex++)
                {
                    var keys = new Keyframe[trackInfo.Events.Count];
                    for (int eventIndex = 0; eventIndex < trackInfo.Events.Count; eventIndex++)
                    {
                        AnalogEventInfo evt = trackInfo.Events[eventIndex];
                        float value = axisIndex < evt.Axes.Length ? evt.Axes[axisIndex] : 0f;
                        keys[eventIndex] = new Keyframe((float)(evt.TimeSeconds - clipStart), value);
                    }

                    axes[axisIndex] = new AnimationCurve(keys);
                }

                ((FacialValueClip)clip.asset).Axes = axes;
            }
        }

        private static bool ContainsSourceId(IReadOnlyCollection<string> sourceIds, string sourceId)
        {
            if (sourceIds == null || string.IsNullOrEmpty(sourceId))
            {
                return false;
            }

            foreach (string candidate in sourceIds)
            {
                if (string.Equals(candidate, sourceId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ResolveFallbackLayerName(FacialProfile profile)
        {
            LayerDefinition? emotionLayer = profile.FindLayerByName("emotion");
            if (emotionLayer.HasValue)
            {
                return emotionLayer.Value.Name;
            }

            ReadOnlySpan<LayerDefinition> layers = profile.Layers.Span;
            return layers.Length > 0 ? layers[0].Name : DefaultFallbackLayerName;
        }

        private static HashSet<string> CollectGazeSourceIds(IReadOnlyList<GazeBindingConfig> gazeConfigs)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (gazeConfigs == null)
            {
                return ids;
            }

            for (int i = 0; i < gazeConfigs.Count; i++)
            {
                GazeBindingConfig config = gazeConfigs[i];
                if (!string.IsNullOrWhiteSpace(config?.sourceIdLeft))
                {
                    ids.Add(config.sourceIdLeft);
                }

                if (!string.IsNullOrWhiteSpace(config?.sourceIdRight))
                {
                    ids.Add(config.sourceIdRight);
                }
            }

            return ids;
        }

        private readonly struct OpenExpressionClip
        {
            public OpenExpressionClip(string expressionId, string layerName, double startTime, int sequence)
            {
                ExpressionId = expressionId;
                LayerName = layerName;
                StartTime = startTime;
                Sequence = sequence;
            }

            public string ExpressionId { get; }

            public string LayerName { get; }

            public double StartTime { get; }

            public int Sequence { get; }
        }

        private readonly struct ExpressionClipInfo
        {
            public ExpressionClipInfo(
                string expressionId,
                string layerName,
                double startTime,
                double endTime,
                int sequence)
            {
                ExpressionId = expressionId;
                LayerName = layerName;
                StartTime = startTime;
                EndTime = Math.Max(startTime, endTime);
                Sequence = sequence;
            }

            public string ExpressionId { get; }

            public string LayerName { get; }

            public double StartTime { get; }

            public double EndTime { get; }

            public int Sequence { get; }
        }

        private sealed class ExpressionClipInfoComparer : IComparer<ExpressionClipInfo>
        {
            public static ExpressionClipInfoComparer Instance { get; } = new ExpressionClipInfoComparer();

            public int Compare(ExpressionClipInfo left, ExpressionClipInfo right)
            {
                int layer = string.CompareOrdinal(left.LayerName, right.LayerName);
                if (layer != 0)
                {
                    return layer;
                }

                int start = left.StartTime.CompareTo(right.StartTime);
                if (start != 0)
                {
                    return start;
                }

                int end = left.EndTime.CompareTo(right.EndTime);
                if (end != 0)
                {
                    return end;
                }

                return left.Sequence.CompareTo(right.Sequence);
            }
        }

        private sealed class ExpressionLane
        {
            public ExpressionLane(TrackAsset track)
            {
                Track = track;
            }

            public TrackAsset Track { get; }

            public double LastEndTime { get; set; }
        }

        private readonly struct AnalogEventInfo
        {
            public AnalogEventInfo(double timeSeconds, float[] axes)
            {
                TimeSeconds = timeSeconds;
                Axes = axes ?? Array.Empty<float>();
            }

            public double TimeSeconds { get; }

            public float[] Axes { get; }
        }

        private readonly struct AnalogTrackInfo
        {
            public AnalogTrackInfo(
                string sourceId,
                FacialValueChannelKind channelKind,
                int axisCount,
                List<AnalogEventInfo> events)
            {
                SourceId = sourceId;
                ChannelKind = channelKind;
                AxisCount = axisCount;
                Events = events;
            }

            public string SourceId { get; }

            public FacialValueChannelKind ChannelKind { get; }

            public int AxisCount { get; }

            public List<AnalogEventInfo> Events { get; }
        }
    }
}
