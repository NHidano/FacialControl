using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// 表情定義。BlendShape 値と遷移設定を保持する不変オブジェクト。
    /// </summary>
    public readonly struct Expression
    {
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
        /// 遷移時間（0〜1 秒、範囲外は自動クランプ）
        /// </summary>
        public float TransitionDuration { get; }

        /// <summary>
        /// 遷移カーブ設定
        /// </summary>
        public TransitionCurve TransitionCurve { get; }

        /// <summary>
        /// BlendShape 値の配列
        /// </summary>
        public ReadOnlyMemory<BlendShapeMapping> BlendShapeValues { get; }

        /// <summary>
        /// 他レイヤーへのオーバーライドスロット配列
        /// </summary>
        public ReadOnlyMemory<LayerSlot> LayerSlots { get; }

        /// <summary>
        /// 表情定義を生成する。配列パラメータは防御的コピーされる。
        /// </summary>
        /// <param name="id">一意識別子（空文字不可）</param>
        /// <param name="name">表情名（空文字不可）</param>
        /// <param name="layer">所属レイヤー名（空文字不可）</param>
        /// <param name="transitionDuration">遷移時間（0〜1 秒、範囲外は自動クランプ）</param>
        /// <param name="transitionCurve">遷移カーブ設定</param>
        /// <param name="blendShapeValues">BlendShape 値の配列。null の場合は空配列</param>
        /// <param name="layerSlots">他レイヤーへのオーバーライドスロット配列。null の場合は空配列</param>
        public Expression(
            string id,
            string name,
            string layer,
            float transitionDuration = 0.25f,
            TransitionCurve transitionCurve = default,
            BlendShapeMapping[] blendShapeValues = null,
            LayerSlot[] layerSlots = null)
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
            TransitionDuration = Math.Clamp(transitionDuration, 0f, 1f);

            TransitionCurve = transitionCurve;

            // 防御的コピーで不変性を保証
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

            if (layerSlots != null)
            {
                var slotCopy = new LayerSlot[layerSlots.Length];
                Array.Copy(layerSlots, slotCopy, layerSlots.Length);
                LayerSlots = slotCopy;
            }
            else
            {
                LayerSlots = Array.Empty<LayerSlot>();
            }
        }
    }
}
