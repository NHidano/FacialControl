using System;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Timeline.Adapters.InputSources
{
    /// <summary>
    /// Timeline-owned gaze sink. Values remain unclamped and are invalid outside active playback.
    /// </summary>
    public sealed class TimelineGazeInputSource : TimelineAnalogInputSource, IInjectedInputSource
    {
        public TimelineGazeInputSource(InputSourceId id)
            : base(id, axisCount: 2)
        {
        }

        public IInputSource ReplacedSource { get; private set; }

        public void AttachReplacement(IInputSource replacedSource)
        {
            ReplacedSource = replacedSource;
        }

        public void ClearReplacement()
        {
            ReplacedSource = null;
        }

        public void Publish(Vector2 value)
        {
            Publish(value.x, value.y);
        }

        public void Publish(float x, float y)
        {
            SetAxis(0, x);
            SetAxis(1, y);
        }

        public void PublishZero()
        {
            Publish(0f, 0f);
        }
    }
}
