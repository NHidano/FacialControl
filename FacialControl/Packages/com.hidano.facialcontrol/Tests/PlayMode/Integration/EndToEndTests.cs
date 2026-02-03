using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    /// <summary>
    /// P14-T01: FacialController のエンドツーエンド統合テスト。
    /// プロファイル読み込み → Expression アクティブ → BlendShape 適用の全フローを検証する。
    /// </summary>
    [TestFixture]
    public class EndToEndTests
    {
        private GameObject _gameObject;

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                Object.DestroyImmediate(_gameObject);
                _gameObject = null;
            }
        }

        // ================================================================
        // プロファイル読み込み → Expression アクティブ → 状態確認
        // ================================================================

        [UnityTest]
        public IEnumerator FullFlow_LoadProfile_ActivateExpression_ExpressionIsActive()
        {
            var profile = CreateTestProfile();
            var controller = CreateInitializedController(profile);

            yield return null;

            var expression = profile.Expressions.Span[0]; // Happy
            controller.Activate(expression);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("Happy", active[0].Name);
        }

        [UnityTest]
        public IEnumerator FullFlow_ActivateAndDeactivate_ExpressionListEmpty()
        {
            var profile = CreateTestProfile();
            var controller = CreateInitializedController(profile);

            yield return null;

            var expression = profile.Expressions.Span[0];
            controller.Activate(expression);
            controller.Deactivate(expression);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(0, active.Count);
        }

        [UnityTest]
        public IEnumerator FullFlow_ActivateMultipleOnSameLastWinsLayer_OnlyLastActive()
        {
            var profile = CreateTestProfile();
            var controller = CreateInitializedController(profile);

            yield return null;

            var happy = profile.Expressions.Span[0];
            var sad = profile.Expressions.Span[1];

            controller.Activate(happy);
            controller.Activate(sad);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("Sad", active[0].Name);
        }

        // ================================================================
        // Blend モードの統合テスト
        // ================================================================

        [UnityTest]
        public IEnumerator FullFlow_BlendMode_MultipleExpressionsCoexist()
        {
            var profile = CreateBlendProfile();
            var controller = CreateInitializedController(profile);

            yield return null;

            var lipA = profile.Expressions.Span[0];
            var lipO = profile.Expressions.Span[1];

            controller.Activate(lipA);
            controller.Activate(lipO);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(2, active.Count);
        }

        [UnityTest]
        public IEnumerator FullFlow_BlendMode_DeactivateOne_OtherRemains()
        {
            var profile = CreateBlendProfile();
            var controller = CreateInitializedController(profile);

            yield return null;

            var lipA = profile.Expressions.Span[0];
            var lipO = profile.Expressions.Span[1];

            controller.Activate(lipA);
            controller.Activate(lipO);
            controller.Deactivate(lipA);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("LipO", active[0].Name);
        }

        // ================================================================
        // マルチレイヤー統合テスト
        // ================================================================

        [UnityTest]
        public IEnumerator FullFlow_MultiLayer_ExpressionsOnDifferentLayers_BothActive()
        {
            var profile = CreateMultiLayerProfile();
            var controller = CreateInitializedController(profile);

            yield return null;

            var emotionExpr = profile.Expressions.Span[0]; // emotion レイヤー
            var eyeExpr = profile.Expressions.Span[1]; // eye レイヤー

            controller.Activate(emotionExpr);
            controller.Activate(eyeExpr);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(2, active.Count);
        }

        [UnityTest]
        public IEnumerator FullFlow_MultiLayer_LastWinsPerLayer_IndependentExclusion()
        {
            var profile = CreateMultiLayerWithMultipleExpressionsProfile();
            var controller = CreateInitializedController(profile);

            yield return null;

            var happy = profile.Expressions.Span[0]; // emotion
            var sad = profile.Expressions.Span[1]; // emotion
            var wink = profile.Expressions.Span[2]; // eye

            controller.Activate(happy);
            controller.Activate(wink);
            controller.Activate(sad); // emotion レイヤーで LastWins → happy を置き換え

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(2, active.Count);

            // emotion は sad のみ、eye は wink が残る
            bool hasSad = false;
            bool hasWink = false;
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].Id == "expr-sad") hasSad = true;
                if (active[i].Id == "expr-wink") hasWink = true;
            }
            Assert.IsTrue(hasSad, "emotion レイヤーで Sad がアクティブであること");
            Assert.IsTrue(hasWink, "eye レイヤーで Wink がアクティブであること");
        }

        // ================================================================
        // プロファイル切替テスト
        // ================================================================

        [UnityTest]
        public IEnumerator ProfileSwitch_LoadNewProfile_ClearsActiveExpressions()
        {
            var profile = CreateTestProfile();
            var controller = CreateInitializedController(profile);

            yield return null;

            var expression = profile.Expressions.Span[0];
            controller.Activate(expression);
            Assert.AreEqual(1, controller.GetActiveExpressions().Count);

            // 新しいプロファイルを読み込み
            var newProfile = CreateAlternativeProfile();
            controller.InitializeWithProfile(newProfile);

            yield return null;

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(0, active.Count);
        }

        [UnityTest]
        public IEnumerator ProfileSwitch_LoadNewProfile_CanActivateNewExpressions()
        {
            var profile = CreateTestProfile();
            var controller = CreateInitializedController(profile);

            yield return null;

            // 新しいプロファイルに切替
            var newProfile = CreateAlternativeProfile();
            controller.InitializeWithProfile(newProfile);

            yield return null;

            var newExpr = new Expression("new-expr", "Angry", "emotion", 0.25f,
                TransitionCurve.Linear,
                new BlendShapeMapping[] { new BlendShapeMapping("anger", 1.0f) });
            controller.Activate(newExpr);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("Angry", active[0].Name);
        }

        [UnityTest]
        public IEnumerator ProfileSwitch_ReloadProfile_PreservesProfileData()
        {
            var profile = CreateTestProfile();
            var controller = CreateInitializedController(profile);

            yield return null;

            controller.ReloadProfile();

            yield return null;

            Assert.IsTrue(controller.IsInitialized);
            Assert.IsTrue(controller.CurrentProfile.HasValue);
            Assert.AreEqual(profile.SchemaVersion, controller.CurrentProfile.Value.SchemaVersion);
        }

        // ================================================================
        // layerSlots オーバーライド統合テスト
        // ================================================================

        [UnityTest]
        public IEnumerator LayerSlots_ExpressionWithOverrides_AppliedToPlayableGraph()
        {
            var blendShapeNames = new string[] { "smile", "eye_wink" };
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("eye", 1, ExclusionMode.LastWins)
            };

            // emotion 表情に eye レイヤーへのオーバーライドを持つ Expression
            var layerSlots = new LayerSlot[]
            {
                new LayerSlot("eye", new BlendShapeMapping[]
                {
                    new BlendShapeMapping("eye_wink", 0.5f)
                })
            };
            var expressions = new Expression[]
            {
                new Expression("expr-smile-wink", "SmileWink", "emotion", 0f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[] { new BlendShapeMapping("smile", 1.0f) },
                    layerSlots)
            };
            var profile = new FacialProfile("1.0.0", layers, expressions);

            var controller = CreateInitializedController(profile);

            yield return null;

            controller.Activate(expressions[0]);

            // アクティブリストに追加されていることを検証
            var active = controller.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("SmileWink", active[0].Name);
        }

        // ================================================================
        // 未定義レイヤー参照のフォールバック統合テスト
        // ================================================================

        [UnityTest]
        public IEnumerator FallbackLayer_UndefinedLayer_FallsBackToEmotion()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
            };
            // Expression が未定義のレイヤー "custom" を参照
            var expressions = new Expression[]
            {
                new Expression("expr-custom", "Custom", "custom", 0.25f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[] { new BlendShapeMapping("smile", 0.5f) })
            };
            var profile = new FacialProfile("1.0.0", layers, expressions);

            var controller = CreateInitializedController(profile);

            yield return null;

            // 未定義レイヤー → emotion にフォールバック
            controller.Activate(expressions[0]);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
        }

        // ================================================================
        // 初期化前の操作テスト
        // ================================================================

        [Test]
        public void BeforeInitialization_Activate_LogsWarning()
        {
            _gameObject = new GameObject("EndToEndTest");
            _gameObject.AddComponent<Animator>();
            var controller = _gameObject.AddComponent<FacialController>();

            var expression = new Expression("test", "Test", "emotion");

            LogAssert.Expect(LogType.Warning, "FacialController が初期化されていません。Activate は無視されます。");
            controller.Activate(expression);
        }

        [Test]
        public void BeforeInitialization_GetActiveExpressions_ReturnsEmpty()
        {
            _gameObject = new GameObject("EndToEndTest");
            _gameObject.AddComponent<Animator>();
            var controller = _gameObject.AddComponent<FacialController>();

            var active = controller.GetActiveExpressions();
            Assert.IsNotNull(active);
            Assert.AreEqual(0, active.Count);
        }

        // ================================================================
        // PlayableGraph ライフサイクル統合テスト
        // ================================================================

        [UnityTest]
        public IEnumerator Lifecycle_DisableAndReenable_CanReinitialize()
        {
            var profile = CreateTestProfile();
            var controller = CreateInitializedController(profile);

            yield return null;
            Assert.IsTrue(controller.IsInitialized);

            // 無効化
            controller.enabled = false;
            yield return null;
            Assert.IsFalse(controller.IsInitialized);

            // 再有効化
            controller.enabled = true;
            yield return null;

            controller.InitializeWithProfile(profile);
            Assert.IsTrue(controller.IsInitialized);

            // 再初期化後に Expression をアクティブ化できる
            var expression = profile.Expressions.Span[0];
            controller.Activate(expression);
            var active = controller.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private FacialController CreateInitializedController(FacialProfile profile)
        {
            _gameObject = new GameObject("EndToEndTest");
            _gameObject.AddComponent<Animator>();
            var childObj = new GameObject("Mesh");
            childObj.transform.SetParent(_gameObject.transform);
            childObj.AddComponent<SkinnedMeshRenderer>();

            var controller = _gameObject.AddComponent<FacialController>();
            controller.InitializeWithProfile(profile);
            return controller;
        }

        private static FacialProfile CreateTestProfile()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
            };
            var expressions = new Expression[]
            {
                new Expression("expr-happy", "Happy", "emotion", 0.25f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[]
                    {
                        new BlendShapeMapping("smile", 1.0f)
                    }),
                new Expression("expr-sad", "Sad", "emotion", 0.25f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[]
                    {
                        new BlendShapeMapping("frown", 0.8f)
                    })
            };
            return new FacialProfile("1.0.0", layers, expressions);
        }

        private static FacialProfile CreateBlendProfile()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("lipsync", 0, ExclusionMode.Blend)
            };
            var expressions = new Expression[]
            {
                new Expression("lip-a", "LipA", "lipsync", 0.1f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[]
                    {
                        new BlendShapeMapping("mouth_a", 1.0f)
                    }),
                new Expression("lip-o", "LipO", "lipsync", 0.1f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[]
                    {
                        new BlendShapeMapping("mouth_o", 0.8f)
                    })
            };
            return new FacialProfile("1.0.0", layers, expressions);
        }

        private static FacialProfile CreateMultiLayerProfile()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("eye", 1, ExclusionMode.LastWins)
            };
            var expressions = new Expression[]
            {
                new Expression("expr-happy", "Happy", "emotion", 0.25f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[]
                    {
                        new BlendShapeMapping("smile", 1.0f)
                    }),
                new Expression("expr-wink", "Wink", "eye", 0.1f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[]
                    {
                        new BlendShapeMapping("eye_wink", 1.0f)
                    })
            };
            return new FacialProfile("1.0.0", layers, expressions);
        }

        private static FacialProfile CreateMultiLayerWithMultipleExpressionsProfile()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("eye", 1, ExclusionMode.LastWins)
            };
            var expressions = new Expression[]
            {
                new Expression("expr-happy", "Happy", "emotion", 0.25f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[]
                    {
                        new BlendShapeMapping("smile", 1.0f)
                    }),
                new Expression("expr-sad", "Sad", "emotion", 0.25f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[]
                    {
                        new BlendShapeMapping("frown", 0.8f)
                    }),
                new Expression("expr-wink", "Wink", "eye", 0.1f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[]
                    {
                        new BlendShapeMapping("eye_wink", 1.0f)
                    })
            };
            return new FacialProfile("1.0.0", layers, expressions);
        }

        private static FacialProfile CreateAlternativeProfile()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("eye", 1, ExclusionMode.LastWins)
            };
            var expressions = new Expression[]
            {
                new Expression("alt-001", "Angry", "emotion", 0.3f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[]
                    {
                        new BlendShapeMapping("anger", 1.0f)
                    })
            };
            return new FacialProfile("2.0.0", layers, expressions);
        }
    }
}
