# com.hidano.facialcontrol.rec

`com.hidano.facialcontrol.rec` は FacialControl の録画・再生機能を提供する UPM パッケージです。
現時点では spec `rec-recording-playback` に対応した構成の土台を提供します。`Runtime` の `Domain` / `Application` / `Adapters`、`Editor`、`Tests`、`Samples~`、`Documentation~` を含み、asmdef による責務分離を前提にしています。

## 前提バージョン
- Unity 6000.3 系
- `com.hidano.facialcontrol` 0.1.0-preview.2

本パッケージは `com.hidano.facialcontrol` のみに依存します。OSC / InputSystem / LipSync / iFacialMocap パッケージへの依存は持ちません。

## ディレクトリ構成

- `Runtime/Domain/`
- `Runtime/Application/`
- `Runtime/Adapters/`
- `Editor/`
- `Tests/EditMode/`
- `Tests/PlayMode/`
- `Tests/Shared/`
- `Samples~/`
- `Documentation~/`

## 補足
このパッケージではパッケージ構造と asmdef 配置を前提に、録画・再生の実装、サンプル、Inspector UI を段階的に追加します。

## 既知制限
- REC 再生開始後に新規登録された live の analog / gaze source は再生 takeover の対象外です。再生開始時点で registry に存在した source と baseline に含まれる source のみを置換します。
- `com.hidano.facialcontrol.timeline` の `TimelineExpressionStateSink` が発火する `TriggerOn` / `TriggerOff` も REC 再生中は抑止されます。REC 再生と Timeline 再生を同時に使う場合、trigger 系の live 更新は共存しません。
