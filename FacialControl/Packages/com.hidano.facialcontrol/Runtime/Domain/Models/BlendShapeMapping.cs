using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// BlendShape 名と値のマッピング。値は 0〜1 に正規化される。
    /// </summary>
    public readonly struct BlendShapeMapping
    {
        /// <summary>
        /// BlendShape 名
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 値（0〜1 正規化、範囲外は自動クランプ）
        /// </summary>
        public float Value { get; }

        /// <summary>
        /// 対象 Renderer 名。null の場合は全 SkinnedMeshRenderer に適用。
        /// </summary>
        public string Renderer { get; }

        public BlendShapeMapping(string name, float value, string renderer = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Value = Math.Clamp(value, 0f, 1f);
            Renderer = renderer;
        }
    }
}
