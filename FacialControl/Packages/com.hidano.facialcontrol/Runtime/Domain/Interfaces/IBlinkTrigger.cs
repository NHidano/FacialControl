using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Domain.Interfaces
{
    /// <summary>
    /// 自動まばたきのトリガー判定インターフェース。
    /// ユーザーが独自の実装を提供可能。
    /// 実装は preview.2 で提供予定。
    /// </summary>
    public interface IBlinkTrigger
    {
        /// <summary>
        /// まばたきをトリガーすべきかを判定する。
        /// </summary>
        /// <param name="deltaTime">前フレームからの経過時間（秒）</param>
        /// <param name="currentState">現在の表情状態</param>
        /// <returns>まばたきをトリガーする場合は true</returns>
        bool ShouldBlink(float deltaTime, in FacialState currentState);
    }
}
