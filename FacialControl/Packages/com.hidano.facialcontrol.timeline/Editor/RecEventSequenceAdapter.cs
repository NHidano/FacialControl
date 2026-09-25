using System;
using System.Collections.Generic;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Models;

namespace Hidano.FacialControl.Timeline.Editor
{
    internal sealed class RecEventSequenceAdapter : IRecordedEventSequence
    {
        private readonly RecordedEvent[] _events;

        public RecEventSequenceAdapter(RecTimeline timeline)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            DurationSeconds = timeline.DurationSeconds;
            _events = new RecordedEvent[timeline.Events.Count];
            for (int i = 0; i < timeline.Events.Count; i++)
            {
                _events[i] = ConvertEvent(timeline, i);
            }
        }

        public double DurationSeconds { get; }

        public int Count => _events.Length;

        public RecordedEvent this[int index] => _events[index];

        private static RecordedEvent ConvertEvent(RecTimeline timeline, int index)
        {
            RecEvent evt = timeline.Events[index];
            switch (evt.Kind)
            {
                case RecEventKind.TriggerOn:
                    return new RecordedEvent(
                        evt.TimestampSeconds,
                        RecordedEventKind.TriggerOn,
                        expressionId: timeline.ExpressionIds[evt.ExpressionIdIndex]);
                case RecEventKind.TriggerOff:
                    return new RecordedEvent(
                        evt.TimestampSeconds,
                        RecordedEventKind.TriggerOff,
                        expressionId: timeline.ExpressionIds[evt.ExpressionIdIndex]);
                case RecEventKind.AnalogSample:
                    return new RecordedEvent(
                        evt.TimestampSeconds,
                        RecordedEventKind.AnalogValue,
                        sourceId: timeline.SourceIds[evt.SourceIdIndex],
                        axes: CopyAxes(timeline.GetAnalogAxes(index)));
                default:
                    throw new InvalidOperationException($"Unsupported REC event kind '{evt.Kind}'.");
            }
        }

        private static float[] CopyAxes(IReadOnlyList<float> axes)
        {
            if (axes == null || axes.Count == 0)
            {
                return Array.Empty<float>();
            }

            var copied = new float[axes.Count];
            for (int i = 0; i < axes.Count; i++)
            {
                copied[i] = axes[i];
            }

            return copied;
        }
    }
}
