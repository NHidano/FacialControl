using System;

namespace Hidano.FacialControl.Adapters.OSC
{
    [Serializable]
    public enum FailSafeMode
    {
        RevertToBase,
        HoldLastValue
    }
}
