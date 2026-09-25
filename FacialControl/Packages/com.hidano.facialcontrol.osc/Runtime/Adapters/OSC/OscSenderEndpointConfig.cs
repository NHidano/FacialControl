using System;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.OSC
{
    [Serializable]
    public class OscSenderEndpointConfig
    {
        public const string DefaultEndpoint = "127.0.0.1";

        public string endpoint = DefaultEndpoint;
        public int port = OscConfiguration.DefaultSendPort;
        public bool enabled = true;
        public AddressPresetKind preset = AddressPresetKind.VRChat;

        public OscSenderEndpointConfig()
        {
        }

        public OscSenderEndpointConfig(
            string endpoint,
            int port,
            bool enabled = true,
            AddressPresetKind preset = AddressPresetKind.VRChat)
        {
            this.endpoint = endpoint ?? string.Empty;
            this.port = port;
            this.enabled = enabled;
            this.preset = preset;
        }
    }
}
