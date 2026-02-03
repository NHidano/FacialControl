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

            // LayerBlender.LayerInput の配列を構築
            // レイヤー数は少量（通常 3〜5）のためここでの配列確保は問題なし
            // （毎フレーム呼び出しの場合は事前確保した配列を再利用すべきだが、
            //   ComputeOutput は Expression 切り替え等のイベント時に呼ばれる想定）
            var layerInputs = new LayerBlender.LayerInput[_layers.Count];
            var outputBuffer = new float[_blendShapeCount];

            for (int i = 0; i < _layers.Count; i++)
            {
                var entry = _layers[i];
                var layerBehaviour = entry.Playable.GetBehaviour();
                var layerOutput = layerBehaviour.OutputWeights;

                // NativeArray から float[] にコピー
                var values = new float[_blendShapeCount];
                int copyLen = Math.Min(_blendShapeCount, layerOutput.Length);
                for (int j = 0; j < copyLen; j++)
                {
                    values[j] = layerOutput[j];
                }

                layerInputs[i] = new LayerBlender.LayerInput(
                    entry.Priority,
                    entry.Weight,
                    values);
            }

            // Domain サービスでブレンド計算
            LayerBlender.Blend(layerInputs, outputBuffer);

            // layerSlots オーバーライド適用
            if (_activeLayerSlots != null && _activeLayerSlots.Length > 0)
            {
                LayerBlender.ApplyLayerSlotOverrides(_blendShapeNames, _activeLayerSlots, outputBuffer);
            }

            // 結果を NativeArray にコピー
            for (int i = 0; i < _blendShapeCount; i++)
            {
                _outputWeights[i] = outputBuffer[i];
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
