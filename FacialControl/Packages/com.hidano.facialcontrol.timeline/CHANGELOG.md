# Changelog

## [0.1.0-preview.1] - 2026-07-16

- `com.hidano.facialcontrol.timeline` パッケージの雛形を追加
- Runtime / Editor / Tests / Documentation~ の初期構成を追加
- Timeline 依存を新規パッケージへ局所化する asmdef と package 依存を定義

### ⚠ BREAKING CHANGES — gaze-channel-redesign

- Timeline の gaze takeover と rec export の判定を Profile の `GazeChannels` と `GazeSourceIdConvention` に統一しました。旧 expressionId / 手書き source id はチャネル id と現在の binding 宣言へ置き換えてください。
- Timeline binding は `IGazeSourceProvider` としてチャネル source を宣言します。旧 gaze 設定の直接注入に依存する拡張は更新が必要です。
- 詳細は core の [`migration-guide.md`](../com.hidano.facialcontrol/Documentation~/migration-guide.md) を参照してください。
