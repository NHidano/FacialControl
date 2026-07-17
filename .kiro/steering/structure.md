# Project Structure

## Organization Philosophy

**マルチパッケージ + レイヤード（クリーンアーキテクチャ）**。

リポジトリ直下は Unity プロジェクト 1 つ（`FacialControl/`）+ ドキュメント類。配布物は `FacialControl/Packages/` 配下のローカル UPM パッケージ群として開発する。各パッケージ内は Domain / Application / Adapters の 3 層 + Editor + Tests + Samples~ で構成し、依存方向を asmdef で強制する。

## Top-Level Layout

```
/                          # リポジトリルート
├── FacialControl/         # Unity プロジェクトルート（このディレクトリで開く）
│   ├── Assets/            # dev 用シーン・モデル参照・確認用 Samples ミラー
│   ├── Packages/          # ローカル UPM パッケージ群（配布物）
│   ├── ProjectSettings/
│   └── (Library/Temp/obj/UserSettings — 触らない)
├── docs/                  # 要件定義・QA・作業手順書
├── .kiro/                 # spec / steering（spec-driven 開発の成果物）
├── .github/               # CI / Copilot 指示
├── CLAUDE.md              # プロジェクト指示（Claude 用、真実の源）
└── README.md
```

## Package Layout (`FacialControl/Packages/`)

7 つのローカル UPM パッケージで分割配布する:

| パッケージ | 役割 |
|-----------|------|
| `com.hidano.facialcontrol` | コア（Domain / Application / Adapters / Editor） |
| `com.hidano.facialcontrol.osc` | OSC 通信拡張（VRChat 互換、uOsc 同梱想定） |
| `com.hidano.facialcontrol.inputsystem` | InputSystem 連携 + `Multi Source Blend Demo` サンプル提供 |
| `com.hidano.facialcontrol.lipsync` | uLipSync 連携アダプター（音素 overlay 入力） |
| `com.hidano.facialcontrol.ifacialmocap` | iFacialMocap 受信アダプター（ARKit 52 / gaze） |
| `com.hidano.facialcontrol.rec` | 操作イベント記録・再生（.fcrec、基準状態 + 注入再生） |
| `com.hidano.facialcontrol.timeline` | Timeline 統合（表情/連続値 Track、ベイク、REC 書き出し。依存: core + rec(Editor のみ) + com.unity.timeline） |

各パッケージ内の標準構成:

```
{package}/
├── Runtime/
│   ├── Domain/            # Unity 非依存。{Models, Interfaces, Services}/
│   ├── Application/       # ユースケース。UseCases/
│   └── Adapters/          # Unity 依存実装。{Playable, OSC, Json, ScriptableObject, Input, InputSources, FileSystem}/
├── Editor/                # UI Toolkit。{Inspector, Windows, Tools, Common}/
├── Tests/
│   ├── EditMode/          # 同期実行・モック/Fake のみ
│   ├── PlayMode/          # MonoBehaviour・コルーチン・実 I/O が必要
│   └── Shared/            # 両モード共用（Fake、テストユーティリティ）
├── Samples~/              # UPM 配布の canonical サンプル（~ で Unity が無視）
├── Documentation~/        # Markdown ドキュメント
├── package.json
├── README.md / CHANGELOG.md / LICENSE.md
```

### Asmdef Dependency Direction（破ってはならない）

```
Hidano.FacialControl.Domain      ← (Unity.Collections のみ)
Hidano.FacialControl.Application ← Domain
Hidano.FacialControl.Adapters    ← Domain, Application, Unity.Animation, Unity.Collections
Hidano.FacialControl.Editor      ← Editor 専用 asmdef
```

Domain は Engine 参照を持たない（`noEngineReferences` ではないが Unity 型を使わない契約）。Adapters のみ Engine 機能と統合する。

## Samples の二重管理ルール

`Samples~/` と `Assets/Samples/` は**意図的に二重管理する**:

- **`Packages/{package}/Samples~/`** — UPM 配布の canonical。`package.json` の `samples` 配列に登録されたものだけが Package Manager から Import 可能。`~` suffix で Unity のコンパイル対象外。
- **`FacialControl/Assets/Samples/`** — dev プロジェクト専用のミラー。HatsuneMiku 等のモデル依存物を Scene にベイクした状態で保持し、Scene 結線して動作確認する。

**同名ファイル（例: `MultiSourceBlendDemoHUD.cs`, `multi_source_blend_demo.json`）はどちらかを編集したら必ずもう一方をコピーして同期する。** drift すると UPM 経由のユーザーと dev で挙動が乖離する。preview.2 以降で `Import Sample` 経由のフローへリファクタする可能性あり。

## Naming Conventions

- **クラス / 構造体 / enum**: `PascalCase`
- **インターフェース**: `I` プレフィックス（例: `IExpressionTrigger`）
- **プライベートフィールド**: `_camelCase`
- **名前空間**: `Hidano.FacialControl.{Domain|Application|Adapters|Editor}.{SubArea}`
- **テストクラス**: `{Target}Tests`
- **テストメソッド**: `{Method}_{Condition}_{Expected}`（例: `SetProfile_ValidJson_ReturnsProfileWithCorrectBlendShapes`）

## Code Organization Principles

- **依存は内向き**: Adapters → Application → Domain。逆方向の参照を asmdef で物理的に禁止する。
- **境界モック**: 外部境界（I/O、ネットワーク、Renderer、Time）のみ Fake 化。Domain 内部はモックしない。
- **配布単位の独立性**: コア / OSC / InputSystem は独立してインストール可能。コアは OSC・InputSystem を知らない。
- **3 レイヤー既定の表情合成**: 感情 / リップシンク / 目（優先度はユーザー設定可、カテゴリ内排他は LastWins / Blend を選択可）。
- **Editor 専用機能はランタイムに混入させない**: `Editor/` 配下のコードは asmdef の `includePlatforms: ["Editor"]` で隔離する想定。

## File Management Rules

- **`.meta` は常にアセットと一緒に管理**（コミット対象）
- **生成バイナリ・ログはコミット禁止**
- **`Library/Temp/obj/UserSettings` は触らない**
- **`tasks.txt` はタスク ID のみを列挙**するファイル（for ループ実行用）。タスクの説明・詳細は `docs/work-procedure.md` に書く。`tasks.txt` に説明を直接追記してはならない。
- **パッケージ更新は `FacialControl/Packages/manifest.json`** で行い、`packages-lock.json` を同期維持する。

---
_Document patterns, not file trees. New files following patterns shouldn't require updates_
