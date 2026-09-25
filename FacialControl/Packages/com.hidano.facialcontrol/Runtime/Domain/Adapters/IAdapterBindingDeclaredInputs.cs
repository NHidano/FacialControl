using System.Collections.Generic;

namespace Hidano.FacialControl.Domain.Adapters
{
    /// <summary>
    /// binding がランタイムで入力源レジストリに登録する入力源 id（canonical id）を
    /// 静的に列挙できることを表す。ルーティングエディタはこれを使ってソースポートを描画する。
    /// </summary>
    /// <remarks>
    /// <see cref="IAdapterBindingDefaultLayer"/> のような Layer 自動生成とは独立した、
    /// 純粋な「公開入力源宣言」。InputSystem のように InputActionAsset から動的に入力源を組む
    /// binding は、設定済みの id をここで静的に返すことでエディタ上のソースポートとして可視化できる。
    /// </remarks>
    public interface IAdapterBindingDeclaredInputs
    {
        /// <summary>
        /// この binding が公開する入力源 canonical id 列。
        /// 形式は <c>&lt;slug&gt;</c> または <c>&lt;slug&gt;:&lt;sub&gt;</c>。
        /// </summary>
        IEnumerable<string> GetDeclaredInputSourceIds();
    }
}
