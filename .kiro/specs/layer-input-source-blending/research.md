# Research & Design Decisions — layer-input-source-blending

---
**Purpose**: 既存 FacialControl コード資産の調査記録と、要件フェーズで確定した D-1〜D-15 をアーキテクチャへ落とし込む際の意思決定ログ。`design.md` で参照されるが、`design.md` は自己完結するため、ここは背景資料として扱う。
---

## Summary
- **Feature**: `layer-input-source-blending`
- **Discovery Scope**: Extension（既存 `LayerBlender` / `FacialProfile` / `OscDoubleBuffer` / `InputSystemAdapter` / `ILipSyncProvider` を拡張する統合機能）
- **Key Findings**:
  - 既存 `LayerBlender.Blend(ReadOnlySpan<LayerInput>, Span<float>)` は GC フリー（`stackalloc` + `ReadOnlyMemory<float>` 参照）で、Priority ベースの lerp 上書きを行う。そのまま inter-layer 段として流用可能（D-3 の 2 段パイプラインに整合）。
  - 既存 `OscDoubleBuffer` は `NativeArray<float>` + `Interlocked.Exchange(ref _writeIndex)` でダブルバッファ swap を実装済み。これを **入力源ウェイトのランタイム更新 (D-7)** にもそのまま踏襲できる。
  - 既存 `TransitionCalculator` / `ExclusionResolver` は完全 static + Span ベースで副作用を持たない。Expression トリガー型アダプタは自分専用のバッファを保持し、これら既存サービスを関数として呼ぶことで Option B (D-1) を GC フリーで実現可能。
  - 既存 `InputSystemAdapter.BindExpression` は `FacialController` 経由で Expression をトグルする設計（`BindingEntry` → `FacialController` 呼出し）。新アーキテクチャでは「アダプタが持つ内部 Expression スタック」へトグルを流す経路を新設する（`FacialController` 直叩きは副作用として禁止）。
  - `SystemTextJsonParser` は `JsonUtility` + DTO 変換。`layers[].inputSources` 配列は DTO 追加で対応できる（D-5 必須化の判定は DTO 正規化後に行う）。

## Research Log

### 既存 LayerBlender 実装調査
- **Context**: D-3 の 2 段パイプラインで既存 API 非破壊を保証するため、`LayerBlender.Blend` の契約を再確認する必要があった。
- **Sources Consulted**: `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Services/LayerBlender.cs`
- **Findings**:
  - 公開 API: `Blend(ReadOnlySpan<LayerInput>, Span<float>)`, `Blend(LayerInput[], float[])`, `ApplyLayerSlotOverrides` の 3 種。
  - `LayerInput` は `(Priority, Weight, ReadOnlyMemory<float> BlendShapeValues)` の readonly struct。
  - `stackalloc int[layers.Length]` で優先度順ソート（レイヤー数 ≤ 16 前提）。挿入ソートで安定性を確保。
  - 低優先度→高優先度の順で lerp 上書き（`output = output + (values - output) * weight`）。
- **Implications**:
  - 新 `LayerInputSourceAggregator` は per-layer `float[]` を埋め、そのポインタを `LayerInput` に載せて `LayerBlender.Blend` へ流せばよい（ゼロコピー）。
  - `LayerInput.Weight` と入力源ウェイトは独立運用すべき（D-4 と整合）。

### OscDoubleBuffer パターン調査
- **Context**: D-7 のランタイム入力源ウェイト更新をどう実装するかを決定するため。
- **Sources Consulted**: `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Adapters/OSC/OscDoubleBuffer.cs`
- **Findings**:
  - `NativeArray<float> _bufferA/_bufferB` + `int _writeIndex` + `Interlocked.Exchange` で swap。
  - `Write(index, value)` は受信スレッドから安全。`GetReadBuffer()` でメインスレッドが `AsReadOnly()` を取る。
  - `Swap()` で書き込み側バッファを swap し新書き込み側をゼロクリア。
- **Implications**:
  - 入力源ウェイトは `(layerIndex, sourceIndex) → float` の 2 次元を 1 次元 `float[layerCount * maxSourcesPerLayer]` にフラット化してダブルバッファ化可能。
  - 「次 swap まで待つペンディング辞書」(D-7) は「write バッファへの直接上書き」でよい。Swap は `Aggregate()` 入口で行う。
  - バルク API は「複数 Write → 最後に Swap」で 1 フレーム内原子性を確保（D-7 Req 4.5）。
  - **OscDoubleBuffer とは Swap 挙動が異なる**（design.md Critical 1 対応）: `OscDoubleBuffer` は毎フレーム OSC 受信があることを前提に swap 後の writeBuffer を zero-clear するが、入力源ウェイトバッファは「Set は稀な更新、観測は毎フレーム」というパターンのため zero-clear や no-op では **2 フレーム前の値を読むスタレデータバグ** が発生する。よって swap 時には `NativeArray.CopyTo` で現 readBuffer の内容を新 writeBuffer に **copy-forward** し、writeBuffer が常に「現行 readBuffer の複製 + 最新 Set」の状態を保つ。コストは O(layerCount × maxSourcesPerLayer)、完全 GC フリー。

### 既存 Expression パイプライン調査
- **Context**: Expression トリガー型アダプタ (D-1 Option B) が「アダプタ内部に Expression スタックを持つ」とき、既存 `ExpressionUseCase` / `LayerUseCase` / `TransitionCalculator` / `ExclusionResolver` をどこまで再利用できるか。
- **Sources Consulted**: `LayerUseCase.cs`, `ExclusionResolver.cs`, `TransitionCalculator.cs`, `Expression.cs`
- **Findings**:
  - `LayerUseCase.LayerTransitionState` が per-layer に `Snapshot/Target/Current` の 3 バッファと `ElapsedTime/Duration/Curve/IsComplete` を保持している。これと同じ構造を **per-source** で持てば Option B の独立遷移が実現する。
  - `ExclusionResolver.ResolveLastWins/ResolveBlend/TakeSnapshot/ClearOutput` はすべて Span ベースで純粋関数。アダプタ内部から直接呼び出せる。
  - `Expression.BlendShapeValues` は `ReadOnlyMemory<BlendShapeMapping>`。アダプタ毎に BlendShape 名 → インデックスの lookup を持つ必要がある（既存 `LayerUseCase.FindBlendShapeIndex` の O(N) 線形探索を踏襲か、事前辞書化で O(1) に改善するか選択）。
- **Implications**:
  - Option B の per-source Expression スタック状態は「`LayerTransitionState` を source スコープにリネームしたもの」として設計できる。
  - 既存 `LayerUseCase.UpdateWeights` の per-layer Expression 解決ロジックは、Expression トリガー型アダプタ内部へ移植する（per-layer ではなく per-source に責務が移る）。
  - 既存 `LayerUseCase` は移行期間中、非破壊のまま残す（呼び出し経路を `LayerInputSourceAggregator` に切り替える時点で deprecated 化）。

### 既存 JSON パーサ調査
- **Context**: D-5 で `inputSources` 必須化を追加するために、既存 `SystemTextJsonParser` の DTO 構造を確認する。
- **Sources Consulted**: `SystemTextJsonParser.cs`, `JsonSchemaDefinition.cs`
- **Findings**:
  - `JsonUtility.FromJson<ProfileDto>` 経由で厳密なクラスバインディング（`any` 不可）。ネスト DTO を段階的に追加可能。
  - `JsonSchemaDefinition.Profile.Layer` に `name`/`priority`/`exclusionMode` のフィールド名定数がある。ここに `inputSources` を追加する。
  - `options` マップは `JsonUtility` では扱えない（自由形式 dict 非対応）。`Newtonsoft.Json` 等の追加依存なしに実装するには「`options` 自身も型付き DTO とし、`options` 配下を `key/value` ペア配列で表現する」あるいは「adapter 固有 DTO クラスを型ごとに用意し、`type` ヒントを使わず `id` で分岐 DTO を選ぶ」のいずれかが必要。
- **Implications**:
  - 現状は `JsonUtility` に留まる方針（既存パターン踏襲・外部依存追加を避ける）。`options` は **`OscOptionsDto { float stalenessSeconds; }`**、**`ExpressionTriggerOptionsDto { int maxStackDepth; }`** のような **id 別の typed DTO**（共通基底 `InputSourceOptionsDto` を継承）を用意し、`inputSources[i].id` でディスパッチする。`Dictionary<string, string>` 形式は `JsonUtility` が自由形式 dict を扱えないため採用しない（設計矛盾回避）。
  - Factory は id → DTO 型のマップを保持し、Parser から渡された生 JSON サブ文字列を `JsonUtility.FromJson<TOptions>` で逆シリアライズする。Factory のシグネチャは `TryCreate(InputSourceId id, InputSourceOptionsDto options, ...)` と `TryDeserializeOptions(InputSourceId id, string optionsJson)` の 2 本。
  - サードパーティ拡張 (`x-` プレフィックス) は `RegisterExtension<TOptions>(id, creator)` で DTO 型と creator 関数を同時に登録する API を提供する。これにより拡張アダプタも typed DTO の恩恵を受けつつ、コアは拡張の DTO 型を知らなくて済む（`where TOptions : InputSourceOptionsDto, new()` 制約）。

### InputSystem バインディング現状
- **Context**: `controller-expr` / `keyboard-expr` を「独立 Expression スタックを持つアダプタ」として再設計する際、既存の `FacialInputBinder` / `InputSystemAdapter` がどう動いているかを把握する。
- **Sources Consulted**: `FacialInputBinder.cs`, `InputSystemAdapter.cs`, `InputBindingProfileSO.cs`
- **Findings**:
  - `FacialInputBinder` は `InputBindingProfileSO.GetBindings()` で `(actionName, expressionId)` ペアを取得し、`InputSystemAdapter.BindExpression` で各 `InputAction` の `performed`/`canceled` に Expression の ON/OFF を接続。ON/OFF の発火先は `FacialController` 直通。
  - `InputBindingProfileSO` は device 分類（controller / keyboard）をフィールドで持たない。`InputActionAsset` のバインディング側（device path）で分類する設計に見える。
- **Implications**:
  - `controller-expr` と `keyboard-expr` の分離は **`InputBindingProfileSO` に "source category" (Controller / Keyboard) フィールドを追加** するか、**2 つの別 SO として運用** するかの 2 択。後者の方が設計がシンプルだが、既存ユーザの移行コストが上がる。**前者（カテゴリフィールド追加）を採用**。
  - `InputSystemAdapter.BindExpression` の発火先を「内部 Expression スタックへ push/pop する IExpressionSink」に差し替え、アダプタ側が自分の `ControllerExpressionInputSource` / `KeyboardExpressionInputSource` に ON/OFF を届ける。

### uOsc / VRChat OSC 互換
- **Context**: OSC アダプタの `options.staleness_seconds` (D-8) を既存 `OscReceiver` でどう計測するか。
- **Sources Consulted**: `OscReceiver.cs` (冒頭 60 行)
- **Findings**:
  - `OscReceiver` はすでに `OscDoubleBuffer` に直接書き込むフロー。staleness の計測には「受信のたびに `Time.unscaledTime` を記録する」必要がある。
  - `OscDoubleBuffer` はインデックス単位に値を書くが、「いつ書かれたか」の情報は持たない。
- **Implications**:
  - `OscInputSource` アダプタは「`OscReceiver` への参照 + 最終受信時刻タイムスタンプ」を持つ。アダプタが時刻を観測し、staleness 判定は `OscInputSource` 側で行う（`OscReceiver` 自体は非侵襲）。
  - 受信時刻の更新をアダプタが知るには、`OscReceiver` に「受信コールバック」のフックを 1 つ追加するか、`OscDoubleBuffer` の書き込みカウンタ（`Interlocked.Increment`）で更新を検知する。**後者（カウンタ）を採用**し `OscDoubleBuffer` への改修は `uint WriteTick { get; }` プロパティ追加のみに抑える。
  - **時刻観測は `ITimeProvider` 経由に抽象化**（design.md Critical 3 対応）: `Time.unscaledTimeAsDouble` を直接参照すると EditMode テストで時刻を決定論的に前進できず、Req 8.2（EditMode-first testability）に違反する。Domain 層に `ITimeProvider { double UnscaledTimeSeconds { get; } }` を追加し、Adapters 層の `UnityTimeProvider` を本番実装、Tests/Shared の `ManualTimeProvider`（writable プロパティ）をテスト実装とする。`OscInputSource` は ctor で `ITimeProvider` を DI 受取し、staleness 判定と rate-limit 系ロジックで共通利用する。

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| A: Single-adapter/raw-values-only | 全入力源が `float[]` を提供する均質アダプタ。Expression 駆動はコア側で保持 | インターフェース最少 | コントローラとキャプチャを「値レベル混合」できず、後勝ちか片側強制になる | D-1 で棄却 |
| B: Hybrid（採用） | 外向き契約は `float[]`、内部は Expression 駆動型と値提供型の 2 系統 | ユースケース 1〜3 全対応。既存 TransitionCalculator/ExclusionResolver を再利用 | Expression スタックのメモリが source 数に比例 → D-14 の事前確保で対処 | D-1 にて確定 |
| C: Per-BlendShape routing | BlendShape 単位で入力源割当 | 最大柔軟 | JSON の冗長度が極端に増え、preview スコープ超過 | non-goal（Out of Scope に明記） |

## Design Decisions

### Decision: Aggregator を Domain 層に置き、Adapter は Adapters 層に置く
- **Context**: D-1 の Hybrid モデルで「IInputSource 契約」はクリーンアーキテクチャのどのレイヤーに属するか。
- **Alternatives Considered**:
  1. `IInputSource` と全アダプタを Adapters 層に置く（Domain は抽象を知らない）
  2. `IInputSource` を Domain 層、Expression トリガー型の共通基底は Domain 層、具体アダプタ（OSC, Input, LipSync）は Adapters 層に分離
- **Selected Approach**: **2 を採用**。`IInputSource` / `LayerInputSourceAggregator` / `ExpressionTriggerInputSourceBase` / `LayerInputSourceWeightMap` を `Domain/Services` 配下に配置し、`OscInputSource` / `ControllerExpressionInputSource` / `KeyboardExpressionInputSource` / `LipSyncInputSource` は `Adapters/InputSources` 配下。
- **Rationale**: Aggregator は純粋ドメイン演算（加重和・クランプ）で Unity 依存ゼロ。EditMode テスト容易性 (Req 8.2) を最大化。具体アダプタのみ `InputAction` / `OscDoubleBuffer` / `ILipSyncProvider` に依存する。
- **Trade-offs**: Domain 層に abstract 基底を置くと、サードパーティ拡張が Domain アセンブリへの参照を必要とする（既存の `ILipSyncProvider` と同じパターンなので許容）。
- **Follow-up**: asmdef 依存方向（Adapters → Application → Domain）を CI で確認する。

### Decision: 入力源ウェイトのランタイム更新は OscDoubleBuffer パターンを踏襲
- **Context**: D-7 でダブルバッファ + Volatile を要求。
- **Alternatives Considered**:
  1. `ConcurrentDictionary<(layer, source), float>` + lock
  2. `Interlocked.Exchange` によるダブルバッファ（既存 OscDoubleBuffer と同型）
  3. lock-free MPMC queue
- **Selected Approach**: **2**。`LayerInputSourceWeightBuffer` を新設し、`float[layerCount * maxSourcesPerLayer]` を 2 面持つ。`Aggregate()` 開始時に swap。
- **Rationale**: 既存パターン踏襲・GC フリー・スレッドセーフ。MPMC 強整合性は不要（D-7 で同一キー同フレーム複数書込は last-writer-wins 仕様）。
- **Trade-offs**: 2 次元 → 1 次元フラット化のインデックス計算が必要。`WeightIndex(layer, source) = layer * maxSourcesPerLayer + source` で解決。
- **Follow-up**: `maxSourcesPerLayer` はプロファイルロード時に決定（全レイヤーの `inputSources.Length` の max）。ランタイム追加/削除は低頻度なので D-10 に従い再確保を許容。

### Decision: Expression スタック深度はアダプタ既定 + プロファイル上書き可能
- **Context**: D-14 の残存課題「スタック深度の指定方法」。
- **Alternatives Considered**:
  1. アダプタごとに定数既定（例: 8）
  2. プロファイル JSON の `options.maxStackDepth` でアダプタ単位に宣言
  3. グローバル設定
- **Selected Approach**: **1 を基本とし、Expression トリガー型のみ `options.maxStackDepth` で上書き可能（省略時 8）**。
- **Rationale**: 通常 8 段で足りる（同時押し入力数の上限は InputAction の同時 pressed 数に等しい）。特殊用途で上書きできるオプション経路を残す。
- **Trade-offs**: 深度超過時の挙動を決める必要 → 「最古 push を drop して警告ログ」。
- **Follow-up**: EditMode テストで深度超過シナリオを検証。

### Decision: 重み合計 > 1 の飽和を診断 API で可視化
- **Context**: D-2 残存リスク。ユーザーがクランプ飽和に気付きにくい。
- **Alternatives Considered**:
  1. 飽和検知時に Debug.LogWarning を毎フレーム発火
  2. 診断スナップショットに `IsSaturated` フラグを含め、Inspector 読取専用ビュー (D-11) で表示
  3. 自動正規化（D-2 で却下済み）
- **Selected Approach**: **2**。`LayerSourceWeightEntry` に `bool Saturated` を含め、スナップショットを観測する Editor/診断 UI が表示する。
- **Rationale**: ホットパスにログコストを載せない（Req 6.1 GC ゼロ維持）。Inspector 側で赤色ハイライトなど UX は別 spec。
- **Trade-offs**: 実装者が診断 API を呼ばないと気付かない → ドキュメントで啓蒙。
- **Follow-up**: 診断 UI（Inspector の読取専用ビュー）は本 spec の Editor タスクに含める。

### Decision: `controller-expr` + `keyboard-expr` 同時トリガー時の UX ガード
- **Context**: D-12 残存リスク。両者同時押しで smile+angry が加算され混乱する可能性。
- **Alternatives Considered**:
  1. 警告ログ（D-5 と同様の "per layer per session" レート制限）
  2. 診断スナップショットに `ActiveSourceCount` を含めて UI 側で表示
  3. 何もしない（仕様として明記のみ）
- **Selected Approach**: **3 + ドキュメント記載**。ハードコードの警告は出さない。ユーザーが両者を意図的に同時押しするのは想定ユースケース（値レベル混合）のため、警告は逆にノイズ。
- **Rationale**: D-12 で「意図された挙動」と明記済み。観測したいユーザーは診断 API + Inspector 読取専用ビューで `Saturated` フラグを見れば足りる。
- **Trade-offs**: 意図せず混合しているユーザーは気付きにくいが、これは UX 設計で Inspector 側に解説を添える形で補完（本 spec Editor タスク範囲）。
- **Follow-up**: `docs/requirements.md` に「意図的加算」の注意書きを追加（implement phase）。

### Decision: バルク API のトランザクション境界
- **Context**: D-7 残存課題「バルクとシングル Set の同フレーム混在」。
- **Alternatives Considered**:
  1. バルク API が内部で pending dict を持ち、コミット時に 1 swap
  2. シングル Set と同じバッファに直書き、単に swap 一度だけ
  3. 別バッファ + コミットトランザクション
- **Selected Approach**: **1**。`BeginBulk()` / `SetWeight()` / `CommitBulk()` のスコープ内では write はメモリ dict に溜まり、Commit 時に write バッファへ一括反映（その後 Aggregate の swap で observable）。
- **Rationale**: 「1 つの Bulk と並行して発生する Single Set は Bulk コミット後に上書き可能」という順序規約を実装者が予測しやすい。
- **Trade-offs**: Bulk スコープ中の dict が小さな GC を発生 → スコープ開始時点で `Dictionary<int, float>` をプール化して回避。
- **Follow-up**: API 表現（`using IDisposable BulkScope = ... `パターン）を design.md に記す。

## Risks & Mitigations
- **Risk**: `JsonUtility` が `options` 自由形式 dict を扱えない → id 別 DTO 方式で回避し、将来 Newtonsoft へ移行可能な interface 境界を保つ。
- **Risk**: Expression スタック事前確保のメモリが「10 体 × source 数 × stack 深度 × float[BlendShapeCount]」で線形に膨らむ → Profiler で計測し、典型値（10 体 × 4 source × 8 depth × 200 BlendShape = 64 KB/体）が許容範囲であることを PlayMode Performance テストで検証。
- **Risk**: `controller-expr` / `keyboard-expr` の分離に `InputBindingProfileSO` 拡張が必要 → SO にカテゴリフィールド追加は破壊的変更。preview 段階のため許容（FR-001）。移行ノートを Migration セクションに記載。
- **Risk**: 既存 `LayerUseCase` のコードパスと新 `LayerInputSourceAggregator` パスが並存すると責務重複 → `LayerUseCase` は 1 呼出し経路の上位ラッパーとして残し、内部実装を Aggregator 呼出しに差し替える（API 非破壊）。

## References
- `.kiro/specs/layer-input-source-blending/requirements.md` — 要件と D-1〜D-15
- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Services/LayerBlender.cs` — 既存 inter-layer ブレンド実装
- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Adapters/OSC/OscDoubleBuffer.cs` — ダブルバッファ参照実装
- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Services/TransitionCalculator.cs` — 遷移カーブ評価（ソース毎に再利用）
- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Services/ExclusionResolver.cs` — カテゴリ内排他（ソース内部で再利用）
- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Application/UseCases/LayerUseCase.cs` — 既存 per-layer 遷移管理（リファクタ対象）
- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Adapters/Json/JsonSchemaDefinition.cs` — JSON スキーマ定数
- `CLAUDE.md` — プロジェクト設計方針（クリーンアーキテクチャ、GC ゼロ目標、4 スペースインデント、TDD）
