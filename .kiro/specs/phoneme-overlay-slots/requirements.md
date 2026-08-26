# Requirements Document

## Project Description (Input)
Expression の Phoneme Overlay スロット拡張: 既存の Expression (Smile, Angry 等) に A/I/U/E/O 5 種の Phoneme Overlay スロットを持たせる。何も設定されていなければ Lipsync 側で設定した AnimationClip/BlendShape がそのまま出る。Phoneme Overlay が設定されている場合はそちらを優先して使用する。既存の blink overlay と同じ枠組みを a/i/u/e/o 5 phoneme へ広げる。FacialCharacterProfileSO.Slots に a/i/u/e/o を予約 slot 名として導入し、Expression 毎の OverlaySlotBinding で各 phoneme slot に snapshot を上書き宣言可能にする。ULipSyncAdapterBinding の出力経路を Overlay レイヤー経由で発火させ、OverlayInputSource 解決で Expression の slot 宣言が優先、未宣言時は ULipSyncAdapterBinding の BlendShape/AnimationClipPhonemeEntry がデフォルトとして残る形にする。Backlog の S-17 に対応する spec。

## Introduction
本 spec は preview.1 段階の `overlay-clip-redesign` で確立した 3 状態 Overlay モデル (`default fallback` / `Suppress` / `Snapshot override`) を、リップシンク用の 5 phoneme slot (`a` / `i` / `u` / `e` / `o`) に拡張する。これにより Unity エンジニアは Smile / Angry 等の表情ごとに「この表情のときだけ "あ" の口形を別 AnimationClip で上書きする」運用を Inspector で完結でき、未宣言の slot については `ULipSyncAdapterBinding` の `BlendShapePhonemeEntry` / `AnimationClipPhonemeEntry` がそのままデフォルト出力として機能する。技術的には `Expression.Overlays` の枠組みを再利用し、`FacialCharacterProfileSO.Slots` への phoneme slot 登録、`OverlayInputSource` の解決経路、`ULipSyncAdapterBinding` の出力経路再配線を整合させる。

## Boundary Context
- **In scope**:
  - `FacialCharacterProfileSO.Slots` への phoneme slot (`a` / `i` / `u` / `e` / `o`) の予約 / 初期登録ポリシー確立
  - `Expression.Overlays` (= `OverlaySlotBinding` 配列) を介した phoneme slot ごとの 3 状態 (`default fallback` / `Suppress` / `Snapshot override`) 宣言
  - `ULipSyncAdapterBinding` の出力経路を `Overlay` レイヤー (`OverlayInputSource`) 経由で発火させる再配線
  - `OverlayInputSource` の解決ロジックを「Expression の slot 宣言を優先、未宣言時は `ULipSyncAdapterBinding` のデフォルト出力にフォールバック」に拡張
  - Inspector の `FacialCharacterProfileSO` Expression Row における phoneme overlay 編集 UI 追加 (Foldout / Tab で折り畳む方針)
  - 既存 JSON スキーマ / SO シリアライズへの phoneme slot 設定の保存と読込
  - EditMode / PlayMode テストの追加 (3 状態 × 5 phoneme slot の解決検証、GC ゼロ維持)
- **Out of scope**:
  - 音声解析側ロジック (uLipSync 本体 / `ULipSyncProvider` / `LipSyncInputSource` の評価ロジック変更)
  - phoneme 5 種を超える音素 (silence / nn / consonant 等) への拡張
  - リップシンク用 AnimationClip サンプリング失敗時の自動 path 補正 (Backlog S-9 の領域)
  - Default Overlays 経路への phoneme slot 自動登録 (Profile 全体既定としての phoneme overlay は本 spec で扱わない)
  - VRM / ARKit / OSC アダプタ側のロジック変更 (`overlaySlot` 参照を更新する範囲のみ許容)
  - 旧スキーマからの自動マイグレーション (preview 段階の破壊的変更は許容)
- **Adjacent expectations**:
  - `overlay-clip-redesign` spec が確立した `OverlaySlotBinding` 3 状態モデルと `FacialProfile.Slots` を単一の真実とする原則を踏襲する。phoneme slot は同モデルへ追加で乗り、独自モデルは作らない
  - `ULipSyncAdapterBinding` の既定 Layer (`lipsync`) と既定 InputSourceId (`ulipsync`) の枠組み、および `IAdapterBindingInitialDefaults` による 5 音素プリセットは維持する
  - Backlog S-17 が示す「未宣言時は Lipsync 側の AnimationClip/BlendShape がそのまま出る」要望に整合させる
  - `OverlayInputSource` が `overlay-clip-redesign` で確立した「ctor で `_resolvedBySlot` を事前構築し毎フレームヒープ確保ゼロ」契約を破らない

## Requirements

### Requirement 1: Phoneme Slot 名予約と Profile 既定登録
**Objective:** Unity エンジニアとして、`FacialCharacterProfileSO` 上で phoneme slot を一意に識別できる予約名で扱いたい。これにより各 Expression の Overlay 宣言と `ULipSyncAdapterBinding` の出力経路が同じ slot 名規約で接続される。

#### Acceptance Criteria
1. The Phoneme Overlay Slots feature shall reserve five slot names `a`, `i`, `u`, `e`, `o` (lowercase, ASCII) as canonical phoneme overlay slot identifiers used across Domain / JSON / SO / Editor layers.
2. When a developer creates a new `FacialCharacterProfileSO` via `CreateAssetMenu`, the FacialCharacterProfileSO shall ensure the five reserved phoneme slot names are present in `Slots` as an opt-in default that can be removed by the developer.
3. When a developer manually edits `Slots` in the Inspector and removes a phoneme slot name, the FacialCharacterProfileSO shall persist the removal and shall not silently re-add it on save / reload.
4. If a developer registers a phoneme slot name with different casing (e.g. `A`, `I`), then the FacialCharacterProfileSO shall treat it as a distinct slot from the reserved lowercase names and shall not auto-canonicalize the casing.
5. While a phoneme slot is declared in `Slots`, the OverlayInputSource shall participate in slot resolution for that slot; while it is not declared, the OverlayInputSource shall remain inactive for that slot (existing behavior carried over from `overlay-clip-redesign`).

### Requirement 2: Expression に対する Phoneme Overlay の 3 状態宣言
**Objective:** Unity エンジニアとして、Smile / Angry 等の Expression ごとに「この表情のときの "あ" 口形」を default / suppress / override の 3 状態で宣言したい。これにより既存 blink overlay と同じワークフローで phoneme overlay を扱える。

#### Acceptance Criteria
1. The Expression model shall accept up to five `OverlaySlotBinding` entries for the reserved phoneme slot names without introducing a new field beyond the existing `Overlays` collection.
2. When an Expression declares an `OverlaySlotBinding` for a phoneme slot with `Suppress = false` and a non-null `Snapshot`, the OverlayInputSource shall use that snapshot as the active resolution for the slot while the Expression is the top-active emotion expression.
3. When an Expression declares an `OverlaySlotBinding` for a phoneme slot with `Suppress = true`, the OverlayInputSource shall write no values for the slot (i.e. `TryWriteValues` returns `false` and `ContributeMask` is empty) regardless of the `ULipSyncAdapterBinding` output.
4. When an Expression declares an `OverlaySlotBinding` for a phoneme slot in `default fallback` state (no `AnimationClip` / no `cachedSnapshot` / `Suppress = false`), the OverlayInputSource shall delegate the slot output to the `ULipSyncAdapterBinding` default phoneme entry path.
5. When an Expression has no `OverlaySlotBinding` entry for a phoneme slot, the OverlayInputSource shall treat the slot as `default fallback` for that Expression and shall delegate the slot output to the `ULipSyncAdapterBinding` default phoneme entry path.
6. If both `Suppress = true` and a non-empty `cachedSnapshot` are present on the same phoneme `OverlaySlotBinding`, the OverlaySlotBindingSerializable validation shall log a warning and the runtime shall honor `Suppress = true` (matching `overlay-clip-redesign` precedence).

### Requirement 3: ULipSyncAdapterBinding の出力経路を Overlay レイヤー経由にする
**Objective:** Unity エンジニアとして、`ULipSyncAdapterBinding` が直接 `lipsync` Layer の `LipSyncInputSource` へ書き込む現行経路を、`OverlayInputSource` 経由で `Overlay` レイヤーに供給する構造へ切り替えたい。これにより Expression 側の slot 宣言が出力経路の単一接点となる。

#### Acceptance Criteria
1. When the ULipSyncAdapterBinding starts and resolves its analyzer + phoneme entries successfully, the ULipSyncAdapterBinding shall expose its per-phoneme blendshape weights through a path consumable by `OverlayInputSource` for each of the five phoneme slots.
2. While a phoneme slot is declared in `FacialCharacterProfileSO.Slots`, the OverlayInputSource shall be registered for that slot and shall be capable of resolving both Expression-level overrides and the ULipSyncAdapterBinding default output.
3. When the active Expression declares an Override or Suppress binding for a phoneme slot, the OverlayInputSource shall preempt the ULipSyncAdapterBinding default output for that slot in the same frame.
4. When no Expression-level override is active for a phoneme slot, the OverlayInputSource shall output the per-phoneme weights derived from the ULipSyncAdapterBinding's `BlendShapePhonemeEntry` or `AnimationClipPhonemeEntry` for that phoneme.
5. While the ULipSyncAdapterBinding is not started (e.g. no audio input device resolved), the OverlayInputSource shall report `ContributeMask` empty and `TryWriteValues` returning `false` for all phoneme slots even when the slots are declared in `Slots`.
6. If the ULipSyncAdapterBinding's existing `LipSyncInputSource` registration would conflict with the new Overlay-based path, the feature shall provide a compatibility strategy (e.g. flag, layer routing rule, or deprecation note) that prevents double-writing to the same BlendShape indices within the same frame.

### Requirement 4: OverlayInputSource の Phoneme 解決ロジック
**Objective:** Unity エンジニアとして、phoneme slot に対する `OverlayInputSource` の解決順序が明確で予測可能であること。これによりトラブル時の挙動追跡が容易になる。

#### Acceptance Criteria
1. When resolving a phoneme slot, the OverlayInputSource shall apply the following precedence (highest first): (a) Expression-level Override for the active top emotion expression, (b) Expression-level Suppress for the active top emotion expression, (c) `FacialProfile.DefaultOverlays` Override / Suppress declaration for the slot (if any), (d) ULipSyncAdapterBinding default phoneme output.
2. While no top-active emotion expression is resolved (e.g. base expression only), the OverlayInputSource shall use `FacialProfile.DefaultOverlays` first and fall back to the ULipSyncAdapterBinding default phoneme output.
3. When `OverlayInputSource.TryWriteValues` is called for a phoneme slot in the same frame, the OverlayInputSource shall not allocate managed heap memory after construction (preserving `overlay-clip-redesign` per-frame zero-GC contract).
4. If a phoneme slot is declared in `Slots` but neither any Expression nor `DefaultOverlays` provides an Override / Suppress, the OverlayInputSource shall produce zero values and `TryWriteValues` shall return `false`, allowing the ULipSyncAdapterBinding default path to surface.
5. The OverlayInputSource shall reuse the existing `_resolvedBySlot` Dictionary structure and `SlotKey` type from `overlay-clip-redesign` without introducing a new lookup container per phoneme slot.

### Requirement 5: JSON / SO シリアライズ互換
**Objective:** Unity エンジニアとして、phoneme overlay 設定が JSON および ScriptableObject の双方で persist し、ビルド後でも JSON 経由で差し替え可能であること。

#### Acceptance Criteria
1. When an Expression's phoneme `OverlaySlotBinding` is serialized to JSON via `SystemTextJsonParser`, the JSON shall preserve the slot name, the `Suppress` flag, and the inline `ExpressionSnapshot` (or null) under the same `overlays` array schema established by `overlay-clip-redesign`.
2. When `FacialCharacterProfileSO` is saved in the Editor and re-loaded, the SO shall round-trip phoneme slot bindings without losing the slot name, the `animationClip` reference, the `cachedSnapshot`, or the `suppress` flag.
3. When `FacialCharacterProfileExporter` bakes Expression `animationClip` references for phoneme slots into `cachedSnapshot`, the exporter shall reuse the same sampling pipeline used for non-phoneme slots without phoneme-specific branching.
4. If a JSON profile contains a phoneme `OverlaySlotBinding` with an unknown `slot` value not declared in `Slots`, the parser shall emit `InvalidSlotReference` diagnostics consistent with `overlay-clip-redesign` and the OverlayInputSource for that slot shall stay inactive.
5. The feature shall not introduce a new JSON schema version bump; the existing schema established in `overlay-clip-redesign` shall accommodate phoneme slots through data, not structure.

### Requirement 6: Inspector UI の Expression Row 拡張
**Objective:** Unity エンジニアとして、Expression Row 上で phoneme overlay の 3 状態を編集したいが、5 slot 縦並びは視認性を損なう。折り畳み UI で集約して扱えること。

#### Acceptance Criteria
1. The `FacialCharacterProfileSOInspector` shall render phoneme overlay editors (a / i / u / e / o) for each Expression inside a single collapsible container (Foldout or Tab group) within the Expression row, rather than as five inline rows.
2. While the phoneme overlay container is collapsed, the Inspector shall display a one-line summary that indicates how many of the five phoneme slots are in `Override`, `Suppress`, and `default fallback` states for the Expression.
3. When the developer expands the phoneme overlay container, the Inspector shall display per-slot UI consistent with the existing blink overlay UI (AnimationClip field, Suppress toggle, cachedSnapshot preview) for each declared phoneme slot in `Slots`.
4. If a reserved phoneme slot is not declared in `Slots`, the Inspector shall hide its editor row inside the expanded container and shall surface a hint that the slot can be added via the Slots section.
5. When the developer changes a phoneme slot binding through the Inspector, the Inspector shall trigger the same `cachedSnapshot` baking flow used by non-phoneme overlays so that the `ExpressionSnapshot` is captured immediately on save.
6. The Inspector UI for phoneme overlays shall use UI Toolkit, consistent with the project-wide convention forbidding new IMGUI UI.

### Requirement 7: Compatibility Strategy for Existing Mouth Layer Output
**Objective:** Unity エンジニアとして、既存プロジェクトで `ULipSyncAdapterBinding` が `lipsync` Layer に直接書いていた配線が、Overlay 経路導入後も二重書き込み / 出力消失を起こさず動作してほしい。

#### Acceptance Criteria
1. The feature shall define a single source-of-truth output path for `ULipSyncAdapterBinding` per phoneme slot per frame so that the same BlendShape index is not written by both the legacy `lipsync` Layer path and the new Overlay path simultaneously.
2. When the developer upgrades an existing `FacialCharacterProfileSO` that lacks phoneme slot declarations, the system shall continue running with the legacy `lipsync` Layer path unchanged (no regression for non-opted-in profiles).
3. When the developer opts into phoneme overlay slots by declaring `a` / `i` / `u` / `e` / `o` in `Slots`, the system shall route the `ULipSyncAdapterBinding` default phoneme output through `OverlayInputSource` and shall disable the legacy `lipsync` Layer direct-write for those phonemes (or document an explicit migration step).
4. If conflicting output paths are detected at runtime (legacy + overlay both active for the same phoneme BlendShape), the system shall emit a Unity `Debug.LogWarning` once per session identifying the slot and recommend the migration step.
5. The feature shall document the compatibility strategy choice (overlay-only / legacy-only / coexistence with priority) in the design phase so the implementation has a single agreed-upon behavior.

### Requirement 8: Performance and Allocation Guarantees
**Objective:** Unity エンジニアとして、phoneme overlay 機能を追加してもプロジェクト共通の毎フレーム GC ゼロ目標が破られないこと。

#### Acceptance Criteria
1. While the system is running with all five phoneme slots declared and five Expression overrides registered, the OverlayInputSource per-frame `TryWriteValues` path shall allocate zero managed heap bytes (verified by PlayMode performance test, consistent with `overlay-clip-redesign`).
2. When the `OverlayInputSource` resolves a phoneme slot's snapshot via `_resolvedBySlot`, the lookup shall complete in O(1) without per-frame allocation.
3. The ULipSyncAdapterBinding's Overlay output path shall reuse pre-allocated buffers (`float[]` / `BitArray`) and shall not allocate per frame after `OnStart` completes successfully.
4. While the system supports up to ten simultaneous characters with phoneme overlay slots declared, the aggregate per-frame allocation from phoneme overlay resolution shall remain zero across all characters.
5. The feature shall include a PlayMode performance test that fails if any of the above per-frame zero-allocation guarantees is violated.

### Requirement 9: Testability and Test Coverage
**Objective:** Unity エンジニアとして、phoneme overlay の 3 状態 × 5 slot × Expression 切り替えの組み合わせが自動テストでカバーされており、TDD サイクルで安全に変更できること。

#### Acceptance Criteria
1. The feature shall include EditMode tests that cover, for each of the five phoneme slots, the three states (`default fallback` / `Suppress` / `Override`) on at least one Expression.
2. When the active top emotion expression changes between two Expressions that declare different phoneme overrides for the same slot, the OverlayInputSource shall produce the expected snapshot in PlayMode tests within one frame of the change.
3. The feature shall include an EditMode test that verifies JSON round-trip preservation of phoneme `OverlaySlotBinding` entries through `SystemTextJsonParser` and `FacialCharacterProfileConverter`.
4. The feature shall include an EditMode test that verifies `InvalidSlotReference` diagnostics are emitted when a phoneme `OverlaySlotBinding` references a slot not declared in `Slots`.
5. The feature shall include a PlayMode test that exercises the `ULipSyncAdapterBinding` -> `OverlayInputSource` -> BlendShape weight pipeline end-to-end with a stubbed `ULipSyncProvider` to avoid requiring a real audio input device.
6. Test method names shall follow the project convention `{Method}_{Condition}_{Expected}` defined in steering structure.md.

### Requirement 10: Documentation and Migration Guidance
**Objective:** Unity エンジニアとして、phoneme overlay 機能の導入方針 / 既存プロジェクトの移行手順 / 制限事項が README または Documentation~ で確認できること。

#### Acceptance Criteria
1. The feature shall update `Packages/com.hidano.facialcontrol/Documentation~/` (or the package README) with a section explaining the phoneme overlay slot model and the precedence order defined in Requirement 4.
2. When a developer opts in to phoneme overlay slots, the documentation shall provide a step-by-step migration guide covering Slots declaration, Expression overlay editing, and the legacy `lipsync` Layer compatibility behavior from Requirement 7.
3. The documentation shall explicitly list the five reserved phoneme slot names (`a` / `i` / `u` / `e` / `o`) and note that custom phoneme sets are out of scope.
4. The documentation shall reference the Backlog entry S-17 as the source of the requirement and link to `overlay-clip-redesign` as the prerequisite model.
5. Where the implementation diverges from the EARS-defined precedence in Requirement 4 due to runtime constraints, the documentation shall record the divergence and the rationale.
