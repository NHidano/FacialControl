using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// 表情定義。AnimationClip 由来の <see cref="ExpressionSnapshot"/> を <see cref="SnapshotId"/> で参照する不変オブジェクト。
    /// <para>
    /// Domain の primary identity は (Id, Name, Layer, OverrideMask, SnapshotId) の 5 値。
    /// Bridge field（<see cref="TransitionDuration"/> / <see cref="TransitionCurve"/> /
    /// <see cref="BlendShapeValues"/>）は互換目的で保持され、将来的に
    /// <see cref="ExpressionSnapshot"/> 経路へ移行後に撤去予定。
    /// </para>
    /// </summary>
    public readonly struct Expression
    {
        /// <summary>
        /// 表情遷移時間の既定値（秒）。1 / 15 秒 ≒ 0.0667 秒。
        /// SnapshotId / Bridge ctor、<see cref="ExpressionSnapshot"/> の既定値、
        /// <see cref="Hidano.FacialControl.Adapters.ScriptableObject.Serializable.ExpressionSerializable"/>
        /// 等、本値を参照する全箇所で source-of-truth として用いる。
        /// </summary>
        public const float DefaultTransitionDuration = 1f / 15f;

        /// <summary>
        /// 一意識別子（GUID）
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// 表情名
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 所属レイヤー名
        /// </summary>
        public string Layer { get; }

        /// <summary>
        /// 他レイヤーへのオーバーライド対象を示すビットフラグ。
        /// </summary>
        public LayerOverrideMask OverrideMask { get; }

        /// <summary>
        /// AnimationClip 由来の <see cref="ExpressionSnapshot"/> を識別する snapshot id。
        /// 通常は <see cref="Id"/> と同値で運用するが、別 snapshot を参照することも許される。
        /// </summary>
        public string SnapshotId { get; }

        // ===== Bridge fields (将来撤去予定) =====

        /// <summary>
        /// [Bridge] 遷移時間（0〜1 秒、範囲外は自動クランプ）。
        /// <see cref="ExpressionSnapshot.TransitionDuration"/> 経路へ移行後に撤去予定。
        /// </summary>
        public float TransitionDuration { get; }

        /// <summary>
        /// [Bridge] 遷移カーブ設定。<see cref="TransitionCurvePreset"/> 経路へ移行後に撤去予定。
        /// </summary>
        public TransitionCurve TransitionCurve { get; }

        /// <summary>
        /// [Bridge] BlendShape 値の配列。<see cref="ExpressionSnapshot.BlendShapes"/> 経路へ移行後に撤去予定。
        /// </summary>
        public ReadOnlyMemory<BlendShapeMapping> BlendShapeValues { get; }

        /// <summary>
        /// Trigger / Analog 入力で重ね合わせる overlay の解決状態を slot 単位で宣言する。
        /// 各要素は <see cref="OverlaySlotBinding.Suppress"/> と
        /// <see cref="OverlaySlotBinding.Snapshot"/> の組み合わせで default fallback /
        /// 明示 suppress / 個別 snapshot override の 3 状態を表す。
        /// </summary>
        /// <remarks>
        /// 解決経路 (<c>OverlayInputSource</c>) は本フィールドを優先し、
        /// 該当 slot が未宣言、または binding が default fallback の場合に
        /// <see cref="FacialProfile.DefaultOverlays"/> へ fallback する。
        /// </remarks>
        public ReadOnlyMemory<OverlaySlotBinding> Overlays { get; }

        /// <summary>
        /// SnapshotId 参照型コンストラクタ。
        /// Bridge field（TransitionDuration / TransitionCurve / BlendShapeValues）は default 値で初期化される。
        /// </summary>
        /// <param name="id">一意識別子（空文字不可）</param>
        /// <param name="name">表情名（空文字不可）</param>
        /// <param name="layer">所属レイヤー名（空文字不可）</param>
        /// <param name="overrideMask">オーバーライド対象レイヤーのビットフラグ</param>
        /// <param name="snapshotId">AnimationClip 由来 snapshot の識別子（空文字不可）</param>
        public Expression(
            string id,
            string name,
            string layer,
            LayerOverrideMask overrideMask,
            string snapshotId,
            OverlaySlotBinding[] overlays = null)
        {
            if (id == null)
                throw new ArgumentNullException(nameof(id));
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("ID を空にすることはできません。", nameof(id));
            if (name == null)
                throw new ArgumentNullException(nameof(name));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("名前を空にすることはできません。", nameof(name));
            if (layer == null)
                throw new ArgumentNullException(nameof(layer));
            if (string.IsNullOrWhiteSpace(layer))
                throw new ArgumentException("レイヤー名を空にすることはできません。", nameof(layer));
            if (snapshotId == null)
                throw new ArgumentNullException(nameof(snapshotId));
            if (string.IsNullOrWhiteSpace(snapshotId))
                throw new ArgumentException("SnapshotId を空にすることはできません。", nameof(snapshotId));

            Id = id;
            Name = name;
            Layer = layer;
            OverrideMask = overrideMask;
            SnapshotId = snapshotId;

            TransitionDuration = DefaultTransitionDuration;
            TransitionCurve = default;
            BlendShapeValues = Array.Empty<BlendShapeMapping>();
            Overlays = CopyOverlays(overlays);
        }

        /// <summary>
        /// [Bridge ctor] 撤去予定の旧 API。
        /// 新規コードは SnapshotId 受けの ctor を使用すること。
        /// 配列パラメータは防御的コピーされる。
        /// </summary>
        /// <param name="id">一意識別子（空文字不可）</param>
        /// <param name="name">表情名（空文字不可）</param>
        /// <param name="layer">所属レイヤー名（空文字不可）</param>
        /// <param name="transitionDuration">遷移時間（0〜1 秒、範囲外は自動クランプ）</param>
        /// <param name="transitionCurve">遷移カーブ設定</param>
        /// <param name="blendShapeValues">BlendShape 値の配列。null の場合は空配列</param>
        /// <param name="overlays">overlay slot binding の配列。null の場合は空配列</param>
        /// <param name="overrideMask">
        /// この表情がアクティブな間、抑制対象とする他レイヤーのビットフラグ。
        /// 既定値 <see cref="LayerOverrideMask.None"/>（後方互換）。
        /// </param>
        public Expression(
            string id,
            string name,
            string layer,
            float transitionDuration = DefaultTransitionDuration,
            TransitionCurve transitionCurve = default,
            BlendShapeMapping[] blendShapeValues = null,
            OverlaySlotBinding[] overlays = null,
            LayerOverrideMask overrideMask = LayerOverrideMask.None)
        {
            if (id == null)
                throw new ArgumentNullException(nameof(id));
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("ID を空にすることはできません。", nameof(id));
            if (name == null)
                throw new ArgumentNullException(nameof(name));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("名前を空にすることはできません。", nameof(name));
            if (layer == null)
                throw new ArgumentNullException(nameof(layer));
            if (string.IsNullOrWhiteSpace(layer))
                throw new ArgumentException("レイヤー名を空にすることはできません。", nameof(layer));

            Id = id;
            Name = name;
            Layer = layer;
            OverrideMask = overrideMask;
            SnapshotId = id;
            TransitionDuration = Math.Clamp(transitionDuration, 0f, 1f);
            TransitionCurve = transitionCurve;

            if (blendShapeValues != null)
            {
                var bsCopy = new BlendShapeMapping[blendShapeValues.Length];
                Array.Copy(blendShapeValues, bsCopy, blendShapeValues.Length);
                BlendShapeValues = bsCopy;
            }
            else
            {
                BlendShapeValues = Array.Empty<BlendShapeMapping>();
            }

            Overlays = CopyOverlays(overlays);
        }

        private static ReadOnlyMemory<OverlaySlotBinding> CopyOverlays(OverlaySlotBinding[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<OverlaySlotBinding>();
            }
            var copy = new OverlaySlotBinding[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }

        /// <summary>
        /// 指定 slot の overlay 解決状態を表す <see cref="OverlaySlotBinding"/> を検索する。
        /// </summary>
        /// <param name="slot">検索対象 slot 名。null / 空文字なら false。</param>
        /// <param name="binding">見つかった binding。見つからなければ default。</param>
        /// <returns>当該 slot の binding を宣言していれば true（suppress / snapshot / default fallback を含む）、未宣言なら false。</returns>
        public bool TryGetOverlay(string slot, out OverlaySlotBinding binding)
        {
            binding = default;
            if (string.IsNullOrEmpty(slot))
            {
                return false;
            }

            var span = Overlays.Span;
            for (int i = 0; i < span.Length; i++)
            {
                if (string.Equals(span[i].Slot, slot, StringComparison.Ordinal))
                {
                    binding = span[i];
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 表情の人間可読表現を返す（<c>{Id}:{Name}@{Layer}</c> 形式）。
        /// </summary>
        public override string ToString()
        {
            return $"{Id}:{Name}@{Layer}";
        }
    }
}
