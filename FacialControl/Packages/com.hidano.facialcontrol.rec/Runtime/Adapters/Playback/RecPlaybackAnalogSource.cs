using System;
using System.Collections;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Rec.Adapters.Playback
{
    /// <summary>
    /// Playback-owned analog/gaze source injected into the core registry during rec playback.
    /// </summary>
    public sealed class RecPlaybackAnalogSource : IInputSource, IAnalogInputSource, IInjectedInputSource
    {
        private static readonly BitArray EmptyContributeMask = new BitArray(0);

        private readonly float[] _axes;
        private bool _isValid;

        public RecPlaybackAnalogSource(string id, int axisCount, IInputSource replacedSource)
        {
            if (axisCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(axisCount), axisCount, "Axis count must be greater than zero.");
            }

            Id = InputSourceId.Parse(id).Value;
            AxisCount = axisCount;
            ReplacedSource = replacedSource;
            _axes = new float[axisCount];
        }

        public string Id { get; }

        public InputSourceType Type => InputSourceType.ValueProvider;

        public int BlendShapeCount => 0;

        public BitArray ContributeMask => EmptyContributeMask;

        public bool IsValid => _isValid;

        public int AxisCount { get; }

        public IInputSource ReplacedSource { get; }

        public void Tick(float deltaTime)
        {
        }

        public bool TryWriteValues(Span<float> output)
        {
            return false;
        }

        public bool SetAxes(ReadOnlySpan<float> axes)
        {
            if (axes.Length != AxisCount)
            {
                return false;
            }

            axes.CopyTo(_axes);
            _isValid = true;
            return true;
        }

        public bool TryReadScalar(out float value)
        {
            if (!_isValid || AxisCount <= 0)
            {
                value = default;
                return false;
            }

            value = _axes[0];
            return true;
        }

        public bool TryReadVector2(out float x, out float y)
        {
            if (!_isValid || AxisCount < 2)
            {
                x = default;
                y = default;
                return false;
            }

            x = _axes[0];
            y = _axes[1];
            return true;
        }

        public bool TryReadAxes(Span<float> output)
        {
            if (!_isValid)
            {
                return false;
            }

            int copyLength = Math.Min(output.Length, AxisCount);
            if (copyLength > 0)
            {
                _axes.AsSpan(0, copyLength).CopyTo(output);
            }

            return true;
        }
    }
}
