using System;

namespace Hidano.FacialControl.LipSync.Adapters
{
    public interface IULipSyncEventSource
    {
        event Action<uLipSync.LipSyncInfo> OnLipSyncUpdate;
    }
}
