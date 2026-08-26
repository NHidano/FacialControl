using System;
using UnityEngine;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using NAudio.Wave;
#endif

namespace Hidano.FacialControl.LipSync.Adapters.Devices
{
    public sealed class DefaultAsioDriverEnumerator : IAsioDriverEnumerator
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private readonly Func<string[]> _getDriverNames;

        public DefaultAsioDriverEnumerator()
            : this(AsioOut.GetDriverNames)
        {
        }

        internal DefaultAsioDriverEnumerator(Func<string[]> getDriverNames)
        {
            _getDriverNames = getDriverNames ?? throw new ArgumentNullException(nameof(getDriverNames));
        }

        public string[] GetDriverNames()
        {
            try
            {
                return _getDriverNames() ?? Array.Empty<string>();
            }
            catch (Exception exception)
            {
                Debug.LogError($"Failed to enumerate ASIO drivers: {exception}");
                return Array.Empty<string>();
            }
        }
#else
        public string[] GetDriverNames()
        {
            return Array.Empty<string>();
        }
#endif
    }
}
