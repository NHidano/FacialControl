using System;

namespace Hidano.FacialControl.Rec.Domain.Models
{
    /// <summary>
    /// Immutable logical record used by REC timelines and binary serialization.
    /// Axis payload itself is stored out of line and referenced by <see cref="AxisCount"/>.
    /// </summary>
    public readonly struct RecEvent : IEquatable<RecEvent>
    {
        public enum IdDefinitionKind : byte
        {
            None = 0,
            Source = 1,
            Expression = 2,
        }

        private RecEvent(
            RecEventKind kind,
            double timestampSeconds,
            ushort sourceIdIndex,
            ushort expressionIdIndex,
            ushort idIndex,
            IdDefinitionKind idKind,
            byte axisCount,
            double durationSeconds,
            uint eventCount)
        {
            Kind = kind;
            TimestampSeconds = timestampSeconds;
            SourceIdIndex = sourceIdIndex;
            ExpressionIdIndex = expressionIdIndex;
            IdIndex = idIndex;
            DefinedIdKind = idKind;
            AxisCount = axisCount;
            DurationSeconds = durationSeconds;
            EventCount = eventCount;
        }

        public RecEventKind Kind { get; }

        public double TimestampSeconds { get; }

        public ushort SourceIdIndex { get; }

        public ushort ExpressionIdIndex { get; }

        public ushort IdIndex { get; }

        public IdDefinitionKind DefinedIdKind { get; }

        public byte AxisCount { get; }

        public double DurationSeconds { get; }

        public uint EventCount { get; }

        public bool IsTimedEvent =>
            Kind == RecEventKind.TriggerOn
            || Kind == RecEventKind.TriggerOff
            || Kind == RecEventKind.AnalogSample;

        public static RecEvent CreateIdDefine(ushort idIndex, IdDefinitionKind idKind)
        {
            if (idKind != IdDefinitionKind.Source && idKind != IdDefinitionKind.Expression)
            {
                throw new ArgumentOutOfRangeException(nameof(idKind), "IdDefine must specify source or expression.");
            }

            return new RecEvent(RecEventKind.IdDefine, 0d, 0, 0, idIndex, idKind, 0, 0d, 0);
        }

        public static RecEvent CreateTriggerOn(double timestampSeconds, ushort sourceIdIndex, ushort expressionIdIndex)
        {
            ValidateTimestamp(timestampSeconds);
            return new RecEvent(RecEventKind.TriggerOn, timestampSeconds, sourceIdIndex, expressionIdIndex, 0, IdDefinitionKind.None, 0, 0d, 0);
        }

        public static RecEvent CreateTriggerOff(double timestampSeconds, ushort sourceIdIndex, ushort expressionIdIndex)
        {
            ValidateTimestamp(timestampSeconds);
            return new RecEvent(RecEventKind.TriggerOff, timestampSeconds, sourceIdIndex, expressionIdIndex, 0, IdDefinitionKind.None, 0, 0d, 0);
        }

        public static RecEvent CreateAnalogSample(double timestampSeconds, ushort sourceIdIndex, byte axisCount)
        {
            ValidateTimestamp(timestampSeconds);
            ValidateAxisCount(axisCount);
            return new RecEvent(RecEventKind.AnalogSample, timestampSeconds, sourceIdIndex, 0, 0, IdDefinitionKind.None, axisCount, 0d, 0);
        }

        public static RecEvent CreateBaselineTrigger(ushort sourceIdIndex, ushort expressionIdIndex)
        {
            return new RecEvent(RecEventKind.BaselineTrigger, 0d, sourceIdIndex, expressionIdIndex, 0, IdDefinitionKind.None, 0, 0d, 0);
        }

        public static RecEvent CreateBaselineAnalog(ushort sourceIdIndex, byte axisCount)
        {
            ValidateAxisCount(axisCount);
            return new RecEvent(RecEventKind.BaselineAnalog, 0d, sourceIdIndex, 0, 0, IdDefinitionKind.None, axisCount, 0d, 0);
        }

        public static RecEvent CreateFooter(double durationSeconds, uint eventCount)
        {
            if (durationSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Duration must be non-negative.");
            }

            return new RecEvent(RecEventKind.Footer, 0d, 0, 0, 0, IdDefinitionKind.None, 0, durationSeconds, eventCount);
        }

        public bool Equals(RecEvent other)
        {
            return Kind == other.Kind
                && TimestampSeconds.Equals(other.TimestampSeconds)
                && SourceIdIndex == other.SourceIdIndex
                && ExpressionIdIndex == other.ExpressionIdIndex
                && IdIndex == other.IdIndex
                && DefinedIdKind == other.DefinedIdKind
                && AxisCount == other.AxisCount
                && DurationSeconds.Equals(other.DurationSeconds)
                && EventCount == other.EventCount;
        }

        public override bool Equals(object obj)
        {
            return obj is RecEvent other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add((int)Kind);
            hash.Add(TimestampSeconds);
            hash.Add(SourceIdIndex);
            hash.Add(ExpressionIdIndex);
            hash.Add(IdIndex);
            hash.Add((int)DefinedIdKind);
            hash.Add(AxisCount);
            hash.Add(DurationSeconds);
            hash.Add(EventCount);
            return hash.ToHashCode();
        }

        public static bool operator ==(RecEvent left, RecEvent right) => left.Equals(right);
        public static bool operator !=(RecEvent left, RecEvent right) => !left.Equals(right);

        private static void ValidateTimestamp(double timestampSeconds)
        {
            if (timestampSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(timestampSeconds), "Timestamp must be non-negative.");
            }
        }

        private static void ValidateAxisCount(byte axisCount)
        {
            if (axisCount == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(axisCount), "Axis count must be greater than zero.");
            }
        }
    }
}
