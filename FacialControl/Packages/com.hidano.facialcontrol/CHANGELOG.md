# Changelog

すべての変更は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従います。

## [0.1.0-preview.1] - Unreleased

初回プレリリース。

### Added

#### Domain 層
- `FacialProfile`、`Expression`、`BlendShapeMapping`、`LayerDefinition`、`LayerSlot` ドメインモデル
- `ExclusionMode`（LastWins / Blend）、`TransitionCurveType` enum
- `TransitionCurve` 構造体（Linear / EaseIn / EaseOut / EaseInOut / Custom）
- `TransitionCalculator` — 遷移カーブ評価サービス
- `ExclusionResolver` — LastWins クロスフェード / Blend 加算排他ロジック
- `LayerBlender` — レイヤー優先度ベースのウェイトブレンドと layerSlots オーバーライド
- `ARKitDetector` — ARKit 52 / PerfectSync の完全一致検出とレイヤーグルーピング
- `IJsonParser`、`IProfileRepository`、`ILipSyncProvider`、`IBlinkTrigger` インターフェース
- `FacialControlConfig`、`FacialState`、`FacialOutputData` 構造体

#### Application 層
- `ProfileUseCase` — プロファイル読み込み・再読み込み・Expression 取得
- `ExpressionUseCase` — Expression のアクティブ化・非アクティブ化
- `LayerUseCase` — レイヤーウェイト更新とブレンド出力計算
- `ARKitUseCase` — ARKit / PerfectSync 検出と Expression・OSC マッピング自動生成

#### Adapters 層
- `SystemTextJsonParser` — System.Text.Json ベースの JSON パース / シリアライズ
- `FileProfileRepository` — ファイルシステムからのプロファイル読み書き
- `NativeArrayPool` — GC フリーの NativeArray プール管理
- `AnimationClipCache` — LRU 方式の AnimationClip キャッシュ
- `PropertyStreamHandleCache` — BlendShape → PropertyStreamHandle キャッシュ
- `LayerPlayable`（ScriptPlayable）— NativeArray ベースの補間計算と排他モード処理
- `FacialControlMixer`（ScriptPlayable）— レイヤーウェイトブレンドと最終出力統合
- `PlayableGraphBuilder` — FacialProfile からの PlayableGraph 構築
- `OscDoubleBuffer` — ロックフリーのダブルバッファリング
- `OscReceiver` / `OscSender` — uOsc ベースの OSC 送受信
- `OscReceiverPlayable` — PlayableGraph への OSC 受信統合
- `OscMappingTable` — OSC アドレスと BlendShape のマッピング管理
- `FacialProfileSO`（ScriptableObject）— JSON への参照ポインター
- `FacialProfileMapper` — FacialProfile ⟷ FacialProfileSO 変換
- `FacialController`（MonoBehaviour）— メインコンポーネント（Activate / Deactivate / LoadProfile / ReloadProfile）
- `InputSystemAdapter` — InputAction Asset との連携（Button / Value 両対応）
- デフォルト InputAction Asset

#### Editor 拡張
- `FacialControllerEditor` — FacialController の Inspector カスタマイズ
- `FacialProfileSOEditor` — FacialProfileSO の Inspector カスタマイズ
- UI Toolkit スタイル共通定義
- `ProfileManagerWindow` — Expression の一覧表示・検索・CRUD・Undo 連動
- JSON インポート / エクスポート機能
- `ExpressionCreatorWindow` — BlendShape スライダーでリアルタイムプレビューしながら Expression 作成
- `PreviewRenderUtility` ラッパー（カメラ / ライティング / RenderTexture 管理）
- `ARKitDetectorWindow` — ARKit / PerfectSync 自動検出 Editor UI

#### テンプレート
- `default_profile.json` — デフォルト 3 レイヤー + 基本 Expression（default, blink, gaze_follow, gaze_camera）
- `default_config.json` — VRChat プリセットの OSC 設定
- デフォルト InputAction Asset

#### ドキュメント
- 全公開 API の XML コメント
- クイックスタートガイド（`Documentation~/quickstart.md`）
- JSON スキーマリファレンス（`Documentation~/json-schema.md`）
