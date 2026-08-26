using System;
using UnityEngine;

namespace Hidano.FacialControl.LipSync.Adapters.PhonemeEntries
{
    [Serializable]
    public sealed class ExpressionPhonemeEntry : PhonemeEntryBase
    {
        [SerializeField] private string _expressionId = string.Empty;

        public string ExpressionId => _expressionId;
    }
}
