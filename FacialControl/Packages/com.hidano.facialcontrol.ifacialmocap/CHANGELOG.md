# Changelog

すべての変更は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従います。

## [0.1.0-preview.1] - Unreleased

初回プレリリース。`com.hidano.facialcontrol` に iFacialMocap (iOS) 受信アダプタを追加しました。

### Added

- `IFacialMocapReceiverAdapterBinding` を追加し、iFacialMocap の UDP テキストプロトコル（標準 `-` / v2 `&` 両対応）から BlendShape・視線・頭部ポーズを受信できるようにしました。
- `IFacialMocapReceiverHost` を追加し、受信スレッドでの UDP listen とハンドシェイク送信、最新フレーム保持を行います。
- `IFacialMocapPacketParser` / `IFacialMocapBlendShapeCatalog` / `EyeGazeConverter` など Unity 非依存のプロトコル層を追加しました。
- BlendShape 値パイプラインは `com.hidano.facialcontrol.osc` の `OscDoubleBuffer` / `OscInputSource`、視線は `GazeVector2InputSource` を再利用し、頭部は N 軸 `AnalogAxesInputSource` で公開します。
- `IFacialMocapRuntimeSettingsSO` / `IFacialMocapOptionsDto` と JSON ラウンドトリップ、UI Toolkit ベースの `IFacialMocapReceiverAdapterBindingDrawer` を追加しました。
- Package Manager の Import Sample から利用できる `IFacialMocapReceiverDemo` を追加しました。

### Changed

- `package.json` の `name` を `jp.co.com.hidano.facialcontrol.ifacialmocap` から `com.hidano.facialcontrol.ifacialmocap` に修正しました（他パッケージと同じ `com.hidano.facialcontrol.*` 体系に揃えるため。未公開のため利用者への影響はありません）。`packages-lock.json` のキーも併せて更新しています。
- 自前の gaze 目ボーン適用を撤去し、core `FacialController` の集約適用へ移行しました。`IFacialMocapReceiverAdapterBinding` は `GazeBonePoseProvider` を構築・駆動せず、視線入力源（`<slug>:gaze.left` / `<slug>:gaze.right`）の registry 登録までを担います。あわせて FacialController からの gaze 結線フック `Configure(IReadOnlyList<GazeBindingConfig>)` を削除しました（目ボーン適用が binding 外へ移ったため不要）。`GazeBindingConfig` での結線手順（README 記載）は変わりません。

### Documentation

- README に iFacialMocap プロトコル、受信設定、視線 / 頭部の結線手順、サンプル導線を記載しました。
- `Documentation~/ifacialmocap-options.md` と `Documentation~/usage.md` を追加しました。
