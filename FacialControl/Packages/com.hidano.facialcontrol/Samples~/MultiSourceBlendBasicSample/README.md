# MultiSourceBlend Basic Sample

`AdapterBindingBase` 派生の Mock binding 2 種を使い、core の MultiSourceBlend 処理だけを最小構成で確認するサンプルです。HUD と Scene は含めません。

## 含まれるもの

| ファイル | 役割 |
| --- | --- |
| `MockTriggerAdapterBinding.cs` | `[FacialAdapterBinding(displayName: "Mock Trigger")]` 付きの trigger 系 Mock binding |
| `MockAnalogAdapterBinding.cs` | `[FacialAdapterBinding(displayName: "Mock Analog")]` 付きの analog 系 Mock binding |
| `MultiSourceBlendBasicRunner.cs` | `Tools > FacialControl > Run MultiSourceBlend Basic Sample` から実行する最小 Runner |
| `multi_source_blend_basic.json` | slug 形式の `inputSources` を持つサンプル profile |

## 実行手順

1. Package Manager から `MultiSourceBlend Basic Sample` を Import します。
2. Unity Editor の `Tools > FacialControl > Run MultiSourceBlend Basic Sample` を実行します。
3. Console に discovery 結果、登録された slug、`Blink` / `Smile` / `MouthOpen` の出力値が表示されます。

## 注意

この Mock binding はサンプル専用です。テスト用 Mock とは独立しており、どちらかを変更した場合は用途の差分を確認してください。
