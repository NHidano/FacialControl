using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Domain.Services
{
    /// <summary>
    /// <see cref="ExpressionSnapshot"/> 辞書を構築時に preallocate し、
    /// SnapshotId → BlendShape 値 / Bone スナップショット列の解決を 0-alloc で提供する Domain サービス
    /// 。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 構築時に <see cref="IReadOnlyDictionary{TKey, TValue}"/> を内部辞書に防御コピーし、
    /// <see cref="TryResolve"/> ホットパスでは <c>Span&lt;float&gt;</c> / <c>Span&lt;BoneSnapshot&gt;</c>
    /// 出力バッファに対して値書込みのみを行う（managed heap 確保ゼロ）。
    /// </para>
    /// <para>
    /// LayerOverrideMask による他レイヤー抑制は <c>LayerUseCase</c> が最終ブレンド前に担い、
    /// 本サービスは snapshot table 引きと出力バッファへの値展開のみを担う。
    /// </para>
    /// </remarks>
    public sealed class ExpressionResolver
    {
        private readonly Dictionary<string, ExpressionSnapshot> _snapshots;

        /// <summary>
        /// 既知の SnapshotId 集合の数。
        /// </summary>
        public int SnapshotCount => _snapshots.Count;

        /// <summary>
        /// 構築時に渡された snapshot 群を内部にコピーして preallocate する。
        /// </summary>
        /// <param name="snapshots">SnapshotId → <see cref="ExpressionSnapshot"/> の辞書（null は空辞書として扱う）。</param>
        public ExpressionResolver(IReadOnlyDictionary<string, ExpressionSnapshot> snapshots)
        {
            if (snapshots == null)
            {
                _snapshots = new Dictionary<string, ExpressionSnapshot>(0, StringComparer.Ordinal);
                return;
            }

            _snapshots = new Dictionary<string, ExpressionSnapshot>(snapshots.Count, StringComparer.Ordinal);
            foreach (var kv in snapshots)
            {
                if (kv.Key == null)
                {
                    continue;
                }
                _snapshots[kv.Key] = kv.Value;
            }
        }

        /// <summary>
        /// 指定 <paramref name="snapshotId"/> の snapshot を 出力バッファに展開する。
        /// 0-alloc を維持するため、出力先は呼出側が事前確保した <see cref="Span{T}"/> を渡す。
        /// </summary>
        /// <param name="snapshotId">対象 SnapshotId（null は false を返す）。</param>
        /// <param name="blendShapeOutput">BlendShape 値書込み先。長さは snapshot.BlendShapes.Length 以上が必要。</param>
        /// <param name="boneOutput">Bone スナップショット書込み先。長さは snapshot.Bones.Length 以上が必要。</param>
        /// <returns>解決成功で true。Id 未登録 / どちらかのバッファ不足で false（出力は変更しない）。</returns>
        public bool TryResolve(string snapshotId, Span<float> blendShapeOutput, Span<BoneSnapshot> boneOutput)
        {
            if (snapshotId == null)
            {
                return false;
            }

            if (!_snapshots.TryGetValue(snapshotId, out var snapshot))
            {
                return false;
            }

            var blendShapeSpan = snapshot.BlendShapes.Span;
            var boneSpan = snapshot.Bones.Span;

            if (blendShapeOutput.Length < blendShapeSpan.Length)
            {
                return false;
            }

            if (boneOutput.Length < boneSpan.Length)
            {
                return false;
            }

            for (int i = 0; i < blendShapeSpan.Length; i++)
            {
                blendShapeOutput[i] = blendShapeSpan[i].Value;
            }

            for (int i = 0; i < boneSpan.Length; i++)
            {
                boneOutput[i] = boneSpan[i];
            }

            return true;
        }

        /// <summary>
        /// 指定 SnapshotId の <see cref="ExpressionSnapshot"/> を取得する。
        /// </summary>
        /// <param name="snapshotId">対象 SnapshotId。</param>
        /// <param name="snapshot">解決された snapshot。</param>
        /// <returns>登録済みなら true。</returns>
        public bool TryGetSnapshot(string snapshotId, out ExpressionSnapshot snapshot)
        {
            if (snapshotId == null)
            {
                snapshot = default;
                return false;
            }

            return _snapshots.TryGetValue(snapshotId, out snapshot);
        }
    }
}
