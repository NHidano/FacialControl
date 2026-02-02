# FacialControl 技術仕様書

> **バージョン**: 1.0.0
> **最終更新**: 2026-02-02
> **ステータス**: レビュー待ち
> **対象リリース**: preview.1

---

## 1. preview.1 スコープ

### 含まれる機能

| 機能 | 説明 |
|------|------|
| コア（プロファイル + レイヤー + 遷移） | 表情プロファイル管理、マルチレイヤー制御、表情遷移・補間 |
| OSC 送受信 | uOsc ベースの UDP 通信。VRChat + ARKit アドレスプリセット |
| ARKit 52 / PerfectSync | 手動トリガーによる BlendShape スキャン + プロファイル自動生成 |
| Editor 拡張 | Inspector カスタマイズ、プロファイル管理ウィンドウ、AnimationClip 作成支援、JSON インポート / エクスポート |
| ドキュメント | API リファレンス、クイックスタートガイド、JSON スキーマドキュメント |

### preview.2 以降に延期

| 機能 | 理由 |
|------|------|
| 自動まばたき | IBlinkTrigger インターフェースは定義するが、実装は延期 |
| 視線制御（視線追従 / カメラ目線） | Vector3 ターゲット + BlendShape / ボーン両対応の設計は行うが、実装は延期 |
| VRM 対応 | リリース後の早期マイルストーン |
| Timeline 統合 | Animator ベースのリアルタイム制御を優先 |

---

## 2. プロファイル構造

### 2.1 基本構造

```
ExpressionProfile
├── id: string                      // 一意識別子
├── name: string                    // 表示名
├── schemaVersion: string           // JSONスキーマバージョン（例: "1.0"）
├── layer: string                   // 所属レイヤー名（固定スロット名）
├── exclusionMode: ExclusionMode    // 排他モード（LastWins / Blend）
├── transitionDuration: float       // 遷移時間（0〜1秒、デフォルト0.25秒）
├── transitionCurve: TransitionCurve // 遷移カーブ設定
├── baseClip: AnimationClipRef      // 基本表情用 AnimationClip 参照
├── layerSlots: Dictionary<string, AnimationClipRef>  // レイヤー別スロット（固定スロット名）
│   ├── "emotion": AnimationClipRef
│   ├── "lipsync": AnimationClipRef
│   └── "eye": AnimationClipRef
└── blendShapeValues: BlendShapeMapping[]  // BlendShape 名と値のマッピング
```

### 2.2 レイヤー別スロット

- **固定スロット名**: デフォルト 3 レイヤー（`emotion`, `lipsync`, `eye`）に対応するスロットを辞書型で保持
- 基本表情用の AnimationClip 1 つにつき、その表情の時だけ上書きする各レイヤー用スロットを必要分だけ保持
- カスタムレイヤー追加時はスロットも追加が必要

### 2.3 カテゴリ属性

- **レイヤー指定のみ**: プロファイルは属するレイヤー名を 1 つだけ持つ
- タグやサブカテゴリは将来拡張

### 2.4 ランタイム出力データ

AnimationClip に依存せず、BlendShape 配列 + テクスチャ / UV アニメーション情報の複合構造体として出力:

```csharp
public struct FacialOutputData
{
    public NativeArray<float> BlendShapeWeights;
    public TextureSwapInfo[] TextureSwaps;      // 初期化時に確保
    public UVAnimationInfo[] UVAnimations;       // 初期化時に確保
}
```

---

## 3. レイヤーシステム

### 3.1 デフォルトレイヤー構成

| レイヤー | 優先度 | 用途 |
|----------|--------|------|
| emotion | 最低 | 感情ベースの表情 |
| lipsync | 中 | リップシンク |
| eye | 最高 | 目の表情 |

### 3.2 排他モード（プロファイル単位で設定）

#### 後勝ち（LastWins）
- 同じレイヤーに後勝ちプロファイル A と B がある場合
- B をアクティブにすると **A から B へクロスフェード**
- 設定された遷移時間で A のウェイトを 1→0、B のウェイトを 0→1 に同時遷移
- 遷移中に新しい表情がトリガーされた場合は、**現在の補間値から即座に新遷移を開始**

#### ブレンド（Blend）
- 同じレイヤーに複数のブレンドプロファイルがアクティブな場合
- **加算ブレンド（クランプ）**: 各プロファイルのウェイトを加算し、0〜1 にクランプ
- 例: A=0.6, B=0.5 → 合計 1.1 → クランプ 1.0

### 3.3 レイヤー優先度

- PlayableGraph の AnimationMixerPlayable でレイヤーウェイトを制御
- 高優先度レイヤーの値が低優先度レイヤーを上書き

---

## 4. 表情遷移

### 4.1 サポートする遷移カーブ（preview.1）

| 種類 | 説明 |
|------|------|
| 線形補間（Lerp） | デフォルト |
| EaseIn | 開始が緩やか |
| EaseOut | 終了が緩やか |
| EaseInOut | 開始と終了が緩やか |
| AnimationCurve | ユーザー定義のカスタムカーブ |

### 4.2 遷移パラメータ

- **遷移時間**: 0〜1 秒（デフォルト 0.25 秒）
- **遷移カーブ**: プリセット名 or カスタム AnimationCurve
- **遷移中の新トリガー**: 現在の補間値から即座に新遷移を開始

### 4.3 AnimationCurve の JSON シリアライズ

キーフレーム配列をそのまま JSON 化:

```json
{
  "transitionCurve": {
    "type": "custom",
    "keys": [
      {"time": 0.0, "value": 0.0, "inTangent": 0.0, "outTangent": 2.0, "inWeight": 0.0, "outWeight": 0.0, "weightedMode": 0},
      {"time": 1.0, "value": 1.0, "inTangent": 0.0, "outTangent": 0.0, "inWeight": 0.0, "outWeight": 0.0, "weightedMode": 0}
    ]
  }
}
```

プリセットイージングの場合:

```json
{
  "transitionCurve": {
    "type": "easeInOut"
  }
}
```

---

## 5. PlayableAPI アーキテクチャ

### 5.1 設計方針

- **全て PlayableAPI に統一**: BlendShape、テクスチャ切り替え、UV アニメーション全てを PlayableGraph で制御
- **1 Graph / キャラクター**: キャラクター 1 体につき 1 つの PlayableGraph
- **レイヤー分ノード**: Graph 内にデフォルト 3 レイヤー分の Playable ノードを配置

### 5.2 PlayableGraph 構成

```
PlayableGraph (per character)
├── AnimationPlayableOutput → Animator
├── AnimationLayerMixerPlayable (root mixer)
│   ├── Layer: emotion (weight: priority-based)
│   │   ├── AnimationMixerPlayable (profile mixer)
│   │   │   ├── AnimationClipPlayable (profile A)
│   │   │   └── AnimationClipPlayable (profile B)
│   │   └── ScriptPlayable<OscReceiverPlayable> (OSC input)
│   ├── Layer: lipsync (weight: priority-based)
│   │   ├── AnimationMixerPlayable (profile mixer)
│   │   └── ScriptPlayable<LipSyncPlayable> (external lipsync)
│   └── Layer: eye (weight: priority-based)
│       ├── AnimationMixerPlayable (profile mixer)
│       └── ScriptPlayable<BlinkPlayable> (auto-blink, preview.2)
└── ScriptPlayable<FacialControlMixer> (custom exclusion logic)
```

### 5.3 OSC 受信の Playable 統合

- **カスタム PlayableNode**: `ScriptPlayable<OscReceiverPlayable>` で OSC 受信値を処理
- OSC 受信値を NativeArray 経由でバッファに書き込み、Playable 内で BlendShape に適用
- **GC フリー**: NativeArray ベースのバッファで毎フレームのヒープ確保を回避

### 5.4 プロファイル切り替え

- **動的再構築**: プロファイル切り替え時に PlayableGraph のノードを再構築
- **GC 許容**: 切り替え時の GC は許容（毎フレームの GC はゼロ）
- 512 プロファイル分のノードを事前構築するのはメモリ過剰のため不採用

---

## 6. OSC 通信

### 6.1 基本設計

- **送受信両方**: preview.1 で送信・受信を両方実装
- **uOsc**: package.json の依存定義で参照（OpenUPM 登録確認が必要、未登録の場合はソースコード同梱にフォールバック）
- **UDP ポート**: デフォルト送信 9000、受信 9001（VRChat 標準）。Inspector / JSON で変更可能

### 6.2 アドレスパターン

| プリセット | 送信アドレス | 受信アドレス |
|-----------|-------------|-------------|
| VRChat | `/avatar/parameters/{name}` | `/avatar/parameters/{name}` |
| ARKit | `/ARKit/{blendShapeName}` | `/ARKit/{blendShapeName}` |

- preview.1 では VRChat と ARKit のプリセットを提供
- 完全カスタムアドレスは将来対応

### 6.3 通信仕様

- BlendShape 単位で個別 OSC メッセージ送受信
- 1 フレーム間に複数回送受信可能
- UDP 送受信はメインスレッド非依存

---

## 7. ARKit 52 / PerfectSync

### 7.1 自動検出

- **手動トリガー**: ユーザーが API 呼び出しまたは Editor ボタンで実行
- 自動的な検出タイミングは設けない（意図しないタイミングでの検出を避ける）

### 7.2 検出フロー

1. ユーザーが API / Editor ボタンで検出を実行
2. 対象モデルの SkinnedMeshRenderer から BlendShape 名をスキャン
3. ARKit 52 パラメータ名 / PerfectSync パラメータ名とマッチング
4. マッチしたパラメータからプロファイルを自動生成
5. 未対応パラメータは警告なしでスキップ

---

## 8. 入力システム

### 8.1 デフォルトバインディング

- **ボタン + 修飾キーの組み合わせ**: 各ボタン/キーにプロファイルをバインド
- **連続値入力**: ゲームパッドトリガー等のアナログ入力で**表情強度（ブレンドウェイト）を制御**
- InputAction の Button 型と Value 型の両方を使用

### 8.2 カスタマイズ

- InputAction Asset の差し替えによるカスタマイズ
- 入力インターフェースの抽象化により独自実装に差し替え可能

---

## 9. リップシンク

### 9.1 インターフェース

```csharp
public interface ILipSyncProvider
{
    /// <summary>
    /// リップシンクの BlendShape 値を取得する。
    /// 内部実装は固定長バッファで GC フリー。
    /// </summary>
    void GetLipSyncValues(Span<float> output);

    /// <summary>
    /// 対応する BlendShape 名の一覧を取得する。
    /// </summary>
    ReadOnlySpan<string> BlendShapeNames { get; }
}
```

- 外部プラグイン（uLipSync 等）がインターフェースを実装
- 内部実装は固定長バッファで GC フリー
- FacialControl はリップシンク用レイヤーに値を適用するのみ

---

## 10. 自動まばたき（preview.2 延期）

### 10.1 インターフェース定義（preview.1 で定義）

```csharp
public interface IBlinkTrigger
{
    /// <summary>
    /// まばたきをトリガーすべきかを判定する。
    /// </summary>
    bool ShouldBlink(float deltaTime, in FacialState currentState);
}
```

### 10.2 実装方針（preview.2）

- 単純なランダムではなく、人間のしぐさをベースにしたアルゴリズム
- 基本トリガー: ランダム間隔（3〜7 秒）+ 視線変更時
- 拡張可能: ユーザーが独自の IBlinkTrigger を実装可能
- OSC / ARKit からのまばたき入力があればそちらを優先（フォールバック機構）

---

## 11. 視線制御（preview.2 延期）

### 11.1 設計方針（preview.1 でインターフェース定義）

- **BlendShape + ボーン両方対応**: モデルの仕様に応じて選択
- **ターゲット指定**: Vector3 座標指定（Transform 参照は将来拡張）
- 「視線追従」と「カメラ目線」のデフォルトプロファイルはテンプレートとして同梱

---

## 12. JSON 設計

### 12.1 バージョニング

- JSON スキーマに `schemaVersion` フィールドを含める
- パーサーがバージョンを見てマイグレーション可能
- preview 間でもバージョン管理

### 12.2 ビルド後の JSON 配置

- **StreamingAssets**: ビルド後もファイルシステムから直接アクセス可能
- Android 将来対応時は StreamingAssets の読み取り専用制約に注意

### 12.3 ランタイム JSON 変換

- **初期化時に 1 回変換 + メモリキャッシュ**: アプリ起動時に JSON をパースし、ドメインモデルをメモリに保持
- ホットリロード時は再パースが必要

### 12.4 ScriptableObject との関係

- **SO は別構造（Unity 最適化）**: Inspector での表示に最適化した独自構造
- ドメインモデルと SO の間にマッパーを配置（Adapters 層）
- Editor では JSON ↔ SO の相互変換が可能

### 12.5 プロファイル JSON 例

```json
{
  "schemaVersion": "1.0",
  "id": "smile_01",
  "name": "笑顔",
  "layer": "emotion",
  "exclusionMode": "lastWins",
  "transitionDuration": 0.25,
  "transitionCurve": {
    "type": "easeInOut"
  },
  "blendShapeValues": [
    {"name": "Fcl_ALL_Joy", "value": 1.0},
    {"name": "Fcl_EYE_Joy", "value": 0.8}
  ],
  "layerSlots": {
    "lipsync": {
      "blendShapeValues": [
        {"name": "Fcl_MTH_A", "value": 0.5}
      ]
    }
  }
}
```

---

## 13. 複数キャラクター制御

### 13.1 コンポーネントモデル

- **キャラクターごとにコンポーネント**: 各キャラクターの GameObject に `FacialController` コンポーネントをアタッチ
- 各キャラクターが独立した PlayableGraph を持つ
- 各キャラクターが独立したプロファイルセットを保持

### 13.2 グローバル制御

- グローバルな制御（全員同じ表情等）が必要な場合は別途マネージャーを配置
- preview.1 ではグローバルマネージャーはスコープ外

---

## 14. Editor 拡張

### 14.1 Inspector カスタマイズ

- **FacialController コンポーネント**: プロファイル選択、レイヤー設定、OSC 設定を統合表示
- **プロファイル ScriptableObject**: BlendShape 値リスト、遷移設定、排他モード設定の専用 UI
- UI Toolkit ベースの CustomEditor

### 14.2 プロファイル管理ウィンドウ（EditorWindow）

- プロファイルのリスト表示
- **名前検索**: 部分一致検索（レイヤーフィルターは将来拡張）
- **サムネイルプレビュー**: 手動ボタンで生成（対象モデルの指定が必要）
- プロファイルの CRUD 操作
- JSON インポート / エクスポート

### 14.3 AnimationClip 作成支援ツール

- 独立プレビューウィンドウで BlendShape スライダー操作
- **preview.1 では BlendShape のみ**: テクスチャ切り替え / UV アニメーションは将来対応
- テクスチャ / UV は Unity 標準 AnimationWindow で編集

### 14.4 Editor ディレクトリ構造

```
Editor/
├── Inspector/          # FacialController, SO の CustomEditor
├── Windows/            # プロファイル管理ウィンドウ
├── Tools/              # AnimationClip 作成支援、ARKit 検出
└── Common/             # 共通ユーティリティ、UI Toolkit スタイル
```

---

## 15. テンプレートプロファイル

### 15.1 同梱テンプレート

パッケージにデフォルトプロファイルの JSON テンプレートを同梱:

| テンプレート名 | レイヤー | 説明 |
|---------------|---------|------|
| default | emotion | デフォルト表情（ニュートラル） |
| blink | eye | まばたき |
| gaze_follow | eye | 視線追従（preview.2 で実装） |
| gaze_camera | eye | カメラ目線（preview.2 で実装） |

- モデル固有の BlendShape 名はユーザーがカスタマイズ
- ARKit 検出時にモデル固有のテンプレートも自動生成

---

## 16. パフォーマンス設計

### 16.1 GC アロケーション方針

| タイミング | GC 許容 | 説明 |
|-----------|---------|------|
| 初期化時 | 許容 | JSON パース、PlayableGraph 構築、バッファ確保 |
| プロファイル切り替え時 | 許容 | PlayableGraph ノード再構築 |
| 毎フレーム処理 | **ゼロ目標** | ウェイト更新、補間計算、OSC 送受信、BlendShape 適用 |

### 16.2 データ構造

- 浮動小数点は `float` 基本
- NativeArray ベースのバッファで毎フレーム処理
- JSON パース負荷を抑えるデータ構造（初期化時のみ）

---

## 17. パッケージ構成

### 17.1 ディレクトリ構造

```
com.hidano.facialcontrol/
├── package.json
├── README.md
├── CHANGELOG.md
├── LICENSE.md
├── Runtime/
│   ├── Domain/                    # ドメインロジック（Unity 非依存）
│   │   ├── Models/                # ExpressionProfile, BlendShapeMapping 等
│   │   ├── Interfaces/            # ILipSyncProvider, IBlinkTrigger 等
│   │   └── Services/              # 遷移計算、排他ロジック等
│   ├── Application/               # ユースケース
│   │   └── UseCases/              # プロファイル管理、レイヤー制御等
│   └── Adapters/                  # Unity 依存の実装
│       ├── Playable/              # PlayableAPI 関連
│       ├── OSC/                   # OSC アダプター（uOsc ラッパー）
│       ├── Json/                  # JSON パーサー
│       ├── ScriptableObject/      # SO マッパー
│       └── Input/                 # InputSystem アダプター
├── Editor/
│   ├── Inspector/                 # CustomEditor
│   ├── Windows/                   # EditorWindow
│   ├── Tools/                     # AnimationClip 作成支援、ARKit 検出
│   └── Common/                    # 共通ユーティリティ、スタイル
├── Templates/                     # デフォルトプロファイル JSON テンプレート
├── Documentation~/                # ドキュメント
│   ├── api-reference.md
│   ├── quickstart.md
│   └── json-schema.md
└── Tests/
    ├── EditMode/
    │   ├── Domain/
    │   ├── Application/
    │   └── Adapters/
    ├── PlayMode/
    │   ├── Integration/
    │   └── Performance/
    └── Shared/
```

### 17.2 Assembly Definition

| asmdef | 依存先 |
|--------|--------|
| Hidano.FacialControl.Domain | なし（Unity 非依存） |
| Hidano.FacialControl.Application | Domain |
| Hidano.FacialControl.Adapters | Domain, Application, Unity.Animation |
| Hidano.FacialControl.Editor | Domain, Application, Adapters |
| Hidano.FacialControl.Tests.EditMode | Domain, Application, Adapters |
| Hidano.FacialControl.Tests.PlayMode | Domain, Application, Adapters |

---

## 18. テスト戦略

### 18.1 配置基準

| テスト種別 | 配置先 | 対象 |
|-----------|--------|------|
| 単体テスト | EditMode | Domain 層（プロファイル、遷移計算、排他ロジック） |
| 単体テスト | EditMode | Application 層（ユースケース） |
| Fake 統合テスト | EditMode | Adapters 層（JSON パーサー、SO マッパー） |
| 統合テスト | PlayMode | PlayableAPI 実装、OSC 送受信 |
| パフォーマンステスト | PlayMode | GC 計測、フレームレート |

### 18.2 TDD サイクル

```
1. Red:    失敗するテストを書く（EditMode 優先）
2. Green:  テストを通す最小限のコードを書く
3. Refactor: リファクタリング（テストは緑を維持）
```

---

## 19. ドキュメント（preview.1 同梱）

| ドキュメント | 内容 |
|------------|------|
| API リファレンス | XML コメントから自動生成 |
| クイックスタート | 基本的なセットアップ手順、最初のプロファイル作成 |
| JSON スキーマ | プロファイル JSON の全フィールド定義と例 |

---

## 20. 決定事項サマリー

| # | 項目 | 決定 | 備考 |
|---|------|------|------|
| 1 | プロファイルの AnimationClip 構造 | 基本 Clip 1 + レイヤー別スロット（固定名） | ランタイムは BlendShape 配列ベース |
| 2 | OSC 方向性 | 送受信両方（preview.1） | VRChat + ARKit プリセット |
| 3 | 排他粒度 | プロファイル単位 | 各プロファイルが個別に排他モードを持つ |
| 4 | スロット定義 | 固定スロット名（辞書型） | emotion, lipsync, eye |
| 5 | ARKit 検出トリガー | 手動（API / Editor ボタン） | 自動検出なし |
| 6 | JSON 変換タイミング | 初期化時 1 回 + メモリキャッシュ | ホットリロード時は再パース |
| 7 | JSON 配置場所 | StreamingAssets | Android 対応時に注意 |
| 8 | マルチキャラクター | キャラクターごとに独立プロファイルセット | メモリ増だが柔軟性優先 |
| 9 | テクスチャ / UV 制御 | PlayableAPI（AnimationClip 再生） | BlendShape も PlayableAPI に統一 |
| 10 | BlendShape 制御 | 全て PlayableAPI に統一 | カスタム PlayableNode で OSC 値を反映 |
| 11 | Editor ツール（preview.1） | BlendShape のみ | テクスチャ / UV は将来 |
| 12 | OSC アドレス | VRChat + ARKit プリセット | 完全カスタムは将来 |
| 13 | OSC → Playable | カスタム PlayableNode（ScriptPlayable） | NativeArray でGCフリー |
| 14 | カテゴリ属性 | レイヤー指定のみ | タグ等は将来 |
| 15 | 入力方式 | ボタン + 修飾キー + 連続値（表情強度） | ゲームパッドトリガー対応 |
| 16 | リップシンク入力 | ILipSyncProvider インターフェース | 固定長バッファで GC フリー |
| 17 | JSON バージョン | schemaVersion フィールドあり | preview 間でもバージョン管理 |
| 18 | サムネイル | 手動ボタンで生成 | 対象モデル指定が必要 |
| 19 | Curve シリアライズ | キーフレーム配列そのまま JSON 化 | Keyframe 全フィールド保持 |
| 20 | 検索 | 名前検索のみ | レイヤーフィルターは将来 |
| 21 | uOsc 同梱 | package.json 依存定義 | OpenUPM 登録確認要 |
| 22 | コントローラー | キャラクターごとにコンポーネント | グローバルマネージャーは別途 |
| 23 | UDP ポート | デフォルト VRChat 標準 + ユーザー設定可 | Inspector / JSON で変更 |
| 24 | PlayableGraph 構成 | 1 Graph / キャラクター + レイヤー分ノード | AnimationLayerMixerPlayable |
| 25 | デフォルトプロファイル | テンプレート JSON 同梱 | モデル固有名はユーザーカスタマイズ |
| 26 | 排他動作（後勝ち） | A → B クロスフェード | 遷移中の新トリガーは現在値から即新遷移 |
| 27 | ブレンド動作 | 加算ブレンド（クランプ） | 0〜1 にクランプ |
| 28 | まばたき | IBlinkTrigger IF 定義（実装は preview.2） | 人間的しぐさアルゴリズム |
| 29 | 視線制御 | BlendShape + ボーン両対応（preview.2） | Vector3 ターゲット指定 |
| 30 | GC 許容範囲 | 初期化 + プロファイル切り替え | 毎フレームはゼロ |
| 31 | Graph 再構築 | 動的再構築（GC 許容） | メモリ効率優先 |
| 32 | テスト戦略 | ドメイン EditMode / 統合 PlayMode | CLAUDE.md 基準と整合 |
| 33 | Editor 構造 | 機能別 + 共通層 | Inspector, Windows, Tools, Common |
| 34 | SO 構造 | 別構造（Unity 最適化）+ マッパー | Adapters 層にマッパー配置 |
| 35 | preview.1 スコープ | コア + OSC + ARKit + Editor | まばたき・視線は preview.2 |
| 36 | ドキュメント | API + クイックスタート + JSON スキーマ | チュートリアルは将来 |
