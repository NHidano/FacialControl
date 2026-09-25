# Changelog

すべての変更は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従います。

## 初回リリース

本パッケージはこれが初回リリースです。

### Changed

- `PhonemeEntryListView` の音素エントリ一覧に交互背景（`AlternatingRowBackground.ContentOnly`）を付け、複数フィールドで構成される各行の境界を視認しやすくした。あわせて一覧ヘッダー Foldout の開閉状態を `SessionState` に保存し、Inspector 再構築（domain reload / asset 再読み込み）後も直前の展開状態を復元するようにした（Editor 再起動時はリセット）。
- `PhonemeEntryListView` の音素エントリ `ListView` を固定行高（132px）から `DynamicHeight` 仮想化に変更し、エントリ形式（BlendShape / AnimationClip / Expression）によって短い行の下に空白が残り縦に間延びする問題を解消した。あわせて `minHeight`（96px）を撤去し、一覧を折りたたんだ際に下部へ無駄な空白が残る問題も解消した。

### Fixed

- Expression の phoneme Override / Suppress が実経路で機能しない不具合を修正し、Override のセマンティクスを「音素の口形状 snapshot の差し替え」として実装した。従来は (1) `LayerInputSourceAggregator` が加重和のみで preemption を持たず Override が既定出力へ加算されて飽和・Suppress は素通し、(2) `OverlayInputSource`（`overlay:{slot}`）の静的出力により表情中は override snapshot が音声と無関係に常時 100% 出力される、という二重の問題があった。修正後は `LipSyncPhonemeOverlayInputSource` が active 表情の phoneme binding / DefaultOverlays を解決し、Override 中は override snapshot を既定 snapshot の代わりに**同じ駆動 weight（音素 weight × 音量）**で合成する（無音時は出力なし）。Suppress 中は当該 slot の出力を停止する。precedence は Expression Override → Suppress → DefaultOverlays → LipSync default。予約音素 slot（a/i/u/e/o）の `OverlayInputSource` は静的出力を廃止（常に無効ソース。`overlay:{slot}` のレイヤー宣言は互換のため残置可）。suppress=false かつ空 snapshot の binding（Inspector 未設定のまま出力されたもの）は default fallback 扱い。実経路（LayerUseCase + Aggregator）を通す `PhonemeOverlayPreemptionTests` と差し替え・駆動 weight 検証の `PhonemeOverlayIntegrationTests` で固定。
- リップシンクデバイス未選択（初回起動等で保存済みデバイスが無い）だと `ULipSyncAdapterBinding` の初期化がデバイス解決エラーで中断し、phoneme overlay 入力源（`lipsync-overlay:a`〜`o`）が一切登録されずリップシンクが動かない不具合を修正。デバイス名未指定の場合は既定のマイク（マイク一覧の先頭）へフォールバックして初期化を継続する（ASIO は明示選択時のみ使用）。フォールバック発動時はどのマイクを使用したかを `Debug.Log` で通知する。マイクが 1 台も無い場合は従来どおり未解決エラーとなる。
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
