# Changelog

すべての変更は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従います。

## 初回リリース

本パッケージはこれが初回リリースです。

### Fixed

- OSC 受信中に数分に 1 回程度の頻度で表情が一瞬素の状態に戻る（例: 笑顔の目閉じが 1 tick だけ開く）不具合を修正しました。`OscDoubleBuffer.Swap()` が write buffer をゼロクリアしていたため、bundle の UDP パケット分断（accumulation timeout 超過）・パケットロス・受信の無い tick を挟んだ瞬間に、その frame へ含まれなかった BlendShape が 0 として読者に観測されていました。`LayerInputSourceWeightBuffer.SwapIfDirty` と同じ copy-forward 方式（swap 後に新 read buffer の内容を新 write buffer へ複製）に変更し、未受信 index は前回値を保持するようにしました。受信停止時のゼロ化は従来どおり `OscInputSource` の staleness + `FailSafeMode` が担います。あわせて `Swap()` を `Write()`（受信側スレッド）と同一 lock で排他し、swap 中の書込ロストを防ぎました。

### Changed

- `OscReceiverAdapterBindingDrawer` の Mappings 一覧に交互背景（`AlternatingRowBackground.ContentOnly`）を付け、複数フィールドで構成される各行の境界を視認しやすくした。あわせて一覧ヘッダー Foldout の開閉状態を `SessionState` に保存し、Inspector 再構築（domain reload / asset 再読み込み）後も直前の展開状態を復元するようにした（Editor 再起動時はリセット）。
- `OscReceiverAdapterBindingDrawer` の Mappings `ListView` を固定行高（236px）から `DynamicHeight` 仮想化に変更し、mode によって非表示になるフィールド分の空白で行が縦に間延びする問題を解消した。あわせて `minHeight`（120px）を撤去し、一覧を折りたたんだ際に下部へ無駄な空白が残る問題も解消した。

### Breaking changes

- `OscAdapterBinding` を `OscReceiverAdapterBinding` にリネームしました。Inspector の Add ドロップダウン display name も `"OSC"` から `"OSC Receiver"` に変更し、送信側 `OscSenderAdapterBinding` (`"OSC Sender"`) との対称性を確保しました。既存 Profile / Scene asset の `RefIds` 内 `class: OscAdapterBinding` 指定は自動 migration を提供しないため、新クラス名で再アサインしてください。
- `OscSenderHost` MonoBehaviour を廃止し、`OscSender` に統合しました。AdapterBinding 経路は `OscSender.Configure(endpoint, port, mappings[, addressUtf8])` で起動し、`OscSender.OnDestroy` が同 GameObject 上の `uOscClient` を破棄します。
- `OscSenderAdapterBinding` の public API `HelperHost` / `HelperHostCount` / `GetHelperHost(int)` を `HelperSender` / `HelperSenderCount` / `GetHelperSender(int)` にリネーム。戻り型も `OscSenderHost` から `OscSender` に変わります。
- `OscSender.Address` プロパティを `OscSender.Endpoint` にリネームしました。SerializeField の `_address` も `_endpoint` に変更したため、Inspector で直接 `OscSender` を Add していたケースでは既存シリアライズ値が失われます (AdapterBinding 経路では影響なし)。
- `OscReceiverAdapterBinding` の受信 mapping を BlendShape 専用の旧構造から、`OscMappingEntry` / `OscMappingMode` による mode 別 entry 構造へ破壊的に変更しました。既存の `OscReceiverAdapterBinding` Asset / Scene は自動 migration を提供しないため、新しい `_mappings` に再設定してください。
- Gaze 受信を `OscReceiverAdapterBinding` に統合し、`Gaze_VRChat_XY` と `Gaze_ARKit_8BS` の mode を追加しました。ARKit 8 BlendShape mode では `addressPattern` を無視し、固定の `/ARKit/eyeLook...` 8 アドレスから左右別 Vector2 を復元します。

### Added

- `OscReceiver` の受信ポートが使用中の場合、空きポートへ自動繰り上げして待ち受けるようにしました（例: 9001 使用中なら 9002）。繰り上げ時は警告ログで実際の待ち受けポートを通知します。uOSC は SO_REUSEADDR 付きで bind するため従来はポート衝突が無警告で受信不能になっていましたが、ビルド済みアプリでも衝突に気づけるようになります。空き判定・繰り上げ解決を担う `OscPortResolver` と、実際の待ち受けポートを返す `OscReceiver.ActivePort` を追加しました。
- `OscSenderAdapterBinding` を追加し、`FacialOutputBus` から post-blend BlendShape と Gaze Vector2 を購読して OSC 送信できるようにしました。
- 複数 endpoint 同報、VRChat / ARKit アドレスプリセット、OSC bundle フレーム送信、BlendShape 名 heartbeat、sender identity、同一プロセス内 loopback 抑制を追加しました。
- `OscReceiverAdapterBinding` に bundle atomic swap、staleness fail-safe、sender identity によるゾンビ排除、heartbeat 整合性検査、Gaze Vector2 受信 source 登録を追加しました。
- `OscSenderOptionsDto` / `OscSenderEndpointDto` / `OscReceiverOptionsDto` / `OscMappingEntryDto` と JSON スキーマドキュメントを追加しました。
- UI Toolkit ベースの `OscSenderAdapterBindingDrawer` と `OscReceiverAdapterBindingDrawer` を追加し、送受信設定と mode 別 mapping を Inspector から編集できるようにしました。
- Package Manager の Import Sample から利用できる `OscOutputDemo` と `OscReceiverDemo` を追加しました。

### Documentation

- README に OSC 送信、受信拡張、Gaze Vector2 受信、破壊的変更、サンプル導線を追記しました。
- `Documentation~/osc-sender-options.md` と `Documentation~/osc-receiver-options.md` に JSON DTO の canonical field とサンプルを記載しました。

初回リリースで `com.hidano.facialcontrol` に OSC 受信アダプタを追加し、VRChat / ARKit 互換 OSC で BlendShape を受信できる構成として提供します。

### Added

- `OscReceiverAdapterBinding`、`ArKitOscAdapterBinding`、`OscReceiverHost`、`OscSender`、`OscInputSource`、`OscDoubleBuffer`、`OscMappingTable` を追加しました。
- uOSC ベースの UDP 送受信経路と、`FacialCharacterProfileSO` の Adapter Bindings から利用する最小構成を追加しました。
