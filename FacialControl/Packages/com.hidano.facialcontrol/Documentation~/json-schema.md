# JSON スキーマリファレンス

FacialControl で使用する JSON ファイルのスキーマ定義です。

FacialControl は 2 種類の JSON ファイルを使用します。

| ファイル | 用途 | 配置先 |
|---------|------|--------|
| プロファイル JSON | 表情定義（レイヤー構成 + Expression） | `StreamingAssets/FacialControl/{name}_profile.json` |
| 設定 JSON（config.json） | OSC 通信設定 + キャッシュ設定 | `StreamingAssets/FacialControl/config.json` |

スキーマバージョン: `1.0`

---

## プロファイル JSON

表情プロファイルを定義する JSON ファイルです。レイヤー構成と Expression（表情）の一覧を含みます。

### ルート構造

```json
{
    "schemaVersion": "1.0",
    "layers": [],
    "expressions": []
}
```

| フィールド | 型 | 必須 | 説明 |
|-----------|------|------|------|
| `schemaVersion` | string | はい | スキーマバージョン。現在は `"1.0"` 固定 |
| `layers` | LayerDefinition[] | はい | レイヤー定義の配列 |
| `expressions` | Expression[] | はい | Expression 定義の配列 |

### LayerDefinition

レイヤーの定義です。レイヤーは表情を分類・管理する単位で、優先度と排他モードを持ちます。

```json
{
    "name": "emotion",
    "priority": 0,
    "exclusionMode": "lastWins"
}
```

| フィールド | 型 | 必須 | 制約 | 説明 |
|-----------|------|------|------|------|
| `name` | string | はい | 空文字不可 | レイヤー名 |
| `priority` | integer | はい | 0 以上 | 優先度（値が大きいほど優先） |
| `exclusionMode` | string | はい | `"lastWins"` \| `"blend"` | 排他モード |

#### 排他モード（ExclusionMode）

| 値 | 動作 |
|----|------|
| `"lastWins"` | 同レイヤー内で最後にアクティブ化された Expression のみ有効。旧 Expression からクロスフェード遷移する |
| `"blend"` | 同レイヤー内の複数 Expression を加算ブレンド。合計値は 0〜1 にクランプされる |

### Expression

表情（Expression）の定義です。BlendShape 値の組み合わせと遷移設定を持ちます。

```json
{
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "name": "笑顔",
    "layer": "emotion",
    "transitionDuration": 0.25,
    "transitionCurve": {
        "type": "easeInOut"
    },
    "blendShapeValues": [
        {"name": "Fcl_ALL_Joy", "value": 1.0},
        {"name": "Fcl_EYE_Joy_R", "value": 0.6, "renderer": "Face"}
    ],
    "layerSlots": [
        {
            "layer": "lipsync",
            "blendShapeValues": [
                {"name": "Fcl_MTH_A", "value": 0.5}
            ]
        }
    ]
}
```

| フィールド | 型 | 必須 | 制約 | 説明 |
|-----------|------|------|------|------|
| `id` | string | はい | GUID 形式、空文字不可 | 一意な識別子 |
| `name` | string | はい | 空文字不可 | 表情名（日本語可） |
| `layer` | string | はい | `layers` に定義済みのレイヤー名 | 所属レイヤー。未定義レイヤーの場合は `"emotion"` にフォールバック |
| `transitionDuration` | number | はい | 0.0〜1.0（自動クランプ） | 遷移時間（秒）。デフォルト: 0.25 |
| `transitionCurve` | TransitionCurve | はい | — | 遷移カーブ設定 |
| `blendShapeValues` | BlendShapeMapping[] | はい | — | BlendShape 値の配列 |
| `layerSlots` | LayerSlot[] | はい | — | 他レイヤーへのオーバーライド設定 |

### TransitionCurve

表情遷移のカーブ設定です。プリセットカーブまたはカスタムカーブを指定できます。

#### プリセットカーブ

```json
{
    "type": "easeInOut"
}
```

#### カスタムカーブ

```json
{
    "type": "custom",
    "keys": [
        {
            "time": 0.0,
            "value": 0.0,
            "inTangent": 0.0,
            "outTangent": 1.0,
            "inWeight": 0.0,
            "outWeight": 0.33,
            "weightedMode": 0
        },
        {
            "time": 1.0,
            "value": 1.0,
            "inTangent": 1.0,
            "outTangent": 0.0,
            "inWeight": 0.33,
            "outWeight": 0.0,
            "weightedMode": 0
        }
    ]
}
```

| フィールド | 型 | 必須 | 制約 | 説明 |
|-----------|------|------|------|------|
| `type` | string | はい | 下記参照 | カーブ種別 |
| `keys` | CurveKeyFrame[] | `type` が `"custom"` の場合のみ | — | カスタムカーブのキーフレーム配列 |

#### カーブ種別（TransitionCurveType）

| 値 | 説明 |
|----|------|
| `"linear"` | 線形補間 |
| `"easeIn"` | 加速（開始がゆるやか） |
| `"easeOut"` | 減速（終了がゆるやか） |
| `"easeInOut"` | 加速→減速 |
| `"custom"` | カスタムカーブ（`keys` フィールドが必要） |

#### CurveKeyFrame

カスタムカーブのキーフレーム定義です。Unity の `Keyframe` 構造体に対応します。

| フィールド | 型 | 説明 |
|-----------|------|------|
| `time` | number | 時間位置（0.0〜1.0） |
| `value` | number | この時点での値 |
| `inTangent` | number | 入力側タンジェント |
| `outTangent` | number | 出力側タンジェント |
| `inWeight` | number | 入力側ウェイト（デフォルト: 0.0） |
| `outWeight` | number | 出力側ウェイト（デフォルト: 0.0） |
| `weightedMode` | integer | ウェイトモード（デフォルト: 0） |

### BlendShapeMapping

BlendShape の名前と値のマッピングです。

```json
{
    "name": "Fcl_ALL_Joy",
    "value": 1.0,
    "renderer": "Face"
}
```

| フィールド | 型 | 必須 | 制約 | 説明 |
|-----------|------|------|------|------|
| `name` | string | はい | 空文字不可 | BlendShape 名。モデルに定義されている名前と完全一致させる（2 バイト文字・特殊記号対応） |
| `value` | number | はい | 0.0〜1.0（自動クランプ） | BlendShape ウェイト（正規化値）。Unity 適用時は 0〜100 に変換される |
| `renderer` | string | いいえ | — | 対象 SkinnedMeshRenderer 名。省略時は全 SkinnedMeshRenderer に適用 |

### LayerSlot

他レイヤーへのオーバーライド設定です。この Expression がアクティブになると、指定レイヤーの BlendShape 値を完全に置換します（対象レイヤーの排他モードをバイパス）。

```json
{
    "layer": "lipsync",
    "blendShapeValues": [
        {"name": "Fcl_MTH_A", "value": 0.5}
    ]
}
```

| フィールド | 型 | 必須 | 説明 |
|-----------|------|------|------|
| `layer` | string | はい | オーバーライド対象のレイヤー名 |
| `blendShapeValues` | BlendShapeMapping[] | はい | 対象レイヤーに適用する BlendShape 値 |

---

## 設定 JSON（config.json）

OSC 通信と AnimationClip キャッシュの設定ファイルです。

### ルート構造

```json
{
    "schemaVersion": "1.0",
    "osc": {},
    "cache": {}
}
```

| フィールド | 型 | 必須 | 説明 |
|-----------|------|------|------|
| `schemaVersion` | string | はい | スキーマバージョン。現在は `"1.0"` 固定 |
| `osc` | OscConfiguration | はい | OSC 通信設定 |
| `cache` | CacheConfiguration | はい | キャッシュ設定 |

### OscConfiguration

OSC 通信の設定です。VRChat OSC 互換（`/avatar/parameters/{name}` 形式）に対応しています。

```json
{
    "sendPort": 9000,
    "receivePort": 9001,
    "preset": "vrchat",
    "mapping": []
}
```

| フィールド | 型 | 必須 | 制約 | デフォルト | 説明 |
|-----------|------|------|------|-----------|------|
| `sendPort` | integer | はい | 0〜65535 | 9000 | UDP 送信ポート |
| `receivePort` | integer | はい | 0〜65535 | 9001 | UDP 受信ポート |
| `preset` | string | はい | — | `"vrchat"` | OSC アドレスプリセット名 |
| `mapping` | OscMapping[] | はい | — | `[]` | OSC アドレスと BlendShape のマッピング配列 |

#### プリセットの OSC アドレス形式

| プリセット | アドレス形式 |
|-----------|------------|
| `"vrchat"` | `/avatar/parameters/{blendShapeName}` |
| `"arkit"` | `/ARKit/{blendShapeName}` |

### OscMapping

OSC アドレスと BlendShape の対応を定義します。送信・受信の両方に使用されます。

```json
{
    "oscAddress": "/avatar/parameters/Fcl_ALL_Joy",
    "blendShapeName": "Fcl_ALL_Joy",
    "layer": "emotion"
}
```

| フィールド | 型 | 必須 | 説明 |
|-----------|------|------|------|
| `oscAddress` | string | はい | OSC アドレスパス |
| `blendShapeName` | string | はい | 対象の BlendShape 名 |
| `layer` | string | はい | 対象のレイヤー名 |

### CacheConfiguration

AnimationClip の LRU キャッシュ設定です。

```json
{
    "animationClipLruSize": 16
}
```

| フィールド | 型 | 必須 | 制約 | デフォルト | 説明 |
|-----------|------|------|------|-----------|------|
| `animationClipLruSize` | integer | はい | 1 以上 | 16 | AnimationClip LRU キャッシュの最大エントリ数 |

---

## サンプル JSON

### プロファイル JSON（技術仕様書 §13.7）

3 レイヤー構成で 3 つの Expression を含む完全なプロファイル例です。

```json
{
    "schemaVersion": "1.0",
    "layers": [
        {"name": "emotion", "priority": 0, "exclusionMode": "lastWins"},
        {"name": "lipsync", "priority": 1, "exclusionMode": "blend"},
        {"name": "eye", "priority": 2, "exclusionMode": "lastWins"}
    ],
    "expressions": [
        {
            "id": "550e8400-e29b-41d4-a716-446655440000",
            "name": "笑顔",
            "layer": "emotion",
            "transitionDuration": 0.25,
            "transitionCurve": {
                "type": "easeInOut"
            },
            "blendShapeValues": [
                {"name": "Fcl_ALL_Joy", "value": 1.0},
                {"name": "Fcl_EYE_Joy", "value": 0.8},
                {"name": "Fcl_EYE_Joy_R", "value": 0.6, "renderer": "Face"}
            ],
            "layerSlots": [
                {
                    "layer": "lipsync",
                    "blendShapeValues": [
                        {"name": "Fcl_MTH_A", "value": 0.5}
                    ]
                }
            ]
        },
        {
            "id": "661f9511-f30c-52e5-b827-557766551111",
            "name": "怒り",
            "layer": "emotion",
            "transitionDuration": 0.15,
            "transitionCurve": {
                "type": "linear"
            },
            "blendShapeValues": [
                {"name": "Fcl_ALL_Angry", "value": 1.0},
                {"name": "Fcl_BRW_Angry", "value": 0.9}
            ],
            "layerSlots": []
        },
        {
            "id": "772a0622-a41d-63f6-c938-668877662222",
            "name": "まばたき",
            "layer": "eye",
            "transitionDuration": 0.08,
            "transitionCurve": {
                "type": "linear"
            },
            "blendShapeValues": [
                {"name": "Fcl_EYE_Close", "value": 1.0}
            ],
            "layerSlots": []
        }
    ]
}
```

### 設定 JSON（技術仕様書 §13.8）

VRChat プリセットの OSC 設定例です。

```json
{
    "schemaVersion": "1.0",
    "osc": {
        "sendPort": 9000,
        "receivePort": 9001,
        "preset": "vrchat",
        "mapping": [
            {"oscAddress": "/avatar/parameters/Fcl_ALL_Joy", "blendShapeName": "Fcl_ALL_Joy", "layer": "emotion"},
            {"oscAddress": "/avatar/parameters/Fcl_MTH_A", "blendShapeName": "Fcl_MTH_A", "layer": "lipsync"}
        ]
    },
    "cache": {
        "animationClipLruSize": 16
    }
}
```

### カスタムカーブを含むプロファイル例

```json
{
    "schemaVersion": "1.0",
    "layers": [
        {"name": "emotion", "priority": 0, "exclusionMode": "lastWins"}
    ],
    "expressions": [
        {
            "id": "883b1733-b52e-74a7-da49-779988773333",
            "name": "驚き",
            "layer": "emotion",
            "transitionDuration": 0.3,
            "transitionCurve": {
                "type": "custom",
                "keys": [
                    {
                        "time": 0.0,
                        "value": 0.0,
                        "inTangent": 0.0,
                        "outTangent": 2.0,
                        "inWeight": 0.0,
                        "outWeight": 0.33,
                        "weightedMode": 0
                    },
                    {
                        "time": 0.5,
                        "value": 1.0,
                        "inTangent": 0.0,
                        "outTangent": 0.0,
                        "inWeight": 0.33,
                        "outWeight": 0.33,
                        "weightedMode": 0
                    },
                    {
                        "time": 1.0,
                        "value": 1.0,
                        "inTangent": 0.0,
                        "outTangent": 0.0,
                        "inWeight": 0.33,
                        "outWeight": 0.0,
                        "weightedMode": 0
                    }
                ]
            },
            "blendShapeValues": [
                {"name": "Fcl_ALL_Surprised", "value": 1.0}
            ],
            "layerSlots": []
        }
    ]
}
```

---

## 値の範囲と自動クランプ

以下のフィールドは範囲外の値が指定された場合、自動的にクランプされます。

| フィールド | 有効範囲 | クランプ動作 |
|-----------|---------|------------|
| `BlendShapeMapping.value` | 0.0〜1.0 | 負値は 0.0、1.0 超は 1.0 にクランプ |
| `Expression.transitionDuration` | 0.0〜1.0 | 負値は 0.0、1.0 超は 1.0 にクランプ |
| `LayerDefinition.priority` | 0 以上 | 負値は 0 にクランプ |

## テンプレートファイル

パッケージには以下のテンプレートファイルが同梱されています。`StreamingAssets/FacialControl/` にコピーして使用してください。

| ファイル | 説明 |
|---------|------|
| `Templates/default_profile.json` | デフォルト 3 レイヤー構成 + 基本 Expression（default, blink, gaze_follow, gaze_camera） |
| `Templates/default_config.json` | VRChat プリセットの OSC 設定 + 基本マッピング |
