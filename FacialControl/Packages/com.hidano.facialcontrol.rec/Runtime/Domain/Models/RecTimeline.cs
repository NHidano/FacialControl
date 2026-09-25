using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Rec.Domain.Models
{
    /// <summary>
    /// Immutable loaded recording timeline.
    /// </summary>
    public sealed class RecTimeline
    {
        private readonly RecEvent[] _events;
        private readonly string[] _sourceIds;
        private readonly string[] _expressionIds;
        private readonly float[][] _analogAxesByEvent;

        public RecTimeline(
            RecBaselineState baseline,
            IEnumerable<RecEvent> events,
            IEnumerable<string> sourceIds,
            IEnumerable<string> expressionIds,
            double durationSeconds,
            IEnumerable<IReadOnlyList<float>> analogAxesByEvent = null)
        {
            if (durationSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Duration must be non-negative.");
            }

            Baseline = baseline ?? RecBaselineState.Empty;
            _sourceIds = CopyIds(sourceIds, nameof(sourceIds));
            _expressionIds = CopyIds(expressionIds, nameof(expressionIds));
            _events = CopyAndValidateEvents(events, _sourceIds.Length, _expressionIds.Length, durationSeconds);
            _analogAxesByEvent = CopyAndValidateAnalogAxes(_events, analogAxesByEvent, nameof(analogAxesByEvent));
            DurationSeconds = durationSeconds;
        }

        public RecBaselineState Baseline { get; }

        public IReadOnlyList<RecEvent> Events => _events;

        public IReadOnlyList<string> SourceIds => _sourceIds;

        public IReadOnlyList<string> ExpressionIds => _expressionIds;

        public double DurationSeconds { get; }

        public IReadOnlyList<float> GetAnalogAxes(int eventIndex)
        {
            ValidateEventIndex(eventIndex);
            float[] axes = _analogAxesByEvent[eventIndex];
            return axes ?? Array.Empty<float>();
        }

        public ReadOnlySpan<float> GetAnalogAxesSpan(int eventIndex)
        {
            ValidateEventIndex(eventIndex);
            float[] axes = _analogAxesByEvent[eventIndex];
            return axes == null ? ReadOnlySpan<float>.Empty : axes;
        }

        private static string[] CopyIds(IEnumerable<string> ids, string paramName)
        {
            if (ids == null)
            {
                return Array.Empty<string>();
            }

            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in ids)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    throw new ArgumentException("Id values must be non-empty.", paramName);
                }

                if (!seen.Add(id))
                {
                    throw new ArgumentException($"Duplicate id '{id}' is not allowed.", paramName);
                }

                list.Add(id);
            }

            return list.Count == 0 ? Array.Empty<string>() : list.ToArray();
        }

        private static RecEvent[] CopyAndValidateEvents(
            IEnumerable<RecEvent> events,
            int sourceIdCount,
            int expressionIdCount,
            double durationSeconds)
        {
            if (events == null)
            {
                return Array.Empty<RecEvent>();
            }

            var list = new List<RecEvent>();
            double lastTimestamp = 0d;
            bool first = true;

            foreach (RecEvent evt in events)
            {
                if (!evt.IsTimedEvent)
                {
                    throw new ArgumentException("Timeline events must be timed trigger or analog records.", nameof(events));
                }

                if (!first && evt.TimestampSeconds < lastTimestamp)
                {
                    throw new ArgumentException("Timeline events must be sorted by non-decreasing timestamp.", nameof(events));
                }

                ValidateIndexes(evt, sourceIdCount, expressionIdCount, nameof(events));
                lastTimestamp = evt.TimestampSeconds;
                first = false;
                list.Add(evt);
            }

            if (!first && lastTimestamp > durationSeconds)
            {
                throw new ArgumentException("Timeline duration must be greater than or equal to the last event timestamp.", nameof(durationSeconds));
            }

            return list.Count == 0 ? Array.Empty<RecEvent>() : list.ToArray();
        }

        private static float[][] CopyAndValidateAnalogAxes(
            IReadOnlyList<RecEvent> events,
            IEnumerable<IReadOnlyList<float>> analogAxesByEvent,
            string paramName)
        {
            int eventCount = events.Count;
            if (eventCount == 0)
            {
                if (analogAxesByEvent == null)
                {
                    return Array.Empty<float[]>();
                }

                using (IEnumerator<IReadOnlyList<float>> enumerator = analogAxesByEvent.GetEnumerator())
                {
                    if (enumerator.MoveNext())
                    {
                        throw new ArgumentException("Analog axes count must match the event count.", paramName);
                    }
                }

                return Array.Empty<float[]>();
            }

            var copied = new float[eventCount][];
            if (analogAxesByEvent == null)
            {
                for (int i = 0; i < eventCount; i++)
                {
                    copied[i] = events[i].Kind == RecEventKind.AnalogSample
                        ? throw new ArgumentException("Analog events require axis payloads.", paramName)
                        : Array.Empty<float>();
                }

                return copied;
            }

            int index = 0;
            foreach (IReadOnlyList<float> axes in analogAxesByEvent)
            {
                if (index >= eventCount)
                {
                    throw new ArgumentException("Analog axes count must match the event count.", paramName);
                }

                copied[index] = CopyAndValidateAxes(events[index], axes, paramName);
                index++;
            }

            if (index != eventCount)
            {
                throw new ArgumentException("Analog axes count must match the event count.", paramName);
            }

            return copied;
        }

        private static float[] CopyAndValidateAxes(RecEvent evt, IReadOnlyList<float> axes, string paramName)
        {
            if (evt.Kind != RecEventKind.AnalogSample)
            {
                if (axes != null && axes.Count > 0)
                {
                    throw new ArgumentException("Non-analog events must not carry axis payloads.", paramName);
                }

                return Array.Empty<float>();
            }

            if (axes == null)
            {
                throw new ArgumentException("Analog events require axis payloads.", paramName);
            }

            if (axes.Count != evt.AxisCount)
            {
                throw new ArgumentException("Axis payload length must match the event axis count.", paramName);
            }

            var copied = new float[axes.Count];
            for (int i = 0; i < copied.Length; i++)
            {
                copied[i] = axes[i];
            }

            return copied;
        }

        private static void ValidateIndexes(RecEvent evt, int sourceIdCount, int expressionIdCount, string paramName)
        {
            if (evt.Kind == RecEventKind.AnalogSample)
            {
                if (evt.SourceIdIndex >= sourceIdCount)
                {
                    throw new ArgumentException("Analog sample references an unknown source id index.", paramName);
                }

                return;
            }

            if (evt.SourceIdIndex >= sourceIdCount)
            {
                throw new ArgumentException("Trigger event references an unknown source id index.", paramName);
            }

            if (evt.ExpressionIdIndex >= expressionIdCount)
            {
                throw new ArgumentException("Trigger event references an unknown expression id index.", paramName);
            }
        }

        private void ValidateEventIndex(int eventIndex)
        {
            if ((uint)eventIndex >= (uint)_events.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(eventIndex));
            }
        }
    }
}
