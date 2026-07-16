using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Rec.Domain.Models
{
    /// <summary>
    /// Immutable baseline snapshot captured at recording start.
    /// </summary>
    public sealed class RecBaselineState
    {
        private static readonly TriggerEntry[] EmptyTriggerEntries = Array.Empty<TriggerEntry>();
        private static readonly AnalogEntry[] EmptyAnalogEntries = Array.Empty<AnalogEntry>();

        public static RecBaselineState Empty { get; } = new RecBaselineState(null, null);

        private readonly TriggerEntry[] _triggerEntries;
        private readonly AnalogEntry[] _analogEntries;

        public RecBaselineState(IEnumerable<TriggerEntry> triggerEntries, IEnumerable<AnalogEntry> analogEntries)
        {
            _triggerEntries = CopyTriggers(triggerEntries);
            _analogEntries = CopyAnalogs(analogEntries);
        }

        public IReadOnlyList<TriggerEntry> TriggerEntries => _triggerEntries;

        public IReadOnlyList<AnalogEntry> AnalogEntries => _analogEntries;

        public bool TryGetTriggerStack(string sourceId, out IReadOnlyList<string> expressionIds)
        {
            if (sourceId != null)
            {
                for (int i = 0; i < _triggerEntries.Length; i++)
                {
                    if (string.Equals(_triggerEntries[i].SourceId, sourceId, StringComparison.Ordinal))
                    {
                        expressionIds = _triggerEntries[i].ExpressionIds;
                        return true;
                    }
                }
            }

            expressionIds = Array.Empty<string>();
            return false;
        }

        public bool TryGetAnalogAxes(string sourceId, out IReadOnlyList<float> axes)
        {
            if (sourceId != null)
            {
                for (int i = 0; i < _analogEntries.Length; i++)
                {
                    if (string.Equals(_analogEntries[i].SourceId, sourceId, StringComparison.Ordinal))
                    {
                        axes = _analogEntries[i].Axes;
                        return true;
                    }
                }
            }

            axes = Array.Empty<float>();
            return false;
        }

        public readonly struct TriggerEntry
        {
            private readonly string[] _expressionIds;

            public TriggerEntry(string sourceId, IEnumerable<string> expressionIds)
            {
                if (string.IsNullOrWhiteSpace(sourceId))
                {
                    throw new ArgumentException("Source id is required.", nameof(sourceId));
                }

                SourceId = sourceId;
                _expressionIds = CopyStrings(expressionIds, nameof(expressionIds));
            }

            public string SourceId { get; }

            public IReadOnlyList<string> ExpressionIds => _expressionIds ?? Array.Empty<string>();
        }

        public readonly struct AnalogEntry
        {
            private readonly float[] _axes;

            public AnalogEntry(string sourceId, IEnumerable<float> axes)
            {
                if (string.IsNullOrWhiteSpace(sourceId))
                {
                    throw new ArgumentException("Source id is required.", nameof(sourceId));
                }

                SourceId = sourceId;
                _axes = CopyFloats(axes, nameof(axes));
                if (_axes.Length == 0)
                {
                    throw new ArgumentException("At least one axis value is required.", nameof(axes));
                }
            }

            public string SourceId { get; }

            public IReadOnlyList<float> Axes => _axes ?? Array.Empty<float>();
        }

        private static TriggerEntry[] CopyTriggers(IEnumerable<TriggerEntry> entries)
        {
            if (entries == null)
            {
                return EmptyTriggerEntries;
            }

            var list = new List<TriggerEntry>();
            foreach (TriggerEntry entry in entries)
            {
                list.Add(new TriggerEntry(entry.SourceId, entry.ExpressionIds));
            }

            return list.Count == 0 ? EmptyTriggerEntries : list.ToArray();
        }

        private static AnalogEntry[] CopyAnalogs(IEnumerable<AnalogEntry> entries)
        {
            if (entries == null)
            {
                return EmptyAnalogEntries;
            }

            var list = new List<AnalogEntry>();
            foreach (AnalogEntry entry in entries)
            {
                list.Add(new AnalogEntry(entry.SourceId, entry.Axes));
            }

            return list.Count == 0 ? EmptyAnalogEntries : list.ToArray();
        }

        private static string[] CopyStrings(IEnumerable<string> values, string paramName)
        {
            if (values == null)
            {
                return Array.Empty<string>();
            }

            var list = new List<string>();
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException("Values must be non-empty.", paramName);
                }

                list.Add(value);
            }

            return list.Count == 0 ? Array.Empty<string>() : list.ToArray();
        }

        private static float[] CopyFloats(IEnumerable<float> values, string paramName)
        {
            if (values == null)
            {
                throw new ArgumentNullException(paramName);
            }

            var list = new List<float>();
            foreach (float value in values)
            {
                list.Add(value);
            }

            return list.Count == 0 ? Array.Empty<float>() : list.ToArray();
        }
    }
}
