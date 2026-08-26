using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Json.Dto;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.ScriptableObject.Serializable
{
    /// <summary>
    /// Base Expression 用の最小 Serializable 型。
    /// Expression 固有の id/name/layer/kind などのメタデータは持たない。
    /// </summary>
    [Serializable]
    public sealed class BaseExpressionSerializable
    {
        [Tooltip("ベース表情の AnimationClip。未設定なら全 BlendShape 0 のベースとして扱う。")]
        public AnimationClip animationClip;

        [Tooltip("AutoExporter がベイクしたベース表情のサンプリング結果。ExpressionSnapshotDto を流用する。")]
        public ExpressionSnapshotDto cachedSnapshot = CreateEmptySnapshot();

        public bool IsEmpty => IsSnapshotEmpty(cachedSnapshot);

        public ExpressionSnapshotDto EnsureCachedSnapshot()
        {
            if (cachedSnapshot == null)
            {
                cachedSnapshot = CreateEmptySnapshot();
            }
            else if (cachedSnapshot.blendShapes == null)
            {
                cachedSnapshot.blendShapes = new List<BlendShapeSnapshotDto>();
            }

            return cachedSnapshot;
        }

        public static ExpressionSnapshotDto CreateEmptySnapshot()
        {
            return new ExpressionSnapshotDto
            {
                blendShapes = new List<BlendShapeSnapshotDto>(),
            };
        }

        public static bool IsSnapshotEmpty(ExpressionSnapshotDto snapshot)
        {
            return snapshot == null
                   || snapshot.blendShapes == null
                   || snapshot.blendShapes.Count == 0;
        }
    }
}
