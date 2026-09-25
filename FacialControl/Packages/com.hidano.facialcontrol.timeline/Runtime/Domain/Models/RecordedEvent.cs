using System;

namespace Hidano.FacialControl.Timeline.Domain.Models
{
    public interface IRecordedEventSequence
    {
        double DurationSeconds { get; }

        int Count { get; }

        RecordedEvent this[int index] { get; }
    }

    public enum RecordedEventKind
    {
        TriggerOn,
        TriggerOff,
        AnalogValue,
    }

    public readonly struct RecordedEvent
    {
        private readonly float[] _axes;

        public RecordedEvent(
            double timeSeconds,
            RecordedEventKind kind,
            string expressionId = null,
            string sourceId = null,
            float[] axes = null)
        {
            if (timeSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(timeSeconds), "Time must be non-negative.");
            }

            if ((kind == RecordedEventKind.TriggerOn || kind == RecordedEventKind.TriggerOff)
                && string.IsNullOrWhiteSpace(expressionId))
            {
                throw new ArgumentException("Trigger events require an expression id.", nameof(expressionId));
            }

            if (kind == RecordedEventKind.AnalogValue && string.IsNullOrWhiteSpace(sourceId))
            {
                throw new ArgumentException("Analog events require a source id.", nameof(sourceId));
            }

            if (kind == RecordedEventKind.AnalogValue)
            {
                if (axes == null || axes.Length == 0)
                {
                    throw new ArgumentException("Analog events require at least one axis.", nameof(axes));
                }

                _axes = new float[axes.Length];
                Array.Copy(axes, _axes, axes.Length);
            }
            else
            {
                _axes = Array.Empty<float>();
            }

            TimeSeconds = timeSeconds;
            Kind = kind;
            ExpressionId = expressionId ?? string.Empty;
            SourceId = sourceId ?? string.Empty;
        }

        public double TimeSeconds { get; }

        public RecordedEventKind Kind { get; }

        public string ExpressionId { get; }

        public string SourceId { get; }

        public int AxisCount => _axes?.Length ?? 0;

        public float[] Axes
        {
            get
            {
                if (_axes == null || _axes.Length == 0)
                {
                    return Array.Empty<float>();
                }

                var copy = new float[_axes.Length];
                Array.Copy(_axes, copy, _axes.Length);
                return copy;
            }
        }

        public float GetAxis(int axisIndex)
        {
            return (uint)axisIndex < (uint)AxisCount ? _axes[axisIndex] : 0f;
        }
    }
}
