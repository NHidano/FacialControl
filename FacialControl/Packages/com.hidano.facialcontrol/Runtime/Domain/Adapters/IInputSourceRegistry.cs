using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.InputSources
{
    /// <summary>
    /// slug-keyed <see cref="IInputSource"/> lookup の中立 interface。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本 interface は <see cref="Hidano.FacialControl.Domain.Adapters.AdapterBuildContext"/> の
    /// field 型として参照されるため Domain asmdef に配置する（Adapters は Domain を参照するが
    /// その逆は不可なので、interface のみ Domain に置き impl は Adapters/InputSources に置く）。
    /// 名前空間は impl 側と揃え <c>Hidano.FacialControl.Adapters.InputSources</c> を採用する。
    /// </para>
    /// <para>
    /// Binding は <see cref="Hidano.FacialControl.Domain.Adapters.AdapterBindingBase.OnStart(in Hidano.FacialControl.Domain.Adapters.AdapterBuildContext)"/>
    /// 内で <see cref="Register(AdapterSlug, IInputSource)"/> を呼び自身の primary 入力源を、
    /// 必要なら <see cref="Register(AdapterSlug, string, IInputSource)"/> で
    /// <c>&lt;slug&gt;:&lt;sub&gt;</c> 形式の追加入力源を登録する（D-3, D-4）。
    /// </para>
    /// </remarks>
    public interface IInputSourceRegistry
    {
        /// <summary>
        /// <c>&lt;slug&gt;</c> primary id で <paramref name="source"/> を登録する。
        /// 同 id 重複登録は LogError + 後勝ち上書きとする（preview 段階の柔軟性優先）。
        /// </summary>
        /// <exception cref="System.ArgumentNullException">
        /// <paramref name="source"/> が <c>null</c> の場合。
        /// </exception>
        void Register(AdapterSlug slug, IInputSource source);

        /// <summary>
        /// <c>&lt;slug&gt;</c> primary id 縺ｮ <paramref name="source"/> 繧貞ｷｮ縺玲崛縺医ｋ縲・
        /// 譛ｪ逋ｻ骭ｲ縺ｮ蝣ｴ蜷医・譁ｰ隕冗匳骭ｲ縺励√里蟄倥・ insertion order 繧剃ｿ晄戟縺吶ｋ縲・
        /// </summary>
        /// <exception cref="System.ArgumentNullException">
        /// <paramref name="source"/> 縺・<c>null</c> 縺ｮ蝣ｴ蜷医・
        /// </exception>
        void Replace(AdapterSlug slug, IInputSource source);

        /// <summary>
        /// <c>&lt;slug&gt;:&lt;sub&gt;</c> 複合 id で <paramref name="source"/> を登録する。
        /// 同 id 重複登録は LogError + 後勝ち上書きとする。
        /// </summary>
        /// <exception cref="System.ArgumentException">
        /// <paramref name="sub"/> が <c>null</c> または空文字列の場合。
        /// </exception>
        /// <exception cref="System.ArgumentNullException">
        /// <paramref name="source"/> が <c>null</c> の場合。
        /// </exception>
        void Register(AdapterSlug slug, string sub, IInputSource source);

        /// <summary>
        /// <c>&lt;slug&gt;:&lt;sub&gt;</c> 隍・粋 id 縺ｮ <paramref name="source"/> 繧貞ｷｮ縺玲崛縺医ｋ縲・
        /// 譛ｪ逋ｻ骭ｲ縺ｮ蝣ｴ蜷医・譁ｰ隕冗匳骭ｲ縺吶ｋ縲・
        /// </summary>
        /// <exception cref="System.ArgumentException">
        /// <paramref name="sub"/> 縺・<c>null</c> 縺ｾ縺溘・遨ｺ譁・ｭ怜・縺ｮ蝣ｴ蜷医・
        /// </exception>
        /// <exception cref="System.ArgumentNullException">
        /// <paramref name="source"/> 縺・<c>null</c> 縺ｮ蝣ｴ蜷医・
        /// </exception>
        void Replace(AdapterSlug slug, string sub, IInputSource source);

        /// <summary>
        /// <c>&lt;slug&gt;</c> primary id の登録を解除する。未登録の場合は何もしない。
        /// </summary>
        void Unregister(AdapterSlug slug);

        void Unregister(AdapterSlug slug, string sub);

        /// <summary>
        /// layer.inputSources[].id 形式の文字列（<c>&lt;slug&gt;</c> または <c>&lt;slug&gt;:&lt;sub&gt;</c>）
        /// を登録済 <see cref="IInputSource"/> に解決する。
        /// </summary>
        /// <param name="layerInputSourceId">
        /// <c>null</c> または空文字列のときは <c>false</c> を返し <paramref name="source"/> は <c>null</c>。
        /// </param>
        bool TryResolve(string layerInputSourceId, out IInputSource source);

        /// <summary>
        /// 現在登録されている全 id（primary / 複合の混在）の診断用スナップショット。
        /// 同一状態に対する複数回の列挙は同じ順序を返す（挿入順保持）。
        /// </summary>
        IReadOnlyList<string> RegisteredIds { get; }

        /// <summary>
        /// 指定 id（primary slug または slug:sub）の Register/Replace を購読する。
        /// 該当 id の source が登録された時点で handler が発火する（auto mapping OSC の遅延登録対応）。
        /// id が null/空、handler が null の場合は no-op。
        /// </summary>
        void Subscribe(string id, Action<IInputSource> handler);
    }
}
