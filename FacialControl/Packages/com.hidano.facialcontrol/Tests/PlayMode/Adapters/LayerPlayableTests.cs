using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.PlayMode.Adapters
{
    /// <summary>
    /// P06-T05: LayerPlayable の PlayMode テスト。
    /// 補間計算、排他モード（LastWins / Blend）、遷移割込を検証する。
    /// </summary>
    [TestFixture]
    public class LayerPlayableTests
    {
        private PlayableGraph _graph;
        private GameObject _gameObject;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("LayerPlayableTest");
            _graph = PlayableGraph.Create("LayerPlayableTestGraph");
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
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 4,
                exclusionMode: ExclusionMode.LastWins);

            Assert.IsTrue(playable.IsValid());
        }

        [Test]
        public void Create_ZeroBlendShapeCount_ReturnsValidPlayable()
        {
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 0,
                exclusionMode: ExclusionMode.LastWins);

            Assert.IsTrue(playable.IsValid());
        }

        [Test]
        public void GetBehaviour_AfterCreate_ReturnsNonNull()
        {
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 4,
                exclusionMode: ExclusionMode.LastWins);

            var behaviour = playable.GetBehaviour();

            Assert.IsNotNull(behaviour);
        }

        [Test]
        public void OutputWeights_AfterCreate_AllZero()
        {
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 4,
                exclusionMode: ExclusionMode.LastWins);

            var behaviour = playable.GetBehaviour();
            var output = behaviour.OutputWeights;

            Assert.AreEqual(4, output.Length);
            for (int i = 0; i < output.Length; i++)
            {
                Assert.AreEqual(0f, output[i]);
            }
        }

        // ================================================================
        // SetTargetExpression — LastWins モード
        // ================================================================

        [Test]
        public void SetTargetExpression_LastWins_SetsTargetValues()
        {
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 3,
                exclusionMode: ExclusionMode.LastWins);

            var behaviour = playable.GetBehaviour();
            var targetValues = new float[] { 0.5f, 0.8f, 0.3f };

            behaviour.SetTargetExpression("expr-1", targetValues, 0.25f, TransitionCurve.Linear);

            Assert.IsTrue(behaviour.IsTransitioning);
        }

        [Test]
        public void SetTargetExpression_ZeroDuration_CompletesImmediately()
        {
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 3,
                exclusionMode: ExclusionMode.LastWins);

            var behaviour = playable.GetBehaviour();
            var targetValues = new float[] { 0.5f, 0.8f, 0.3f };

            behaviour.SetTargetExpression("expr-1", targetValues, 0f, TransitionCurve.Linear);

            // 遷移時間 0 の場合は即座に切り替え（遷移完了状態）
            Assert.IsFalse(behaviour.IsTransitioning);
            var output = behaviour.OutputWeights;
            Assert.AreEqual(0.5f, output[0], 0.001f);
            Assert.AreEqual(0.8f, output[1], 0.001f);
            Assert.AreEqual(0.3f, output[2], 0.001f);
        }

        // ================================================================
        // UpdateTransition — LastWins 補間
        // ================================================================

        [Test]
        public void UpdateTransition_LastWins_LinearInterpolation_Midpoint()
        {
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 2,
                exclusionMode: ExclusionMode.LastWins);

            var behaviour = playable.GetBehaviour();
            var targetValues = new float[] { 1.0f, 0.6f };

            // 初期状態（全ゼロ）から targetValues への遷移
            behaviour.SetTargetExpression("expr-1", targetValues, 0.5f, TransitionCurve.Linear);

            // 0.25 秒更新 → t = 0.5 (半分)
            behaviour.UpdateTransition(0.25f);

            var output = behaviour.OutputWeights;
            Assert.AreEqual(0.5f, output[0], 0.01f);
            Assert.AreEqual(0.3f, output[1], 0.01f);
        }

        [Test]
        public void UpdateTransition_LastWins_LinearInterpolation_Complete()
        {
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 2,
                exclusionMode: ExclusionMode.LastWins);

            var behaviour = playable.GetBehaviour();
            var targetValues = new float[] { 0.8f, 0.4f };

            behaviour.SetTargetExpression("expr-1", targetValues, 0.5f, TransitionCurve.Linear);

            // 0.5 秒更新 → t = 1.0 (完了)
            behaviour.UpdateTransition(0.5f);

            Assert.IsFalse(behaviour.IsTransitioning);
            var output = behaviour.OutputWeights;
            Assert.AreEqual(0.8f, output[0], 0.001f);
            Assert.AreEqual(0.4f, output[1], 0.001f);
        }

        [Test]
        public void UpdateTransition_LastWins_OvershootDuration_ClampsToTarget()
        {
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 2,
                exclusionMode: ExclusionMode.LastWins);

            var behaviour = playable.GetBehaviour();
            var targetValues = new float[] { 1.0f, 0.5f };

            behaviour.SetTargetExpression("expr-1", targetValues, 0.25f, TransitionCurve.Linear);

            // 遷移時間を超えた更新
            behaviour.UpdateTransition(1.0f);

            Assert.IsFalse(behaviour.IsTransitioning);
            var output = behaviour.OutputWeights;
            Assert.AreEqual(1.0f, output[0], 0.001f);
            Assert.AreEqual(0.5f, output[1], 0.001f);
        }

        [Test]
        public void UpdateTransition_LastWins_SequentialExpressions_CrossfadesFromCurrent()
        {
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 2,
                exclusionMode: ExclusionMode.LastWins);

            var behaviour = playable.GetBehaviour();

            // 最初の表情を完了させる
            var firstTarget = new float[] { 1.0f, 0.0f };
            behaviour.SetTargetExpression("expr-1", firstTarget, 0f, TransitionCurve.Linear);

            // 2 番目の表情を設定
            var secondTarget = new float[] { 0.0f, 1.0f };
            behaviour.SetTargetExpression("expr-2", secondTarget, 0.5f, TransitionCurve.Linear);

            // 半分まで進める → from=[1,0] to=[0,1] t=0.5
            behaviour.UpdateTransition(0.25f);

            var output = behaviour.OutputWeights;
            Assert.AreEqual(0.5f, output[0], 0.01f);
            Assert.AreEqual(0.5f, output[1], 0.01f);
        }

        // ================================================================
        // 遷移割込（Transition Interrupt）
        // ================================================================

        [Test]
        public void SetTargetExpression_DuringTransition_SnapshotsAndStartsNewTransition()
        {
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 2,
                exclusionMode: ExclusionMode.LastWins);

            var behaviour = playable.GetBehaviour();

            // 最初の遷移
            var firstTarget = new float[] { 1.0f, 0.0f };
            behaviour.SetTargetExpression("expr-1", firstTarget, 1.0f, TransitionCurve.Linear);

            // 0.5 秒進めて中間状態にする → [0.5, 0.0]
            behaviour.UpdateTransition(0.5f);

            // 遷移割込: 新しい Expression をトリガー
            var secondTarget = new float[] { 0.0f, 1.0f };
            behaviour.SetTargetExpression("expr-2", secondTarget, 1.0f, TransitionCurve.Linear);

            // 割込直後は遷移中
            Assert.IsTrue(behaviour.IsTransitioning);

            // 半分進める → from=[0.5, 0.0] (snapshot) to=[0.0, 1.0] t=0.5
            behaviour.UpdateTransition(0.5f);

            var output = behaviour.OutputWeights;
            Assert.AreEqual(0.25f, output[0], 0.01f);
            Assert.AreEqual(0.5f, output[1], 0.01f);
        }

        // ================================================================
        // Blend モード
        // ================================================================

        [Test]
        public void AddBlendExpression_Blend_AddsWeightedValues()
        {
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 3,
                exclusionMode: ExclusionMode.Blend);

            var behaviour = playable.GetBehaviour();

            var values1 = new float[] { 1.0f, 0.0f, 0.5f };
            behaviour.AddBlendExpression("expr-1", values1, 0.5f);

            behaviour.ComputeBlendOutput();

            var output = behaviour.OutputWeights;
            Assert.AreEqual(0.5f, output[0], 0.001f);
            Assert.AreEqual(0.0f, output[1], 0.001f);
            Assert.AreEqual(0.25f, output[2], 0.001f);
        }

        [Test]
        public void AddBlendExpression_MultipleExpressions_AddsAndClamps()
        {
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 2,
                exclusionMode: ExclusionMode.Blend);

            var behaviour = playable.GetBehaviour();

            var values1 = new float[] { 0.6f, 0.3f };
            var values2 = new float[] { 0.7f, 0.5f };
            behaviour.AddBlendExpression("expr-1", values1, 1.0f);
            behaviour.AddBlendExpression("expr-2", values2, 1.0f);

            behaviour.ComputeBlendOutput();

            var output = behaviour.OutputWeights;
            // 0.6 + 0.7 = 1.3 → clamped to 1.0
            Assert.AreEqual(1.0f, output[0], 0.001f);
            // 0.3 + 0.5 = 0.8
            Assert.AreEqual(0.8f, output[1], 0.001f);
        }

        [Test]
        public void RemoveBlendExpression_RemovesFromOutput()
        {
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 2,
                exclusionMode: ExclusionMode.Blend);

            var behaviour = playable.GetBehaviour();

            var values1 = new float[] { 0.5f, 0.5f };
            var values2 = new float[] { 0.3f, 0.3f };
            behaviour.AddBlendExpression("expr-1", values1, 1.0f);
            behaviour.AddBlendExpression("expr-2", values2, 1.0f);

            behaviour.RemoveBlendExpression("expr-1");
            behaviour.ComputeBlendOutput();

            var output = behaviour.OutputWeights;
            Assert.AreEqual(0.3f, output[0], 0.001f);
            Assert.AreEqual(0.3f, output[1], 0.001f);
        }

        // ================================================================
        // Deactivate
        // ================================================================

        [Test]
        public void Deactivate_LastWins_ResetsToZero()
        {
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 2,
                exclusionMode: ExclusionMode.LastWins);

            var behaviour = playable.GetBehaviour();
            var targetValues = new float[] { 1.0f, 0.5f };
            behaviour.SetTargetExpression("expr-1", targetValues, 0f, TransitionCurve.Linear);

            // 非アクティブ化 → ゼロへ遷移
            behaviour.Deactivate(0f);

            var output = behaviour.OutputWeights;
            Assert.AreEqual(0f, output[0], 0.001f);
            Assert.AreEqual(0f, output[1], 0.001f);
        }

        [Test]
        public void Deactivate_LastWins_WithDuration_TransitionsToZero()
        {
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 2,
                exclusionMode: ExclusionMode.LastWins);

            var behaviour = playable.GetBehaviour();
            var targetValues = new float[] { 1.0f, 0.8f };
            behaviour.SetTargetExpression("expr-1", targetValues, 0f, TransitionCurve.Linear);

            // 遷移付きで非アクティブ化
            behaviour.Deactivate(0.5f);

            Assert.IsTrue(behaviour.IsTransitioning);

            // 半分進める
            behaviour.UpdateTransition(0.25f);

            var output = behaviour.OutputWeights;
            Assert.AreEqual(0.5f, output[0], 0.01f);
            Assert.AreEqual(0.4f, output[1], 0.01f);
        }

        // ================================================================
        // ActiveExpressionId
        // ================================================================

        [Test]
        public void ActiveExpressionId_AfterCreate_IsNull()
        {
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 2,
                exclusionMode: ExclusionMode.LastWins);

            var behaviour = playable.GetBehaviour();

            Assert.IsNull(behaviour.ActiveExpressionId);
        }

        [Test]
        public void ActiveExpressionId_AfterSetTarget_ReturnsId()
        {
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 2,
                exclusionMode: ExclusionMode.LastWins);

            var behaviour = playable.GetBehaviour();
            behaviour.SetTargetExpression("expr-1", new float[] { 0.5f, 0.5f }, 0.25f, TransitionCurve.Linear);

            Assert.AreEqual("expr-1", behaviour.ActiveExpressionId);
        }

        // ================================================================
        // Dispose（NativeArray 解放）
        // ================================================================

        [Test]
        public void Dispose_ReleasesNativeArrays()
        {
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 4,
                exclusionMode: ExclusionMode.LastWins);

            var behaviour = playable.GetBehaviour();

            Assert.DoesNotThrow(() => behaviour.Dispose());
        }

        [Test]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 4,
                exclusionMode: ExclusionMode.LastWins);

            var behaviour = playable.GetBehaviour();

            behaviour.Dispose();

            Assert.DoesNotThrow(() => behaviour.Dispose());
        }

        // ================================================================
        // EaseInOut カーブ
        // ================================================================

        [Test]
        public void UpdateTransition_EaseInOut_MidpointIs05()
        {
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 1,
                exclusionMode: ExclusionMode.LastWins);

            var behaviour = playable.GetBehaviour();
            var targetValues = new float[] { 1.0f };
            var curve = new TransitionCurve(TransitionCurveType.EaseInOut);

            behaviour.SetTargetExpression("expr-1", targetValues, 1.0f, curve);

            // t=0.5 → EaseInOut(0.5) = 0.5
            behaviour.UpdateTransition(0.5f);

            var output = behaviour.OutputWeights;
            Assert.AreEqual(0.5f, output[0], 0.01f);
        }

        // ================================================================
        // BlendShapeCount プロパティ
        // ================================================================

        [Test]
        public void BlendShapeCount_ReturnsConfiguredCount()
        {
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 8,
                exclusionMode: ExclusionMode.LastWins);

            var behaviour = playable.GetBehaviour();

            Assert.AreEqual(8, behaviour.BlendShapeCount);
        }

        // ================================================================
        // ExclusionMode プロパティ
        // ================================================================

        [Test]
        public void ExclusionMode_ReturnsConfiguredMode()
        {
            var playable = LayerPlayable.Create(
                _graph,
                blendShapeCount: 4,
                exclusionMode: ExclusionMode.Blend);

            var behaviour = playable.GetBehaviour();

            Assert.AreEqual(ExclusionMode.Blend, behaviour.ExclusionMode);
        }
    }
}
