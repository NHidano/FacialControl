# Requirements Document

## Project Description (Input)
FacialCharacterSO の Inspector UX 改善とデータモデル簡素化のための破壊的改修。Expression を AnimationClip 参照ベースに刷新し（Name=ファイル名、Id=GUID 自動生成、Layer=ドロップダウン、TransitionDuration / TransitionCurve / BlendShapeValues / RendererPath / BonePose は AnimationClip から自動取得・吸収）、LayerSlot を「Override 対象レイヤーのビットフラグ」に簡素化、RendererPath 手入力欄を廃止、BonePose 独立データを AnimationClip に統合、アナログバインディングの AnalogMappingFunction (deadzone/scale/offset/curve/invert/clamp) を InputAction Processor 化して AnalogBindingEntry を InputActionReference + targetIdentifier + targetAxis のみに簡素化、ExpressionBindingEntry の Category (Controller/Keyboard) を InputAction binding の device class から自動推定するように廃止、KeyboardExpressionInputSource / ControllerExpressionInputSource を単一 ExpressionInputSourceAdapter に統合。preview 段階のため schema 互換性は破壊する。Runtime は引き続き JSON ベースで動作させ、Editor 保存時に AnimationClip → 中間 JSON 表現にサンプリングして焼き込む。

## Introduction

本仕様は、FacialControl パッケージ（preview 段階）における `FacialCharacterSO` Inspector の UX 改善とコアデータモデル簡素化を目的とした破壊的改修である。Unity エンジニアが Inspector 上で表情・入力バインディング設定を行う際の冗長さ・誤入力リスク・概念重複を排除し、AnimationClip を真実の源（Source of Truth）とする統一モデルへ移行する。preview 段階のため schema 互換性は破壊するが、移行ガイドを提供して既存ユーザーの移行を支援する。Runtime は引き続き JSON ファースト原則を維持し、Editor 保存時のみ AnimationClip → 中間 JSON サンプリングを実行する。

## Boundary Context

- **In scope**:
  - `Expression` データモデルの AnimationClip 参照ベース化と関連プロパティ（TransitionDuration / TransitionCurve / BlendShapeValues / RendererPath / BonePose）の吸収
  - `LayerSlot` の Override 対象レイヤーのビットフラグへの簡素化
  - `RendererPath` 手入力欄の Inspector からの廃止と AnimationClip binding path からの自動取得
  - `BonePose` 独立データの AnimationClip への統合
  - `AnalogMappingFunction` の InputAction Processor 化と `AnalogBindingEntry` の簡素化
  - `ExpressionBindingEntry.Category` の廃止と device class からの自動推定
  - `KeyboardExpressionInputSource` / `ControllerExpressionInputSource` の `ExpressionInputSourceAdapter` への統合
  - Editor 保存時 AnimationClip → 中間 JSON サンプリング
  - 移行ガイドドキュメントの提供
- **Out of scope**:
  - Runtime ランタイムの JSON フォーマット根本刷新（既存 JSON ファースト原則は維持し、中間 JSON 表現の追加のみ）
  - 新規入力デバイス対応（VR / モバイル）
  - Timeline 統合
  - VRM 対応（リリース後マイルストーン）
  - 既存 schema を読み込む後方互換シリアライザの実装（preview 段階のため不要）
- **Adjacent expectations**:
  - `com.hidano.facialcontrol`（コア）: Domain の `Expression` / `LayerSlot` / `BonePose` モデル、Adapters の JSON パーサー / ScriptableObject 変換、Editor の Inspector
  - `com.hidano.facialcontrol.inputsystem`: `AnalogBindingEntry` / `ExpressionBindingEntry` / `*ExpressionInputSource` 系統、`InputBindingProfileSO`、`Multi Source Blend Demo` サンプル
  - クリーンアーキテクチャ契約: Domain は Unity 非依存維持、Editor 専用機能は Runtime に混入させない
  - パフォーマンス契約: 毎フレームのヒープ確保ゼロ目標、AnimationClip サンプリングは Editor 専用

## Requirements

### Requirement 1: Expression データモデルの AnimationClip 参照ベース化
**Objective:** Unity エンジニアとして、Expression を AnimationClip 参照だけで定義したい。これにより、表情データの真実の源を AnimationClip に一元化し、Inspector 上の冗長な手入力や二重管理を排除できる。

#### Acceptance Criteria
1. The Expression data model shall hold an AnimationClip reference, an automatically generated Id (GUID), a Name derived from the AnimationClip file name, and a Layer selected via dropdown.
2. When an AnimationClip is assigned to an Expression, the FacialCharacterSO Inspector shall populate Name from the AnimationClip asset file name without stem extension.
3. When a new Expression is created, the FacialCharacterSO Inspector shall generate a fresh GUID as the Id and persist it.
4. When the Expression Inspector renders a Layer field, the FacialCharacterSO Inspector shall display the available layers as a dropdown sourced from the configured layer definitions.
5. The Expression data model shall NOT expose TransitionDuration, TransitionCurve, BlendShapeValues, RendererPath, or BonePose as independent serialized fields, because these values are derived from the referenced AnimationClip.
6. When the AnimationClip reference is null, the FacialCharacterSO Inspector shall display a validation error and shall prevent saving the Expression entry.
7. If two Expression entries share the same Id within a single FacialCharacterSO, the FacialCharacterSO Inspector shall display a duplicate-Id error and shall block save.

### Requirement 2: AnimationClip からの派生プロパティ自動取得
**Objective:** Unity エンジニアとして、TransitionDuration や RendererPath、BlendShape 値、BonePose を AnimationClip から自動的に得たい。これにより、AnimationClip と Expression エントリ間の整合性ズレを防止できる。

#### Acceptance Criteria
1. The Expression Resolver shall extract BlendShape value snapshots from the referenced AnimationClip by sampling its curves.
2. The Expression Resolver shall extract bone pose data (translation / rotation / scale curves) from the referenced AnimationClip and shall treat them as the bone pose layer for that Expression.
3. The Expression Resolver shall extract renderer paths from the AnimationClip's curve binding paths and shall use them as the target renderer references.
4. When an AnimationClip contains TransitionDuration metadata via clip events or naming convention, the Expression Resolver shall apply it to the Expression's transition duration.
5. When the AnimationClip lacks explicit transition duration metadata, the Expression Resolver shall fall back to the default transition duration of 0.25 seconds.
6. The Expression Resolver shall extract transition curve information when present in the AnimationClip and shall fall back to linear interpolation when absent.
7. If the AnimationClip contains a binding the resolver cannot interpret (e.g., unsupported property type), the Expression Resolver shall log a warning via Debug.LogWarning and shall skip that binding without aborting resolution.

### Requirement 3: LayerSlot のビットフラグ簡素化
**Objective:** Unity エンジニアとして、LayerSlot を「どのレイヤーを Override 対象とするか」のビットフラグだけで指定したい。これにより、レイヤー指定モデルの複雑な状態を排除し、設定意図を明示できる。

#### Acceptance Criteria
1. The LayerSlot data model shall be defined as a flags enumeration representing the set of layers to override.
2. When an Expression's LayerSlot has multiple flags set, the Expression Resolver shall treat the Expression as overriding all flagged layers atomically within a single transition.
3. The FacialCharacterSO Inspector shall display LayerSlot as a multi-select flags field with checkboxes for each defined layer.
4. The LayerSlot model shall NOT contain layer priority, blend mode, or any per-layer parameter beyond the override flags themselves.
5. If LayerSlot has zero flags set, the FacialCharacterSO Inspector shall display a validation error indicating that at least one override target is required.

### Requirement 4: RendererPath 手入力欄の廃止
**Objective:** Unity エンジニアとして、RendererPath を Inspector で手入力したくない。これにより、Hierarchy 階層変更時の path 不整合や typo による無効バインディングを排除できる。

#### Acceptance Criteria
1. The FacialCharacterSO Inspector shall NOT display a manual RendererPath text input field for Expressions.
2. The Expression Resolver shall derive renderer paths from the AnimationClip's curve binding paths.
3. When the AnimationClip contains binding paths, the FacialCharacterSO Inspector shall display the resolved renderer paths in a read-only summary view for verification.
4. If no binding paths are present in the AnimationClip, the FacialCharacterSO Inspector shall display an informational hint explaining that the AnimationClip has no renderer-targeted curves.
5. When the user changes the AnimationClip reference, the FacialCharacterSO Inspector shall refresh the displayed renderer paths automatically.

### Requirement 5: BonePose 独立データの AnimationClip 統合
**Objective:** Unity エンジニアとして、BonePose を Expression の独立フィールドとして管理したくない。これにより、表情データを AnimationClip 内に一元化でき、ScriptableObject と AnimationClip の二重編集を不要にできる。

#### Acceptance Criteria
1. The Expression data model shall NOT contain a standalone BonePose serialized field.
2. The Expression Resolver shall extract bone pose information solely from the referenced AnimationClip's transform curves.
3. When an AnimationClip lacks bone curves, the Expression Resolver shall treat the Expression as having no bone pose contribution.
4. The migration guide shall describe how to convert pre-redesign BonePose data into AnimationClip transform curves.

### Requirement 6: AnalogMappingFunction の InputAction Processor 化
**Objective:** Unity エンジニアとして、deadzone / scale / offset / curve / invert / clamp といったアナログ変換ロジックを InputAction Asset の Processor チェーンとして表現したい。これにより、Unity InputSystem 標準の編集 UI と再利用可能なプリセットを利用できる。

#### Acceptance Criteria
1. The FacialControl InputSystem package shall provide custom InputProcessor implementations for deadzone, scale, offset, curve, invert, and clamp.
2. The AnalogBindingEntry data model shall consist solely of an InputActionReference, a targetIdentifier, and a targetAxis.
3. The AnalogBindingEntry data model shall NOT contain an embedded AnalogMappingFunction or any equivalent inline parameter set.
4. When the InputSystem dispatches a value through a bound InputAction, the ExpressionInputSourceAdapter shall consume the post-processor value as the final analog input without further transformation.
5. Where existing analog mapping configurations need to be preserved during migration, the migration guide shall describe how to translate AnalogMappingFunction parameters into equivalent processor strings on the InputAction.
6. If a user attaches an unsupported processor combination, the ExpressionInputSourceAdapter shall log a warning via Debug.LogWarning and shall continue with the raw value.

### Requirement 7: ExpressionBindingEntry.Category の自動推定
**Objective:** Unity エンジニアとして、コントローラかキーボードかを ExpressionBindingEntry に手動で指定したくない。これにより、入力デバイスの分類ミスを排除し、設定項目を 1 つ削減できる。

#### Acceptance Criteria
1. The ExpressionBindingEntry data model shall NOT contain a Category field for Controller / Keyboard distinction.
2. The ExpressionInputSourceAdapter shall infer the device category from the InputAction binding's device class at runtime.
3. When an InputAction binding targets a Keyboard device, the ExpressionInputSourceAdapter shall classify the input source as keyboard.
4. When an InputAction binding targets a Gamepad / Joystick / XRController device, the ExpressionInputSourceAdapter shall classify the input source as controller.
5. If a binding targets a device class the adapter does not recognize, the ExpressionInputSourceAdapter shall log a warning via Debug.LogWarning and shall fall back to the controller classification.

### Requirement 8: ExpressionInputSourceAdapter への統合
**Objective:** Unity エンジニアとして、Keyboard 用と Controller 用に分かれた MonoBehaviour を 1 つに統合したい。これにより、Scene 配置とコンポーネント管理を簡素化し、入力ソース追加時の二重実装を防げる。

#### Acceptance Criteria
1. The FacialControl InputSystem package shall provide a single ExpressionInputSourceAdapter component.
2. The FacialControl InputSystem package shall mark KeyboardExpressionInputSource and ControllerExpressionInputSource as removed and shall NOT include them in the redesigned API surface.
3. When ExpressionInputSourceAdapter is attached to a GameObject, the adapter shall handle both keyboard and controller bindings transparently based on the bound InputActions.
4. While ExpressionInputSourceAdapter is enabled, the adapter shall route all bound InputAction events to the corresponding ExpressionTrigger or ValueProvider sinks.
5. The migration guide shall describe how to replace existing KeyboardExpressionInputSource / ControllerExpressionInputSource components with ExpressionInputSourceAdapter on existing scenes and prefabs.

### Requirement 9: Editor 保存時の AnimationClip → 中間 JSON サンプリング
**Objective:** Unity エンジニアとして、Runtime では JSON ベースで動作する設計を維持したまま AnimationClip ベースの編集体験を得たい。これにより、JSON ファースト永続化原則を保ちつつ Inspector の UX を改善できる。

#### Acceptance Criteria
1. When the FacialCharacterSO Inspector saves the asset, the Editor sampler shall sample each Expression's AnimationClip into an intermediate JSON representation.
2. The intermediate JSON representation shall contain the BlendShape values, bone pose snapshots, and renderer paths derived from the AnimationClip.
3. The Runtime resolver shall consume the intermediate JSON representation without depending on the AnimationClip asset at runtime.
4. The Editor sampler shall be implemented in the Editor assembly only and shall NOT be referenced from Runtime assemblies.
5. While the Editor is sampling AnimationClips, the FacialCharacterSO Inspector shall display progress feedback if the operation exceeds 200 milliseconds.
6. If AnimationClip sampling fails for any Expression, the Editor sampler shall log an error via Debug.LogError and shall abort the save operation for that Expression entry.
7. The intermediate JSON schema shall version-tag each entry to allow future format evolution.

### Requirement 10: 破壊的変更と移行ガイド提供
**Objective:** preview 段階の利用者として、本改修が schema 互換性を破壊することを明確に把握し、既存資産を移行する手順を入手したい。これにより、preview 利用者の移行コストを最小化できる。

#### Acceptance Criteria
1. The redesigned FacialCharacterSO shall NOT load pre-redesign serialized assets without explicit migration.
2. The migration guide shall document the schema differences between the pre-redesign and post-redesign data models.
3. The migration guide shall describe step-by-step procedures for converting existing FacialCharacterSO assets, BonePose data, AnalogMappingFunction parameters, and ExpressionBindingEntry.Category values.
4. The migration guide shall reside under the package's Documentation~ folder.
5. Where automatic migration is feasible for trivial cases (e.g., default-only AnalogMappingFunction), the migration guide shall describe the available Editor menu commands or note their absence explicitly.
6. The CHANGELOG entry for this change shall mark the release as a breaking change and shall link to the migration guide.

### Requirement 11: パフォーマンス契約の維持
**Objective:** Unity エンジニアとして、本改修が毎フレームのヒープ確保ゼロ目標を破壊しないことを保証したい。これにより、同時 10 体以上のキャラクター制御における GC スパイクを回避できる。

#### Acceptance Criteria
1. While the Runtime resolver is consuming the intermediate JSON representation, the resolver shall NOT allocate managed heap memory per frame for steady-state Expression evaluation.
2. The Runtime resolver shall NOT call AnimationClip sampling APIs at runtime, because sampling is an Editor-only concern.
3. The ExpressionInputSourceAdapter shall NOT allocate per-frame garbage when dispatching InputAction events to ExpressionTrigger or ValueProvider sinks.
4. When the FacialCharacterSO is loaded at runtime, the Runtime resolver shall preallocate caches for BlendShape indices, bone references, and renderer references.
5. The performance test suite under Tests/PlayMode/Performance shall verify zero per-frame allocations for the Runtime resolver and ExpressionInputSourceAdapter using GC.GetTotalMemory delta or Unity Profiler markers.

### Requirement 12: テスト要件（TDD 厳守）
**Objective:** Unity エンジニアとして、本改修がプロジェクトの TDD 原則と EditMode/PlayMode 配置基準に従うことを保証したい。これにより、回帰防止と CI 安定性を維持できる。

#### Acceptance Criteria
1. The development team shall follow Red-Green-Refactor for every behavior introduced by this redesign.
2. The Tests/EditMode suite shall cover Expression resolution from AnimationClip mock data, intermediate JSON serialization / deserialization, LayerSlot flag evaluation, and AnalogMappingFunction-to-processor migration logic.
3. The Tests/PlayMode suite shall cover ExpressionInputSourceAdapter behavior with the actual InputSystem, transition interpolation across multiple layers via the LayerSlot flags, and zero-allocation runtime resolution.
4. When a test verifies AnimationClip sampling, the test shall reside under Tests/EditMode because AnimationClip sampling is Editor-only.
5. When a test verifies InputSystem-bound device categorization, the test shall reside under Tests/PlayMode because InputSystem device simulation requires the Player loop.
6. Test class names shall follow `{Target}Tests` and method names shall follow `{Method}_{Condition}_{Expected}`.
7. The test suite shall not introduce per-frame allocations in PlayMode performance tests verifying Requirement 11.

### Requirement 13: クリーンアーキテクチャ契約の維持
**Objective:** Unity エンジニアとして、本改修が Domain / Application / Adapters / Editor のレイヤー分離を破壊しないことを保証したい。これにより、Runtime UI 提供禁止・Engine 非依存 Domain・Editor 専用機能の隔離といった既存契約を維持できる。

#### Acceptance Criteria
1. The Expression data model in Domain shall NOT reference UnityEngine.AnimationClip directly; instead, the Adapters layer shall hold the AnimationClip reference and provide the resolved intermediate JSON to Domain.
2. The Editor sampler shall live under the Editor assembly with `includePlatforms: ["Editor"]` and shall NOT be reachable from Runtime assemblies.
3. The ExpressionInputSourceAdapter shall reside in the `com.hidano.facialcontrol.inputsystem` Adapters layer and shall NOT leak InputSystem types into Domain.
4. The Domain layer shall remain free of UnityEngine.* references except `Unity.Collections` as already permitted by the project contract.
5. While the Inspector is rendered, the Editor module shall use UI Toolkit and shall NOT introduce new IMGUI panels.
