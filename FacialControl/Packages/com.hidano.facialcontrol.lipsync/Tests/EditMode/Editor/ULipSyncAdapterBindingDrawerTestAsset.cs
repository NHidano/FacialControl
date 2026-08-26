using Hidano.FacialControl.Domain.Adapters;
using UnityEngine;

namespace Hidano.FacialControl.LipSync.Tests.EditMode.Editor
{
    public sealed class ULipSyncAdapterBindingDrawerTestAsset : ScriptableObject
    {
        [SerializeReference]
        public AdapterBindingBase Binding;
    }
}
