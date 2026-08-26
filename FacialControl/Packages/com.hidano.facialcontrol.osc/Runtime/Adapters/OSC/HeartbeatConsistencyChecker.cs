using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.OSC
{
    public sealed class HeartbeatConsistencyChecker
    {
        private readonly string[] _receiverBlendShapeNames;
        private readonly HashSet<string> _receiverNameSet;
        private readonly HashSet<string> _senderNameSet;
        private readonly List<string> _senderOnlyNames;
        private readonly List<string> _receiverOnlyNames;
        private readonly HashSet<int> _loggedMismatchHashes;
        private readonly bool _warnLogEnabled;

        private bool _hasMismatch;

        public HeartbeatConsistencyChecker(
            IReadOnlyList<OscMapping> receiverMappings,
            bool warnLogEnabled = true)
            : this(ExtractBlendShapeNames(receiverMappings), warnLogEnabled)
        {
        }

        public HeartbeatConsistencyChecker(
            IReadOnlyList<string> receiverBlendShapeNames,
            bool warnLogEnabled = true)
        {
            if (receiverBlendShapeNames == null)
            {
                throw new ArgumentNullException(nameof(receiverBlendShapeNames));
            }

            _receiverBlendShapeNames = new string[receiverBlendShapeNames.Count];
            _receiverNameSet = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < receiverBlendShapeNames.Count; i++)
            {
                string name = receiverBlendShapeNames[i] ?? string.Empty;
                _receiverBlendShapeNames[i] = name;
                if (name.Length == 0)
                {
                    continue;
                }

                _receiverNameSet.Add(name);
            }

            _warnLogEnabled = warnLogEnabled;
            _senderNameSet = new HashSet<string>(StringComparer.Ordinal);
            _senderOnlyNames = new List<string>();
            _receiverOnlyNames = new List<string>();
            _loggedMismatchHashes = new HashSet<int>();
        }

        public HeartbeatConsistencyChecker(
            IReadOnlyList<string> meshBlendShapeNames,
            IReadOnlyList<OscMapping> receiverMappings,
            bool warnLogEnabled)
            : this(ExtractBlendShapeNames(receiverMappings), warnLogEnabled)
        {
            if (meshBlendShapeNames == null)
            {
                throw new ArgumentNullException(nameof(meshBlendShapeNames));
            }
        }

        public int BlendShapeCount => _receiverBlendShapeNames.Length;

        public bool HasMismatch => _hasMismatch;

        public IReadOnlyList<string> SenderOnlyNames => _senderOnlyNames;

        public IReadOnlyList<string> ReceiverOnlyNames => _receiverOnlyNames;

        public void UpdateFromHeartbeat(IReadOnlyList<string> senderBlendShapeNames)
        {
            if (senderBlendShapeNames == null)
            {
                throw new ArgumentNullException(nameof(senderBlendShapeNames));
            }

            _senderNameSet.Clear();
            _senderOnlyNames.Clear();
            _receiverOnlyNames.Clear();

            for (int i = 0; i < senderBlendShapeNames.Count; i++)
            {
                string name = senderBlendShapeNames[i];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (_senderNameSet.Add(name) && !_receiverNameSet.Contains(name))
                {
                    _senderOnlyNames.Add(name);
                }
            }

            for (int i = 0; i < _receiverBlendShapeNames.Length; i++)
            {
                string receiverName = _receiverBlendShapeNames[i];
                if (receiverName.Length > 0 &&
                    !_senderNameSet.Contains(receiverName) &&
                    !_receiverOnlyNames.Contains(receiverName))
                {
                    _receiverOnlyNames.Add(receiverName);
                }
            }

            _hasMismatch = _senderOnlyNames.Count > 0 || _receiverOnlyNames.Count > 0;
            if (_hasMismatch && _warnLogEnabled)
            {
                LogMismatchOnce();
            }
        }

        public void Clear()
        {
            _senderNameSet.Clear();
            _senderOnlyNames.Clear();
            _receiverOnlyNames.Clear();
            _loggedMismatchHashes.Clear();
            _hasMismatch = false;
        }

        private void LogMismatchOnce()
        {
            SortMismatchNames();
            int mismatchHash = ComputeMismatchHash(_senderOnlyNames, _receiverOnlyNames);
            if (!_loggedMismatchHashes.Add(mismatchHash))
            {
                return;
            }

            Debug.LogWarning(
                "[FacialControl] HeartbeatConsistencyChecker mismatch: "
                + "senderOnly=[" + string.Join(", ", _senderOnlyNames) + "], "
                + "receiverOnly=[" + string.Join(", ", _receiverOnlyNames) + "]");
        }

        private void SortMismatchNames()
        {
            _senderOnlyNames.Sort(StringComparer.Ordinal);
            _receiverOnlyNames.Sort(StringComparer.Ordinal);
        }

        private static int ComputeMismatchHash(
            IReadOnlyList<string> senderOnlyNames,
            IReadOnlyList<string> receiverOnlyNames)
        {
            unchecked
            {
                int hash = 17;
                hash = AppendHash(hash, senderOnlyNames);
                hash = (hash * 31) ^ 0x5f3759df;
                hash = AppendHash(hash, receiverOnlyNames);
                return hash;
            }
        }

        private static int AppendHash(int hash, IReadOnlyList<string> names)
        {
            unchecked
            {
                for (int i = 0; i < names.Count; i++)
                {
                    hash = (hash * 31) ^ StringComparer.Ordinal.GetHashCode(names[i]);
                }

                hash = (hash * 31) ^ names.Count;
                return hash;
            }
        }

        private static string[] ExtractBlendShapeNames(IReadOnlyList<OscMapping> mappings)
        {
            if (mappings == null)
            {
                throw new ArgumentNullException(nameof(mappings));
            }

            string[] names = new string[mappings.Count];
            for (int i = 0; i < mappings.Count; i++)
            {
                names[i] = mappings[i].BlendShapeName ?? string.Empty;
            }

            return names;
        }
    }
}
