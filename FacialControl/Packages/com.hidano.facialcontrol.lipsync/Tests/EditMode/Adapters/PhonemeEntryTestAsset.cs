using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using UnityEngine;

namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    public sealed class PhonemeEntryTestAsset : ScriptableObject
    {
        [SerializeReference] private PhonemeEntryBase _entry;

        public PhonemeEntryBase Entry => _entry;
    }
}
