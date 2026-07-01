using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VContainer;
using Hidano.FacialControl.Adapters.Bone;
using Hidano.FacialControl.Adapters.DependencyInjection;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using GazeBindingConfig = Hidano.FacialControl.Adapters.ScriptableObject.GazeBindingConfig;

namespace Hidano.FacialControl.Adapters.Playable
{
    /// <summary>
    /// FacialControl のメインコンポーネント。
    /// <see cref="FacialCharacterProfileSO"/> を参照してレイヤー集約パイプラインを構築し、
    /// 確定した BlendShape 出力を <see cref="IBlendShapeOutputWriter"/> 経由で反映しながら、
    /// Expression のアクティブ化・非アクティブ化を制御する。
    /// OnEnable で自動初期化、Initialize() で手動初期化が可能。
    /// OnDisable で出力ライターと NativeArray を破棄する。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [AddComponentMenu("FacialControl/Facial Controller")]
    public class FacialController : MonoBehaviour, IBonePoseProvider, IBonePoseSource
    {
        /// <summary>
        /// 統合キャラクター SO 参照。
        /// 設定されていれば SO 名から StreamingAssets/FacialControl/{name}/profile.json を自動探索し、
        /// 存在すれば JSON、不在なら SO の Inspector データから FacialProfile を構築する。
        /// </summary>
        [Tooltip("キャラクター単位の表情・入力統合 SO。これを 1 個 D&D するだけで動作する。")]
        [SerializeField]
        private FacialCharacterProfileSO _characterSO;

        /// <summary>
        /// SkinnedMeshRenderer の手動オーバーライドリスト。
        /// 空の場合は子オブジェクトから自動検索する。
        /// </summary>
        [Tooltip("SkinnedMeshRenderer のリスト（空の場合は自動検索）")]
        [SerializeField]
        private SkinnedMeshRenderer[] _skinnedMeshRenderers;

        private Animator _animator;
        private ExpressionUseCase _expressionUseCase;
        // 系2(ExpressionTriggerInputSource)ベースの active provider。OverlayInputSource(overlay suppress)へ
        // 後期バインドで供給する（実機 InputSystem 経路の active 表情を解決するため）。
        private Layer2ActiveExpressionProvider _layer2Provider;
        private LayerUseCase _layerUseCase;
        private FacialProfile? _currentProfile;
        private string[] _blendShapeNames;
        private IBlendShapeOutputWriter _outputWriter;
        private bool _isInitialized;
        private BoneWriter _boneWriter;
        private FacialControllerLifetimeScope _childLifetimeScope;
        private IFacialOutputBus _facialOutputBus;
        private IInputSourceRegistry _inputSourceRegistry;
        private IReadOnlyList<GazeBindingConfig> _gazeConfigs = Array.Empty<GazeBindingConfig>();
        private GazeSnapshot[] _gazeSnapshotBuffer = Array.Empty<GazeSnapshot>();

        /// <summary>
        /// 初期化済みかどうか
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// 現在のプロファイル。未読み込みの場合は null。
        /// </summary>
        public FacialProfile? CurrentProfile => _currentProfile;

        /// <summary>
        /// 統合キャラクター SO の参照。
        /// </summary>
        public FacialCharacterProfileSO CharacterSO
        {
            get => _characterSO;
            set => _characterSO = value;
        }

        /// <summary>
        /// SkinnedMeshRenderer のリスト（手動オーバーライド用）
        /// </summary>
        public SkinnedMeshRenderer[] SkinnedMeshRenderers
        {
            get => _skinnedMeshRenderers;
            set => _skinnedMeshRenderers = value;
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnEnable()
        {
            // 統合 SO が設定されていれば自動初期化を試みる。
            if (_characterSO != null)
            {
                Initialize();
            }
        }

        private void OnDisable()
        {
            Cleanup();
        }

        private void LateUpdate()
        {
            if (!_isInitialized || _layerUseCase == null)
                return;

            // Aggregator パイプラインを 1 フレーム分進める。
            // sourceIdx=0 の LayerExpressionSource は ExpressionUseCase.GetActiveExpressions から駆動、
            // sourceIdx=1+ の IInputSource (input / osc 等) は
            // 各アダプタの TriggerOn/Off または WriteTick 経由で駆動される。
            _layerUseCase.UpdateWeights(Time.deltaTime);

            // Aggregator 出力（正規化済み 0..1）を出力ライター経由で BlendShape に転写する。
            var output = _layerUseCase.BlendedOutputSpan;

            _outputWriter?.Write(output);

            // BoneWriter は LateUpdate 末尾で適用する（Animator → BlendShape → BoneWriter の順）。
            PublishFacialOutput(output);

            _boneWriter?.Apply();
        }

        /// <summary>
        /// 手動初期化。<see cref="_characterSO"/> からプロファイルを読み込み、
        /// レイヤー集約パイプラインと出力ライターを構築する。SO 未設定の場合は何もしない。
        /// </summary>
        public void Initialize()
        {
            if (_characterSO == null)
            {
                return;
            }

            // Animator 取得
            _animator = GetComponent<Animator>();
            if (_animator == null)
            {
                Debug.LogWarning("Animator コンポーネントが見つかりません。初期化をスキップします。");
                return;
            }

            // SkinnedMeshRenderer を取得
            var renderers = ResolveSkinnedMeshRenderers();
            if (renderers.Length == 0)
            {
                Debug.LogWarning("SkinnedMeshRenderer が見つかりません。初期化をスキップします。");
                return;
            }

            // BlendShape 名を収集
            _blendShapeNames = CollectBlendShapeNames(renderers);

            // 統合 SO からプロファイルを構築。
            FacialProfile profile = LoadProfileFromCharacterSO(_characterSO);

            InitializeInternal(profile);
        }

        /// <summary>
        /// テスト・内部用: FacialProfile を直接指定して初期化する。
        /// JSON ファイルパスを経由せずにインメモリのプロファイルで初期化できる。
        /// </summary>
        /// <param name="profile">使用するプロファイル</param>
        public void InitializeWithProfile(FacialProfile profile)
        {
            // Animator 取得
            _animator = GetComponent<Animator>();
            if (_animator == null)
            {
                Debug.LogWarning("Animator コンポーネントが見つかりません。初期化をスキップします。");
                return;
            }

            // SkinnedMeshRenderer を取得
            var renderers = ResolveSkinnedMeshRenderers();

            // BlendShape 名を収集
            _blendShapeNames = CollectBlendShapeNames(renderers);

            InitializeInternal(profile);
        }

        private void InitializeInternal(FacialProfile profile)
        {
            // 既存のリソースがあればクリーンアップ (Registry / WeightBuffer の Dispose を含む)
            Cleanup();

            _currentProfile = profile;
            _expressionUseCase = new ExpressionUseCase(profile);
            _layer2Provider = new Layer2ActiveExpressionProvider(profile);

            var blendShapeNames = _blendShapeNames ?? Array.Empty<string>();
            var renderers = _skinnedMeshRenderers ?? Array.Empty<SkinnedMeshRenderer>();
            _outputWriter = new SkinnedMeshRendererBlendShapeWriter(renderers, blendShapeNames);

            // VContainer の per-FC child scope を無条件で build する。
            // 各 binding の OnStart は VContainer の IInitializable 経由で同期的に呼ばれ、
            // 自身の IInputSource を child scope の InputSourceRegistry に slug ベースで登録する。
            BuildAdapterBindingsChildScope(profile, blendShapeNames);

            // profile.LayerInputSources を child scope 内 InputSourceRegistry 経由で IInputSource に解決する。
            var additionalSources = ResolveLayerInputSourcesFromRegistry(profile);

            // overlay suppress の active 取得を系2(ExpressionTriggerInputSource)ベースにする。
            // OverlayInputSource は child scope build 時点（additionalSources 解決前）に
            // _layer2Provider を受け取っているため、解決済みの系2 群をここで後期バインドで流し込む。
            PopulateLayer2Provider(profile, additionalSources);

            // LayerUseCase に組み立て済み IInputSource 列を注入し、
            // 内部で LayerInputSourceRegistry / LayerInputSourceWeightBuffer / LayerInputSourceAggregator を再構築させる。
            _layerUseCase = new LayerUseCase(profile, _expressionUseCase, blendShapeNames, additionalSources);

            // BoneWriter を生成・初期化。
            SetupBoneWriter(profile);

            _isInitialized = true;
        }

        private void BuildAdapterBindingsChildScope(FacialProfile profile, string[] blendShapeNames)
        {
            IReadOnlyList<AdapterBindingBase> bindings =
                _characterSO != null && _characterSO.AdapterBindings != null
                    ? _characterSO.AdapterBindings
                    : Array.Empty<AdapterBindingBase>();

            IReadOnlyList<GazeBindingConfig> gazeConfigs =
                _characterSO != null && _characterSO.GazeConfigs != null
                    ? _characterSO.GazeConfigs
                    : Array.Empty<GazeBindingConfig>();

            _facialOutputBus = null;
            _inputSourceRegistry = null;
            _gazeConfigs = gazeConfigs ?? Array.Empty<GazeBindingConfig>();
            EnsureGazeSnapshotBufferCapacity(_gazeConfigs.Count);

            ConfigureAdapterBindingsWithGazeConfigs(bindings, gazeConfigs);

            var appScope = FacialControlAppLifetimeScope.GetOrCreate();
            if (appScope == null)
            {
                Debug.LogWarning(
                    "[FacialControl] FacialController: FacialControlAppLifetimeScope が取得できないため child scope build をスキップします。");
                return;
            }

            // _characterSO が null または AdapterBindings が空でも child scope は build する
            // （新 binding 経路一本化: 無条件 build）。
            // bindings が無い場合は空 list を渡して InputSourceRegistry のみ container に登録される。
            try
            {
                _childLifetimeScope = FacialControllerLifetimeScope.Build(
                    appScope,
                    profile,
                    blendShapeNames,
                    bindings,
                    gameObject,
                    childScopeName: name,
                    activeExpressionProvider: _layer2Provider);
                CacheChildScopeServices();
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[FacialControl] FacialController: child LifetimeScope の build に失敗しました: {ex}");
                _childLifetimeScope = null;
            }
        }

        private static void ConfigureAdapterBindingsWithGazeConfigs(
            IReadOnlyList<AdapterBindingBase> bindings,
            IReadOnlyList<GazeBindingConfig> gazeConfigs)
        {
            if (bindings == null || bindings.Count == 0)
            {
                return;
            }

            for (int i = 0; i < bindings.Count; i++)
            {
                AdapterBindingBase binding = bindings[i];
                if (binding == null)
                {
                    continue;
                }

                ConfigureAdapterBindingWithGazeConfigs(binding, gazeConfigs);
            }
        }

        private static void ConfigureAdapterBindingWithGazeConfigs(
            AdapterBindingBase binding,
            IReadOnlyList<GazeBindingConfig> gazeConfigs)
        {
            MethodInfo configure = FindGazeConfigureMethod(binding.GetType());
            if (configure == null)
            {
                return;
            }

            ParameterInfo[] parameters = configure.GetParameters();
            var args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i == parameters.Length - 1)
                {
                    args[i] = gazeConfigs;
                    continue;
                }

                if (!TryReadConfigureArgument(binding, parameters[i], out args[i]))
                {
                    return;
                }
            }

            try
            {
                configure.Invoke(binding, args);
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                Debug.LogWarning(
                    "[FacialControl] FacialController: AdapterBinding Configure gaze injection failed: "
                    + inner.Message);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[FacialControl] FacialController: AdapterBinding Configure gaze injection failed: "
                    + ex.Message);
            }
        }

        private static MethodInfo FindGazeConfigureMethod(Type bindingType)
        {
            MethodInfo[] methods = bindingType.GetMethods(BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!string.Equals(method.Name, "Configure", StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 0)
                {
                    continue;
                }

                Type lastParameterType = parameters[parameters.Length - 1].ParameterType;
                if (IsGazeConfigListType(lastParameterType))
                {
                    return method;
                }
            }

            return null;
        }

        private static bool IsGazeConfigListType(Type type)
        {
            return type != null
                && type.IsGenericType
                && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
                && type.GetGenericArguments()[0] == typeof(GazeBindingConfig);
        }

        private static bool TryReadConfigureArgument(
            AdapterBindingBase binding,
            ParameterInfo parameter,
            out object value)
        {
            string name = parameter.Name;
            switch (name)
            {
                case "asset":
                    return TryReadMemberValue(binding, "InputActionAsset", "_inputActionAsset", out value);
                case "actionMapName":
                    return TryReadMemberValue(binding, "ActionMapName", "_actionMapName", out value);
                case "expressionBindings":
                    return TryReadMemberValue(binding, null, "_expressionBindings", out value);
                default:
                    return TryReadMemberValue(
                        binding,
                        ToPascalCase(name),
                        "_" + name,
                        out value);
            }
        }

        private static bool TryReadMemberValue(
            AdapterBindingBase binding,
            string propertyName,
            string fieldName,
            out object value)
        {
            Type type = binding.GetType();
            if (!string.IsNullOrEmpty(propertyName))
            {
                PropertyInfo property = type.GetProperty(
                    propertyName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    value = property.GetValue(binding);
                    return true;
                }
            }

            if (!string.IsNullOrEmpty(fieldName))
            {
                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    value = field.GetValue(binding);
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static string ToPascalCase(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            if (name.Length == 1)
            {
                return char.ToUpperInvariant(name[0]).ToString();
            }

            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        private void CacheChildScopeServices()
        {
            if (_childLifetimeScope == null || _childLifetimeScope.Container == null)
            {
                return;
            }

            _childLifetimeScope.Container.TryResolve<IFacialOutputBus>(out _facialOutputBus);
            _childLifetimeScope.Container.TryResolve<IInputSourceRegistry>(out _inputSourceRegistry);
        }

        private void PublishFacialOutput(ReadOnlySpan<float> postBlendValues)
        {
            if (_facialOutputBus == null || !_facialOutputBus.HasObservers)
            {
                return;
            }

            ReadOnlySpan<GazeSnapshot> gazeSnapshots = BuildGazeSnapshotSpan();
            _facialOutputBus.Publish(postBlendValues, gazeSnapshots);
        }

        private ReadOnlySpan<GazeSnapshot> BuildGazeSnapshotSpan()
        {
            if (_gazeConfigs == null || _gazeConfigs.Count == 0 || _inputSourceRegistry == null)
            {
                return Array.Empty<GazeSnapshot>();
            }

            EnsureGazeSnapshotBufferCapacity(_gazeConfigs.Count);

            int count = 0;
            for (int i = 0; i < _gazeConfigs.Count; i++)
            {
                if (TryBuildGazeSnapshot(_gazeConfigs[i], out GazeSnapshot snapshot))
                {
                    _gazeSnapshotBuffer[count] = snapshot;
                    count++;
                }
            }

            return new ReadOnlySpan<GazeSnapshot>(_gazeSnapshotBuffer, 0, count);
        }

        private void EnsureGazeSnapshotBufferCapacity(int count)
        {
            if (_gazeSnapshotBuffer == null || _gazeSnapshotBuffer.Length != count)
            {
                _gazeSnapshotBuffer = count == 0
                    ? Array.Empty<GazeSnapshot>()
                    : new GazeSnapshot[count];
            }
        }

        private bool TryBuildGazeSnapshot(GazeBindingConfig config, out GazeSnapshot snapshot)
        {
            snapshot = default;
            if (config == null || string.IsNullOrEmpty(config.expressionId))
            {
                return false;
            }

            if (!GazeBindingConfigResolver.TryResolve(
                    config,
                    _inputSourceRegistry,
                    out ResolvedGazeInputSources sources))
            {
                return false;
            }

            if (!TryReadGazeInput(sources.LeftSource, out float x, out float y)
                && !TryReadGazeInput(sources.RightSource, out x, out y))
            {
                return false;
            }

            snapshot = new GazeSnapshot(config.expressionId, x, y);
            return true;
        }

        private static bool TryReadGazeInput(
            IAnalogInputSource source,
            out float x,
            out float y)
        {
            x = default;
            y = default;
            if (source == null || !source.IsValid)
            {
                return false;
            }

            bool hasValue = source.AxisCount >= 2
                ? source.TryReadVector2(out x, out y)
                : source.TryReadScalar(out x);

            if (!hasValue)
            {
                x = default;
                y = default;
                return false;
            }

            x = Mathf.Clamp(x, -1f, 1f);
            y = Mathf.Clamp(y, -1f, 1f);
            return true;
        }

        private List<(int layerIdx, IInputSource source, float weight)> ResolveLayerInputSourcesFromRegistry(
            FacialProfile profile)
        {
            var result = new List<(int layerIdx, IInputSource source, float weight)>();
            if (_childLifetimeScope == null || _childLifetimeScope.Container == null)
            {
                return result;
            }

            if (!_childLifetimeScope.Container.TryResolve<IInputSourceRegistry>(out var registry)
                || registry == null)
            {
                return result;
            }

            var layerInputSourcesSpan = profile.LayerInputSources.Span;
            int layerCount = profile.Layers.Length;
            int declarationLayers = layerInputSourcesSpan.Length;
            int upper = layerCount < declarationLayers ? layerCount : declarationLayers;

            for (int l = 0; l < upper; l++)
            {
                var declarations = layerInputSourcesSpan[l];
                if (declarations == null || declarations.Length == 0)
                {
                    continue;
                }

                for (int d = 0; d < declarations.Length; d++)
                {
                    var decl = declarations[d];
                    if (registry.TryResolve(decl.Id, out var source) && source != null)
                    {
                        result.Add((l, source, decl.Weight));
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"FacialController: inputSource id '{decl.Id ?? "<null>"}' を InputSourceRegistry で解決できないため layer {l} でスキップします。 後から登録された場合は購読経由で layer へ後付けバインドします。");
                        int capturedLayer = l;
                        registry.Subscribe(decl.Id, lateSource =>
                        {
                            if (_layerUseCase != null && lateSource != null)
                            {
                                _layerUseCase.BindLateInputSource(capturedLayer, lateSource);
                                Debug.Log($"[FacialController] late-bind inputSource id='{lateSource.Id}' を layer {capturedLayer} へ後付けバインド完了。");
                            }
                        });
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 解決済みの追加入力源のうち系2（<see cref="Hidano.FacialControl.Domain.Services.ExpressionTriggerInputSourceBase"/>）を
        /// レイヤー名付きで <see cref="_layer2Provider"/> に後期バインドする。
        /// OverlayInputSource は本メソッド実行前に空の provider を注入済みのため、
        /// ここで実体を流し込むことで実機 InputSystem 経路の active 表情が overlay suppress に反映される。
        /// </summary>
        private void PopulateLayer2Provider(
            FacialProfile profile,
            System.Collections.Generic.List<(int layerIdx, Hidano.FacialControl.Domain.Interfaces.IInputSource source, float weight)> additionalSources)
        {
            if (_layer2Provider == null || additionalSources == null)
            {
                return;
            }

            var layerSpan = profile.Layers.Span;
            var list = new System.Collections.Generic.List<(string, Hidano.FacialControl.Domain.Services.ExpressionTriggerInputSourceBase)>(additionalSources.Count);
            for (int i = 0; i < additionalSources.Count; i++)
            {
                var entry = additionalSources[i];
                if (entry.source is Hidano.FacialControl.Domain.Services.ExpressionTriggerInputSourceBase trigger
                    && (uint)entry.layerIdx < (uint)layerSpan.Length)
                {
                    list.Add((layerSpan[entry.layerIdx].Name, trigger));
                }
            }

            _layer2Provider.SetSources(list);
        }

        private void SetupBoneWriter(FacialProfile profile)
        {
            if (_animator == null)
            {
                return;
            }

            var resolver = new BoneTransformResolver(_animator.transform);
            _boneWriter = new BoneWriter(resolver, _animator);

            // basisBoneName は Humanoid Avatar から解決し、未設定 / 非 Humanoid は "Head" をデフォルトとする。
            string basisBoneName = "Head";
            if (_animator.avatar != null && _animator.avatar.isHuman)
            {
                var resolved = HumanoidBoneAutoAssigner.ResolveBasisBoneName(_animator);
                if (!string.IsNullOrEmpty(resolved))
                {
                    basisBoneName = resolved;
                }
            }

            // 初期 BoneSnapshot 列は profile に保持しない設計のため空で初期化する。
            // analog-input-binding 等が後から SetActiveBoneSnapshots で流す。
            _boneWriter.Initialize(ReadOnlyMemory<BoneSnapshot>.Empty, basisBoneName);
        }

        // ================================================================
        // 公開 API
        // ================================================================

        /// <summary>
        /// Expression をアクティブ化する。
        /// レイヤーの排他モードに基づいて処理される。
        /// </summary>
        /// <param name="expression">アクティブ化する Expression</param>
        public void Activate(Expression expression)
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("FacialController が初期化されていません。Activate は無視されます。");
                return;
            }

            _expressionUseCase.Activate(expression);
        }

        /// <summary>
        /// Expression を非アクティブ化する。
        /// </summary>
        /// <param name="expression">非アクティブ化する Expression</param>
        public void Deactivate(Expression expression)
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("FacialController が初期化されていません。Deactivate は無視されます。");
                return;
            }

            _expressionUseCase.Deactivate(expression);
        }

        /// <summary>
        /// 統合キャラクター SO を切り替える。レイヤー集約パイプラインと出力ライターを再構築する。
        /// </summary>
        /// <param name="characterSO">新しい統合キャラクター SO。</param>
        public void LoadCharacter(FacialCharacterProfileSO characterSO)
        {
            if (characterSO == null)
            {
                Debug.LogWarning("CharacterSO が null です。LoadCharacter は無視されます。");
                return;
            }

            _characterSO = characterSO;

            // Animator / Renderer を遅延解決する (SO 切替時に Initialize と同じ前段処理を踏む)。
            _animator = GetComponent<Animator>();
            if (_animator == null)
            {
                Debug.LogWarning("Animator コンポーネントが見つかりません。LoadCharacter は無視されます。");
                return;
            }

            var renderers = ResolveSkinnedMeshRenderers();
            _blendShapeNames = CollectBlendShapeNames(renderers);

            var profile = LoadProfileFromCharacterSO(characterSO);
            InitializeInternal(profile);
        }

        /// <summary>
        /// 現在のプロファイルを再読み込みする。レイヤー集約パイプラインと出力ライターを再構築する。
        /// </summary>
        public void ReloadProfile()
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("FacialController が初期化されていません。ReloadProfile は無視されます。");
                return;
            }

            if (_currentProfile.HasValue)
            {
                InitializeInternal(_currentProfile.Value);
            }
        }

        /// <summary>
        /// (layer, source) スロットの入力源ウェイトをランタイムで書込む。
        /// 任意スレッドから呼出可能で、書込は次回の <c>LayerInputSourceAggregator.Aggregate</c>
        /// 入口の <c>SwapIfDirty</c> 以降に観測される 。
        /// 値は 0〜1 に silent clamp され、範囲外 (layer, source) は警告 + no-op 。
        /// 未初期化の場合は警告ログを出して何もしない。
        /// </summary>
        /// <param name="layerIdx">レイヤーインデックス。</param>
        /// <param name="sourceIdx">入力源インデックス。<c>0</c> は内部 Expression スロットの予約枠、
        /// プロファイル宣言 (<c>inputSources</c>) で追加された入力源は登録順に <c>1, 2, ...</c> を取る。</param>
        /// <param name="weight">ウェイト値 (範囲外は silent clamp)。</param>
        public void SetInputSourceWeight(int layerIdx, int sourceIdx, float weight)
        {
            if (!_isInitialized || _layerUseCase == null)
            {
                Debug.LogWarning("FacialController が初期化されていません。SetInputSourceWeight は無視されます。");
                return;
            }

            _layerUseCase.SetInputSourceWeight(layerIdx, sourceIdx, weight);
        }

        /// <summary>
        /// 指定レイヤーの inter-layer weight を 0〜1 にクランプして書込む。
        /// Overlay 機能で「Trigger 押し量 = overlay レイヤー weight」を毎フレーム反映するために
        /// adapter binding (<see cref="Hidano.FacialControl.Adapters.AdapterBindings.InputSystem.InputSystemAdapterBinding"/> 等) から呼ばれる。
        /// 未初期化の場合は no-op。
        /// </summary>
        /// <param name="layerName">対象レイヤー名（profile.layers の name と一致）。</param>
        /// <param name="weight">レイヤー weight（範囲外は silent clamp）。</param>
        public void SetLayerWeight(string layerName, float weight)
        {
            if (!_isInitialized || _layerUseCase == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(layerName))
            {
                return;
            }

            _layerUseCase.SetLayerWeight(layerName, weight);
        }

        /// <summary>
        /// 入力源ウェイトのバルク書込スコープを開始する。
        /// 返された <see cref="LayerInputSourceWeightBuffer.BulkScope"/> の
        /// <c>SetWeight</c> で書いた値はスコープの <c>Dispose</c> 時に一括 flush され、
        /// 次回 Aggregate で atomic に観測される 。
        /// 戻り値は <see cref="IDisposable"/> として <c>using</c> 文で利用可能。
        /// 未初期化の場合は no-op となるスコープを返す。
        /// </summary>
        public LayerInputSourceWeightBuffer.BulkScope BeginInputSourceWeightBatch()
        {
            if (!_isInitialized || _layerUseCase == null)
            {
                Debug.LogWarning("FacialController が初期化されていません。BeginInputSourceWeightBatch は no-op スコープを返します。");
                return default;
            }

            return _layerUseCase.BeginInputSourceWeightBatch();
        }

        /// <summary>
        /// 直近 Aggregate で観測された (layer, source) ウェイトの診断スナップショットを返す
        /// 。Editor の読取専用ビュー向け。
        /// 未初期化の場合は空リストを返す。
        /// </summary>
        public IReadOnlyList<LayerSourceWeightEntry> GetInputSourceWeightsSnapshot()
        {
            if (!_isInitialized || _layerUseCase == null)
            {
                return Array.Empty<LayerSourceWeightEntry>();
            }
            return _layerUseCase.GetInputSourceWeightsSnapshot();
        }

        /// <summary>
        /// プロファイルの <c>inputSources</c> 宣言から生成された Expression トリガー型
        /// 入力源 (<c>input</c> など) を id で検索する。
        /// Samples のデモ HUD や Editor ツールから特定アダプタを掴んで
        /// <see cref="ExpressionTriggerInputSourceBase.TriggerOn"/> /
        /// <see cref="ExpressionTriggerInputSourceBase.TriggerOff"/> を直接呼びたい場合に利用する。
        /// </summary>
        /// <param name="id">検索対象の入力源 id。</param>
        /// <param name="source">見つかったアダプタ、見つからない場合は null。</param>
        /// <returns>id が一致する Expression トリガー型ソースが登録されていれば true。</returns>
        public bool TryGetExpressionTriggerSourceById(string id, out ExpressionTriggerInputSourceBase source)
        {
            source = null;
            if (!_isInitialized || _layerUseCase == null)
            {
                return false;
            }
            return _layerUseCase.TryGetExpressionTriggerSourceById(id, out source);
        }

        /// <summary>
        /// 外部 (analog-input-binding 等) から現在 active な <see cref="BoneSnapshot"/> 列を差替える
        /// 。次フレームの <see cref="BoneWriter.Apply"/> から有効。
        /// </summary>
        public void SetActiveBoneSnapshots(ReadOnlyMemory<BoneSnapshot> snapshots)
        {
            if (_boneWriter == null)
            {
                Debug.LogWarning("FacialController が初期化されていません。SetActiveBoneSnapshots は無視されます。");
                return;
            }

            _boneWriter.SetActiveBoneSnapshots(snapshots);
        }

        /// <summary>
        /// 現在 active な <see cref="BoneSnapshot"/> 列を返す 。
        /// </summary>
        public ReadOnlyMemory<BoneSnapshot> GetActiveBoneSnapshots()
        {
            if (_boneWriter == null)
            {
                return default;
            }

            return _boneWriter.GetActiveBoneSnapshots();
        }

        /// <summary>
        /// 現在アクティブな Expression のリストを返す。
        /// </summary>
        /// <returns>アクティブな Expression のリスト</returns>
        public List<Expression> GetActiveExpressions()
        {
            if (_expressionUseCase == null)
            {
                return new List<Expression>();
            }

            return _expressionUseCase.GetActiveExpressions();
        }

        // ================================================================
        // 内部メソッド
        // ================================================================

        private SkinnedMeshRenderer[] ResolveSkinnedMeshRenderers()
        {
            // 手動オーバーライドが設定されていればそれを使用
            if (_skinnedMeshRenderers != null && _skinnedMeshRenderers.Length > 0)
            {
                return _skinnedMeshRenderers;
            }

            // 子オブジェクトから自動検索し、フィールドに保持する
            _skinnedMeshRenderers = GetComponentsInChildren<SkinnedMeshRenderer>();
            return _skinnedMeshRenderers;
        }

        private string[] CollectBlendShapeNames(SkinnedMeshRenderer[] renderers)
        {
            var names = new List<string>();
            var nameSet = new HashSet<string>();

            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer.sharedMesh == null)
                    continue;

                var mesh = renderer.sharedMesh;
                int blendShapeCount = mesh.blendShapeCount;

                for (int j = 0; j < blendShapeCount; j++)
                {
                    string bsName = mesh.GetBlendShapeName(j);

                    if (nameSet.Add(bsName))
                    {
                        names.Add(bsName);
                    }
                }
            }

            return names.ToArray();
        }
        private void Cleanup()
        {
            // child scope を build していた場合は最初に Dispose し、binding.Dispose を完了させる。
            // host 群の Dispose を完了させてから既存 cleanup を行う。
            if (_childLifetimeScope != null)
            {
                _childLifetimeScope.Dispose();
                _childLifetimeScope = null;
            }

            _facialOutputBus = null;
            _inputSourceRegistry = null;
            _gazeConfigs = Array.Empty<GazeBindingConfig>();
            _gazeSnapshotBuffer = Array.Empty<GazeSnapshot>();

            // プロファイル再ロード時は Registry / WeightBuffer を Dispose して再構築する。
            if (_layerUseCase != null)
            {
                _layerUseCase.Dispose();
                _layerUseCase = null;
            }

            if (_outputWriter != null)
            {
                _outputWriter.Dispose();
                _outputWriter = null;
            }

            // BoneWriter は書込中だった bone の localRotation を初回書込み直前の値に戻してから Dispose する。
            if (_boneWriter != null)
            {
                _boneWriter.RestoreInitialRotations();
                _boneWriter.Dispose();
                _boneWriter = null;
            }

            _expressionUseCase = null;
            _isInitialized = false;
        }

        /// <summary>
        /// 新統合 SO からプロファイルを構築する。SO の <see cref="FacialCharacterProfileSO.LoadProfile"/>
        /// がパス自動探索 + JSON 読込 + フォールバックを担当する。
        /// </summary>
        private static FacialProfile LoadProfileFromCharacterSO(FacialCharacterProfileSO so)
        {
            if (so == null)
            {
                return CreateDefaultProfile();
            }

            try
            {
                return so.LoadProfile();
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"FacialController: Character SO '{so.name}' からのプロファイル構築に失敗しました: {ex.Message}。デフォルトプロファイルで初期化します。");
                return CreateDefaultProfile();
            }
        }

        private static FacialProfile CreateDefaultProfile()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend),
                new LayerDefinition("eye", 2, ExclusionMode.LastWins)
            };
            return new FacialProfile("1.0.0", layers);
        }
    }
}
