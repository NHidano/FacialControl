using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// AnimationClip サンプリング由来の表情スナップショット値型。
    /// 1 つの AnimationClip を時刻 0 で評価して得られる
    /// (Id, TransitionDuration, TransitionCurvePreset, BlendShapes[], Bones[], RendererPaths[]) を
    /// 不変に保持する Domain 値型で、Unity 非依存（UnityEngine.* を参照しない）。
    /// <para>
    /// 配列防御コピー責務は本型のコンストラクタが担う。外部からの書換不可性は
    /// <see cref="ReadOnlyMemory{T}"/> 経由の公開によって保証する（Span<T> 経由の書換不可）。
    /// </para>
    /// <para>
    /// 注意: 既存 <see cref="TransitionCurve"/> との衝突を避けるため、本型は
    /// <see cref="TransitionCurvePreset"/> enum を用いる。Phase 3 で
    /// <see cref="TransitionCurve"/> 側を物理削除予定（research.md Topic 9 参照）。
    /// </para>
    /// </summary>
    public readonly struct ExpressionSnapshot : IEquatable<ExpressionSnapshot>
    {
        /// <summary>表情を一意に識別する Id（null は空文字に正規化）。</summary>
        public string Id { get; }

        /// <summary>表情遷移時間（秒）。0〜1 秒、デフォルトは <see cref="Expression.DefaultTransitionDuration"/>（1/15 秒）。</summary>
        public float TransitionDuration { get; }

        /// <summary>遷移カーブプリセット（Linear / EaseIn / EaseOut / EaseInOut）。デフォルト Linear。</summary>
        public TransitionCurvePreset TransitionCurvePreset { get; }

        /// <summary>BlendShape スナップショット配列。外部から書換不可（防御コピー済）。</summary>
        public ReadOnlyMemory<BlendShapeSnapshot> BlendShapes { get; }

        /// <summary>Bone スナップショット配列。外部から書換不可（防御コピー済）。</summary>
        public ReadOnlyMemory<BoneSnapshot> Bones { get; }

        /// <summary>サマリ用 SkinnedMeshRenderer Transform 階層パス配列。外部から書換不可（防御コピー済）。</summary>
        public ReadOnlyMemory<string> RendererPaths { get; }

        /// <summary>
        /// ExpressionSnapshot を生成する。配列引数は防御コピーされる。
        /// </summary>
        /// <param name="id">表情 Id（null は空文字扱い）</param>
        /// <param name="transitionDuration">遷移時間（秒）</param>
        /// <param name="transitionCurvePreset">遷移カーブプリセット</param>
        /// <param name="blendShapes">BlendShape スナップショット配列（null は空 Memory）</param>
        /// <param name="bones">Bone スナップショット配列（null は空 Memory）</param>
        /// <param name="rendererPaths">RendererPath サマリ配列（null は空 Memory、要素 null は空文字に正規化）</param>
        public ExpressionSnapshot(
            string id,
            float transitionDuration,
            TransitionCurvePreset transitionCurvePreset,
            BlendShapeSnapshot[] blendShapes,
            BoneSnapshot[] bones,
            string[] rendererPaths)
        {
            Id = id ?? string.Empty;
            TransitionDuration = transitionDuration;
            TransitionCurvePreset = transitionCurvePreset;
            BlendShapes = CloneOrEmpty(blendShapes);
            Bones = CloneOrEmpty(bones);
            RendererPaths = CloneRendererPaths(rendererPaths);
        }

        /// <summary>
        /// デフォルト遷移時間 (<see cref="Expression.DefaultTransitionDuration"/>, 1/15 秒) / Linear カーブ /
        /// 空配列で snapshot を生成する。fallback / placeholder 用途。
        /// </summary>
        public static ExpressionSnapshot CreateDefault(string id)
        {
            return new ExpressionSnapshot(
                id,
                transitionDuration: Expression.DefaultTransitionDuration,
                transitionCurvePreset: Models.TransitionCurvePreset.Linear,
                blendShapes: null,
                bones: null,
                rendererPaths: null);
        }

        public bool Equals(ExpressionSnapshot other)
        {
            return string.Equals(Id, other.Id, StringComparison.Ordinal)
                && TransitionDuration.Equals(other.TransitionDuration)
                && TransitionCurvePreset == other.TransitionCurvePreset
                && BlendShapes.Equals(other.BlendShapes)
                && Bones.Equals(other.Bones)
                && RendererPaths.Equals(other.RendererPaths);
        }

        public override bool Equals(object obj) => obj is ExpressionSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (Id != null ? StringComparer.Ordinal.GetHashCode(Id) : 0);
                hash = (hash * 31) + TransitionDuration.GetHashCode();
                hash = (hash * 31) + (int)TransitionCurvePreset;
                hash = (hash * 31) + BlendShapes.Length;
                hash = (hash * 31) + Bones.Length;
                hash = (hash * 31) + RendererPaths.Length;
                return hash;
            }
        }

        public static bool operator ==(ExpressionSnapshot left, ExpressionSnapshot right) => left.Equals(right);

        public static bool operator !=(ExpressionSnapshot left, ExpressionSnapshot right) => !left.Equals(right);

        private static ReadOnlyMemory<T> CloneOrEmpty<T>(T[] source)
        {
            if (source == null || source.Length == 0)
            {
                return ReadOnlyMemory<T>.Empty;
            }
            var copy = new T[source.Length];
            Array.Copy(source, copy, source.Length);
            return new ReadOnlyMemory<T>(copy);
        }

        private static ReadOnlyMemory<string> CloneRendererPaths(string[] source)
        {
            if (source == null || source.Length == 0)
            {
                return ReadOnlyMemory<string>.Empty;
            }
            var copy = new string[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                copy[i] = source[i] ?? string.Empty;
            }
            return new ReadOnlyMemory<string>(copy);
        }
    }
}
