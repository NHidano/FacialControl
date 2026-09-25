namespace Hidano.FacialControl.Rec.Domain.Models
{
    /// <summary>
    /// Record kinds persisted by the REC package.
    /// </summary>
    public enum RecEventKind : byte
    {
        IdDefine = 1,
        TriggerOn = 2,
        TriggerOff = 3,
        AnalogSample = 4,
        BaselineTrigger = 5,
        BaselineAnalog = 6,
        Footer = 255,
    }
}
