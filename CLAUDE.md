# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクト概要

FacialControl は、3D キャラクターの表情をリアルタイムに制御する Unity 向けライブラリ（開発者向けアセット）。OpenUPM へのリリースを想定。主なユースケースは VTuber 配信用フェイシャルキャプチャ連動と、GUI エディタでの AnimationClip 作成支援。ターゲットユーザーは Unity エンジニア。

## 重要なドキュメント

- **QA シート**: `docs/requirements-qa.md` — プロジェクト要件の詳細な Q&A。実装判断に迷った場合はここを参照
- **要件定義**: `docs/requirements.md`
- **Copilot 指示**: `.github/copilot-instructions.md`

## 開発環境

- **Unity**: 6000.3.2f1 (Unity 6)
- **レンダリング**: URP v17.3.0（PC / モバイル別設定あり）
- **カラースペース**: Linear
- **Unity プロジェクトルート**: `FacialControl/` ディレクトリ配下

## 主要な依存パッケージ

| パッケージ | 用途 |
|-----------|------|
| `com.unity.inputsystem` (1.17.0) | 入力デバイスの動的切り替え |
| `com.unity.timeline` (1.8.9) | タイムラインアニメーション |
| `com.unity.test-framework` (1.6.0) | Edit Mode / Play Mode テスト |
| `jp.lilxyzw.liltoon` | トゥーンシェーダー |
| `com.mikunote.magica-cloth-2` | クロスシミュレーション |
| `com.mikunote.hatsunemiku-sample` | 開発用サンプルモデル（リリース非同梱） |

外部パッケージ（lilToon、MagicaCloth2、Miku モデル）は `NHidano/Mikunote_Models` リポジトリから SSH 経由で取得される。

## 開発コマンド

### テスト実行
```bash
# EditModeテスト（単体テスト）
"<UnityPath>/Unity.exe" -batchmode -nographics -projectPath ./FacialControl \
    -runTests -testPlatform EditMode \
    -testResults ./test-results/editmode.xml

# PlayModeテスト（統合テスト）
"<UnityPath>/Unity.exe" -batchmode -nographics -projectPath ./FacialControl \
    -runTests -testPlatform PlayMode \
    -testResults ./test-results/playmode.xml
```

## アーキテクチャ方針

### コア設計

- **プロファイルベースの表情管理**: 表情は「プロファイル」で抽象化。各プロファイルは基本 AnimationClip + カテゴリ属性 + リップシンク用 AnimationClip + 遷移時間を保持
- **JSON ファーストの永続化**: コア機能は JSON フォーマット。Unity 向けオプションとして ScriptableObject に変換。ビルド後も JSON で表情設定を差し替え可能にする
- **ランタイム JSON パース**: JSON → ScriptableObject 変換はランタイム機能。Asset ファイル保存のみ Editor ツール
- **マルチレイヤー構成**: 感情ベース表情 + リップシンク + まばたき等、複数レイヤーでオーバーライド管理
- **ネットワーク伝送**: UDP + uOsc (OSC プロトコル)。1 フレーム間に複数回送受信

### 表情制御方式

ブレンドシェイプ + ボーン + テクスチャ切り替え + UV アニメーションの組み合わせ。表情遷移は線形補間がデフォルト（イージング/カスタムカーブで上書き可能）。遷移時間は 0〜1 秒（デフォルト 0.25 秒）。

### 対応フォーマット

- FBX: プロトタイプから標準対応
- VRM: リリース後の早期マイルストーン
- ブレンドシェイプ命名規則は固定しない（2 バイト文字・特殊記号を正しく扱う）
- ARKit 52 / PerfectSync 対応モデルはプロファイル自動生成を提供

## 開発方針

### TDD（テスト駆動開発）
```
Red-Green-Refactorサイクル:
1. Red:    失敗するテストを書く
2. Green:  テストを通す最小限のコードを書く
3. Refactor: リファクタリング（テストは緑を維持）
```

**原則**:
- テストファースト: 実装前にテストを書く
- 小さなステップ: 1つのテストで1つの振る舞いを検証
- モックは最小限: 外部境界（I/O、ネットワーク）のみモック化
- FIRST原則: Fast, Independent, Repeatable, Self-validating, Timely

### 設計原則

- 単一責任の原則に従う
- PR ベースの開発フロー
- 対象プラットフォームは現状 Windows PC のみ。モバイル・WebGL・VR は将来拡張の余地を残す設計とする
- OSC 以外の通信プロトコルもインターフェースで抽象化し将来拡張可能にする
- レンダーパイプライン非依存の設計

### パフォーマンス設計指針

- 毎フレームのヒープ確保を避ける（GC スパイク対策）
- 浮動小数点は `float` 基本
- UDP 送受信はメインスレッド非依存
- JSON パース負荷を抑えるデータ構造

## 開発規約

### コーディングスタイル
- C#、4スペースインデント、改行時に中括弧
- 日本語で応答・コメント・ドキュメントを記述
- 明示的な `public` / `private` を推奨
- クラス / 構造体 / enum: PascalCase
- インターフェース: `I` プレフィックス
- プライベートフィールド: `_camelCase`

### テスト命名規則
- クラス名: `{対象クラス}Tests`
- メソッド名: `{メソッド}_{条件}_{期待結果}`
- 例: `SetProfile_ValidJson_ReturnsProfileWithCorrectBlendShapes`

### テストフォルダ構造
```
Tests/
├── EditMode/           # PlayMode不要なテスト（単体・Fake統合）
│   ├── Domain/         # プロファイル、ブレンドシェイプ等のドメインロジック
│   ├── Application/    # ユースケーステスト
│   └── Adapters/       # リポジトリ、JSONパーサー等
├── PlayMode/           # PlayMode必須なテスト（実通信・性能）
│   ├── Integration/
│   └── Performance/
└── Shared/             # EditMode/PlayMode共用（Fakes等）
```

### テスト配置基準（EditMode vs PlayMode）

**配置はテストカテゴリ（単体/統合）ではなく、実行時要件で決定する。**

| 配置先 | 基準 | 例 |
|--------|------|-----|
| EditMode | モック・Fakeのみ、同期実行、PlayMode機能不要 | JSONパーステスト、プロファイル変換テスト |
| PlayMode | MonoBehaviourライフサイクル、コルーチン、実UDP/OSC通信、フレーム同期が必要 | 表情遷移の補間テスト、OSC送受信テスト |

## 品質基準

### 性能要件
| 指標 | 基準 |
|------|------|
| GCアロケーション | 毎フレーム処理でゼロ目標 |
| 表情遷移 | 線形補間 0〜1秒対応 |
| ネットワーク | 1フレーム間に複数回UDP送受信可能 |
| プリセット上限 | ユーザープリセット最大512（ペイロード可変） |

## Claude Code 実行ルール

- Unity テストランナーは `run_in_background` を使わず、`timeout: 600000` の同期 Bash 呼び出しで実行する

## 重要な注意事項

### ファイル管理
- `.meta` ファイルは常にアセットと共に管理
- 生成されたバイナリやログはコミット禁止
- `Library/Temp/obj/UserSettings` は触らない

### パッケージ管理
- `FacialControl/Packages/manifest.json` でパッケージ更新
- `packages-lock.json` を同期維持

### バージョン管理
- 短縮系命令形コミットメッセージ（日本語可）
- 例: "表情プロファイルのJSON読み込み機能を追加"
