using System;
using System.IO;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.FileSystem
{
    /// <summary>
    /// IProfileRepository のファイルシステム実装。
    /// IJsonParser を利用して JSON ファイルの読み書きを行う。
    /// </summary>
    public sealed class FileProfileRepository : IProfileRepository
    {
        private readonly IJsonParser _parser;

        /// <summary>
        /// FileProfileRepository を生成する。
        /// </summary>
        /// <param name="parser">JSON パーサー</param>
        public FileProfileRepository(IJsonParser parser)
        {
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        }

        /// <summary>
        /// 指定パスから FacialProfile を読み込む。
        /// ファイルの内容を UTF-8 で読み込み、IJsonParser でパースする。
        /// </summary>
        /// <param name="path">プロファイルファイルのパス</param>
        /// <returns>読み込まれた FacialProfile</returns>
        public FacialProfile LoadProfile(string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("パスを空にすることはできません。", nameof(path));

            if (!File.Exists(path))
                throw new FileNotFoundException("プロファイルファイルが見つかりません。", path);

            var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
            return _parser.ParseProfile(json);
        }

        /// <summary>
        /// FacialProfile を指定パスに保存する。
        /// IJsonParser でシリアライズした JSON を UTF-8 でファイルに書き込む。
        /// 親ディレクトリが存在しない場合は自動的に作成する。
        /// </summary>
        /// <param name="path">保存先ファイルパス</param>
        /// <param name="profile">保存対象のプロファイル</param>
        public void SaveProfile(string path, FacialProfile profile)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("パスを空にすることはできません。", nameof(path));

            var json = _parser.SerializeProfile(profile);

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, json, System.Text.Encoding.UTF8);
        }
    }
}
