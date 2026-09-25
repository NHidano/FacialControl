using System.Collections.Generic;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Application.UseCases
{
    /// <summary>
    /// Expression のアクティブ化・非アクティブ化を管理するユースケース。
    /// レイヤーの排他モード（LastWins / Blend）に基づいて
    /// アクティブな Expression リストを管理する。
    /// </summary>
    public class ExpressionUseCase : IActiveExpressionProvider
    {
        private FacialProfile _profile;
        private readonly Dictionary<string, List<Expression>> _activeByLayer;
        private ExpressionResolver _resolver;

        /// <summary>
        /// 紐付けられた <see cref="ExpressionResolver"/>（未注入の場合 null）。
        /// </summary>
        public ExpressionResolver Resolver => _resolver;

        /// <summary>
        /// ExpressionUseCase を生成する。
        /// </summary>
        /// <param name="profile">対象の表情設定プロファイル</param>
        public ExpressionUseCase(FacialProfile profile)
            : this(profile, resolver: null)
        {
        }

        /// <summary>
        /// ExpressionUseCase を生成する（<see cref="ExpressionResolver"/> 注入版）。
        /// </summary>
        /// <param name="profile">対象の表情設定プロファイル</param>
        /// <param name="resolver">SnapshotId → 値解決サービス（null 許容）</param>
        public ExpressionUseCase(FacialProfile profile, ExpressionResolver resolver)
        {
            _profile = profile;
            _activeByLayer = new Dictionary<string, List<Expression>>();
            _resolver = resolver;
        }

        /// <summary>
        /// <see cref="ExpressionResolver"/> を後付けで注入する。
        /// </summary>
        /// <param name="resolver">SnapshotId → 値解決サービス（null で解除）</param>
        public void SetResolver(ExpressionResolver resolver)
        {
            _resolver = resolver;
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
            CollectActiveExpressions(result);
            return result;
        }

        /// <summary>
        /// 現在アクティブな全 Expression を、呼び出し側が用意したバッファへ収集する。
        /// バッファは呼出開始時にクリアされ、新規 List は確保しない。
        /// </summary>
        /// <param name="buffer">収集先バッファ</param>
        public void CollectActiveExpressions(List<Expression> buffer)
        {
            if (buffer == null)
                throw new System.ArgumentNullException(nameof(buffer));

            buffer.Clear();
            foreach (var layerExpressions in _activeByLayer.Values)
            {
                buffer.AddRange(layerExpressions);
            }
        }

        /// <inheritdoc />
        public Expression? TryGetTopActiveExpression(string layerName)
        {
            if (string.IsNullOrEmpty(layerName))
            {
                return null;
            }
            if (!_activeByLayer.TryGetValue(layerName, out var list) || list == null || list.Count == 0)
            {
                return null;
            }
            return list[list.Count - 1];
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
