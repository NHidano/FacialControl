namespace Hidano.FacialControl.Adapters.Json
{
    /// <summary>
    /// FacialControl JSON スキーマの定数定義。
    /// プロファイル JSON（技術仕様書 §13.7）および
    /// 設定 JSON（技術仕様書 §13.8）のスキーマ情報を提供する。
    /// </summary>
    public static class JsonSchemaDefinition
    {
        /// <summary>
        /// 現在サポートするスキーマバージョン
        /// </summary>
        public const string CurrentSchemaVersion = "1.0";

        /// <summary>
        /// プロファイル JSON スキーマ定義（技術仕様書 §13.7）
        /// </summary>
        public static class Profile
        {
            // --- ルートフィールド ---

            /// <summary>schemaVersion フィールド名</summary>
            public const string SchemaVersion = "schemaVersion";

            /// <summary>layers フィールド名</summary>
            public const string Layers = "layers";

            /// <summary>expressions フィールド名</summary>
            public const string Expressions = "expressions";

            /// <summary>rendererPaths フィールド名</summary>
            public const string RendererPaths = "rendererPaths";

            /// <summary>
            /// レイヤー定義のフィールド名
            /// </summary>
            public static class Layer
            {
                /// <summary>レイヤー名</summary>
                public const string Name = "name";

                /// <summary>優先度（0 以上の整数）</summary>
                public const string Priority = "priority";

                /// <summary>排他モード（"lastWins" | "blend"）</summary>
                public const string ExclusionMode = "exclusionMode";
            }

            /// <summary>
            /// Expression のフィールド名
            /// </summary>
            public static class Expression
            {
                /// <summary>ID（GUID 文字列）</summary>
                public const string Id = "id";

                /// <summary>表情名</summary>
                public const string Name = "name";

                /// <summary>所属レイヤー名</summary>
                public const string Layer = "layer";

                /// <summary>遷移時間（0〜1 秒、デフォルト 0.25）</summary>
                public const string TransitionDuration = "transitionDuration";

                /// <summary>遷移カーブ設定</summary>
                public const string TransitionCurve = "transitionCurve";

                /// <summary>BlendShape 値の配列</summary>
                public const string BlendShapeValues = "blendShapeValues";

                /// <summary>レイヤースロット（他レイヤーへのオーバーライド）</summary>
                public const string LayerSlots = "layerSlots";
            }

            /// <summary>
            /// 遷移カーブのフィールド名
            /// </summary>
            public static class TransitionCurve
            {
                /// <summary>カーブ種別（"linear" | "easeIn" | "easeOut" | "easeInOut" | "custom"）</summary>
                public const string Type = "type";

                /// <summary>カスタムカーブ用キーフレーム配列</summary>
                public const string Keys = "keys";
            }

            /// <summary>
            /// カーブキーフレームのフィールド名
            /// </summary>
            public static class CurveKeyFrame
            {
                /// <summary>時間（0〜1）</summary>
                public const string Time = "time";

                /// <summary>値</summary>
                public const string Value = "value";

                /// <summary>入力タンジェント</summary>
                public const string InTangent = "inTangent";

                /// <summary>出力タンジェント</summary>
                public const string OutTangent = "outTangent";

                /// <summary>入力ウェイト</summary>
                public const string InWeight = "inWeight";

                /// <summary>出力ウェイト</summary>
                public const string OutWeight = "outWeight";

                /// <summary>ウェイトモード</summary>
                public const string WeightedMode = "weightedMode";
            }

            /// <summary>
            /// BlendShapeMapping のフィールド名
            /// </summary>
            public static class BlendShapeMapping
            {
                /// <summary>BlendShape 名</summary>
                public const string Name = "name";

                /// <summary>値（0〜1）</summary>
                public const string Value = "value";

                /// <summary>対象 Renderer 名（省略可）</summary>
                public const string Renderer = "renderer";
            }

            /// <summary>
            /// LayerSlot のフィールド名
            /// </summary>
            public static class LayerSlot
            {
                /// <summary>ターゲットレイヤー名</summary>
                public const string Layer = "layer";

                /// <summary>BlendShape 値の配列</summary>
                public const string BlendShapeValues = "blendShapeValues";
            }

            /// <summary>
            /// ExclusionMode の JSON 値
            /// </summary>
            public static class ExclusionModeValues
            {
                public const string LastWins = "lastWins";
                public const string Blend = "blend";
            }

            /// <summary>
            /// TransitionCurveType の JSON 値
            /// </summary>
            public static class TransitionCurveTypeValues
            {
                public const string Linear = "linear";
                public const string EaseIn = "easeIn";
                public const string EaseOut = "easeOut";
                public const string EaseInOut = "easeInOut";
                public const string Custom = "custom";
            }
        }

        /// <summary>
        /// 設定 JSON スキーマ定義（技術仕様書 §13.8）
        /// </summary>
        public static class Config
        {
            // --- ルートフィールド ---

            /// <summary>schemaVersion フィールド名</summary>
            public const string SchemaVersion = "schemaVersion";

            /// <summary>osc フィールド名</summary>
            public const string Osc = "osc";

            /// <summary>cache フィールド名</summary>
            public const string Cache = "cache";

            /// <summary>
            /// OSC 設定のフィールド名
            /// </summary>
            public static class OscConfig
            {
                /// <summary>送信ポート（デフォルト 9000）</summary>
                public const string SendPort = "sendPort";

                /// <summary>受信ポート（デフォルト 9001）</summary>
                public const string ReceivePort = "receivePort";

                /// <summary>プリセット名（デフォルト "vrchat"）</summary>
                public const string Preset = "preset";

                /// <summary>マッピング配列</summary>
                public const string Mapping = "mapping";
            }

            /// <summary>
            /// OSC マッピングのフィールド名
            /// </summary>
            public static class OscMapping
            {
                /// <summary>OSC アドレス</summary>
                public const string OscAddress = "oscAddress";

                /// <summary>BlendShape 名</summary>
                public const string BlendShapeName = "blendShapeName";

                /// <summary>レイヤー名</summary>
                public const string Layer = "layer";
            }

            /// <summary>
            /// キャッシュ設定のフィールド名
            /// </summary>
            public static class CacheConfig
            {
                /// <summary>AnimationClip LRU キャッシュサイズ（デフォルト 16）</summary>
                public const string AnimationClipLruSize = "animationClipLruSize";
            }
        }

        /// <summary>
        /// 技術仕様書 §13.7 のサンプルプロファイル JSON
        /// </summary>
        public const string SampleProfileJson = @"{
    ""schemaVersion"": ""1.0"",
    ""rendererPaths"": [""Armature/Body""],
    ""layers"": [
        {""name"": ""emotion"", ""priority"": 0, ""exclusionMode"": ""lastWins""},
        {""name"": ""lipsync"", ""priority"": 1, ""exclusionMode"": ""blend""},
        {""name"": ""eye"", ""priority"": 2, ""exclusionMode"": ""lastWins""}
    ],
    ""expressions"": [
        {
            ""id"": ""550e8400-e29b-41d4-a716-446655440000"",
            ""name"": ""笑顔"",
            ""layer"": ""emotion"",
            ""transitionDuration"": 0.25,
            ""transitionCurve"": {
                ""type"": ""easeInOut""
            },
            ""blendShapeValues"": [
                {""name"": ""Fcl_ALL_Joy"", ""value"": 1.0},
                {""name"": ""Fcl_EYE_Joy"", ""value"": 0.8},
                {""name"": ""Fcl_EYE_Joy_R"", ""value"": 0.6, ""renderer"": ""Face""}
            ],
            ""layerSlots"": [
                {
                    ""layer"": ""lipsync"",
                    ""blendShapeValues"": [
                        {""name"": ""Fcl_MTH_A"", ""value"": 0.5}
                    ]
                }
            ]
        },
        {
            ""id"": ""661f9511-f30c-52e5-b827-557766551111"",
            ""name"": ""怒り"",
            ""layer"": ""emotion"",
            ""transitionDuration"": 0.15,
            ""transitionCurve"": {
                ""type"": ""linear""
            },
            ""blendShapeValues"": [
                {""name"": ""Fcl_ALL_Angry"", ""value"": 1.0},
                {""name"": ""Fcl_BRW_Angry"", ""value"": 0.9}
            ],
            ""layerSlots"": []
        },
        {
            ""id"": ""772a0622-a41d-63f6-c938-668877662222"",
            ""name"": ""まばたき"",
            ""layer"": ""eye"",
            ""transitionDuration"": 0.08,
            ""transitionCurve"": {
                ""type"": ""linear""
            },
            ""blendShapeValues"": [
                {""name"": ""Fcl_EYE_Close"", ""value"": 1.0}
            ],
            ""layerSlots"": []
        }
    ]
}";

        /// <summary>
        /// 技術仕様書 §13.8 のサンプル設定 JSON
        /// </summary>
        public const string SampleConfigJson = @"{
    ""schemaVersion"": ""1.0"",
    ""osc"": {
        ""sendPort"": 9000,
        ""receivePort"": 9001,
        ""preset"": ""vrchat"",
        ""mapping"": [
            {""oscAddress"": ""/avatar/parameters/Fcl_ALL_Joy"", ""blendShapeName"": ""Fcl_ALL_Joy"", ""layer"": ""emotion""},
            {""oscAddress"": ""/avatar/parameters/Fcl_MTH_A"", ""blendShapeName"": ""Fcl_MTH_A"", ""layer"": ""lipsync""}
        ]
    },
    ""cache"": {
        ""animationClipLruSize"": 16
    }
}";
    }
}
