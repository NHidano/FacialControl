using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.Bone
{
    /// <summary>
    /// アナログバインディングを毎フレーム評価し <see cref="BoneSnapshot"/> 列を構築、
    /// <see cref="IBonePoseProvider.SetActiveBoneSnapshots"/> 経由で注入するアダプタ
    /// 。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 構築時に <see cref="AnalogBindingTargetKind.BonePose"/> の binding を抽出し、
    /// ユニークな bone 名ごとに <see cref="BoneSnapshot"/> スロットを 1 度だけ確保する。
    /// 毎フレーム <see cref="BuildAndPush"/> で同一スロットの値だけを書換え、
    /// <see cref="ReadOnlyMemory{T}"/> 経由で <see cref="IBonePoseProvider"/> に渡す。
    /// </para>
    /// <para>
    /// 同一 (bone, axis) への複数 binding は post-mapping 値の sum。
    /// bindings が 0 件 / 全ソースが無効の場合は空 <see cref="ReadOnlyMemory{T}"/> を発行し、
    /// <see cref="UnityEngine.Debug"/> を呼ばない（<see cref="BoneWriter.Apply"/> は空エントリで no-op）。
    /// </para>
    /// </remarks>
    public sealed class AnalogBonePoseProvider : IDisposable
    {
        /// <summary>
        /// 本アダプタが発行する論理 ID（参照キー未使用、互換目的のみ）。
        /// </summary>
        public const string PoseId = "analog-bonepose";

        private readonly IBonePoseProvider _boneProvider;
        private readonly IReadOnlyDictionary<string, IAnalogInputSource> _sources;
        private readonly ResolvedBinding[] _resolvedBindings;
        private readonly BonePoseSlot[] _slots;
        private readonly BoneSnapshot[] _snapshotBuffer;
        private bool _disposed;

        /// <summary>
        /// <see cref="AnalogBonePoseProvider"/> を構築する。
        /// </summary>
        /// <param name="boneProvider">BoneSnapshot 注入先（<see cref="FacialController"/> 等）。</param>
        /// <param name="sources">sourceId → <see cref="IAnalogInputSource"/> の辞書。</param>
        /// <param name="bonePoseBindings">バインディング集合。<see cref="AnalogBindingTargetKind.BonePose"/> のみ採用。</param>
        /// <param name="restPoses">ボーンパス → 初期 Euler 回転 (度)。アナログ入力 0 のときに保つ姿勢。null 可。</param>
        public AnalogBonePoseProvider(
            IBonePoseProvider boneProvider,
            IReadOnlyDictionary<string, IAnalogInputSource> sources,
            IReadOnlyList<AnalogBindingEntry> bonePoseBindings,
            IReadOnlyDictionary<string, UnityEngine.Vector3> restPoses = null)
        {
            _boneProvider = boneProvider ?? throw new ArgumentNullException(nameof(boneProvider));
            _sources = sources ?? throw new ArgumentNullException(nameof(sources));
            if (bonePoseBindings == null)
            {
                throw new ArgumentNullException(nameof(bonePoseBindings));
            }

            // Step 1: BonePose ターゲットの bindings のみ抽出し、source 解決を済ませる。
            // ユニーク bone ごとに slotIndex を割当てる。
            var boneNameToSlot = new Dictionary<string, int>(StringComparer.Ordinal);
            var resolvedList = new List<ResolvedBinding>(bonePoseBindings.Count);

            for (int i = 0; i < bonePoseBindings.Count; i++)
            {
                var entry = bonePoseBindings[i];
                if (entry.TargetKind != AnalogBindingTargetKind.BonePose)
                {
                    continue;
                }

                if (!_sources.TryGetValue(entry.SourceId, out var source) || source == null)
                {
                    Debug.LogWarning(
                        $"[AnalogBonePoseProvider] source '{entry.SourceId}' not registered " +
                        $"(target='{entry.TargetIdentifier}'). Binding skipped.");
                    continue;
                }

                if (!boneNameToSlot.TryGetValue(entry.TargetIdentifier, out int slot))
                {
                    slot = boneNameToSlot.Count;
                    boneNameToSlot[entry.TargetIdentifier] = slot;
                }

                resolvedList.Add(new ResolvedBinding(
                    source, entry.SourceAxis, slot, entry.TargetAxis));
            }

            _resolvedBindings = resolvedList.Count == 0
                ? Array.Empty<ResolvedBinding>()
                : resolvedList.ToArray();

            // Step 2: ユニーク bone のスロット配列と pre-alloc 済 snapshot buffer を作る。
            // 初期回転 (rest pose) があればスロット側に保持し、毎フレームの加算ベースとする。
            int slotCount = boneNameToSlot.Count;
            _slots = slotCount == 0 ? Array.Empty<BonePoseSlot>() : new BonePoseSlot[slotCount];
            foreach (var kv in boneNameToSlot)
            {
                var rest = UnityEngine.Vector3.zero;
                if (restPoses != null && restPoses.TryGetValue(kv.Key, out var pose))
                {
                    rest = pose;
                }
                _slots[kv.Value] = new BonePoseSlot(kv.Key, rest);
            }

            _snapshotBuffer = slotCount == 0 ? Array.Empty<BoneSnapshot>() : new BoneSnapshot[slotCount];
            // 初期 snapshot に rest pose を反映する。BonePath は構築後不変なので毎フレーム書換える必要なし。
            for (int s = 0; s < slotCount; s++)
            {
                var slot = _slots[s];
                _snapshotBuffer[s] = new BoneSnapshot(
                    slot.BoneName,
                    0f, 0f, 0f,
                    slot.RestEulerX, slot.RestEulerY, slot.RestEulerZ,
                    1f, 1f, 1f);
            }
        }

        /// <summary>
        /// per-frame に呼出され、binding 評価 → <see cref="BoneSnapshot"/> 列構築 →
        /// <see cref="IBonePoseProvider.SetActiveBoneSnapshots"/> を 1 回行う。
        /// </summary>
        public void BuildAndPush()
        {
            if (_disposed)
            {
                return;
            }

            int slotCount = _slots.Length;

            // 全スロットの (X, Y, Z) を rest pose にリセットする。
            // 以後の binding 評価結果はこのベースに加算される。
            for (int s = 0; s < slotCount; s++)
            {
                _slots[s].EulerX = _slots[s].RestEulerX;
                _slots[s].EulerY = _slots[s].RestEulerY;
                _slots[s].EulerZ = _slots[s].RestEulerZ;
            }

            // 各 binding を評価して該当 (slot, axis) に sum で加算する。
            int rbCount = _resolvedBindings.Length;
            for (int i = 0; i < rbCount; i++)
            {
                var rb = _resolvedBindings[i];
                var source = rb.Source;
                if (!source.IsValid)
                {
                    continue;
                }
                if (rb.SourceAxis < 0 || rb.SourceAxis >= source.AxisCount)
                {
                    continue;
                }
                if (!TryReadAxis(source, rb.SourceAxis, out float raw))
                {
                    continue;
                }

                // dead-zone / scale / offset / curve / invert / clamp の値変換は
                // Adapters 側 InputProcessor 経路で扱うため、生値をそのまま加算する。
                ref var slot = ref _slots[rb.SlotIndex];
                switch (rb.TargetAxis)
                {
                    case AnalogTargetAxis.X:
                        slot.EulerX += raw;
                        break;
                    case AnalogTargetAxis.Y:
                        slot.EulerY += raw;
                        break;
                    case AnalogTargetAxis.Z:
                        slot.EulerZ += raw;
                        break;
                }
            }

            // pre-alloc 済 _snapshotBuffer に値を書込む（BonePath は ctor で確定済、
            // Position / Scale は default で固定）。
            for (int s = 0; s < slotCount; s++)
            {
                var slot = _slots[s];
                _snapshotBuffer[s] = new BoneSnapshot(
                    slot.BoneName,
                    0f, 0f, 0f,
                    slot.EulerX, slot.EulerY, slot.EulerZ,
                    1f, 1f, 1f);
            }

            _boneProvider.SetActiveBoneSnapshots(new ReadOnlyMemory<BoneSnapshot>(_snapshotBuffer, 0, slotCount));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _disposed = true;
        }

        private static bool TryReadAxis(IAnalogInputSource source, int axis, out float value)
        {
            if (axis < 0 || axis >= source.AxisCount)
            {
                value = 0f;
                return false;
            }

            if (source.AxisCount == 1)
            {
                return source.TryReadScalar(out value);
            }

            if (source.AxisCount == 2)
            {
                if (source.TryReadVector2(out float x, out float y))
                {
                    value = axis == 0 ? x : y;
                    return true;
                }
                value = 0f;
                return false;
            }

            // N-axis: 軸数分のスタック領域に書込んで 1 軸抽出する。
            Span<float> buf = stackalloc float[source.AxisCount];
            if (source.TryReadAxes(buf))
            {
                value = buf[axis];
                return true;
            }
            value = 0f;
            return false;
        }

        private struct BonePoseSlot
        {
            public string BoneName;
            public float EulerX;
            public float EulerY;
            public float EulerZ;
            public readonly float RestEulerX;
            public readonly float RestEulerY;
            public readonly float RestEulerZ;

            public BonePoseSlot(string boneName, UnityEngine.Vector3 rest)
            {
                BoneName = boneName;
                EulerX = rest.x;
                EulerY = rest.y;
                EulerZ = rest.z;
                RestEulerX = rest.x;
                RestEulerY = rest.y;
                RestEulerZ = rest.z;
            }
        }

        private readonly struct ResolvedBinding
        {
            public readonly IAnalogInputSource Source;
            public readonly int SourceAxis;
            public readonly int SlotIndex;
            public readonly AnalogTargetAxis TargetAxis;

            public ResolvedBinding(
                IAnalogInputSource source,
                int sourceAxis,
                int slotIndex,
                AnalogTargetAxis targetAxis)
            {
                Source = source;
                SourceAxis = sourceAxis;
                SlotIndex = slotIndex;
                TargetAxis = targetAxis;
            }
        }
    }
}
