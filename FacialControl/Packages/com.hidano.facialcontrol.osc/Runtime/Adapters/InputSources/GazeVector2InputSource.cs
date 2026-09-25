using System;
using System.Collections;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.InputSources
{
    /// <summary>
    /// Exposes a Gaze Vector2 value through both IInputSource and IAnalogInputSource.
    /// </summary>
    public sealed class GazeVector2InputSource : IInputSource, IAnalogInputSource
    {
        private readonly InputSourceId _id;
        private readonly BitArray _contributeMask;

        private float _x;
        private float _y;
        private bool _isValid;

        public GazeVector2InputSource(InputSourceId id)
        {
            if (string.IsNullOrEmpty(id.Value))
            {
                throw new ArgumentException(
                    "id must be an initialized InputSourceId.", nameof(id));
            }

            _id = id;
            _contributeMask = new BitArray(0);
        }

        /// <inheritdoc />
        public string Id => _id.Value;

        /// <inheritdoc />
        public InputSourceType Type => InputSourceType.ValueProvider;

        /// <inheritdoc />
        public int BlendShapeCount => 0;

        /// <inheritdoc />
        public BitArray ContributeMask => _contributeMask;

        /// <inheritdoc />
        public bool IsValid => _isValid;

        /// <inheritdoc />
        public int AxisCount => 2;

        public void Publish(Vector2 value)
        {
            Publish(value.x, value.y);
        }

        public void Publish(float x, float y)
        {
            _x = x;
            _y = y;
            _isValid = true;
        }

        public void PublishZero()
        {
            Publish(0f, 0f);
        }

        public void Invalidate()
        {
            _isValid = false;
        }

        /// <inheritdoc />
        public void Tick(float deltaTime)
        {
        }

        /// <inheritdoc />
        public bool TryWriteValues(Span<float> output)
        {
            return false;
        }

        /// <inheritdoc />
        public bool TryReadScalar(out float value)
        {
            if (!_isValid)
            {
                value = default;
                return false;
            }

            value = _x;
            return true;
        }

        /// <inheritdoc />
        public bool TryReadVector2(out float x, out float y)
        {
            if (!_isValid)
            {
                x = default;
                y = default;
                return false;
            }

            x = _x;
            y = _y;
            return true;
        }

        /// <inheritdoc />
        public bool TryReadAxes(Span<float> output)
        {
            if (!_isValid || output.Length == 0)
            {
                return false;
            }

            output[0] = _x;
            if (output.Length > 1)
            {
                output[1] = _y;
            }

            return true;
        }
    }
}
