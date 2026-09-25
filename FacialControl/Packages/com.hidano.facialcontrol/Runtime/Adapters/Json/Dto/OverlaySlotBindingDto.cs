namespace Hidano.FacialControl.Adapters.Json.Dto
{
    /// <summary>
    /// <c>expressions[].snapshot.overlays[]</c> および <c>defaultOverlays[]</c> の 1 エントリ DTO。
    /// <para>
    /// <see cref="suppress"/> と <see cref="snapshot"/> で default fallback / suppress / snapshot override を表現する。
    /// JsonUtility 互換のため <see cref="System.SerializableAttribute"/> を付与する。
    /// </para>
    /// </summary>
    [System.Serializable]
    public sealed class OverlaySlotBindingDto
    {
        /// <summary>slot 識別子（"blink" 等）。空文字不可。</summary>
        public string slot;

        /// <summary>
        /// 当該 slot の overlay を明示的に抑制する場合 true。
        /// </summary>
        public bool suppress;

        /// <summary>
        /// 個別 override として適用する snapshot。null の場合は default fallback または suppress。
        /// 再帰終端型 <see cref="OverlaySnapshotDto"/>（overlays を持たない）を宣言型とすることで、
        /// JsonUtility / Unity シリアライザの自己再帰（depth limit 10 超過）を型レベルで断つ。
        /// </summary>
        public OverlaySnapshotDto snapshot;
    }
}
