using System;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.Bone
{
    /// <summary>
    /// hot path で再利用する事前確保中間 buffer 。
    /// </summary>
    /// <remarks>
    /// 解決済み <see cref="Transform"/> 配列と中間 quaternion 配列を保持し、
    /// BonePose 切替時に容量不足のときのみ拡張する（縮小しない）。
    /// 同一 BonePose 継続中は <see cref="EnsureCapacity"/> 呼出で内部配列を再確保しない。
    /// </remarks>
    public sealed class BonePoseSnapshot
    {
        private static readonly Transform[] EmptyTransforms = Array.Empty<Transform>();
        private static readonly Quaternion[] EmptyRotations = Array.Empty<Quaternion>();

        private Transform[] _transforms;
        private Quaternion[] _rotations;

        public BonePoseSnapshot()
        {
            _transforms = EmptyTransforms;
            _rotations = EmptyRotations;
        }

        public Transform[] Transforms => _transforms;

        public Quaternion[] Rotations => _rotations;

        public int Capacity => _transforms.Length;

        public void EnsureCapacity(int requested)
        {
            if (requested <= _transforms.Length)
            {
                return;
            }

            var newTransforms = new Transform[requested];
            Array.Copy(_transforms, newTransforms, _transforms.Length);
            _transforms = newTransforms;

            var newRotations = new Quaternion[requested];
            Array.Copy(_rotations, newRotations, _rotations.Length);
            _rotations = newRotations;
        }
    }
}
