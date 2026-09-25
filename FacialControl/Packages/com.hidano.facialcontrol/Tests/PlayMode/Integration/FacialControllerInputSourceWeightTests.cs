using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    /// <summary>
    // ランタイム weight 変更 API の PlayMode 統合テスト。
    /// </summary>
    /// <remarks>
    /// 観測完了条件: メインスレッド外スレッドから <c>SetInputSourceWeight</c> を呼んでも
    /// 次フレームの <c>Aggregate</c> 観測 (BlendShape 出力) に反映されること
    /// 。
    /// </remarks>
    [TestFixture]
    public class FacialControllerInputSourceWeightTests
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
        // 任意スレッド書込 → 次 Aggregate / BlendShape 出力で観測
        // (LayerUseCase 直結で end-to-end 検証。FacialController.SetInputSourceWeight は
        //  この LayerUseCase API への薄い forwarding なので、契約は同等)。
        // ================================================================

        [UnityTest]
        public IEnumerator SetInputSourceWeight_FromBackgroundThread_ReflectedInBlendShapeOutput()
        {
            string[] blendShapeNames = { "bs_a", "bs_b" };
            var profile = CreateLayerProfileWithExpression("emotion", "expr-1", "bs_a", value: 1.0f);
            var expressionUseCase = new ExpressionUseCase(profile);

            // sourceIdx=1 に FakeSource (bs_b に値 1.0 を書込む)。weight=0 で初期化。
            var fake = new FakeSelectiveValueSource(
                "osc", blendShapeCount: 2, writeIndex: 1, writeValue: 1.0f);
            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, fake, 0.0f)
            };
            var layerUseCase = new LayerUseCase(profile, expressionUseCase, blendShapeNames, additional);

            try
            {
                // LayerExpressionSource (sourceIdx=0) を活性化するため Expression を Activate。
                // transitionDuration=0 のため 1 frame で値が target に到達する。
                expressionUseCase.Activate(profile.Expressions.Span[0]);
                layerUseCase.UpdateWeights(0.001f);

                var initial = layerUseCase.GetBlendedOutput();
                Assert.AreEqual(1.0f, initial[0], 1e-4f, "前提: source0 (Expression) の bs_a 寄与が出ていること。");
                Assert.AreEqual(0.0f, initial[1], 1e-4f, "前提: source1 (FakeSource) の bs_b 寄与は weight=0 で出ないこと。");

                // 背景スレッドから FakeSource の weight を 0.7 へ書込む。
                int mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
                int? backgroundThreadId = null;
                var task = Task.Run(() =>
                {
                    backgroundThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
                    layerUseCase.SetInputSourceWeight(0, sourceIdx: 1, weight: 0.7f);
                });
                task.Wait();
                Assert.IsNotNull(backgroundThreadId);
                Assert.AreNotEqual(mainThreadId, backgroundThreadId.Value,
                    "前提: SetInputSourceWeight は実際にメインスレッド外で呼ばれていること。");

                yield return null;

                // 次フレーム: UpdateWeights → Aggregate 入口の SwapIfDirty で書込が観測される。
                layerUseCase.UpdateWeights(0.016f);

                var next = layerUseCase.GetBlendedOutput();
                Assert.AreEqual(1.0f, next[0], 1e-4f, "source0 の寄与 (bs_a=1.0) は維持されること。");
                Assert.AreEqual(0.7f, next[1], 1e-4f,
                    "background thread からの SetInputSourceWeight が次 BlendShape 出力に反映されること。");
            }
            finally
            {
                layerUseCase.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator BeginInputSourceWeightBatch_FromBackgroundThread_AtomicallyReflected()
        {
            string[] blendShapeNames = { "bs_a", "bs_b", "bs_c" };
            var profile = CreateLayerProfileWithExpression("emotion", "expr-1", "bs_a", value: 1.0f);
            var expressionUseCase = new ExpressionUseCase(profile);

            // sourceIdx=1 / 2 にそれぞれ別 BlendShape へ書込む FakeSource を配置。
            var fakeB = new FakeSelectiveValueSource("osc", blendShapeCount: 3, writeIndex: 1, writeValue: 1.0f);
            var fakeC = new FakeSelectiveValueSource("lipsync", blendShapeCount: 3, writeIndex: 2, writeValue: 1.0f);
            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, fakeB, 0.0f),
                (0, fakeC, 0.0f)
            };
            var layerUseCase = new LayerUseCase(profile, expressionUseCase, blendShapeNames, additional);

            try
            {
                expressionUseCase.Activate(profile.Expressions.Span[0]);
                layerUseCase.UpdateWeights(0.001f);

                // 背景スレッドからバルクスコープで 2 つの weight をまとめて書込む。
                var task = Task.Run(() =>
                {
                    using (var batch = layerUseCase.BeginInputSourceWeightBatch())
                    {
                        batch.SetWeight(0, 1, 0.4f);
                        batch.SetWeight(0, 2, 0.3f);
                    }
                });
                task.Wait();

                yield return null;

                layerUseCase.UpdateWeights(0.016f);
                var output = layerUseCase.GetBlendedOutput();

                Assert.AreEqual(1.0f, output[0], 1e-4f, "source0 (Expression) の寄与は維持されること。");
                Assert.AreEqual(0.4f, output[1], 1e-4f, "FakeB の weight=0.4 が bs_b に反映されること。");
                Assert.AreEqual(0.3f, output[2], 1e-4f, "FakeC の weight=0.3 が bs_c に反映されること。");
            }
            finally
            {
                layerUseCase.Dispose();
            }
        }

        // ================================================================
        // FacialController 経由 API の forwarding と未初期化時の振る舞い
        // ================================================================

        [Test]
        public void FacialController_SetInputSourceWeight_BeforeInitialization_LogsWarning()
        {
            _gameObject = CreateGameObjectWithAnimator();
            var controller = _gameObject.AddComponent<FacialController>();

            LogAssert.Expect(LogType.Warning,
                "FacialController が初期化されていません。SetInputSourceWeight は無視されます。");
            controller.SetInputSourceWeight(0, 1, 0.5f);
        }

        [Test]
        public void FacialController_BeginInputSourceWeightBatch_BeforeInitialization_LogsWarning()
        {
            _gameObject = CreateGameObjectWithAnimator();
            var controller = _gameObject.AddComponent<FacialController>();

            LogAssert.Expect(LogType.Warning,
                "FacialController が初期化されていません。BeginInputSourceWeightBatch は no-op スコープを返します。");
            using (controller.BeginInputSourceWeightBatch())
            {
                // no-op スコープ: SetWeight しても何も起きない、Dispose も安全に呼べる。
            }
        }

        [UnityTest]
        public IEnumerator FacialController_SetInputSourceWeight_FromBackgroundThread_ForwardsToWeightBuffer()
        {
            _gameObject = CreateGameObjectWithAnimatorAndRenderer();
            var controller = _gameObject.AddComponent<FacialController>();
            var so = CreateSOWithBindingDeclaration("mock");
            controller.CharacterSO = so;
            controller.Initialize();

            try
            {
                yield return null;
                Assert.IsTrue(controller.IsInitialized);

                int mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
                int? backgroundThreadId = null;
                var task = Task.Run(() =>
                {
                    backgroundThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
                    controller.SetInputSourceWeight(0, sourceIdx: 1, weight: 0.42f);
                });
                task.Wait();
                Assert.AreNotEqual(mainThreadId, backgroundThreadId.Value);

                yield return null;

                // FacialController は内部で LayerUseCase → WeightBuffer に委譲する。
                // テスト用レンダラーは BlendShape を持たないため bsCount=0 となり
                // LayerUseCase.UpdateWeights は早期 return する。そのため WeightBuffer.SwapIfDirty を
                // 直接発火し、次 Aggregate と同じ契約（Volatile 観測）で readBuffer に反映される
                // ことを検証する。
                var layerUseCase = GetPrivateField<LayerUseCase>(controller, "_layerUseCase");
                Assert.IsNotNull(layerUseCase);
                var weightBuffer = GetPrivateField<LayerInputSourceWeightBuffer>(layerUseCase, "_weightBuffer");
                Assert.IsNotNull(weightBuffer);

                weightBuffer.SwapIfDirty();
                Assert.AreEqual(0.42f, weightBuffer.GetWeight(0, 1), 1e-4f,
                    "FacialController.SetInputSourceWeight の forwarding が次 SwapIfDirty で観測されること。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(so);
            }
        }

        [UnityTest]
        public IEnumerator FacialController_BeginInputSourceWeightBatch_AfterInit_ForwardsBulkScope()
        {
            _gameObject = CreateGameObjectWithAnimatorAndRenderer();
            var controller = _gameObject.AddComponent<FacialController>();
            var so = CreateSOWithBindingDeclaration("mock");
            controller.CharacterSO = so;
            controller.Initialize();

            try
            {
                yield return null;
                Assert.IsTrue(controller.IsInitialized);

                using (var batch = controller.BeginInputSourceWeightBatch())
                {
                    batch.SetWeight(0, 1, 0.55f);
                }

                var layerUseCase = GetPrivateField<LayerUseCase>(controller, "_layerUseCase");
                var weightBuffer = GetPrivateField<LayerInputSourceWeightBuffer>(layerUseCase, "_weightBuffer");
                weightBuffer.SwapIfDirty();
                Assert.AreEqual(0.55f, weightBuffer.GetWeight(0, 1), 1e-4f,
                    "FacialController.BeginInputSourceWeightBatch の BulkScope 経由書込が反映されること。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(so);
            }
        }

        // ================================================================
        // Fakes / ヘルパー
        // ================================================================

        /// <summary>
        /// 指定 index の BlendShape にだけ固定値を書込む値提供型フェイク。
        /// 他の index は呼出側のクリア状態 (ゼロ) のまま残す。
        /// </summary>
        private sealed class FakeSelectiveValueSource : ValueProviderInputSourceBase
        {
            private readonly int _writeIndex;
            private readonly float _writeValue;

            public FakeSelectiveValueSource(string id, int blendShapeCount, int writeIndex, float writeValue)
                : base(InputSourceId.Parse(id), blendShapeCount)
            {
                _writeIndex = writeIndex;
                _writeValue = writeValue;
            }

            public override bool TryWriteValues(Span<float> output)
            {
                if ((uint)_writeIndex < (uint)output.Length)
                {
                    output[_writeIndex] = _writeValue;
                }
                return true;
            }
        }

        private static FacialProfile CreateLayerProfileWithExpression(
            string layerName, string expressionId, string blendShapeName, float value)
        {
            var layers = new[]
            {
                new LayerDefinition(layerName, 0, ExclusionMode.LastWins)
            };
            var expressions = new[]
            {
                new Expression(
                    expressionId, expressionId, layerName,
                    transitionDuration: 0f,
                    transitionCurve: TransitionCurve.Linear,
                    blendShapeValues: new[] { new BlendShapeMapping(blendShapeName, value) })
            };
            return new FacialProfile("1.0.0", layers, expressions);
        }

        /// <summary>
        /// 単一 layer + Mock binding 1 個（slug = <paramref name="slug"/>）の <see cref="FacialCharacterProfileSO"/>
        /// を構築する。layer.inputSources[0] には slug をそのまま id として宣言し、
        /// FacialController が child scope の <see cref="IInputSourceRegistry"/> 経由で sourceIdx=1 として登録するようにする。
        /// </summary>
        private static MockBindingProfileSO CreateSOWithBindingDeclaration(string slug)
        {
            var so = ScriptableObject.CreateInstance<MockBindingProfileSO>();
            so.LayerName = "emotion";
            so.LayerInputSourceId = slug;
            so.WritableAdapterBindings.Add(new ZeroValueAdapterBinding(slug, blendShapeCount: 0)
            {
                Slug = slug
            });
            return so;
        }

        /// <summary>
        /// テスト用 SO。<see cref="LoadProfile"/> で 1 layer + 1 inputSources 宣言の最小プロファイルを返す。
        /// </summary>
        public sealed class MockBindingProfileSO : FacialCharacterProfileSO
        {
            public string LayerName = "emotion";
            public string LayerInputSourceId;

            public List<AdapterBindingBase> WritableAdapterBindings => _adapterBindings;

            public override FacialProfile LoadProfile()
            {
                var layers = new[]
                {
                    new LayerDefinition(LayerName, 0, ExclusionMode.LastWins)
                };
                var layerInputSources = new InputSourceDeclaration[][]
                {
                    new InputSourceDeclaration[]
                    {
                        new InputSourceDeclaration(LayerInputSourceId, 1.0f, null)
                    }
                };
                return new FacialProfile("2.0", layers, null, null, layerInputSources);
            }
        }

        /// <summary>
        /// <see cref="OnStart"/> で slug を primary id として <see cref="ZeroValueInputSource"/> を
        /// <see cref="IInputSourceRegistry"/> に登録する Mock binding。本テストでは
        /// 入力値そのものは観測対象外で、sourceIdx=1 が確保されることのみを目的とする。
        /// </summary>
        [Serializable]
        public sealed class ZeroValueAdapterBinding : AdapterBindingBase
        {
            [NonSerialized] private readonly string _id;
            [NonSerialized] private readonly int _blendShapeCount;

            public ZeroValueAdapterBinding(string id, int blendShapeCount)
            {
                _id = id;
                _blendShapeCount = blendShapeCount;
            }

            public override void OnStart(in AdapterBuildContext ctx)
            {
                var slug = AdapterSlug.Parse(Slug);
                ctx.InputSourceRegistry.Register(
                    slug, new ZeroValueInputSource(_id, _blendShapeCount));
            }
        }

        private sealed class ZeroValueInputSource : ValueProviderInputSourceBase
        {
            public ZeroValueInputSource(string id, int blendShapeCount)
                : base(InputSourceId.Parse(id), blendShapeCount)
            {
            }

            public override bool TryWriteValues(Span<float> output)
            {
                return true;
            }
        }

        private static GameObject CreateGameObjectWithAnimator()
        {
            var go = new GameObject("FacialControllerInputSourceWeightTest");
            go.AddComponent<Animator>();
            return go;
        }

        private static GameObject CreateGameObjectWithAnimatorAndRenderer()
        {
            var go = new GameObject("FacialControllerInputSourceWeightTest");
            go.AddComponent<Animator>();
            var childObj = new GameObject("Mesh");
            childObj.transform.SetParent(go.transform);
            childObj.AddComponent<SkinnedMeshRenderer>();
            return go;
        }

        private static T GetPrivateField<T>(object target, string fieldName) where T : class
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.GetValue(target) as T;
        }
    }
}
