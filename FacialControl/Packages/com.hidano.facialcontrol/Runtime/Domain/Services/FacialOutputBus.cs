using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Domain.Services
{
    /// <summary>
    /// Publishes post-blend BlendShape values and connected Gaze snapshots to registered observers.
    /// </summary>
    public sealed class FacialOutputBus : IFacialOutputBus
    {
        private readonly List<IFacialOutputObserver> _observers = new List<IFacialOutputObserver>();
        private readonly List<IFacialOutputObserver> _pendingAdds = new List<IFacialOutputObserver>();
        private readonly List<IFacialOutputObserver> _pendingRemoves = new List<IFacialOutputObserver>();
        private GazeSnapshot[] _connectedGazeSnapshots = Array.Empty<GazeSnapshot>();
        private int _publishDepth;

        public bool HasObservers => _observers.Count > 0;

        public void Subscribe(IFacialOutputObserver observer)
        {
            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            if (_publishDepth > 0)
            {
                QueueSubscribe(observer);
                return;
            }

            AddObserver(observer);
        }

        public void Unsubscribe(IFacialOutputObserver observer)
        {
            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            if (_publishDepth > 0)
            {
                QueueUnsubscribe(observer);
                return;
            }

            RemoveObserver(observer);
        }

        public void Publish(
            ReadOnlySpan<float> postBlendValues,
            ReadOnlySpan<GazeSnapshot> gazeSnapshots)
        {
            if (!HasObservers)
            {
                return;
            }

            ReadOnlySpan<GazeSnapshot> connectedGazeSnapshots =
                ExcludeDisconnectedGazeSnapshots(gazeSnapshots);

            _publishDepth++;
            try
            {
                for (int i = 0; i < _observers.Count; i++)
                {
                    try
                    {
                        _observers[i].OnFacialOutputPublished(postBlendValues, connectedGazeSnapshots);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }
            }
            finally
            {
                _publishDepth--;
                if (_publishDepth == 0)
                {
                    ApplyPendingChanges();
                }
            }
        }

        private ReadOnlySpan<GazeSnapshot> ExcludeDisconnectedGazeSnapshots(
            ReadOnlySpan<GazeSnapshot> gazeSnapshots)
        {
            int firstDisconnectedIndex = -1;
            for (int i = 0; i < gazeSnapshots.Length; i++)
            {
                if (!IsConnected(gazeSnapshots[i]))
                {
                    firstDisconnectedIndex = i;
                    break;
                }
            }

            if (firstDisconnectedIndex < 0)
            {
                return gazeSnapshots;
            }

            EnsureConnectedGazeSnapshotCapacity(gazeSnapshots.Length);

            int connectedCount = 0;
            for (int i = 0; i < gazeSnapshots.Length; i++)
            {
                GazeSnapshot snapshot = gazeSnapshots[i];
                if (IsConnected(snapshot))
                {
                    _connectedGazeSnapshots[connectedCount] = snapshot;
                    connectedCount++;
                }
            }

            return new ReadOnlySpan<GazeSnapshot>(_connectedGazeSnapshots, 0, connectedCount);
        }

        private void EnsureConnectedGazeSnapshotCapacity(int capacity)
        {
            if (_connectedGazeSnapshots.Length < capacity)
            {
                _connectedGazeSnapshots = new GazeSnapshot[capacity];
            }
        }

        private static bool IsConnected(GazeSnapshot snapshot)
        {
            return !string.IsNullOrEmpty(snapshot.ExpressionId);
        }

        private void QueueSubscribe(IFacialOutputObserver observer)
        {
            int pendingRemoveIndex = _pendingRemoves.IndexOf(observer);
            if (pendingRemoveIndex >= 0)
            {
                _pendingRemoves.RemoveAt(pendingRemoveIndex);
                return;
            }

            if (!_observers.Contains(observer) && !_pendingAdds.Contains(observer))
            {
                _pendingAdds.Add(observer);
            }
        }

        private void QueueUnsubscribe(IFacialOutputObserver observer)
        {
            int pendingAddIndex = _pendingAdds.IndexOf(observer);
            if (pendingAddIndex >= 0)
            {
                _pendingAdds.RemoveAt(pendingAddIndex);
                return;
            }

            if (_observers.Contains(observer) && !_pendingRemoves.Contains(observer))
            {
                _pendingRemoves.Add(observer);
            }
        }

        private void ApplyPendingChanges()
        {
            for (int i = 0; i < _pendingRemoves.Count; i++)
            {
                RemoveObserver(_pendingRemoves[i]);
            }

            _pendingRemoves.Clear();

            for (int i = 0; i < _pendingAdds.Count; i++)
            {
                AddObserver(_pendingAdds[i]);
            }

            _pendingAdds.Clear();
        }

        private void AddObserver(IFacialOutputObserver observer)
        {
            if (!_observers.Contains(observer))
            {
                _observers.Add(observer);
            }
        }

        private void RemoveObserver(IFacialOutputObserver observer)
        {
            int index = _observers.IndexOf(observer);
            if (index >= 0)
            {
                _observers.RemoveAt(index);
            }
        }
    }
}
