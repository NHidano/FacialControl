using System;
using Unity.Collections;
using UnityEngine.Playables;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.Playable
{
    /// <summary>
    /// OscDoubleBuffer からの値を読み取り、PlayableGraph へ統合する ScriptPlayable。
    /// フレームごとに ReadFromBuffer を呼び出すことで、OSC 受信値を BlendShape ウェイトとして出力する。
    /// </summary>
    public class OscReceiverPlayable : PlayableBehaviour, IDisposable
    {
        private OscDoubleBuffer _buffer;
        private int _blendShapeCount;
        private int _mappingCount;

        // 出力バッファ
        private NativeArray<float> _outputWeights;

        private bool _disposed;

        /// <summary>
        /// 出力 BlendShape ウェイト配列（読み取り専用）。
        /// </summary>
        public NativeArray<float> OutputWeights => _outputWeights;

        /// <summary>
        /// BlendShape 数。
        /// </summary>
        public int BlendShapeCount => _blendShapeCount;

        /// <summary>
        /// OscReceiverPlayable を生成し、PlayableGraph に追加する。
        /// </summary>
        /// <param name="graph">PlayableGraph</param>
        /// <param name="buffer">OSC 受信用ダブルバッファ</param>
        /// <param name="mappings">OSC マッピング配列（バッファインデックスとの対応）</param>
        /// <param name="blendShapeCount">出力 BlendShape 数</param>
        /// <returns>生成された ScriptPlayable</returns>
        public static ScriptPlayable<OscReceiverPlayable> Create(
            PlayableGraph graph,
            OscDoubleBuffer buffer,
            OscMapping[] mappings,
            int blendShapeCount)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (mappings == null)
                throw new ArgumentNullException(nameof(mappings));

            var playable = ScriptPlayable<OscReceiverPlayable>.Create(graph);
            var behaviour = playable.GetBehaviour();
            behaviour.Initialize(buffer, mappings, blendShapeCount);
            return playable;
        }

        private void Initialize(OscDoubleBuffer buffer, OscMapping[] mappings, int blendShapeCount)
        {
            _buffer = buffer;
            _blendShapeCount = blendShapeCount;
            _mappingCount = mappings.Length;

            if (blendShapeCount > 0)
            {
                _outputWeights = new NativeArray<float>(blendShapeCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            }
            else
            {
                _outputWeights = new NativeArray<float>(0, Allocator.Persistent);
            }
        }

        /// <summary>
        /// OscDoubleBuffer の読み取りバッファから値を読み取り、OutputWeights に反映する。
        /// フレームごとにメインスレッドから呼び出す。
        /// </summary>
        public void ReadFromBuffer()
        {
            if (_blendShapeCount == 0 || _buffer == null)
                return;

            var readBuffer = _buffer.GetReadBuffer();
            int readCount = Math.Min(Math.Min(_blendShapeCount, readBuffer.Length), _mappingCount);

            for (int i = 0; i < readCount; i++)
            {
                float value = readBuffer[i];
                // 0〜1 にクランプ
                _outputWeights[i] = value > 1f ? 1f : (value < 0f ? 0f : value);
            }
        }

        /// <summary>
        /// NativeArray リソースを解放する。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            if (_outputWeights.IsCreated)
            {
                _outputWeights.Dispose();
            }

            _disposed = true;
        }

        public override void OnPlayableDestroy(UnityEngine.Playables.Playable playable)
        {
            Dispose();
        }
    }
}
