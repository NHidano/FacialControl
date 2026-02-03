using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using Unity.Collections;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.PlayMode.Adapters
{
    /// <summary>
    /// P06-T06: FacialControlMixer の PlayMode テスト。
    /// レイヤーウェイトブレンド、layerSlots オーバーライド、最終出力の統合を検証する。
    /// </summary>
    [TestFixture]
    public class FacialControlMixerTests
    {
        private PlayableGraph _graph;
        private GameObject _gameObject;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("FacialControlMixerTest");
            _graph = PlayableGraph.Create("FacialControlMixerTestGraph");
        }

        [TearDown]
        public void TearDown()
        {
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }
            if (_gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_gameObject);
            }
        }

        // ================================================================
        // 生成・初期化
        // ================================================================

        [Test]
        public void Create_ValidParameters_ReturnsValidPlayable()
        {
            var blendShapeNames = new string[] { "BlendA", "BlendB", "BlendC" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);

            Assert.IsTrue(playable.IsValid());
        }

        [Test]
        public void Create_EmptyBlendShapeNames_ReturnsValidPlayable()
        {
            var blendShapeNames = Array.Empty<string>();
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);

            Assert.IsTrue(playable.IsValid());
        }

        [Test]
        public void GetBehaviour_AfterCreate_ReturnsNonNull()
        {
            var blendShapeNames = new string[] { "BlendA", "BlendB" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);

            var behaviour = playable.GetBehaviour();

            Assert.IsNotNull(behaviour);
        }

        [Test]
        public void BlendShapeCount_ReturnsConfiguredCount()
        {
            var blendShapeNames = new string[] { "BlendA", "BlendB", "BlendC", "BlendD" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);

            var behaviour = playable.GetBehaviour();

            Assert.AreEqual(4, behaviour.BlendShapeCount);
        }

        [Test]
        public void OutputWeights_AfterCreate_AllZero()
        {
            var blendShapeNames = new string[] { "BlendA", "BlendB", "BlendC" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);

            var behaviour = playable.GetBehaviour();
            var output = behaviour.OutputWeights;

            Assert.AreEqual(3, output.Length);
            for (int i = 0; i < output.Length; i++)
            {
                Assert.AreEqual(0f, output[i]);
            }
        }

        // ================================================================
        // レイヤー登録
        // ================================================================

        [Test]
        public void RegisterLayer_SingleLayer_IncrementsLayerCount()
        {
            var blendShapeNames = new string[] { "BlendA", "BlendB" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            var layerPlayable = LayerPlayable.Create(_graph, 2, ExclusionMode.LastWins);
            behaviour.RegisterLayer("emotion", 0, 1.0f, layerPlayable);

            Assert.AreEqual(1, behaviour.LayerCount);
        }

        [Test]
        public void RegisterLayer_MultipleLayers_IncrementsLayerCount()
        {
            var blendShapeNames = new string[] { "BlendA", "BlendB" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            var layer1 = LayerPlayable.Create(_graph, 2, ExclusionMode.LastWins);
            var layer2 = LayerPlayable.Create(_graph, 2, ExclusionMode.Blend);
            behaviour.RegisterLayer("emotion", 0, 1.0f, layer1);
            behaviour.RegisterLayer("lipsync", 1, 1.0f, layer2);

            Assert.AreEqual(2, behaviour.LayerCount);
        }

        // ================================================================
        // SetLayerWeight
        // ================================================================

        [Test]
        public void SetLayerWeight_ExistingLayer_UpdatesWeight()
        {
            var blendShapeNames = new string[] { "BlendA", "BlendB" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            var layerPlayable = LayerPlayable.Create(_graph, 2, ExclusionMode.LastWins);
            behaviour.RegisterLayer("emotion", 0, 1.0f, layerPlayable);

            behaviour.SetLayerWeight("emotion", 0.5f);

            // ブレンド結果で検証（下のブレンドテストで詳細検証）
            Assert.AreEqual(1, behaviour.LayerCount);
        }

        [Test]
        public void SetLayerWeight_NonExistentLayer_DoesNotThrow()
        {
            var blendShapeNames = new string[] { "BlendA" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            Assert.DoesNotThrow(() => behaviour.SetLayerWeight("nonexistent", 0.5f));
        }

        // ================================================================
        // ComputeOutput — 単一レイヤーブレンド
        // ================================================================

        [Test]
        public void ComputeOutput_SingleLayer_FullWeight_PassesThrough()
        {
            var blendShapeNames = new string[] { "BlendA", "BlendB" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            var layerPlayable = LayerPlayable.Create(_graph, 2, ExclusionMode.LastWins);
            var layerBehaviour = layerPlayable.GetBehaviour();
            layerBehaviour.SetTargetExpression("expr-1", new float[] { 0.8f, 0.4f }, 0f, TransitionCurve.Linear);

            behaviour.RegisterLayer("emotion", 0, 1.0f, layerPlayable);
            behaviour.ComputeOutput();

            var output = behaviour.OutputWeights;
            Assert.AreEqual(0.8f, output[0], 0.001f);
            Assert.AreEqual(0.4f, output[1], 0.001f);
        }

        [Test]
        public void ComputeOutput_SingleLayer_HalfWeight_ScalesValues()
        {
            var blendShapeNames = new string[] { "BlendA", "BlendB" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            var layerPlayable = LayerPlayable.Create(_graph, 2, ExclusionMode.LastWins);
            var layerBehaviour = layerPlayable.GetBehaviour();
            layerBehaviour.SetTargetExpression("expr-1", new float[] { 1.0f, 0.6f }, 0f, TransitionCurve.Linear);

            behaviour.RegisterLayer("emotion", 0, 0.5f, layerPlayable);
            behaviour.ComputeOutput();

            var output = behaviour.OutputWeights;
            Assert.AreEqual(0.5f, output[0], 0.001f);
            Assert.AreEqual(0.3f, output[1], 0.001f);
        }

        [Test]
        public void ComputeOutput_SingleLayer_ZeroWeight_OutputZero()
        {
            var blendShapeNames = new string[] { "BlendA", "BlendB" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            var layerPlayable = LayerPlayable.Create(_graph, 2, ExclusionMode.LastWins);
            var layerBehaviour = layerPlayable.GetBehaviour();
            layerBehaviour.SetTargetExpression("expr-1", new float[] { 1.0f, 1.0f }, 0f, TransitionCurve.Linear);

            behaviour.RegisterLayer("emotion", 0, 0.0f, layerPlayable);
            behaviour.ComputeOutput();

            var output = behaviour.OutputWeights;
            Assert.AreEqual(0f, output[0], 0.001f);
            Assert.AreEqual(0f, output[1], 0.001f);
        }

        // ================================================================
        // ComputeOutput — 複数レイヤーブレンド
        // ================================================================

        [Test]
        public void ComputeOutput_TwoLayers_HigherPriorityOverrides()
        {
            var blendShapeNames = new string[] { "BlendA", "BlendB" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            // emotion レイヤー（低優先度）
            var emotionPlayable = LayerPlayable.Create(_graph, 2, ExclusionMode.LastWins);
            var emotionBehaviour = emotionPlayable.GetBehaviour();
            emotionBehaviour.SetTargetExpression("expr-1", new float[] { 1.0f, 0.0f }, 0f, TransitionCurve.Linear);

            // lipsync レイヤー（高優先度）
            var lipsyncPlayable = LayerPlayable.Create(_graph, 2, ExclusionMode.Blend);
            var lipsyncBehaviour = lipsyncPlayable.GetBehaviour();
            lipsyncBehaviour.AddBlendExpression("lip-1", new float[] { 0.0f, 1.0f }, 1.0f);
            lipsyncBehaviour.ComputeBlendOutput();

            behaviour.RegisterLayer("emotion", 0, 1.0f, emotionPlayable);
            behaviour.RegisterLayer("lipsync", 1, 1.0f, lipsyncPlayable);
            behaviour.ComputeOutput();

            var output = behaviour.OutputWeights;
            // lipsync (priority=1, weight=1.0) が emotion (priority=0, weight=1.0) を完全上書き
            Assert.AreEqual(0.0f, output[0], 0.001f);
            Assert.AreEqual(1.0f, output[1], 0.001f);
        }

        [Test]
        public void ComputeOutput_TwoLayers_PartialWeight_Blends()
        {
            var blendShapeNames = new string[] { "BlendA", "BlendB" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            // emotion レイヤー（低優先度）
            var emotionPlayable = LayerPlayable.Create(_graph, 2, ExclusionMode.LastWins);
            var emotionBehaviour = emotionPlayable.GetBehaviour();
            emotionBehaviour.SetTargetExpression("expr-1", new float[] { 1.0f, 0.0f }, 0f, TransitionCurve.Linear);

            // lipsync レイヤー（高優先度、ウェイト 0.5）
            var lipsyncPlayable = LayerPlayable.Create(_graph, 2, ExclusionMode.Blend);
            var lipsyncBehaviour = lipsyncPlayable.GetBehaviour();
            lipsyncBehaviour.AddBlendExpression("lip-1", new float[] { 0.0f, 1.0f }, 1.0f);
            lipsyncBehaviour.ComputeBlendOutput();

            behaviour.RegisterLayer("emotion", 0, 1.0f, emotionPlayable);
            behaviour.RegisterLayer("lipsync", 1, 0.5f, lipsyncPlayable);
            behaviour.ComputeOutput();

            var output = behaviour.OutputWeights;
            // lerp(emotion, lipsync, 0.5)
            // BlendA: lerp(1.0, 0.0, 0.5) = 0.5
            // BlendB: lerp(0.0, 1.0, 0.5) = 0.5
            Assert.AreEqual(0.5f, output[0], 0.01f);
            Assert.AreEqual(0.5f, output[1], 0.01f);
        }

        [Test]
        public void ComputeOutput_ThreeLayers_PriorityOrdering()
        {
            var blendShapeNames = new string[] { "BlendA" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            // 低優先度: 1.0
            var layer0 = LayerPlayable.Create(_graph, 1, ExclusionMode.LastWins);
            layer0.GetBehaviour().SetTargetExpression("e0", new float[] { 1.0f }, 0f, TransitionCurve.Linear);

            // 中優先度: 0.5（weight=0.5）
            var layer1 = LayerPlayable.Create(_graph, 1, ExclusionMode.LastWins);
            layer1.GetBehaviour().SetTargetExpression("e1", new float[] { 0.0f }, 0f, TransitionCurve.Linear);

            // 高優先度: 0.8（weight=1.0）
            var layer2 = LayerPlayable.Create(_graph, 1, ExclusionMode.LastWins);
            layer2.GetBehaviour().SetTargetExpression("e2", new float[] { 0.8f }, 0f, TransitionCurve.Linear);

            behaviour.RegisterLayer("base", 0, 1.0f, layer0);
            behaviour.RegisterLayer("mid", 1, 0.5f, layer1);
            behaviour.RegisterLayer("top", 2, 1.0f, layer2);
            behaviour.ComputeOutput();

            var output = behaviour.OutputWeights;
            // base=1.0 → lerp(1.0, 0.0, 0.5)=0.5 → lerp(0.5, 0.8, 1.0)=0.8
            Assert.AreEqual(0.8f, output[0], 0.01f);
        }

        [Test]
        public void ComputeOutput_NoLayers_OutputRemainsZero()
        {
            var blendShapeNames = new string[] { "BlendA", "BlendB" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            behaviour.ComputeOutput();

            var output = behaviour.OutputWeights;
            Assert.AreEqual(0f, output[0]);
            Assert.AreEqual(0f, output[1]);
        }

        // ================================================================
        // ComputeOutput — SetLayerWeight の反映
        // ================================================================

        [Test]
        public void ComputeOutput_AfterSetLayerWeight_UsesUpdatedWeight()
        {
            var blendShapeNames = new string[] { "BlendA" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            var layerPlayable = LayerPlayable.Create(_graph, 1, ExclusionMode.LastWins);
            layerPlayable.GetBehaviour().SetTargetExpression("expr-1", new float[] { 1.0f }, 0f, TransitionCurve.Linear);

            behaviour.RegisterLayer("emotion", 0, 1.0f, layerPlayable);

            // ウェイトを 0.3 に変更
            behaviour.SetLayerWeight("emotion", 0.3f);
            behaviour.ComputeOutput();

            var output = behaviour.OutputWeights;
            Assert.AreEqual(0.3f, output[0], 0.001f);
        }

        // ================================================================
        // layerSlots オーバーライド
        // ================================================================

        [Test]
        public void ComputeOutput_WithLayerSlots_OverridesSpecificBlendShapes()
        {
            var blendShapeNames = new string[] { "BlendA", "BlendB", "BlendC" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            // emotion レイヤー
            var emotionPlayable = LayerPlayable.Create(_graph, 3, ExclusionMode.LastWins);
            emotionPlayable.GetBehaviour().SetTargetExpression(
                "expr-1", new float[] { 0.8f, 0.5f, 0.3f }, 0f, TransitionCurve.Linear);

            behaviour.RegisterLayer("emotion", 0, 1.0f, emotionPlayable);

            // layerSlots: BlendB を 1.0 にオーバーライド
            var slots = new LayerSlot[]
            {
                new LayerSlot("emotion", new BlendShapeMapping[]
                {
                    new BlendShapeMapping("BlendB", 1.0f)
                })
            };
            behaviour.SetActiveLayerSlots(slots);
            behaviour.ComputeOutput();

            var output = behaviour.OutputWeights;
            Assert.AreEqual(0.8f, output[0], 0.001f);  // 変更なし
            Assert.AreEqual(1.0f, output[1], 0.001f);  // オーバーライド
            Assert.AreEqual(0.3f, output[2], 0.001f);  // 変更なし
        }

        [Test]
        public void ComputeOutput_WithLayerSlots_UnknownName_Skipped()
        {
            var blendShapeNames = new string[] { "BlendA", "BlendB" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            var layerPlayable = LayerPlayable.Create(_graph, 2, ExclusionMode.LastWins);
            layerPlayable.GetBehaviour().SetTargetExpression(
                "expr-1", new float[] { 0.5f, 0.5f }, 0f, TransitionCurve.Linear);

            behaviour.RegisterLayer("emotion", 0, 1.0f, layerPlayable);

            // 存在しない名前のオーバーライド
            var slots = new LayerSlot[]
            {
                new LayerSlot("emotion", new BlendShapeMapping[]
                {
                    new BlendShapeMapping("NonExistent", 1.0f)
                })
            };
            behaviour.SetActiveLayerSlots(slots);
            behaviour.ComputeOutput();

            var output = behaviour.OutputWeights;
            Assert.AreEqual(0.5f, output[0], 0.001f);
            Assert.AreEqual(0.5f, output[1], 0.001f);
        }

        [Test]
        public void ComputeOutput_WithMultipleLayerSlots_AllApplied()
        {
            var blendShapeNames = new string[] { "BlendA", "BlendB", "BlendC" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            var layerPlayable = LayerPlayable.Create(_graph, 3, ExclusionMode.LastWins);
            layerPlayable.GetBehaviour().SetTargetExpression(
                "expr-1", new float[] { 0.5f, 0.5f, 0.5f }, 0f, TransitionCurve.Linear);

            behaviour.RegisterLayer("emotion", 0, 1.0f, layerPlayable);

            // 複数のオーバーライド
            var slots = new LayerSlot[]
            {
                new LayerSlot("emotion", new BlendShapeMapping[]
                {
                    new BlendShapeMapping("BlendA", 0.0f),
                    new BlendShapeMapping("BlendC", 1.0f)
                })
            };
            behaviour.SetActiveLayerSlots(slots);
            behaviour.ComputeOutput();

            var output = behaviour.OutputWeights;
            Assert.AreEqual(0.0f, output[0], 0.001f);  // オーバーライド
            Assert.AreEqual(0.5f, output[1], 0.001f);  // 変更なし
            Assert.AreEqual(1.0f, output[2], 0.001f);  // オーバーライド
        }

        [Test]
        public void ComputeOutput_NullLayerSlots_NoError()
        {
            var blendShapeNames = new string[] { "BlendA" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            var layerPlayable = LayerPlayable.Create(_graph, 1, ExclusionMode.LastWins);
            layerPlayable.GetBehaviour().SetTargetExpression(
                "expr-1", new float[] { 0.7f }, 0f, TransitionCurve.Linear);

            behaviour.RegisterLayer("emotion", 0, 1.0f, layerPlayable);
            behaviour.SetActiveLayerSlots(null);
            behaviour.ComputeOutput();

            var output = behaviour.OutputWeights;
            Assert.AreEqual(0.7f, output[0], 0.001f);
        }

        // ================================================================
        // ClearActiveLayerSlots
        // ================================================================

        [Test]
        public void ClearActiveLayerSlots_RemovesOverrides()
        {
            var blendShapeNames = new string[] { "BlendA", "BlendB" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            var layerPlayable = LayerPlayable.Create(_graph, 2, ExclusionMode.LastWins);
            layerPlayable.GetBehaviour().SetTargetExpression(
                "expr-1", new float[] { 0.5f, 0.5f }, 0f, TransitionCurve.Linear);

            behaviour.RegisterLayer("emotion", 0, 1.0f, layerPlayable);

            var slots = new LayerSlot[]
            {
                new LayerSlot("emotion", new BlendShapeMapping[]
                {
                    new BlendShapeMapping("BlendA", 1.0f)
                })
            };
            behaviour.SetActiveLayerSlots(slots);
            behaviour.ClearActiveLayerSlots();
            behaviour.ComputeOutput();

            var output = behaviour.OutputWeights;
            Assert.AreEqual(0.5f, output[0], 0.001f);  // オーバーライドが消えた
            Assert.AreEqual(0.5f, output[1], 0.001f);
        }

        // ================================================================
        // 日本語 BlendShape 名
        // ================================================================

        [Test]
        public void ComputeOutput_JapaneseBlendShapeNames_OverrideWorks()
        {
            var blendShapeNames = new string[] { "まばたき", "笑顔", "怒り" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            var layerPlayable = LayerPlayable.Create(_graph, 3, ExclusionMode.LastWins);
            layerPlayable.GetBehaviour().SetTargetExpression(
                "expr-1", new float[] { 0.3f, 0.6f, 0.2f }, 0f, TransitionCurve.Linear);

            behaviour.RegisterLayer("emotion", 0, 1.0f, layerPlayable);

            var slots = new LayerSlot[]
            {
                new LayerSlot("emotion", new BlendShapeMapping[]
                {
                    new BlendShapeMapping("笑顔", 1.0f)
                })
            };
            behaviour.SetActiveLayerSlots(slots);
            behaviour.ComputeOutput();

            var output = behaviour.OutputWeights;
            Assert.AreEqual(0.3f, output[0], 0.001f);
            Assert.AreEqual(1.0f, output[1], 0.001f);  // 日本語名でオーバーライド成功
            Assert.AreEqual(0.2f, output[2], 0.001f);
        }

        // ================================================================
        // Dispose
        // ================================================================

        [Test]
        public void Dispose_ReleasesNativeArrays()
        {
            var blendShapeNames = new string[] { "BlendA", "BlendB" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            Assert.DoesNotThrow(() => behaviour.Dispose());
        }

        [Test]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            var blendShapeNames = new string[] { "BlendA", "BlendB" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            behaviour.Dispose();

            Assert.DoesNotThrow(() => behaviour.Dispose());
        }

        // ================================================================
        // BlendShapeNames プロパティ
        // ================================================================

        [Test]
        public void BlendShapeNames_ReturnsConfiguredNames()
        {
            var blendShapeNames = new string[] { "BlendA", "BlendB", "BlendC" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            var names = behaviour.BlendShapeNames;

            Assert.AreEqual(3, names.Length);
            Assert.AreEqual("BlendA", names[0]);
            Assert.AreEqual("BlendB", names[1]);
            Assert.AreEqual("BlendC", names[2]);
        }

        // ================================================================
        // 空の BlendShape
        // ================================================================

        [Test]
        public void ComputeOutput_ZeroBlendShapeCount_DoesNotThrow()
        {
            var blendShapeNames = Array.Empty<string>();
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            var layerPlayable = LayerPlayable.Create(_graph, 0, ExclusionMode.LastWins);
            behaviour.RegisterLayer("emotion", 0, 1.0f, layerPlayable);

            Assert.DoesNotThrow(() => behaviour.ComputeOutput());
        }

        // ================================================================
        // PrepareFrame
        // ================================================================

        [Test]
        public void PrepareFrame_TransitioningLayer_AdvancesTransition()
        {
            var blendShapeNames = new string[] { "BlendA", "BlendB" };
            var mixerPlayable = FacialControlMixer.Create(_graph, blendShapeNames);
            var mixer = mixerPlayable.GetBehaviour();

            var layerPlayable = LayerPlayable.Create(_graph, 2, ExclusionMode.LastWins);
            var layerBehaviour = layerPlayable.GetBehaviour();
            // 遷移時間 1.0 秒で設定 → 遷移中になる
            layerBehaviour.SetTargetExpression("expr-1", new float[] { 1.0f, 0.5f }, 1.0f, TransitionCurve.Linear);

            mixer.RegisterLayer("emotion", 0, 1.0f, layerPlayable);

            Assert.IsTrue(layerBehaviour.IsTransitioning);

            // PrepareFrame を deltaTime=0.5 で呼ぶ（直接呼び出し）
            var frameData = CreateFrameData(0.5f);
            mixer.PrepareFrame(mixerPlayable, frameData);

            // 遷移が進んでいるはず（0→target の 50%）
            var output = mixer.OutputWeights;
            Assert.AreEqual(0.5f, output[0], 0.01f);
            Assert.AreEqual(0.25f, output[1], 0.01f);
            Assert.IsTrue(layerBehaviour.IsTransitioning);
        }

        [Test]
        public void PrepareFrame_CompletesTransition_OutputMatchesTarget()
        {
            var blendShapeNames = new string[] { "BlendA" };
            var mixerPlayable = FacialControlMixer.Create(_graph, blendShapeNames);
            var mixer = mixerPlayable.GetBehaviour();

            var layerPlayable = LayerPlayable.Create(_graph, 1, ExclusionMode.LastWins);
            var layerBehaviour = layerPlayable.GetBehaviour();
            layerBehaviour.SetTargetExpression("expr-1", new float[] { 0.8f }, 0.5f, TransitionCurve.Linear);

            mixer.RegisterLayer("emotion", 0, 1.0f, layerPlayable);

            // 遷移完了分の deltaTime を渡す
            var frameData = CreateFrameData(0.5f);
            mixer.PrepareFrame(mixerPlayable, frameData);

            var output = mixer.OutputWeights;
            Assert.AreEqual(0.8f, output[0], 0.001f);
            Assert.IsFalse(layerBehaviour.IsTransitioning);
        }

        [Test]
        public void PrepareFrame_MultipleLayers_AllTransitionsAdvanced()
        {
            var blendShapeNames = new string[] { "BlendA" };
            var mixerPlayable = FacialControlMixer.Create(_graph, blendShapeNames);
            var mixer = mixerPlayable.GetBehaviour();

            // 2 つのレイヤー、どちらも遷移中
            var layer1 = LayerPlayable.Create(_graph, 1, ExclusionMode.LastWins);
            layer1.GetBehaviour().SetTargetExpression("e1", new float[] { 1.0f }, 1.0f, TransitionCurve.Linear);

            var layer2 = LayerPlayable.Create(_graph, 1, ExclusionMode.LastWins);
            layer2.GetBehaviour().SetTargetExpression("e2", new float[] { 0.5f }, 1.0f, TransitionCurve.Linear);

            mixer.RegisterLayer("emotion", 0, 1.0f, layer1);
            mixer.RegisterLayer("eye", 1, 1.0f, layer2);

            Assert.IsTrue(layer1.GetBehaviour().IsTransitioning);
            Assert.IsTrue(layer2.GetBehaviour().IsTransitioning);

            var frameData = CreateFrameData(1.0f);
            mixer.PrepareFrame(mixerPlayable, frameData);

            // 両方の遷移が完了しているはず
            Assert.IsFalse(layer1.GetBehaviour().IsTransitioning);
            Assert.IsFalse(layer2.GetBehaviour().IsTransitioning);
        }

        [Test]
        public void PrepareFrame_NoTransition_ComputeOutputStillCalled()
        {
            var blendShapeNames = new string[] { "BlendA" };
            var mixerPlayable = FacialControlMixer.Create(_graph, blendShapeNames);
            var mixer = mixerPlayable.GetBehaviour();

            // 即時遷移（duration=0）
            var layerPlayable = LayerPlayable.Create(_graph, 1, ExclusionMode.LastWins);
            layerPlayable.GetBehaviour().SetTargetExpression("expr-1", new float[] { 0.7f }, 0f, TransitionCurve.Linear);

            mixer.RegisterLayer("emotion", 0, 1.0f, layerPlayable);

            var frameData = CreateFrameData(0.016f);
            mixer.PrepareFrame(mixerPlayable, frameData);

            // ComputeOutput が呼ばれて出力に反映されているはず
            var output = mixer.OutputWeights;
            Assert.AreEqual(0.7f, output[0], 0.001f);
        }

        /// <summary>
        /// テスト用に FrameData を生成するヘルパー。
        /// FrameData は struct のため、リフレクションで deltaTime を設定する。
        /// </summary>
        private static FrameData CreateFrameData(float deltaTime)
        {
            var frameData = new FrameData();
            // FrameData.deltaTime は読み取り専用プロパティだが、
            // 内部フィールドにリフレクションでアクセスして設定する
            var type = typeof(FrameData);
            var field = type.GetField("m_DeltaTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                object boxed = frameData;
                field.SetValue(boxed, deltaTime);
                frameData = (FrameData)boxed;
            }
            return frameData;
        }

        // ================================================================
        // レイヤー入力順序の非依存性
        // ================================================================

        [Test]
        public void ComputeOutput_LayersRegisteredOutOfOrder_BlendsByPriority()
        {
            var blendShapeNames = new string[] { "BlendA" };
            var playable = FacialControlMixer.Create(_graph, blendShapeNames);
            var behaviour = playable.GetBehaviour();

            // 高優先度を先に登録
            var highPriority = LayerPlayable.Create(_graph, 1, ExclusionMode.LastWins);
            highPriority.GetBehaviour().SetTargetExpression("e-high", new float[] { 0.2f }, 0f, TransitionCurve.Linear);

            var lowPriority = LayerPlayable.Create(_graph, 1, ExclusionMode.LastWins);
            lowPriority.GetBehaviour().SetTargetExpression("e-low", new float[] { 1.0f }, 0f, TransitionCurve.Linear);

            // 逆順で登録（高優先度を先に）
            behaviour.RegisterLayer("high", 10, 1.0f, highPriority);
            behaviour.RegisterLayer("low", 0, 1.0f, lowPriority);
            behaviour.ComputeOutput();

            var output = behaviour.OutputWeights;
            // 低優先度(1.0) → 高優先度(0.2, weight=1.0) で上書き → 0.2
            Assert.AreEqual(0.2f, output[0], 0.001f);
        }
    }
}
