using System;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Domain.Services
{
    /// <summary>
    /// レイヤー優先度に基づくウェイトブレンドと layerSlots オーバーライドを行う静的サービス。
    /// 全メソッドは GC フリーで動作する（事前確保済み配列の再利用を前提）。
    /// </summary>
    public static class LayerBlender
    {
        /// <summary>
        /// レイヤーブレンドの入力データ。
        /// 各レイヤーの優先度、ウェイト、BlendShape 値を保持する。
        /// </summary>
        public readonly struct LayerInput
        {
            /// <summary>
            /// レイヤー優先度（値が大きいほど優先）
            /// </summary>
            public int Priority { get; }

            /// <summary>
            /// レイヤーウェイト（0〜1、範囲外は自動クランプ）
            /// </summary>
            public float Weight { get; }

            /// <summary>
            /// レイヤーの BlendShape ウェイト値
            /// </summary>
            public ReadOnlyMemory<float> BlendShapeValues { get; }

            public LayerInput(int priority, float weight, float[] blendShapeValues)
            {
                Priority = priority;
                Weight = weight;
                BlendShapeValues = blendShapeValues ?? Array.Empty<float>();
            }
        }

        /// <summary>
        /// レイヤー優先度に基づく BlendShape ウェイトブレンドを計算する。
        /// 低優先度から順に適用し、高優先度のレイヤーが weight に応じて上書きする。
        /// weight=1.0 で完全上書き、weight=0.0 で下位レイヤー値が維持される。
        /// </summary>
        /// <param name="layers">ブレンド対象のレイヤー入力配列</param>
        /// <param name="output">出力先 BlendShape ウェイト配列</param>
        public static void Blend(ReadOnlySpan<LayerInput> layers, Span<float> output)
        {
            if (layers.Length == 0 || output.Length == 0)
                return;

            // 優先度順にソートするためインデックス配列を使用
            // スタック上で処理（小規模レイヤー数を想定）
            Span<int> indices = layers.Length <= 16
                ? stackalloc int[layers.Length]
                : new int[layers.Length];

            for (int i = 0; i < layers.Length; i++)
                indices[i] = i;

            // 優先度昇順でソート（挿入ソート: レイヤー数が少ないため効率的）
            for (int i = 1; i < indices.Length; i++)
            {
                int key = indices[i];
                int j = i - 1;
                while (j >= 0 && layers[indices[j]].Priority > layers[key].Priority)
                {
                    indices[j + 1] = indices[j];
                    j--;
                }
                indices[j + 1] = key;
            }

            // 安定ソート: 同一優先度では元の入力順を維持（挿入ソートなので自然に安定）

            // 最低優先度のレイヤーで初期化
            int firstIdx = indices[0];
            float firstWeight = Clamp01(layers[firstIdx].Weight);
            var firstValues = layers[firstIdx].BlendShapeValues.Span;
            int blendLength = Math.Min(output.Length, firstValues.Length);

            for (int i = 0; i < blendLength; i++)
            {
                output[i] = Clamp01(firstValues[i] * firstWeight);
            }

            // 残りのレイヤーを優先度順に上書きブレンド
            for (int layerIdx = 1; layerIdx < indices.Length; layerIdx++)
            {
                int idx = indices[layerIdx];
                float weight = Clamp01(layers[idx].Weight);
                var values = layers[idx].BlendShapeValues.Span;
                int len = Math.Min(output.Length, values.Length);

                for (int i = 0; i < len; i++)
                {
                    // 線形補間: output[i] = lerp(output[i], values[i], weight)
                    output[i] = Clamp01(output[i] + (values[i] - output[i]) * weight);
                }
            }
        }

        /// <summary>
        /// レイヤーブレンド: 配列ベースのオーバーロード。
        /// </summary>
        public static void Blend(LayerInput[] layers, float[] output)
        {
            Blend(
                new ReadOnlySpan<LayerInput>(layers),
                new Span<float>(output));
        }

        /// <summary>
        /// LayerSlot によるオーバーライドを適用する。
        /// 指定された BlendShape 名に一致する値を完全置換する。
        /// </summary>
        /// <param name="blendShapeNames">BlendShape 名のリスト（output と同じインデックス）</param>
        /// <param name="slots">適用する LayerSlot 配列</param>
        /// <param name="output">上書き対象の BlendShape ウェイト配列</param>
        public static void ApplyLayerSlotOverrides(ReadOnlySpan<string> blendShapeNames, LayerSlot[] slots, Span<float> output)
        {
            if (slots == null || slots.Length == 0)
                return;

            for (int s = 0; s < slots.Length; s++)
            {
                var slotValues = slots[s].BlendShapeValues.Span;

                for (int v = 0; v < slotValues.Length; v++)
                {
                    string targetName = slotValues[v].Name;
                    float targetValue = slotValues[v].Value;

                    // BlendShape 名でインデックスを検索
                    for (int i = 0; i < blendShapeNames.Length && i < output.Length; i++)
                    {
                        if (blendShapeNames[i] == targetName)
                        {
                            output[i] = Clamp01(targetValue);
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// LayerSlot オーバーライド: 配列ベースのオーバーロード。
        /// </summary>
        public static void ApplyLayerSlotOverrides(string[] blendShapeNames, LayerSlot[] slots, float[] output)
        {
            ApplyLayerSlotOverrides(
                new ReadOnlySpan<string>(blendShapeNames),
                slots,
                new Span<float>(output));
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
