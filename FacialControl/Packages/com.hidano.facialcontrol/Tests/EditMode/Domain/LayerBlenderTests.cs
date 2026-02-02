using System;
using System.Collections.Generic;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    public class LayerBlenderTests
    {
        // --- 優先度順ブレンド ---

        [Test]
        public void Blend_SingleLayer_ReturnsLayerValues()
        {
            // 単一レイヤーの場合、そのレイヤーの値がそのまま出力される
            var layers = new LayerBlender.LayerInput[]
            {
                new LayerBlender.LayerInput(
                    priority: 0,
                    weight: 1.0f,
                    blendShapeValues: new float[] { 0.5f, 0.8f, 0.3f })
            };
            var output = new float[3];

            LayerBlender.Blend(layers, output);

            Assert.AreEqual(0.5f, output[0], 0.0001f);
            Assert.AreEqual(0.8f, output[1], 0.0001f);
            Assert.AreEqual(0.3f, output[2], 0.0001f);
        }

        [Test]
        public void Blend_TwoLayers_HigherPriorityOverridesLower()
        {
            // 高優先度レイヤーが低優先度レイヤーを上書き（weight=1.0の場合）
            var layers = new LayerBlender.LayerInput[]
            {
                new LayerBlender.LayerInput(
                    priority: 0,
                    weight: 1.0f,
                    blendShapeValues: new float[] { 1.0f, 0.0f }),
                new LayerBlender.LayerInput(
                    priority: 1,
                    weight: 1.0f,
                    blendShapeValues: new float[] { 0.0f, 1.0f })
            };
            var output = new float[2];

            LayerBlender.Blend(layers, output);

            // 高優先度(priority=1)が完全に上書き
            Assert.AreEqual(0.0f, output[0], 0.0001f);
            Assert.AreEqual(1.0f, output[1], 0.0001f);
        }

        [Test]
        public void Blend_TwoLayers_PartialWeight_BlendsBetweenLayers()
        {
            // 高優先度レイヤーの weight が 0.5 の場合、低優先度と半分ずつブレンド
            var layers = new LayerBlender.LayerInput[]
            {
                new LayerBlender.LayerInput(
                    priority: 0,
                    weight: 1.0f,
                    blendShapeValues: new float[] { 1.0f, 0.0f }),
                new LayerBlender.LayerInput(
                    priority: 1,
                    weight: 0.5f,
                    blendShapeValues: new float[] { 0.0f, 1.0f })
            };
            var output = new float[2];

            LayerBlender.Blend(layers, output);

            // lerp(base, high, 0.5): lerp(1.0, 0.0, 0.5)=0.5, lerp(0.0, 1.0, 0.5)=0.5
            Assert.AreEqual(0.5f, output[0], 0.0001f);
            Assert.AreEqual(0.5f, output[1], 0.0001f);
        }

        [Test]
        public void Blend_TwoLayers_ZeroWeight_LowerLayerUnchanged()
        {
            // 高優先度レイヤーの weight=0 の場合、低優先度がそのまま残る
            var layers = new LayerBlender.LayerInput[]
            {
                new LayerBlender.LayerInput(
                    priority: 0,
                    weight: 1.0f,
                    blendShapeValues: new float[] { 0.8f, 0.6f }),
                new LayerBlender.LayerInput(
                    priority: 1,
                    weight: 0.0f,
                    blendShapeValues: new float[] { 0.2f, 0.4f })
            };
            var output = new float[2];

            LayerBlender.Blend(layers, output);

            Assert.AreEqual(0.8f, output[0], 0.0001f);
            Assert.AreEqual(0.6f, output[1], 0.0001f);
        }

        [Test]
        public void Blend_ThreeLayers_PriorityOrder_HighestWins()
        {
            // 3 レイヤーで最高優先度が完全に上書き
            var layers = new LayerBlender.LayerInput[]
            {
                new LayerBlender.LayerInput(
                    priority: 0,
                    weight: 1.0f,
                    blendShapeValues: new float[] { 0.1f }),
                new LayerBlender.LayerInput(
                    priority: 1,
                    weight: 1.0f,
                    blendShapeValues: new float[] { 0.5f }),
                new LayerBlender.LayerInput(
                    priority: 2,
                    weight: 1.0f,
                    blendShapeValues: new float[] { 0.9f })
            };
            var output = new float[1];

            LayerBlender.Blend(layers, output);

            Assert.AreEqual(0.9f, output[0], 0.0001f);
        }

        [Test]
        public void Blend_ThreeLayers_MiddlePartialWeight_BlendCorrectly()
        {
            // 3 レイヤーで中間のみ部分ウェイト
            var layers = new LayerBlender.LayerInput[]
            {
                new LayerBlender.LayerInput(
                    priority: 0,
                    weight: 1.0f,
                    blendShapeValues: new float[] { 0.0f }),
                new LayerBlender.LayerInput(
                    priority: 1,
                    weight: 0.5f,
                    blendShapeValues: new float[] { 1.0f }),
                new LayerBlender.LayerInput(
                    priority: 2,
                    weight: 1.0f,
                    blendShapeValues: new float[] { 0.8f })
            };
            var output = new float[1];

            LayerBlender.Blend(layers, output);

            // 低→中: lerp(0.0, 1.0, 0.5) = 0.5
            // 中結果→高: lerp(0.5, 0.8, 1.0) = 0.8
            Assert.AreEqual(0.8f, output[0], 0.0001f);
        }

        [Test]
        public void Blend_UnsortedPriority_SortsBeforeBlending()
        {
            // 入力が優先度順でなくても正しくソートして処理される
            var layers = new LayerBlender.LayerInput[]
            {
                new LayerBlender.LayerInput(
                    priority: 2,
                    weight: 1.0f,
                    blendShapeValues: new float[] { 0.9f }),
                new LayerBlender.LayerInput(
                    priority: 0,
                    weight: 1.0f,
                    blendShapeValues: new float[] { 0.1f }),
                new LayerBlender.LayerInput(
                    priority: 1,
                    weight: 1.0f,
                    blendShapeValues: new float[] { 0.5f })
            };
            var output = new float[1];

            LayerBlender.Blend(layers, output);

            // 最高優先度(priority=2)の値が最終出力
            Assert.AreEqual(0.9f, output[0], 0.0001f);
        }

        [Test]
        public void Blend_EmptyLayers_OutputRemainsZero()
        {
            // レイヤーが空の場合、出力はゼロのまま
            var layers = Array.Empty<LayerBlender.LayerInput>();
            var output = new float[] { 0.0f, 0.0f };

            LayerBlender.Blend(layers, output);

            Assert.AreEqual(0.0f, output[0], 0.0001f);
            Assert.AreEqual(0.0f, output[1], 0.0001f);
        }

        [Test]
        public void Blend_EmptyOutput_NoException()
        {
            // 出力配列が空でも例外なく動作
            var layers = new LayerBlender.LayerInput[]
            {
                new LayerBlender.LayerInput(
                    priority: 0,
                    weight: 1.0f,
                    blendShapeValues: new float[] { 0.5f })
            };
            var output = Array.Empty<float>();

            Assert.DoesNotThrow(() => LayerBlender.Blend(layers, output));
        }

        [Test]
        public void Blend_WeightClamped_BelowZero_TreatedAsZero()
        {
            // 負のウェイトは 0 にクランプ
            var layers = new LayerBlender.LayerInput[]
            {
                new LayerBlender.LayerInput(
                    priority: 0,
                    weight: 1.0f,
                    blendShapeValues: new float[] { 0.8f }),
                new LayerBlender.LayerInput(
                    priority: 1,
                    weight: -0.5f,
                    blendShapeValues: new float[] { 0.2f })
            };
            var output = new float[1];

            LayerBlender.Blend(layers, output);

            // 負のウェイトは0扱い → 低優先度の値がそのまま
            Assert.AreEqual(0.8f, output[0], 0.0001f);
        }

        [Test]
        public void Blend_WeightClamped_AboveOne_TreatedAsOne()
        {
            // 1 を超えるウェイトは 1 にクランプ
            var layers = new LayerBlender.LayerInput[]
            {
                new LayerBlender.LayerInput(
                    priority: 0,
                    weight: 1.0f,
                    blendShapeValues: new float[] { 0.8f }),
                new LayerBlender.LayerInput(
                    priority: 1,
                    weight: 1.5f,
                    blendShapeValues: new float[] { 0.2f })
            };
            var output = new float[1];

            LayerBlender.Blend(layers, output);

            // 1を超えるウェイトは1扱い → 高優先度の値に完全置換
            Assert.AreEqual(0.2f, output[0], 0.0001f);
        }

        [Test]
        public void Blend_OutputClampedToZeroOne()
        {
            // 出力は 0〜1 にクランプされる
            var layers = new LayerBlender.LayerInput[]
            {
                new LayerBlender.LayerInput(
                    priority: 0,
                    weight: 1.0f,
                    blendShapeValues: new float[] { 0.5f })
            };
            var output = new float[1];

            LayerBlender.Blend(layers, output);

            Assert.GreaterOrEqual(output[0], 0f);
            Assert.LessOrEqual(output[0], 1f);
        }

        [Test]
        public void Blend_LargeArray_AllElementsCorrect()
        {
            // 大きい配列でも全要素が正しく処理される
            int size = 100;
            var lowValues = new float[size];
            var highValues = new float[size];
            for (int i = 0; i < size; i++)
            {
                lowValues[i] = 0.0f;
                highValues[i] = 1.0f;
            }

            var layers = new LayerBlender.LayerInput[]
            {
                new LayerBlender.LayerInput(priority: 0, weight: 1.0f, blendShapeValues: lowValues),
                new LayerBlender.LayerInput(priority: 1, weight: 0.75f, blendShapeValues: highValues)
            };
            var output = new float[size];

            LayerBlender.Blend(layers, output);

            for (int i = 0; i < size; i++)
            {
                Assert.AreEqual(0.75f, output[i], 0.0001f);
            }
        }

        // --- Span オーバーロード ---

        [Test]
        public void Blend_Span_SingleLayer_ReturnsLayerValues()
        {
            // Span ベースのオーバーロードでも正しく動作
            var layers = new LayerBlender.LayerInput[]
            {
                new LayerBlender.LayerInput(
                    priority: 0,
                    weight: 1.0f,
                    blendShapeValues: new float[] { 0.5f, 0.8f })
            };
            var output = new float[2];

            LayerBlender.Blend(new ReadOnlySpan<LayerBlender.LayerInput>(layers), new Span<float>(output));

            Assert.AreEqual(0.5f, output[0], 0.0001f);
            Assert.AreEqual(0.8f, output[1], 0.0001f);
        }

        // --- layerSlots オーバーライド（完全置換） ---

        [Test]
        public void ApplyLayerSlotOverrides_SingleSlot_ReplacesTargetLayerValues()
        {
            // layerSlot のオーバーライドがターゲットレイヤーの値を完全置換する
            var blendShapeNames = new string[] { "bs_a", "bs_b", "bs_c" };
            var currentOutput = new float[] { 0.5f, 0.5f, 0.5f };
            var slot = new LayerSlot("lipsync", new BlendShapeMapping[]
            {
                new BlendShapeMapping("bs_a", 1.0f),
                new BlendShapeMapping("bs_c", 0.0f)
            });

            LayerBlender.ApplyLayerSlotOverrides(
                new ReadOnlySpan<string>(blendShapeNames),
                new LayerSlot[] { slot },
                new Span<float>(currentOutput));

            // bs_a はオーバーライドで 1.0 に置換
            Assert.AreEqual(1.0f, currentOutput[0], 0.0001f);
            // bs_b はオーバーライドに含まれないのでそのまま
            Assert.AreEqual(0.5f, currentOutput[1], 0.0001f);
            // bs_c はオーバーライドで 0.0 に置換
            Assert.AreEqual(0.0f, currentOutput[2], 0.0001f);
        }

        [Test]
        public void ApplyLayerSlotOverrides_MultipleSlots_AllApplied()
        {
            // 複数の LayerSlot が全て適用される
            var blendShapeNames = new string[] { "bs_a", "bs_b", "bs_c" };
            var currentOutput = new float[] { 0.0f, 0.0f, 0.0f };

            var slot1 = new LayerSlot("emotion", new BlendShapeMapping[]
            {
                new BlendShapeMapping("bs_a", 0.8f)
            });
            var slot2 = new LayerSlot("lipsync", new BlendShapeMapping[]
            {
                new BlendShapeMapping("bs_b", 0.6f),
                new BlendShapeMapping("bs_c", 0.4f)
            });

            LayerBlender.ApplyLayerSlotOverrides(
                new ReadOnlySpan<string>(blendShapeNames),
                new LayerSlot[] { slot1, slot2 },
                new Span<float>(currentOutput));

            Assert.AreEqual(0.8f, currentOutput[0], 0.0001f);
            Assert.AreEqual(0.6f, currentOutput[1], 0.0001f);
            Assert.AreEqual(0.4f, currentOutput[2], 0.0001f);
        }

        [Test]
        public void ApplyLayerSlotOverrides_EmptySlots_NoChange()
        {
            // 空の LayerSlot 配列の場合、出力は変更されない
            var blendShapeNames = new string[] { "bs_a" };
            var currentOutput = new float[] { 0.5f };

            LayerBlender.ApplyLayerSlotOverrides(
                new ReadOnlySpan<string>(blendShapeNames),
                Array.Empty<LayerSlot>(),
                new Span<float>(currentOutput));

            Assert.AreEqual(0.5f, currentOutput[0], 0.0001f);
        }

        [Test]
        public void ApplyLayerSlotOverrides_UnknownBlendShapeName_Skipped()
        {
            // 存在しない BlendShape 名のオーバーライドはスキップされる
            var blendShapeNames = new string[] { "bs_a", "bs_b" };
            var currentOutput = new float[] { 0.5f, 0.5f };

            var slot = new LayerSlot("emotion", new BlendShapeMapping[]
            {
                new BlendShapeMapping("bs_unknown", 1.0f)
            });

            LayerBlender.ApplyLayerSlotOverrides(
                new ReadOnlySpan<string>(blendShapeNames),
                new LayerSlot[] { slot },
                new Span<float>(currentOutput));

            // 変更なし
            Assert.AreEqual(0.5f, currentOutput[0], 0.0001f);
            Assert.AreEqual(0.5f, currentOutput[1], 0.0001f);
        }

        [Test]
        public void ApplyLayerSlotOverrides_OverlappingSlots_LastSlotWins()
        {
            // 複数の LayerSlot が同じ BlendShape を対象にした場合、後のスロットが優先
            var blendShapeNames = new string[] { "bs_a" };
            var currentOutput = new float[] { 0.0f };

            var slot1 = new LayerSlot("emotion", new BlendShapeMapping[]
            {
                new BlendShapeMapping("bs_a", 0.3f)
            });
            var slot2 = new LayerSlot("lipsync", new BlendShapeMapping[]
            {
                new BlendShapeMapping("bs_a", 0.9f)
            });

            LayerBlender.ApplyLayerSlotOverrides(
                new ReadOnlySpan<string>(blendShapeNames),
                new LayerSlot[] { slot1, slot2 },
                new Span<float>(currentOutput));

            // 後の slot2 が優先
            Assert.AreEqual(0.9f, currentOutput[0], 0.0001f);
        }

        [Test]
        public void ApplyLayerSlotOverrides_ValuesClampedToZeroOne()
        {
            // オーバーライド値は BlendShapeMapping で既にクランプ済みだが、出力も 0〜1
            var blendShapeNames = new string[] { "bs_a" };
            var currentOutput = new float[] { 0.0f };

            var slot = new LayerSlot("emotion", new BlendShapeMapping[]
            {
                new BlendShapeMapping("bs_a", 0.7f)
            });

            LayerBlender.ApplyLayerSlotOverrides(
                new ReadOnlySpan<string>(blendShapeNames),
                new LayerSlot[] { slot },
                new Span<float>(currentOutput));

            Assert.GreaterOrEqual(currentOutput[0], 0f);
            Assert.LessOrEqual(currentOutput[0], 1f);
        }

        // --- 配列ベースオーバーロード ---

        [Test]
        public void ApplyLayerSlotOverrides_ArrayOverload_Works()
        {
            // 配列ベースのオーバーロードも正しく動作
            var blendShapeNames = new string[] { "bs_a", "bs_b" };
            var currentOutput = new float[] { 0.5f, 0.5f };

            var slot = new LayerSlot("emotion", new BlendShapeMapping[]
            {
                new BlendShapeMapping("bs_a", 0.9f)
            });

            LayerBlender.ApplyLayerSlotOverrides(
                blendShapeNames,
                new LayerSlot[] { slot },
                currentOutput);

            Assert.AreEqual(0.9f, currentOutput[0], 0.0001f);
            Assert.AreEqual(0.5f, currentOutput[1], 0.0001f);
        }

        // --- 2バイト文字・特殊文字対応 ---

        [Test]
        public void ApplyLayerSlotOverrides_JapaneseBlendShapeNames_MatchesCorrectly()
        {
            // 日本語 BlendShape 名が正しくマッチする
            var blendShapeNames = new string[] { "口_開く", "目_閉じる" };
            var currentOutput = new float[] { 0.0f, 0.0f };

            var slot = new LayerSlot("lipsync", new BlendShapeMapping[]
            {
                new BlendShapeMapping("口_開く", 0.8f)
            });

            LayerBlender.ApplyLayerSlotOverrides(
                new ReadOnlySpan<string>(blendShapeNames),
                new LayerSlot[] { slot },
                new Span<float>(currentOutput));

            Assert.AreEqual(0.8f, currentOutput[0], 0.0001f);
            Assert.AreEqual(0.0f, currentOutput[1], 0.0001f);
        }

        // --- 統合シナリオ ---

        [Test]
        public void BlendAndOverride_FullPipeline_CorrectResult()
        {
            // Blend → ApplyLayerSlotOverrides の完全パイプライン
            // 3 レイヤー（emotion=0, lipsync=1, eye=2）でブレンドし、
            // その後 LayerSlot オーバーライドを適用

            var layers = new LayerBlender.LayerInput[]
            {
                new LayerBlender.LayerInput(
                    priority: 0,
                    weight: 1.0f,
                    blendShapeValues: new float[] { 0.5f, 0.0f, 0.3f }),
                new LayerBlender.LayerInput(
                    priority: 1,
                    weight: 0.5f,
                    blendShapeValues: new float[] { 0.0f, 1.0f, 0.0f }),
                new LayerBlender.LayerInput(
                    priority: 2,
                    weight: 1.0f,
                    blendShapeValues: new float[] { 0.0f, 0.0f, 0.8f })
            };
            var output = new float[3];

            // ステップ1: レイヤーブレンド
            LayerBlender.Blend(layers, output);

            // 低(0): [0.5, 0.0, 0.3]
            // 中(1): lerp(base, mid, 0.5) → [0.25, 0.5, 0.15]
            // 高(2): lerp(mid_result, high, 1.0) → [0.0, 0.0, 0.8]
            Assert.AreEqual(0.0f, output[0], 0.0001f);
            Assert.AreEqual(0.0f, output[1], 0.0001f);
            Assert.AreEqual(0.8f, output[2], 0.0001f);

            // ステップ2: LayerSlot オーバーライド
            var blendShapeNames = new string[] { "bs_a", "bs_b", "bs_c" };
            var overrideSlot = new LayerSlot("lipsync", new BlendShapeMapping[]
            {
                new BlendShapeMapping("bs_b", 0.7f)
            });

            LayerBlender.ApplyLayerSlotOverrides(
                new ReadOnlySpan<string>(blendShapeNames),
                new LayerSlot[] { overrideSlot },
                new Span<float>(output));

            Assert.AreEqual(0.0f, output[0], 0.0001f);
            Assert.AreEqual(0.7f, output[1], 0.0001f);  // オーバーライドで置換
            Assert.AreEqual(0.8f, output[2], 0.0001f);
        }

        [Test]
        public void Blend_SamePriority_InputOrderMaintained()
        {
            // 同一優先度の場合、入力順で後のレイヤーが上書き
            var layers = new LayerBlender.LayerInput[]
            {
                new LayerBlender.LayerInput(
                    priority: 0,
                    weight: 1.0f,
                    blendShapeValues: new float[] { 0.3f }),
                new LayerBlender.LayerInput(
                    priority: 0,
                    weight: 1.0f,
                    blendShapeValues: new float[] { 0.7f })
            };
            var output = new float[1];

            LayerBlender.Blend(layers, output);

            // 同一優先度では後のレイヤーが上書き
            Assert.AreEqual(0.7f, output[0], 0.0001f);
        }
    }
}
