using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine.Playables;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Adapters.Playable
{
    /// <summary>
    /// レイヤー単位の表情制御を行う ScriptPlayable。
    /// NativeArray ベースの補間計算、スナップショットバッファによる遷移割込、
    /// LastWins / Blend 排他モード処理を行う。
    /// </summary>
    public class LayerPlayable : PlayableBehaviour, IDisposable
    {
        private int _blendShapeCount;
        private ExclusionMode _exclusionMode;

        // 出力バッファ
        private NativeArray<float> _outputWeights;

        // LastWins 用: 遷移元（スナップショット）と遷移先
        private NativeArray<float> _snapshotBuffer;
        private NativeArray<float> _targetBuffer;

        // 遷移状態
        private float _transitionDuration;
        private float _elapsedTime;
        private TransitionCurve _transitionCurve;
        private bool _isTransitioning;
        private string _activeExpressionId;

        // Blend 用: アクティブな Expression のウェイト情報
        private readonly List<BlendEntry> _blendEntries = new List<BlendEntry>();

        private bool _disposed;

        /// <summary>
        /// 出力 BlendShape ウェイト配列（読み取り専用）。
        /// </summary>
        public NativeArray<float> OutputWeights => _outputWeights;

        /// <summary>
        /// 遷移中かどうか。
        /// </summary>
        public bool IsTransitioning => _isTransitioning;

        /// <summary>
        /// 現在アクティブな Expression の ID。未設定の場合は null。
        /// </summary>
        public string ActiveExpressionId => _activeExpressionId;

        /// <summary>
        /// BlendShape 数。
        /// </summary>
        public int BlendShapeCount => _blendShapeCount;

        /// <summary>
        /// 排他モード。
        /// </summary>
        public ExclusionMode ExclusionMode => _exclusionMode;

        /// <summary>
        /// LayerPlayable を生成し、PlayableGraph に追加する。
        /// </summary>
        /// <param name="graph">PlayableGraph</param>
        /// <param name="blendShapeCount">BlendShape 数</param>
        /// <param name="exclusionMode">排他モード</param>
        /// <returns>生成された ScriptPlayable</returns>
        public static ScriptPlayable<LayerPlayable> Create(
            PlayableGraph graph,
            int blendShapeCount,
            ExclusionMode exclusionMode)
        {
            var playable = ScriptPlayable<LayerPlayable>.Create(graph);
            var behaviour = playable.GetBehaviour();
            behaviour.Initialize(blendShapeCount, exclusionMode);
            return playable;
        }

        private void Initialize(int blendShapeCount, ExclusionMode exclusionMode)
        {
            _blendShapeCount = blendShapeCount;
            _exclusionMode = exclusionMode;

            if (blendShapeCount > 0)
            {
                _outputWeights = new NativeArray<float>(blendShapeCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                _snapshotBuffer = new NativeArray<float>(blendShapeCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                _targetBuffer = new NativeArray<float>(blendShapeCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            }
            else
            {
                _outputWeights = new NativeArray<float>(0, Allocator.Persistent);
                _snapshotBuffer = new NativeArray<float>(0, Allocator.Persistent);
                _targetBuffer = new NativeArray<float>(0, Allocator.Persistent);
            }

            _isTransitioning = false;
            _activeExpressionId = null;
        }

        /// <summary>
        /// LastWins モードでターゲット Expression を設定する。
        /// 遷移中に呼ばれた場合は現在の出力値をスナップショットし、新しい遷移を開始する。
        /// </summary>
        /// <param name="expressionId">Expression ID</param>
        /// <param name="targetValues">ターゲット BlendShape 値</param>
        /// <param name="duration">遷移時間（秒）</param>
        /// <param name="curve">遷移カーブ</param>
        public void SetTargetExpression(string expressionId, float[] targetValues, float duration, TransitionCurve curve)
        {
            _activeExpressionId = expressionId;

            // 現在の出力値をスナップショットにコピー
            if (_blendShapeCount > 0)
            {
                _outputWeights.CopyTo(_snapshotBuffer);

                // ターゲット値を設定
                for (int i = 0; i < _blendShapeCount && i < targetValues.Length; i++)
                {
                    _targetBuffer[i] = targetValues[i];
                }
            }

            _transitionDuration = duration;
            _transitionCurve = curve;
            _elapsedTime = 0f;

            // 遷移時間 0 の場合は即座に切り替え
            if (duration <= 0f)
            {
                _isTransitioning = false;
                if (_blendShapeCount > 0)
                {
                    _targetBuffer.CopyTo(_outputWeights);
                }
            }
            else
            {
                _isTransitioning = true;
            }
        }

        /// <summary>
        /// 遷移を指定した deltaTime 分進める。
        /// </summary>
        /// <param name="deltaTime">経過時間（秒）</param>
        public void UpdateTransition(float deltaTime)
        {
            if (!_isTransitioning || _blendShapeCount == 0)
            {
                return;
            }

            _elapsedTime += deltaTime;

            float weight = TransitionCalculator.ComputeBlendWeight(
                in _transitionCurve,
                _elapsedTime,
                _transitionDuration);

            // スナップショット（from）→ ターゲット（to）のクロスフェード
            // NativeArray の要素アクセスで GC フリー補間
            for (int i = 0; i < _blendShapeCount; i++)
            {
                float from = _snapshotBuffer[i];
                float to = _targetBuffer[i];
                float result = from + (to - from) * weight;
                _outputWeights[i] = result > 1f ? 1f : (result < 0f ? 0f : result);
            }

            // 遷移完了判定
            if (_elapsedTime >= _transitionDuration)
            {
                _isTransitioning = false;
            }
        }

        /// <summary>
        /// Blend モードで Expression を追加する。
        /// </summary>
        /// <param name="expressionId">Expression ID</param>
        /// <param name="values">BlendShape 値</param>
        /// <param name="weight">ブレンドウェイト</param>
        public void AddBlendExpression(string expressionId, float[] values, float weight)
        {
            // 既存エントリの更新
            for (int i = 0; i < _blendEntries.Count; i++)
            {
                if (_blendEntries[i].ExpressionId == expressionId)
                {
                    _blendEntries[i] = new BlendEntry(expressionId, values, weight);
                    return;
                }
            }

            _blendEntries.Add(new BlendEntry(expressionId, values, weight));
        }

        /// <summary>
        /// Blend モードから Expression を削除する。
        /// </summary>
        /// <param name="expressionId">Expression ID</param>
        public void RemoveBlendExpression(string expressionId)
        {
            for (int i = _blendEntries.Count - 1; i >= 0; i--)
            {
                if (_blendEntries[i].ExpressionId == expressionId)
                {
                    _blendEntries.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>
        /// Blend モードの全 Expression から最終出力を計算する。
        /// </summary>
        public void ComputeBlendOutput()
        {
            if (_blendShapeCount == 0)
            {
                return;
            }

            // 出力をゼロクリア
            for (int i = 0; i < _blendShapeCount; i++)
            {
                _outputWeights[i] = 0f;
            }

            // 各 Expression のウェイト付き値を加算
            for (int entryIndex = 0; entryIndex < _blendEntries.Count; entryIndex++)
            {
                var entry = _blendEntries[entryIndex];
                var values = entry.Values;
                float weight = entry.Weight;

                for (int i = 0; i < _blendShapeCount && i < values.Length; i++)
                {
                    float newVal = _outputWeights[i] + values[i] * weight;
                    _outputWeights[i] = newVal > 1f ? 1f : (newVal < 0f ? 0f : newVal);
                }
            }
        }

        /// <summary>
        /// 現在の Expression を非アクティブ化し、ゼロへ遷移する。
        /// </summary>
        /// <param name="duration">遷移時間（秒）。0 の場合は即座にゼロ。</param>
        public void Deactivate(float duration)
        {
            if (_blendShapeCount == 0)
            {
                _activeExpressionId = null;
                _isTransitioning = false;
                return;
            }

            // 現在の出力をスナップショット
            _outputWeights.CopyTo(_snapshotBuffer);

            // ターゲットをゼロに設定
            for (int i = 0; i < _blendShapeCount; i++)
            {
                _targetBuffer[i] = 0f;
            }

            _activeExpressionId = null;
            _transitionDuration = duration;
            _transitionCurve = TransitionCurve.Linear;
            _elapsedTime = 0f;

            if (duration <= 0f)
            {
                _isTransitioning = false;
                for (int i = 0; i < _blendShapeCount; i++)
                {
                    _outputWeights[i] = 0f;
                }
            }
            else
            {
                _isTransitioning = true;
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
            if (_snapshotBuffer.IsCreated)
            {
                _snapshotBuffer.Dispose();
            }
            if (_targetBuffer.IsCreated)
            {
                _targetBuffer.Dispose();
            }

            _blendEntries.Clear();
            _disposed = true;
        }

        public override void OnPlayableDestroy(UnityEngine.Playables.Playable playable)
        {
            Dispose();
        }

        /// <summary>
        /// Blend モードのエントリ。
        /// </summary>
        private struct BlendEntry
        {
            public string ExpressionId;
            public float[] Values;
            public float Weight;

            public BlendEntry(string expressionId, float[] values, float weight)
            {
                ExpressionId = expressionId;
                Values = values;
                Weight = weight;
            }
        }
    }
}
