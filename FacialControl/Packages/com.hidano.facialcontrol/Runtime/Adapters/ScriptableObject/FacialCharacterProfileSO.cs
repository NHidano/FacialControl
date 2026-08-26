using System.Collections.Generic;
using System.IO;
using Hidano.FacialControl.Adapters.FileSystem;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;
using GazeBindingConfig = Hidano.FacialControl.Adapters.ScriptableObject.GazeBindingConfig;

namespace Hidano.FacialControl.Adapters.ScriptableObject.Serializable
{
    [CreateAssetMenu(fileName = "NewFacialCharacterProfile", menuName = "FacialControl/Facial Character Profile")]
    public class FacialCharacterProfileSO : UnityEngine.ScriptableObject, IFacialCharacterProfile
    {
        public const string StreamingAssetsRootFolder = "FacialControl";
        public const string ProfileJsonFileName = "profile.json";

        [SerializeField] protected string _schemaVersion = SystemTextJsonParser.SchemaVersionV2;
        [SerializeField] protected List<LayerDefinitionSerializable> _layers = new List<LayerDefinitionSerializable>();
        [SerializeField] protected List<ExpressionSerializable> _expressions = new List<ExpressionSerializable>();
        [SerializeField] protected BaseExpressionSerializable _baseExpression = new BaseExpressionSerializable();
        [SerializeField] protected List<string> _rendererPaths = new List<string>();
        [SerializeField] protected List<GazeBindingConfig> _gazeConfigs = new List<GazeBindingConfig>();
        [SerializeField] private List<string> _slots = new();
        [SerializeField] protected List<OverlaySlotBindingSerializable> _defaultOverlays = new List<OverlaySlotBindingSerializable>();
        [SerializeReference] protected List<AdapterBindingBase> _adapterBindings = new List<AdapterBindingBase>();

#if UNITY_EDITOR
        [SerializeField] protected GameObject _referenceModel;
        public GameObject ReferenceModel { get => _referenceModel; set => _referenceModel = value; }
#endif

        public string CharacterAssetName => name;
        public string SchemaVersion { get => _schemaVersion; set => _schemaVersion = value; }
        public List<LayerDefinitionSerializable> Layers => _layers;
        public List<ExpressionSerializable> Expressions => _expressions;
        public BaseExpressionSerializable BaseExpression
        {
            get
            {
                if (_baseExpression == null)
                {
                    _baseExpression = new BaseExpressionSerializable();
                }

                _baseExpression.EnsureCachedSnapshot();
                return _baseExpression;
            }
        }

        public List<string> RendererPaths => _rendererPaths;
        public IReadOnlyList<GazeBindingConfig> GazeConfigs => _gazeConfigs ?? (_gazeConfigs = new List<GazeBindingConfig>());
        public IReadOnlyList<string> Slots => _slots ?? (_slots = new List<string>());
        public List<OverlaySlotBindingSerializable> DefaultOverlays
            => _defaultOverlays ?? (_defaultOverlays = new List<OverlaySlotBindingSerializable>());
        public IReadOnlyList<AdapterBindingBase> AdapterBindings => _adapterBindings;

        public virtual FacialProfile BuildFallbackProfile()
        {
            return FacialCharacterProfileConverter.ToFacialProfile(
                schemaVersion: _schemaVersion,
                layers: _layers,
                expressions: _expressions,
                rendererPaths: _rendererPaths,
                defaultOverlays: DefaultOverlays,
                slots: Slots,
                // StreamingAssets の profile.json が未生成でも SO の bake 済みベース表情を適用する。
                baseExpression: BaseExpression.EnsureCachedSnapshot().blendShapes);
        }

        public static string GetStreamingAssetsProfilePath(string assetName)
        {
            if (string.IsNullOrWhiteSpace(assetName)) return null;
            return Path.Combine(UnityEngine.Application.streamingAssetsPath, StreamingAssetsRootFolder, assetName, ProfileJsonFileName);
        }

        public virtual FacialProfile LoadProfile()
        {
            string path = GetStreamingAssetsProfilePath(CharacterAssetName);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return BuildFallbackProfile();
            try
            {
                var repo = new FileProfileRepository(new SystemTextJsonParser());
                return repo.LoadProfile(path);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(name + ": StreamingAssets JSON load failed, using SO data. " + ex.Message);
                return BuildFallbackProfile();
            }
        }
    }
}
