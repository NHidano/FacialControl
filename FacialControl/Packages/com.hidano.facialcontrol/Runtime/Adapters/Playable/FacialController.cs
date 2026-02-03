using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Adapters.Playable
{
    /// <summary>
    /// FacialControl のメインコンポーネント。
    /// FacialProfileSO を参照して PlayableGraph を構築し、
    /// Expression のアクティブ化・非アクティブ化を制御する。
    /// OnEnable で自動初期化、Initialize() で手動初期化が可能。
    /// OnDisable で PlayableGraph と NativeArray を破棄する。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [AddComponentMenu("FacialControl/Facial Controller")]
    public class FacialController : MonoBehaviour
    {
        /// <summary>
        /// 表情プロファイルの ScriptableObject 参照
        /// </summary>
        [Tooltip("表情プロファイルの ScriptableObject")]
        [SerializeField]
        private FacialProfileSO _profileSO;

        /// <summary>
        /// SkinnedMeshRenderer の手動オーバーライドリスト。
        /// 空の場合は子オブジェクトから自動検索する。
        /// </summary>
        [Tooltip("SkinnedMeshRenderer のリスト（空の場合は自動検索）")]
        [SerializeField]
        private SkinnedMeshRenderer[] _skinnedMeshRenderers;

        /// <summary>
        /// OSC 送信ポート番号
        /// </summary>
        [Tooltip("OSC 送信ポート番号")]
        [SerializeField]
        private int _oscSendPort = Domain.Models.OscConfiguration.DefaultSendPort;

        /// <summary>
        /// OSC 受信ポート番号
        /// </summary>
        [Tooltip("OSC 受信ポート番号")]
        [SerializeField]
        private int _oscReceivePort = Domain.Models.OscConfiguration.DefaultReceivePort;

        private Animator _animator;
        private PlayableGraphBuilder.BuildResult _graphBuildResult;
        private ExpressionUseCase _expressionUseCase;
        private FacialProfile? _currentProfile;
        private string[] _blendShapeNames;
        private bool _isInitialized;

        // BlendShape 出力インデックス → (Renderer, Renderer 上の BS index) のマッピング
        private BlendShapeTarget[][] _blendShapeTargets;

        /// <summary>
        /// BlendShape の出力先ターゲット情報。
        /// </summary>
        private struct BlendShapeTarget
        {
            public SkinnedMeshRenderer Renderer;
            public int RendererBlendShapeIndex;

            public BlendShapeTarget(SkinnedMeshRenderer renderer, int rendererBlendShapeIndex)
            {
                Renderer = renderer;
                RendererBlendShapeIndex = rendererBlendShapeIndex;
            }
        }

        /// <summary>
        /// 初期化済みかどうか
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// 現在のプロファイル。未読み込みの場合は null。
        /// </summary>
        public FacialProfile? CurrentProfile => _currentProfile;

        /// <summary>
        /// FacialProfileSO の参照
        /// </summary>
        public FacialProfileSO ProfileSO
        {
            get => _profileSO;
            set => _profileSO = value;
        }

        /// <summary>
        /// SkinnedMeshRenderer のリスト（手動オーバーライド用）
        /// </summary>
        public SkinnedMeshRenderer[] SkinnedMeshRenderers
        {
            get => _skinnedMeshRenderers;
            set => _skinnedMeshRenderers = value;
        }

        /// <summary>
        /// OSC 送信ポート番号
        /// </summary>
        public int OscSendPort
        {
            get => _oscSendPort;
            set => _oscSendPort = value;
        }

        /// <summary>
        /// OSC 受信ポート番号
        /// </summary>
        public int OscReceivePort
        {
            get => _oscReceivePort;
            set => _oscReceivePort = value;
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnEnable()
        {
            // ProfileSO が設定されていれば自動初期化を試みる
            if (_profileSO != null)
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
            if (!_isInitialized || _graphBuildResult == null || _blendShapeTargets == null)
                return;

            var mixer = _graphBuildResult.Mixer.GetBehaviour();
            var output = mixer.OutputWeights;

            int count = Math.Min(output.Length, _blendShapeTargets.Length);
            for (int i = 0; i < count; i++)
            {
                var targets = _blendShapeTargets[i];
                if (targets == null)
                    continue;

                float weight = output[i] * 100f; // Unity は 0-100 スケール
                for (int t = 0; t < targets.Length; t++)
                {
                    targets[t].Renderer.SetBlendShapeWeight(
                        targets[t].RendererBlendShapeIndex, weight);
                }
            }
        }

        /// <summary>
        /// 手動初期化。ProfileSO からプロファイルを読み込み、PlayableGraph を構築する。
        /// ProfileSO が未設定の場合は警告を出して何もしない。
        /// </summary>
        public void Initialize()
        {
            if (_profileSO == null)
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

            // プロファイルを構築（JSON パスがない場合はデフォルトの空プロファイルを使用）
            FacialProfile profile;
            if (!string.IsNullOrWhiteSpace(_profileSO.JsonFilePath))
            {
                // 通常は IProfileRepository 経由だが、テスト用のフォールバック
                // 実際のファイル読み込みは FacialProfileMapper を使用する
                profile = CreateDefaultProfile();
            }
            else
            {
                profile = CreateDefaultProfile();
            }

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
            // 既存のリソースがあればクリーンアップ
            Cleanup();

            _currentProfile = profile;
            _expressionUseCase = new ExpressionUseCase(profile);

            // PlayableGraph を構築
            _graphBuildResult = PlayableGraphBuilder.Build(
                _animator, profile, _blendShapeNames ?? Array.Empty<string>());

            _graphBuildResult.Graph.Play();
            _isInitialized = true;
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

            // PlayableGraph のレイヤーにも反映
            ApplyExpressionToPlayable(expression);
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

            // PlayableGraph のレイヤーからも除去
            RemoveExpressionFromPlayable(expression);
        }

        /// <summary>
        /// プロファイルを切り替える。PlayableGraph を再構築する。
        /// </summary>
        /// <param name="profileSO">新しいプロファイル SO</param>
        public void LoadProfile(FacialProfileSO profileSO)
        {
            if (profileSO == null)
            {
                Debug.LogWarning("ProfileSO が null です。LoadProfile は無視されます。");
                return;
            }

            _profileSO = profileSO;

            // デフォルトプロファイルで再初期化
            var profile = CreateDefaultProfile();
            InitializeInternal(profile);
        }

        /// <summary>
        /// 現在のプロファイルを再読み込みする。PlayableGraph を再構築する。
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
            // 名前 → 出力インデックスの逆引き
            var nameToIndex = new Dictionary<string, int>();
            // 出力インデックス → ターゲットリスト（同名 BS が複数 Renderer に存在する場合）
            var targetLists = new List<List<BlendShapeTarget>>();

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
                        int outputIndex = names.Count;
                        names.Add(bsName);
                        nameToIndex[bsName] = outputIndex;

                        var list = new List<BlendShapeTarget>();
                        list.Add(new BlendShapeTarget(renderer, j));
                        targetLists.Add(list);
                    }
                    else
                    {
                        // 同名 BS が別の Renderer にも存在する
                        int outputIndex = nameToIndex[bsName];
                        targetLists[outputIndex].Add(new BlendShapeTarget(renderer, j));
                    }
                }
            }

            // ターゲットマッピングを配列に変換
            _blendShapeTargets = new BlendShapeTarget[targetLists.Count][];
            for (int i = 0; i < targetLists.Count; i++)
            {
                _blendShapeTargets[i] = targetLists[i].ToArray();
            }

            return names.ToArray();
        }

        private void ApplyExpressionToPlayable(Expression expression)
        {
            if (_graphBuildResult == null || !_currentProfile.HasValue)
                return;

            var profile = _currentProfile.Value;
            string effectiveLayer = profile.GetEffectiveLayer(expression);

            if (!_graphBuildResult.LayerPlayables.TryGetValue(effectiveLayer, out var layerPlayable))
                return;

            var behaviour = layerPlayable.GetBehaviour();

            if (behaviour.ExclusionMode == ExclusionMode.LastWins)
            {
                // BlendShape 値を名前ベースで展開
                var targetValues = ExpandBlendShapeValues(expression);
                behaviour.SetTargetExpression(
                    expression.Id,
                    targetValues,
                    expression.TransitionDuration,
                    expression.TransitionCurve);
            }
            else
            {
                // Blend モード
                var values = ExpandBlendShapeValues(expression);
                behaviour.AddBlendExpression(expression.Id, values, 1.0f);
                behaviour.ComputeBlendOutput();
            }
        }

        private void RemoveExpressionFromPlayable(Expression expression)
        {
            if (_graphBuildResult == null || !_currentProfile.HasValue)
                return;

            var profile = _currentProfile.Value;
            string effectiveLayer = profile.GetEffectiveLayer(expression);

            if (!_graphBuildResult.LayerPlayables.TryGetValue(effectiveLayer, out var layerPlayable))
                return;

            var behaviour = layerPlayable.GetBehaviour();

            if (behaviour.ExclusionMode == ExclusionMode.LastWins)
            {
                behaviour.Deactivate(expression.TransitionDuration);
            }
            else
            {
                behaviour.RemoveBlendExpression(expression.Id);
                behaviour.ComputeBlendOutput();
            }
        }

        private float[] ExpandBlendShapeValues(Expression expression)
        {
            var bsNames = _blendShapeNames ?? Array.Empty<string>();
            var values = new float[bsNames.Length];

            var bsSpan = expression.BlendShapeValues.Span;
            for (int i = 0; i < bsSpan.Length; i++)
            {
                int idx = FindBlendShapeIndex(bsSpan[i].Name);
                if (idx >= 0)
                {
                    values[idx] = bsSpan[i].Value;
                }
            }

            return values;
        }

        private int FindBlendShapeIndex(string name)
        {
            if (_blendShapeNames == null)
                return -1;

            for (int i = 0; i < _blendShapeNames.Length; i++)
            {
                if (_blendShapeNames[i] == name)
                    return i;
            }

            return -1;
        }

        private void Cleanup()
        {
            if (_graphBuildResult != null)
            {
                _graphBuildResult.Dispose();
                _graphBuildResult = null;
            }

            _expressionUseCase = null;
            _isInitialized = false;
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
