# OscReceiverOptions JSON スキーマ

`OscReceiverOptionsDto` は `OscReceiverAdapterBinding` の listen endpoint、mode 別 mapping、staleness、フェイルセーフ、heartbeat 整合性検査、bundle 解釈を保存する JSON DTO です。Unity の `JsonUtility` 互換 DTO として扱うため、未知キーは無視され、欠落または不正な値は DTO 側の既定値で補完されます。

> **環境依存項目の保存先**: `listenEndpoint` / `listenPort` / `stalenessSeconds` / `failSafeMode` /
> `consistencyCheckWarnLog` / `bundleMode` / `bundleAccumulationTimeoutMs` の Inspector 編集および
> 永続化は、spec [`adapter-runtime-settings`](../../../../.kiro/specs/adapter-runtime-settings/) で導入された
> `OscRuntimeSettingsSO` (sub-asset) の Receiver セクションに移行済みです。`OscReceiverAdapterBinding._settings`
> 経由でアサインしてください。本 JSON DTO は `OscRuntimeSettingsSO.ToJson()` / `FromJson()` で
> ラウンドトリップする際の永続化フォーマットも兼ねます。詳細は
> [`com.hidano.facialcontrol/Documentation~/adapter-runtime-settings.md`](../../com.hidano.facialcontrol/Documentation~/adapter-runtime-settings.md)
> を参照してください。

## ルートフィールド

| フィールド | 型 | 既定値 | 説明 |
| --- | --- | --- | --- |
| `listenEndpoint` | `string` | `"127.0.0.1"` | UDP listen endpoint。空文字または空白は既定値に補完されます。 |
| `listenPort` | `integer` | `9001` | UDP listen port。`1` から `65535` 以外は既定値に補完されます。 |
| `mappings` | `OscMappingEntryDto[]` | `[]` | `blendShape` / `gazeVrchatXy` / `gazeArkit8Bs` の mode 別 entry 一覧。 |
| `stalenessSeconds` | `number` | `0.0` | 受信値の有効期限（秒）。`0.0` は staleness 判定なしです。負数または `NaN` は既定値に補完されます。 |
| `failSafeMode` | `string` | `"revertToBase"` | staleness 超過時の挙動。`"revertToBase"` または `"holdLastValue"` を指定します。 |
| `consistencyCheckWarnLog` | `bool` | `true` | heartbeat の BlendShape 名一覧と受信 mapping の不一致を警告ログに出すかどうか。 |
| `bundleMode` | `string` | `"atomicSwap"` | bundle 解釈モード。`"atomicSwap"` または `"individualMessage"` を指定します。 |
| `bundleAccumulationTimeoutMs` | `number` | `5.0` | bundle 内メッセージを同一フレーム単位として蓄積するタイムアウト（ミリ秒）。`0` 以下または `NaN` は既定値に補完されます。 |

## failSafeMode

| JSON 値 | runtime enum | 説明 |
| --- | --- | --- |
| `"revertToBase"` | `FailSafeMode.RevertToBase` | staleness 超過時にベース表情へ戻します。 |
| `"holdLastValue"` | `FailSafeMode.HoldLastValue` | staleness 超過時も最後に受信した値を保持します。 |

## bundleMode

| JSON 値 | runtime enum | 説明 |
| --- | --- | --- |
| `"atomicSwap"` | `BundleInterpretationMode.AtomicSwap` | 同一 bundle の全メッセージを蓄積してから一括反映します。 |
| `"individualMessage"` | `BundleInterpretationMode.IndividualMessage` | bare message と同様に受信順で個別反映します。 |

## OscMappingEntryDto 共通フィールド

| フィールド | 型 | 既定値 | 説明 |
| --- | --- | --- | --- |
| `mode` | `string` | `"blendShape"` | canonical JSON 値は `"blendShape"` / `"gazeVrchatXy"` / `"gazeArkit8Bs"`。runtime enum 名の `Normal_BlendShape` / `Gaze_VRChat_XY` / `Gaze_ARKit_8BS` も入力として受け付け、canonical 値に正規化されます。 |
| `expressionId` | `string` | `""` | FacialControl 側の expressionId。空の場合、その entry はスキップされ警告ログが出ます。 |
| `addressPattern` | `string` | `""` | mode 別に意味が変わる OSC アドレス指定。 |
| `sourceIdLeft` | `string` | `""` | Gaze entry で `leftRightIndependent = true` のときに参照される左目用 sourceId。 |
| `sourceIdRight` | `string` | `""` | Gaze entry で `leftRightIndependent = true` のときに参照される右目用 sourceId。 |
| `leftRightIndependent` | `bool` | `false` | Gaze を左右別 source として publish するかどうか。`true` の Gaze entry では `sourceIdLeft` と `sourceIdRight` の両方が必須です。 |

## mode 別 entry 構造

| runtime mode | canonical JSON `mode` | 必須フィールド | `addressPattern` | Gaze source policy |
| --- | --- | --- | --- | --- |
| `Normal_BlendShape` | `"blendShape"` | `expressionId`, `addressPattern` | 完全な OSC アドレス。例: `/avatar/parameters/Smile` | `sourceIdLeft` / `sourceIdRight` / `leftRightIndependent` は無視されます。 |
| `Gaze_VRChat_XY` | `"gazeVrchatXy"` | `expressionId`, `addressPattern` | X/Y 末尾を除いた base アドレス。例: `/avatar/parameters/Look` は `/avatar/parameters/LookX` と `/avatar/parameters/LookY` を受信します。 | `leftRightIndependent = false` では `(slug, expressionId)` の共通 source、`true` では `(slug, expressionId.left/right)` の左右別 source を publish します。 |
| `Gaze_ARKit_8BS` | `"gazeArkit8Bs"` | `expressionId` | 無視されます。固定の `/ARKit/eyeLookInLeft` など 8 アドレスを受信します。 | `leftRightIndependent` の値に関わらず左右別 source を publish します。`true` の場合は `sourceIdLeft` / `sourceIdRight` の両方が必須です。 |

## 整合性検査ポリシー

送信側 heartbeat `/_facialcontrol/blendshape_names` を受信すると、受信側は BlendShape mapping と名前一覧を比較します。部分不一致の場合、不一致 BlendShape の反映だけを停止し、一致した BlendShape の更新は継続します。`consistencyCheckWarnLog = true` の場合、送信側にだけ存在する名前と受信側にだけ存在する名前を Unity 標準警告ログへ出力します。

## サンプル JSON: Normal_BlendShape

```json
{
  "listenEndpoint": "127.0.0.1",
  "listenPort": 9001,
  "mappings": [
    {
      "mode": "blendShape",
      "expressionId": "Smile",
      "addressPattern": "/avatar/parameters/Smile",
      "sourceIdLeft": "",
      "sourceIdRight": "",
      "leftRightIndependent": false
    }
  ],
  "stalenessSeconds": 0.25,
  "failSafeMode": "revertToBase",
  "consistencyCheckWarnLog": true,
  "bundleMode": "atomicSwap",
  "bundleAccumulationTimeoutMs": 5.0
}
```

## サンプル JSON: Gaze_VRChat_XY

```json
{
  "listenEndpoint": "127.0.0.1",
  "listenPort": 9001,
  "mappings": [
    {
      "mode": "gazeVrchatXy",
      "expressionId": "Look",
      "addressPattern": "/avatar/parameters/Look",
      "sourceIdLeft": "Look.left",
      "sourceIdRight": "Look.right",
      "leftRightIndependent": true
    }
  ],
  "stalenessSeconds": 0.0,
  "failSafeMode": "holdLastValue",
  "consistencyCheckWarnLog": false,
  "bundleMode": "individualMessage",
  "bundleAccumulationTimeoutMs": 12.0
}
```

## サンプル JSON: Gaze_ARKit_8BS

```json
{
  "listenEndpoint": "127.0.0.1",
  "listenPort": 9001,
  "mappings": [
    {
      "mode": "gazeArkit8Bs",
      "expressionId": "LookArKit",
      "addressPattern": "",
      "sourceIdLeft": "",
      "sourceIdRight": "",
      "leftRightIndependent": false
    }
  ],
  "stalenessSeconds": 0.1,
  "failSafeMode": "revertToBase",
  "consistencyCheckWarnLog": true,
  "bundleMode": "atomicSwap",
  "bundleAccumulationTimeoutMs": 5.0
}
```
