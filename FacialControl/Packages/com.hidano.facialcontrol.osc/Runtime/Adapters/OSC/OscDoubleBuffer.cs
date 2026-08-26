using System;
using System.Threading;
using Unity.Collections;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// OSC receive-side double buffer backed by NativeArray.
    /// </summary>
    public class OscDoubleBuffer : IDisposable
    {
        private NativeArray<float> _bufferA;
        private NativeArray<float> _bufferB;
        private readonly object _resizeLock = new object();
        private int _writeIndex; // 0 = A is write buffer, 1 = B is write buffer
        private int _size;
        private int _writeTick;
        private bool _disposed;

        /// <summary>
        /// Buffer element count.
        /// </summary>
        public int Size => _size;

        /// <summary>
        /// Monotonic counter incremented after successful writes.
        /// </summary>
        public int WriteTick => Volatile.Read(ref _writeTick);

        public OscDoubleBuffer(int size)
        {
            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, "Size must be 0 or greater.");
            }

            _size = size;
            _bufferA = new NativeArray<float>(size, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            _bufferB = new NativeArray<float>(size, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            _writeIndex = 0;
        }

        /// <summary>
        /// Writes a value into the current write buffer. Out-of-range writes are dropped.
        /// </summary>
        public void Write(int index, float value)
        {
            lock (_resizeLock)
            {
                if (_disposed || index < 0 || index >= _size)
                {
                    return;
                }

                var writeBuffer = GetWriteBuffer();
                writeBuffer[index] = value;
                Interlocked.Increment(ref _writeTick);
            }
        }

        /// <summary>
        /// Swaps read and write buffers. Called from the main thread at frame boundaries.
        /// </summary>
        /// <remarks>
        /// Index flip 後、新 read buffer の内容を新 write buffer へ copy-forward する
        /// (<c>LayerInputSourceWeightBuffer.SwapIfDirty</c> と同じ Critical 1 対策)。
        /// 旧実装は新 write buffer をゼロクリアしていたため、「その frame に受信しなかった
        /// index」が次の Swap で 0 として読者に観測され、bundle のパケット分断・ロスト・
        /// 受信の無い tick を挟んだ瞬間に表情が一瞬素に戻る不具合があった。
        /// copy-forward により未受信 index は前回値を保持する（受信停止時のゼロ化は
        /// <c>OscInputSource</c> の staleness + <c>FailSafeMode</c> が担う）。
        /// <see cref="Write"/>（受信側スレッド）との競合による書込ロストを防ぐため
        /// <c>_resizeLock</c> で排他する。
        /// </remarks>
        public void Swap()
        {
            lock (_resizeLock)
            {
                if (_disposed)
                {
                    return;
                }

                int newWriteIndex = 1 - _writeIndex;
                Interlocked.Exchange(ref _writeIndex, newWriteIndex);

                var newReadBuffer = newWriteIndex == 0 ? _bufferB : _bufferA;
                var newWriteBuffer = newWriteIndex == 0 ? _bufferA : _bufferB;
                newReadBuffer.CopyTo(newWriteBuffer);
            }
        }

        /// <summary>
        /// Gets the read buffer. Main-thread only.
        /// </summary>
        public NativeArray<float>.ReadOnly GetReadBuffer()
        {
            var readBuffer = _writeIndex == 0 ? _bufferB : _bufferA;
            return readBuffer.AsReadOnly();
        }

        /// <summary>
        /// Resizes both buffers while preserving overlapping values.
        /// </summary>
        public void Resize(int newSize)
        {
            if (newSize < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(newSize), newSize, "Size must be 0 or greater.");
            }

            lock (_resizeLock)
            {
                if (newSize == _size)
                {
                    return;
                }

                var newBufferA = new NativeArray<float>(newSize, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                NativeArray<float> newBufferB = default;

                try
                {
                    newBufferB = new NativeArray<float>(newSize, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                    CopyBuffer(_bufferA, newBufferA);
                    CopyBuffer(_bufferB, newBufferB);
                }
                catch
                {
                    if (newBufferA.IsCreated)
                    {
                        newBufferA.Dispose();
                    }

                    if (newBufferB.IsCreated)
                    {
                        newBufferB.Dispose();
                    }

                    throw;
                }

                var oldBufferA = _bufferA;
                var oldBufferB = _bufferB;

                _bufferA = newBufferA;
                _bufferB = newBufferB;
                _size = newSize;

                if (oldBufferA.IsCreated)
                {
                    oldBufferA.Dispose();
                }

                if (oldBufferB.IsCreated)
                {
                    oldBufferB.Dispose();
                }
            }
        }

        public void Dispose()
        {
            lock (_resizeLock)
            {
                if (_disposed)
                {
                    return;
                }

                if (_bufferA.IsCreated)
                {
                    _bufferA.Dispose();
                }

                if (_bufferB.IsCreated)
                {
                    _bufferB.Dispose();
                }

                _disposed = true;
            }
        }

        private NativeArray<float> GetWriteBuffer()
        {
            return _writeIndex == 0 ? _bufferA : _bufferB;
        }

        private static void CopyBuffer(NativeArray<float> source, NativeArray<float> destination)
        {
            if (!source.IsCreated || !destination.IsCreated)
            {
                return;
            }

            int count = Math.Min(source.Length, destination.Length);
            for (int i = 0; i < count; i++)
            {
                destination[i] = source[i];
            }
        }
    }
}
