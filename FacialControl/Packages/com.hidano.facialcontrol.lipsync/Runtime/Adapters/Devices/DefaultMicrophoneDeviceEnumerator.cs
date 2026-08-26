using UnityEngine;

namespace Hidano.FacialControl.LipSync.Adapters.Devices
{
    public sealed class DefaultMicrophoneDeviceEnumerator : IMicrophoneDeviceEnumerator
    {
        public string[] GetDeviceNames()
        {
            return Microphone.devices;
        }
    }
}
