using System;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.ScriptableObject
{
    /// <summary>
    /// FacialProfile ⟷ FacialProfileSO の変換を担当するマッパー。
    /// SO の JSON ファイルパスを経由してプロファイルの読み込み・保存を行い、
    /// SO の表示用フィールド（スキーマバージョン、レイヤー数、Expression 数）を更新する。
    /// </summary>
    public sealed class FacialProfileMapper
    {
        private readonly IProfileRepository _repository;

        /// <summary>
        /// FacialProfileMapper を生成する。
        /// </summary>
        /// <param name="repository">プロファイルの永続化リポジトリ</param>
        public FacialProfileMapper(IProfileRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        /// <summary>
        /// FacialProfileSO の JSON ファイルパスからプロファイルを読み込む。
        /// SO → JSON パス取得 → リポジトリ経由でパース のフロー。
        /// </summary>
        /// <param name="so">読み込み元の ScriptableObject</param>
        /// <returns>パースされた FacialProfile</returns>
        public FacialProfile ToProfile(FacialProfileSO so)
        {
            if (so == null)
                throw new ArgumentNullException(nameof(so));
            ValidateJsonFilePath(so.JsonFilePath);

            return _repository.LoadProfile(so.JsonFilePath);
        }

        /// <summary>
        /// FacialProfile の情報で FacialProfileSO の表示用フィールドを更新する。
        /// JsonFilePath は変更しない。
        /// </summary>
        /// <param name="so">更新対象の ScriptableObject</param>
        /// <param name="profile">更新元のプロファイル</param>
        public void UpdateSO(FacialProfileSO so, FacialProfile profile)
        {
            if (so == null)
                throw new ArgumentNullException(nameof(so));

            so.SchemaVersion = profile.SchemaVersion;
            so.LayerCount = profile.Layers.Length;
            so.ExpressionCount = profile.Expressions.Length;
            so.RendererPaths = profile.RendererPaths.ToArray();
        }

        /// <summary>
        /// FacialProfileSO の JSON ファイルパスからプロファイルを読み込み、
        /// SO の表示用フィールドも同時に更新する。
        /// </summary>
        /// <param name="so">対象の ScriptableObject</param>
        /// <returns>読み込まれた FacialProfile</returns>
        public FacialProfile LoadAndUpdateSO(FacialProfileSO so)
        {
            var profile = ToProfile(so);
            UpdateSO(so, profile);
            return profile;
        }

        /// <summary>
        /// FacialProfile を FacialProfileSO の JSON ファイルパスに保存し、
        /// SO の表示用フィールドも更新する。
        /// </summary>
        /// <param name="so">保存先パスを持つ ScriptableObject</param>
        /// <param name="profile">保存するプロファイル</param>
        public void SaveFromSO(FacialProfileSO so, FacialProfile profile)
        {
            if (so == null)
                throw new ArgumentNullException(nameof(so));
            ValidateJsonFilePath(so.JsonFilePath);

            _repository.SaveProfile(so.JsonFilePath, profile);
            UpdateSO(so, profile);
        }

        private static void ValidateJsonFilePath(string jsonFilePath)
        {
            if (string.IsNullOrWhiteSpace(jsonFilePath))
                throw new ArgumentException(
                    "FacialProfileSO の JsonFilePath が設定されていません。",
                    nameof(jsonFilePath));
        }
    }
}
