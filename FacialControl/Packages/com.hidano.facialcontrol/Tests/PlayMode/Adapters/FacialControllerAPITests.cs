using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.PlayMode.Adapters
{
    /// <summary>
    /// P08-T03: FacialController の公開 API テスト。
    /// Activate / Deactivate / LoadProfile / ReloadProfile の動作を検証する。
    /// </summary>
    [TestFixture]
    public class FacialControllerAPITests
    {
        private GameObject _gameObject;

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_gameObject);
                _gameObject = null;
            }
        }

        // ================================================================
        // Activate
        // ================================================================

        [Test]
        public void Activate_ValidExpression_AddsToActiveList()
        {
            var controller = CreateInitializedController(out var profile);
            var expression = profile.Expressions.Span[0];

            controller.Activate(expression);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual(expression.Id, active[0].Id);
        }

        [Test]
        public void Activate_MultipleExpressions_LastWins_OnlyLastActive()
        {
            var controller = CreateInitializedController(out var profile);
            var expr1 = profile.Expressions.Span[0];
            var expr2 = profile.Expressions.Span[1];

            controller.Activate(expr1);
            controller.Activate(expr2);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual(expr2.Id, active[0].Id);
        }

        [Test]
        public void Activate_BeforeInitialization_LogsWarning()
        {
            _gameObject = CreateGameObjectWithAnimator();
            var controller = _gameObject.AddComponent<FacialController>();
            var expression = CreateTestExpression("test-id", "test", "emotion");

            LogAssert.Expect(LogType.Warning, "FacialController が初期化されていません。Activate は無視されます。");
            controller.Activate(expression);
        }

        // ================================================================
        // Deactivate
        // ================================================================

        [Test]
        public void Deactivate_ActiveExpression_RemovesFromActiveList()
        {
            var controller = CreateInitializedController(out var profile);
            var expression = profile.Expressions.Span[0];

            controller.Activate(expression);
            controller.Deactivate(expression);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(0, active.Count);
        }

        [Test]
        public void Deactivate_InactiveExpression_DoesNotThrow()
        {
            var controller = CreateInitializedController(out var profile);
            var expression = profile.Expressions.Span[0];

            Assert.DoesNotThrow(() => controller.Deactivate(expression));
        }

        [Test]
        public void Deactivate_BeforeInitialization_LogsWarning()
        {
            _gameObject = CreateGameObjectWithAnimator();
            var controller = _gameObject.AddComponent<FacialController>();
            var expression = CreateTestExpression("test-id", "test", "emotion");

            LogAssert.Expect(LogType.Warning, "FacialController が初期化されていません。Deactivate は無視されます。");
            controller.Deactivate(expression);
        }

        // ================================================================
        // LoadProfile
        // ================================================================

        [Test]
        public void LoadProfile_ValidProfileSO_RebuildsGraph()
        {
            var controller = CreateInitializedController(out _);

            var newProfileSO = CreateProfileSOWithProfile(CreateAlternativeProfile());

            Assert.DoesNotThrow(() => controller.LoadProfile(newProfileSO));
            Assert.IsTrue(controller.IsInitialized);
        }

        [Test]
        public void LoadProfile_NullProfileSO_LogsWarning()
        {
            var controller = CreateInitializedController(out _);

            LogAssert.Expect(LogType.Warning, "ProfileSO が null です。LoadProfile は無視されます。");
            controller.LoadProfile(null);
        }

        [Test]
        public void LoadProfile_ClearsActiveExpressions()
        {
            var controller = CreateInitializedController(out var profile);
            var expression = profile.Expressions.Span[0];
            controller.Activate(expression);

            var newProfileSO = CreateProfileSOWithProfile(CreateAlternativeProfile());
            controller.LoadProfile(newProfileSO);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(0, active.Count);
        }

        // ================================================================
        // ReloadProfile
        // ================================================================

        [Test]
        public void ReloadProfile_WithLoadedProfile_RebuildsGraph()
        {
            var controller = CreateInitializedController(out _);

            Assert.DoesNotThrow(() => controller.ReloadProfile());
            Assert.IsTrue(controller.IsInitialized);
        }

        [Test]
        public void ReloadProfile_ClearsActiveExpressions()
        {
            var controller = CreateInitializedController(out var profile);
            var expression = profile.Expressions.Span[0];
            controller.Activate(expression);

            controller.ReloadProfile();

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(0, active.Count);
        }

        [Test]
        public void ReloadProfile_BeforeInitialization_LogsWarning()
        {
            _gameObject = CreateGameObjectWithAnimator();
            var controller = _gameObject.AddComponent<FacialController>();

            LogAssert.Expect(LogType.Warning, "FacialController が初期化されていません。ReloadProfile は無視されます。");
            controller.ReloadProfile();
        }

        // ================================================================
        // GetActiveExpressions
        // ================================================================

        [Test]
        public void GetActiveExpressions_NoActive_ReturnsEmptyList()
        {
            var controller = CreateInitializedController(out _);

            var active = controller.GetActiveExpressions();

            Assert.IsNotNull(active);
            Assert.AreEqual(0, active.Count);
        }

        // ================================================================
        // CurrentProfile プロパティ
        // ================================================================

        [Test]
        public void CurrentProfile_AfterInitialize_ReturnsProfile()
        {
            var controller = CreateInitializedController(out _);

            Assert.IsTrue(controller.CurrentProfile.HasValue);
        }

        [Test]
        public void CurrentProfile_BeforeInitialize_ReturnsNull()
        {
            _gameObject = CreateGameObjectWithAnimator();
            var controller = _gameObject.AddComponent<FacialController>();

            Assert.IsFalse(controller.CurrentProfile.HasValue);
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private FacialController CreateInitializedController(out FacialProfile profile)
        {
            profile = CreateTestProfile();
            _gameObject = CreateGameObjectWithAnimatorAndRenderer();
            var controller = _gameObject.AddComponent<FacialController>();
            var profileSO = CreateProfileSOWithProfile(profile);
            controller.ProfileSO = profileSO;
            controller.InitializeWithProfile(profile);
            return controller;
        }

        private static GameObject CreateGameObjectWithAnimator()
        {
            var go = new GameObject("FacialControllerAPITest");
            go.AddComponent<Animator>();
            return go;
        }

        private static GameObject CreateGameObjectWithAnimatorAndRenderer()
        {
            var go = new GameObject("FacialControllerAPITest");
            go.AddComponent<Animator>();
            var childObj = new GameObject("Mesh");
            childObj.transform.SetParent(go.transform);
            childObj.AddComponent<SkinnedMeshRenderer>();
            return go;
        }

        private static FacialProfile CreateTestProfile()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
            };
            var expressions = new Expression[]
            {
                new Expression("expr-001", "Happy", "emotion", 0.25f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[]
                    {
                        new BlendShapeMapping("smile", 1.0f)
                    }),
                new Expression("expr-002", "Sad", "emotion", 0.25f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[]
                    {
                        new BlendShapeMapping("frown", 0.8f)
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
            return new FacialProfile("1.0.0", layers, expressions);
        }

        private static FacialProfileSO CreateProfileSO()
        {
            return UnityEngine.ScriptableObject.CreateInstance<FacialProfileSO>();
        }

        private static FacialProfileSO CreateProfileSOWithProfile(FacialProfile profile)
        {
            var so = UnityEngine.ScriptableObject.CreateInstance<FacialProfileSO>();
            so.SchemaVersion = profile.SchemaVersion;
            so.LayerCount = profile.Layers.Length;
            so.ExpressionCount = profile.Expressions.Length;
            return so;
        }

        private static Expression CreateTestExpression(string id, string name, string layer)
        {
            return new Expression(id, name, layer);
        }
    }
}
