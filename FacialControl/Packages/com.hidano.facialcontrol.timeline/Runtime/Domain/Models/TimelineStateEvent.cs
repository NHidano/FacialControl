using System;
using UnityEngine;

namespace Hidano.FacialControl.Timeline.Domain.Models
{
    [Serializable]
    public struct TimelineStateEvent
    {
        public const byte KindOn = 0;
        public const byte KindOff = 1;

        [SerializeField] private double timeSeconds;
        [SerializeField] private byte kind;
        [SerializeField] private string expressionId;
        [SerializeField] private string layerName;

        public TimelineStateEvent(double timeSeconds, byte kind, string expressionId, string layerName)
        {
            this.timeSeconds = timeSeconds;
            this.kind = kind;
            this.expressionId = expressionId;
            this.layerName = layerName;
        }

        public double TimeSeconds => timeSeconds;

        public byte Kind => kind;

        public string ExpressionId => expressionId;

        public string LayerName => layerName;

        public bool IsOn => Kind == KindOn;

        public bool IsOff => Kind == KindOff;
    }
}
