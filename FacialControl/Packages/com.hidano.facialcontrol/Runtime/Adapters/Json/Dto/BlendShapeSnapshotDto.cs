namespace Hidano.FacialControl.Adapters.Json.Dto
{
    /// <summary>
    /// 中間 JSON Schema v2.0: <c>expressions[].snapshot.blendShapes[]</c> の 1 エントリ。
    /// AnimationClip サンプリング由来の単一 BlendShape 値を JSON へ運搬する DTO。
    /// JsonUtility 互換のため <see cref="System.SerializableAttribute"/> を付与する。
    /// <para>
    /// Domain 側の対応値型は <see cref="Hidano.FacialControl.Domain.Models.BlendShapeSnapshot"/>。
    /// </para>
    /// </summary>
    [System.Serializable]
    public sealed class BlendShapeSnapshotDto
    {
        /// <summary>
        /// 対象 SkinnedMeshRenderer の Transform 階層パス（例: "Body" / "Armature/Head/Mesh"）。
        /// </summary>
        public string rendererPath;

        /// <summary>BlendShape 名。多バイト文字を含む任意の文字列を受理する。</summary>
        public string name;

        /// <summary>時刻 0 における AnimationCurve.Evaluate(0f) の値。</summary>
        public float value;
    }
}
