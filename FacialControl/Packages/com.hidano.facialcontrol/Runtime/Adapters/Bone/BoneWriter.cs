using System;
using System.Collections.Generic;
using UnityEngine;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Adapters.Bone
{
    /// <summary>
    /// active な <see cref="BoneSnapshot"/> 列を毎フレーム basis 相対で <see cref="UnityEngine.Transform.localRotation"/> に
    /// 書戻すサービス 。
    /// </summary>
    /// <remarks>
    /// メインスレッド限定、hot path で alloc しない。
    /// <see cref="RestoreInitialRotations"/> は遅延スナップショット方式。
    /// <see cref="Initialize"/> で basis をキャッシュ、<see cref="Apply"/> は引数なし。
    /// 入力型は <see cref="BoneSnapshot"/> 経路に統一。
    /// </remarks>
    public sealed class BoneWriter : IBonePoseSource, IBonePoseProvider, IDisposable
    {
        private readonly BoneTransformResolver _resolver;
        private readonly Animator _animator;
        private readonly BonePoseSnapshot _snapshot = new BonePoseSnapshot();
        private readonly Dictionary<string, Quaternion> _initialSnapshot = new Dictionary<string, Quaternion>();

        private ReadOnlyMemory<BoneSnapshot> _activeSnapshots;
        private ReadOnlyMemory<BoneSnapshot> _pendingSnapshots;
        private bool _hasPending;
        private string _basisBoneName;
        private Transform _basisBone;
        private bool _initialized;

        public BoneWriter(BoneTransformResolver resolver, Animator animator)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _animator = animator;
        }

        /// <summary>
        /// 初期 BoneSnapshot 列と basis bone 名で BoneWriter を初期化する。
        /// </summary>
        /// <param name="initialSnapshots">初期 <see cref="BoneSnapshot"/> 列（空でも可、analog-input-binding が後から流す典型ケース）</param>
        /// <param name="basisBoneName">顔相対軸の基準ボーン名（典型: "Head"）。Initialize 時に basis Transform を解決してキャッシュする</param>
        public void Initialize(ReadOnlyMemory<BoneSnapshot> initialSnapshots, string basisBoneName)
        {
            _activeSnapshots = initialSnapshots;
            _basisBoneName = basisBoneName ?? string.Empty;

            if (string.IsNullOrEmpty(_basisBoneName))
            {
                Debug.LogWarning("[BoneWriter] basisBoneName が空のため basis bone を解決できません。Apply は frame ごと skip されます。");
                _basisBone = null;
            }
            else
            {
                _basisBone = _resolver.Resolve(_basisBoneName);
                // 解決失敗時の警告は BoneTransformResolver 側で 1 回だけ出る（dedupe 済み）。
            }

            var snapshots = initialSnapshots.Span;
            for (int i = 0; i < snapshots.Length; i++)
            {
                _ = _resolver.Resolve(snapshots[i].BonePath);
            }

            _snapshot.EnsureCapacity(snapshots.Length);
            _initialized = true;
        }

        /// <inheritdoc />
        /// <remarks>
        /// next-frame セマンティクス : 呼出時点では <c>_pendingSnapshots</c> に格納するのみで、
        /// 次回 <see cref="Apply"/> 開始時に <c>_activeSnapshots</c> へ swap する。
        /// 同一フレームで複数回呼ばれた場合は latest-wins（pending は queue ではなく最新値保持）。
        /// メインスレッド限定契約。
        /// </remarks>
        public void SetActiveBoneSnapshots(ReadOnlyMemory<BoneSnapshot> snapshots)
        {
            _pendingSnapshots = snapshots;
            _hasPending = true;
        }

        /// <inheritdoc />
        public ReadOnlyMemory<BoneSnapshot> GetActiveBoneSnapshots()
        {
            return _activeSnapshots;
        }

        /// <summary>
        /// FacialController.LateUpdate 末尾から呼ばれる毎フレーム適用処理。
        /// </summary>
        /// <remarks>
        /// MINOR-1: 引数なし。Initialize 時にキャッシュした basis Transform の <c>localRotation</c> を 1 回読み、
        /// 各エントリの最終 localRotation を Composer で合成して対象 Transform に書込む。
        /// Position / Scale は未使用（Euler 角のみ反映）。
        /// </remarks>
        public void Apply()
        {
            if (!_initialized)
            {
                return;
            }

            if (_hasPending)
            {
                _activeSnapshots = _pendingSnapshots;
                _pendingSnapshots = default;
                _hasPending = false;
            }

            var snapshots = _activeSnapshots.Span;
            if (snapshots.Length == 0)
            {
                return;
            }

            if (_basisBone == null)
            {
                // basis 未解決時は world 軸フォールバックしない。warning は Initialize で出力済み。
                return;
            }

            var basisRot = _basisBone.localRotation;

            for (int i = 0; i < snapshots.Length; i++)
            {
                var entry = snapshots[i];
                var target = _resolver.Resolve(entry.BonePath);
                if (target == null)
                {
                    // bone 名未解決は warning + skip + 続行。warning は resolver 側で dedupe 済み。
                    continue;
                }

                // 遅延スナップショット (MAJOR-1): 初回書込み直前の localRotation を first-write-only で記録。
                // 既に key が存在する場合は ContainsKey のみで早期 return し、hot path で alloc しない。
                if (!_initialSnapshot.ContainsKey(entry.BonePath))
                {
                    _initialSnapshot[entry.BonePath] = target.localRotation;
                }

                BonePoseComposer.Compose(
                    basisRot.x, basisRot.y, basisRot.z, basisRot.w,
                    entry.EulerX, entry.EulerY, entry.EulerZ,
                    out float qx, out float qy, out float qz, out float qw);

                target.localRotation = new Quaternion(qx, qy, qz, qw);
            }
        }

        /// <summary>
        /// 書込中だった bone の <c>localRotation</c> を Initialize 後の最初の書込み直前の値に戻す。
        /// </summary>
        /// <remarks>
        /// MAJOR-1: 遅延スナップショット方式。<see cref="Apply"/> 内で各エントリの初回書込み直前に
        /// <c>_initialSnapshot[bonePath] = transform.localRotation</c> を記録し、本メソッドで巡回復元する。
        /// </remarks>
        public void RestoreInitialRotations()
        {
            // snapshot 空（Apply 未実行 / 全エントリ解決失敗）→ 何もしない（no-op）。
            if (_initialSnapshot.Count == 0)
            {
                return;
            }

            foreach (var kvp in _initialSnapshot)
            {
                var target = _resolver.Resolve(kvp.Key);
                if (target == null)
                {
                    // Initialize 〜 Apply 〜 Restore の間にヒエラルキーが消失したケース。
                    // warning は resolver 側で dedupe 済みのため skip のみ。
                    continue;
                }

                target.localRotation = kvp.Value;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _initialized = false;
            _basisBone = null;
            _pendingSnapshots = default;
            _activeSnapshots = default;
            _hasPending = false;
            _initialSnapshot.Clear();
        }
    }
}
