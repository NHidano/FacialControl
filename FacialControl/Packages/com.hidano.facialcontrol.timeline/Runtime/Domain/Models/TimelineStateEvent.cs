namespace Hidano.FacialControl.Timeline.Domain.Models
{
    public readonly struct TimelineStateEvent
    {
        public const byte KindOn = 0;
        public const byte KindOff = 1;

        public TimelineStateEvent(double timeSeconds, byte kind, string expressionId, string layerName)
        {
            TimeSeconds = timeSeconds;
            Kind = kind;
            ExpressionId = expressionId;
            LayerName = layerName;
        }

        public double TimeSeconds { get; }

        public byte Kind { get; }

        public string ExpressionId { get; }

        public string LayerName { get; }

        public bool IsOn => Kind == KindOn;

        public bool IsOff => Kind == KindOff;
    }
}
