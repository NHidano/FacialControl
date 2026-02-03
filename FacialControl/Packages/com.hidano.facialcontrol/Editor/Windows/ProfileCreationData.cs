using System;
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
        /// FacialProfile ドメインモデルを構築する。
        /// スキーマバージョンは "1.0"、Expression リストは空。
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

            return new FacialProfile("1.0", layerDefinitions, Array.Empty<Expression>());
        }
    }
}
