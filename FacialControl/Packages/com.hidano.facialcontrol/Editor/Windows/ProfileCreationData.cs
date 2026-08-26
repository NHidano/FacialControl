using System;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Editor.Windows
{
    /// <summary>
    /// 新規プロファイル作成用のデータ構造体。
    /// ユーザーが入力したプロファイル名とレイヤー構成を保持し、
    /// FacialProfile ドメインモデルへの変換を提供する。
    /// </summary>
    public sealed class ProfileCreationData
    {
        /// <summary>
        /// 雛形 Expression 生成に使用する BlendShape 命名規則プリセット。
        /// </summary>
        public enum NamingConvention
        {
            /// <summary>
            /// VRM 0.x / 1.0 の標準 BlendShape 名（例: Fcl_ALL_Joy）
            /// </summary>
            VRM,

            /// <summary>
            /// ARKit 52 の BlendShape 名（例: mouthSmile_L）
            /// </summary>
            ARKit,

            /// <summary>
            /// 雛形を生成しない（ユーザーが個別に定義する）
            /// </summary>
            None
        }

        /// <summary>
        /// レイヤー定義エントリ（UI 入力用）
        /// </summary>
        public sealed class LayerEntry
        {
            public string Name { get; set; }
            public int Priority { get; set; }
            public ExclusionMode ExclusionMode { get; set; }

            public LayerEntry(string name, int priority, ExclusionMode exclusionMode)
            {
                Name = name;
                Priority = priority;
                ExclusionMode = exclusionMode;
            }
        }

        /// <summary>
        /// プロファイル名
        /// </summary>
        public string ProfileName { get; }

        /// <summary>
        /// レイヤー定義リスト
        /// </summary>
        public LayerEntry[] Layers { get; }

        /// <summary>
        /// 雛形 Expression（smile / angry / blink）を生成するかどうか。
        /// デフォルト false。雛形を含めたい場合は明示的に true を設定する
        /// （ダイアログ UI 側で true + <see cref="NamingConvention.VRM"/> を初期値として提示する）。
        /// </summary>
        public bool IncludeSampleExpressions { get; set; } = false;

        /// <summary>
        /// 雛形 Expression 生成時に使用する BlendShape 命名規則。
        /// デフォルト <see cref="NamingConvention.None"/>（雛形なし）。
        /// </summary>
        public NamingConvention Naming { get; set; } = NamingConvention.None;

        /// <summary>
        /// JSON ファイル名（プロファイル名 + .json）
        /// </summary>
        public string JsonFileName => ProfileName + ".json";

        /// <summary>
        /// StreamingAssets からの相対パス
        /// </summary>
        public string JsonRelativePath => "FacialControl/" + JsonFileName;

        /// <summary>
        /// 指定されたプロファイル名とレイヤー構成でデータを生成する。
        /// </summary>
        /// <param name="profileName">プロファイル名（空文字不可）</param>
        /// <param name="layers">レイヤー定義配列（null・空配列不可）</param>
        public ProfileCreationData(string profileName, LayerEntry[] layers)
        {
            if (profileName == null)
                throw new ArgumentNullException(nameof(profileName));
            if (string.IsNullOrWhiteSpace(profileName))
                throw new ArgumentException("プロファイル名を空にすることはできません。", nameof(profileName));
            if (layers == null)
                throw new ArgumentNullException(nameof(layers));
            if (layers.Length == 0)
                throw new ArgumentException("レイヤーは 1 つ以上必要です。", nameof(layers));

            ProfileName = profileName;
            Layers = layers;
        }

        /// <summary>
        /// デフォルトレイヤー構成（emotion / lipsync / eye）で作成データを生成する。
        /// </summary>
        /// <param name="profileName">プロファイル名（空文字不可）</param>
        public static ProfileCreationData CreateDefault(string profileName)
        {
            if (profileName == null)
                throw new ArgumentNullException(nameof(profileName));
            if (string.IsNullOrWhiteSpace(profileName))
                throw new ArgumentException("プロファイル名を空にすることはできません。", nameof(profileName));

            var layers = new[]
            {
                new LayerEntry("emotion", 0, ExclusionMode.LastWins),
                new LayerEntry("lipsync", 1, ExclusionMode.Blend),
                new LayerEntry("eye", 2, ExclusionMode.LastWins)
            };

            return new ProfileCreationData(profileName, layers);
        }

        /// <summary>
        /// 命名規則に対応した雛形 Expression 配列を構築する。
        /// smile / angry（emotion レイヤー）と blink（eye レイヤー）を含む。
        /// <see cref="NamingConvention.None"/> の場合は空配列を返す。
        /// </summary>
        /// <param name="convention">BlendShape 命名規則プリセット</param>
        /// <returns>雛形 Expression 配列</returns>
        public static Expression[] BuildSampleExpressions(NamingConvention convention)
        {
            if (convention == NamingConvention.None)
                return Array.Empty<Expression>();

            var easeInOut = new TransitionCurve(TransitionCurveType.EaseInOut);
            var linear = new TransitionCurve(TransitionCurveType.Linear);

            // 命名規則毎の BlendShape 値をまとめて解決
            BlendShapeMapping[] smileShapes;
            BlendShapeMapping[] angryShapes;
            BlendShapeMapping[] blinkShapes;

            switch (convention)
            {
                case NamingConvention.VRM:
                    smileShapes = new[]
                    {
                        new BlendShapeMapping("Fcl_ALL_Joy", 1.0f)
                    };
                    angryShapes = new[]
                    {
                        new BlendShapeMapping("Fcl_ALL_Angry", 1.0f)
                    };
                    blinkShapes = new[]
                    {
                        new BlendShapeMapping("Fcl_ALL_Close", 1.0f)
                    };
                    break;

                case NamingConvention.ARKit:
                    smileShapes = new[]
                    {
                        new BlendShapeMapping("mouthSmile_L", 1.0f),
                        new BlendShapeMapping("mouthSmile_R", 1.0f)
                    };
                    angryShapes = new[]
                    {
                        new BlendShapeMapping("browDown_L", 1.0f),
                        new BlendShapeMapping("browDown_R", 1.0f)
                    };
                    blinkShapes = new[]
                    {
                        new BlendShapeMapping("eyeBlink_L", 1.0f),
                        new BlendShapeMapping("eyeBlink_R", 1.0f)
                    };
                    break;

                default:
                    return Array.Empty<Expression>();
            }

            return new[]
            {
                new Expression(
                    Guid.NewGuid().ToString(),
                    "smile",
                    "emotion",
                    0.25f,
                    easeInOut,
                    smileShapes),
                new Expression(
                    Guid.NewGuid().ToString(),
                    "angry",
                    "emotion",
                    0.25f,
                    easeInOut,
                    angryShapes),
                new Expression(
                    Guid.NewGuid().ToString(),
                    "blink",
                    "eye",
                    0.08f,
                    linear,
                    blinkShapes)
            };
        }

        /// <summary>
        /// FacialProfile ドメインモデルを構築する。
        /// スキーマバージョンは <see cref="SystemTextJsonParser.SchemaVersionV2"/>。
        /// <see cref="IncludeSampleExpressions"/> が true かつ <see cref="Naming"/> が
        /// <see cref="NamingConvention.None"/> でない場合、雛形 Expression を含める。
        /// </summary>
        public FacialProfile BuildProfile()
        {
            var layerDefinitions = new LayerDefinition[Layers.Length];
            for (int i = 0; i < Layers.Length; i++)
            {
                layerDefinitions[i] = new LayerDefinition(
                    Layers[i].Name,
                    Layers[i].Priority,
                    Layers[i].ExclusionMode);
            }

            var expressions = IncludeSampleExpressions
                ? BuildSampleExpressions(Naming)
                : Array.Empty<Expression>();

            return new FacialProfile(SystemTextJsonParser.SchemaVersionV2, layerDefinitions, expressions);
        }
    }
}
// recompile trigger
