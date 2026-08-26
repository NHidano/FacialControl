using System;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.Json.Dto
{
    [Serializable]
    public sealed class OscSenderEndpointDto : ISerializationCallbackReceiver
    {
        public const string PresetVRChat = "vrchat";
        public const string PresetARKit = "arkit";

        public string ip = OscSenderEndpointConfig.DefaultEndpoint;
        public int port = OscConfiguration.DefaultSendPort;
        public string preset = PresetVRChat;
        public bool enabled = true;

        public OscSenderEndpointDto()
        {
        }

        public OscSenderEndpointDto(
            string ip,
            int port,
            string preset = PresetVRChat,
            bool enabled = true)
        {
            this.ip = ip;
            this.port = port;
            this.preset = preset;
            this.enabled = enabled;
            ApplyDefaults();
        }

        public OscSenderEndpointConfig ToConfig()
        {
            ApplyDefaults();
            return new OscSenderEndpointConfig(ip, port, enabled, ToAddressPresetKind(preset));
        }

        public void ApplyDefaults()
        {
            if (string.IsNullOrWhiteSpace(ip))
            {
                ip = OscSenderEndpointConfig.DefaultEndpoint;
            }
            else
            {
                ip = ip.Trim();
            }

            if (port <= 0 || port > 65535)
            {
                port = OscConfiguration.DefaultSendPort;
            }

            preset = NormalizePreset(preset);
        }

        public void OnBeforeSerialize()
        {
            ApplyDefaults();
        }

        public void OnAfterDeserialize()
        {
            ApplyDefaults();
        }

        public static string ToPresetString(AddressPresetKind preset)
        {
            switch (preset)
            {
                case AddressPresetKind.ARKit:
                    return PresetARKit;
                case AddressPresetKind.VRChat:
                default:
                    return PresetVRChat;
            }
        }

        public static AddressPresetKind ToAddressPresetKind(string preset)
        {
            return string.Equals(NormalizePreset(preset), PresetARKit, StringComparison.Ordinal)
                ? AddressPresetKind.ARKit
                : AddressPresetKind.VRChat;
        }

        private static string NormalizePreset(string preset)
        {
            if (string.IsNullOrWhiteSpace(preset))
            {
                return PresetVRChat;
            }

            string normalized = preset.Trim();
            if (string.Equals(normalized, PresetARKit, StringComparison.OrdinalIgnoreCase))
            {
                return PresetARKit;
            }

            if (string.Equals(normalized, PresetVRChat, StringComparison.OrdinalIgnoreCase))
            {
                return PresetVRChat;
            }

            return PresetVRChat;
        }
    }
}
