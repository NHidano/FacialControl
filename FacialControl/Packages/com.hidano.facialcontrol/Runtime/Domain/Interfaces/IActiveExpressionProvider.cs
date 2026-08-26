using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Domain.Interfaces
{
    /// <summary>
    /// 指定レイヤーで現在 active な「top」Expression を提供する読取専用 interface。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Overlay 解決経路（<c>OverlayInputSource</c>）が emotion レイヤーの active 表情を毎フレーム参照するために用いる。
    /// 実装は <see cref="Hidano.FacialControl.Application.UseCases.ExpressionUseCase"/> が担当する。
    /// Domain → Application 方向の逆参照を避けるため、Domain 層に interface のみ配置する。
    /// </para>
    /// <para>
    /// 「top」の定義はレイヤーの <see cref="ExclusionMode"/> 依存:
    /// <list type="bullet">
    ///   <item><see cref="ExclusionMode.LastWins"/> なら唯一の active 表情。</item>
    ///   <item><see cref="ExclusionMode.Blend"/> なら最も最後に Activate された表情（preview スコープでは LastWins 前提運用）。</item>
    /// </list>
    /// </para>
    /// </remarks>
    public interface IActiveExpressionProvider
    {
        /// <summary>
        /// 指定レイヤーで現在 active な top Expression を返す。
        /// active 表情がない、または当該レイヤーが空の場合は <c>null</c>。
        /// </summary>
        /// <param name="layerName">対象レイヤー名（null / 空文字でも例外を投げず null を返す）。</param>
        Expression? TryGetTopActiveExpression(string layerName);
    }
}
