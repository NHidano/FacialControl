using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// AnimationClip サンプリング由来の単一 BlendShape スナップショット値型。
    /// (RendererPath, Name, Value) の triple を不変に保持する Domain 値型で、
    /// Unity 非依存（UnityEngine.* を参照しない）。
    /// <para>
    /// readonly struct + immutable string + float のため Domain レベルで防御コピーは不要。
    /// 配列としての防御コピー責務は ExpressionSnapshot 側が持つ。
    /// </para>
    /// </summary>
    public readonly struct BlendShapeSnapshot : IEquatable<BlendShapeSnapshot>
    {
        /// <summary>BlendShape を保持する SkinnedMeshRenderer の Transform 階層パス（null は空文字に正規化）。</summary>
        public string RendererPath { get; }

        /// <summary>BlendShape 名（null は空文字に正規化）。</summary>
        public string Name { get; }

        /// <summary>時刻 0 における AnimationCurve.Evaluate(0f) の値。</summary>
        public float Value { get; }

        /// <summary>
        /// BlendShapeSnapshot を生成する。
        /// </summary>
        /// <param name="rendererPath">SkinnedMeshRenderer 階層パス（null は空文字扱い）</param>
        /// <param name="name">BlendShape 名（null は空文字扱い）</param>
        /// <param name="value">時刻 0 の値</param>
        public BlendShapeSnapshot(string rendererPath, string name, float value)
        {
            RendererPath = rendererPath ?? string.Empty;
            Name = name ?? string.Empty;
            Value = value;
        }

        public bool Equals(BlendShapeSnapshot other)
        {
            return string.Equals(RendererPath, other.RendererPath, StringComparison.Ordinal)
                && string.Equals(Name, other.Name, StringComparison.Ordinal)
                && Value.Equals(other.Value);
        }

        public override bool Equals(object obj) => obj is BlendShapeSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (RendererPath != null ? StringComparer.Ordinal.GetHashCode(RendererPath) : 0);
                hash = (hash * 31) + (Name != null ? StringComparer.Ordinal.GetHashCode(Name) : 0);
                hash = (hash * 31) + Value.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(BlendShapeSnapshot left, BlendShapeSnapshot right) => left.Equals(right);

        public static bool operator !=(BlendShapeSnapshot left, BlendShapeSnapshot right) => !left.Equals(right);
    }
}
