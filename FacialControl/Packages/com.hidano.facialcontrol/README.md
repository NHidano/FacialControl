# FacialControl

3D キャラクターの表情をリアルタイムに制御する Unity 向けライブラリ（開発者向けアセット）。

## 概要

FacialControl は、VTuber 配信用フェイシャルキャプチャ連動や GUI エディタでの Expression 作成支援を主なユースケースとする、Unity エンジニア向けの表情制御パッケージです。

### 主な機能

- **マルチレイヤー表情制御** — 感情・リップシンク・目などのレイヤーで表情を同時管理。排他モード（LastWins / Blend）をレイヤー単位で設定可能
- **スムーズな表情遷移** — 線形補間・イージング・カスタムカーブによる Expression 間の遷移。遷移中の割り込みにも GC フリーで対応
- **OSC ネットワーク通信** — VRChat 互換の OSC（UDP + uOsc）による BlendShape データの送受信。ダブルバッファリングで低遅延
- **ARKit / PerfectSync 対応** — ARKit 52 ブレンドシェイプと PerfectSync の自動検出・Expression 自動生成
- **Editor 拡張** — Inspector カスタマイズ、プロファイル管理ウィンドウ、BlendShape スライダー付き Expression 作成ツール（UI Toolkit）
- **JSON ファーストの永続化** — 表情設定は JSON で管理。ビルド後も差し替え可能。ScriptableObject は JSON への参照ポインター
- **入力デバイス対応** — InputSystem によるコントローラ / キーボードからの Expression トリガー
- **リップシンク連携** — 外部プラグイン（uLipSync 等）からの入力を受け付ける `ILipSyncProvider` インターフェース

## 動作要件

- Unity 6000.3 以降
- Animator コンポーネントが設定された 3D キャラクターモデル（FBX）
- BlendShape を持つ SkinnedMeshRenderer

## インストール

### OpenUPM 経由（推奨）

`Packages/manifest.json` に以下を追加してください。

```json
{
    "scopedRegistries": [
        {
            "name": "OpenUPM",
            "url": "https://package.openupm.com",
            "scopes": [
                "com.hidano"
            ]
        }
    ],
    "dependencies": {
        "com.hidano.facialcontrol": "0.1.0-preview.1"
    }
}
```

### ローカルインストール

リポジトリをクローンして `Packages/` ディレクトリに配置する場合:

```json
{
    "dependencies": {
        "com.hidano.facialcontrol": "file:com.hidano.facialcontrol"
    }
}
```

## クイックスタート

1. `StreamingAssets/FacialControl/` にプロファイル JSON を配置（テンプレート: `Templates/default_profile.json`）
2. Unity Editor で **Create** → **FacialControl** → **Facial Profile** から FacialProfileSO を作成し、JSON パスを設定
3. キャラクターの GameObject に **FacialController** コンポーネントを追加
4. Inspector で FacialProfileSO を割り当て

```csharp
// スクリプトから Expression を切り替え
_facialController.Activate(expression);
_facialController.Deactivate(expression);
```

詳細は [クイックスタートガイド](Documentation~/quickstart.md) を参照してください。

## アーキテクチャ

クリーンアーキテクチャ（Domain / Application / Adapters）を採用。Unity 依存を Adapters 層に封じ込め、各レイヤーは Assembly Definition で依存方向を強制しています。

```
Runtime/
├── Domain/         # ドメインロジック（Unity 非依存）
├── Application/    # ユースケース
└── Adapters/       # Unity 依存の実装（PlayableAPI, OSC, JSON, Input）
Editor/             # Editor 拡張（UI Toolkit）
```

表情制御は PlayableGraph + AnimationStream API ベースで、既存の Animator と共存可能です。

## パフォーマンス

- 毎フレーム処理での GC アロケーションゼロ
- 遷移割り込み時も NativeArray 再利用で GC フリー
- OSC 受信はダブルバッファリング（ロックフリー）、送信は別スレッドで非同期実行
- 10 体以上のキャラクター同時制御を想定した設計

## ドキュメント

- [クイックスタートガイド](Documentation~/quickstart.md)
- [JSON スキーマリファレンス](Documentation~/json-schema.md)

## 依存パッケージ

| パッケージ | バージョン | 用途 |
|-----------|-----------|------|
| `com.unity.inputsystem` | 1.17.0 | 入力デバイスの動的切り替え |
| `com.hidano.uosc` | 1.0.0 | OSC（UDP）通信 |

## 対応フォーマット

| フォーマット | 対応状況 |
|-------------|----------|
| FBX | 対応 |
| VRM | 今後対応予定 |

## ライセンス

[MIT License](LICENSE.md)
