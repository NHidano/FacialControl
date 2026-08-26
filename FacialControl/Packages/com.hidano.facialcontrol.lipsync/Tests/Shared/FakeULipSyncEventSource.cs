using System;
using Hidano.FacialControl.LipSync.Adapters;

namespace Hidano.FacialControl.LipSync.Tests.Shared
{
    public sealed class FakeULipSyncEventSource : IULipSyncEventSource
    {
        public event Action<uLipSync.LipSyncInfo> OnLipSyncUpdate;

        public void Invoke(uLipSync.LipSyncInfo info)
        {
            OnLipSyncUpdate?.Invoke(info);
        }
    }
}
