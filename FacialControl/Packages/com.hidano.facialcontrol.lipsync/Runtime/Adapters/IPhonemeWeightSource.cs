namespace Hidano.FacialControl.LipSync.Adapters
{
    /// <summary>
    /// 音素別の正規化済みウェイトと音量を供給する境界。
    /// uLipSync 公式の音量正規化・SmoothDamp・sum=1 正規化を委譲した結果を
    /// <see cref="ULipSyncProvider"/> が読み取るための抽象。
    /// </summary>
    /// <remarks>
    /// FacialControl 側では volume / min-max / 平滑化を一切加工せず、uLipSync が
    /// 算出した値をそのまま読み出す。EditMode 単体テストを MonoBehaviour 非依存に
    /// 保つため、provider はこの抽象にのみ依存する。
    /// </remarks>
    public interface IPhonemeWeightSource
    {
        /// <summary>
        /// uLipSync が算出した現在の正規化音量 (0..1)。
        /// </summary>
        float CurrentVolume { get; }

        /// <summary>
        /// 指定音素の現在の正規化ウェイト (0..1, sum=1 正規化済み) を取得する。
        /// </summary>
        /// <param name="phonemeId">音素 id（例: "A"/"I"/"U"/"E"/"O"）。</param>
        /// <param name="weight">登録済みなら現在ウェイト、未登録なら 0。</param>
        /// <returns>音素が登録済みなら true。</returns>
        bool TryGetPhonemeWeight(string phonemeId, out float weight);
    }
}
