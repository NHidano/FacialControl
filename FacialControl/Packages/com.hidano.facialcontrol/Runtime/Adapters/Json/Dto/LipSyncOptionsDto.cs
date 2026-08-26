namespace Hidano.FacialControl.Adapters.Json.Dto
{
    /// <summary>
    /// 予約 ID <c>lipsync</c> の入力源固有 options。preview 段階では空。
    /// 将来フィールドが追加されても JSON 後方互換のために派生を維持する。
    /// </summary>
    [System.Serializable]
    public sealed class LipSyncOptionsDto : InputSourceOptionsDto
    {
    }
}
