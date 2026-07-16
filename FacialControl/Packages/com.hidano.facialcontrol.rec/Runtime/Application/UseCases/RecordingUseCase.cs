using System;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Rec.Domain.Interfaces;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Services;
using UnityEngine;

namespace Hidano.FacialControl.Rec.Application.UseCases
{
    /// <summary>
    /// Coordinates a single recording session against the per-controller input observation bus.
    /// </summary>
    public sealed class RecordingUseCase : IFacialInputObserver, IDisposable
    {
        private readonly IFacialInputObservationBus _observationBus;
        private readonly IRecClock _clock;
        private readonly IRecEventSink _sink;

        private RecIdTable _idTable = new RecIdTable();
        private RecordingState _state;
        private int _eventCount;
        private bool _disposed;

        public RecordingUseCase(
            IFacialInputObservationBus observationBus,
            IRecClock clock,
            IRecEventSink sink)
        {
            _observationBus = observationBus ?? throw new ArgumentNullException(nameof(observationBus));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _state = RecordingState.Idle;
        }

        public RecordingState State => _state;

        public bool IsRecording => _state == RecordingState.Recording;

        public void StartRecording(RecBaselineState baseline)
        {
            ThrowIfDisposed();

            if (IsRecording)
            {
                Debug.LogWarning("Recording is already active. StartRecording was ignored.");
                return;
            }

            _idTable = new RecIdTable();
            _eventCount = 0;
            _clock.Reset();
            _sink.Open(baseline ?? RecBaselineState.Empty);
            _observationBus.Subscribe(this);
            _state = RecordingState.Recording;
        }

        public void StopRecording()
        {
            ThrowIfDisposed();

            if (!IsRecording)
            {
                return;
            }

            double durationSeconds = _clock.ElapsedSeconds;
            int eventCount = _eventCount;

            _state = RecordingState.Idle;
            _observationBus.Unsubscribe(this);
            _idTable = new RecIdTable();
            _eventCount = 0;

            _sink.Complete(durationSeconds, eventCount);
        }

        public void ToggleRecording(RecBaselineState baseline)
        {
            ThrowIfDisposed();

            if (IsRecording)
            {
                StopRecording();
                return;
            }

            StartRecording(baseline);
        }

        public void OnTriggerOn(string sourceId, string expressionId)
        {
            if (!IsRecording)
            {
                return;
            }

            if (!TryResolveTriggerIds(sourceId, expressionId, out ushort sourceIndex, out ushort expressionIndex))
            {
                return;
            }

            AppendEvent(RecEvent.CreateTriggerOn(_clock.ElapsedSeconds, sourceIndex, expressionIndex), ReadOnlySpan<float>.Empty);
        }

        public void OnTriggerOff(string sourceId, string expressionId)
        {
            if (!IsRecording)
            {
                return;
            }

            if (!TryResolveTriggerIds(sourceId, expressionId, out ushort sourceIndex, out ushort expressionIndex))
            {
                return;
            }

            AppendEvent(RecEvent.CreateTriggerOff(_clock.ElapsedSeconds, sourceIndex, expressionIndex), ReadOnlySpan<float>.Empty);
        }

        public void OnAnalogSample(string sourceId, ReadOnlySpan<float> axes)
        {
            if (!IsRecording)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(sourceId))
            {
                Debug.LogWarning("Recording ignored an analog sample because sourceId was null or empty.");
                return;
            }

            if (axes.Length <= 0 || axes.Length > byte.MaxValue)
            {
                Debug.LogWarning($"Recording ignored analog sample '{sourceId}' because axis count {axes.Length} was invalid.");
                return;
            }

            ushort sourceIndex = EnsureSourceIdDefined(sourceId);
            AppendEvent(RecEvent.CreateAnalogSample(_clock.ElapsedSeconds, sourceIndex, checked((byte)axes.Length)), axes);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (IsRecording)
            {
                _state = RecordingState.Idle;
                _observationBus.Unsubscribe(this);
                _idTable = new RecIdTable();
                _eventCount = 0;
            }

            _disposed = true;
        }

        private bool TryResolveTriggerIds(string sourceId, string expressionId, out ushort sourceIndex, out ushort expressionIndex)
        {
            sourceIndex = 0;
            expressionIndex = 0;

            if (string.IsNullOrWhiteSpace(sourceId))
            {
                Debug.LogWarning("Recording ignored a trigger event because sourceId was null or empty.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(expressionId))
            {
                Debug.LogWarning("Recording ignored a trigger event because expressionId was null or empty.");
                return false;
            }

            sourceIndex = EnsureSourceIdDefined(sourceId);
            expressionIndex = EnsureExpressionIdDefined(expressionId);
            return true;
        }

        private ushort EnsureSourceIdDefined(string sourceId)
        {
            bool existed = _idTable.TryGetSourceIndex(sourceId, out ushort index);
            index = _idTable.GetOrAddSourceId(sourceId);
            if (!existed)
            {
                AppendEvent(RecEvent.CreateIdDefine(index, RecEvent.IdDefinitionKind.Source), ReadOnlySpan<float>.Empty);
            }

            return index;
        }

        private ushort EnsureExpressionIdDefined(string expressionId)
        {
            bool existed = _idTable.TryGetExpressionIndex(expressionId, out ushort index);
            index = _idTable.GetOrAddExpressionId(expressionId);
            if (!existed)
            {
                AppendEvent(RecEvent.CreateIdDefine(index, RecEvent.IdDefinitionKind.Expression), ReadOnlySpan<float>.Empty);
            }

            return index;
        }

        private void AppendEvent(in RecEvent evt, ReadOnlySpan<float> axes)
        {
            _sink.AppendEvent(evt, axes);
            _eventCount++;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RecordingUseCase));
            }
        }
    }
}
