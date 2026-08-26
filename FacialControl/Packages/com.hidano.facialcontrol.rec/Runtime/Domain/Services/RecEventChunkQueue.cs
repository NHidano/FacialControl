using System;
using System.Threading;
using Hidano.FacialControl.Rec.Domain.Models;

namespace Hidano.FacialControl.Rec.Domain.Services
{
    /// <summary>
    /// Single-producer/single-consumer chunked queue for handing recording events from the main thread to a writer thread.
    /// </summary>
    public sealed class RecEventChunkQueue
    {
        private readonly int _segmentCapacity;
        private readonly int _axisFloatCapacityPerSegment;

        private Segment _producerSegment;
        private Segment _consumerSegment;
        private Segment _freeListHead;
        private int _growthCount;

        public RecEventChunkQueue(int segmentCapacity, int initialSegments, int axisFloatCapacityPerSegment)
        {
            if (segmentCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(segmentCapacity), "Segment capacity must be greater than zero.");
            }

            if (initialSegments <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialSegments), "Initial segment count must be greater than zero.");
            }

            if (axisFloatCapacityPerSegment <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(axisFloatCapacityPerSegment), "Axis float capacity must be greater than zero.");
            }

            _segmentCapacity = segmentCapacity;
            _axisFloatCapacityPerSegment = axisFloatCapacityPerSegment;

            _producerSegment = new Segment(segmentCapacity, axisFloatCapacityPerSegment);
            _consumerSegment = _producerSegment;

            for (int i = 1; i < initialSegments; i++)
            {
                PushFreeSegment(new Segment(segmentCapacity, axisFloatCapacityPerSegment));
            }
        }

        public bool IsEmpty
        {
            get
            {
                Segment segment = _consumerSegment;
                if (segment.ReadCount < Volatile.Read(ref segment.PublishedCount))
                {
                    return false;
                }

                return Volatile.Read(ref segment.Next) == null;
            }
        }

        public int GrowthCount => Volatile.Read(ref _growthCount);

        /// <summary>
        /// Producer-thread only. Adds an event to the queue and never drops it; saturation grows the queue by adding a new segment.
        /// </summary>
        public void Enqueue(in RecEvent evt, ReadOnlySpan<float> axes, string idValue = null)
        {
            ValidateAxes(evt, axes);

            Segment segment = _producerSegment;
            if (!segment.CanWrite(axes.Length, _segmentCapacity, _axisFloatCapacityPerSegment))
            {
                segment = MoveProducerToNextSegment(segment);
            }

            segment.Write(in evt, axes, idValue);
            Volatile.Write(ref segment.PublishedCount, segment.WriteCount);
        }

        /// <summary>
        /// Consumer-thread only. Returns true when an event is available. The returned axes span is valid until the next TryDequeue call.
        /// </summary>
        public bool TryDequeue(out RecEvent evt, out ReadOnlySpan<float> axes, out string idValue)
        {
            Segment segment = _consumerSegment;

            while (true)
            {
                int publishedCount = Volatile.Read(ref segment.PublishedCount);
                if (segment.ReadCount < publishedCount)
                {
                    segment.Read(out evt, out axes, out idValue);
                    return true;
                }

                Segment next = Volatile.Read(ref segment.Next);
                if (next == null)
                {
                    evt = default;
                    axes = default;
                    idValue = null;
                    return false;
                }

                MoveConsumerToNextSegment(segment, next);
                segment = next;
            }
        }

        private Segment MoveProducerToNextSegment(Segment current)
        {
            Segment next = PopFreeSegment();
            if (next == null)
            {
                next = new Segment(_segmentCapacity, _axisFloatCapacityPerSegment);
                Interlocked.Increment(ref _growthCount);
            }

            Volatile.Write(ref current.Next, next);
            _producerSegment = next;
            return next;
        }

        private void MoveConsumerToNextSegment(Segment current, Segment next)
        {
            _consumerSegment = next;
            current.ResetForReuse();
            PushFreeSegment(current);
        }

        private Segment PopFreeSegment()
        {
            while (true)
            {
                Segment head = Volatile.Read(ref _freeListHead);
                if (head == null)
                {
                    return null;
                }

                Segment next = head.FreeNext;
                if (Interlocked.CompareExchange(ref _freeListHead, next, head) == head)
                {
                    head.FreeNext = null;
                    return head;
                }
            }
        }

        private void PushFreeSegment(Segment segment)
        {
            while (true)
            {
                Segment currentHead = Volatile.Read(ref _freeListHead);
                segment.FreeNext = currentHead;
                if (Interlocked.CompareExchange(ref _freeListHead, segment, currentHead) == currentHead)
                {
                    return;
                }
            }
        }

        private static void ValidateAxes(in RecEvent evt, ReadOnlySpan<float> axes)
        {
            if (axes.Length != evt.AxisCount)
            {
                throw new ArgumentException("Axes length must match the event axis count.", nameof(axes));
            }
        }

        private sealed class Segment
        {
            private readonly RecEvent[] _events;
            private readonly int[] _axisStarts;
            private readonly float[] _axisValues;
            private readonly string[] _idValues;

            public Segment(int segmentCapacity, int axisFloatCapacityPerSegment)
            {
                _events = new RecEvent[segmentCapacity];
                _axisStarts = new int[segmentCapacity];
                _axisValues = new float[axisFloatCapacityPerSegment];
                _idValues = new string[segmentCapacity];
            }

            public Segment Next;

            public Segment FreeNext;

            public int WriteCount;

            public int ReadCount;

            public int AxisWriteCount;

            public int PublishedCount;

            public bool CanWrite(int axisCount, int segmentCapacity, int axisFloatCapacityPerSegment)
            {
                return WriteCount < segmentCapacity
                    && AxisWriteCount + axisCount <= axisFloatCapacityPerSegment;
            }

            public void Write(in RecEvent evt, ReadOnlySpan<float> axes, string idValue)
            {
                int index = WriteCount;
                int axisStart = AxisWriteCount;

                if (!axes.IsEmpty)
                {
                    axes.CopyTo(_axisValues.AsSpan(axisStart, axes.Length));
                }

                _axisStarts[index] = axisStart;
                _events[index] = evt;
                _idValues[index] = idValue;
                AxisWriteCount += axes.Length;
                WriteCount = index + 1;
            }

            public void Read(out RecEvent evt, out ReadOnlySpan<float> axes, out string idValue)
            {
                int index = ReadCount;
                evt = _events[index];
                idValue = _idValues[index];
                _idValues[index] = null;
                axes = evt.AxisCount == 0
                    ? ReadOnlySpan<float>.Empty
                    : new ReadOnlySpan<float>(_axisValues, _axisStarts[index], evt.AxisCount);
                ReadCount = index + 1;
            }

            public void ResetForReuse()
            {
                Next = null;
                FreeNext = null;
                WriteCount = 0;
                ReadCount = 0;
                AxisWriteCount = 0;
                PublishedCount = 0;
            }
        }
    }
}
