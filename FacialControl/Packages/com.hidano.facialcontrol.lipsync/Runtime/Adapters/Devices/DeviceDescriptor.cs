using System;

namespace Hidano.FacialControl.LipSync.Adapters.Devices
{
    [Serializable]
    public struct DeviceDescriptor
    {
        public string DeviceName;
        public int DisambiguatorIndex;
    }
}
