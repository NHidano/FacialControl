# FacialControl iFacialMocap

`com.hidano.facialcontrol` の iFacialMocap (iOS) 受信アダプタ。

## 概要

このパッケージは FacialControl コアに iFacialMocap の受信機能を追加します。iFacialMocap が UDP で配信する独自テキストプロトコル（OSC ではない）を解析し、ARKit 互換 BlendShape・視線・頭部ポーズを `FacialController` に流し込みます。

- **IFacialMocapReceiverAdapterBinding**: `FacialCharacterProfileSO` の Adapter Bindings に追加する受信 binding。BlendShape（iFM 名 → メッシュ BlendShape 名へ変換）、視線（`rightEye` / `leftEye` のオイラー角 → Gaze Vector2）、頭部（`head` の回転/移動 → N 軸 analog 入力源）を登録します。
- **IFacialMocapReceiverHost**: binding が `AddComponent` する helper MonoBehaviour。受信スレッドで UDP を listen し、必要ならハンドシェイクを送信してストリームを起動、最新フレームを保持します。
- **値パイプラインの再利用**: BlendShape は `com.hidano.facialcontrol.osc` の `OscDoubleBuffer` / `OscInputSource`、視線は `GazeVector2InputSource` を再利用します。iFM 固有なのは UDP 受信とテキストパースだけです。

## 主な機能

- iFacialMocap UDP テキストプロトコル受信（標準 `-` 区切り / v2 `&` 区切り 両対応）
- ハンドシェイク送信（端末 `IP:49983` へトリガー文字列を送り、60fps ストリームを起動）
- ARKit 互換 52 BlendShape（iFM の `_L`/`_R` サフィックス名 → 任意のメッシュ BlendShape 名へ変換表でマッピング）
- 視線: `rightEye` / `leftEye` のオイラー角 → 正規化 Vector2（左右独立。Profile の `GazeBindingConfig` で目ボーンへ）
- 頭部: `head` の回転（任意で移動）→ N 軸 analog 入力源（Profile の `AnalogBindingEntry` の BonePose で頭ボーンへ）
- staleness fail-safe（受信停止時に base へ復帰）

## 依存パッケージ

| パッケージ | バージョン | 用途 |
|---|---|---|
| `com.hidano.facialcontrol` | 0.1.0-preview.30 以降 | コア機能、AdapterBinding、Gaze / Bone binding |
| `com.hidano.facialcontrol.osc` | 0.1.0-preview.7 以降 | `OscDoubleBuffer` / `OscInputSource` / `GazeVector2InputSource` の再利用 |

> 補足: 本パッケージは OSC を使いませんが、受信値パイプラインの実装を共有するため `.osc` パッケージへ依存します（uOSC への依存はトランスポート層に閉じており、本パッケージからは利用しません）。

## 使い方

1. `com.hidano.facialcontrol`、`com.hidano.facialcontrol.osc`、本パッケージを `Packages/manifest.json` に追加
2. キャラクターの GameObject に `FacialController` を追加し、`FacialCharacterProfileSO` を結線
3. **Adapter Bindings** セクションで `iFacialMocap Receiver` を追加し、listen port（既定 49983）、端末 IP / ハンドシェイク、BlendShape マッピング、視線 / 頭部の出力を設定
4. 視線を目ボーンに反映する場合は Profile の `GazeBindingConfig` で本 binding の Gaze source id（`<slug>:gaze.left` / `<slug>:gaze.right`）を結線
5. 頭部を頭ボーンに反映する場合は Profile の `AnalogBindingEntry`（TargetKind=BonePose）で本 binding の Head source id（`<slug>:head`）を頭ボーンへ結線
6. Package Manager の **Import Sample** から `IFacialMocapReceiverDemo` を import すると最小の受信構成を確認できます

## iFacialMocap プロトコル

- ハンドシェイク: PC から端末 `49983` へ `iFacialMocap_sahuasouryya9218sauhuiayeta91555dy3719` を送信 → 端末が PC `49983` へ 60fps で UDP 返信
- パケット: `name-value|...|=head#X,Y,Z,posX,posY,posZ|rightEye#X,Y,Z|leftEye#X,Y,Z|`（BlendShape 値域 0〜100、角度は度）
- v2: トリガーに `|sendDataVersion=v2` を付与すると name-value 区切りが `-` → `&`（負値対応）

参考: <https://www.ifacialmocap.com/for-developer/>

## JSON リファレンス

- [IFacialMocapOptions JSON スキーマ](Documentation~/ifacialmocap-options.md)
- [使い方ガイド](Documentation~/usage.md)

## ライセンス

[MIT License](../com.hidano.facialcontrol/LICENSE.md)
