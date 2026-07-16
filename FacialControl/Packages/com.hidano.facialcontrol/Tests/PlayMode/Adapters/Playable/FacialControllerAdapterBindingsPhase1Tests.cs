using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.AdapterBindings.InputSystem;
using Hidano.FacialControl.Adapters.DependencyInjection;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using VContainer;
using GazeBindingConfig = Hidano.FacialControl.Adapters.ScriptableObject.GazeBindingConfig;

namespace Hidano.FacialControl.Tests.PlayMode.Adapters.Playable
{
    /// <summary>
    /// <see cref="FacialController"/> + <c>_adapterBindings</c> 経路 PlayMode テスト。
    /// 「無条件 child scope build → binding lifecycle が VContainer 経由で駆動される」契約を検証する。
    /// </summary>
    [TestFixture]
    public class FacialControllerAdapterBindingsPhase1Tests
    {
        private GameObject _controllerGameObject;
        private Mesh _mesh;

        [TearDown]
        public void TearDown()
        {
            if (_mesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_mesh);
                _mesh = null;
            }

            if (_controllerGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_controllerGameObject);
                _controllerGameObject = null;
            }
        }

        // ---------------------------------------------------------------
        // _adapterBindings に登録された binding は child scope build → VContainer 経由で
        // OnStart / OnLateTick / Dispose が呼ばれる。
        // ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator Initialize_AdapterBindingsNonEmpty_VContainerInvokesBindingLifecycle()
        {
            _controllerGameObject = CreateControllerHost();
            var controller = _controllerGameObject.AddComponent<FacialController>();
            var so = ScriptableObject.CreateInstance<TestablePhase1ProfileSO>();
            var binding = new TrackingAdapterBinding { Slug = "tracking-1" };
            so.WritableAdapterBindings.Add(binding);

            try
            {
                controller.CharacterSO = so;
                controller.Initialize();

                Assert.That(controller.IsInitialized, Is.True,
                    "Initialize は AdapterBindings 経路でも完了するはず。");
                Assert.That(binding.OnStartCount, Is.EqualTo(1),
                    "child scope build 後に binding.OnStart が VContainer の IInitializable 経由で 1 回呼ばれるはず。");

                // VContainer の ILateTickable は Unity の LateUpdate と同じ PlayerLoop bucket。
                // 1〜2 frame 進めて少なくとも 1 回 dispatch されたことを観測する。
                yield return null;
                yield return null;

                Assert.That(binding.OnLateTickCount, Is.GreaterThanOrEqualTo(1),
                    "binding.OnLateTick は VContainer の ILateTickable として PlayerLoop.LateUpdate 経由で駆動されるはず。");

                // OnDisable → Cleanup → child scope.Dispose() → binding.Dispose() の連鎖を検証。
                _controllerGameObject.SetActive(false);

                Assert.That(binding.DisposeCount, Is.EqualTo(1),
                    "Cleanup で child scope を Dispose() し、binding.Dispose が VContainer の IDisposable 経由で 1 回呼ばれるはず。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(so);
            }
        }

        // ---------------------------------------------------------------
        // _adapterBindings が空でも child scope build は走る（無条件 build）。
        // 旧 Extension 経路は完全削除済みのため、空 list でも Initialize は完了する。
        // ---------------------------------------------------------------

        [Test]
        public void Initialize_AdapterBindingsEmpty_BuildsChildScopeUnconditionally()
        {
            _controllerGameObject = CreateControllerHost();
            var controller = _controllerGameObject.AddComponent<FacialController>();
            var so = ScriptableObject.CreateInstance<TestablePhase1ProfileSO>();
            try
            {
                Assert.That(so.AdapterBindings.Count, Is.EqualTo(0),
                    "Test 前提: AdapterBindings は空 list で開始するはず。");

                controller.CharacterSO = so;
                controller.Initialize();

                Assert.That(controller.IsInitialized, Is.True,
                    "AdapterBindings が空でも child scope build は走り Initialize は完了するはず。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(so);
            }
        }

        [UnityTest]
        public IEnumerator Initialize_InputSystemBinding_InjectsSORootGazeConfigsByReference()
        {
            _controllerGameObject = CreateControllerHost();
            var controller = _controllerGameObject.AddComponent<FacialController>();
            var so = ScriptableObject.CreateInstance<TestablePhase1ProfileSO>();
            var binding = new InputSystemAdapterBinding { Slug = "input-system-gaze-injection" };
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.AddActionMap("Expression");
            binding.InputActionAsset = asset;
            so.WritableAdapterBindings.Add(binding);
            so.WritableGazeConfigs.Add(new GazeBindingConfig { expressionId = "expr-gaze" });

            try
            {
                controller.CharacterSO = so;
                controller.Initialize();

                object injected = ReadInjectedGazeConfigs(binding);
                Assert.That(injected, Is.SameAs(so.GazeConfigs),
                    "runtime build 経路で InputSystemAdapterBinding.Configure に SO ルート GazeConfigs が参照同値で注入されるべき。");

                _controllerGameObject.SetActive(false);
                yield return null;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
                UnityEngine.Object.DestroyImmediate(so);
            }
        }

        [UnityTest]
        public IEnumerator LateUpdate_OutputObserverSubscribedViaChildScope_ReceivesPullPublish()
        {
            _controllerGameObject = CreateControllerHost();
            var controller = _controllerGameObject.AddComponent<FacialController>();
            var so = ScriptableObject.CreateInstance<TestablePhase1ProfileSO>();
            var binding = new OutputObservingAdapterBinding { Slug = "output-observer" };
            so.WritableAdapterBindings.Add(binding);
            so.WritableGazeConfigs.Add(new GazeBindingConfig { expressionId = "unresolved-gaze" });

            try
            {
                controller.CharacterSO = so;
                controller.Initialize();

                Assert.That(controller.IsInitialized, Is.True);
                Assert.That(binding.CapturedBus, Is.Not.Null);

                var scope = ReadChildLifetimeScope(controller);
                Assert.That(scope, Is.Not.Null);
                Assert.That(scope.Container.TryResolve<IFacialOutputBus>(out var resolvedBus), Is.True);
                Assert.That(resolvedBus, Is.SameAs(binding.CapturedBus));

                yield return null;

                Assert.That(binding.PublishCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(binding.LastBlendShapeCount, Is.EqualTo(0));
                Assert.That(binding.LastGazeCount, Is.EqualTo(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(so);
            }
        }

        // ---------------------------------------------------------------
        // gaze の目ボーン適用は core の FacialController に集約されている。
        // 各 binding が registry に登録した gaze 入力源を GazeBindingConfigResolver で解決し、
        // LateUpdate で GazeBonePoseProvider が目ボーンの localRotation を書く。
        // ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator LateUpdate_RegistryGazeSource_RotatesEyeBoneViaCentralizedProvider()
        {
            _controllerGameObject = CreateControllerHost();

            // 目ボーンを host 配下に用意（bone path は単純名で階層全体を再帰探索して解決される）。
            var eyeBone = new GameObject("LeftEye");
            eyeBone.transform.SetParent(_controllerGameObject.transform, worldPositionStays: false);
            eyeBone.transform.localRotation = Quaternion.identity;

            var controller = _controllerGameObject.AddComponent<FacialController>();
            var so = ScriptableObject.CreateInstance<TestablePhase1ProfileSO>();

            // gaze 入力源(fake-gaze:eye-look)を registry に登録するだけの binding。
            var binding = new GazeSourceRegisteringBinding
            {
                Slug = "fake-gaze",
                ExpressionId = "eye-look",
                X = 1f,
                Y = 0f,
            };
            so.WritableAdapterBindings.Add(binding);
            so.WritableGazeConfigs.Add(new GazeBindingConfig
            {
                expressionId = "eye-look",
                leftEyeBonePath = "LeftEye",
            });

            try
            {
                controller.CharacterSO = so;
                controller.Initialize();

                Assert.That(controller.IsInitialized, Is.True);

                // Initialize 段階では provider を構築するのみで Apply していないため目ボーンは初期姿勢のまま。
                Quaternion before = eyeBone.transform.localRotation;
                Assert.That(Quaternion.Angle(before, Quaternion.identity), Is.LessThan(0.01f),
                    "Initialize 直後は目ボーンはまだ回っていないはず。");

                // LateUpdate で GazeBonePoseProvider.Apply が走り目ボーンが回る。
                yield return null;

                Quaternion after = eyeBone.transform.localRotation;
                Assert.That(Quaternion.Angle(before, after), Is.GreaterThan(1f),
                    "registry の gaze 入力源から FacialController が目ボーンの localRotation を回すべき（入力方式非依存の集約適用）。");
            }
            finally
            {
                _controllerGameObject.SetActive(false);
                UnityEngine.Object.DestroyImmediate(so);
            }
        }

        [UnityTest]
        public IEnumerator ReplaceAndUnregister_DeclaredLayerInputSource_PropagatesToBlendOutput()
        {
            _controllerGameObject = CreateControllerHost();
            AssignBlendShapeMesh(_controllerGameObject, "smile");

            var controller = _controllerGameObject.AddComponent<FacialController>();
            var so = ScriptableObject.CreateInstance<DeclaredInputProfileSO>();
            so.LayerInputSourceId = "rebind-source";
            so.BlendShapeName = "smile";

            var binding = new ValueSourceRegisteringBinding
            {
                Slug = "rebind-source",
                Source = new MutableValueSource("rebind-source", 1, 0, 0.2f)
            };
            so.WritableAdapterBindings.Add(binding);

            try
            {
                controller.CharacterSO = so;
                controller.Initialize();

                yield return null;

                Assert.That(ReadCurrentOutput(controller)[0], Is.EqualTo(0.2f).Within(0.001f));

                controller.InputSourceRegistry.Replace(
                    AdapterSlug.Parse("rebind-source"),
                    new MutableValueSource("rebind-source", 1, 0, 0.8f));

                yield return null;

                Assert.That(ReadCurrentOutput(controller)[0], Is.EqualTo(0.8f).Within(0.001f));

                controller.InputSourceRegistry.Unregister(AdapterSlug.Parse("rebind-source"));

                yield return null;

                Assert.That(ReadCurrentOutput(controller)[0], Is.EqualTo(0f).Within(0.001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(so);
            }
        }

        [UnityTest]
        public IEnumerator Replace_TriggerSource_RewiresObservationBusToNewInstance()
        {
            _controllerGameObject = CreateControllerHost();

            var controller = _controllerGameObject.AddComponent<FacialController>();
            var so = ScriptableObject.CreateInstance<DeclaredInputProfileSO>();
            so.LayerInputSourceId = "trigger-source";
            so.IncludeExpression = true;
            so.ExpressionId = "expr-smile";

            var binding = new TriggerSourceRegisteringBinding
            {
                Slug = "trigger-source",
                ExpressionId = "expr-smile"
            };
            so.WritableAdapterBindings.Add(binding);

            try
            {
                controller.CharacterSO = so;
                controller.Initialize();

                var observer = new RecordingInputObserver();
                controller.InputObservationBus.Subscribe(observer);

                Assert.That(controller.TryGetExpressionTriggerSourceById("trigger-source", out var initial), Is.True);
                initial.TriggerOn("expr-smile");

                Assert.That(observer.TriggerOnCount, Is.EqualTo(1));

                var replacement = new TestObservationTriggerSource(
                    "trigger-source",
                    controller.CurrentProfile.GetValueOrDefault(),
                    Array.Empty<string>());
                controller.InputSourceRegistry.Replace(AdapterSlug.Parse("trigger-source"), replacement);

                yield return null;

                replacement.TriggerOn("expr-smile");
                Assert.That(observer.TriggerOnCount, Is.EqualTo(2));

                initial.TriggerOn("expr-smile");
                Assert.That(observer.TriggerOnCount, Is.EqualTo(2));

                controller.InputObservationBus.Unsubscribe(observer);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(so);
            }
        }

        [UnityTest]
        public IEnumerator ReplaceAndUnregister_GazeSource_RebuildsProvider()
        {
            _controllerGameObject = CreateControllerHost();

            var eyeBone = new GameObject("LeftEye");
            eyeBone.transform.SetParent(_controllerGameObject.transform, worldPositionStays: false);
            eyeBone.transform.localRotation = Quaternion.identity;

            var controller = _controllerGameObject.AddComponent<FacialController>();
            var so = ScriptableObject.CreateInstance<DeclaredInputProfileSO>();
            so.WritableGazeConfigs.Add(new GazeBindingConfig
            {
                expressionId = "look",
                useDistinctLeftRight = true,
                sourceIdLeft = "look-left",
                sourceIdRight = "look-right",
                leftEyeBonePath = "LeftEye",
            });

            var binding = new DistinctGazeSourceRegisteringBinding
            {
                Slug = "gaze-rebind",
                LeftSource = new MutableGazeAnalogSource("look-left", 0.2f, 0f),
                RightSource = new MutableGazeAnalogSource("look-right", 0.2f, 0f),
            };
            so.WritableAdapterBindings.Add(binding);

            try
            {
                controller.CharacterSO = so;
                controller.Initialize();

                yield return null;

                Quaternion initialAppliedRotation = eyeBone.transform.localRotation;
                Assert.That(Quaternion.Angle(Quaternion.identity, initialAppliedRotation), Is.GreaterThan(0.1f));

                controller.InputSourceRegistry.Replace(
                    AdapterSlug.Parse("look-left"),
                    new MutableGazeAnalogSource("look-left", -1f, 0f));

                yield return null;

                Quaternion replacedRotation = eyeBone.transform.localRotation;
                Assert.That(Quaternion.Angle(initialAppliedRotation, replacedRotation), Is.GreaterThan(1f));

                controller.InputSourceRegistry.Unregister(AdapterSlug.Parse("look-left"));
                controller.InputSourceRegistry.Unregister(AdapterSlug.Parse("look-right"));

                yield return null;

                Assert.That(Quaternion.Angle(Quaternion.identity, eyeBone.transform.localRotation), Is.LessThan(0.1f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(so);
            }
        }

        // ---------------------------------------------------------------
        // Helpers / Mocks
        // ---------------------------------------------------------------

        private static GameObject CreateControllerHost()
        {
            var go = new GameObject("FacialControllerAdapterBindingsPhase1TestsHost");
            go.AddComponent<Animator>();
            // ResolveSkinnedMeshRenderers が空配列を返すと Initialize が早期 return するため、
            // sharedMesh を持たない Renderer を 1 件だけぶら下げて Initialize を成立させる。
            var meshGo = new GameObject("Mesh");
            meshGo.transform.SetParent(go.transform);
            meshGo.AddComponent<SkinnedMeshRenderer>();
            return go;
        }

        private void AssignBlendShapeMesh(GameObject host, string blendShapeName)
        {
            SkinnedMeshRenderer renderer = host.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.That(renderer, Is.Not.Null);

            _mesh = new Mesh();
            _mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            _mesh.triangles = new[] { 0, 1, 2 };
            _mesh.AddBlendShapeFrame(blendShapeName, 100f, new Vector3[3], null, null);
            renderer.sharedMesh = _mesh;
        }

        private static float[] ReadCurrentOutput(FacialController controller)
        {
            FieldInfo field = typeof(FacialController).GetField(
                "_layerUseCase",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);

            LayerUseCase useCase = field.GetValue(controller) as LayerUseCase;
            Assert.That(useCase, Is.Not.Null);
            return useCase.GetBlendedOutput();
        }

        /// <summary>
        /// task 4.2 で追加された <c>protected List&lt;AdapterBindingBase&gt; _adapterBindings</c> field に
        /// PlayMode テスト側から直接書き込めるよう公開する concrete <see cref="FacialCharacterProfileSO"/>。
        /// <see cref="LoadProfile"/> は StreamingAssets 探索を経由せず最小の <see cref="FacialProfile"/> を返す。
        /// </summary>
        public sealed class TestablePhase1ProfileSO : FacialCharacterProfileSO
        {
            public List<AdapterBindingBase> WritableAdapterBindings => _adapterBindings;
            public List<GazeBindingConfig> WritableGazeConfigs => _gazeConfigs;

            public override FacialProfile LoadProfile()
            {
                var layers = new[]
                {
                    new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
                };
                return new FacialProfile("2.0", layers);
            }
        }

        public sealed class DeclaredInputProfileSO : FacialCharacterProfileSO
        {
            public string LayerInputSourceId;
            public string BlendShapeName = "smile";
            public bool IncludeExpression;
            public string ExpressionId = "expr";

            public List<AdapterBindingBase> WritableAdapterBindings => _adapterBindings;
            public List<GazeBindingConfig> WritableGazeConfigs => _gazeConfigs;

            public override FacialProfile LoadProfile()
            {
                var layers = new[]
                {
                    new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
                };

                InputSourceDeclaration[][] layerInputSources = string.IsNullOrEmpty(LayerInputSourceId)
                    ? null
                    : new[]
                    {
                        new[]
                        {
                            new InputSourceDeclaration(LayerInputSourceId, 1f, null)
                        }
                    };

                Expression[] expressions = null;
                if (IncludeExpression)
                {
                    expressions = new[]
                    {
                        new Expression(
                            ExpressionId,
                            ExpressionId,
                            "emotion",
                            0f,
                            TransitionCurve.Linear,
                            new[]
                            {
                                new BlendShapeMapping(BlendShapeName, 1f)
                            })
                    };
                }

                return new FacialProfile("2.0", layers, expressions, null, layerInputSources);
            }
        }

        /// <summary>
        /// VContainer 経由の lifecycle 駆動を観測するための <see cref="AdapterBindingBase"/> 派生 Mock。
        /// 同 instance に対する <see cref="OnStart"/> / <see cref="OnLateTick"/> / <see cref="Dispose"/>
        /// の呼出回数を <c>NonSerialized</c> field で集計する。
        /// </summary>
        private static object ReadInjectedGazeConfigs(InputSystemAdapterBinding binding)
        {
            var field = typeof(InputSystemAdapterBinding).GetField(
                "_injectedGazeConfigs",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null,
                "InputSystemAdapterBinding._injectedGazeConfigs は runtime 注入ハンドルとして存在するべき。");
            return field.GetValue(binding);
        }

        private static FacialControllerLifetimeScope ReadChildLifetimeScope(FacialController controller)
        {
            FieldInfo field = typeof(FacialController).GetField(
                "_childLifetimeScope",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (FacialControllerLifetimeScope)field.GetValue(controller);
        }

        [Serializable]
        public sealed class TrackingAdapterBinding : AdapterBindingBase
        {
            [NonSerialized] public int OnStartCount;
            [NonSerialized] public int OnLateTickCount;
            [NonSerialized] public int DisposeCount;

            public override void OnStart(in AdapterBuildContext ctx)
            {
                OnStartCount++;
            }

            public override void OnLateTick(float deltaTime)
            {
                OnLateTickCount++;
            }

            public override void Dispose()
            {
                DisposeCount++;
            }
        }

        [Serializable]
        private sealed class ValueSourceRegisteringBinding : AdapterBindingBase
        {
            [NonSerialized] public MutableValueSource Source;

            public override void OnStart(in AdapterBuildContext ctx)
            {
                ctx.InputSourceRegistry.Register(AdapterSlug.Parse(Slug), Source);
            }
        }

        [Serializable]
        private sealed class TriggerSourceRegisteringBinding : AdapterBindingBase
        {
            [NonSerialized] public string ExpressionId;
            [NonSerialized] public TestObservationTriggerSource RegisteredSource;

            public override void OnStart(in AdapterBuildContext ctx)
            {
                RegisteredSource = new TestObservationTriggerSource(
                    Slug,
                    ctx.Profile,
                    ctx.BlendShapeNames);
                ctx.InputSourceRegistry.Register(AdapterSlug.Parse(Slug), RegisteredSource);
            }
        }

        [Serializable]
        private sealed class OutputObservingAdapterBinding : AdapterBindingBase, IFacialOutputObserver
        {
            [NonSerialized] public IFacialOutputBus CapturedBus;
            [NonSerialized] public int PublishCount;
            [NonSerialized] public int LastBlendShapeCount;
            [NonSerialized] public int LastGazeCount;

            public override void OnStart(in AdapterBuildContext ctx)
            {
                CapturedBus = ctx.FacialOutputBus;
                CapturedBus.Subscribe(this);
            }

            public void OnFacialOutputPublished(
                ReadOnlySpan<float> postBlendValues,
                ReadOnlySpan<GazeSnapshot> gazeSnapshots)
            {
                PublishCount++;
                LastBlendShapeCount = postBlendValues.Length;
                LastGazeCount = gazeSnapshots.Length;
            }

            public override void Dispose()
            {
                CapturedBus?.Unsubscribe(this);
                CapturedBus = null;
            }
        }

        /// <summary>
        /// gaze 入力源(<c>&lt;slug&gt;:&lt;expressionId&gt;</c>)を registry に登録するだけの最小 binding。
        /// OSC / InputSystem / iFacialMocap 各受信 binding が gaze source を登録する挙動を模す。
        /// 目ボーン適用は FacialController 側に集約されているため本 binding は tick で何もしない。
        /// </summary>
        [Serializable]
        private sealed class GazeSourceRegisteringBinding : AdapterBindingBase
        {
            [NonSerialized] public string ExpressionId;
            [NonSerialized] public float X;
            [NonSerialized] public float Y;

            public override void OnStart(in AdapterBuildContext ctx)
            {
                AdapterSlug slug = AdapterSlug.Parse(Slug);
                var source = new FakeGazeAnalogSource(Slug + ":" + ExpressionId, X, Y);
                ctx.InputSourceRegistry.Register(slug, ExpressionId, source);
            }

            public override void OnLateTick(float deltaTime)
            {
            }

            public override void Dispose()
            {
            }
        }

        [Serializable]
        private sealed class DistinctGazeSourceRegisteringBinding : AdapterBindingBase
        {
            [NonSerialized] public MutableGazeAnalogSource LeftSource;
            [NonSerialized] public MutableGazeAnalogSource RightSource;

            public override void OnStart(in AdapterBuildContext ctx)
            {
                ctx.InputSourceRegistry.Register(AdapterSlug.Parse(LeftSource.Id), LeftSource);
                ctx.InputSourceRegistry.Register(AdapterSlug.Parse(RightSource.Id), RightSource);
            }
        }

        /// <summary>
        /// 固定 Vector2 を返すアナログ入力源。
        /// (EditMode の <c>GazeBindingConfigResolverTests.FakeGazeSource</c> と同型)。
        /// </summary>
        private sealed class FakeGazeAnalogSource : IInputSource, IAnalogInputSource
        {
            private readonly float _x;
            private readonly float _y;

            public FakeGazeAnalogSource(string id, float x, float y)
            {
                Id = id;
                _x = x;
                _y = y;
                ContributeMask = new BitArray(0);
            }

            public string Id { get; }
            public InputSourceType Type => InputSourceType.ValueProvider;
            public int BlendShapeCount => 0;
            public BitArray ContributeMask { get; }
            public bool IsValid => true;
            public int AxisCount => 2;

            public void Tick(float deltaTime)
            {
            }

            public bool TryWriteValues(Span<float> output) => false;

            public bool TryReadScalar(out float value)
            {
                value = _x;
                return true;
            }

            public bool TryReadVector2(out float x, out float y)
            {
                x = _x;
                y = _y;
                return true;
            }

            public bool TryReadAxes(Span<float> output)
            {
                if (output.Length > 0)
                {
                    output[0] = _x;
                }
                if (output.Length > 1)
                {
                    output[1] = _y;
                }

                return true;
            }
        }

        private sealed class MutableGazeAnalogSource : IInputSource, IAnalogInputSource
        {
            private readonly float _x;
            private readonly float _y;

            public MutableGazeAnalogSource(string id, float x, float y)
            {
                Id = id;
                _x = x;
                _y = y;
                ContributeMask = new BitArray(0);
            }

            public string Id { get; }
            public InputSourceType Type => InputSourceType.ValueProvider;
            public int BlendShapeCount => 0;
            public BitArray ContributeMask { get; }
            public bool IsValid => true;
            public int AxisCount => 2;

            public void Tick(float deltaTime)
            {
            }

            public bool TryWriteValues(Span<float> output) => false;

            public bool TryReadScalar(out float value)
            {
                value = _x;
                return true;
            }

            public bool TryReadVector2(out float x, out float y)
            {
                x = _x;
                y = _y;
                return true;
            }

            public bool TryReadAxes(Span<float> output)
            {
                if (output.Length > 0)
                {
                    output[0] = _x;
                }

                if (output.Length > 1)
                {
                    output[1] = _y;
                }

                return true;
            }
        }

        private sealed class MutableValueSource : ValueProviderInputSourceBase
        {
            private readonly int _writeIndex;
            private readonly float _value;

            public MutableValueSource(string id, int blendShapeCount, int writeIndex, float value)
                : base(InputSourceId.Parse(id), blendShapeCount)
            {
                _writeIndex = writeIndex;
                _value = value;
            }

            public override bool TryWriteValues(Span<float> output)
            {
                if ((uint)_writeIndex < (uint)output.Length)
                {
                    output[_writeIndex] = _value;
                }

                return true;
            }
        }

        private sealed class TestObservationTriggerSource : ExpressionTriggerInputSourceBase
        {
            public TestObservationTriggerSource(
                string id,
                FacialProfile profile,
                IReadOnlyList<string> blendShapeNames)
                : base(
                    InputSourceId.Parse(id),
                    blendShapeNames?.Count ?? 0,
                    maxStackDepth: 4,
                    exclusionMode: ExclusionMode.LastWins,
                    blendShapeNames ?? Array.Empty<string>(),
                    profile)
            {
            }
        }

        private sealed class RecordingInputObserver : IFacialInputObserver
        {
            public int TriggerOnCount { get; private set; }

            public void OnTriggerOn(string sourceId, string expressionId)
            {
                TriggerOnCount++;
            }

            public void OnTriggerOff(string sourceId, string expressionId)
            {
            }

            public void OnAnalogSample(string sourceId, ReadOnlySpan<float> axes)
            {
            }
        }
    }
}
