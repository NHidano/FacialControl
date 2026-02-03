using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine.Playables;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Adapters.Playable
{
    /// <summary>
    /// PlayableGraph のルートノードとして機能する ScriptPlayable。
    /// 複数の LayerPlayable からの出力をレイヤー優先度に基づいてブレンドし、
    /// layerSlots によるオーバーライドを適用して最終出力を生成する。
    /// </summary>
    public class FacialControlMixer : PlayableBehaviour, IDisposable
    {
        private int _blendShapeCount;
        private string[] _blendShapeNames;

        // 最終出力バッファ
        private NativeArray<float> _outputWeights;

        // 登録レイヤー情報
        private readonly List<LayerEntry> _layers = new List<LayerEntry>();

        // アクティブな layerSlots（Expression のオーバーライド用）
        private LayerSlot[] _activeLayerSlots;

        // ComputeOutput 用の事前確保バッファ
        private LayerBlender.LayerInput[] _layerInputBuffer;
        private float[] _outputBuffer;
        private float[][] _layerValueBuffers;

        private bool _disposed;

        /// <summary>
        /// 最終出力 BlendShape ウェイト配列（読み取り専用）。
        /// </summary>
        public NativeArray<float> OutputWeights => _outputWeights;

        /// <summary>
        /// BlendShape 数。
        /// </summary>
        public int BlendShapeCount => _blendShapeCount;

        /// <summary>
        /// 登録済みレイヤー数。
        /// </summary>
        public int LayerCount => _layers.Count;

        /// <summary>
        /// BlendShape 名のリスト。
        /// </summary>
        public ReadOnlySpan<string> BlendShapeNames => _blendShapeNames;

        /// <summary>
        /// FacialControlMixer を生成し、PlayableGraph に追加する。
        /// </summary>
        /// <param name="graph">PlayableGraph</param>
        /// <param name="blendShapeNames">全 BlendShape 名のリスト</param>
        /// <returns>生成された ScriptPlayable</returns>
        public static ScriptPlayable<FacialControlMixer> Create(
            PlayableGraph graph,
            string[] blendShapeNames)
        {
            var playable = ScriptPlayable<FacialControlMixer>.Create(graph);
            var behaviour = playable.GetBehaviour();
            behaviour.Initialize(blendShapeNames);
            return playable;
        }

        private void Initialize(string[] blendShapeNames)
        {
            _blendShapeNames = blendShapeNames ?? Array.Empty<string>();
            _blendShapeCount = _blendShapeNames.Length;

            if (_blendShapeCount > 0)
            {
                _outputWeights = new NativeArray<float>(_blendShapeCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            }
            else
            {
                _outputWeights = new NativeArray<float>(0, Allocator.Persistent);
            }
        }

        /// <summary>
        /// レイヤーを登録する。
        /// </summary>
        /// <param name="layerName">レイヤー名</param>
        /// <param name="priority">優先度（値が大きいほど優先）</param>
        /// <param name="weight">レイヤーウェイト（0〜1）</param>
        /// <param name="layerPlayable">対応する LayerPlayable</param>
        public void RegisterLayer(string layerName, int priority, float weight, ScriptPlayable<LayerPlayable> layerPlayable)
        {
            _layers.Add(new LayerEntry(layerName, priority, weight, layerPlayable));
        }

        /// <summary>
        /// レイヤーウェイトを設定する。
        /// </summary>
        /// <param name="layerName">レイヤー名</param>
        /// <param name="weight">新しいウェイト（0〜1）</param>
        public void SetLayerWeight(string layerName, float weight)
        {
            for (int i = 0; i < _layers.Count; i++)
            {
                if (_layers[i].Name == layerName)
                {
                    _layers[i] = new LayerEntry(
                        _layers[i].Name,
                        _layers[i].Priority,
                        weight,
                        _layers[i].Playable);
                    return;
                }
            }
        }

        /// <summary>
        /// アクティブな layerSlots を設定する。
        /// Expression がアクティブになった際にオーバーライド値を反映するために使用する。
        /// </summary>
        /// <param name="slots">適用する LayerSlot 配列。null の場合はオーバーライドなし。</param>
        public void SetActiveLayerSlots(LayerSlot[] slots)
        {
            _activeLayerSlots = slots;
        }

        /// <summary>
        /// アクティブな layerSlots をクリアする。
        /// </summary>
        public void ClearActiveLayerSlots()
        {
            _activeLayerSlots = null;
        }

        /// <summary>
        /// PlayableGraph のフレーム準備コールバック。
        /// 毎フレーム全レイヤーの遷移を進め、最終出力を計算する。
        /// </summary>
        public override void PrepareFrame(UnityEngine.Playables.Playable playable, FrameData info)
        {
            float deltaTime = info.deltaTime;

            // 全レイヤーの遷移を進める
            for (int i = 0; i < _layers.Count; i++)
            {
                var behaviour = _layers[i].Playable.GetBehaviour();
                behaviour.UpdateTransition(deltaTime);
            }

            // レイヤー出力をブレンドして最終出力を計算
            ComputeOutput();
        }

        /// <summary>
        /// 全レイヤーの出力をブレンドし、layerSlots オーバーライドを適用して最終出力を計算する。
        /// </summary>
        public void ComputeOutput()
        {
            if (_blendShapeCount == 0)
            {
                return;
            }

            if (_layers.Count == 0)
            {
                // レイヤーなし: 出力をゼロに
                for (int i = 0; i < _blendShapeCount; i++)
                {
                    _outputWeights[i] = 0f;
                }
                return;
            }

            // 事前確保バッファのサイズが合わなければ再確保
            EnsureBuffers();

            for (int i = 0; i < _layers.Count; i++)
            {
                var entry = _layers[i];
                var layerBehaviour = entry.Playable.GetBehaviour();
                var layerOutput = layerBehaviour.OutputWeights;

                // NativeArray から事前確保 float[] にコピー
                var values = _layerValueBuffers[i];
                int copyLen = Math.Min(_blendShapeCount, layerOutput.Length);
                for (int j = 0; j < copyLen; j++)
                {
                    values[j] = layerOutput[j];
                }
                // 残りをゼロクリア
                for (int j = copyLen; j < _blendShapeCount; j++)
                {
                    values[j] = 0f;
                }

                _layerInputBuffer[i] = new LayerBlender.LayerInput(
                    entry.Priority,
                    entry.Weight,
                    values);
            }

            // 出力バッファをゼロクリア
            Array.Clear(_outputBuffer, 0, _blendShapeCount);

            // Domain サービスでブレンド計算
            LayerBlender.Blend(_layerInputBuffer, _outputBuffer);

            // layerSlots オーバーライド適用
            if (_activeLayerSlots != null && _activeLayerSlots.Length > 0)
            {
                LayerBlender.ApplyLayerSlotOverrides(_blendShapeNames, _activeLayerSlots, _outputBuffer);
            }

            // 結果を NativeArray にコピー
            for (int i = 0; i < _blendShapeCount; i++)
            {
                _outputWeights[i] = _outputBuffer[i];
            }
        }

        /// <summary>
        /// ComputeOutput 用の内部バッファを確保・再確保する。
        /// レイヤー数や BlendShape 数が変わった場合のみ再確保する。
        /// </summary>
        private void EnsureBuffers()
        {
            int layerCount = _layers.Count;

            if (_outputBuffer == null || _outputBuffer.Length < _blendShapeCount)
            {
                _outputBuffer = new float[_blendShapeCount];
            }

            if (_layerInputBuffer == null || _layerInputBuffer.Length < layerCount)
            {
                _layerInputBuffer = new LayerBlender.LayerInput[layerCount];
            }

            if (_layerValueBuffers == null || _layerValueBuffers.Length < layerCount)
            {
                var newBuffers = new float[layerCount][];
                if (_layerValueBuffers != null)
                {
                    Array.Copy(_layerValueBuffers, newBuffers, _layerValueBuffers.Length);
                }
                _layerValueBuffers = newBuffers;
            }

            for (int i = 0; i < layerCount; i++)
            {
                if (_layerValueBuffers[i] == null || _layerValueBuffers[i].Length < _blendShapeCount)
                {
                    _layerValueBuffers[i] = new float[_blendShapeCount];
                }
            }
        }

        /// <summary>
        /// NativeArray リソースを解放する。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_outputWeights.IsCreated)
            {
                _outputWeights.Dispose();
            }

            _layers.Clear();
            _activeLayerSlots = null;
            _disposed = true;
        }

        public override void OnPlayableDestroy(UnityEngine.Playables.Playable playable)
        {
            Dispose();
        }

        /// <summary>
        /// レイヤー登録情報。
        /// </summary>
        private struct LayerEntry
        {
            public string Name;
            public int Priority;
            public float Weight;
            public ScriptPlayable<LayerPlayable> Playable;

            public LayerEntry(string name, int priority, float weight, ScriptPlayable<LayerPlayable> playable)
            {
                Name = name;
                Priority = priority;
                Weight = weight;
                Playable = playable;
            }
        }
    }
}
