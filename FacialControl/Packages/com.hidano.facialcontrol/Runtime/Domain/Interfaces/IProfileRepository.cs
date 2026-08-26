using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Domain.Interfaces
{
    /// <summary>
    /// プロファイルの永続化を担当するリポジトリインターフェース。
    /// ファイルパスを指定して FacialProfile の読み込み・保存を行う。
    /// </summary>
    public interface IProfileRepository
    {
        /// <summary>
        /// 指定パスから FacialProfile を読み込む。
        /// </summary>
        /// <param name="path">プロファイルファイルのパス</param>
        /// <returns>読み込まれた FacialProfile</returns>
        FacialProfile LoadProfile(string path);

        /// <summary>
        /// FacialProfile を指定パスに保存する。
        /// </summary>
        /// <param name="path">保存先ファイルパス</param>
        /// <param name="profile">保存対象のプロファイル</param>
        void SaveProfile(string path, FacialProfile profile);
    }
}
