namespace Hidano.FacialControl.Domain.Interfaces
{
    /// <summary>
    /// ゲーム内の <c>Time.timeScale</c> に影響されない経過秒数を提供する時刻抽象。
    /// Domain 層に配置することで、Unity API 非依存の純粋ロジックから時刻を参照できる。
    /// 実装は Adapters 層の <c>UnityTimeProvider</c>、テストは Tests/Shared の
    /// <c>ManualTimeProvider</c> を使い分ける（EditMode での決定論化）。
    /// </summary>
    public interface ITimeProvider
    {
        /// <summary>
        /// 単調増加する経過秒数。<c>Time.timeScale</c> の影響を受けない。
        /// </summary>
        double UnscaledTimeSeconds { get; }
    }
}
