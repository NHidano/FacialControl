using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Adapters.InputSources
{
    /// <summary>
    /// Expression トリガー型入力源の汎用具象実装 。
    /// device 種別に依存しない 1 個の concrete 実装として提供する 。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本クラスは <see cref="ExpressionTriggerInputSourceBase"/> を継承し、構築時に与えられた
    /// 任意の <see cref="InputSourceId"/> を保持する。device 別カテゴリ分けは
    /// <see cref="ExpressionInputSourceAdapter"/> + <see cref="Hidano.FacialControl.Adapters.Input.InputDeviceCategorizer"/>
    /// が <see cref="UnityEngine.InputSystem.InputAction.bindings"/> から自動推定して dispatch する責務を負う。
    /// </para>
    /// <para>
    /// JSON Layer の <c>inputSources[]</c> 宣言は予約 id <c>input</c> 1 種類で表現する。
    /// 旧 <c>controller-expr</c> / <c>keyboard-expr</c> の二系統分離は preview 段階で廃止された
    /// （InputSystem が device 抽象化を担うため、レイヤー粒度での device 分離は不要）。
    /// </para>
    /// </remarks>
    public sealed class ExpressionTriggerInputSource : ExpressionTriggerInputSourceBase
    {
        /// <summary>InputSystem 経由の Expression トリガー型入力源を表す予約 id。</summary>
        public const string InputReservedId = "input";

        /// <summary>
        /// <see cref="ExpressionTriggerInputSource"/> を構築する。
        /// </summary>
        /// <param name="id">入力源識別子 (任意の <see cref="InputSourceId"/>)。</param>
        /// <param name="blendShapeCount">書込む BlendShape 個数 (0 以上)。</param>
        /// <param name="maxStackDepth">Expression スタックの最大深度 (1 以上)。</param>
        /// <param name="exclusionMode">本インスタンス内部で適用する排他モード。</param>
        /// <param name="blendShapeNames">BlendShape 名の列 (名前→インデックス解決用)。</param>
        /// <param name="profile">Expression 検索に用いる <see cref="FacialProfile"/>。</param>
        public ExpressionTriggerInputSource(
            InputSourceId id,
            int blendShapeCount,
            int maxStackDepth,
            ExclusionMode exclusionMode,
            IReadOnlyList<string> blendShapeNames,
            FacialProfile profile)
            : base(id, blendShapeCount, maxStackDepth, exclusionMode, blendShapeNames, profile)
        {
        }
    }
}
