namespace Hidano.FacialControl.Adapters.Playable
{
    /// <summary>
    /// 同一 SkinnedMeshRenderer を複数の <see cref="FacialController"/> が制御対象にしている場合の解決結果。
    /// </summary>
    public enum FacialControllerConflictResolution
    {
        /// <summary>競合していない（判定対象が欠けている場合を含む）。</summary>
        None = 0,

        /// <summary>既存の制御側を残し、これから初期化しようとしている側を無効化する。</summary>
        YieldToExisting = 1,

        /// <summary>既存の制御側を無効化し、これから初期化しようとしている側が引き継ぐ。</summary>
        TakeOverFromExisting = 2,
    }
}
