using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Domain.Adapters
{
    /// <summary>
    /// Editor 上で <see cref="AdapterBindingBase"/> 派生インスタンスを新規追加した直後に、
    /// 対応する Layer がプロファイルに存在しない場合に作るデフォルト Layer 設定を提供する hook。
    /// 実装した binding のみ自動 Layer 追加が行われる。
    /// </summary>
    /// <remarks>
    /// 主に LipSync のように「専用 Layer が無いと意味のある出力にならない」binding が実装する。
    /// ユーザーが Inspector でレイヤーを後から組み直すことは妨げない (削除された Layer を再度
    /// 自動補充することはしない)。runtime 経由の <see cref="System.Activator.CreateInstance(System.Type)"/>
    /// では呼ばれず、あくまで Editor 操作で「ユーザーが追加した直後」のシナリオ専用。
    /// </remarks>
    public interface IAdapterBindingDefaultLayer
    {
        /// <summary>新規追加する Layer の表示名 (= JSON / Layers リスト上の name)。</summary>
        string DefaultLayerName { get; }

        /// <summary>
        /// 新規 Layer の入力源 ID。<see cref="InputSourceId"/> 規約に合致する文字列でなければならない。
        /// 通常は binding の Slug と同値。
        /// </summary>
        /// <remarks>
        /// Deprecated for adapters that need multiple default input sources.
        /// Implement <see cref="IAdapterBindingDefaultLayerInputs"/> for the multi-source path.
        /// </remarks>
        string DefaultLayerInputSourceId { get; }

        /// <summary>新規 Layer の排他モード。</summary>
        ExclusionMode DefaultLayerExclusionMode { get; }
    }
}
