using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    /// <summary>
    /// P14-T02: 遷移 / 割込の統合テスト。
    /// FacialController を通した Expression 遷移、LastWins / Blend クロスフェード、
    /// 遷移割込を PlayableGraph 上で検証する。
    /// </summary>
    [TestFixture]
    public class TransitionIntegrationTests
    {
        private GameObject _gameObject;
        private PlayableGraph _graph;

        [TearDown]
        public void TearDown()
        {
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }
            if (_gameObject != null)
            {
                Object.DestroyImmediate(_gameObject);
                _gameObject = null;
            }
        }

        // ================================================================
        // LastWins 遷移の統合テスト
        // ================================================================

        [Test]
        public void LastWins_ZeroDuration_ImmediateSwitch()
        {
            SetUpGraph();
            var blendShapeNames = new string[] { "smile", "frown" };
            var profile = CreateTestProfile(ExclusionMode.LastWins);
            var result = PlayableGraphBuilder.Build(_gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            var emotionBehaviour = result.LayerPlayables["emotion"].GetBehaviour();
            var targetValues = new float[] { 1.0f, 0.0f };
            emotionBehaviour.SetTargetExpression("happy", targetValues, 0f, TransitionCurve.Linear);

            Assert.IsFalse(emotionBehaviour.IsTransitioning);
            Assert.AreEqual(1.0f, emotionBehaviour.OutputWeights[0], 0.001f);
            Assert.AreEqual(0.0f, emotionBehaviour.OutputWeights[1], 0.001f);

            result.Dispose();
        }

        [Test]
        public void LastWins_LinearTransition_MidpointValues()
        {
            SetUpGraph();
            var blendShapeNames = new string[] { "smile", "frown" };
            var profile = CreateTestProfile(ExclusionMode.LastWins);
            var result = PlayableGraphBuilder.Build(_gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            var emotionBehaviour = result.LayerPlayables["emotion"].GetBehaviour();
            var targetValues = new float[] { 1.0f, 0.6f };
            emotionBehaviour.SetTargetExpression("happy", targetValues, 0.5f, TransitionCurve.Linear);

            // 半分進める → t = 0.5
            emotionBehaviour.UpdateTransition(0.25f);

            Assert.IsTrue(emotionBehaviour.IsTransitioning);
            Assert.AreEqual(0.5f, emotionBehaviour.OutputWeights[0], 0.01f);
            Assert.AreEqual(0.3f, emotionBehaviour.OutputWeights[1], 0.01f);

            result.Dispose();
        }

        [Test]
        public void LastWins_LinearTransition_CompleteTransition()
        {
            SetUpGraph();
            var blendShapeNames = new string[] { "smile", "frown" };
            var profile = CreateTestProfile(ExclusionMode.LastWins);
            var result = PlayableGraphBuilder.Build(_gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            var emotionBehaviour = result.LayerPlayables["emotion"].GetBehaviour();
            var targetValues = new float[] { 0.8f, 0.4f };
            emotionBehaviour.SetTargetExpression("happy", targetValues, 0.25f, TransitionCurve.Linear);

            // 遷移完了まで進める
            emotionBehaviour.UpdateTransition(0.25f);

            Assert.IsFalse(emotionBehaviour.IsTransitioning);
            Assert.AreEqual(0.8f, emotionBehaviour.OutputWeights[0], 0.001f);
            Assert.AreEqual(0.4f, emotionBehaviour.OutputWeights[1], 0.001f);

            result.Dispose();
        }

        [Test]
        public void LastWins_SequentialExpressions_CrossfadesFromPrevious()
        {
            SetUpGraph();
            var blendShapeNames = new string[] { "smile", "frown" };
            var profile = CreateTestProfile(ExclusionMode.LastWins);
            var result = PlayableGraphBuilder.Build(_gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            var emotionBehaviour = result.LayerPlayables["emotion"].GetBehaviour();

            // 最初の表情を即座に適用
            emotionBehaviour.SetTargetExpression("happy", new float[] { 1.0f, 0.0f }, 0f, TransitionCurve.Linear);

            // 2 番目の表情へ遷移
            emotionBehaviour.SetTargetExpression("sad", new float[] { 0.0f, 1.0f }, 0.5f, TransitionCurve.Linear);

            // 半分進める → from=[1,0] to=[0,1] t=0.5
            emotionBehaviour.UpdateTransition(0.25f);

            Assert.AreEqual(0.5f, emotionBehaviour.OutputWeights[0], 0.01f);
            Assert.AreEqual(0.5f, emotionBehaviour.OutputWeights[1], 0.01f);

            result.Dispose();
        }

        // ================================================================
        // 遷移割込テスト
        // ================================================================

        [Test]
        public void TransitionInterrupt_DuringTransition_SnapshotsCurrentAndStartsNew()
        {
            SetUpGraph();
            var blendShapeNames = new string[] { "smile", "frown" };
            var profile = CreateTestProfile(ExclusionMode.LastWins);
            var result = PlayableGraphBuilder.Build(_gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            var emotionBehaviour = result.LayerPlayables["emotion"].GetBehaviour();

            // 最初の遷移: ゼロ → [1.0, 0.0]
            emotionBehaviour.SetTargetExpression("happy", new float[] { 1.0f, 0.0f }, 1.0f, TransitionCurve.Linear);

            // 0.5 秒進める → [0.5, 0.0]
            emotionBehaviour.UpdateTransition(0.5f);

            // 遷移割込: 新しい Expression
            emotionBehaviour.SetTargetExpression("surprised", new float[] { 0.0f, 1.0f }, 1.0f, TransitionCurve.Linear);

            Assert.IsTrue(emotionBehaviour.IsTransitioning);

            // 割込後 0.5 秒進める → from=[0.5, 0.0] to=[0.0, 1.0] t=0.5
            emotionBehaviour.UpdateTransition(0.5f);

            Assert.AreEqual(0.25f, emotionBehaviour.OutputWeights[0], 0.01f);
            Assert.AreEqual(0.5f, emotionBehaviour.OutputWeights[1], 0.01f);

            result.Dispose();
        }

        [Test]
        public void TransitionInterrupt_MultipleInterrupts_EachSnapshotsCurrent()
        {
            SetUpGraph();
            var blendShapeNames = new string[] { "blend" };
            var profile = CreateTestProfile(ExclusionMode.LastWins);
            var result = PlayableGraphBuilder.Build(_gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            var emotionBehaviour = result.LayerPlayables["emotion"].GetBehaviour();

            // 遷移 1: 0 → 1.0 (1秒)
            emotionBehaviour.SetTargetExpression("e1", new float[] { 1.0f }, 1.0f, TransitionCurve.Linear);
            emotionBehaviour.UpdateTransition(0.5f); // → 0.5

            // 割込 1: 0.5 → 0.0 (1秒)
            emotionBehaviour.SetTargetExpression("e2", new float[] { 0.0f }, 1.0f, TransitionCurve.Linear);
            emotionBehaviour.UpdateTransition(0.5f); // → lerp(0.5, 0.0, 0.5) = 0.25

            // 割込 2: 0.25 → 1.0 (1秒)
            emotionBehaviour.SetTargetExpression("e3", new float[] { 1.0f }, 1.0f, TransitionCurve.Linear);
            emotionBehaviour.UpdateTransition(0.5f); // → lerp(0.25, 1.0, 0.5) = 0.625

            Assert.AreEqual(0.625f, emotionBehaviour.OutputWeights[0], 0.01f);

            result.Dispose();
        }

        // ================================================================
        // EaseInOut カーブ遷移テスト
        // ================================================================

        [Test]
        public void EaseInOut_TransitionMidpoint_ValueIs05()
        {
            SetUpGraph();
            var blendShapeNames = new string[] { "blend" };
            var profile = CreateTestProfile(ExclusionMode.LastWins);
            var result = PlayableGraphBuilder.Build(_gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            var emotionBehaviour = result.LayerPlayables["emotion"].GetBehaviour();
            var curve = new TransitionCurve(TransitionCurveType.EaseInOut);
            emotionBehaviour.SetTargetExpression("e1", new float[] { 1.0f }, 1.0f, curve);

            // t=0.5 → EaseInOut(0.5) = 0.5
            emotionBehaviour.UpdateTransition(0.5f);

            Assert.AreEqual(0.5f, emotionBehaviour.OutputWeights[0], 0.01f);

            result.Dispose();
        }

        [Test]
        public void EaseIn_TransitionMidpoint_ValueLessThan05()
        {
            SetUpGraph();
            var blendShapeNames = new string[] { "blend" };
            var profile = CreateTestProfile(ExclusionMode.LastWins);
            var result = PlayableGraphBuilder.Build(_gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            var emotionBehaviour = result.LayerPlayables["emotion"].GetBehaviour();
            var curve = new TransitionCurve(TransitionCurveType.EaseIn);
            emotionBehaviour.SetTargetExpression("e1", new float[] { 1.0f }, 1.0f, curve);

            // t=0.5 → EaseIn は前半がゆるやか → 値 < 0.5
            emotionBehaviour.UpdateTransition(0.5f);

            Assert.Less(emotionBehaviour.OutputWeights[0], 0.5f);

            result.Dispose();
        }

        [Test]
        public void EaseOut_TransitionMidpoint_ValueGreaterThan05()
        {
            SetUpGraph();
            var blendShapeNames = new string[] { "blend" };
            var profile = CreateTestProfile(ExclusionMode.LastWins);
            var result = PlayableGraphBuilder.Build(_gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            var emotionBehaviour = result.LayerPlayables["emotion"].GetBehaviour();
            var curve = new TransitionCurve(TransitionCurveType.EaseOut);
            emotionBehaviour.SetTargetExpression("e1", new float[] { 1.0f }, 1.0f, curve);

            // t=0.5 → EaseOut は前半が急 → 値 > 0.5
            emotionBehaviour.UpdateTransition(0.5f);

            Assert.Greater(emotionBehaviour.OutputWeights[0], 0.5f);

            result.Dispose();
        }

        // ================================================================
        // Blend モード遷移テスト
        // ================================================================

        [Test]
        public void Blend_AddMultipleExpressions_ValuesAddedAndClamped()
        {
            SetUpGraph();
            var blendShapeNames = new string[] { "mouth_a", "mouth_o" };
            var profile = CreateTestProfile(ExclusionMode.Blend);
            var result = PlayableGraphBuilder.Build(_gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            var lipsyncBehaviour = result.LayerPlayables["emotion"].GetBehaviour();

            lipsyncBehaviour.AddBlendExpression("lip-a", new float[] { 0.7f, 0.2f }, 1.0f);
            lipsyncBehaviour.AddBlendExpression("lip-o", new float[] { 0.5f, 0.6f }, 1.0f);
            lipsyncBehaviour.ComputeBlendOutput();

            // 0.7 + 0.5 = 1.2 → clamped to 1.0
            Assert.AreEqual(1.0f, lipsyncBehaviour.OutputWeights[0], 0.001f);
            // 0.2 + 0.6 = 0.8
            Assert.AreEqual(0.8f, lipsyncBehaviour.OutputWeights[1], 0.001f);

            result.Dispose();
        }

        [Test]
        public void Blend_RemoveExpression_RecalculatesOutput()
        {
            SetUpGraph();
            var blendShapeNames = new string[] { "mouth_a" };
            var profile = CreateTestProfile(ExclusionMode.Blend);
            var result = PlayableGraphBuilder.Build(_gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            var lipsyncBehaviour = result.LayerPlayables["emotion"].GetBehaviour();

            lipsyncBehaviour.AddBlendExpression("lip-a", new float[] { 0.5f }, 1.0f);
            lipsyncBehaviour.AddBlendExpression("lip-o", new float[] { 0.3f }, 1.0f);
            lipsyncBehaviour.RemoveBlendExpression("lip-a");
            lipsyncBehaviour.ComputeBlendOutput();

            Assert.AreEqual(0.3f, lipsyncBehaviour.OutputWeights[0], 0.001f);

            result.Dispose();
        }

        // ================================================================
        // Deactivate 遷移テスト
        // ================================================================

        [Test]
        public void Deactivate_ZeroDuration_ImmediateReset()
        {
            SetUpGraph();
            var blendShapeNames = new string[] { "smile" };
            var profile = CreateTestProfile(ExclusionMode.LastWins);
            var result = PlayableGraphBuilder.Build(_gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            var emotionBehaviour = result.LayerPlayables["emotion"].GetBehaviour();
            emotionBehaviour.SetTargetExpression("happy", new float[] { 1.0f }, 0f, TransitionCurve.Linear);

            emotionBehaviour.Deactivate(0f);

            Assert.IsFalse(emotionBehaviour.IsTransitioning);
            Assert.AreEqual(0f, emotionBehaviour.OutputWeights[0], 0.001f);

            result.Dispose();
        }

        [Test]
        public void Deactivate_WithDuration_TransitionsToZero()
        {
            SetUpGraph();
            var blendShapeNames = new string[] { "smile" };
            var profile = CreateTestProfile(ExclusionMode.LastWins);
            var result = PlayableGraphBuilder.Build(_gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            var emotionBehaviour = result.LayerPlayables["emotion"].GetBehaviour();
            emotionBehaviour.SetTargetExpression("happy", new float[] { 1.0f }, 0f, TransitionCurve.Linear);

            emotionBehaviour.Deactivate(0.5f);
            Assert.IsTrue(emotionBehaviour.IsTransitioning);

            // 半分進める → from=1.0, to=0.0, t=0.5 → 0.5
            emotionBehaviour.UpdateTransition(0.25f);
            Assert.AreEqual(0.5f, emotionBehaviour.OutputWeights[0], 0.01f);

            // 完了まで進める
            emotionBehaviour.UpdateTransition(0.25f);
            Assert.IsFalse(emotionBehaviour.IsTransitioning);
            Assert.AreEqual(0f, emotionBehaviour.OutputWeights[0], 0.001f);

            result.Dispose();
        }

        // ================================================================
        // Mixer を通したレイヤーブレンド遷移テスト
        // ================================================================

        [Test]
        public void MixerBlend_TwoLayersWithTransition_BlendedOutput()
        {
            SetUpGraph();
            var blendShapeNames = new string[] { "blend" };
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend)
            };
            var profile = new FacialProfile("1.0.0", layers);
            var result = PlayableGraphBuilder.Build(_gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            // emotion レイヤー: 即座に 0.8
            var emotionBehaviour = result.LayerPlayables["emotion"].GetBehaviour();
            emotionBehaviour.SetTargetExpression("happy", new float[] { 0.8f }, 0f, TransitionCurve.Linear);

            // lipsync レイヤー: 0.4 をブレンド追加
            var lipsyncBehaviour = result.LayerPlayables["lipsync"].GetBehaviour();
            lipsyncBehaviour.AddBlendExpression("lip-a", new float[] { 0.4f }, 1.0f);
            lipsyncBehaviour.ComputeBlendOutput();

            // Mixer で合成
            var mixerBehaviour = result.Mixer.GetBehaviour();
            mixerBehaviour.ComputeOutput();

            // lipsync(priority=1, weight=1.0) が emotion(priority=0, weight=1.0) を上書き
            // lerp(0.8, 0.4, 1.0) = 0.4
            Assert.AreEqual(0.4f, mixerBehaviour.OutputWeights[0], 0.01f);

            result.Dispose();
        }

        [Test]
        public void MixerBlend_PartialLayerWeight_InterpolatedOutput()
        {
            SetUpGraph();
            var blendShapeNames = new string[] { "blend" };
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend)
            };
            var profile = new FacialProfile("1.0.0", layers);
            var result = PlayableGraphBuilder.Build(_gameObject.GetComponent<Animator>(), profile, blendShapeNames);

            // emotion: 1.0
            var emotionBehaviour = result.LayerPlayables["emotion"].GetBehaviour();
            emotionBehaviour.SetTargetExpression("happy", new float[] { 1.0f }, 0f, TransitionCurve.Linear);

            // lipsync: 0.0 (weight=0.5)
            var lipsyncBehaviour = result.LayerPlayables["lipsync"].GetBehaviour();
            lipsyncBehaviour.AddBlendExpression("lip-a", new float[] { 0.0f }, 1.0f);
            lipsyncBehaviour.ComputeBlendOutput();

            var mixerBehaviour = result.Mixer.GetBehaviour();
            mixerBehaviour.SetLayerWeight("lipsync", 0.5f);
            mixerBehaviour.ComputeOutput();

            // lerp(1.0, 0.0, 0.5) = 0.5
            Assert.AreEqual(0.5f, mixerBehaviour.OutputWeights[0], 0.01f);

            result.Dispose();
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void SetUpGraph()
        {
            _gameObject = new GameObject("TransitionIntegrationTest");
            _gameObject.AddComponent<Animator>();
        }

        private static FacialProfile CreateTestProfile(ExclusionMode mode)
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, mode)
            };
            return new FacialProfile("1.0.0", layers);
        }
    }
}
