using UnityEngine;

namespace Hidano.FacialControl.Adapters.Playable
{
    /// <summary>
    /// 同一 SkinnedMeshRenderer を掴んだ 2 つの <see cref="FacialController"/> のうち
    /// どちらを生かすかを Transform 階層から決める判定サービス。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 規則は「階層上位（祖先側）優先、親子関係が無ければ先着優先」。
    /// 祖先側を優先するのは <c>GetComponentsInChildren</c> による自動検索で
    /// 祖先が子孫の SkinnedMeshRenderer をすべて包含するためで、
    /// 子孫側を残すと祖先が持っていた他の renderer が未制御になる。
    /// </para>
    /// </remarks>
    public static class FacialControllerConflictResolver
    {
        /// <summary>
        /// 競合の解決方法を決める。
        /// </summary>
        /// <param name="candidate">これから初期化しようとしている FacialController の Transform。</param>
        /// <param name="existing">既に同じ renderer を制御している FacialController の Transform。</param>
        /// <returns>
        /// どちらかが null なら <see cref="FacialControllerConflictResolution.None"/>。
        /// <paramref name="candidate"/> が <paramref name="existing"/> の祖先なら
        /// <see cref="FacialControllerConflictResolution.TakeOverFromExisting"/>、
        /// それ以外は <see cref="FacialControllerConflictResolution.YieldToExisting"/>。
        /// </returns>
        public static FacialControllerConflictResolution Resolve(Transform candidate, Transform existing)
        {
            if (candidate == null || existing == null)
            {
                return FacialControllerConflictResolution.None;
            }

            if (candidate == existing)
            {
                return FacialControllerConflictResolution.YieldToExisting;
            }

            if (IsAncestorOf(candidate, existing))
            {
                return FacialControllerConflictResolution.TakeOverFromExisting;
            }

            // existing が祖先の場合も、まったくの無関係（兄弟・別ルート）の場合も
            // candidate 側が譲る。後者は先着優先の帰結。
            return FacialControllerConflictResolution.YieldToExisting;
        }

        /// <summary>
        /// <paramref name="ancestor"/> が <paramref name="node"/> の祖先かどうかを返す。自分自身は祖先に含めない。
        /// </summary>
        private static bool IsAncestorOf(Transform ancestor, Transform node)
        {
            Transform current = node.parent;
            while (current != null)
            {
                if (current == ancestor)
                {
                    return true;
                }
                current = current.parent;
            }

            return false;
        }
    }
}
