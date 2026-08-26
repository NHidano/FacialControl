using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.InputSources
{
    /// <summary>
    /// slug-keyed <see cref="IInputSource"/> lookup の中継 interface。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本 interface は <see cref="Hidano.FacialControl.Domain.Adapters.AdapterBuildContext"/> の
    /// field 型として公開されるが、Domain asmdef から実装へ依存させないため、
    /// interface 定義のみを Domain に置き、実装は Adapters/InputSources に置く。
    /// そのため namespace は実装側と揃えて
    /// <c>Hidano.FacialControl.Adapters.InputSources</c> を使用する。
    /// </para>
    /// <para>
    /// Binding は
    /// <see cref="Hidano.FacialControl.Domain.Adapters.AdapterBindingBase.OnStart(in Hidano.FacialControl.Domain.Adapters.AdapterBuildContext)"/>
    /// 内で <see cref="Register(AdapterSlug, IInputSource)"/> を呼び自身の primary 入力源を、
    /// 必要なら <see cref="Register(AdapterSlug, string, IInputSource)"/> で
    /// <c>&lt;slug&gt;:&lt;sub&gt;</c> 形式の composite 入力源を登録する。
    /// </para>
    /// </remarks>
    public interface IInputSourceRegistry
    {
        /// <summary>
        /// <c>&lt;slug&gt;</c> primary id で <paramref name="source"/> を登録する。
        /// 同一 id への重複登録は LogError を出し、後勝ちで上書きする。
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="source"/> が <c>null</c> の場合。
        /// </exception>
        void Register(AdapterSlug slug, IInputSource source);

        /// <summary>
        /// <c>&lt;slug&gt;</c> primary id に対応する source を置き換える。
        /// 対象 id が未登録なら新規登録として扱う。
        /// Subscribe ハンドラには新しい source が同期通知される。
        /// 注入者は <see cref="Hidano.FacialControl.Domain.Interfaces.IInjectedInputSource"/> を
        /// マーカーとして用い、既存 entry が注入ソースなら他者占有とみなして
        /// 自身の Replace をスキップする契約に従う。
        /// Subscribe 通知中に本 API を呼ぶことは契約違反であり、
        /// 実装は LogError + no-op とする。
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="source"/> が <c>null</c> の場合。
        /// </exception>
        void Replace(AdapterSlug slug, IInputSource source);

        /// <summary>
        /// <c>&lt;slug&gt;:&lt;sub&gt;</c> composite id で <paramref name="source"/> を登録する。
        /// 同一 id への重複登録は LogError を出し、後勝ちで上書きする。
        /// </summary>
        /// <exception cref="ArgumentException">
        /// <paramref name="sub"/> が <c>null</c> または空文字の場合。
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="source"/> が <c>null</c> の場合。
        /// </exception>
        void Register(AdapterSlug slug, string sub, IInputSource source);

        /// <summary>
        /// <c>&lt;slug&gt;:&lt;sub&gt;</c> composite id に対応する source を置き換える。
        /// 対象 id が未登録なら新規登録として扱う。
        /// Subscribe ハンドラには新しい source が同期通知される。
        /// 注入者は <see cref="Hidano.FacialControl.Domain.Interfaces.IInjectedInputSource"/> を
        /// マーカーとして用い、既存 entry が注入ソースなら他者占有とみなして
        /// 自身の Replace をスキップする契約に従う。
        /// Subscribe 通知中に本 API を呼ぶことは契約違反であり、
        /// 実装は LogError + no-op とする。
        /// </summary>
        /// <exception cref="ArgumentException">
        /// <paramref name="sub"/> が <c>null</c> または空文字の場合。
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="source"/> が <c>null</c> の場合。
        /// </exception>
        void Replace(AdapterSlug slug, string sub, IInputSource source);

        /// <summary>
        /// <c>&lt;slug&gt;</c> primary id の登録を解除する。未登録なら no-op。
        /// 登録解除された場合、Subscribe ハンドラには <c>null</c> が同期通知される。
        /// 注入者は現エントリが自分の装着した
        /// <see cref="Hidano.FacialControl.Domain.Interfaces.IInjectedInputSource"/> と
        /// 参照同一である場合のみ復元または解除を行う。
        /// 参照が異なる場合は Warning + no-op とし、後続占有者を破壊しない。
        /// Subscribe 通知中に本 API を呼ぶことは契約違反であり、
        /// 実装は LogError + no-op とする。
        /// </summary>
        void Unregister(AdapterSlug slug);

        /// <summary>
        /// <c>&lt;slug&gt;:&lt;sub&gt;</c> composite id の登録を解除する。未登録なら no-op。
        /// 登録解除された場合、Subscribe ハンドラには <c>null</c> が同期通知される。
        /// 注入者は現エントリが自分の装着した
        /// <see cref="Hidano.FacialControl.Domain.Interfaces.IInjectedInputSource"/> と
        /// 参照同一である場合のみ復元または解除を行う。
        /// 参照が異なる場合は Warning + no-op とし、後続占有者を破壊しない。
        /// Subscribe 通知中に本 API を呼ぶことは契約違反であり、
        /// 実装は LogError + no-op とする。
        /// </summary>
        void Unregister(AdapterSlug slug, string sub);

        /// <summary>
        /// layer.inputSources[].id 形式の文字列
        /// （<c>&lt;slug&gt;</c> または <c>&lt;slug&gt;:&lt;sub&gt;</c>）を
        /// 登録済み <see cref="IInputSource"/> に解決する。
        /// </summary>
        /// <param name="layerInputSourceId">
        /// <c>null</c> または空文字のときは <c>false</c> を返し、
        /// <paramref name="source"/> は <c>null</c>。
        /// </param>
        bool TryResolve(string layerInputSourceId, out IInputSource source);

        /// <summary>
        /// 現在登録されている全 id（primary / composite の両方）の読み取り専用スナップショット。
        /// 各評価フェーズに対するキー一覧の走査に使う。
        /// </summary>
        IReadOnlyList<string> RegisteredIds { get; }

        /// <summary>
        /// 指定 id（primary slug または slug:sub）への通知ハンドラを登録する。
        /// Register/Replace 時は新しい source を、Unregister 時は <c>null</c> を同期通知する。
        /// id が <c>null</c> / empty、または handler が <c>null</c> の場合は no-op。
        /// Subscribe 通知中に本 API を呼ぶことは契約違反であり、
        /// 実装は LogError + no-op とする。
        /// </summary>
        void Subscribe(string id, Action<IInputSource> handler);
    }
}
