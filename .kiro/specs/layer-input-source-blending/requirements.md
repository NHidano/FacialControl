# Requirements Document

## Project Description (Input)
レイヤーごとに複数の入力源（ゲームコントローラ / OSC フェイシャルキャプチャ / 外部リップシンク等）を有効化し、入力源ごとにブレンドウェイトを設定可能にする機能。既存の LayerBlender を拡張し、同一レイヤー内で複数入力源を重み付き合成できるようにする。

【背景】
現状の FacialControl では「レイヤー」はレイヤー間の優先度ブレンドと、同レイヤー内の Expression 同士の排他制御（LastWins / Blend）のみに使われている。BlendShape を複数の入力源（ゲームパッド操作 / フェイシャルキャプチャ OSC / uLipSync などの外部リップシンクプロバイダ）から駆動したい場合、現設計では後勝ちで上書きされてしまい、明示的な入力源切替・重み付きブレンドはできない。

【想定ユースケース】
1. 状況切替: ある時は eye / emotion レイヤーをゲームコントローラで操作し、別の場面ではフェイシャルキャプチャに切り替える
2. 重み付きブレンド: 眉の動き（emotion レイヤーに含まれる BlendShape）をゲームコントローラ 50% + フェイシャルキャプチャ 50% で合成
3. 特定入力源固定: lipsync レイヤーは常に uLipSync のみ有効、OSC の口パラメータは無視

【スコープ】
- レイヤー単位の「入力源ウェイトマトリクス」を JSON プロファイルで表現
- InputBindingProfile / OSC 受信 / ILipSyncProvider を「入力源」として抽象化
- 入力源ごとのウェイトはランタイム変更可能（状況切替）
- BlendShape 単位の入力源ルーティングは今回スコープ外（必要なら別スペック）

【非スコープ】
- 新規入力源プラグイン API の拡張（既存抽象の範囲で対応）
- Editor UI 拡張は最小限（JSON 直編集前提で可）

feature name: layer-input-source-blending

## Requirements

## Introduction
本機能は既存の `LayerBlender`（`Hidano.FacialControl.Domain.Services.LayerBlender`）を拡張し、各レイヤーに対して複数の入力源（Input Source）を同時に供給・重み付き合成できるようにする。入力源は InputSystem によるコントローラ / キーボード入力、OSC 経由のフェイシャルキャプチャ、`ILipSyncProvider` からのリップシンク入力などを抽象化した概念として扱う。既存のレイヤー優先度ブレンドと `LayerSlot` オーバーライドの挙動は維持したまま、レイヤー内部での入力源合成機構を追加する。これにより、状況に応じた入力源切替（ゲームコントローラ ⇄ フェイシャルキャプチャ）、重み付きブレンド（コントローラ 50% + キャプチャ 50%）、特定入力源固定（lipsync レイヤーは uLipSync のみ）といったユースケースを JSON プロファイルとランタイム API の両面から実現する。

## Boundary Context
- **In scope**:
  - レイヤー単位の「入力源ウェイトマトリクス」を表現するドメインモデル
  - FacialProfile JSON での入力源ウェイト宣言と読み書き
  - `LayerBlender` の拡張: レイヤーへの入力が単一値配列ではなく「入力源別値配列 + 重み」の集合となる
  - 入力源の抽象インターフェース（既存の `InputBindingProfile` / OSC / `ILipSyncProvider` を入力源としてアダプトするための契約）
  - ランタイムでの入力源ウェイト変更 API と、未接続・無効入力源のフェイルセーフ
- **Out of scope**:
  - BlendShape 単位（1 つの BlendShape だけを特定の入力源に割り当てる）のルーティング
  - 新規入力源プラグイン API の拡張（例: 新しいセンサークラス、Web カメラトラッキング）
  - Editor UI 拡張（preview 段階では JSON 直接編集で運用）
  - Timeline / AnimationClip からの入力源ウェイト自動操作
- **Adjacent expectations**:
  - 既存の `LayerBlender.Blend` / `ApplyLayerSlotOverrides` の公開 API と GC フリー特性（`Runtime/Domain/Services/LayerBlender.cs`）は本機能でも維持する
  - `docs/requirements.md` FR-002（マルチレイヤー表情制御）、FR-004（OSC）、FR-005（入力デバイス制御）、FR-007（リップシンク連携）と整合する
  - 毎フレーム処理での GC アロケーションはゼロ目標（非機能要件 4.1）

## Open Questions and Decisions (Dig)

本セクションは deep-dig 調査で確定した決定事項を記録する。インタラクティブ dig セッション（AskUserQuestion による複数ラウンドの対話、2026-04-20）で承認・改訂された決定を反映済み。各決定は EARS 要件本文 (AC) に D-番号で相互参照される。

### D-1: 「入力源」の意味論 — ハイブリッドモデル (Option B)（リスク: 高・確定）
- **決定**: **ハイブリッド入力源モデル**。入力源アダプタは最終的に `float[BlendShapeCount]` を返す共通契約を持つが、**内部実装は 2 タイプに分かれる**:
  - **Expression トリガー型アダプタ**（例: コントローラ、キーボード）: アダプタ内部に **専用の Expression スタック + TransitionCalculator** を持ち (Option B)、Expression ID のトリガーを受けて BlendShape 値配列を生成する。
  - **BlendShape 値提供型アダプタ**（例: OSC、ILipSyncProvider）: 外部から受信した BlendShape 値配列をそのまま提供する。内部に Expression パイプラインを持たない。
- **データフロー**（同レイヤー内）:
  ```
  Controller --> [専用 Expr パイプ] --> BlendShape[] A
  Keyboard   --> [専用 Expr パイプ] --> BlendShape[] B
  OSC        ----------------------> BlendShape[] C (ARKit raw)
                                      ↓
                         output[k] = clamp01(w_A·A[k] + w_B·B[k] + w_C·C[k])
  ```
- **Rationale**: ユースケース「コントローラで smile 50% + キーボードで angry 50% を値レベル混合」「OSC は BlendShape 値を生の数値で送る」の双方を自然に表現できる唯一のモデル。コントローラとキーボードが独立した Expression 遷移を持つため、両者のトリガーが競合しない。
- **含意**: Expression トリガー型アダプタは「コントローラ用アダプタ」「キーボード用アダプタ」などそれぞれ独立クラスとなる。従来の「`legacy` 予約 ID で Expression パイプライン全体を 1 本の入力源として温存」は廃止（D-5 参照）。
- **JSON 宣言**: 型は ID から暗黙判別。JSON は `{ id, weight, options }` のみで型フィールドなし（D-15）。

### D-2: レイヤー内合成の数式（リスク: 高・確定）
- **決定**: **加重和 + 最終クランプ**。各 BlendShape k について `output[k] = clamp01(Σ wᵢ · values_i[k])`。正規化は行わない。
- **Rationale**: 「コントローラ 50% + キャプチャ 50%」の解釈が最も直感的。正規化すると「全ソース弱化時に相対的に強く出る」望まぬ挙動を生む。lerp 連鎖は順序依存で非対称。
- **残存リスク**: 加重和 > 1 の飽和に気付きにくい → 診断 API (D-9) で可視化。

### D-3: レイヤー内合成と inter-layer ブレンドの順序（リスク: 高・確定）
- **決定**: **2 段階パイプライン**。(1) 各レイヤーで入力源集約を行い per-layer `float[]` を生成、(2) 既存の `LayerBlender.Blend(ReadOnlySpan<LayerInput>, Span<float>)` にそのまま渡す。
- **API 形**: 新規サービス（仮: `LayerInputSourceAggregator`）が per-layer 集約を担い、その出力を既存 `LayerBlender.Blend` に供給する。既存 API 非破壊。

### D-4: 「レイヤーウェイト」と「入力源ウェイト」の関係（リスク: 中・確定）
- **決定**: 独立。レイヤーウェイトは既存通り inter-layer ブレンドで適用。入力源ウェイトは per-layer 集約内で適用。
- **含意**: レイヤーウェイト 0 なら入力源の設定に関わらずそのレイヤーの寄与はゼロ。

### D-5: `inputSources` 必須化、legacy フォールバック廃止（リスク: 中・確定）
- **決定**: `inputSources` フィールドは**必須**。未宣言のレイヤーはパーサーがエラーを返す（or 警告ログ + レイヤー出力ゼロ）。暗黙 `legacy` フォールバックは**廃止**。
- **Rationale**: preview 段階のため後方互換なしで OK（ユーザー確認済み）。明示宣言により挙動の予測可能性を最大化。
- **含意**:
  - 既存プロファイル（未宣言）は移行が必要。preview の破壊的変更ポリシー（FR-001）で対応。
  - `legacy` 予約 ID は廃止。Expression 駆動は各 Expression トリガー型アダプタ（コントローラ用、キーボード用など）が個別に担う（D-1）。
  - Expression のみ使いたいユースケースは `inputSources: [{"id": "controller-expr", "weight": 1.0}]` のように明示宣言。

### D-6: 入力源識別子の名前空間と予約名（リスク: 中・確定）
- **決定**:
  - 識別子は ASCII `[a-zA-Z0-9_.-]{1,64}`。それ以外は parser が警告＋スキップ。
  - 予約 ID: `osc`、`lipsync`、`controller-expr`、`keyboard-expr`（Expression トリガー型アダプタ）、`input`（InputBindingProfile 由来の直接駆動 — 将来拡張用プレースホルダ）。
  - `legacy` は **予約から外す**（D-5 で廃止）。
  - サードパーティ拡張は `x-` プレフィックス推奨（例: `x-mycompany-arm-sensor`）。

### D-7: ランタイム API のスレッドポリシー（リスク: 中・確定）
- **決定**: **ダブルバッファ + Volatile read/write**（OscDoubleBuffer の踏襲パターン）。呼び出し側はどのスレッドからでも Set を発行可能、値は次の `Aggregate()` 呼び出しで swap されて観測される。強い lock-free MPMC 性は要求しない。
- **含意**: 同一 (layer, source) への同フレーム内複数書き込みは最後の書き込みが勝つ。Req 4.5 のバルク API は、内部で「次 swap まで待つペンディング辞書」に溜めて一括反映する方式。

### D-8: OSC アダプタの「古い値保持」ポリシー（リスク: 中・確定）
- **決定**:
  - デフォルト挙動は last valid を維持。
  - 追加で「staleness タイムアウト」設定 (`options.staleness_seconds`) をオプトインで導入。設定値を超えて新規データが無い場合、アダプタは `IsValid=false` を返しソースの寄与をゼロにする。

### D-9: 診断 API のスレッド安全性 / GC（リスク: 低・確定）
- **決定**: snapshot API は 2 系統提供。
  - `TryWriteSnapshot(Span<LayerSourceWeightEntry> buffer, out int written)` — GC フリー、ホットパス向け。
  - `GetSnapshot()` — List を返す便利版、Editor/診断用途。

### D-10: Performance ベンチの明確化（リスク: 低・確定）
- **決定**: 入力源の追加/削除は低頻度操作（シーン切替・プロファイル読込時）と定義。毎フレームの Set は GC ゼロ。`PlayMode/Performance/GCAllocationTests` に per-frame Set パターンのテストを追加。

### D-11: Editor UI の最小スコープ（リスク: 低・確定）
- **決定**: preview 段階では JSON 直編集前提。Inspector には「現在の入力源ウェイトマップ（読み取り専用）」のみ表示（Req 8.1 snapshot API 使用）。編集 UI は out of scope。

### D-12: カテゴリ排他ルールの適用範囲（リスク: 中・確定）
- **決定**: CLAUDE.md の「カテゴリ内排他 (LastWins / Blend)」ルールは **各 Expression トリガー型アダプタの内部でのみ適用**。ソース間（例: `controller-expr` と `keyboard-expr`）に対しては適用されない。ソース間合成は D-2 の加重和のみ。
- **Rationale**: Option B のメリット（値レベル混合）を保持するため、ソース間の Expression 単位排他は行わない。各ソースは独立して自身の Expression スタックを持ち、その内部でのみ category 排他が発生する。
- **含意**: ユーザーが「コントローラで smile、キーボードで angry」を同時に押すと、両者の BlendShape 値が値レベルで加算合成される（smile + angry の同時表情になる）。これは意図された挙動。

### D-13: Transition の保持場所（リスク: 中・確定）
- **決定**: TransitionCalculator は **ソースごとに保持**。各 Expression トリガー型アダプタが専用の TransitionCalculator を持ち、独立して Expression 遷移を進行する。
- **含意**: コントローラの smile 遷移（0.25s fade 中）とキーボードの angry 遷移（0.25s fade 中）は独立進行。同一レイヤーの最終出力は両者の加重和。

### D-14: Option B の GC/メモリ対策（リスク: 中・確定）
- **決定**: **事前確保プール方式**。プロファイルロード時に全 Expression トリガー型ソースの Expression スタック + TransitionCalculator バッファを確保する。毎フレームは値更新のみ。
- **含意**:
  - プール容量はプロファイル設定から導出（ソース数 × Expression スタック深度）。
  - ランタイムでのソース追加/削除は低頻度操作として扱い、その時のみアロケーションを許容（D-10）。
  - Req 6.1（GC ゼロ）と Req 6.5（10 体同時）の両立は、事前確保コスト（O(sourcesPerChar × charCount)）の線形スケールで達成。
- **Rationale**: Option B のメモリ負荷（ソース数 × Expression スタック）を、初期化時の 1 回に集約することで per-frame GC を回避。

### D-15: JSON での入力源型宣言（リスク: 低・確定）
- **決定**: JSON には `{ id, weight, options }` のみ。入力源の「型」（Expression トリガー型 vs BlendShape 値提供型）は **ID から暗黙判別**。アダプタ実装が自身の型を知っているため。
- **Rationale**: スキーマの冗長性を避ける。ID が一意に決まればアダプタ登録も一意に決まる。
- **含意**: 予約 ID（D-6）の一覧でアダプタ型が決定される。サードパーティ拡張 (`x-` プレフィックス) は登録時に型を宣言。

---

## Requirements

### Requirement 1: 入力源の抽象化とレイヤー入力モデル
**Objective:** Unity エンジニアとして、コントローラ / OSC / リップシンクなど異種の入力源を統一的にレイヤーへ供給したいので、入力源を共通のインターフェースと値配列として扱えるようにしたい。

#### Acceptance Criteria
1. The Layer Input Source Blending Service shall define a domain-level abstraction (D-1 hybrid model) that represents an input source as a tuple of (source identifier, BlendShape value array, validity flag); adapter implementations may be either Expression-trigger type (holding an internal Expression stack and TransitionCalculator) or BlendShape-value-provider type, but both types satisfy the same outward contract of returning a `float[BlendShapeCount]` per frame.
2. The Layer Input Source Blending Service shall accept multiple input sources per layer without assuming a fixed number of sources at compile time.
3. When an input source provides a BlendShape value array whose length does not match the layer's BlendShape count, the Layer Input Source Blending Service shall process only the overlapping range and leave the remaining indices unchanged.
4. If an input source is marked as invalid or disconnected, the Layer Input Source Blending Service shall treat its contribution as zero without raising an exception.
5. The Layer Input Source Blending Service shall not depend on Unity-specific APIs within the Domain layer so that the abstraction remains testable under EditMode without PlayMode fixtures.
6. Each Expression-trigger type adapter shall own its own independent Expression stack and TransitionCalculator instance (D-1 Option B, D-13) so that multiple such adapters on the same layer can drive different Expressions concurrently and blend their BlendShape outputs at the value level.
7. The Layer Input Source Blending Service shall restrict input source identifiers to the pattern `[a-zA-Z0-9_.-]{1,64}` and reserve the identifiers `osc`, `lipsync`, `controller-expr`, `keyboard-expr`, and `input` for first-party adapters (D-6); third-party adapters shall use the `x-` prefix. The identifier `legacy` is not reserved and shall not be used (D-5: legacy fallback is removed).
8. Category exclusion rules (LastWins / Blend as defined in CLAUDE.md) shall apply only within the Expression stack of a single Expression-trigger adapter, and shall never apply across different input sources (D-12).

### Requirement 2: 入力源ウェイトマトリクスとレイヤー内合成
**Objective:** Unity エンジニアとして、同一レイヤー内で複数入力源を重み付きで合成したいので、レイヤーごとに入力源ウェイトマトリクスを定義し一貫した合成結果を得たい。

#### Acceptance Criteria
1. The Layer Input Source Blending Service shall associate each layer with a weight map keyed by input source identifier, where each weight is a float in the range 0 to 1.
2. When computing a layer's aggregated BlendShape values, the Layer Input Source Blending Service shall compute, for each BlendShape index k, `output[k] = clamp01(Σ wᵢ · values_i[k])` where wᵢ is the source weight and values_i[k] is the i-th source's value (D-2: weighted-sum-with-final-clamp, no normalization).
3. While the sum of input source weights within a single layer exceeds 1, the Layer Input Source Blending Service shall clamp the final per-BlendShape result to the range 0 to 1 without rescaling individual source weights (D-2).
4. If no input source is registered for a layer (including no legacy source), the Layer Input Source Blending Service shall output the layer's default (zero) BlendShape values and log a warning at most once per layer per session.
5. When an input source weight is set outside the 0 to 1 range, the Layer Input Source Blending Service shall silently clamp the value into 0 to 1, consistent with the existing `LayerBlender` clamping policy.
6. The Layer Input Source Blending Service shall preserve the current inter-layer priority blending and `LayerSlot` override semantics of `LayerBlender` after per-layer source aggregation is applied (D-3: 2-stage pipeline — aggregate within layer, then feed legacy `LayerBlender.Blend`).
7. The Layer Input Source Blending Service shall apply the per-layer inter-layer weight (`LayerInput.Weight`) independently of input source weights; source weights affect only intra-layer aggregation and do not multiply with the inter-layer weight at the aggregation stage (D-4).

### Requirement 3: FacialProfile JSON スキーマ拡張
**Objective:** Unity エンジニアとして、入力源ウェイトをプロファイル JSON で宣言したいので、レイヤー定義に入力源ウェイトマトリクスを追加し、ランタイムで読み書き可能にしたい。

#### Acceptance Criteria
1. The FacialProfile JSON parser shall recognize a **required** `inputSources` field on each layer definition whose value is an ordered array of objects `{ "id": string, "weight": float, "options": object? }`, where `weight` defaults to 1.0 if omitted and `options` is an adapter-specific opaque map. The JSON schema shall not include a `type` field; the adapter type is inferred from `id` (D-15).
2. When a layer definition omits the `inputSources` field or provides an empty array, the FacialProfile JSON parser shall treat it as a profile error, log an error, and produce zero BlendShape output for that layer (D-5: no implicit `legacy` fallback; existing profiles without `inputSources` must be migrated per the preview-stage breaking-change policy in `docs/requirements.md` FR-001).
3. If an `inputSources` entry specifies an identifier not matching the pattern in Requirement 1.7 or an identifier for which no adapter is registered at runtime, the FacialProfile JSON parser shall log a warning and skip that entry without aborting profile loading.
4. When the same input source identifier appears more than once under a single layer, the FacialProfile JSON parser shall keep the last occurrence and log a warning about the duplicate.
5. The FacialProfile JSON parser shall serialize input source weights back to JSON with stable ordering (declaration order preserved) so that round-trip import/export does not introduce spurious diffs.
6. Where the JSON schema evolves during the preview phase, the FacialProfile JSON parser shall follow the project's preview-stage breaking-change policy (documented in `docs/requirements.md` FR-001) without requiring migration scripts.
7. The FacialProfile JSON parser shall pass through the per-source `options` map to the corresponding adapter without interpreting its contents, supporting adapter-specific configuration such as `staleness_seconds` (D-8) without requiring core schema changes.

### Requirement 4: ランタイムでの入力源ウェイト制御
**Objective:** Unity エンジニアとして、シーン内の状況に応じて入力源ウェイトを切り替えたいので、ランタイム API から入力源ウェイトを安全に変更できるようにしたい。

#### Acceptance Criteria
1. The Layer Input Source Blending Service shall expose a runtime API that sets the weight of a single input source on a specified layer in O(1) or O(sourcesPerLayer) time.
2. When a runtime weight change is requested for an existing (layer, input source) pair, the Layer Input Source Blending Service shall apply the new weight starting from the next blending evaluation without interrupting ongoing expression transitions.
3. If a runtime weight change targets a non-existent layer or input source identifier, the Layer Input Source Blending Service shall log a warning and leave all other weights unchanged.
4. While a weight change is in progress from a non-main thread, the Layer Input Source Blending Service shall ensure thread-safe visibility of the new weight for the next main-thread evaluation using a double-buffered pending/active weight map with volatile read/write semantics, without requiring the caller to take locks explicitly (D-7). Multiple writes to the same (layer, source) within one frame shall apply last-writer-wins.
5. The Layer Input Source Blending Service shall provide a bulk API that applies multiple (layer, input source, weight) updates atomically within a single frame so that coordinated scenario switches (e.g., "switch eye + emotion from controller to capture") are observed as a single transition; the bulk update shall become visible on the next aggregation and shall not interleave with single-entry updates made during the same frame (D-7).

### Requirement 5: 既存入力源アダプタとの統合
**Objective:** Unity エンジニアとして、既存のコントローラ / OSC / リップシンク実装を再利用したいので、これらを入力源抽象にアダプトするだけで新機構に接続できるようにしたい。

#### Acceptance Criteria
1. The Input Source Adapter layer shall provide Expression-trigger type adapters with reserved ids `controller-expr` (driven by `InputBindingProfile` from game controller) and `keyboard-expr` (driven by `InputBindingProfile` from keyboard) (D-1, D-6). Each such adapter shall own an independent Expression stack and TransitionCalculator (D-13) so that multiple Expression-trigger adapters on the same layer can drive different Expressions in parallel.
2. The Input Source Adapter layer shall provide an adapter (reserved id `osc`) that exposes OSC-received BlendShape values (from the existing double-buffered OSC receiver) as a single input source.
3. The Input Source Adapter layer shall provide an adapter (reserved id `lipsync`) that exposes `ILipSyncProvider` output as a single input source, usable on the lipsync layer.
4. When the OSC receiver has not produced any data within the current frame, the OSC input source adapter shall report the input source as valid with the last received values rather than producing a spurious zero frame.
5. Where the OSC adapter is configured with an `options.staleness_seconds` value greater than zero, the adapter shall mark itself invalid (contributing zero) when the most recent OSC packet is older than that threshold (D-8); when the option is absent or zero, the adapter shall maintain the legacy "hold last value indefinitely" behavior.
6. While an `ILipSyncProvider` returns no value (e.g., silence or plugin not running), the lipsync input source adapter shall mark the input source as inactive so that the Layer Input Source Blending Service treats its contribution as zero without producing audible jitter.
7. Where an adapter needs configuration (port, device, identifier name, staleness), the Input Source Adapter layer shall accept that configuration through constructor arguments or ScriptableObject/JSON `options` map without requiring changes to the Domain abstraction (D-6, D-8).

### Requirement 6: パフォーマンスと GC 非発生
**Objective:** Unity エンジニアとして、毎フレームで入力源ブレンドを実行しても性能劣化が発生しないでほしいので、GC ゼロ・スレッドセーフ・低計算量で動作する実装を保証したい。

#### Acceptance Criteria
1. The Layer Input Source Blending Service shall perform per-frame blending with zero managed heap allocations when the set of (layers, input sources) is unchanged (D-10, D-14).
2. The Layer Input Source Blending Service shall reuse pre-allocated buffers (e.g., `NativeArray` or pooled `float[]`) for per-source and per-layer intermediate values, and shall pre-allocate Expression stacks + TransitionCalculator buffers for all Expression-trigger type adapters at profile load time (D-14: pre-allocated pool).
3. When the number of input sources across all layers is N and the BlendShape count is M, the Layer Input Source Blending Service shall complete per-frame blending in O(N * M) time without hidden quadratic behavior.
4. The Layer Input Source Blending Service shall maintain compatibility with the existing `LayerBlender` GC-free guarantee so that callers relying on `ReadOnlySpan<LayerInput>` / `Span<float>` overloads continue to operate allocation-free.
5. While ten or more characters are controlled simultaneously (per non-functional requirement 4.1), the Layer Input Source Blending Service shall not introduce per-character initialization work that scales worse than linearly in `sourcesPerChar * charCount` (D-14: pre-allocation cost scales linearly).

### Requirement 7: 既存 LayerBlender API 保全とプロファイル移行
**Objective:** Unity エンジニアとして、`LayerBlender` の公開 API を壊したくない一方、既存プロファイルの `inputSources` 未宣言問題は preview 段階の破壊的変更として扱いたい。

#### Acceptance Criteria
1. The Layer Input Source Blending Service shall keep the existing `LayerBlender.Blend(ReadOnlySpan<LayerInput>, Span<float>)` and `LayerBlender.ApplyLayerSlotOverrides(...)` signatures callable from external packages without source-level breaking changes (D-3: the new aggregator feeds into the legacy `LayerBlender` without altering its signatures).
2. When a layer has a mixture of input sources with one source at weight 1 and all others at weight 0, the Layer Input Source Blending Service shall output exactly that source's values (within floating-point tolerance) for that layer.
3. The Layer Input Source Blending Service shall not implicitly fall back to any legacy Expression pipeline when `inputSources` is absent (D-5); profile loaders shall surface the missing field as an error and existing profiles without `inputSources` must be migrated.
4. Where the `inputSources` field is newly added to an existing profile, the migration path shall be documented as a preview-stage breaking change per `docs/requirements.md` FR-001; no automatic migration script is required for preview releases.

### Requirement 8: 診断・テスト容易性
**Objective:** Unity エンジニアとして、入力源ブレンドの挙動を検証・デバッグしたいので、テスト可能な境界と観測可能な状態を確保したい。

#### Acceptance Criteria
1. The Layer Input Source Blending Service shall expose two read-only snapshot APIs: (a) a GC-free `TryWriteSnapshot(Span<LayerSourceWeightEntry> buffer, out int written)` suitable for per-frame use, and (b) a convenience `GetSnapshot()` returning a managed list for editor/diagnostic use only (D-9).
2. The Layer Input Source Blending Service shall be fully exercisable under EditMode tests using fakes for the `legacy` Expression pipeline, OSC receiver, and `ILipSyncProvider` without requiring PlayMode.
3. When per-source weights are changed via the runtime API, the Layer Input Source Blending Service shall make the change observable through the snapshot API on the next evaluation.
4. The FacialProfile JSON parser shall emit identical, stable JSON output for equivalent input-source weight configurations so that snapshot-based tests can assert round-trip equality.
5. Where verbose diagnostic logging is enabled (e.g., via a development flag), the Layer Input Source Blending Service shall log, at most once per second per layer, the current per-source weights to aid debugging without flooding the Unity Console.
6. The `FacialProfileSO` Inspector shall display a read-only view of the current per-layer input source weight map using the snapshot API (D-11); editing input source weights via the Inspector is out of scope for this spec.

---

## Dig Summary

### Investigation Overview
- **Interactive dig session** (2026-04-20) using `AskUserQuestion` across 3 rounds.
- ラウンド数: 3（対話ベース、ユーザー承認済み）
- 洗い出した暗黙前提: 15（D-1〜D-15）
- 主要な改訂: D-1（解釈 B → ハイブリッドモデル Option B）、D-5（legacy フォールバック廃止）、D-6（`legacy` 予約解除）
- 新規追加: D-12（カテゴリ排他スコープ）、D-13（Transition 保持場所）、D-14（GC対策プール）、D-15（JSON型宣言）

### Key Discoveries
1. **「入力源」はハイブリッドモデル + Option B**（D-1）。アダプタは外向き契約として `float[]` を返すが、内部実装は 2 型（Expression トリガー型 / BlendShape 値提供型）。Expression トリガー型は**ソース毎に独立した Expression スタック + TransitionCalculator** を持つ。値レベルで smile + angry の同時表現が可能。
2. **`legacy` 予約 ID 廃止**（D-5、D-6）。preview 段階の破壊的変更として扱う。`inputSources` は必須フィールド化。既存プロファイルは移行が必要。
3. **カテゴリ排他ルールはソース内部限定**（D-12）。`controller-expr` と `keyboard-expr` が同一レイヤーで異なる Expression を駆動した場合、BlendShape 値レベルで加算される（排他されない）。これは Option B のメリットを活かす意図的な設計。
4. **Transition はソース毎に保持**（D-13）。各ソースが独自の TransitionCalculator を持ち、独立に遷移進行。
5. **事前確保プールで GC ゼロ維持**（D-14）。プロファイルロード時に全ソースの Expression スタックを確保。毎フレームは値更新のみ。
6. **レイヤー内合成は加重和 + 最終クランプ**（D-2）。正規化や lerp 連鎖ではない。
7. **2 段階パイプライン**（D-3）。新規 `LayerInputSourceAggregator` → 既存 `LayerBlender.Blend`。既存 API 非破壊。

### All Decisions

| ID | トピック | 決定 | 関連 AC |
|------|-------------|---------------|-----|
| D-1 | 入力源の意味論 | ハイブリッドモデル + Option B（ソース毎に独立 Expression パイプ） | R1.1, R1.6, R5.1 |
| D-2 | 合成式 | 加重和 + 最終クランプ（正規化なし） | R2.2, R2.3 |
| D-3 | 合成順序 | 2 段階（intra-layer aggregate → inter-layer `LayerBlender`） | R2.6, R7.1 |
| D-4 | レイヤー vs 入力源ウェイト | 独立。レイヤー weight は inter-layer のみ、ソース weight は intra-layer のみ | R2.7 |
| D-5 | `inputSources` 必須化 | legacy フォールバック廃止。未宣言はエラー。preview 破壊的変更 OK | R3.2, R7.3, R7.4 |
| D-6 | 識別子規約 | `[a-zA-Z0-9_.-]{1,64}`、予約: `osc` `lipsync` `controller-expr` `keyboard-expr` `input`。`legacy` は非予約 | R1.7 |
| D-7 | スレッドポリシー | ダブルバッファ + Volatile、lock-free MPMC 不要 | R4.4, R4.5 |
| D-8 | OSC staleness | オプトインの `options.staleness_seconds` | R5.5 |
| D-9 | 診断 API の GC | TryWriteSnapshot（GC フリー）と GetSnapshot（List）の 2 系統 | R8.1 |
| D-10 | 性能ベンチ | per-frame Set パターンのテスト追加 | R6.1 |
| D-11 | Editor UI スコープ | 読み取り専用表示のみ、編集 UI は別スペック | R8.6 |
| D-12 | カテゴリ排他スコープ | 各 Expression トリガー型アダプタ内部のみ適用。ソース間は D-2 の加重和のみ | R1.8 |
| D-13 | Transition 保持場所 | ソース毎に TransitionCalculator を保持、独立進行 | R1.6 |
| D-14 | GC対策 | 事前確保プール。プロファイルロード時に全 Expression スタック確保 | R6.1, R6.2, R6.5 |
| D-15 | JSON 型宣言 | `{id, weight, options}` のみ。型は ID から暗黙判別 | R3.1 |

### Remaining Risks (design フェーズで対応)
- **重み合計 > 1 時のクランプ飽和の可視化**（D-2）: 設定者が合成結果の飽和に気付きにくい。診断 API で可視化する UX を design フェーズで検討。
- **バルク API とシングル Set の同フレーム混在**（D-7）: 順序規約は Req 4.5 に最低限記述済みだが、design フェーズで詳細化。
- **Expression スタック深度のプロファイル指定方法**（D-14）: 事前確保するスタック深度をプロファイルから導出するか、アダプタ毎のデフォルトを持つか。design フェーズで決定。
- **ユースケースの意図しない加算**（D-12）: `controller-expr` で smile、`keyboard-expr` で angry を同時に押すと両者の BlendShape が加算され、ユーザーが混乱する可能性。design フェーズで Inspector 警告や診断ログの UX を検討。

### Recommended Next Steps
1. **本要件書の承認** — すべての D-1〜D-15 はユーザー対話で承認済み。レビュー後 `/kiro:spec-design layer-input-source-blending` へ進行。
2. **design フェーズで残存リスクの詳細設計** — D-2 飽和可視化、D-7 バルク/シングル順序規約、D-14 スタック深度の指定方法、D-12 の UX ガード。
3. **テスト戦略の明確化** — Option B のソース独立性を検証する EditMode テスト、Req 6.1 GC ゼロの PlayMode Performance テストを tasks フェーズで定義。
