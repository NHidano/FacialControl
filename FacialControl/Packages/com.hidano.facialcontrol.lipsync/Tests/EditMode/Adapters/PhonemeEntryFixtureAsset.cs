using System.Collections.Generic;
using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using UnityEngine;

namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    public sealed class PhonemeEntryFixtureAsset : ScriptableObject
    {
        [SerializeReference]
        public List<PhonemeEntryBase> Entries = new List<PhonemeEntryBase>();
    }
}
