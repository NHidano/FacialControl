# com.hidano.facialcontrol.rec

`com.hidano.facialcontrol.rec` は FacialControl の記録・再生機能用 UPM パッケージです。

現時点では仕様書 `rec-recording-playback` に沿った骨格のみを提供します。Runtime の `Domain` / `Application` / `Adapters`、`Editor`、`Tests`、`Samples~`、`Documentation~` を分離し、asmdef で依存方向を固定しています。

## 依存関係

- Unity 6000.3 以降
- `com.hidano.facialcontrol` 0.1.0-preview.2

本パッケージは `com.hidano.facialcontrol` のみに依存し、OSC / InputSystem / LipSync / iFacialMocap パッケージへの依存は持ちません。

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

## 備考

このコミットではパッケージ骨格と asmdef 構成のみを追加しています。記録・再生の実装本体、サンプル、Inspector UI は後続タスクで追加します。
