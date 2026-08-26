# Requirements Document

## Project Description (Input)
gaze-config-promotion: GazeConfig を FacialCharacterProfileSO ルートへ昇格し、Inspector セクションを再編する。

【背景】
adapter-binding-architecture spec の改修で _gazeConfigs は InputSystemAdapterBinding 内部に格納される構造になったが、以下の問題を抱えている:
1. 目線ボーンパス・可動範囲・LookXxxClip はキャラ/モデル固有データであり、入力源（InputSystem/OSC/ARKit）と独立であるべき。現状は InputSystem に密結合
2. FacialCharacterProfileSOInspector に gaze 関連のデッドコード（_gazeConfigsProperty 周辺、AppendGazeConfigForExpression 等）が残置されている
3. GazeConfig の opt-in 追加 UX が無く、現状は InputSystemAdapterBindingDrawer の素の PropertyField でリスト編集のみ
4. 参照モデルを再割当てしても GazeConfigs が自動更新されない
5. Inspector セクション順が直感に反している（AdapterBindings → Layers → ReferenceModel → Debug）

【目的】
- GazeConfig を SO ルート直下に昇格し、入力源非依存のキャラ設定として正しく位置づける
- Inspector を「参照モデル → レイヤーと表情 → GazeConfigs → AdapterBindings → Debug」の順で再構成
- GazeConfig は Analog Expression の中から明示的に opt-in で紐づける UX に変更（自動連動はしない）
- 参照モデル割当て時、bone path が空の GazeConfig を自動補完。手動値は保持
- 「全 GazeConfig を参照モデルから再解決」一括ボタンを GazeConfigs セクションに追加
- Expression 行から ID 表示を撤去し Debug セクションへ集約
- ExpressionKind に Vector2 kind は新設しない（Analog のままで sidecar として GazeConfig を opt-in）

【スコープ】
- com.hidano.facialcontrol コア: FacialCharacterProfileSO / GazeBindingConfig / IFacialCharacterProfile / FacialCharacterProfileConverter / FacialCharacterProfileExporter
- com.hidano.facialcontrol.inputsystem: InputSystemAdapterBinding / GazeExpressionConfig / InputSystemAdapterBindingDrawer
- profile.json スキーマ: gaze_configs[] を root 直下へ。schemaVersion bump（破壊的変更を許容、preview 段階のため migration 不要）
- Sample asset: MultiSourceBlendDemoCharacter.asset / profile.json を新スキーマへ手術
- Editor Inspector: FacialCharacterProfileSOInspector セクション再編・dead code 削除
- 既存テスト: InputSystemAdapterBindingTests / IntegrationTests / RoundTrip 系を更新

【スコープ外】
- ExpressionKind に Vector2 kind を新設すること
- OSC/ARKit native での gaze 駆動の実装（IBonePoseSource インターフェースは将来拡張可能な状態に保つが実装は別 spec）
- BlendShape-only gaze の挙動変更
- 自動まばたき・視線追従ターゲット指定（preview.2 以降の別 spec）
- bone path の相対化（backlog S-1 と一緒に対処する選択肢はあるが本 spec では対象外）

【preview リリース方針】
preview 段階のため破壊的変更（JSON schema bump / Sample asset 移植）を許容。migration コードは書かない。CHANGELOG / migration-guide には変更内容を記録する。

## Introduction

本 spec は、`com.hidano.facialcontrol` コアの `FacialCharacterProfileSO` データモデルを再構成し、目線（gaze）に関するキャラ固有設定（左右目ボーンパス・可動範囲・Look*Clip 等）を入力源（InputSystem / OSC / ARKit）から分離する。具体的には、現状 `InputSystemAdapterBinding._gazeConfigs` 配下に保持されている `GazeBindingConfig` のリストを `FacialCharacterProfileSO` のルート直下へ昇格させる。InputSystem 側は「どの InputAction でその gaze を駆動するか」のみを保持する `InputSystemGazeBinding`（expressionId + InputActionReference）に縮退する。

同時に、`FacialCharacterProfileSOInspector` のセクション順を「参照モデル → レイヤーと表情 → GazeConfigs → AdapterBindings → Debug」へ再編し、GazeConfigs セクションでは Analog Expression の中から opt-in で GazeConfig を追加する UX、参照モデルからの自動補完、一括再解決ボタンを提供する。Expression 行からは ID 表示を撤去し、Debug セクションに「Expression ID マッピング」一覧として集約する。

preview 段階のため JSON schema は bump し、Sample asset は新スキーマへ手術する。migration コードは書かず、変更内容は CHANGELOG / migration-guide に記録する。

## Boundary Context

- **In scope**:
  - `FacialCharacterProfileSO` ルート直下への `_gazeConfigs: List<GazeBindingConfig>` 新設
  - `InputSystemAdapterBinding._gazeConfigs` の撤去と、`_gazeInputBindings: List<InputSystemGazeBinding>`（expressionId + InputActionReference のみ）の新設
  - `IFacialCharacterProfile` インターフェースへの gaze configs アクセス追加
  - `FacialCharacterProfileConverter` / `FacialCharacterProfileExporter` の新スキーマ対応
  - `profile.json` の `gaze_configs[]` を root 直下へ移動、`schemaVersion` の bump
  - Sample asset (`MultiSourceBlendDemoCharacter.asset` および対応 `profile.json`) の新スキーマ移植
  - `FacialCharacterProfileSOInspector` のセクション順再編とデッドコード削除
  - GazeConfigs セクションの opt-in 追加 UX、参照モデル自動補完、一括再解決ボタン
  - Expression 行 ID 表示撤去と Debug セクションへの ID マッピング一覧追加
  - Analog Expression 削除時 / Analog→Digital 変更時の対応 GazeConfig 自動削除（孤児防止）
  - 既存 EditMode / PlayMode テストの更新
  - CHANGELOG / migration-guide への破壊的変更の記録

- **Out of scope**:
  - `ExpressionKind` に Vector2 kind を新設する変更
  - OSC / ARKit native での gaze 駆動実装（インターフェースは将来拡張可能な状態に保つが実装は別 spec）
  - BlendShape-only gaze の挙動変更
  - 自動まばたき・視線追従ターゲット指定（preview.2 以降の別 spec）
  - bone path の相対化（backlog S-1 と一緒に対処する選択肢はあるが本 spec では対象外）
  - 旧スキーマ JSON からの自動 migration コード

- **Adjacent expectations**:
  - `adapter-binding-architecture` spec で確立した「AdapterBinding は SO の入力源固有設定のみを保持する」原則と整合させる
  - `Multi Source Blend Demo` サンプル（dev `Assets/Samples/` 側 / UPM `Samples~/` 側の両方）が新スキーマで動作する
  - `IBonePoseSource` 等の gaze 駆動契約は OSC/ARKit 拡張（別 spec）から再利用可能な状態に保つ

## Requirements

### Requirement 1: GazeConfig の SO ルート昇格とデータモデル

**Objective:** Unity エンジニアとして、GazeConfig をキャラ/モデル固有データとして `FacialCharacterProfileSO` ルート直下で扱いたい。これにより入力源（InputSystem / OSC / ARKit）と独立した正しい責務分離を得たい。

#### Acceptance Criteria

1. The `FacialCharacterProfileSO` shall expose a serialized field `_gazeConfigs` of type `List<GazeBindingConfig>` directly under its root.
2. The `IFacialCharacterProfile` interface shall expose a read-only accessor for the root `_gazeConfigs` collection.
3. The `InputSystemAdapterBinding` shall not declare any `_gazeConfigs` field nor own any `GazeBindingConfig` instances.
4. The `InputSystemAdapterBinding` shall declare a serialized field `_gazeInputBindings` of type `List<InputSystemGazeBinding>` whose elements hold only `expressionId` and an `InputActionReference`.
5. When `InputSystemAdapterBinding.OnStart` is invoked, the `InputSystemAdapterBinding` shall resolve `GazeBindingConfig` entries by reading `_gazeConfigs` from the owning `FacialCharacterProfileSO` and pairing them with `_gazeInputBindings` via `expressionId`.
6. If an `InputSystemGazeBinding.expressionId` does not match any entry in the SO root `_gazeConfigs`, then the `InputSystemAdapterBinding` shall log a warning via `Debug.LogWarning` and skip wiring that binding.
7. The `GazeBindingConfig` type used by the SO root collection shall reuse the existing core type without creating a new variant.
8. The `GazeExpressionConfig` type (which previously embedded `InputActionReference`) shall be removed or repurposed strictly to expose only `expressionId` + `InputActionReference` semantics, with no remaining bone-path or look-clip fields.

### Requirement 2: profile.json スキーマ更新と破壊的変更の記録

**Objective:** Unity エンジニアとして、`profile.json` 表現も SO と同じく `gaze_configs[]` を root に持つ形へ揃えたい。これにより JSON ⇄ SO のラウンドトリップが自然に成立する。

#### Acceptance Criteria

1. The `profile.json` schema shall place `gaze_configs[]` directly under the root object, not nested inside any adapter binding object.
2. The `profile.json` schema shall increment its `schemaVersion` value to reflect this breaking change.
3. The `FacialCharacterProfileConverter` shall convert root-level `gaze_configs[]` from JSON into the SO root `_gazeConfigs` field.
4. The `FacialCharacterProfileExporter` shall serialize the SO root `_gazeConfigs` field to root-level `gaze_configs[]` in the produced `profile.json`.
5. When a `profile.json` written by the new converter is round-tripped through `FacialCharacterProfileExporter` and back, the `FacialCharacterProfileConverter` shall produce a SO whose `_gazeConfigs` is value-equal to the original.
6. The FacialControl repository shall not contain any code that auto-migrates pre-bump (legacy) `profile.json` files; legacy JSON support is intentionally dropped for preview.
7. The `CHANGELOG.md` of `com.hidano.facialcontrol` shall record the gaze-configs root promotion and `schemaVersion` bump under a "Breaking changes" entry.
8. The `Documentation~/migration-guide` (or equivalent migration document) shall describe how users hand-edit existing `profile.json` files to the new schema.

### Requirement 3: Sample asset の新スキーマ移植

**Objective:** Unity エンジニアとして、`Multi Source Blend Demo` サンプルが新スキーマでそのまま動作する状態を保ちたい。dev プロジェクトと UPM 配布の双方で挙動が乖離しないようにしたい。

#### Acceptance Criteria

1. The `MultiSourceBlendDemoCharacter.asset` (under `FacialControl/Assets/Samples/...`) shall be re-saved so that its YAML places `_gazeConfigs` at the SO root and removes any `_gazeConfigs` field from `InputSystemAdapterBinding`.
2. The corresponding `profile.json` under the dev sample directory shall be edited so that `gaze_configs[]` lives at the root with the bumped `schemaVersion`.
3. The mirrored copies under `Packages/com.hidano.facialcontrol*/Samples~/` (canonical UPM samples) shall be updated identically and stay in sync with the dev `Assets/Samples/` copies.
4. When the `Multi Source Blend Demo` scene is opened in dev, the `Multi Source Blend Demo` HUD shall continue to drive gaze without any null-reference or schema-version warnings logged at scene load.
5. The `InputSystemAdapterBinding` instance inside the demo asset shall declare `_gazeInputBindings` entries whose `expressionId` values match the `expressionId` values of the SO root `_gazeConfigs`.

### Requirement 4: Inspector セクション順の再編

**Objective:** Unity エンジニアとして、`FacialCharacterProfileSO` Inspector を直感的なセクション順で操作したい。データの依存関係（参照モデルが先、AdapterBindings はキャラ設定の後）に沿った並びにしたい。

#### Acceptance Criteria

1. While the `FacialCharacterProfileSOInspector` renders an inspected SO, the `FacialCharacterProfileSOInspector` shall arrange its sections in this top-to-bottom order: (1) Save Status Bar, (2) Reference Model, (3) Layers and Expressions, (4) GazeConfigs, (5) Adapter Bindings, (6) Debug.
2. The `FacialCharacterProfileSOInspector` shall emit each section as a distinct visual group (foldout or labeled container) rather than a flat list.
3. The Save Status Bar shall remain pinned to the top of the inspector regardless of foldout state of any other section.
4. The Adapter Bindings section shall appear strictly after the GazeConfigs section in the rendered hierarchy.

### Requirement 5: GazeConfigs セクションの opt-in 追加 UX

**Objective:** Unity エンジニアとして、Analog Expression の中から明示的に opt-in で GazeConfig を追加したい。Expression を増やしただけで GazeConfig が自動生成されると、不要な entry を量産してしまうため避けたい。

#### Acceptance Criteria

1. While the GazeConfigs section renders, the `FacialCharacterProfileSOInspector` shall display a "+ GazeConfig を追加" dropdown control whose options are exactly the Analog-kind Expressions in the SO whose `expressionId` is not already present in `_gazeConfigs`.
2. When the user selects an Expression from the "+ GazeConfig を追加" dropdown, the `FacialCharacterProfileSOInspector` shall append a new `GazeBindingConfig` to `_gazeConfigs` with that Expression's `expressionId`.
3. If every Analog-kind Expression already has a corresponding `_gazeConfigs` entry, then the `FacialCharacterProfileSOInspector` shall render the dropdown in a disabled (non-selectable) state with a label indicating that no candidates remain.
4. The `FacialCharacterProfileSOInspector` shall not auto-create a `GazeBindingConfig` simply because an Expression is added or its kind is set to Analog.
5. While a `GazeBindingConfig` exists in `_gazeConfigs`, the `FacialCharacterProfileSOInspector` shall render that entry as a row containing at minimum: the bound Expression name, editable bone path / range / look-clip fields, a "参照モデルから自動設定" button, and a remove button.
6. When the user clicks the remove button on a GazeConfig row, the `FacialCharacterProfileSOInspector` shall remove that `GazeBindingConfig` from `_gazeConfigs`.
7. Expression rows in the "Layers and Expressions" section shall not contain any control to create or remove a `GazeBindingConfig`.

### Requirement 6: 参照モデルからの自動補完 / 一括再解決

**Objective:** Unity エンジニアとして、参照モデルを割り当てたタイミングで bone path 等を自動補完し、必要な時に一括で再解決したい。ただし手動で編集した値は保持したい。

#### Acceptance Criteria

1. When a reference model `GameObject` is newly assigned to the SO (the slot transitions from null to a non-null value, or from one model to a different one), the `FacialCharacterProfileSOInspector` shall iterate `_gazeConfigs` and, for each entry whose `leftEyeBonePath` and `rightEyeBonePath` are both empty, populate those fields by resolving against the new reference model.
2. While performing the auto-fill described above, the `FacialCharacterProfileSOInspector` shall not overwrite `leftEyeBonePath` or `rightEyeBonePath` values that are already non-empty.
3. When the user clicks the "参照モデルから自動設定" button on a single GazeConfig row, the `FacialCharacterProfileSOInspector` shall re-resolve that row's bone paths and ranges against the currently assigned reference model, overwriting existing values for that row.
4. While the GazeConfigs section renders, the `FacialCharacterProfileSOInspector` shall display a "全 GazeConfig を参照モデルから再解決" bulk button directly within that section.
5. When the user clicks the "全 GazeConfig を参照モデルから再解決" button, the `FacialCharacterProfileSOInspector` shall iterate every entry in `_gazeConfigs` and re-resolve each one's bone paths and ranges against the currently assigned reference model, overwriting existing values.
6. If the SO has no reference model assigned, then the `FacialCharacterProfileSOInspector` shall render the per-row "参照モデルから自動設定" and bulk "再解決" controls in a disabled state.
7. The auto-fill and re-resolve logic shall reuse the existing reference-model resolution helpers rather than introduce a parallel implementation.

### Requirement 7: 孤児 GazeConfig の自動削除

**Objective:** Unity エンジニアとして、対応する Analog Expression が無くなった GazeConfig が SO に残留しないようにしたい。Expression が削除された場合や kind が Analog→Digital に変わった場合に手動で掃除する手間を避けたい。

#### Acceptance Criteria

1. When an Expression is removed from the SO and that Expression's `expressionId` matches an existing `GazeBindingConfig.expressionId`, the `FacialCharacterProfileSOInspector` shall remove the matching `GazeBindingConfig` from `_gazeConfigs` as part of the same edit.
2. When an existing Expression's kind is changed from Analog to a non-Analog kind (e.g. Digital), and that Expression's `expressionId` matches an existing `GazeBindingConfig.expressionId`, the `FacialCharacterProfileSOInspector` shall remove the matching `GazeBindingConfig` from `_gazeConfigs` as part of the same edit.
3. While `_gazeConfigs` is being mutated by the orphan-cleanup logic, the `FacialCharacterProfileSOInspector` shall record the change through the same `SerializedObject` / `Undo` pipeline used by other inspector edits so that the change is undoable.
4. The `FacialCharacterProfileSOInspector` shall not remove `GazeBindingConfig` entries for any reason other than (a) explicit user removal in the GazeConfigs section, (b) Expression deletion, or (c) Expression kind transition out of Analog.

### Requirement 8: Expression 行 ID 表示の撤去と Debug セクションへの集約

**Objective:** Unity エンジニアとして、Expression 行の見通しを良くし、ID 等のデバッグ情報は Debug セクションでまとめて確認したい。

#### Acceptance Criteria

1. The `FacialCharacterProfileSOInspector` shall not render the legacy `expression-row-id-label` (or any equivalent expressionId text label) inside an Expression row.
2. The Debug section of the `FacialCharacterProfileSOInspector` shall include an "Expression ID マッピング" listing whose rows show, for every Expression in the SO, at least: Expression name, `expressionId`, `kind`, and the layer it belongs to.
3. When a new Expression is added to the SO, the Debug section's "Expression ID マッピング" listing shall reflect the new entry on the next inspector repaint.
4. When an existing Expression is deleted or its name / kind / layer changes, the Debug section's "Expression ID マッピング" listing shall reflect the updated state on the next inspector repaint.

### Requirement 9: Inspector dead code の整理

**Objective:** Unity エンジニアとして、旧構造前提のデッドコードや hook が `FacialCharacterProfileSOInspector` に残らないようにしたい。新構造に必要なものだけを保持したい。

#### Acceptance Criteria

1. The `FacialCharacterProfileSOInspector` source shall not retain a `_gazeConfigsProperty` field referencing the old `InputSystemAdapterBinding._gazeConfigs` path.
2. The `FacialCharacterProfileSOInspector` source shall not retain `AppendGazeConfigForExpression`, `RemoveGazeConfigByExpressionId`, `FindGazeConfigIndexByExpressionId`, or `HasGazeConfigForExpression` methods that operate against the old structure.
3. The `FacialCharacterProfileSOInspector` source shall not retain the `BuildEyeLookFields`, `BuildGazeClipField`, or `OnBuildAnalogExpressionInputSourceFields` callbacks that wired gaze fields into Expression rows under the old structure.
4. Where the new GazeConfigs section requires equivalent behavior (e.g. a method to append a GazeConfig for a chosen Expression), the `FacialCharacterProfileSOInspector` shall introduce a new implementation aligned with the SO-root structure rather than reusing legacy method names.
5. The `FacialCharacterProfileSOInspector` source shall compile under the project's standard Editor asmdef without referencing any removed type or member.

### Requirement 10: Runtime 結線 (InputSystemAdapterBinding.OnStart)

**Objective:** Unity エンジニアとして、ランタイムで `InputSystemAdapterBinding` が SO ルートの `_gazeConfigs` と自身の `_gazeInputBindings` を組み合わせて gaze provider を構築する挙動を確実に得たい。

#### Acceptance Criteria

1. When `InputSystemAdapterBinding.OnStart` is invoked, the `InputSystemAdapterBinding` shall iterate the SO root `_gazeConfigs` and, for each entry, look up the matching `InputSystemGazeBinding` in `_gazeInputBindings` by `expressionId`.
2. When a matching `(GazeBindingConfig, InputSystemGazeBinding)` pair is found at `OnStart`, the `InputSystemAdapterBinding` shall construct a gaze provider using the bone paths / ranges / look-clips from the `GazeBindingConfig` and the resolved `InputAction` from the `InputSystemGazeBinding`.
3. If a `GazeBindingConfig` has no matching `InputSystemGazeBinding`, then the `InputSystemAdapterBinding` shall skip wiring that gaze entry without raising an exception.
4. If an `InputSystemGazeBinding` has no matching `GazeBindingConfig` in the SO root collection, then the `InputSystemAdapterBinding` shall log a warning and skip that binding (per Requirement 1.6) without raising an exception.
5. The `InputSystemAdapterBinding` runtime path shall not read any gaze-related field (bone paths, ranges, look-clips) from itself; it shall obtain such fields exclusively via the SO root `_gazeConfigs`.
6. The runtime gaze-construction path shall preserve the existing `IBonePoseSource` (or equivalent) contract so that future OSC / ARKit native gaze drivers can plug into the same construction without further core changes.

### Requirement 11: テスト戦略

**Objective:** Unity エンジニアとして、データモデル変更と Inspector UX 変更の双方に対して TDD（Red-Green-Refactor）でカバレッジを保ちたい。EditMode / PlayMode は実行時要件で配置を分けたい。

#### Acceptance Criteria

1. The EditMode test suite for `com.hidano.facialcontrol` shall include a round-trip test that converts a `profile.json` with root-level `gaze_configs[]` into a SO and back, asserting `_gazeConfigs` equality.
2. The EditMode test suite shall include `FacialCharacterProfileConverter` tests asserting that root-level `gaze_configs[]` is mapped to the SO root `_gazeConfigs`.
3. The EditMode test suite shall include `FacialCharacterProfileExporter` tests asserting that the SO root `_gazeConfigs` is serialized to root-level `gaze_configs[]`.
4. The EditMode test suite shall include `FacialCharacterProfileSOInspector` tests covering: opt-in addition via the Analog Expression dropdown, single-row removal, bulk removal triggered by Expression deletion, bulk removal triggered by Analog→Digital kind transition, single-row "参照モデルから自動設定" overwrite, bulk "再解決" overwrite, and reference-model assignment auto-fill that preserves manually set non-empty values.
5. The PlayMode test suite shall update `InputSystemAdapterBindingIntegrationTests.OnStart_GazePath_*` cases so that they assert the new structure (SO root `_gazeConfigs` + binding `_gazeInputBindings`).
6. If a previously existing test referenced `InputSystemAdapterBinding._gazeConfigs` or `GazeExpressionConfig` bone-path fields, then that test shall be updated or removed so that no test references the old structure after this spec is implemented.
7. The TDD workflow shall be Red → Green → Refactor: each new behavior shall first appear as a failing test, then a minimal implementation, then refactor while keeping tests green.

### Requirement 12: スコープ外事項の明示

**Objective:** プロジェクトのレビュアおよび将来の自分として、本 spec が触らない領域を要件レベルで固定したい。これにより別 spec / 別 PR ネタの境界を保てる。

#### Acceptance Criteria

1. The `gaze-config-promotion` deliverables shall not introduce a new `Vector2` value into `ExpressionKind`.
2. The `gaze-config-promotion` deliverables shall not implement OSC-driven or ARKit-driven gaze provider behavior; only the contract surface (e.g. `IBonePoseSource`) shall remain extensible.
3. The `gaze-config-promotion` deliverables shall not change the runtime behavior of BlendShape-only gaze (i.e. gaze paths that drive BlendShapes rather than bones) beyond the schema relocation required by Requirement 2.
4. The `gaze-config-promotion` deliverables shall not introduce automatic blink behavior or visual gaze-target follow behavior; those are deferred to a preview.2-or-later spec.
5. The `gaze-config-promotion` deliverables shall not relativize bone paths; bone-path relativization is tracked under backlog item S-1 and is out of scope for this spec.
6. If a contributor needs functionality listed in this Requirement, then the contributor shall record it under `docs/backlog.md` or open a new spec rather than expanding `gaze-config-promotion`.
