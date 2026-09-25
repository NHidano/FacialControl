using System;
using System.Collections.Generic;
using Unity.Collections;

namespace Hidano.FacialControl.Adapters.Playable
{
    /// <summary>
    /// NativeArray のプール管理クラス。
    /// Allocator.Persistent で事前確保し、毎フレーム再利用することで GC フリーを実現する。
    /// BlendShape 総数に基づくサイズで NativeArray を確保し、OnDisable 時に解放する。
    /// </summary>
    public class NativeArrayPool<T> : IDisposable where T : struct
    {
        private readonly List<NativeArray<T>> _available = new List<NativeArray<T>>();
        private readonly List<NativeArray<T>> _outstanding = new List<NativeArray<T>>();
        private int _size;
        private bool _disposed;

        /// <summary>
        /// 現在のプールサイズ（NativeArray の要素数）。
        /// </summary>
        public int Size => _size;

        /// <summary>
        /// 指定サイズで NativeArrayPool を初期化する。
        /// </summary>
        /// <param name="size">各 NativeArray の要素数（BlendShape 総数）。0 以上を指定。</param>
        public NativeArrayPool(int size)
        {
            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, "サイズは 0 以上を指定してください。");
            }

            _size = size;
        }

        /// <summary>
        /// プールから NativeArray を取得する。
        /// 利用可能なバッファがあれば再利用し、なければ新規確保する。
        /// 返却された NativeArray はゼロクリア済み。
        /// </summary>
        public NativeArray<T> Allocate()
        {
            if (_available.Count > 0)
            {
                int lastIndex = _available.Count - 1;
                var array = _available[lastIndex];
                _available.RemoveAt(lastIndex);

                // ゼロクリア
                ClearArray(array);

                _outstanding.Add(array);
                return array;
            }

            var newArray = new NativeArray<T>(_size, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            _outstanding.Add(newArray);
            return newArray;
        }

        /// <summary>
        /// NativeArray をプールに返却する。
        /// 返却されたバッファは次回の Allocate で再利用される。
        /// </summary>
        public void Return(NativeArray<T> array)
        {
            _outstanding.Remove(array);
            _available.Add(array);
        }

        /// <summary>
        /// プールのサイズを変更する。
        /// サイズが変更された場合、既存のプール内バッファを全て解放し、新しいサイズで再確保する。
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

            // 利用可能バッファを解放
            for (int i = 0; i < _available.Count; i++)
            {
                if (_available[i].IsCreated)
                {
                    _available[i].Dispose();
                }
            }
            _available.Clear();

            // 未返却バッファも解放
            for (int i = 0; i < _outstanding.Count; i++)
            {
                if (_outstanding[i].IsCreated)
                {
                    _outstanding[i].Dispose();
                }
            }
            _outstanding.Clear();

            _size = newSize;
        }

        /// <summary>
        /// プールが管理する全ての NativeArray を解放する。
        /// OnDisable から呼び出すこと。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            for (int i = 0; i < _available.Count; i++)
            {
                if (_available[i].IsCreated)
                {
                    _available[i].Dispose();
                }
            }
            _available.Clear();

            for (int i = 0; i < _outstanding.Count; i++)
            {
                if (_outstanding[i].IsCreated)
                {
                    _outstanding[i].Dispose();
                }
            }
            _outstanding.Clear();

            _disposed = true;
        }

        private static void ClearArray(NativeArray<T> array)
        {
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = default;
            }
        }
    }
}
