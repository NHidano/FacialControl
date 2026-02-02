using System;
using System.Collections.Generic;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Application.UseCases;

namespace Hidano.FacialControl.Tests.EditMode.Application
{
    [TestFixture]
    public class ExpressionUseCaseTests
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

        private static Expression CreateExpression(
            string id = "expr-1",
            string name = "smile",
            string layer = "emotion")
        {
            return new Expression(id, name, layer);
        }

        private static FacialProfile CreateProfile(
            LayerDefinition[] layers = null,
            Expression[] expressions = null)
        {
            return new FacialProfile(
                "1.0",
                layers ?? CreateDefaultLayers(),
                expressions ?? new[] { CreateExpression() });
        }

        private ExpressionUseCase _useCase;

        [SetUp]
        public void SetUp()
        {
            var profile = CreateProfile();
            _useCase = new ExpressionUseCase(profile);
        }

        // --- コンストラクタ ---

        [Test]
        public void Constructor_ValidProfile_CreatesInstance()
        {
            var profile = CreateProfile();
            var useCase = new ExpressionUseCase(profile);

            Assert.IsNotNull(useCase);
        }

        [Test]
        public void Constructor_InitiallyNoActiveExpressions()
        {
            var activeExpressions = _useCase.GetActiveExpressions();

            Assert.AreEqual(0, activeExpressions.Count);
        }

        // --- Activate ---

        [Test]
        public void Activate_ValidExpression_AddsToActiveList()
        {
            var expr = CreateExpression();

            _useCase.Activate(expr);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("expr-1", active[0].Id);
        }

        [Test]
        public void Activate_LastWinsLayer_ReplacesExistingExpression()
        {
            var expr1 = CreateExpression("expr-1", "smile", "emotion");
            var expr2 = CreateExpression("expr-2", "sad", "emotion");

            _useCase.Activate(expr1);
            _useCase.Activate(expr2);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("expr-2", active[0].Id);
        }

        [Test]
        public void Activate_BlendLayer_AllowsMultipleExpressions()
        {
            var expr1 = CreateExpression("expr-1", "talk_a", "lipsync");
            var expr2 = CreateExpression("expr-2", "talk_o", "lipsync");

            _useCase.Activate(expr1);
            _useCase.Activate(expr2);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(2, active.Count);
        }

        [Test]
        public void Activate_DifferentLayers_BothActive()
        {
            var emotionExpr = CreateExpression("expr-1", "smile", "emotion");
            var eyeExpr = CreateExpression("expr-2", "wink", "eye");

            _useCase.Activate(emotionExpr);
            _useCase.Activate(eyeExpr);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(2, active.Count);
        }

        [Test]
        public void Activate_SameExpressionTwice_LastWins_NoDuplicate()
        {
            var expr = CreateExpression("expr-1", "smile", "emotion");

            _useCase.Activate(expr);
            _useCase.Activate(expr);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("expr-1", active[0].Id);
        }

        [Test]
        public void Activate_SameExpressionTwice_Blend_NoDuplicate()
        {
            var expr = CreateExpression("expr-1", "talk_a", "lipsync");

            _useCase.Activate(expr);
            _useCase.Activate(expr);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("expr-1", active[0].Id);
        }

        [Test]
        public void Activate_MultipleExpressionsInBlendLayer_AllRetained()
        {
            var expr1 = CreateExpression("expr-1", "talk_a", "lipsync");
            var expr2 = CreateExpression("expr-2", "talk_o", "lipsync");
            var expr3 = CreateExpression("expr-3", "talk_u", "lipsync");

            _useCase.Activate(expr1);
            _useCase.Activate(expr2);
            _useCase.Activate(expr3);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(3, active.Count);
        }

        [Test]
        public void Activate_LastWinsLayer_MultipleReplacements_OnlyLastRemains()
        {
            var expr1 = CreateExpression("expr-1", "smile", "emotion");
            var expr2 = CreateExpression("expr-2", "sad", "emotion");
            var expr3 = CreateExpression("expr-3", "angry", "emotion");

            _useCase.Activate(expr1);
            _useCase.Activate(expr2);
            _useCase.Activate(expr3);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("expr-3", active[0].Id);
        }

        [Test]
        public void Activate_UndefinedLayer_FallsBackToEmotion()
        {
            // プロファイルにない "unknown" レイヤーの Expression → emotion にフォールバック
            var expr1 = CreateExpression("expr-1", "smile", "emotion");
            var expr2 = CreateExpression("expr-2", "custom", "unknown");

            _useCase.Activate(expr1);
            _useCase.Activate(expr2);

            // emotion は LastWins なので、expr2 がフォールバックして emotion レイヤーに入り、
            // expr1 を置き換える
            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("expr-2", active[0].Id);
        }

        // --- Deactivate ---

        [Test]
        public void Deactivate_ActiveExpression_RemovesFromActiveList()
        {
            var expr = CreateExpression();
            _useCase.Activate(expr);

            _useCase.Deactivate(expr);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(0, active.Count);
        }

        [Test]
        public void Deactivate_NonActiveExpression_DoesNothing()
        {
            var expr1 = CreateExpression("expr-1", "smile", "emotion");
            var expr2 = CreateExpression("expr-2", "sad", "emotion");
            _useCase.Activate(expr1);

            // expr2 はアクティブではないので、何もしない
            _useCase.Deactivate(expr2);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("expr-1", active[0].Id);
        }

        [Test]
        public void Deactivate_NoActiveExpressions_DoesNothing()
        {
            var expr = CreateExpression();

            // アクティブな Expression がない状態で Deactivate しても例外なし
            Assert.DoesNotThrow(() => _useCase.Deactivate(expr));
        }

        [Test]
        public void Deactivate_OneOfMultipleInBlendLayer_OthersRemain()
        {
            var expr1 = CreateExpression("expr-1", "talk_a", "lipsync");
            var expr2 = CreateExpression("expr-2", "talk_o", "lipsync");
            var expr3 = CreateExpression("expr-3", "talk_u", "lipsync");

            _useCase.Activate(expr1);
            _useCase.Activate(expr2);
            _useCase.Activate(expr3);

            _useCase.Deactivate(expr2);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(2, active.Count);
            Assert.AreEqual("expr-1", active[0].Id);
            Assert.AreEqual("expr-3", active[1].Id);
        }

        [Test]
        public void Deactivate_FromOneLayer_OtherLayersUnaffected()
        {
            var emotionExpr = CreateExpression("expr-1", "smile", "emotion");
            var lipsyncExpr = CreateExpression("expr-2", "talk_a", "lipsync");

            _useCase.Activate(emotionExpr);
            _useCase.Activate(lipsyncExpr);

            _useCase.Deactivate(emotionExpr);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("expr-2", active[0].Id);
        }

        [Test]
        public void Deactivate_ById_RemovesCorrectExpression()
        {
            var expr1 = CreateExpression("expr-1", "talk_a", "lipsync");
            var expr2 = CreateExpression("expr-2", "talk_o", "lipsync");

            _useCase.Activate(expr1);
            _useCase.Activate(expr2);

            // ID が一致する Expression のみ削除される
            _useCase.Deactivate(expr1);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("expr-2", active[0].Id);
        }

        // --- GetActiveExpressions ---

        [Test]
        public void GetActiveExpressions_ReturnsDefensiveCopy()
        {
            var expr = CreateExpression();
            _useCase.Activate(expr);

            var active1 = _useCase.GetActiveExpressions();
            var active2 = _useCase.GetActiveExpressions();

            // 別のリストインスタンスが返される
            Assert.AreNotSame(active1, active2);
        }

        [Test]
        public void GetActiveExpressions_ModifyingReturnedList_DoesNotAffectInternal()
        {
            var expr = CreateExpression();
            _useCase.Activate(expr);

            var active = _useCase.GetActiveExpressions();
            active.Clear();

            // 内部状態は変更されない
            var activeAgain = _useCase.GetActiveExpressions();
            Assert.AreEqual(1, activeAgain.Count);
        }

        [Test]
        public void GetActiveExpressions_MultipleLayersActive_ReturnsAll()
        {
            var expr1 = CreateExpression("expr-1", "smile", "emotion");
            var expr2 = CreateExpression("expr-2", "talk_a", "lipsync");
            var expr3 = CreateExpression("expr-3", "wink", "eye");

            _useCase.Activate(expr1);
            _useCase.Activate(expr2);
            _useCase.Activate(expr3);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(3, active.Count);
        }

        // --- SetProfile ---

        [Test]
        public void SetProfile_ClearsActiveExpressions()
        {
            var expr = CreateExpression();
            _useCase.Activate(expr);

            var newProfile = CreateProfile();
            _useCase.SetProfile(newProfile);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(0, active.Count);
        }

        [Test]
        public void SetProfile_NewProfileLayerRulesApply()
        {
            // emotion を Blend モードに変更した新しいプロファイル
            var newLayers = new[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.Blend),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend),
                new LayerDefinition("eye", 2, ExclusionMode.LastWins)
            };
            var newProfile = CreateProfile(layers: newLayers);
            _useCase.SetProfile(newProfile);

            var expr1 = CreateExpression("expr-1", "smile", "emotion");
            var expr2 = CreateExpression("expr-2", "sad", "emotion");

            _useCase.Activate(expr1);
            _useCase.Activate(expr2);

            // emotion が Blend モードなので、両方アクティブ
            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(2, active.Count);
        }

        // --- 日本語 Expression 名 ---

        [Test]
        public void Activate_JapaneseExpressionName_Works()
        {
            var expr = CreateExpression("expr-jp", "笑顔", "emotion");

            _useCase.Activate(expr);

            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("笑顔", active[0].Name);
        }

        // --- 複雑なシナリオ ---

        [Test]
        public void ComplexScenario_ActivateDeactivateAcrossLayers()
        {
            var smile = CreateExpression("expr-1", "smile", "emotion");
            var sad = CreateExpression("expr-2", "sad", "emotion");
            var talkA = CreateExpression("expr-3", "talk_a", "lipsync");
            var talkO = CreateExpression("expr-4", "talk_o", "lipsync");
            var wink = CreateExpression("expr-5", "wink", "eye");

            // 初期: 各レイヤーにアクティブ化
            _useCase.Activate(smile);    // emotion: smile
            _useCase.Activate(talkA);    // lipsync: talk_a
            _useCase.Activate(wink);     // eye: wink

            Assert.AreEqual(3, _useCase.GetActiveExpressions().Count);

            // emotion を sad に切り替え（LastWins なので smile が消える）
            _useCase.Activate(sad);
            var active = _useCase.GetActiveExpressions();
            Assert.AreEqual(3, active.Count);

            // lipsync に追加（Blend なので両方残る）
            _useCase.Activate(talkO);
            active = _useCase.GetActiveExpressions();
            Assert.AreEqual(4, active.Count);

            // lipsync から talk_a を非アクティブ化
            _useCase.Deactivate(talkA);
            active = _useCase.GetActiveExpressions();
            Assert.AreEqual(3, active.Count);

            // eye を非アクティブ化
            _useCase.Deactivate(wink);
            active = _useCase.GetActiveExpressions();
            Assert.AreEqual(2, active.Count);
        }
    }
}
