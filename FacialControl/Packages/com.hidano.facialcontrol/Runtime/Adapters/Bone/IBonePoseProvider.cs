using System;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.Bone
{
    /// <summary>
    /// 外部 (analog-input-binding 等) が active <see cref="BoneSnapshot"/> 列を注入する契約 。
    /// </summary>
    /// <remarks>
    /// メインスレッド限定契約。<see cref="SetActiveBoneSnapshots"/> は <see cref="ReadOnlyMemory{T}"/>
    /// で配列参照を渡し、hot path で alloc しない。設定された snapshot 列は次フレームの <c>Apply</c> から有効。
    /// </remarks>
    public interface IBonePoseProvider
    {
        /// <summary>
        /// active な <see cref="BoneSnapshot"/> 列を差替える。次フレームの <c>Apply</c> から有効。
        /// </summary>
        /// <param name="snapshots">適用対象の <see cref="BoneSnapshot"/> 列 (基底配列を共有)。</param>
        void SetActiveBoneSnapshots(ReadOnlyMemory<BoneSnapshot> snapshots);
    }
}
