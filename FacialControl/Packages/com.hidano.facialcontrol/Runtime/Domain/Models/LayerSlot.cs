using System;

namespace Hidano.FacialControl.Domain.Models
{
    /// <summary>
    /// 他レイヤーへのオーバーライド値を保持するスロット。
    /// Expression がアクティブになったときにターゲットレイヤーの BlendShape 値を完全置換する。
    /// </summary>
    public readonly struct LayerSlot
    {
        /// <summary>
        /// ターゲットレイヤー名
        /// </summary>
        public string Layer { get; }

        /// <summary>
        /// オーバーライドする BlendShape 値の配列
        /// </summary>
        public ReadOnlyMemory<BlendShapeMapping> BlendShapeValues { get; }

        /// <summary>
        /// レイヤースロットを生成する。blendShapeValues は防御的コピーされる。
        /// </summary>
        /// <param name="layer">ターゲットレイヤー名（空文字不可）</param>
        /// <param name="blendShapeValues">オーバーライドする BlendShape 値の配列</param>
        public LayerSlot(string layer, BlendShapeMapping[] blendShapeValues)
        {
            if (layer == null)
                throw new ArgumentNullException(nameof(layer));
            if (string.IsNullOrWhiteSpace(layer))
                throw new ArgumentException("レイヤー名を空にすることはできません。", nameof(layer));
            if (blendShapeValues == null)
                throw new ArgumentNullException(nameof(blendShapeValues));

            Layer = layer;
            // 防御的コピーで不変性を保証
            var copy = new BlendShapeMapping[blendShapeValues.Length];
            Array.Copy(blendShapeValues, copy, blendShapeValues.Length);
            BlendShapeValues = copy;
        }
    }
}
