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

        public RecTimeline(
            RecBaselineState baseline,
            IEnumerable<RecEvent> events,
            IEnumerable<string> sourceIds,
            IEnumerable<string> expressionIds,
            double durationSeconds)
        {
            if (durationSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Duration must be non-negative.");
            }

            Baseline = baseline ?? RecBaselineState.Empty;
            _sourceIds = CopyIds(sourceIds, nameof(sourceIds));
            _expressionIds = CopyIds(expressionIds, nameof(expressionIds));
            _events = CopyAndValidateEvents(events, _sourceIds.Length, _expressionIds.Length, durationSeconds);
            DurationSeconds = durationSeconds;
        }

        public RecBaselineState Baseline { get; }

        public IReadOnlyList<RecEvent> Events => _events;

        public IReadOnlyList<string> SourceIds => _sourceIds;

        public IReadOnlyList<string> ExpressionIds => _expressionIds;

        public double DurationSeconds { get; }

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
    }
}
