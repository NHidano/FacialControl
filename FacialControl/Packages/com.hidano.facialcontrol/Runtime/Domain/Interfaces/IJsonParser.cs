using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Domain.Interfaces
{
    /// <summary>
    /// JSON のパース・シリアライズを担当するインターフェース。
    /// FacialProfile および FacialControlConfig の JSON 変換を提供する。
    /// </summary>
    public interface IJsonParser
    {
        /// <summary>
        /// JSON 文字列から FacialProfile をパースする。
        /// </summary>
        /// <param name="json">プロファイル JSON 文字列</param>
        /// <returns>パースされた FacialProfile</returns>
        FacialProfile ParseProfile(string json);

        /// <summary>
        /// FacialProfile を JSON 文字列にシリアライズする。
        /// </summary>
        /// <param name="profile">シリアライズ対象のプロファイル</param>
        /// <returns>JSON 文字列</returns>
        string SerializeProfile(FacialProfile profile);

        /// <summary>
        /// JSON 文字列から FacialControlConfig をパースする。
        /// </summary>
        /// <param name="json">設定 JSON 文字列</param>
        /// <returns>パースされた FacialControlConfig</returns>
        FacialControlConfig ParseConfig(string json);

        /// <summary>
        /// FacialControlConfig を JSON 文字列にシリアライズする。
        /// </summary>
        /// <param name="config">シリアライズ対象の設定</param>
        /// <returns>JSON 文字列</returns>
        string SerializeConfig(FacialControlConfig config);
    }
}
