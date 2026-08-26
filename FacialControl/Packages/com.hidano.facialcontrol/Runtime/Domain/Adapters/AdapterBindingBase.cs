using System;

namespace Hidano.FacialControl.Domain.Adapters
{
    /// <summary>
    /// Adapter package が継承する polymorphic 抽象基底。
    /// Slug を保持し、binding lifecycle hook を virtual no-op で提供する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Domain 層配置のため UnityEngine 参照や VContainer interface は import しない。
    /// 具象側に <c>[Serializable]</c> を必ず付与しないと <c>[SerializeReference]</c> の round-trip が破綻する。
    /// </para>
    /// <para>
    /// DD-1: <see cref="Slug"/> は <c>public string</c> field として宣言し、
    /// Unity の Script Serialization rule（public non-static field 自動 serialize）に乗せる。
    /// <c>[UnityEngine.SerializeField]</c> は使用せず Domain 純度を維持する。
    /// </para>
    /// </remarks>
    [Serializable]
    public abstract class AdapterBindingBase
    {
        /// <summary>
        /// Binding を識別する slug 文字列（Editor から auto-populate される）。
        /// 空 / null も許容（runtime 時点では空 binding は warn 対象）。
        /// </summary>
        public string Slug;

        /// <summary>
        /// Binding 構築時に呼ばれる初期化フック。
        /// 必要なリソース（helper MonoBehaviour、socket 等）を <see cref="AdapterBuildContext"/> 経由で確保する。
        /// </summary>
        public virtual void OnStart(in AdapterBuildContext ctx) { }

        /// <summary>
        /// PlayerLoop の Update phase で 1 フレームに 1 回呼ばれる lifecycle hook。
        /// 必要な binding のみ override する（default は no-op）。
        /// </summary>
        public virtual void OnTick(float deltaTime) { }

        /// <summary>
        /// PlayerLoop の LateUpdate phase で 1 フレームに 1 回呼ばれる lifecycle hook。
        /// </summary>
        public virtual void OnLateTick(float deltaTime) { }

        /// <summary>
        /// PlayerLoop の FixedUpdate phase で呼ばれる lifecycle hook。
        /// </summary>
        public virtual void OnFixedTick(float fixedDeltaTime) { }

        /// <summary>
        /// Binding 破棄時に呼ばれる解放フック。
        /// <see cref="OnStart"/> で確保したリソースをここで解放する。
        /// </summary>
        public virtual void Dispose() { }
    }
}
