using Hidano.FacialControl.Domain.Interfaces;

namespace Hidano.FacialControl.Domain.Interfaces
{
    /// <summary>
    /// Replace/Register による注入で装着された代替入力ソースを表すマーカー契約。
    /// </summary>
    /// <remarks>
    /// <para>
    /// core は本 interface と占有規則の契約だけを提供し、注入状態の集中管理テーブルは持たない。
    /// 実際の装着・復元・競合解決は注入者側が
    /// <see cref="Hidano.FacialControl.Adapters.InputSources.IInputSourceRegistry"/> と
    /// 参照同一性で判断する。
    /// </para>
    /// <para>
    /// 注入者が遵守する占有規則:
    /// <list type="number">
    ///   <item>装着時に既存エントリが <see cref="IInjectedInputSource"/> なら他者占有とみなし、Warning + skip する。</item>
    ///   <item>復元時は現エントリが自分の装着インスタンスと参照同一の場合のみ原本復元または Unregister を行う。</item>
    ///   <item>参照が異なる場合は Warning + no-op とし、後続占有者の状態を破壊しない。</item>
    /// </list>
    /// </para>
    /// <para>
    /// <see cref="ReplacedSource"/> は装着前に退避した原本。
    /// 原本なしで新規 Register した注入では <c>null</c> を許容する。
    /// </para>
    /// </remarks>
    public interface IInjectedInputSource
    {
        /// <summary>
        /// 装着前に退避した原本入力ソース。原本なしの新規 Register 時は <c>null</c>。
        /// </summary>
        IInputSource ReplacedSource { get; }
    }
}
