using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Adapters.OSC
{
    public sealed class OscBundleAccumulator
    {
        public const float DefaultBundleAccumulationTimeoutMs = 5f;

        private const double MillisecondsToSeconds = 0.001d;
        private const ulong BareTimestampKey = 0UL;
        private const ulong ImmediateTimestampKey = 0x1UL;

        private readonly OscDoubleBuffer _buffer;
        private readonly object _sync = new object();
        private readonly Queue<List<BufferedValue>> _readyFrames = new Queue<List<BufferedValue>>();

        private List<BufferedValue> _currentBundleValues = new List<BufferedValue>();
        private List<BufferedValue> _bareValues = new List<BufferedValue>();
        private float _bundleAccumulationTimeoutMs;
        private ulong _currentTimestampKey;
        private double _currentBundleFirstReceivedAtSeconds;
        private bool _hasCurrentBundle;

        public OscBundleAccumulator(
            OscDoubleBuffer buffer,
            float bundleAccumulationTimeoutMs = DefaultBundleAccumulationTimeoutMs)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            BundleAccumulationTimeoutMs = bundleAccumulationTimeoutMs;
        }

        public float BundleAccumulationTimeoutMs
        {
            get => _bundleAccumulationTimeoutMs;
            set
            {
                if (value < 0f)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value,
                        "Timeout must be greater than or equal to zero.");
                }

                _bundleAccumulationTimeoutMs = value;
            }
        }

        public int PendingFrameCount
        {
            get
            {
                lock (_sync)
                {
                    return _readyFrames.Count;
                }
            }
        }

        public bool HasOpenBundle
        {
            get
            {
                lock (_sync)
                {
                    return _hasCurrentBundle;
                }
            }
        }

        public ulong CurrentTimestampKey
        {
            get
            {
                lock (_sync)
                {
                    return _currentTimestampKey;
                }
            }
        }

        public static bool IsBundleTimestamp(ulong timestampKey)
        {
            return timestampKey != BareTimestampKey && timestampKey != ImmediateTimestampKey;
        }

        public void RecordMessage(uOSC.Message message, int index, float value, double receivedAtSeconds)
        {
            ulong timestampKey = message.timestamp.value;
            if (IsBundleTimestamp(timestampKey))
            {
                RecordBundleMessage(timestampKey, index, value, receivedAtSeconds);
                return;
            }

            RecordBareMessage(index, value);
        }

        public void RecordBundleMessage(ulong timestampKey, int index, float value, double receivedAtSeconds)
        {
            if (!IsBundleTimestamp(timestampKey))
            {
                RecordBareMessage(index, value);
                return;
            }

            ValidateIndex(index);

            lock (_sync)
            {
                CompleteBareMessagesLocked();

                if (!_hasCurrentBundle)
                {
                    StartBundleLocked(timestampKey, receivedAtSeconds);
                }
                else if (_currentTimestampKey != timestampKey)
                {
                    CompleteCurrentBundleLocked();
                    StartBundleLocked(timestampKey, receivedAtSeconds);
                }

                _currentBundleValues.Add(new BufferedValue(index, value));
            }
        }

        public void RecordBareMessage(int index, float value)
        {
            ValidateIndex(index);

            lock (_sync)
            {
                CompleteCurrentBundleLocked();
                _bareValues.Add(new BufferedValue(index, value));
            }
        }

        public int FlushDue(double nowSeconds)
        {
            int swapCount = 0;

            while (true)
            {
                List<BufferedValue> frame;
                lock (_sync)
                {
                    if (swapCount == 0)
                    {
                        if (IsCurrentBundleTimedOutLocked(nowSeconds))
                        {
                            CompleteCurrentBundleLocked();
                        }

                        CompleteBareMessagesLocked();
                    }

                    if (_readyFrames.Count == 0)
                    {
                        return swapCount;
                    }

                    frame = _readyFrames.Dequeue();
                }

                ApplyFrame(frame);
                swapCount++;
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _readyFrames.Clear();
                _currentBundleValues.Clear();
                _bareValues.Clear();
                _currentTimestampKey = 0UL;
                _currentBundleFirstReceivedAtSeconds = 0d;
                _hasCurrentBundle = false;
            }
        }

        private void ValidateIndex(int index)
        {
            if (index < 0 || index >= _buffer.Size)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index,
                    $"Index must be in range 0..{_buffer.Size - 1}.");
            }
        }

        private void StartBundleLocked(ulong timestampKey, double receivedAtSeconds)
        {
            _currentTimestampKey = timestampKey;
            _currentBundleFirstReceivedAtSeconds = receivedAtSeconds;
            _hasCurrentBundle = true;
        }

        private bool IsCurrentBundleTimedOutLocked(double nowSeconds)
        {
            if (!_hasCurrentBundle)
            {
                return false;
            }

            double timeoutSeconds = _bundleAccumulationTimeoutMs * MillisecondsToSeconds;
            return nowSeconds - _currentBundleFirstReceivedAtSeconds >= timeoutSeconds;
        }

        private void CompleteCurrentBundleLocked()
        {
            if (!_hasCurrentBundle)
            {
                return;
            }

            if (_currentBundleValues.Count > 0)
            {
                _readyFrames.Enqueue(_currentBundleValues);
                _currentBundleValues = new List<BufferedValue>(_currentBundleValues.Count);
            }

            _currentTimestampKey = 0UL;
            _currentBundleFirstReceivedAtSeconds = 0d;
            _hasCurrentBundle = false;
        }

        private void CompleteBareMessagesLocked()
        {
            if (_bareValues.Count == 0)
            {
                return;
            }

            _readyFrames.Enqueue(_bareValues);
            _bareValues = new List<BufferedValue>(_bareValues.Count);
        }

        private void ApplyFrame(List<BufferedValue> frame)
        {
            for (int i = 0; i < frame.Count; i++)
            {
                BufferedValue value = frame[i];
                _buffer.Write(value.Index, value.Value);
            }

            _buffer.Swap();
        }

        private readonly struct BufferedValue
        {
            public readonly int Index;
            public readonly float Value;

            public BufferedValue(int index, float value)
            {
                Index = index;
                Value = value;
            }
        }
    }
}
