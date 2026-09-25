# OscSenderOptions JSON スキーマ

`OscSenderOptionsDto` は `OscSenderAdapterBinding` の送信先、BlendShape 送信対象、Gaze 送信対象、ループバック抑制、heartbeat 周期を保存する JSON DTO です。Unity の `JsonUtility` 互換 DTO として扱うため、未知キーは無視され、欠落または不正な値は DTO 側の既定値で補完されます。

> **環境依存項目の保存先**: `endpoints` / `suppressLoopback` / `heartbeatIntervalSeconds` の
> Inspector 編集および永続化は、spec [`adapter-runtime-settings`](../../../../.kiro/specs/adapter-runtime-settings/)
> で導入された `OscRuntimeSettingsSO` (sub-asset) の Sender セクションに移行済みです。
> `OscSenderAdapterBinding._settings` 経由でアサインしてください。`blendShapeMapping` /
> `gazeExpressionIds` はキャラ依存項目のため引き続き `OscSenderAdapterBinding._blendShapeNames` /
> `_gazeExpressionIds` でキャラ SO 側に保存されます。詳細は
> [`com.hidano.facialcontrol/Documentation~/adapter-runtime-settings.md`](../../com.hidano.facialcontrol/Documentation~/adapter-runtime-settings.md)
> を参照してください。

## ルートフィールド

| フィールド | 型 | 既定値 | 説明 |
| --- | --- | --- | --- |
| `endpoints` | `OscSenderEndpointDto[]` | `[{ "ip": "127.0.0.1", "port": 9000, "preset": "vrchat", "enabled": true }]` | 送信先 endpoint 一覧。`null` の場合は既定 endpoint 1 件に補完されます。 |
| `blendShapeMapping` | `string[]` | `[]` | 送信する BlendShape 名一覧。空配列は明示リストなしを表します。 |
| `gazeExpressionIds` | `string[]` | `[]` | Gaze Vector2 を OSC 送信する expressionId 一覧。空配列の場合、Gaze は送信されません。 |
| `suppressLoopback` | `bool` | `true` | 同一 child scope 内の OSC 受信 endpoint と一致する送信先を抑制します。 |
| `heartbeatIntervalSeconds` | `number` | `5.0` | BlendShape 名一覧 heartbeat の送信周期（秒）。DTO では `0` 以下または `NaN` を `5.0` に補完し、binding 実行時は `0.5` から `60.0` 秒にクランプされます。 |

## endpoint フィールド

| フィールド | 型 | 既定値 | 説明 |
| --- | --- | --- | --- |
| `ip` | `string` | `"127.0.0.1"` | UDP 送信先 IP またはホスト名。空文字または空白は既定値に補完されます。 |
| `port` | `integer` | `9000` | UDP 送信先ポート。`1` から `65535` 以外は既定値に補完されます。 |
| `preset` | `string` | `"vrchat"` | アドレスプリセット。`"vrchat"` または `"arkit"` を指定します。大文字小文字は区別されず、不明な値は `"vrchat"` に補完されます。 |
| `enabled` | `bool` | `true` | `false` の endpoint は送信スロットから除外されます。 |

## プリセット enum

| JSON 値 | runtime enum | BlendShape アドレス | Gaze 送信 |
| --- | --- | --- | --- |
| `"vrchat"` | `AddressPresetKind.VRChat` | `/avatar/parameters/{name}` | `/avatar/parameters/{expressionId}X` と `/avatar/parameters/{expressionId}Y` に Gaze Vector2 の X/Y を送ります。 |
| `"arkit"` | `AddressPresetKind.ARKit` | `/ARKit/{name}` | `gazeExpressionIds` の Gaze Vector2 を PerfectSync/ARKit eyeLook 8 BlendShape に分解し、`/ARKit/eyeLookInLeft` などの固定 8 アドレスへ送ります。 |

## Gaze 送信構成

`gazeExpressionIds` は Domain bus から publish される Gaze snapshot の `expressionId` を参照します。`"vrchat"` プリセットでは 1 つの expressionId につき X/Y の 2 メッセージを送信します。`"arkit"` プリセットでは 1 つの expressionId につき次の 8 BlendShape を送信します。

| eyeLook 名 | OSC アドレス |
| --- | --- |
| `eyeLookInLeft` | `/ARKit/eyeLookInLeft` |
| `eyeLookOutLeft` | `/ARKit/eyeLookOutLeft` |
| `eyeLookUpLeft` | `/ARKit/eyeLookUpLeft` |
| `eyeLookDownLeft` | `/ARKit/eyeLookDownLeft` |
| `eyeLookInRight` | `/ARKit/eyeLookInRight` |
| `eyeLookOutRight` | `/ARKit/eyeLookOutRight` |
| `eyeLookUpRight` | `/ARKit/eyeLookUpRight` |
| `eyeLookDownRight` | `/ARKit/eyeLookDownRight` |

## heartbeat とループバック抑制

送信側は起動時 1 回と `heartbeatIntervalSeconds` 周期で `/_facialcontrol/blendshape_names` に BlendShape 名一覧 heartbeat を送ります。各フレームの bundle には送信元識別用の `/_facialcontrol/sender_id` も同梱されます。`suppressLoopback = true` の場合、同一 child scope の受信側と同じ endpoint は送信対象から除外されます。

## サンプル JSON

```json
{
  "endpoints": [
    {
      "ip": "127.0.0.1",
      "port": 9000,
      "preset": "vrchat",
      "enabled": true
    },
    {
      "ip": "192.168.0.42",
      "port": 9012,
      "preset": "arkit",
      "enabled": true
    }
  ],
  "blendShapeMapping": [
    "Joy",
    "Blink_L",
    "Blink_R"
  ],
  "gazeExpressionIds": [
    "Look"
  ],
  "suppressLoopback": true,
  "heartbeatIntervalSeconds": 5.0
}
```
