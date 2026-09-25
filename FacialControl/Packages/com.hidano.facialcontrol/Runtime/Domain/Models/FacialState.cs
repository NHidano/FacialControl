using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// 現在のアクティブ Expression 情報を保持する構造体。
    /// IBlinkTrigger の引数として使用され、まばたき判定に必要な表情状態を提供する。
    /// </summary>
    public readonly struct FacialState
    {
        /// <summary>
        /// アクティブな Expression の ID（GUID）。
        /// Expression が未設定の場合は null。
        /// </summary>
        public string ActiveExpressionId { get; }

        /// <summary>
        /// アクティブな Expression の名前。
        /// Expression が未設定の場合は null。
        /// </summary>
        public string ActiveExpressionName { get; }

        /// <summary>
        /// アクティブな Expression が所属するレイヤー名。
        /// Expression が未設定の場合は null。
        /// </summary>
        public string ActiveLayer { get; }

        /// <summary>
        /// 現在の遷移進行度（0〜1）。
        /// 0 は遷移開始、1 は遷移完了を表す。
        /// 遷移中でない場合は 1。
        /// </summary>
        public float TransitionProgress { get; }

        /// <summary>
        /// Expression がアクティブかどうか。
        /// </summary>
        public bool HasActiveExpression { get; }

        /// <summary>
        /// アクティブな Expression 情報を持つ状態を生成する。
        /// </summary>
        /// <param name="activeExpressionId">アクティブな Expression の ID</param>
        /// <param name="activeExpressionName">アクティブな Expression の名前</param>
        /// <param name="activeLayer">アクティブな Expression のレイヤー名</param>
        /// <param name="transitionProgress">遷移進行度（0〜1、範囲外は自動クランプ）</param>
        public FacialState(
            string activeExpressionId,
            string activeExpressionName,
            string activeLayer,
            float transitionProgress = 1f)
        {
            ActiveExpressionId = activeExpressionId;
            ActiveExpressionName = activeExpressionName;
            ActiveLayer = activeLayer;
            TransitionProgress = Math.Clamp(transitionProgress, 0f, 1f);
            HasActiveExpression = activeExpressionId != null;
        }
    }
}
