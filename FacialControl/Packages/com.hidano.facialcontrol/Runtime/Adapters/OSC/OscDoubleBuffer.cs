using System;
using System.Threading;
using Unity.Collections;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// OSC 受信用ダブルバッファ。
    /// 受信スレッドが書き込みバッファに値を書き込み、メインスレッドが読み取りバッファから値を読む。
    /// フレーム境界で Swap を呼び出すことでバッファを交換する。
    /// NativeArray ベースで GC フリー、ロックフリー設計。
    /// </summary>
    public class OscDoubleBuffer : IDisposable
    {
        private NativeArray<float> _bufferA;
        private NativeArray<float> _bufferB;
        private int _writeIndex; // 0 = A が書き込み, 1 = B が書き込み
        private int _size;
        private bool _disposed;

        /// <summary>
        /// バッファの要素数。
        /// </summary>
        public int Size => _size;

        /// <summary>
        /// 指定サイズで OscDoubleBuffer を初期化する。
        /// </summary>
        /// <param name="size">バッファの要素数（BlendShape 総数）。0 以上を指定。</param>
        public OscDoubleBuffer(int size)
        {
            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, "サイズは 0 以上を指定してください。");
            }

            _size = size;
            _bufferA = new NativeArray<float>(size, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            _bufferB = new NativeArray<float>(size, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            _writeIndex = 0; // A が書き込みバッファ、B が読み取りバッファ
        }

        /// <summary>
        /// 書き込みバッファの指定インデックスに値を書き込む。
        /// 受信スレッドから呼び出される。
        /// </summary>
        /// <param name="index">書き込み先のインデックス。</param>
        /// <param name="value">書き込む値。</param>
        public void Write(int index, float value)
        {
            if (index < 0 || index >= _size)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index,
                    $"インデックスは 0〜{_size - 1} の範囲で指定してください。");
            }

            var writeBuffer = GetWriteBuffer();
            writeBuffer[index] = value;
        }

        /// <summary>
        /// バッファをスワップする。フレーム境界でメインスレッドから呼び出す。
        /// スワップ後、旧書き込みバッファが新しい読み取りバッファになり、
        /// 旧読み取りバッファはクリアされて新しい書き込みバッファになる。
        /// </summary>
        public void Swap()
        {
            // アトミックにスワップ（0 → 1, 1 → 0）
            int oldWriteIndex = _writeIndex;
            int newWriteIndex = 1 - oldWriteIndex;
            Interlocked.Exchange(ref _writeIndex, newWriteIndex);

            // 新しい書き込みバッファ（旧読み取りバッファ）をクリア
            var newWriteBuffer = GetWriteBuffer();
            ClearBuffer(newWriteBuffer);
        }

        /// <summary>
        /// 読み取りバッファを取得する。メインスレッドから呼び出す。
        /// </summary>
        /// <returns>読み取り専用の NativeArray。</returns>
        public NativeArray<float>.ReadOnly GetReadBuffer()
        {
            var readBuffer = _writeIndex == 0 ? _bufferB : _bufferA;
            return readBuffer.AsReadOnly();
        }

        /// <summary>
        /// バッファサイズを変更する。
        /// サイズが変更された場合、既存のバッファを解放し新しいサイズで再確保する。
        /// </summary>
        /// <param name="newSize">新しい要素数。0 以上を指定。</param>
        public void Resize(int newSize)
        {
            if (newSize < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(newSize), newSize, "サイズは 0 以上を指定してください。");
            }

            if (newSize == _size)
            {
                return;
            }

            // 既存バッファを解放
            if (_bufferA.IsCreated)
            {
                _bufferA.Dispose();
            }
            if (_bufferB.IsCreated)
            {
                _bufferB.Dispose();
            }

            _size = newSize;
            _bufferA = new NativeArray<float>(newSize, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            _bufferB = new NativeArray<float>(newSize, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            _writeIndex = 0;
        }

        /// <summary>
        /// 両バッファを解放する。OnDisable から呼び出すこと。
        /// </summary>
        public void Dispose()
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

        private NativeArray<float> GetWriteBuffer()
        {
            return _writeIndex == 0 ? _bufferA : _bufferB;
        }

        private static void ClearBuffer(NativeArray<float> buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = 0f;
            }
        }
    }
}
