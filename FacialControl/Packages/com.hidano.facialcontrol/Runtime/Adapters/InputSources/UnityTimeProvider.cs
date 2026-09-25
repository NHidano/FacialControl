using Hidano.FacialControl.Domain.Interfaces;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.InputSources
{
    /// <summary>
    /// <see cref="ITimeProvider"/> の本番実装。<c>UnityEngine.Time.unscaledTimeAsDouble</c>
    /// をそのまま返す薄いラッパー。<c>Time.timeScale</c> の影響を受けない経過秒数を返すため、
    /// OSC staleness 判定や verbose ログのレートリミットに用いる。
    /// プロセス内では単一インスタンスを DI 経由で共有する想定。
    /// </summary>
    public sealed class UnityTimeProvider : ITimeProvider
    {
        public double UnscaledTimeSeconds => Time.unscaledTimeAsDouble;
    }
}
