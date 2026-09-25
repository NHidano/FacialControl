using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// 複数の OSC 送信元から最新起動時刻の UUID だけを採用するポリシー。
    /// </summary>
    public sealed class ZombieEvictionPolicy
    {
        public const int DefaultMaxObservedSenders = 16;

        private readonly Dictionary<Guid, ObservedSender> _observedSenders;
        private readonly Queue<Guid> _insertionOrder;
        private readonly int _maxObservedSenders;

        private SenderIdentity _currentSender;
        private long _nextSequence;
        private bool _hasCurrentSender;

        public ZombieEvictionPolicy(int maxObservedSenders = DefaultMaxObservedSenders)
        {
            if (maxObservedSenders <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxObservedSenders), maxObservedSenders,
                    "Max observed sender count must be greater than zero.");
            }

            _maxObservedSenders = maxObservedSenders;
            _observedSenders = new Dictionary<Guid, ObservedSender>(maxObservedSenders);
            _insertionOrder = new Queue<Guid>(maxObservedSenders);
        }

        public int MaxObservedSenders => _maxObservedSenders;

        public int ObservedSenderCount => _observedSenders.Count;

        public bool HasCurrentSender => _hasCurrentSender;

        public SenderIdentity CurrentSender
        {
            get
            {
                if (!_hasCurrentSender)
                {
                    throw new InvalidOperationException("No sender identity has been observed.");
                }

                return _currentSender;
            }
        }

        /// <summary>
        /// 送信元を観測し、観測後の採用送信元と一致する場合 true を返す。
        /// </summary>
        public bool Observe(SenderIdentity identity)
        {
            Record(identity);
            UpdateCurrentSender();
            return IsAccepted(identity);
        }

        /// <summary>
        /// 指定送信元が現在採用されている送信元と一致するかを返す。
        /// </summary>
        public bool IsAccepted(SenderIdentity identity)
        {
            return _hasCurrentSender && _currentSender.Equals(identity);
        }

        public bool ContainsObservedSender(Guid senderId)
        {
            return _observedSenders.ContainsKey(senderId);
        }

        public void Clear()
        {
            _observedSenders.Clear();
            _insertionOrder.Clear();
            _currentSender = default;
            _nextSequence = 0L;
            _hasCurrentSender = false;
        }

        private void Record(SenderIdentity identity)
        {
            if (_observedSenders.TryGetValue(identity.SenderId, out ObservedSender existing))
            {
                if (identity.StartedAtUnixMs > existing.Identity.StartedAtUnixMs)
                {
                    _observedSenders[identity.SenderId] =
                        new ObservedSender(identity, existing.Sequence);
                }

                return;
            }

            _observedSenders.Add(
                identity.SenderId,
                new ObservedSender(identity, _nextSequence++));
            _insertionOrder.Enqueue(identity.SenderId);

            while (_observedSenders.Count > _maxObservedSenders && _insertionOrder.Count > 0)
            {
                Guid evictedId = _insertionOrder.Dequeue();
                _observedSenders.Remove(evictedId);
            }
        }

        private void UpdateCurrentSender()
        {
            if (_observedSenders.Count == 0)
            {
                _hasCurrentSender = false;
                _currentSender = default;
                return;
            }

            bool hasCandidate = false;
            ObservedSender best = default;
            foreach (ObservedSender candidate in _observedSenders.Values)
            {
                if (!hasCandidate || IsNewer(candidate, best))
                {
                    best = candidate;
                    hasCandidate = true;
                }
            }

            SenderIdentity previous = _currentSender;
            bool hadPrevious = _hasCurrentSender;
            _currentSender = best.Identity;
            _hasCurrentSender = true;

            if (hadPrevious && previous.SenderId != _currentSender.SenderId)
            {
                Debug.Log(
                    "[FacialControl] ZombieEvictionPolicy adopted sender changed: "
                    + $"previousUuid={previous.SenderId:D}, previousStartedAtUnixMs={previous.StartedAtUnixMs}, "
                    + $"currentUuid={_currentSender.SenderId:D}, currentStartedAtUnixMs={_currentSender.StartedAtUnixMs}");
            }
        }

        private static bool IsNewer(ObservedSender candidate, ObservedSender current)
        {
            if (candidate.Identity.StartedAtUnixMs != current.Identity.StartedAtUnixMs)
            {
                return candidate.Identity.StartedAtUnixMs > current.Identity.StartedAtUnixMs;
            }

            return candidate.Sequence < current.Sequence;
        }

        private readonly struct ObservedSender
        {
            public readonly SenderIdentity Identity;
            public readonly long Sequence;

            public ObservedSender(SenderIdentity identity, long sequence)
            {
                Identity = identity;
                Sequence = sequence;
            }
        }
    }
}
