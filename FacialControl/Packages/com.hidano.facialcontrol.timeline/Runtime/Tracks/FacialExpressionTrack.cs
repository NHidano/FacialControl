using System.Collections.Generic;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Playables;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tracks
{
    [TrackClipType(typeof(FacialExpressionClip))]
    [TrackBindingType(typeof(FacialTimelineReceiver))]
    [TrackColor(0.78f, 0.36f, 0.28f)]
    public sealed class FacialExpressionTrack : TrackAsset, ILayerable
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            ScriptPlayable<FacialTrackMixerBehaviour> playable =
                ScriptPlayable<FacialTrackMixerBehaviour>.Create(graph, inputCount);
            playable.GetBehaviour().ConfigureExpression(name, CollectStateEvents(this));
            return playable;
        }

        Playable ILayerable.CreateLayerMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return Playable.Null;
        }

        private static TimelineStateEvent[] CollectStateEvents(FacialExpressionTrack rootTrack)
        {
            if (rootTrack == null)
            {
                return System.Array.Empty<TimelineStateEvent>();
            }

            var orderedEvents = new List<OrderedTimelineStateEvent>();
            int sequence = 0;
            AppendTrackEvents(rootTrack, rootTrack.name, orderedEvents, ref sequence);
            if (orderedEvents.Count <= 1)
            {
                return ToStateEvents(orderedEvents);
            }

            orderedEvents.Sort(CompareEvents);
            return ToStateEvents(orderedEvents);
        }

        private static void AppendTrackEvents(
            TrackAsset track,
            string layerName,
            List<OrderedTimelineStateEvent> events,
            ref int sequence)
        {
            if (track == null)
            {
                return;
            }

            foreach (TimelineClip clip in track.GetClips())
            {
                if (!(clip.asset is FacialExpressionClip expressionClip) || string.IsNullOrEmpty(expressionClip.ExpressionId))
                {
                    continue;
                }

                double startTime = clip.start;
                double endTime = clip.end;
                events.Add(new OrderedTimelineStateEvent(
                    new TimelineStateEvent(startTime, TimelineStateEvent.KindOn, expressionClip.ExpressionId, layerName),
                    sequence++));
                events.Add(new OrderedTimelineStateEvent(
                    new TimelineStateEvent(endTime, TimelineStateEvent.KindOff, expressionClip.ExpressionId, layerName),
                    sequence++));
            }

            foreach (TrackAsset childTrack in track.GetChildTracks())
            {
                AppendTrackEvents(childTrack, layerName, events, ref sequence);
            }
        }

        private static int CompareEvents(OrderedTimelineStateEvent left, OrderedTimelineStateEvent right)
        {
            int timeComparison = left.Event.TimeSeconds.CompareTo(right.Event.TimeSeconds);
            if (timeComparison != 0)
            {
                return timeComparison;
            }

            return left.Sequence.CompareTo(right.Sequence);
        }

        private static TimelineStateEvent[] ToStateEvents(List<OrderedTimelineStateEvent> orderedEvents)
        {
            if (orderedEvents == null || orderedEvents.Count == 0)
            {
                return System.Array.Empty<TimelineStateEvent>();
            }

            var events = new TimelineStateEvent[orderedEvents.Count];
            for (int i = 0; i < orderedEvents.Count; i++)
            {
                events[i] = orderedEvents[i].Event;
            }

            return events;
        }

        private readonly struct OrderedTimelineStateEvent
        {
            public OrderedTimelineStateEvent(TimelineStateEvent stateEvent, int sequence)
            {
                Event = stateEvent;
                Sequence = sequence;
            }

            public TimelineStateEvent Event { get; }

            public int Sequence { get; }
        }
    }
}
