using System;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.Bone
{
    /// <summary>
    /// BoneWriter 自身が実装する内部契約。現在 active な <see cref="BoneSnapshot"/> 列を返す 。
    /// </summary>
    /// <remarks>
    /// メインスレッド限定契約。hot path で alloc しない。
    /// </remarks>
    public interface IBonePoseSource
    {
        /// <summary>
        /// 現在 active な <see cref="BoneSnapshot"/> 列を返す（未設定時は空 <see cref="ReadOnlyMemory{T}"/>）。
        /// </summary>
        ReadOnlyMemory<BoneSnapshot> GetActiveBoneSnapshots();
    }
}
