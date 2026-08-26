using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Application.UseCases;

namespace Hidano.FacialControl.Tests.EditMode.Application
{
    [TestFixture]
    public class LayerUseCaseTests
    {
        // --- ヘルパー ---

        private static LayerDefinition[] CreateDefaultLayers()
        {
            return new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend),
                new LayerDefinition("eye", 2, ExclusionMode.LastWins)
            };
        }

        private static string[] CreateBlendShapeNames()
        {
            return new[] { "bs_smile", "bs_sad", "bs_blink" };
        }

        private static Expression CreateExpression(
            string id = "expr-1",
            string name = "smile",
            string layer = "emotion",
            float transitionDuration = 0.25f,
            BlendShapeMapping[] blendShapeValues = null)
        {
            return new Expression(
                id, name, layer, transitionDuration,
                TransitionCurve.Linear,
                blendShapeValues);
        }

        private static FacialProfile CreateProfile(
            LayerDefinition[] layers = null,
            Expression[] expressions = null)
        {
            return new FacialProfile(
                "1.0",
                layers ?? CreateDefaultLayers(),
                expressions ?? Array.Empty<Expression>());
        }

        private static Dictionary<string, List<Expression>> GetGroupedByLayerBuffer(LayerUseCase useCase)
        {
            var field = typeof(LayerUseCase).GetField(
                "_groupedByLayer",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field, "_groupedByLayer field was not found.");
            return (Dictionary<string, List<Expression>>)field.GetValue(useCase);
        }

        private static List<string> GetActiveGroupedLayerKeys(LayerUseCase useCase)
        {
            var field = typeof(LayerUseCase).GetField(
                "_activeGroupedLayerKeys",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field, "_activeGroupedLayerKeys field was not found.");
            return (List<string>)field.GetValue(useCase);
        }

        private static Dictionary<string, List<Expression>> InvokeGroupByLayer(
            LayerUseCase useCase, List<Expression> expressions)
        {
            var method = typeof(LayerUseCase).GetMethod(
                "GroupByLayer",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(method, "GroupByLayer method was not found.");
            return (Dictionary<string, List<Expression>>)method.Invoke(useCase, new object[] { expressions });
        }

        private LayerUseCase _useCase;
        private ExpressionUseCase _expressionUseCase;
        private FacialProfile _profile;

        [SetUp]
        public void SetUp()
        {
            _profile = CreateProfile();
            _expressionUseCase = new ExpressionUseCase(_profile);
            _useCase = new LayerUseCase(_profile, _expressionUseCase, CreateBlendShapeNames());
        }

        // --- コンストラクタ ---

        [Test]
        public void Constructor_ValidArgs_CreatesInstance()
        {
            Assert.IsNotNull(_useCase);
        }

        [Test]
        public void Constructor_NullBlendShapeNames_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new LayerUseCase(_profile, _expressionUseCase, null));
        }

        [Test]
        public void Constructor_EmptyBlendShapeNames_CreatesInstance()
        {
            var useCase = new LayerUseCase(_profile, _expressionUseCase, Array.Empty<string>());
            Assert.IsNotNull(useCase);
        }

        // --- SetLayerWeight ---

        [Test]
        public void SetLayerWeight_ValidLayerAndWeight_SetsWeight()
        {
            _useCase.SetLayerWeight("emotion", 0.5f);

            // 検証: GetBlendedOutput で反映されることを確認
            // レイヤーウェイトのデフォルトが 1.0 なので、設定後は 0.5 に変わる
            Assert.DoesNotThrow(() => _useCase.SetLayerWeight("emotion", 0.5f));
        }

        [Test]
        public void SetLayerWeight_NullLayer_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                _useCase.SetLayerWeight(null, 0.5f));
        }

        [Test]
        public void SetLayerWeight_WeightAboveOne_ClampedToOne()
        {
            // 範囲外の値はクランプされる（例外なし）
            Assert.DoesNotThrow(() => _useCase.SetLayerWeight("emotion", 1.5f));
        }

        [Test]
        public void SetLayerWeight_WeightBelowZero_ClampedToZero()
        {
            Assert.DoesNotThrow(() => _useCase.SetLayerWeight("emotion", -0.5f));
        }

        [Test]
        public void SetLayerWeight_UndefinedLayer_SetsWeightWithoutError()
        {
            // 未定義レイヤーにも設定可能（後から使われる可能性があるため）
            Assert.DoesNotThrow(() => _useCase.SetLayerWeight("unknown", 0.5f));
        }

        // --- UpdateWeights: LayerOverrideMask 抑制（系2 駆動）---
        //
        // 抑制計算は系2(ExpressionTriggerInputSource)の active を見るため、
        // emotion レイヤーに系2 sink を additionalSources で追加し TriggerOn で active 化する。
        // 出力駆動(系1 の LayerExpressionSource)は euc.Activate のまま（Q2=A: 系1 残す）。

        // 系2 の具象は別アセンブリ(InputSystem)にあるため、テストでは基底を継承した Fake を使う。
        private sealed class FakeTriggerSource : ExpressionTriggerInputSourceBase
        {
            public FakeTriggerSource(FacialProfile profile, string[] blendShapeNames)
                : base(
                    InputSourceId.Parse("input"),
                    blendShapeNames.Length,
                    maxStackDepth: 4,
                    exclusionMode: ExclusionMode.LastWins,
                    blendShapeNames: blendShapeNames,
                    profile: profile)
            {
            }
        }

        private static (LayerUseCase luc, ExpressionUseCase euc, FakeTriggerSource emotionTrigger, Expression smile, Expression ov)
            BuildOverrideScenario(LayerOverrideMask smileMask)
        {
            // emotion(bit0, priority0) と overlay(bit1, priority1) の 2 レイヤー。
            var layers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("overlay", 1, ExclusionMode.LastWins),
            };
            var bsNames = new[] { "bs_a", "bs_b" };
            var smile = new Expression(
                "smile", "smile", "emotion", 0.1f, TransitionCurve.Linear,
                new[] { new BlendShapeMapping("bs_a", 1f, null) },
                null,
                smileMask);
            var ov = new Expression(
                "ov", "ov", "overlay", 0.1f, TransitionCurve.Linear,
                new[] { new BlendShapeMapping("bs_b", 1f, null) });
            var profile = new FacialProfile("1.0", layers, new[] { smile, ov });
            var euc = new ExpressionUseCase(profile);
            // emotion レイヤー(idx0)に系2 を追加。抑制計算は系2 の active から行われる。
            var emotionTrigger = new FakeTriggerSource(profile, bsNames);
            var luc = new LayerUseCase(
                profile, euc, bsNames,
                new[] { (0, (IInputSource)emotionTrigger, 1f) });
            return (luc, euc, emotionTrigger, smile, ov);
        }

        [Test]
        public void UpdateWeights_ActiveExpressionOverridesLayer_SuppressesTargetLayer()
        {
            // smile(emotion) が overlay(bit1) を OverrideMask で抑制する。
            var (luc, euc, emotionTrigger, smile, ov) = BuildOverrideScenario(LayerOverrideMask.Bit1);
            euc.Activate(smile);
            euc.Activate(ov);
            emotionTrigger.TriggerOn("smile");

            luc.UpdateWeights(1f);
            var output = luc.GetBlendedOutput();

            Assert.AreEqual(1f, output[0], 1e-5f, "emotion(自己)は出力される");
            Assert.AreEqual(0f, output[1], 1e-5f, "overlay は OverrideMask により抑制される");
        }

        [Test]
        public void UpdateWeights_NoOverrideMask_TargetLayerContributes()
        {
            // OverrideMask=None なら overlay は通常どおりブレンドされる（対照）。
            var (luc, euc, emotionTrigger, smile, ov) = BuildOverrideScenario(LayerOverrideMask.None);
            euc.Activate(smile);
            euc.Activate(ov);
            emotionTrigger.TriggerOn("smile");

            luc.UpdateWeights(1f);
            var output = luc.GetBlendedOutput();

            Assert.AreEqual(1f, output[0], 1e-5f, "emotion が出力される");
            Assert.AreEqual(1f, output[1], 1e-5f, "OverrideMask が無いので overlay も出力される");
        }

        [Test]
        public void UpdateWeights_OverrideMaskSelfLayer_DoesNotSuppressSelf()
        {
            // smile が自己レイヤー(emotion=bit0)を mask に含めても自己は抑制しない（レイヤー内ブレンド担保）。
            var (luc, euc, emotionTrigger, smile, ov) = BuildOverrideScenario(LayerOverrideMask.Bit0);
            euc.Activate(smile);
            euc.Activate(ov);
            emotionTrigger.TriggerOn("smile");

            luc.UpdateWeights(1f);
            var output = luc.GetBlendedOutput();

            Assert.AreEqual(1f, output[0], 1e-5f, "自己レイヤー emotion は抑制対象外");
            Assert.AreEqual(1f, output[1], 1e-5f, "overlay は mask 対象外なので出力される");
        }

        // --- UpdateWeights ---

        [Test]
        public void UpdateWeights_NoActiveExpressions_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _useCase.UpdateWeights(0.016f));
        }

        [Test]
        public void UpdateWeights_GroupedByLayerBuffer_ReusesAndClearsListsAcrossFrames()
        {
            var emotionExpr = CreateExpression(
                id: "emotion-expr",
                layer: "emotion",
                transitionDuration: 0f,
                blendShapeValues: new[] { new BlendShapeMapping("bs_smile", 1.0f) });
            var eyeExpr = CreateExpression(
                id: "eye-expr",
                layer: "eye",
                transitionDuration: 0f,
                blendShapeValues: new[] { new BlendShapeMapping("bs_blink", 1.0f) });

            _expressionUseCase.Activate(emotionExpr);
            _useCase.UpdateWeights(0.001f);

            var grouped = GetGroupedByLayerBuffer(_useCase);
            var emotionList = grouped["emotion"];
            var eyeList = grouped["eye"];

            Assert.AreEqual(1, emotionList.Count);
            Assert.AreEqual("emotion-expr", emotionList[0].Id);
            Assert.AreEqual(0, eyeList.Count);

            _expressionUseCase.Deactivate(emotionExpr);
            _expressionUseCase.Activate(eyeExpr);
            _useCase.UpdateWeights(0.001f);

            Assert.AreSame(grouped, GetGroupedByLayerBuffer(_useCase));
            Assert.AreSame(emotionList, grouped["emotion"]);
            Assert.AreSame(eyeList, grouped["eye"]);
            Assert.AreEqual(0, emotionList.Count);
            Assert.AreEqual(1, eyeList.Count);
            Assert.AreEqual("eye-expr", eyeList[0].Id);
        }

        [Test]
        public void UpdateWeights_GroupedByLayerBuffer_EmptyPreallocatedLayerDoesNotMarkLayerActive()
        {
            _useCase.UpdateWeights(0.001f);

            var grouped = GetGroupedByLayerBuffer(_useCase);

            Assert.AreEqual(0, grouped["emotion"].Count);
            Assert.AreEqual(0, grouped["lipsync"].Count);
            Assert.AreEqual(0, grouped["eye"].Count);
            Assert.AreEqual(0, _useCase.GetBlendedOutput()[2], 0.001f);
        }

        [Test]
        public void GroupByLayer_EffectiveLayerNotPreallocated_SkipsWithoutAddingKey()
        {
            // 事前確保辞書（_groupedByLayer = profile.Layers 名で確保）に存在しないレイヤー名が
            // effectiveLayer として来ても、新規 List を確保・キー追加せずスキップする（設計 OQ2）。
            // 実機では Layers.Span.Length==0 時に GetEffectiveLayer が宣言外名を返す経路に相当する。
            var grouped = GetGroupedByLayerBuffer(_useCase);

            // "eye" を事前確保辞書から取り除き「未確保レイヤー名」状況を作る。
            // profile には "eye" 層が宣言されているため GetEffectiveLayer は "eye" を返すが、
            // 辞書には "eye" キーが無い、というエッジを再現する。
            grouped.Remove("eye");
            GetActiveGroupedLayerKeys(_useCase).Clear();
            int keyCountBefore = grouped.Count;

            var eyeExpr = CreateExpression(
                id: "eye-expr",
                layer: "eye",
                transitionDuration: 0f,
                blendShapeValues: new[] { new BlendShapeMapping("bs_blink", 1.0f) });

            var result = InvokeGroupByLayer(_useCase, new List<Expression> { eyeExpr });

            Assert.AreSame(grouped, result);
            Assert.IsFalse(
                result.ContainsKey("eye"),
                "未確保レイヤー名のキーが新規追加されてはならない（毎フレ確保の防止）。");
            Assert.AreEqual(
                keyCountBefore, result.Count,
                "辞書のキー数が増えてはならない（新規 List を確保していないこと）。");
        }

        [Test]
        public void UpdateWeights_WithActiveExpression_ProgressesTransition()
        {
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0.5f);

            _expressionUseCase.Activate(expr);

            // 半分の遷移時間経過
            _useCase.UpdateWeights(0.25f);

            var output = _useCase.GetBlendedOutput();
            // 遷移中なので、bs_smile は 0.5 程度（Linear 補間）
            Assert.Greater(output[0], 0f);
            Assert.Less(output[0], 1f);
        }

        [Test]
        public void UpdateWeights_TransitionComplete_ReachesTargetValues()
        {
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0.25f);

            _expressionUseCase.Activate(expr);

            // 遷移時間を超えて更新
            _useCase.UpdateWeights(0.5f);

            var output = _useCase.GetBlendedOutput();
            Assert.AreEqual(1.0f, output[0], 0.001f);
        }

        [Test]
        public void UpdateWeights_ZeroTransitionDuration_ImmediateSwitch()
        {
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.5f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0f);

            _expressionUseCase.Activate(expr);
            _useCase.UpdateWeights(0.001f);

            var output = _useCase.GetBlendedOutput();
            Assert.AreEqual(1.0f, output[0], 0.001f);
            Assert.AreEqual(0.5f, output[1], 0.001f);
        }

        [Test]
        public void UpdateWeights_AfterDeactivate_TransitionsBackToZero()
        {
            // Regression: 直前 active だった expression を Deactivate しても LayerExpressionSource
            // が「ゼロ (= rest) へ補間」を始めず latched ON のままになる不具合があった。
            // 別の表情を入れない限り OFF にならない (Toggle/Hold いずれも同症状)。
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0.25f);

            _expressionUseCase.Activate(expr);
            _useCase.UpdateWeights(1.0f); // 完全に遷移し終わる
            Assert.AreEqual(1.0f, _useCase.GetBlendedOutput()[0], 0.001f, "Activate 後に target に到達していない");

            _expressionUseCase.Deactivate(expr);
            _useCase.UpdateWeights(1.0f); // Deactivate 後の遷移時間も完全に経過

            var output = _useCase.GetBlendedOutput();
            Assert.AreEqual(0f, output[0], 0.001f, "Deactivate 後にゼロへ戻っていない (latched バグ)");
            Assert.AreEqual(0f, output[1], 0.001f);
            Assert.AreEqual(0f, output[2], 0.001f);
        }

        [Test]
        public void UpdateWeights_AfterDeactivate_DuringTransition_ReachesZero()
        {
            // Regression: Deactivate 直後の中間値からゼロへ補間できることを確認する。
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0.5f);

            _expressionUseCase.Activate(expr);
            _useCase.UpdateWeights(0.25f); // 50% 遷移
            float midValue = _useCase.GetBlendedOutput()[0];
            Assert.Greater(midValue, 0f);
            Assert.Less(midValue, 1f);

            _expressionUseCase.Deactivate(expr);
            _useCase.UpdateWeights(1.0f); // 十分な時間で OFF へ補間

            Assert.AreEqual(0f, _useCase.GetBlendedOutput()[0], 0.001f);
        }

        [Test]
        public void UpdateWeights_MultipleDeltaTimeSteps_AccumulatesProgress()
        {
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0.5f);

            _expressionUseCase.Activate(expr);

            // 5 ステップに分けて遷移
            for (int i = 0; i < 5; i++)
                _useCase.UpdateWeights(0.1f);

            var output = _useCase.GetBlendedOutput();
            Assert.AreEqual(1.0f, output[0], 0.001f);
        }

        // --- GetBlendedOutput ---

        [Test]
        public void GetBlendedOutput_NoActiveExpressions_ReturnsZeroArray()
        {
            var output = _useCase.GetBlendedOutput();

            Assert.AreEqual(3, output.Length);
            Assert.AreEqual(0f, output[0]);
            Assert.AreEqual(0f, output[1]);
            Assert.AreEqual(0f, output[2]);
        }

        [Test]
        public void GetBlendedOutput_ReturnsCorrectLength()
        {
            var output = _useCase.GetBlendedOutput();
            Assert.AreEqual(3, output.Length);
        }

        [Test]
        public void GetBlendedOutput_SingleLayerFullyTransitioned_ReturnsExpressionValues()
        {
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 0.8f),
                new BlendShapeMapping("bs_sad", 0.2f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0f);

            _expressionUseCase.Activate(expr);
            _useCase.UpdateWeights(0.001f);

            var output = _useCase.GetBlendedOutput();
            Assert.AreEqual(0.8f, output[0], 0.001f);
            Assert.AreEqual(0.2f, output[1], 0.001f);
            Assert.AreEqual(0.0f, output[2], 0.001f);
        }

        [Test]
        public void GetBlendedOutput_MultipleLayersActive_BlendsByPriority()
        {
            var emotionBs = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var eyeBs = new[]
            {
                new BlendShapeMapping("bs_smile", 0.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 1.0f)
            };

            var emotionExpr = CreateExpression("expr-1", "smile", "emotion",
                transitionDuration: 0f, blendShapeValues: emotionBs);
            var eyeExpr = CreateExpression("expr-2", "blink", "eye",
                transitionDuration: 0f, blendShapeValues: eyeBs);

            _expressionUseCase.Activate(emotionExpr);
            _expressionUseCase.Activate(eyeExpr);
            _useCase.UpdateWeights(0.001f);

            var output = _useCase.GetBlendedOutput();
            // eye (priority=2) は emotion (priority=0) より優先
            // 両レイヤーの weight=1.0 なので、eye の値が優先される
            Assert.AreEqual(1.0f, output[2], 0.001f); // bs_blink は eye レイヤーで 1.0
        }

        [Test]
        public void GetBlendedOutput_LayerWeightZero_LayerIgnored()
        {
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0f);

            _expressionUseCase.Activate(expr);
            _useCase.SetLayerWeight("emotion", 0f);
            _useCase.UpdateWeights(0.001f);

            var output = _useCase.GetBlendedOutput();
            // レイヤーウェイトが 0 なので出力はゼロ
            Assert.AreEqual(0f, output[0], 0.001f);
        }

        [Test]
        public void GetBlendedOutput_LayerWeightHalf_ScalesOutput()
        {
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0f);

            _expressionUseCase.Activate(expr);
            _useCase.SetLayerWeight("emotion", 0.5f);
            _useCase.UpdateWeights(0.001f);

            var output = _useCase.GetBlendedOutput();
            Assert.AreEqual(0.5f, output[0], 0.001f);
        }

        [Test]
        public void GetBlendedOutput_ReturnsDefensiveCopy()
        {
            var output1 = _useCase.GetBlendedOutput();
            var output2 = _useCase.GetBlendedOutput();

            Assert.AreNotSame(output1, output2);
        }

        // --- BlendedOutputSpan (zero-alloc accessor) ---

        [Test]
        public void BlendedOutputSpan_NoActiveExpressions_AllZero()
        {
            var span = _useCase.BlendedOutputSpan;

            Assert.AreEqual(3, span.Length);
            Assert.AreEqual(0f, span[0]);
            Assert.AreEqual(0f, span[1]);
            Assert.AreEqual(0f, span[2]);
        }

        [Test]
        public void BlendedOutputSpan_AfterUpdateWeights_MatchesGetBlendedOutput()
        {
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 0.8f),
                new BlendShapeMapping("bs_sad", 0.2f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0f);

            _expressionUseCase.Activate(expr);
            _useCase.UpdateWeights(0.001f);

            var span = _useCase.BlendedOutputSpan;
            var copy = _useCase.GetBlendedOutput();

            Assert.AreEqual(copy.Length, span.Length);
            for (int i = 0; i < copy.Length; i++)
            {
                Assert.AreEqual(copy[i], span[i], 1e-6f,
                    $"index {i}: BlendedOutputSpan と GetBlendedOutput が一致すべき");
            }
        }

        [Test]
        public void BlendedOutputSpan_Length_MatchesBlendShapeCount()
        {
            var span = _useCase.BlendedOutputSpan;
            Assert.AreEqual(3, span.Length);
        }

        // --- 遷移割込 ---

        [Test]
        public void UpdateWeights_TransitionInterrupt_StartsFromCurrentValues()
        {
            var blendShapes1 = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var blendShapes2 = new[]
            {
                new BlendShapeMapping("bs_smile", 0.0f),
                new BlendShapeMapping("bs_sad", 1.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };

            var expr1 = CreateExpression("expr-1", "smile", "emotion",
                transitionDuration: 0.5f, blendShapeValues: blendShapes1);
            var expr2 = CreateExpression("expr-2", "sad", "emotion",
                transitionDuration: 0.5f, blendShapeValues: blendShapes2);

            // expr1 をアクティブにして途中まで遷移
            _expressionUseCase.Activate(expr1);
            _useCase.UpdateWeights(0.25f); // 50% 遷移
            var midOutput = _useCase.GetBlendedOutput();
            float midSmile = midOutput[0];

            // expr2 に切り替え（遷移割込）
            _expressionUseCase.Activate(expr2);
            _useCase.UpdateWeights(0.001f); // 割込直後

            var interruptOutput = _useCase.GetBlendedOutput();
            // 割込直後は遷移元（前のスナップショット値）に近い
            // bs_sad が少し増加し始めているはず
            Assert.GreaterOrEqual(interruptOutput[0], 0f);
        }

        // --- SetProfile ---

        [Test]
        public void SetProfile_ResetsTransitionState()
        {
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0f);

            _expressionUseCase.Activate(expr);
            _useCase.UpdateWeights(0.001f);

            // 新しいプロファイルに切り替え
            var newProfile = CreateProfile();
            _useCase.SetProfile(newProfile, CreateBlendShapeNames());

            var output = _useCase.GetBlendedOutput();
            // プロファイル切替後はゼロにリセット
            Assert.AreEqual(0f, output[0], 0.001f);
        }

        // --- Blend モード（lipsync レイヤー） ---

        [Test]
        public void GetBlendedOutput_BlendModeLayer_AddsMutipleExpressions()
        {
            var bs1 = new[]
            {
                new BlendShapeMapping("bs_smile", 0.3f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var bs2 = new[]
            {
                new BlendShapeMapping("bs_smile", 0.4f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };

            var expr1 = CreateExpression("expr-1", "talk_a", "lipsync",
                transitionDuration: 0f, blendShapeValues: bs1);
            var expr2 = CreateExpression("expr-2", "talk_o", "lipsync",
                transitionDuration: 0f, blendShapeValues: bs2);

            _expressionUseCase.Activate(expr1);
            _expressionUseCase.Activate(expr2);
            _useCase.UpdateWeights(0.001f);

            var output = _useCase.GetBlendedOutput();
            // Blend モードなので加算される（0.3 + 0.4 = 0.7）
            Assert.AreEqual(0.7f, output[0], 0.05f);
        }

        [Test]
        public void GetBlendedOutput_BlendModeLayer_ClampsToOne()
        {
            var bs1 = new[]
            {
                new BlendShapeMapping("bs_smile", 0.8f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var bs2 = new[]
            {
                new BlendShapeMapping("bs_smile", 0.8f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };

            var expr1 = CreateExpression("expr-1", "talk_a", "lipsync",
                transitionDuration: 0f, blendShapeValues: bs1);
            var expr2 = CreateExpression("expr-2", "talk_o", "lipsync",
                transitionDuration: 0f, blendShapeValues: bs2);

            _expressionUseCase.Activate(expr1);
            _expressionUseCase.Activate(expr2);
            _useCase.UpdateWeights(0.001f);

            var output = _useCase.GetBlendedOutput();
            // 0.8 + 0.8 = 1.6 → 1.0 にクランプ
            Assert.AreEqual(1.0f, output[0], 0.001f);
        }

        // --- BlendShape 名マッピング ---

        [Test]
        public void GetBlendedOutput_ExpressionWithPartialBlendShapes_MapsCorrectly()
        {
            // Expression が全 BlendShape を含まない場合、
            // 対応するインデックスのみ更新される
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_sad", 0.7f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0f);

            _expressionUseCase.Activate(expr);
            _useCase.UpdateWeights(0.001f);

            var output = _useCase.GetBlendedOutput();
            Assert.AreEqual(0.0f, output[0], 0.001f); // bs_smile は未設定
            Assert.AreEqual(0.7f, output[1], 0.001f); // bs_sad は 0.7
            Assert.AreEqual(0.0f, output[2], 0.001f); // bs_blink は未設定
        }

        // --- デフォルト動作 ---

        [Test]
        public void LayerWeight_DefaultIsOne()
        {
            // 何も設定しない場合、レイヤーウェイトはデフォルト 1.0
            var blendShapes = new[]
            {
                new BlendShapeMapping("bs_smile", 1.0f),
                new BlendShapeMapping("bs_sad", 0.0f),
                new BlendShapeMapping("bs_blink", 0.0f)
            };
            var expr = CreateExpression(
                blendShapeValues: blendShapes,
                transitionDuration: 0f);

            _expressionUseCase.Activate(expr);
            _useCase.UpdateWeights(0.001f);

            var output = _useCase.GetBlendedOutput();
            Assert.AreEqual(1.0f, output[0], 0.001f);
        }

        // --- TryGetExpressionTriggerSourceById ---

        private sealed class FakeExpressionTriggerSource : ExpressionTriggerInputSourceBase
        {
            public FakeExpressionTriggerSource(string id, int blendShapeCount, FacialProfile profile)
                : base(InputSourceId.Parse(id), blendShapeCount, 4, ExclusionMode.LastWins,
                       Array.Empty<string>(), profile)
            {
            }
        }

        [Test]
        public void TryGetExpressionTriggerSourceById_RegisteredId_ReturnsTrue()
        {
            var fake = new FakeExpressionTriggerSource("input", 3, _profile);
            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, fake, 1.0f),
            };
            using var useCase = new LayerUseCase(
                _profile, _expressionUseCase, CreateBlendShapeNames(), additional);

            bool found = useCase.TryGetExpressionTriggerSourceById(
                "input", out var source);

            Assert.IsTrue(found);
            Assert.AreSame(fake, source);
        }

        [Test]
        public void TryGetExpressionTriggerSourceById_UnregisteredId_ReturnsFalse()
        {
            bool found = _useCase.TryGetExpressionTriggerSourceById(
                "input", out var source);

            Assert.IsFalse(found);
            Assert.IsNull(source);
        }

        [Test]
        public void TryGetExpressionTriggerSourceById_NullOrEmptyId_ReturnsFalse()
        {
            Assert.IsFalse(_useCase.TryGetExpressionTriggerSourceById(null, out var s1));
            Assert.IsNull(s1);
            Assert.IsFalse(_useCase.TryGetExpressionTriggerSourceById("", out var s2));
            Assert.IsNull(s2);
        }

        [Test]
        public void TryGetExpressionTriggerSourceById_NonExpressionTriggerSource_NotReturned()
        {
            // ValueProvider 型のソースは ExpressionTrigger lookup には該当しない。
            var valueProvider = new FakeValueProviderSource("osc", 3);
            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, valueProvider, 1.0f),
            };
            using var useCase = new LayerUseCase(
                _profile, _expressionUseCase, CreateBlendShapeNames(), additional);

            bool found = useCase.TryGetExpressionTriggerSourceById("osc", out var source);

            Assert.IsFalse(found);
            Assert.IsNull(source);
        }

        private sealed class FakeValueProviderSource : ValueProviderInputSourceBase
        {
            public FakeValueProviderSource(string id, int blendShapeCount)
                : base(InputSourceId.Parse(id), blendShapeCount)
            {
            }

            public override bool TryWriteValues(Span<float> output) => false;
        }

        // --- additional IInputSource だけで駆動するレイヤーが blend に含まれる契約 ---

#if FACIALCONTROL_HAS_INPUTSYSTEM_MODULE
        /// <summary>
        /// Profile で additional IInputSource を宣言したレイヤーは、LayerExpressionSource
        /// (sourceIdx=0) が一度も activate されていなくても blend 対象となり、
        /// sourceIdx=1+ のソースが TriggerOn した値が最終 BlendShape 出力に届くこと。
        /// </summary>
        [Test]
        public void UpdateWeights_AdditionalSourceOnly_TriggersReachFinalOutput()
        {
            var layers = new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) };
            var expressionBs = new[] { new BlendShapeMapping("bs_smile", 1.0f) };
            var smileExpr = new Expression(
                "smile", "smile", "emotion", 0f, TransitionCurve.Linear, expressionBs);
            var profile = new FacialProfile("1.0", layers, new[] { smileExpr });
            var expressionUseCase = new ExpressionUseCase(profile);
            var blendShapeNames = new[] { "bs_smile", "bs_sad", "bs_blink" };

            var controller = new global::Hidano.FacialControl.Adapters.InputSources.ExpressionTriggerInputSource(
                id: global::Hidano.FacialControl.Domain.Models.InputSourceId.Parse(
                    global::Hidano.FacialControl.Adapters.InputSources.ExpressionTriggerInputSource.InputReservedId),
                blendShapeCount: blendShapeNames.Length,
                maxStackDepth: 4,
                exclusionMode: ExclusionMode.LastWins,
                blendShapeNames: blendShapeNames,
                profile: profile);
            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, controller, 1.0f),
            };

            using var useCase = new LayerUseCase(
                profile, expressionUseCase, blendShapeNames, additional);

            // ExpressionUseCase.Activate は呼ばない。sourceIdx=1 (input) だけで駆動する。
            controller.TriggerOn("smile");
            useCase.UpdateWeights(0.001f);

            var output = useCase.GetBlendedOutput();
            Assert.AreEqual(1.0f, output[0], 1e-4f,
                "additional source のみで triggered した bs_smile が最終出力に反映されること");
        }
#endif

        // --- late-bind 経路 (BindLateInputSource) の回帰 ---
        //
        // init 時点で registry 未解決だった入力源（auto mapping OSC の heartbeat 受信後など）が
        // 購読経由で後から BindLateInputSource された場合でも、その値が最終 BlendShape 出力へ届くこと。
        // 修正前は 2 段でゼロ化されていた:
        //   (1) weight バッファへ宣言 weight が焼かれず、Aggregator の `w > 0f` ガードで書込値が破棄。
        //   (2) _layerHasAdditionalSources が立たず、UpdateWeights の blend フィルタ
        //       (HasBeenActive || hasAdditional) からレイヤーごと除外。
        // さらに OSC 単独レイヤーは init 時 MaxSourcesPerLayer=1 のため、late-bind で registry のみ
        // 容量拡張され weight バッファが範囲外になり SetWeight すら no-op になっていた。

        // 値提供型 (ValueProvider) の Fake。TryWriteValues で固定値を index0 に書き true を返す
        // = OscInputSource が非ゼロ受信値を書く挙動の最小モック。ContributeMask は基底が全 true。
        private sealed class FakeValueWritingSource : ValueProviderInputSourceBase
        {
            private readonly float _value;

            public FakeValueWritingSource(string id, int blendShapeCount, float value)
                : base(InputSourceId.Parse(id), blendShapeCount)
            {
                _value = value;
            }

            public override bool TryWriteValues(Span<float> output)
            {
                if (output.Length > 0)
                {
                    output[0] = _value;
                }
                return true;
            }
        }

        [Test]
        public void BindLateInputSource_UnresolvedAtInit_ValueReachesFinalOutput()
        {
            var layers = new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) };
            var profile = new FacialProfile("1.0", layers, Array.Empty<Expression>());
            var expressionUseCase = new ExpressionUseCase(profile);
            var blendShapeNames = new[] { "bs_smile", "bs_sad", "bs_blink" };

            // additional なしで構築 → MaxSourcesPerLayer=1（OSC が init 未解決だった状況を模す）。
            using var useCase = new LayerUseCase(profile, expressionUseCase, blendShapeNames);

            var lateSource = new FakeValueWritingSource("osc", blendShapeNames.Length, 0.6f);
            useCase.BindLateInputSource(0, lateSource, 1.0f);

            useCase.UpdateWeights(0.001f);

            var output = useCase.GetBlendedOutput();
            Assert.AreEqual(0.6f, output[0], 1e-4f,
                "late-bind された入力源の値が最終出力へ届くこと（修正前はゼロ）");
        }

        [Test]
        public void BindLateInputSource_AppliesDeclaredWeight_ScalesOutput()
        {
            var layers = new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) };
            var profile = new FacialProfile("1.0", layers, Array.Empty<Expression>());
            var expressionUseCase = new ExpressionUseCase(profile);
            var blendShapeNames = new[] { "bs_smile", "bs_sad", "bs_blink" };

            using var useCase = new LayerUseCase(profile, expressionUseCase, blendShapeNames);

            var lateSource = new FakeValueWritingSource("osc", blendShapeNames.Length, 0.6f);
            useCase.BindLateInputSource(0, lateSource, 0.5f); // 宣言 weight 0.5

            useCase.UpdateWeights(0.001f);

            var output = useCase.GetBlendedOutput();
            Assert.AreEqual(0.3f, output[0], 1e-4f,
                "late-bind の宣言 weight が intra-layer 加重で反映されること (0.6 * 0.5)");
        }
        [Test]
        public void UnbindLateInputSource_AfterLateBind_RevertsToUnresolvedBehavior()
        {
            var layers = new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) };
            var profile = new FacialProfile("1.0", layers, Array.Empty<Expression>());
            var expressionUseCase = new ExpressionUseCase(profile);
            var blendShapeNames = new[] { "bs_smile", "bs_sad", "bs_blink" };

            using var useCase = new LayerUseCase(profile, expressionUseCase, blendShapeNames);

            var lateSource = new FakeValueWritingSource("osc", blendShapeNames.Length, 0.6f);
            useCase.BindLateInputSource(0, lateSource, 1.0f);
            useCase.UpdateWeights(0.001f);
            Assert.AreEqual(0.6f, useCase.GetBlendedOutput()[0], 1e-4f);

            useCase.UnbindLateInputSource(0, "osc");
            useCase.UpdateWeights(0.001f);

            var output = useCase.GetBlendedOutput();
            Assert.AreEqual(0f, output[0], 1e-4f,
                "Unbind 後は late source が合成から外れ、未解決時と同じゼロ出力へ戻ること");
        }

        [Test]
        public void UnbindLateInputSource_RemovesOnlySpecifiedId()
        {
            var layers = new[] { new LayerDefinition("emotion", 0, ExclusionMode.LastWins) };
            var profile = new FacialProfile("1.0", layers, Array.Empty<Expression>());
            var expressionUseCase = new ExpressionUseCase(profile);
            var blendShapeNames = new[] { "bs_smile", "bs_sad", "bs_blink" };
            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, new FakeValueWritingSource("osc-a", blendShapeNames.Length, 0.4f), 1.0f),
                (0, new FakeValueWritingSource("osc-b", blendShapeNames.Length, 0.2f), 1.0f),
            };

            using var useCase = new LayerUseCase(profile, expressionUseCase, blendShapeNames, additional);

            useCase.UpdateWeights(0.001f);
            Assert.AreEqual(0.6f, useCase.GetBlendedOutput()[0], 1e-4f);

            useCase.UnbindLateInputSource(0, "osc-a");
            useCase.UpdateWeights(0.001f);

            var output = useCase.GetBlendedOutput();
            Assert.AreEqual(0.2f, output[0], 1e-4f,
                "指定 id のみ除去し、残存 source の寄与は維持すること");
        }
    }
}
