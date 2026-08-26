using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.InputSources
{
    public sealed class OverlayInputSource : ValueProviderInputSourceBase
    {
        public const string ReservedIdPrefix = "overlay";

        private readonly string _slot;
        private readonly string _emotionLayerName;
        private readonly IActiveExpressionProvider _activeProvider;
        private readonly Dictionary<SlotKey, ResolvedSnapshot> _resolvedBySlot;
        private readonly BitArray _activeMask;
        private readonly BitArray _emptyMask;
        private readonly bool _slotDeclared;
        private bool _logged;

        // フォニーム予約 slot (a/i/u/e/o) は静的出力しない（常に無効ソース）。
        // 音素 Override は「口形状 snapshot の差し替え」であり、駆動 weight
        // （音素 weight × 音量）ごと LipSyncPhonemeOverlayInputSource 側で合成される。
        // ここで snapshot を静的出力すると「表情中ずっと 100% 出力」になり誤り。
        // 登録自体はレイヤー配線互換（overlay:{slot} 宣言）のため許容する。
        private readonly bool _phonemeSlotInert;

        // ---- クロスフェード状態 ----
        // 解決結果 (default / override / suppress) が切替わった際、旧出力から新出力へ
        // 表情遷移と同じ duration/curve で補間する。
        private readonly bool _crossfadeEnabled;
        private readonly float[] _currentValues;
        private readonly float[] _fromValues;
        private readonly float[] _targetValues;
        private readonly BitArray _fromMask;
        private BitArray _targetMask;
        // 現在ターゲットの同一性キー (ResolvedSnapshot.Values の参照)。null = 無効ターゲット (suppress / 解決なし)。
        private float[] _targetKey;
        private bool _targetActive;
        private bool _resolvedOnce;
        private float _elapsedTime;
        private float _duration;
        private TransitionCurve _curve;
        private bool _transitionComplete = true;

        public OverlayInputSource(
            InputSourceId id,
            string slot,
            int blendShapeCount,
            IReadOnlyList<string> blendShapeNames,
            FacialProfile profile,
            IActiveExpressionProvider activeProvider,
            string emotionLayerName)
            : base(id, blendShapeCount)
        {
            if (string.IsNullOrWhiteSpace(slot))
            {
                throw new ArgumentException("slot must not be empty.", nameof(slot));
            }
            if (blendShapeNames == null)
            {
                throw new ArgumentNullException(nameof(blendShapeNames));
            }

            _slot = slot;
            _activeProvider = activeProvider;
            _emotionLayerName = string.IsNullOrEmpty(emotionLayerName) ? "emotion" : emotionLayerName;
            _activeMask = new BitArray(blendShapeCount, false);
            _emptyMask = new BitArray(blendShapeCount, false);
            _phonemeSlotInert = PhonemeOverlaySlots.IsReserved(slot);
            _crossfadeEnabled = !_phonemeSlotInert;
            _currentValues = blendShapeCount == 0 ? Array.Empty<float>() : new float[blendShapeCount];
            _fromValues = blendShapeCount == 0 ? Array.Empty<float>() : new float[blendShapeCount];
            _targetValues = blendShapeCount == 0 ? Array.Empty<float>() : new float[blendShapeCount];
            _fromMask = new BitArray(blendShapeCount, false);
            _targetMask = _emptyMask;
            _curve = TransitionCurve.Linear;
            _resolvedBySlot = new Dictionary<SlotKey, ResolvedSnapshot>(
                profile.Expressions.Length + profile.DefaultOverlays.Length,
                EqualityComparer<SlotKey>.Default);

            _slotDeclared = ContainsSlot(profile.Slots.Span, _slot);
            if (!_slotDeclared)
            {
                LogUndeclaredSlotOnce();
                return;
            }

            var nameToIndex = new Dictionary<string, int>(blendShapeNames.Count, StringComparer.Ordinal);
            for (int i = 0; i < blendShapeNames.Count; i++)
            {
                string name = blendShapeNames[i];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }
                if (!nameToIndex.ContainsKey(name))
                {
                    nameToIndex.Add(name, i);
                }
            }

            var exprSpan = profile.Expressions.Span;
            for (int i = 0; i < exprSpan.Length; i++)
            {
                var expression = exprSpan[i];
                var overlaySpan = expression.Overlays.Span;
                for (int j = 0; j < overlaySpan.Length; j++)
                {
                    var binding = overlaySpan[j];
                    if (!string.Equals(binding.Slot, _slot, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (binding.IsDefaultFallback)
                    {
                        continue;
                    }

                    var key = new SlotKey(expression.Id, _slot);
                    if (!_resolvedBySlot.ContainsKey(key))
                    {
                        _resolvedBySlot.Add(
                            key,
                            ResolvedSnapshot.Build(binding, nameToIndex, blendShapeCount));
                    }
                }
            }

            var defaultOverlaySpan = profile.DefaultOverlays.Span;
            for (int i = 0; i < defaultOverlaySpan.Length; i++)
            {
                var binding = defaultOverlaySpan[i];
                if (!string.Equals(binding.Slot, _slot, StringComparison.Ordinal))
                {
                    continue;
                }
                if (binding.IsDefaultFallback)
                {
                    continue;
                }

                var key = new SlotKey(null, _slot);
                if (!_resolvedBySlot.ContainsKey(key))
                {
                    _resolvedBySlot.Add(
                        key,
                        ResolvedSnapshot.Build(binding, nameToIndex, blendShapeCount));
                }
            }
        }

        public override BitArray ContributeMask => HasActiveOutput ? _activeMask : _emptyMask;

        // ターゲットが有効か、無効ターゲットへのフェードアウトが進行中の間は寄与を継続する。
        private bool HasActiveOutput => _targetActive || !_transitionComplete;

        /// <summary>
        /// 解決結果の切替検出とクロスフェードの時間進行。
        /// <see cref="LayerInputSourceAggregator"/> から毎フレーム
        /// <see cref="TryWriteValues"/> の直前に呼ばれる。
        /// </summary>
        public override void Tick(float deltaTime)
        {
            if (!_slotDeclared || _phonemeSlotInert)
            {
                return;
            }

            RefreshResolution();

            if (_transitionComplete)
            {
                return;
            }

            if (deltaTime > 0f)
            {
                _elapsedTime += deltaTime;
            }

            float weight = TransitionCalculator.ComputeBlendWeight(_curve, _elapsedTime, _duration);
            for (int i = 0; i < BlendShapeCount; i++)
            {
                _currentValues[i] = _fromValues[i] + (_targetValues[i] - _fromValues[i]) * weight;
            }

            if (_elapsedTime >= _duration)
            {
                SnapToTarget();
            }
        }

        public override bool TryWriteValues(Span<float> output)
        {
            if (!_slotDeclared || _phonemeSlotInert)
            {
                return false;
            }

            RefreshResolution();

            if (!HasActiveOutput)
            {
                return false;
            }

            int copyLen = output.Length < BlendShapeCount ? output.Length : BlendShapeCount;
            for (int i = 0; i < copyLen; i++)
            {
                output[i] = _currentValues[i];
            }

            return true;
        }

        /// <summary>
        /// active provider から現在の解決結果を求め、ターゲットが切替わっていれば
        /// クロスフェード遷移を開始する（初回解決とフォニーム予約 slot・遷移時間 0 は即時反映）。
        /// duration/curve は表情側の遷移と同期させるため、切替時点の active 表情の
        /// <see cref="Expression.TransitionDuration"/> / <see cref="Expression.TransitionCurve"/> を使い、
        /// active 不在（解除でデフォルトへ戻る）時は既定リリース
        /// (<see cref="Expression.DefaultTransitionDuration"/> + Linear) を使う。
        /// </summary>
        private void RefreshResolution()
        {
            ResolvedSnapshot resolved = default;
            bool found = false;
            Expression? active = null;

            if (_activeProvider != null)
            {
                active = _activeProvider.TryGetTopActiveExpression(_emotionLayerName);
                if (active.HasValue)
                {
                    var activeKey = new SlotKey(active.Value.Id, _slot);
                    found = _resolvedBySlot.TryGetValue(activeKey, out resolved);
                }
            }

            if (!found)
            {
                found = _resolvedBySlot.TryGetValue(new SlotKey(null, _slot), out resolved);
            }

            bool targetActive = found && !resolved.Suppress && resolved.HasSnapshot;
            float[] newKey = targetActive ? resolved.Values : null;
            if (_resolvedOnce && ReferenceEquals(newKey, _targetKey))
            {
                return;
            }

            bool isFirstResolution = !_resolvedOnce;
            _resolvedOnce = true;
            _targetKey = newKey;
            _targetActive = targetActive;
            _targetMask = targetActive ? resolved.Mask : _emptyMask;
            BuildDenseTargetValues(targetActive ? resolved : default);

            if (isFirstResolution || !_crossfadeEnabled)
            {
                SnapToTarget();
                return;
            }

            float duration = active.HasValue
                ? active.Value.TransitionDuration
                : Expression.DefaultTransitionDuration;
            if (duration <= 0f)
            {
                SnapToTarget();
                return;
            }

            // 現在出力値からのクロスフェードを開始する。遷移中の mask は from ∪ target。
            Array.Copy(_currentValues, _fromValues, BlendShapeCount);
            CopyMaskInto(_activeMask, _fromMask);
            _activeMask.SetAll(false);
            _activeMask.Or(_fromMask);
            OrMaskInto(_targetMask, _activeMask);
            _duration = duration;
            _curve = active.HasValue ? active.Value.TransitionCurve : TransitionCurve.Linear;
            _elapsedTime = 0f;
            _transitionComplete = false;
        }

        private void SnapToTarget()
        {
            Array.Copy(_targetValues, _currentValues, BlendShapeCount);
            _activeMask.SetAll(false);
            OrMaskInto(_targetMask, _activeMask);
            _transitionComplete = true;
        }

        private void BuildDenseTargetValues(in ResolvedSnapshot resolved)
        {
            Array.Clear(_targetValues, 0, _targetValues.Length);
            var indices = resolved.Indices;
            var values = resolved.Values;
            if (indices == null || values == null)
            {
                return;
            }

            for (int i = 0; i < indices.Length; i++)
            {
                int index = indices[i];
                if ((uint)index < (uint)_targetValues.Length)
                {
                    _targetValues[index] = values[i];
                }
            }
        }

        private static void CopyMaskInto(BitArray source, BitArray destination)
        {
            destination.SetAll(false);
            destination.Or(source);
        }

        private void OrMaskInto(BitArray source, BitArray destination)
        {
            int length = destination.Length;
            for (int i = 0; i < length; i++)
            {
                if (i < source.Length && source[i])
                {
                    destination[i] = true;
                }
            }
        }

        private void LogUndeclaredSlotOnce()
        {
            if (_logged)
            {
                return;
            }

            _logged = true;
            Debug.LogWarning(
                $"[OverlayInputSource] slot='{_slot}' is not declared in profile.Slots. Overlay input source will stay inactive.");
        }

        private static bool ContainsSlot(ReadOnlySpan<string> slots, string slot)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (string.Equals(slots[i], slot, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal readonly struct SlotKey : IEquatable<SlotKey>
    {
        public readonly string ExpressionId;
        public readonly string Slot;
        private readonly int _hashCode;

        public SlotKey(string expressionId, string slot)
        {
            ExpressionId = expressionId;
            Slot = slot;

            unchecked
            {
                int slotHash = slot != null ? StringComparer.Ordinal.GetHashCode(slot) : 0;
                int expressionHash = expressionId != null ? StringComparer.Ordinal.GetHashCode(expressionId) : 0;
                _hashCode = (slotHash * 397) ^ expressionHash;
            }
        }

        public bool Equals(SlotKey other)
        {
            return string.Equals(Slot, other.Slot, StringComparison.Ordinal)
                && string.Equals(ExpressionId, other.ExpressionId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is SlotKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }
    }

    internal readonly struct ResolvedSnapshot
    {
        public readonly bool Suppress;
        public readonly bool HasSnapshot;
        public readonly int[] Indices;
        public readonly float[] Values;
        public readonly BitArray Mask;

        public ResolvedSnapshot(
            bool suppress,
            bool hasSnapshot,
            int[] indices,
            float[] values,
            BitArray mask)
        {
            Suppress = suppress;
            HasSnapshot = hasSnapshot;
            Indices = indices ?? Array.Empty<int>();
            Values = values ?? Array.Empty<float>();
            Mask = mask;
        }

        public static ResolvedSnapshot Build(
            OverlaySlotBinding binding,
            IReadOnlyDictionary<string, int> nameToIndex,
            int blendShapeCount)
        {
            if (binding.Suppress)
            {
                return new ResolvedSnapshot(
                    suppress: true,
                    hasSnapshot: false,
                    indices: Array.Empty<int>(),
                    values: Array.Empty<float>(),
                    mask: new BitArray(blendShapeCount, false));
            }

            if (!binding.Snapshot.HasValue)
            {
                return new ResolvedSnapshot(
                    suppress: false,
                    hasSnapshot: false,
                    indices: Array.Empty<int>(),
                    values: Array.Empty<float>(),
                    mask: new BitArray(blendShapeCount, false));
            }

            return Build(binding.Snapshot.Value, nameToIndex, blendShapeCount);
        }

        private static ResolvedSnapshot Build(
            ExpressionSnapshot snapshot,
            IReadOnlyDictionary<string, int> nameToIndex,
            int blendShapeCount)
        {
            var blendShapes = snapshot.BlendShapes.Span;
            int count = 0;
            for (int i = 0; i < blendShapes.Length; i++)
            {
                string name = blendShapes[i].Name;
                if (!string.IsNullOrEmpty(name) && nameToIndex.ContainsKey(name))
                {
                    count++;
                }
            }

            int[] indices = count == 0 ? Array.Empty<int>() : new int[count];
            float[] values = count == 0 ? Array.Empty<float>() : new float[count];
            var mask = new BitArray(blendShapeCount, false);

            int writeIndex = 0;
            for (int i = 0; i < blendShapes.Length; i++)
            {
                string name = blendShapes[i].Name;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }
                if (!nameToIndex.TryGetValue(name, out int index))
                {
                    continue;
                }

                indices[writeIndex] = index;
                values[writeIndex] = blendShapes[i].Value;
                mask[index] = true;
                writeIndex++;
            }

            return new ResolvedSnapshot(
                suppress: false,
                hasSnapshot: true,
                indices: indices,
                values: values,
                mask: mask);
        }
    }
}
