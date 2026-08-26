namespace Hidano.FacialControl.Adapters.Json.Dto
{
    /// <summary>
    /// <see cref="Hidano.FacialControl.Domain.Models.AnalogBindingEntry"/> の JSON 永続化 DTO。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="targetKind"/> は文字列で永続化（"blendshape" / "bonepose"、大小無視）。
    /// <see cref="targetAxis"/> は文字列で永続化（"X" / "Y" / "Z"、BlendShape ターゲット時は無視）。
    /// 不正値はローダ側で <c>Debug.LogWarning</c> + skip 扱い。
    /// </para>
    /// <para>
    /// <see cref="scale"/> と <see cref="direction"/> は BlendShape clip 由来 binding 用の追加属性。
    /// 旧スキーマ JSON では欠落しているため、欠落・空・不正値は default 値（scale=1, direction=Bipolar）で
    /// fallback する。BonePose ターゲットは原則 scale=1 / direction=Bipolar で使う。
    /// </para>
    /// </remarks>
    [System.Serializable]
    public sealed class AnalogBindingEntryDto
    {
        /// <summary>入力源 ID 文字列。</summary>
        public string sourceId;

        /// <summary>入力源軸番号（&gt;= 0、scalar=0 / Vector2 で X=0,Y=1）。</summary>
        public int sourceAxis;

        /// <summary>ターゲット種別文字列（"blendshape" / "bonepose"、大小無視）。</summary>
        public string targetKind;

        /// <summary>ターゲット識別子（BlendShape 名 または bone 名）。</summary>
        public string targetIdentifier;

        /// <summary>BonePose ターゲットの Euler 軸文字列（"X"/"Y"/"Z"、大小無視）。BlendShape では無視。</summary>
        public string targetAxis;

        /// <summary>
        /// 反映倍率。clip 由来 binding では keyframe weight が入る。
        /// JsonUtility が欠落フィールドに 0 を入れてしまうため、ローダ側で 0 は default（1f）扱いする。
        /// </summary>
        public float scale;

        /// <summary>
        /// 入力符号フィルタ ("bipolar" / "positive" / "negative"、大小無視)。空・不正は Bipolar 扱い。
        /// </summary>
        public string direction;
    }
}
