# com.hidano.facialcontrol.timeline

`com.hidano.facialcontrol.timeline` は FacialControl の Timeline 連携を提供する UPM パッケージです。
現段階では、独自 Track / Clip / mixer / ベイクツールを追加していくための雛形のみを含みます。

## 依存関係

- Unity 6000.3
- `com.hidano.facialcontrol` 0.1.0-preview.2
- `com.hidano.facialcontrol.rec` 0.1.0-preview.1
- `com.unity.timeline` 1.8.9

## ディレクトリ構成

- `Runtime/`
- `Editor/`
- `Tests/Shared/`
- `Tests/EditMode/`
- `Tests/PlayMode/`
- `Documentation~/`

## 補足

Runtime asmdef は core の Domain / Application / Adapters と `Unity.Timeline` のみに依存し、`rec` は参照しません。
`rec` への依存は package.json と Editor asmdef に閉じ込め、Timeline 依存を core / rec パッケージへ波及させない構成にしています。
