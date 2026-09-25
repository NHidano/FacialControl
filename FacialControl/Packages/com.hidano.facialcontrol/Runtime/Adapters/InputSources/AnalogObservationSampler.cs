using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;

namespace Hidano.FacialControl.Adapters.InputSources
{
    /// <summary>
    /// Pull-samples registered analog input sources and publishes only changed values.
    /// </summary>
    public sealed class AnalogObservationSampler
    {
        private readonly IInputSourceRegistry _registry;
        private readonly IFacialInputObservationBus _bus;
        private readonly List<TrackedSource> _trackedSources = new List<TrackedSource>();
        private readonly Dictionary<string, int> _trackedSourceIndices =
            new Dictionary<string, int>(StringComparer.Ordinal);

        private float[] _readBuffer = Array.Empty<float>();

        public AnalogObservationSampler(IInputSourceRegistry registry, IFacialInputObservationBus bus)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        /// <summary>
        /// Samples every registered analog source once and publishes only changed, valid values.
        /// </summary>
        public void Sample()
        {
            if (!_bus.HasObservers)
            {
                return;
            }

            SynchronizeTrackedSources();

            for (int i = 0; i < _trackedSources.Count; i++)
            {
                TrackedSource tracked = _trackedSources[i];
                IAnalogInputSource source = tracked.Source;
                if (source == null || !source.IsValid || tracked.AxisCount <= 0)
                {
                    continue;
                }

                if (!TryReadChangedAxes(tracked, out ReadOnlySpan<float> axes))
                {
                    continue;
                }

                _bus.PublishAnalogSample(tracked.Id, axes);
            }
        }

        private void SynchronizeTrackedSources()
        {
            for (int i = 0; i < _trackedSources.Count; i++)
            {
                TrackedSource tracked = _trackedSources[i];
                tracked.Seen = false;
                _trackedSources[i] = tracked;
            }

            IReadOnlyList<string> registeredIds = _registry.RegisteredIds;
            for (int i = 0; i < registeredIds.Count; i++)
            {
                string id = registeredIds[i];
                if (string.IsNullOrEmpty(id)
                    || !_registry.TryResolve(id, out IInputSource inputSource)
                    || !(inputSource is IAnalogInputSource analogSource)
                    || analogSource.AxisCount <= 0)
                {
                    RemoveTrackedSource(id);
                    continue;
                }

                if (_trackedSourceIndices.TryGetValue(id, out int trackedIndex))
                {
                    TrackedSource tracked = _trackedSources[trackedIndex];
                    tracked.Seen = true;
                    tracked.Source = analogSource;

                    int axisCount = analogSource.AxisCount;
                    if (tracked.AxisCount != axisCount)
                    {
                        tracked.AxisCount = axisCount;
                        tracked.Values = new float[axisCount];
                        tracked.HasSample = false;
                    }

                    _trackedSources[trackedIndex] = tracked;
                    continue;
                }

                var added = new TrackedSource(id, analogSource);
                added.Seen = true;
                _trackedSourceIndices.Add(id, _trackedSources.Count);
                _trackedSources.Add(added);
            }

            for (int i = _trackedSources.Count - 1; i >= 0; i--)
            {
                if (_trackedSources[i].Seen)
                {
                    continue;
                }

                RemoveTrackedSourceAt(i);
            }
        }

        private bool TryReadChangedAxes(TrackedSource tracked, out ReadOnlySpan<float> axes)
        {
            axes = default;
            switch (tracked.AxisCount)
            {
                case 1:
                    return TryReadChangedScalar(tracked, out axes);
                case 2:
                    return TryReadChangedVector2(tracked, out axes);
                default:
                    return TryReadChangedMultiAxis(tracked, out axes);
            }
        }

        private bool TryReadChangedScalar(TrackedSource tracked, out ReadOnlySpan<float> axes)
        {
            axes = default;
            if (!tracked.Source.TryReadScalar(out float value))
            {
                return false;
            }

            int trackedIndex = _trackedSourceIndices[tracked.Id];
            TrackedSource current = _trackedSources[trackedIndex];
            bool changed = !current.HasSample || !AreBitsEqual(current.Values[0], value);
            current.Values[0] = value;
            current.HasSample = true;
            _trackedSources[trackedIndex] = current;

            if (!changed)
            {
                return false;
            }

            axes = new ReadOnlySpan<float>(current.Values, 0, 1);
            return true;
        }

        private bool TryReadChangedVector2(TrackedSource tracked, out ReadOnlySpan<float> axes)
        {
            axes = default;
            if (!tracked.Source.TryReadVector2(out float x, out float y))
            {
                return false;
            }

            int trackedIndex = _trackedSourceIndices[tracked.Id];
            TrackedSource current = _trackedSources[trackedIndex];
            bool changed = !current.HasSample
                || !AreBitsEqual(current.Values[0], x)
                || !AreBitsEqual(current.Values[1], y);

            current.Values[0] = x;
            current.Values[1] = y;
            current.HasSample = true;
            _trackedSources[trackedIndex] = current;

            if (!changed)
            {
                return false;
            }

            axes = new ReadOnlySpan<float>(current.Values, 0, 2);
            return true;
        }

        private bool TryReadChangedMultiAxis(TrackedSource tracked, out ReadOnlySpan<float> axes)
        {
            axes = default;
            EnsureReadBufferCapacity(tracked.AxisCount);
            Span<float> readBuffer = _readBuffer.AsSpan(0, tracked.AxisCount);
            if (!tracked.Source.TryReadAxes(readBuffer))
            {
                return false;
            }

            int trackedIndex = _trackedSourceIndices[tracked.Id];
            TrackedSource current = _trackedSources[trackedIndex];
            bool changed = !current.HasSample;
            for (int i = 0; i < tracked.AxisCount && !changed; i++)
            {
                changed = !AreBitsEqual(current.Values[i], readBuffer[i]);
            }

            for (int i = 0; i < tracked.AxisCount; i++)
            {
                current.Values[i] = readBuffer[i];
            }

            current.HasSample = true;
            _trackedSources[trackedIndex] = current;

            if (!changed)
            {
                return false;
            }

            axes = new ReadOnlySpan<float>(current.Values, 0, tracked.AxisCount);
            return true;
        }

        private void EnsureReadBufferCapacity(int axisCount)
        {
            if (_readBuffer.Length < axisCount)
            {
                _readBuffer = new float[axisCount];
            }
        }

        private void RemoveTrackedSource(string id)
        {
            if (string.IsNullOrEmpty(id) || !_trackedSourceIndices.TryGetValue(id, out int index))
            {
                return;
            }

            RemoveTrackedSourceAt(index);
        }

        private void RemoveTrackedSourceAt(int index)
        {
            string id = _trackedSources[index].Id;
            int lastIndex = _trackedSources.Count - 1;
            if (index != lastIndex)
            {
                TrackedSource moved = _trackedSources[lastIndex];
                _trackedSources[index] = moved;
                _trackedSourceIndices[moved.Id] = index;
            }

            _trackedSources.RemoveAt(lastIndex);
            _trackedSourceIndices.Remove(id);
        }

        private static bool AreBitsEqual(float left, float right)
        {
            return BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);
        }

        private struct TrackedSource
        {
            public TrackedSource(string id, IAnalogInputSource source)
            {
                Id = id;
                Source = source;
                AxisCount = source.AxisCount;
                Values = new float[source.AxisCount];
                HasSample = false;
                Seen = false;
            }

            public string Id;
            public IAnalogInputSource Source;
            public int AxisCount;
            public float[] Values;
            public bool HasSample;
            public bool Seen;
        }
    }
}
