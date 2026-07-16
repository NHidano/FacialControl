using System;
using Hidano.FacialControl.Rec.Domain.Models;

namespace Hidano.FacialControl.Rec.Domain.Services
{
    public interface IRecEventVisitor
    {
        void VisitTriggerOn(string sourceId, string expressionId);

        void VisitTriggerOff(string sourceId, string expressionId);

        void VisitAnalogSample(string sourceId, ReadOnlySpan<float> axes);
    }

    /// <summary>
    /// Advances a loaded timeline against elapsed playback time and dispatches reached events in recorded order.
    /// </summary>
    public sealed class RecPlaybackScheduler
    {
        private RecTimeline _timeline;
        private int _nextEventIndex;

        public double ElapsedSeconds { get; private set; }

        public bool IsCompleted { get; private set; }

        public void Load(RecTimeline timeline)
        {
            _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
            _nextEventIndex = 0;
            ElapsedSeconds = 0d;
            IsCompleted = timeline.Events.Count == 0 && timeline.DurationSeconds <= 0d;
        }

        /// <summary>
        /// Accumulates delta time, dispatches every reached event in recorded order, and returns whether playback reached the end.
        /// </summary>
        public bool Tick(float deltaTime, IRecEventVisitor visitor)
        {
            if (_timeline == null)
            {
                throw new InvalidOperationException("A timeline must be loaded before ticking playback.");
            }

            if (deltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime), "Delta time must be non-negative.");
            }

            if (visitor == null)
            {
                throw new ArgumentNullException(nameof(visitor));
            }

            if (IsCompleted)
            {
                return true;
            }

            ElapsedSeconds += deltaTime;

            while (_nextEventIndex < _timeline.Events.Count)
            {
                RecEvent evt = _timeline.Events[_nextEventIndex];
                if (evt.TimestampSeconds > ElapsedSeconds)
                {
                    break;
                }

                Dispatch(evt, _nextEventIndex, visitor);
                _nextEventIndex++;
            }

            if (_nextEventIndex >= _timeline.Events.Count && ElapsedSeconds >= _timeline.DurationSeconds)
            {
                IsCompleted = true;
            }

            return IsCompleted;
        }

        public void Reset()
        {
            _timeline = null;
            _nextEventIndex = 0;
            ElapsedSeconds = 0d;
            IsCompleted = false;
        }

        private void Dispatch(in RecEvent evt, int eventIndex, IRecEventVisitor visitor)
        {
            string sourceId = _timeline.SourceIds[evt.SourceIdIndex];
            switch (evt.Kind)
            {
                case RecEventKind.TriggerOn:
                    visitor.VisitTriggerOn(sourceId, _timeline.ExpressionIds[evt.ExpressionIdIndex]);
                    return;
                case RecEventKind.TriggerOff:
                    visitor.VisitTriggerOff(sourceId, _timeline.ExpressionIds[evt.ExpressionIdIndex]);
                    return;
                case RecEventKind.AnalogSample:
                    visitor.VisitAnalogSample(sourceId, _timeline.GetAnalogAxesSpan(eventIndex));
                    return;
                default:
                    throw new InvalidOperationException($"Unsupported timed event kind '{evt.Kind}'.");
            }
        }
    }
}
