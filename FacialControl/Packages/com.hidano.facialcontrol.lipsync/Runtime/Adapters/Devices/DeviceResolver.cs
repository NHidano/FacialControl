using System;

namespace Hidano.FacialControl.LipSync.Adapters.Devices
{
    public enum DeviceKind
    {
        Asio,
        Microphone,
        Unresolved,
    }

    public readonly struct DeviceResolution
    {
        public readonly DeviceKind Kind;
        public readonly int ResolvedIndex;
        public readonly string DeviceNameMatched;
        public readonly string[] AvailableAsio;
        public readonly string[] AvailableMic;

        public DeviceResolution(
            DeviceKind kind,
            int resolvedIndex,
            string deviceNameMatched,
            string[] availableAsio,
            string[] availableMic)
        {
            Kind = kind;
            ResolvedIndex = resolvedIndex;
            DeviceNameMatched = deviceNameMatched;
            AvailableAsio = availableAsio ?? Array.Empty<string>();
            AvailableMic = availableMic ?? Array.Empty<string>();
        }
    }

    public static class DeviceResolver
    {
        public static DeviceResolution Resolve(
            DeviceDescriptor descriptor,
            IAsioDriverEnumerator asioEnumerator,
            IMicrophoneDeviceEnumerator micEnumerator)
        {
            if (asioEnumerator == null)
            {
                throw new ArgumentNullException(nameof(asioEnumerator));
            }

            if (micEnumerator == null)
            {
                throw new ArgumentNullException(nameof(micEnumerator));
            }

            // デバイス名未指定（初回起動・デバイス未選択）は既定マイクへフォールバックする。
            // ASIO は明示選択が前提のため既定にはせず、マイク一覧の先頭を採用する。
            // マイクが 1 台も無い場合のみ Unresolved を返す。
            if (string.IsNullOrWhiteSpace(descriptor.DeviceName))
            {
                string[] fallbackAsio = asioEnumerator.GetDriverNames() ?? Array.Empty<string>();
                string[] fallbackMics = micEnumerator.GetDeviceNames() ?? Array.Empty<string>();
                if (fallbackMics.Length > 0)
                {
                    return new DeviceResolution(
                        DeviceKind.Microphone,
                        0,
                        fallbackMics[0],
                        fallbackAsio,
                        fallbackMics);
                }

                return new DeviceResolution(
                    DeviceKind.Unresolved,
                    -1,
                    null,
                    fallbackAsio,
                    fallbackMics);
            }

            string[] asioNames = asioEnumerator.GetDriverNames() ?? Array.Empty<string>();
            if (TryResolve(asioNames, descriptor, out int asioIndex, out string asioName, out bool asioNameFound))
            {
                return new DeviceResolution(
                    DeviceKind.Asio,
                    asioIndex,
                    asioName,
                    asioNames,
                    Array.Empty<string>());
            }

            string[] micNames = micEnumerator.GetDeviceNames() ?? Array.Empty<string>();
            if (asioNameFound)
            {
                return new DeviceResolution(
                    DeviceKind.Unresolved,
                    -1,
                    null,
                    asioNames,
                    micNames);
            }

            if (TryResolve(micNames, descriptor, out int micIndex, out string micName, out _))
            {
                return new DeviceResolution(
                    DeviceKind.Microphone,
                    micIndex,
                    micName,
                    asioNames,
                    micNames);
            }

            return new DeviceResolution(
                DeviceKind.Unresolved,
                -1,
                null,
                asioNames,
                micNames);
        }

        private static bool TryResolve(
            string[] names,
            DeviceDescriptor descriptor,
            out int resolvedIndex,
            out string matchedName,
            out bool nameFound)
        {
            int matchOrdinal = 0;
            nameFound = false;
            for (int i = 0; i < names.Length; i++)
            {
                if (!string.Equals(names[i], descriptor.DeviceName, StringComparison.Ordinal))
                {
                    continue;
                }

                nameFound = true;
                if (matchOrdinal == descriptor.DisambiguatorIndex)
                {
                    resolvedIndex = i;
                    matchedName = names[i];
                    return true;
                }

                matchOrdinal++;
            }

            resolvedIndex = -1;
            matchedName = null;
            return false;
        }
    }
}
