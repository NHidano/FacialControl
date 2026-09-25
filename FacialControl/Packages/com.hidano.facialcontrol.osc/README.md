# FacialControl OSC

`com.hidano.facialcontrol` の OSC（VRChat / ARKit 互換）送受信アダプタ。

## 概要

このパッケージは FacialControl コアに OSC 入出力機能を追加します。

- **OscReceiverAdapterBinding**: `FacialCharacterProfileSO` の Adapter Bindings に追加する受信 binding。BlendShape、VRChat 形式 Gaze X/Y、ARKit / PerfectSync 互換 8 BlendShape Gaze を mode 別 `OscMappingEntry` で受信します。
- **OscSenderAdapterBinding**: `FacialOutputBus` から post-blend BlendShape と Gaze Vector2 を購読し、VRChat / ARKit 互換 OSC bundle として複数 endpoint へ送信します。
- **OscReceiverHost / OscSender**: binding 配下で `AddComponent` される helper MonoBehaviour。uOSC サーバー / クライアントをラップし、受信データを `OscDoubleBuffer` と Gaze source へ流します。
- **JSON DTO / Drawer**: `OscSenderOptionsDto` / `OscReceiverOptionsDto` と UI Toolkit Drawer により、Scene 内設定と JSON サンプルを同じ構造で管理できます。

## 主な機能

- VRChat 互換: `/avatar/parameters/{name}` の BlendShape と `{expressionId}X` / `{expressionId}Y` の Gaze Vector2 送受信
- ARKit / PerfectSync 互換: `/ARKit/{name}` の BlendShape と `eyeLook...` 8 BlendShape 形式の Gaze 送受信
- OSC bundle による 1 フレーム単位の送信と、受信側の atomic swap / individual message 切替
- 複数 endpoint 同報、heartbeat、sender identity、同一プロセス内 loopback 抑制
- staleness fail-safe、ゾンビ sender 排除、heartbeat の BlendShape 名一覧による整合性検査

## 破壊的変更

preview.2 では `OscReceiverAdapterBinding` の SerializedField 構造を破壊的に変更しました。旧 BlendShape 専用 mapping Asset は自動 migration されないため、Inspector で `OscMappingEntry` の mode を選び直し、BlendShape / `Gaze_VRChat_XY` / `Gaze_ARKit_8BS` の各 entry を再作成してください。

## 依存パッケージ

| パッケージ | バージョン | 用途 |
|---|---|---|
| `com.hidano.facialcontrol` | 0.1.0-preview.2 以降 | コア機能、`FacialOutputBus`、Gaze binding |
| `com.hidano.uosc` | 1.0.0 | OSC（UDP）通信 |

## 使い方

1. `com.hidano.facialcontrol` と本パッケージを `Packages/manifest.json` に追加
2. キャラクターの GameObject に `FacialController` を追加し、`FacialCharacterProfileSO` を結線
3. 受信する場合は **Adapter Bindings** セクションで `OSC` を追加し、listen endpoint と mapping mode を設定
4. 送信する場合は `OSC Sender` を追加し、送信先 endpoint、BlendShape 名一覧、Gaze expressionId を設定
5. Package Manager の **Import Sample** から `OscOutputDemo` / `OscReceiverDemo` を import すると、送信側と受信側の最小 Scene を確認できます。

## サンプル

| Sample | 内容 |
|---|---|
| `OscOutputDemo` | `OscSenderAdapterBinding` で BlendShape と `eye_look` Gaze Vector2 を VRChat / ARKit endpoint へ送信するサンプル |
| `OscReceiverDemo` | `OscReceiverAdapterBinding` で VRChat 形式の BlendShape と Gaze Vector2 を受信し、手続き生成メッシュへ反映するサンプル |

サンプルの canonical 配置は `Samples~/` です。開発プロジェクト用ミラーは `FacialControl/Assets/Samples/` に置いています。

## JSON リファレンス

- [OscSenderOptions JSON スキーマ](Documentation~/osc-sender-options.md)
- [OscReceiverOptions JSON スキーマ](Documentation~/osc-receiver-options.md)

## ライセンス

[MIT License](../com.hidano.facialcontrol/LICENSE.md)
