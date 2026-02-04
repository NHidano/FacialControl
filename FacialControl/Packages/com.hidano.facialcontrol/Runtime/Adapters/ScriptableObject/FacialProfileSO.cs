using UnityEngine;

namespace Hidano.FacialControl.Adapters.ScriptableObject
{
    /// <summary>
    /// 表情プロファイルの ScriptableObject。
    /// JSON ファイルへの参照ポインターとして機能し、Inspector でのプロファイル指定に使用する。
    /// 正規データは JSON であり、SO は Editor での操作性のためのビュー。
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewFacialProfile",
        menuName = "FacialControl/Facial Profile",
        order = 0)]
    public class FacialProfileSO : UnityEngine.ScriptableObject
    {
        /// <summary>
        /// プロファイル JSON ファイルへのパス（StreamingAssets からの相対パス）
        /// </summary>
        [Tooltip("プロファイル JSON ファイルへのパス（StreamingAssets からの相対パス）")]
        [SerializeField]
        private string _jsonFilePath;

        /// <summary>
        /// スキーマバージョン（Inspector 表示用、読み取り専用）
        /// </summary>
        [Tooltip("JSON スキーマバージョン")]
        [SerializeField]
        private string _schemaVersion;

        /// <summary>
        /// レイヤー数（Inspector 表示用、読み取り専用）
        /// </summary>
        [Tooltip("プロファイル内のレイヤー数")]
        [SerializeField]
        private int _layerCount;

        /// <summary>
        /// Expression 数（Inspector 表示用、読み取り専用）
        /// </summary>
        [Tooltip("プロファイル内の Expression 数")]
        [SerializeField]
        private int _expressionCount;

        /// <summary>
        /// SkinnedMeshRenderer のヒエラルキーパス配列（モデルルートからの相対パス）
        /// </summary>
        [Tooltip("SkinnedMeshRenderer のヒエラルキーパス（モデルルートからの相対パス）")]
        [SerializeField]
        private string[] _rendererPaths;

#if UNITY_EDITOR
        /// <summary>
        /// 参照モデル（Editor 専用）。Inspector で BlendShape 名取得に使用する。
        /// JSON には含まない。
        /// </summary>
        [Tooltip("BlendShape 名取得用の参照モデル（Editor 専用）")]
        [SerializeField]
        private GameObject _referenceModel;
#endif

        /// <summary>
        /// プロファイル JSON ファイルへのパス（StreamingAssets からの相対パス）
        /// </summary>
        public string JsonFilePath
        {
            get => _jsonFilePath;
            set => _jsonFilePath = value;
        }

        /// <summary>
        /// スキーマバージョン（Inspector 表示用）
        /// </summary>
        public string SchemaVersion
        {
            get => _schemaVersion;
            set => _schemaVersion = value;
        }

        /// <summary>
        /// レイヤー数（Inspector 表示用）
        /// </summary>
        public int LayerCount
        {
            get => _layerCount;
            set => _layerCount = value;
        }

        /// <summary>
        /// Expression 数（Inspector 表示用）
        /// </summary>
        public int ExpressionCount
        {
            get => _expressionCount;
            set => _expressionCount = value;
        }

        /// <summary>
        /// SkinnedMeshRenderer のヒエラルキーパス配列（モデルルートからの相対パス）
        /// </summary>
        public string[] RendererPaths
        {
            get => _rendererPaths;
            set => _rendererPaths = value;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 参照モデル（Editor 専用）。Inspector で BlendShape 名取得に使用する。
        /// JSON には含まない。
        /// </summary>
        public GameObject ReferenceModel
        {
            get => _referenceModel;
            set => _referenceModel = value;
        }
#endif
    }
}
