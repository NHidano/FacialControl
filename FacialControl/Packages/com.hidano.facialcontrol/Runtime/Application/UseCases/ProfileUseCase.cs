using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Application.UseCases
{
    /// <summary>
    /// プロファイルの読み込み・管理を担当するユースケース。
    /// IProfileRepository を通じてプロファイルを読み込み、
    /// Expression の検索やレイヤー検証を提供する。
    /// </summary>
    public class ProfileUseCase
    {
        private readonly IProfileRepository _repository;
        private FacialProfile? _currentProfile;
        private string _currentPath;

        /// <summary>
        /// 現在読み込まれているプロファイル。未読み込みの場合は null。
        /// </summary>
        public FacialProfile? CurrentProfile => _currentProfile;

        /// <summary>
        /// 現在読み込まれているプロファイルのファイルパス。未読み込みの場合は null。
        /// </summary>
        public string CurrentPath => _currentPath;

        /// <summary>
        /// ProfileUseCase を生成する。
        /// </summary>
        /// <param name="repository">プロファイルリポジトリ</param>
        public ProfileUseCase(IProfileRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        /// <summary>
        /// 指定パスからプロファイルを読み込む。
        /// JSON パース → FacialProfile 生成 → レイヤー検証を行う。
        /// </summary>
        /// <param name="path">プロファイルファイルのパス</param>
        /// <returns>読み込まれた FacialProfile</returns>
        public FacialProfile LoadProfile(string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("パスを空にすることはできません。", nameof(path));

            var profile = _repository.LoadProfile(path);
            _currentProfile = profile;
            _currentPath = path;

            return profile;
        }

        /// <summary>
        /// 現在のプロファイルを同じパスから再読み込みする。
        /// </summary>
        /// <returns>再読み込みされた FacialProfile</returns>
        public FacialProfile ReloadProfile()
        {
            if (_currentPath == null)
                throw new InvalidOperationException("プロファイルが読み込まれていません。");

            return LoadProfile(_currentPath);
        }

        /// <summary>
        /// ID から Expression を取得する。
        /// </summary>
        /// <param name="id">Expression の ID</param>
        /// <returns>見つかった Expression。見つからない場合は null。</returns>
        public Expression? GetExpression(string id)
        {
            if (id == null)
                throw new ArgumentNullException(nameof(id));

            var profile = GetCurrentProfileOrThrow();
            return profile.FindExpressionById(id);
        }

        /// <summary>
        /// レイヤー別 Expression リストを取得する。
        /// </summary>
        /// <param name="layer">レイヤー名</param>
        /// <returns>指定レイヤーに属する Expression のリスト</returns>
        public ReadOnlyMemory<Expression> GetExpressionsByLayer(string layer)
        {
            if (layer == null)
                throw new ArgumentNullException(nameof(layer));

            var profile = GetCurrentProfileOrThrow();
            return profile.GetExpressionsByLayer(layer);
        }

        /// <summary>
        /// 現在のプロファイルのレイヤー参照検証結果を取得する。
        /// </summary>
        /// <returns>無効なレイヤー参照のリスト</returns>
        public List<InvalidLayerReference> GetInvalidLayerReferences()
        {
            var profile = GetCurrentProfileOrThrow();
            return profile.ValidateLayerReferences();
        }

        private FacialProfile GetCurrentProfileOrThrow()
        {
            if (!_currentProfile.HasValue)
                throw new InvalidOperationException("プロファイルが読み込まれていません。");

            return _currentProfile.Value;
        }
    }
}
