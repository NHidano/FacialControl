using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Json.Dto;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.ScriptableObject.Serializable
{
    /// <summary>
    /// <see cref="Hidano.FacialControl.Domain.Models.OverlaySlotBinding"/> の Unity Serializable 投影。
    /// SO Inspector で編集される。suppress / cachedSnapshot で
    /// default fallback / 明示 suppress / 個別 snapshot override を表現する。
    /// </summary>
    [Serializable]
    public sealed class OverlaySlotBindingSerializable
    {
        [Tooltip("slot 識別子（例: blink）。")]
        public string slot;

        [Tooltip("suppress=true で AnimationClip と cachedSnapshot は無視されます。")]
        public bool suppress;

        [Tooltip("個別 override 用の AnimationClip。Exporter が cachedSnapshot にベイクします。")]
        public AnimationClip animationClip;

        [Tooltip("Exporter がベイクした overlay snapshot。空の場合は default fallback として扱います。")]
        public OverlaySnapshotDto cachedSnapshot;

        /// <summary>
        /// overlay 用の空 snapshot を生成する。overlay binding の cachedSnapshot は
        /// 再帰終端型 <see cref="OverlaySnapshotDto"/> なので、base/expression 用の
        /// <see cref="BaseExpressionSerializable.CreateEmptySnapshot"/>（ExpressionSnapshotDto）と区別する。
        /// </summary>
        public static OverlaySnapshotDto CreateEmptySnapshot()
        {
            return new OverlaySnapshotDto
            {
                blendShapes = new List<BlendShapeSnapshotDto>(),
            };
        }

        /// <summary>
        /// overlay snapshot が「実質空」（BlendShape を 1 つも持たない）かを判定する。
        /// </summary>
        public static bool IsSnapshotEmpty(OverlaySnapshotDto snapshot)
        {
            return snapshot == null
                   || snapshot.blendShapes == null
                   || snapshot.blendShapes.Count == 0;
        }
    }

    public enum OverlaySlotBindingState
    {
        DefaultFallback,
        Suppress,
        Override,
    }

    public static class OverlaySlotBindingSerializableExtensions
    {
        public static OverlaySlotBindingState GetState(this OverlaySlotBindingSerializable serializable)
        {
            if (serializable == null) throw new ArgumentNullException(nameof(serializable));
            if (serializable.suppress) return OverlaySlotBindingState.Suppress;
            if (serializable.animationClip != null) return OverlaySlotBindingState.Override;
            return OverlaySlotBindingState.DefaultFallback;
        }
    }
}
