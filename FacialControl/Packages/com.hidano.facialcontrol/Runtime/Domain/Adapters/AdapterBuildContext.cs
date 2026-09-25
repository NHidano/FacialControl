using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Domain.Adapters
{
    /// <summary>
    /// Binding が <see cref="AdapterBindingBase.OnStart"/> で必要とする中立 service 一式を渡す
    /// <c>readonly struct</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 値型のため stack 渡し。<c>in</c> 修飾で binding の <see cref="AdapterBindingBase.OnStart"/>
    /// に渡され、boxing なしで field アクセス可能。
    /// </para>
    /// <para>
    /// <see cref="AdapterBindingBase.OnStart(in AdapterBuildContext)"/> が Domain
    /// 層から本 struct を参照する都合上、Domain.Adapters 名前空間に配置する。
    /// </para>
    /// </remarks>
    public readonly struct AdapterBuildContext
    {
        /// <summary>
        /// Binding 構築時点での FacialProfile。Aggregator や Layer 構成へ参照される。
        /// </summary>
        public readonly FacialProfile Profile;

        /// <summary>
        /// Binding が解決対象とする BlendShape 名の配列（Profile に同期した固定長）。
        /// </summary>
        public readonly IReadOnlyList<string> BlendShapeNames;

        /// <summary>
        /// slug-keyed <c>IInputSource</c> lookup。Binding が <see cref="AdapterBindingBase.OnStart"/>
        /// で <c>Register(slug, source)</c> を呼び自身の入力源を登録する。
        /// </summary>
        public readonly IInputSourceRegistry InputSourceRegistry;

        /// <summary>
        /// Post-blend BlendShape and Gaze output bus exposed to adapter bindings.
        /// </summary>
        public readonly IFacialOutputBus FacialOutputBus;

        /// <summary>
        /// Adapter bindings registered in the same FacialController child scope.
        /// </summary>
        public readonly IReadOnlyList<AdapterBindingBase> AdapterBindings;

        /// <summary>
        /// <c>Time.timeScale</c> の影響を受けない経過秒数を提供する時刻抽象。
        /// </summary>
        public readonly ITimeProvider TimeProvider;

        /// <summary>
        /// Binding の helper MonoBehaviour 等を <c>AddComponent</c> する宿主 GameObject。
        /// </summary>
        public readonly GameObject HostGameObject;

        /// <summary>
        /// LipSync provider（任意）。プロジェクトでリップシンク統合がない場合 <c>null</c> 可。
        /// </summary>
        public readonly ILipSyncProvider LipSyncProvider;

        /// <summary>
        /// 指定レイヤーの top active Expression を提供する provider（任意）。
        /// Overlay 解決経路 (<c>OverlayInputSource</c>) で必要となる。null の場合 overlay 機能は no-op。
        /// </summary>
        public readonly IActiveExpressionProvider ActiveExpressionProvider;

        /// <summary>
        /// すべての非 null 必須依存を受け取って中立コンテキストを構築する。
        /// </summary>
        /// <param name="profile">FacialProfile（値型のため null 不可）。</param>
        /// <param name="blendShapeNames">BlendShape 名配列（null 不可、空 list は許容）。</param>
        /// <param name="inputSourceRegistry">IInputSourceRegistry（null 不可）。</param>
        /// <param name="timeProvider">ITimeProvider（null 不可）。</param>
        /// <param name="hostGameObject">宿主 GameObject（null 不可）。</param>
        /// <param name="lipSyncProvider">ILipSyncProvider（null 許容）。</param>
        /// <param name="activeExpressionProvider">IActiveExpressionProvider（null 許容、Overlay 経路用）。</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="blendShapeNames"/> / <paramref name="inputSourceRegistry"/> /
        /// <paramref name="timeProvider"/> / <paramref name="hostGameObject"/> のいずれかが
        /// <c>null</c> の場合に投げられる。
        /// </exception>
        public AdapterBuildContext(
            FacialProfile profile,
            IReadOnlyList<string> blendShapeNames,
            IInputSourceRegistry inputSourceRegistry,
            IFacialOutputBus facialOutputBus,
            ITimeProvider timeProvider,
            GameObject hostGameObject,
            ILipSyncProvider lipSyncProvider,
            IActiveExpressionProvider activeExpressionProvider = null,
            IReadOnlyList<AdapterBindingBase> adapterBindings = null)
        {
            if (blendShapeNames == null)
            {
                throw new ArgumentNullException(nameof(blendShapeNames));
            }
            if (inputSourceRegistry == null)
            {
                throw new ArgumentNullException(nameof(inputSourceRegistry));
            }
            if (facialOutputBus == null)
            {
                throw new ArgumentNullException(nameof(facialOutputBus));
            }
            if (timeProvider == null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            if (hostGameObject == null)
            {
                throw new ArgumentNullException(nameof(hostGameObject));
            }

            Profile = profile;
            BlendShapeNames = blendShapeNames;
            InputSourceRegistry = inputSourceRegistry;
            FacialOutputBus = facialOutputBus;
            AdapterBindings = adapterBindings ?? Array.Empty<AdapterBindingBase>();
            TimeProvider = timeProvider;
            HostGameObject = hostGameObject;
            LipSyncProvider = lipSyncProvider;
            ActiveExpressionProvider = activeExpressionProvider;
        }
    }
}
