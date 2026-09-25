using System;
using UnityEngine.Events;

namespace Hidano.FacialControl.LipSync.Adapters
{
    internal sealed class ULipSyncEventBridge : IULipSyncEventSource, IDisposable
    {
        private readonly uLipSync.uLipSync _source;
        private readonly UnityAction<uLipSync.LipSyncInfo> _unityEventHandler;
        private bool _disposed;

        public ULipSyncEventBridge(uLipSync.uLipSync source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _unityEventHandler = ForwardLipSyncUpdate;
            _source.onLipSyncUpdate.AddListener(_unityEventHandler);
        }

        public event Action<uLipSync.LipSyncInfo> OnLipSyncUpdate;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _source.onLipSyncUpdate.RemoveListener(_unityEventHandler);
            OnLipSyncUpdate = null;
            _disposed = true;
        }

        private void ForwardLipSyncUpdate(uLipSync.LipSyncInfo info)
        {
            OnLipSyncUpdate?.Invoke(info);
        }
    }
}
