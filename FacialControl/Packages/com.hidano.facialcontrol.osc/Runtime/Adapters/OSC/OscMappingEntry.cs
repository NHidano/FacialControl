using System;

namespace Hidano.FacialControl.Adapters.OSC
{
    [Serializable]
    public class OscMappingEntry
    {
        public OscMappingMode mode = OscMappingMode.Normal_BlendShape;
        public string expressionId = string.Empty;
        public string addressPattern = string.Empty;
        public string sourceIdLeft = string.Empty;
        public string sourceIdRight = string.Empty;
        public bool leftRightIndependent;
    }
}
