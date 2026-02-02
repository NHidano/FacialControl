using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Application.UseCases
{
    /// <summary>
    /// Expression のアクティブ化・非アクティブ化を管理するユースケース。
    /// レイヤーの排他モード（LastWins / Blend）に基づいて
    /// アクティブな Expression リストを管理する。
    /// </summary>
    public class ExpressionUseCase
    {
        private FacialProfile _profile;
        private readonly Dictionary<string, List<Expression>> _activeByLayer;

        public ExpressionUseCase(FacialProfile profile)
        {
            _profile = profile;
            _activeByLayer = new Dictionary<string, List<Expression>>();
        }

        /// <summary>
        /// Expression をアクティブ化する。
        /// レイヤーの排他モードに基づき、LastWins の場合は既存の Expression を置き換え、
        /// Blend の場合は追加する。
        /// </summary>
        /// <param name="expression">アクティブ化する Expression</param>
        public void Activate(Expression expression)
        {
            string effectiveLayer = _profile.GetEffectiveLayer(expression);
            var exclusionMode = GetExclusionMode(effectiveLayer);

            if (!_activeByLayer.TryGetValue(effectiveLayer, out var layerExpressions))
            {
                layerExpressions = new List<Expression>();
                _activeByLayer[effectiveLayer] = layerExpressions;
            }

            // 同一 ID の既存 Expression を除去
            RemoveById(layerExpressions, expression.Id);

            if (exclusionMode == ExclusionMode.LastWins)
            {
                // LastWins: 既存を全て置き換え
                layerExpressions.Clear();
                layerExpressions.Add(expression);
            }
            else
            {
                // Blend: 追加
                layerExpressions.Add(expression);
            }
        }

        /// <summary>
        /// Expression を非アクティブ化する。
        /// アクティブリストから ID が一致する Expression を削除する。
        /// </summary>
        /// <param name="expression">非アクティブ化する Expression</param>
        public void Deactivate(Expression expression)
        {
            foreach (var layerExpressions in _activeByLayer.Values)
            {
                RemoveById(layerExpressions, expression.Id);
            }
        }

        /// <summary>
        /// 現在アクティブな全 Expression のリストを返す。
        /// 返されるリストは防御的コピーであり、変更しても内部状態に影響しない。
        /// </summary>
        /// <returns>アクティブな Expression のリスト</returns>
        public List<Expression> GetActiveExpressions()
        {
            var result = new List<Expression>();
            foreach (var layerExpressions in _activeByLayer.Values)
            {
                result.AddRange(layerExpressions);
            }
            return result;
        }

        /// <summary>
        /// プロファイルを切り替える。アクティブな Expression は全てクリアされる。
        /// </summary>
        /// <param name="profile">新しいプロファイル</param>
        public void SetProfile(FacialProfile profile)
        {
            _profile = profile;
            _activeByLayer.Clear();
        }

        private ExclusionMode GetExclusionMode(string layerName)
        {
            var layer = _profile.FindLayerByName(layerName);
            if (layer.HasValue)
                return layer.Value.ExclusionMode;

            // レイヤー未定義の場合はデフォルトで LastWins
            return ExclusionMode.LastWins;
        }

        private static void RemoveById(List<Expression> expressions, string id)
        {
            for (int i = expressions.Count - 1; i >= 0; i--)
            {
                if (expressions[i].Id == id)
                {
                    expressions.RemoveAt(i);
                }
            }
        }
    }
}
