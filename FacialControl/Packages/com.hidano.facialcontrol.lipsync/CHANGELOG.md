# Changelog

すべての変更は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従います。

## 初回リリース

本パッケージはこれが初回リリースです。

### Changed

- `PhonemeEntryListView` の音素エントリ一覧に交互背景（`AlternatingRowBackground.ContentOnly`）を付け、複数フィールドで構成される各行の境界を視認しやすくした。あわせて一覧ヘッダー Foldout の開閉状態を `SessionState` に保存し、Inspector 再構築（domain reload / asset 再読み込み）後も直前の展開状態を復元するようにした（Editor 再起動時はリセット）。
- `PhonemeEntryListView` の音素エントリ `ListView` を固定行高（132px）から `DynamicHeight` 仮想化に変更し、エントリ形式（BlendShape / AnimationClip / Expression）によって短い行の下に空白が残り縦に間延びする問題を解消した。あわせて `minHeight`（96px）を撤去し、一覧を折りたたんだ際に下部へ無駄な空白が残る問題も解消した。

### Fixed

- phoneme overlay 入力源が解決されず口が動かない不具合を修正。`ULipSyncAdapterBinding` は overlay 入力源を binding の `Slug`（既定 `ulipsync`）で登録していた（キー `ulipsync:a`）が、レイヤーの入力源 id・`GetDefaultLayerInputSources`・サンプル・docs はすべて固定 prefix `lipsync-overlay:{slot}` を使うため、`FacialController` のレイヤー解決（`TryResolve("lipsync-overlay:a")`）がヒットせず集約に乗らなかった。登録/解除/重複検知を固定 prefix `lipsync-overlay` 基準に統一し、レイヤー id と一致させた。
- マイク未接続時にノイズを拾って口が開くことがある不具合を修正。`ULipSyncProvider` の音量正規化を `rawVolume` の自前再正規化から uLipSync 本体が正規化済みの `LipSyncInfo.volume` 直結へ戻した。`rawVolume` を調整可能な `Min Volume`/`Max Volume` で再正規化する実装は、`Min Volume` を下げるほどノイズフロアを増幅してしまい、未接続・無音時の瞬間的なノイズで口が開いていた。音量正規化は uLipSync 本体の責務に委ね、FacialControl 側では再加工しない。これに伴い `ULipSyncAdapterBinding` の `Min Volume`/`Max Volume` 設定（Inspector 含む）と `ULipSyncProvider` の `minVolume`/`maxVolume` コンストラクタ引数を削除。小さい声・低ゲインで口が動かない場合は uLipSync 側（マイク gain / `uLipSyncMicrophone` / Profile）で調整する。

初回リリースで `com.hidano.facialcontrol` と `com.hidano.ulipsync-asio` を接続する Windows 向け uLipSync 連携アダプタとして提供します。

### Added

- `com.hidano.facialcontrol.lipsync` の UPM パッケージ足場を追加し、`package.json`、標準 UPM ディレクトリ、Runtime / Editor / Tests asmdef、README、CHANGELOG、LICENSE を配置。
- `ULipSyncAdapterBinding` を追加し、Character Prefab に uLipSync 系コンポーネントを事前付与せず、再生時に `AudioSource`、`uLipSync.uLipSync`、Mic / ASIO 入力コンポーネントを動的に構築できるようにした。
- uLipSync の `LipSyncInfo.phonemeRatios` と `volume` を FacialControl の `lipsync` 入力ソースへ変換する `ULipSyncProvider` と音素エントリを追加。
- Mic / ASIO デバイス名の自動判定と `DeviceDescriptor` による同名デバイスの識別に対応。
- 実行中に入力デバイスを切り替える hot-swap API を追加し、切替時にゼロ値 settle を挟んで既存の Provider / InputSource 登録を維持できるようにした。
- 複数キャラクターがそれぞれ独立した `ULipSyncAdapterBinding` とデバイス設定を保持できる構成を追加。
- `ULipSyncProvider` のイベント受信、スナップショット蓄積、`GetLipSyncValues` のホットパスで GC アロケーション 0 byte を維持する方針と検証テストを追加。
- `ULipSyncAdapterBinding` 用の UI Toolkit PropertyDrawer を追加し、デバイス設定、Analyzer Profile、BlendShape / AnimationClip 形式の音素エントリを Inspector から編集できるようにした。
- `MicLipSyncDemo` sample を追加し、Package Manager から Import してマイク入力の最小構成を確認できるようにした。
- `Runtime/Resources/FacialControl/LipSync/Default uLipSync Profile.asset` を同梱し、`ULipSyncAdapterBinding._analyzerProfile` 未指定時に `Resources.Load` 経由でフォールバックされるようにした。
