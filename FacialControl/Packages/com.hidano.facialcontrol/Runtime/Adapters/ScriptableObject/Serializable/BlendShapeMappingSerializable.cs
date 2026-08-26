using System;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.ScriptableObject.Serializable
{
    /// <summary>
    /// BlendShape 名と値のマッピング (旧 JSON: blendShapeValues[])。
    /// <see cref="Hidano.FacialControl.Domain.Models.BlendShapeMapping"/> の Unity Serializable 投影。
    /// </summary>
    [Serializable]
    public sealed class BlendShapeMappingSerializable
    {
        [Tooltip("対象 BlendShape 名。SkinnedMeshRenderer 上の BlendShape と完全一致させる。")]
        public string name;

        [Tooltip("BlendShape ウェイト値 (0〜1 正規化)。Unity 内部では 0〜100 にスケールされる。")]
        [Range(0f, 1f)]
        public float value;

        [Tooltip("対象 Renderer の絞り込み (省略可)。空なら全 SkinnedMeshRenderer に適用。")]
        public string renderer;
    }
}
