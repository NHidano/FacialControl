namespace Hidano.FacialControl.LipSync.Adapters.Devices
{
    public static class LipSyncDeviceStore
    {
        public const string KeyName = "Hidano.FacialControl.LipSync.MicDevice.Name";
        public const string KeyDisambiguator = "Hidano.FacialControl.LipSync.MicDevice.Disambiguator";

        private static IPlayerPrefsBackend _backend = new DefaultPlayerPrefsBackend();

        public static DeviceDescriptor Load()
        {
            return new DeviceDescriptor
            {
                DeviceName = _backend.GetString(KeyName, string.Empty),
                DisambiguatorIndex = _backend.GetInt(KeyDisambiguator, 0),
            };
        }

        public static void Save(DeviceDescriptor descriptor)
        {
            _backend.SetString(KeyName, descriptor.DeviceName ?? string.Empty);
            _backend.SetInt(KeyDisambiguator, descriptor.DisambiguatorIndex);
            _backend.Save();
        }

        internal static void SetBackend(IPlayerPrefsBackend backend)
        {
            _backend = backend ?? new DefaultPlayerPrefsBackend();
        }

        internal static void ResetBackend()
        {
            _backend = new DefaultPlayerPrefsBackend();
        }
    }
}
