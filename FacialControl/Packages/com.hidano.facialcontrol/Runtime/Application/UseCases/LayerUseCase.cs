using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Application.UseCases
{
    /// <summary>
    /// レイヤーの補間更新と最終 BlendShape 出力の計算を管理するユースケース。
    /// TransitionCalculator / ExclusionResolver / LayerBlender を協調させ、
    /// 全レイヤーの遷移進行・排他解決・優先度ブレンドを統合する。
    /// </summary>
    public class LayerUseCase
    {
        private FacialProfile _profile;
        private readonly ExpressionUseCase _expressionUseCase;
        private string[] _blendShapeNames;
        private readonly Dictionary<string, float> _layerWeights;
        private readonly Dictionary<string, LayerTransitionState> _layerTransitions;

        /// <summary>
        /// レイヤーごとの遷移状態を保持する内部クラス。
        /// </summary>
        private class LayerTransitionState
        {
            /// <summary>遷移経過時間</summary>
            public float ElapsedTime;

            /// <summary>遷移時間</summary>
            public float Duration;

            /// <summary>遷移カーブ</summary>
            public TransitionCurve Curve;

            /// <summary>遷移元スナップショット（from 値）</summary>
            public float[] SnapshotValues;

            /// <summary>遷移先ターゲット値</summary>
            public float[] TargetValues;

            /// <summary>現在の解決済み出力値</summary>
            public float[] CurrentValues;

            /// <summary>遷移が完了済みか</summary>
            public bool IsComplete;

            /// <summary>前回アクティブだった Expression の ID 群（変更検出用）</summary>
            public List<string> PreviousActiveIds;
        }

        public LayerUseCase(FacialProfile profile, ExpressionUseCase expressionUseCase, string[] blendShapeNames)
        {
            if (blendShapeNames == null)
                throw new ArgumentNullException(nameof(blendShapeNames));

            _profile = profile;
            _expressionUseCase = expressionUseCase;
            _blendShapeNames = blendShapeNames;
            _layerWeights = new Dictionary<string, float>();
            _layerTransitions = new Dictionary<string, LayerTransitionState>();
        }

        /// <summary>
        /// レイヤーウェイトを設定する。値は 0〜1 にクランプされる。
        /// </summary>
        /// <param name="layer">レイヤー名</param>
        /// <param name="weight">ウェイト値（0〜1）</param>
        public void SetLayerWeight(string layer, float weight)
        {
            if (layer == null)
                throw new ArgumentNullException(nameof(layer));

            _layerWeights[layer] = Clamp01(weight);
        }

        /// <summary>
        /// 全レイヤーの補間を deltaTime 分だけ進行させる。
        /// アクティブな Expression の変更を検出し、遷移割込を処理する。
        /// </summary>
        /// <param name="deltaTime">経過時間（秒）</param>
        public void UpdateWeights(float deltaTime)
        {
            var activeExpressions = _expressionUseCase.GetActiveExpressions();
            int bsCount = _blendShapeNames.Length;

            if (bsCount == 0)
                return;

            // アクティブな Expression をレイヤー別にグルーピング
            var expressionsByLayer = GroupByLayer(activeExpressions);

            // プロファイルのレイヤー定義を走査
            var layerSpan = _profile.Layers.Span;
            for (int i = 0; i < layerSpan.Length; i++)
            {
                string layerName = layerSpan[i].Name;
                var exclusionMode = layerSpan[i].ExclusionMode;

                if (!expressionsByLayer.TryGetValue(layerName, out var layerExpressions) ||
                    layerExpressions.Count == 0)
                {
                    // このレイヤーにアクティブな Expression がなければスキップ
                    continue;
                }

                var state = GetOrCreateTransitionState(layerName, bsCount);

                // Expression の変更を検出
                bool changed = DetectExpressionChange(state, layerExpressions);

                if (changed)
                {
                    // 遷移割込: 現在の値をスナップショットとして保存
                    ExclusionResolver.TakeSnapshot(state.CurrentValues, state.SnapshotValues);

                    // 新しいターゲット値を計算
                    ComputeTargetValues(layerExpressions, exclusionMode, state.TargetValues, bsCount);

                    // 遷移パラメータを更新（最後にアクティブ化された Expression の遷移設定を使用）
                    var lastExpr = layerExpressions[layerExpressions.Count - 1];
                    state.Duration = lastExpr.TransitionDuration;
                    state.Curve = lastExpr.TransitionCurve;
                    state.ElapsedTime = 0f;
                    state.IsComplete = false;

                    // アクティブ ID リストを更新
                    UpdateActiveIds(state, layerExpressions);
                }

                if (!state.IsComplete)
                {
                    state.ElapsedTime += deltaTime;
                    float weight = TransitionCalculator.ComputeBlendWeight(
                        state.Curve, state.ElapsedTime, state.Duration);

                    ExclusionResolver.ResolveLastWins(
                        state.SnapshotValues, state.TargetValues, weight, state.CurrentValues);

                    if (state.ElapsedTime >= state.Duration)
                    {
                        state.IsComplete = true;
                    }
                }
            }
        }

        /// <summary>
        /// 全レイヤーのブレンド結果を計算し、最終出力 BlendShape 値を返す。
        /// 返されるのは防御的コピーである。
        /// </summary>
        /// <returns>BlendShape ウェイト配列</returns>
        public float[] GetBlendedOutput()
        {
            int bsCount = _blendShapeNames.Length;
            var output = new float[bsCount];

            if (bsCount == 0)
                return output;

            // レイヤー入力を構築
            var layerInputs = new List<LayerBlender.LayerInput>();
            var layerSpan = _profile.Layers.Span;

            for (int i = 0; i < layerSpan.Length; i++)
            {
                string layerName = layerSpan[i].Name;
                int priority = layerSpan[i].Priority;

                if (!_layerTransitions.TryGetValue(layerName, out var state))
                    continue;

                float layerWeight = GetLayerWeight(layerName);

                layerInputs.Add(new LayerBlender.LayerInput(
                    priority, layerWeight, state.CurrentValues));
            }

            if (layerInputs.Count > 0)
            {
                LayerBlender.Blend(layerInputs.ToArray(), output);
            }

            return output;
        }

        /// <summary>
        /// プロファイルを切り替え、遷移状態をリセットする。
        /// </summary>
        /// <param name="profile">新しいプロファイル</param>
        /// <param name="blendShapeNames">新しい BlendShape 名リスト</param>
        public void SetProfile(FacialProfile profile, string[] blendShapeNames)
        {
            _profile = profile;
            _blendShapeNames = blendShapeNames ?? throw new ArgumentNullException(nameof(blendShapeNames));
            _layerTransitions.Clear();
            _layerWeights.Clear();
        }

        private float GetLayerWeight(string layerName)
        {
            if (_layerWeights.TryGetValue(layerName, out float w))
                return w;
            return 1.0f; // デフォルトウェイト
        }

        private LayerTransitionState GetOrCreateTransitionState(string layerName, int bsCount)
        {
            if (!_layerTransitions.TryGetValue(layerName, out var state))
            {
                state = new LayerTransitionState
                {
                    ElapsedTime = 0f,
                    Duration = 0f,
                    Curve = TransitionCurve.Linear,
                    SnapshotValues = new float[bsCount],
                    TargetValues = new float[bsCount],
                    CurrentValues = new float[bsCount],
                    IsComplete = true,
                    PreviousActiveIds = new List<string>()
                };
                _layerTransitions[layerName] = state;
            }
            return state;
        }

        private bool DetectExpressionChange(LayerTransitionState state, List<Expression> currentExpressions)
        {
            if (state.PreviousActiveIds.Count != currentExpressions.Count)
                return true;

            for (int i = 0; i < currentExpressions.Count; i++)
            {
                if (i >= state.PreviousActiveIds.Count ||
                    state.PreviousActiveIds[i] != currentExpressions[i].Id)
                    return true;
            }

            return false;
        }

        private void UpdateActiveIds(LayerTransitionState state, List<Expression> expressions)
        {
            state.PreviousActiveIds.Clear();
            for (int i = 0; i < expressions.Count; i++)
            {
                state.PreviousActiveIds.Add(expressions[i].Id);
            }
        }

        /// <summary>
        /// レイヤー内のアクティブ Expression からターゲット BlendShape 値を計算する。
        /// </summary>
        private void ComputeTargetValues(
            List<Expression> expressions,
            ExclusionMode exclusionMode,
            float[] targetValues,
            int bsCount)
        {
            // ゼロクリア
            Array.Clear(targetValues, 0, bsCount);

            if (exclusionMode == ExclusionMode.LastWins)
            {
                // LastWins: 最後の Expression のみ使用
                var lastExpr = expressions[expressions.Count - 1];
                MapBlendShapeValues(lastExpr, targetValues);
            }
            else
            {
                // Blend: 全 Expression のウェイトを加算
                for (int e = 0; e < expressions.Count; e++)
                {
                    MapBlendShapeValuesAdditive(expressions[e], targetValues);
                }
            }
        }

        /// <summary>
        /// Expression の BlendShape 値を名前ベースでターゲット配列にマッピングする。
        /// </summary>
        private void MapBlendShapeValues(Expression expression, float[] target)
        {
            var bsSpan = expression.BlendShapeValues.Span;
            for (int v = 0; v < bsSpan.Length; v++)
            {
                int idx = FindBlendShapeIndex(bsSpan[v].Name);
                if (idx >= 0)
                {
                    target[idx] = bsSpan[v].Value;
                }
            }
        }

        /// <summary>
        /// Expression の BlendShape 値を加算モードでマッピングする（Blend 用）。
        /// </summary>
        private void MapBlendShapeValuesAdditive(Expression expression, float[] target)
        {
            var bsSpan = expression.BlendShapeValues.Span;
            for (int v = 0; v < bsSpan.Length; v++)
            {
                int idx = FindBlendShapeIndex(bsSpan[v].Name);
                if (idx >= 0)
                {
                    target[idx] = Clamp01(target[idx] + bsSpan[v].Value);
                }
            }
        }

        private int FindBlendShapeIndex(string name)
        {
            for (int i = 0; i < _blendShapeNames.Length; i++)
            {
                if (_blendShapeNames[i] == name)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// アクティブな Expression を有効レイヤー名でグルーピングする。
        /// </summary>
        private Dictionary<string, List<Expression>> GroupByLayer(List<Expression> expressions)
        {
            var grouped = new Dictionary<string, List<Expression>>();

            for (int i = 0; i < expressions.Count; i++)
            {
                string effectiveLayer = _profile.GetEffectiveLayer(expressions[i]);

                if (!grouped.TryGetValue(effectiveLayer, out var list))
                {
                    list = new List<Expression>();
                    grouped[effectiveLayer] = list;
                }
                list.Add(expressions[i]);
            }

            return grouped;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
