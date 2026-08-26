# Implementation Plan: inspector-and-data-model-redesign

> **Mode**: 6-Phase Incremental Implementation (Hybrid Option C, design.md 採用)
> **TDD**: 各タスクで Red → Green → Refactor を完結。Red 段階のテスト一覧と Green 段階の実装変更概要を必ず明示。
> **Test 配置**: AnimationClip サンプリング系は EditMode（Req 12.4）。InputSystem device categorization は PlayMode（Req 12.5）。0-alloc 検証は PlayMode/Performance（Req 11.5）。
> **粒度**: 各サブタスクは 1 PR 単位（4–8 時間目安）。
> **Parallel marker `(P)`**: 同一 Phase 内で `_Boundary:_` が重ならない場合に付与。Phase 境界は逐次実行。
> **要件 ID 規約**: `_Requirements:_` には数値 ID のみカンマ区切りで列挙する（例: `1.1, 1.5, 13.1`）。

---

## Phase 1: Domain 新型追加（bridge 期間維持）

- [x] 1. Phase 1 Foundation — Domain 新型 + 既存 Domain Tests 緑維持
- [x] 1.1 Domain 値型 LayerOverrideMask を追加し、bit 演算と layer 名配列との往復を確立する
  - `[Flags] enum : int`（32 bit）として宣言、`None / Bit0 .. Bit31` を定義
  - bit position と layer 名の対応表は Adapters 側のヘルパーが持つこと（Domain は関知しない）を README コメントで明示
  - **Red**: `LayerOverrideMaskTests`（`Combine_TwoFlags_HasBoth`, `HasFlag_AbsentBit_ReturnsFalse`, `None_HasNoBits_ReturnsTrue`, `BitPositionCount_Equals32`）を新規追加
  - **Green**: `Runtime/Domain/Models/LayerOverrideMask.cs` を新規作成し flags enum を実装
  - **Refactor**: XML doc コメントを追記、`research.md` Topic 9 のリンクを README コメントに残す
  - 完了基準: 新規テストが 4 本緑、既存 Domain テストは無傷
  - _Requirements: 3.1, 3.4, 13.1, 13.4_
  - _Boundary: Domain.Models.LayerOverrideMask_

- [x] 1.2 (P) Domain 値型 BlendShapeSnapshot / BoneSnapshot を追加し、防御コピーと不変性を保証する
  - `BlendShapeSnapshot`: `RendererPath / Name / Value` の `readonly struct`
  - `BoneSnapshot`: `BonePath / Position(X,Y,Z) / Euler(X,Y,Z) / Scale(X,Y,Z)` の `readonly struct`
  - **Red**: `BlendShapeSnapshotTests`（`Ctor_Stores_AllFields`, `Equality_SameValues_AreEqual`）と `BoneSnapshotTests`（`Ctor_Stores_AllNineFloats`）を追加
  - **Green**: `Runtime/Domain/Models/BlendShapeSnapshot.cs` / `BoneSnapshot.cs` を新規作成
  - **Refactor**: 既存 `BonePose` / `BonePoseEntry` との重複コメントを残し、Phase 3 で削除予定であることを明示
  - 完了基準: 各 struct のコンストラクタ + 等価性テストが緑
  - _Requirements: 1.5, 2.1, 2.2, 9.2, 13.1_
  - _Boundary: Domain.Models.BlendShapeSnapshot, Domain.Models.BoneSnapshot_

- [x] 1.3 (P) Domain 値型 ExpressionSnapshot を追加し、AnimationClip 由来 snapshot の Domain 受け皿を成立させる
  - `ExpressionSnapshot`: `Id / TransitionDuration / TransitionCurvePreset / ReadOnlyMemory<BlendShapeSnapshot> / ReadOnlyMemory<BoneSnapshot> / ReadOnlyMemory<string> RendererPaths`
  - `TransitionCurvePreset` enum を新規定義（`Linear=0, EaseIn=1, EaseOut=2, EaseInOut=3`）
  - **Red**: `ExpressionSnapshotTests`（`Ctor_Defensive_Copies_BlendShapes`, `Ctor_NullArrays_ProducesEmptyMemory`, `TransitionCurvePreset_DefaultsTo_Linear`, `TransitionDuration_Default_Is_PointTwoFive`）を追加
  - **Green**: `Runtime/Domain/Models/ExpressionSnapshot.cs` と `Runtime/Domain/Models/TransitionCurvePreset.cs` を新規作成。コンストラクタで `BlendShapeSnapshot[]` / `BoneSnapshot[]` を防御コピーし `ReadOnlyMemory<T>` で公開
  - **Refactor**: 既存 `TransitionCurve` enum との衝突回避方針をコメントに残す（Phase 3 で `TransitionCurve` 側を削除）
  - 完了基準: 4 本のテストが緑、`ReadOnlyMemory<T>` 経由で外部から書換不可
  - _Requirements: 1.5, 2.1, 2.2, 2.5, 2.6, 9.2, 13.1_
  - _Boundary: Domain.Models.ExpressionSnapshot, Domain.Models.TransitionCurvePreset_
  - _Depends: 1.2_

- [x] 1.4 Phase 1 完了レビュー — Domain 新型 4 種の API surface 確認と asmdef 静的検査
  - `Hidano.FacialControl.Domain.asmdef` の `references` に `UnityEngine.AnimationClip` 等が混入していないことを確認
  - 既存 `Expression` / `LayerSlot` / `BonePose` は本 Phase では変更しないこと（bridge 期間）を CI で確認
  - 完了基準: Unity Test Runner の Domain test category 全緑、`grep` で Domain 配下に `UnityEngine.AnimationClip` 参照ゼロ
  - _Requirements: 13.1, 13.4_
  - _Boundary: Domain.Models_
  - _Depends: 1.1, 1.2, 1.3_

---

## Phase 2: AnimationClip サンプラ実装（Editor 専用）

- [x] 2. Phase 2 — AnimationClip → ExpressionSnapshot サンプラと中間 JSON v2.0 schema を成立させる

- [x] 2.1 Editor 専用サンプラの interface と stateless 実装を導入し、AnimationUtility 経由で時刻 0 値を取得する
  - `IExpressionAnimationClipSampler` interface（`SampleSnapshot(snapshotId, clip)` と `SampleSummary(clip)` の 2 メソッド）を Editor asmdef 配下に新規定義
  - `AnimationClipExpressionSampler` 実装: `AnimationUtility.GetCurveBindings(clip)` で binding 列挙、`AnimationUtility.GetEditorCurve(clip, binding).Evaluate(0f)` で時刻 0 値取得
  - BlendShape binding は `binding.propertyName.StartsWith("blendShape.")` で判別、Transform binding は `m_LocalPosition.{x,y,z}` / `m_LocalRotation` 系 / `m_LocalScale.{x,y,z}` で判別
  - unsupported binding は `Debug.LogWarning` + skip（Req 2.7）
  - **Red**: `AnimationClipExpressionSamplerTests`（EditMode）に以下を追加
    - `SampleSnapshot_BlendShapeCurves_ReturnsValuesAtTimeZero`
    - `SampleSnapshot_TransformCurves_ReturnsBoneSnapshot`
    - `SampleSnapshot_UnsupportedBinding_LogsWarningAndSkips`
    - `SampleSummary_ReturnsRendererPathsAndBlendShapeNames`
    - `SampleSnapshot_NullClip_Throws`
  - **Green**: `Editor/Sampling/IExpressionAnimationClipSampler.cs` と `AnimationClipExpressionSampler.cs` を実装。テンポラリ AnimationClip を `new AnimationClip()` で生成しキーフレームを `AnimationUtility.SetEditorCurve` で挿入してテスト
  - **Refactor**: BlendShape / Transform binding 判別ロジックを private static helper に抽出
  - 完了基準: 5 本のテストが EditMode で緑、`Hidano.FacialControl.Editor.asmdef` の `includePlatforms` が `["Editor"]` であることを確認
  - _Requirements: 2.1, 2.2, 2.3, 2.7, 9.4, 13.2_
  - _Boundary: Editor.Sampling_
  - _Depends: 1.3_

- [x] 2.2 (P) AnimationEvent 経由の TransitionDuration / TransitionCurvePreset メタデータを抽出する
  - 予約 AnimationEvent: `functionName == "FacialControlMeta_Set"`, `stringParameter` でキー識別（`"transitionDuration"` / `"transitionCurvePreset"`）, `floatParameter` で値運搬
  - 同名 key が複数あれば最初の 1 個のみ採用、警告 1 回
  - 不在時 fallback: `TransitionDuration = 0.25f`, `TransitionCurve = Linear`（Req 2.5, 2.6）
  - **Red**: `AnimationClipExpressionSamplerMetadataTests`（EditMode）に以下を追加
    - `SampleSnapshot_NoMetadata_FallsBackToDefaults`
    - `SampleSnapshot_DurationEvent_AppliesDuration`
    - `SampleSnapshot_CurvePresetEvent_AppliesPreset`
    - `SampleSnapshot_DuplicateKey_LogsWarningAndUsesFirst`
  - **Green**: サンプラに `AnimationUtility.GetAnimationEvents(clip)` 経由のメタデータ抽出ロジックを追加
  - **Refactor**: 予約 functionName をクラス定数 `MetaSetFunctionName = "FacialControlMeta_Set"` として公開
  - 完了基準: 4 本のテストが緑、fallback 動作が Req 2.5/2.6 通り
  - _Requirements: 2.4, 2.5, 2.6_
  - _Boundary: Editor.Sampling_
  - _Depends: 2.1_

- [x] 2.3 中間 JSON schema v2.0 の DTO を新設し、snapshot table 形式の round-trip を確立する
  - 新設 DTO: `ExpressionSnapshotDto` / `BlendShapeSnapshotDto` / `BoneSnapshotDto`
  - 改修 DTO: `ExpressionDto` を `id / name / layer / layerOverrideMask: List<string> / snapshot: ExpressionSnapshotDto` に再構成。旧 `transitionDuration / transitionCurve / blendShapeValues / layerSlots` field は撤去
  - JSON 例（design.md "Logical Data Model" セクション準拠）:
    - `schemaVersion: "2.0"`, `layers[]`, `expressions[].snapshot.{transitionDuration, transitionCurvePreset, blendShapes[], bones[], rendererPaths[]}`, top-level `rendererPaths[]`
  - **Red**: `IntermediateJsonSchemaV2Tests`（EditMode）に以下を追加
    - `RoundTrip_FullSnapshot_PreservesAllFields`
    - `Parse_SchemaVersionMismatch_ThrowsInvalidOperation`
    - `Parse_MissingSnapshot_ProducesEmptySnapshot`
    - `RendererPaths_AreSubset_Of_TopLevelRendererPaths`
  - **Green**: `Runtime/Adapters/Json/Dto/ExpressionSnapshotDto.cs` 等を新設、`SystemTextJsonParser.cs` で `schemaVersion == "2.0"` strict チェックを追加（Req 10.1）
  - **Refactor**: 既存 `BonePoseDto` / `BonePoseEntryDto` は本 Phase では削除せず Obsolete マーク（Phase 3 で物理削除）
  - 完了基準: 4 本のテストが緑、schema v1.0 を渡すと `InvalidOperationException` + `Debug.LogError`
  - _Requirements: 9.1, 9.2, 9.7, 10.1_
  - _Boundary: Adapters.Json.Dto, Adapters.Json.SystemTextJsonParser_
  - _Depends: 1.3_

- [x] 2.4 サンプラの Editor 専用境界とパフォーマンスを CI で保証する
  - asmdef 静的検査: `Hidano.FacialControl.Editor.asmdef` の `includePlatforms: ["Editor"]` を確認
  - 1 Expression あたり 50ms 以内（典型 ~10 BlendShapes）の EditMode benchmark を `AnimationClipExpressionSamplerBenchmarkTests` で計測（`UnityEngine.TestTools.Performance` または `Stopwatch` 比較）
  - サンプラを Runtime asmdef から参照しようとする コードが書けないことを Test asmdef で確認（コンパイルエラーになる経路を意図的に test fixture に残さない）
  - **Red**: `EditorOnlyVisibilityTests`（EditMode）に「Runtime asmdef 名から `Editor.Sampling` namespace を `System.Reflection` で探索しても見つからない」テストを追加
  - **Green**: 既に Editor asmdef なので追加実装不要。テストで保証
  - **Refactor**: ベンチマーク結果を `research.md` Topic 5 セクションに追記
  - 完了基準: benchmark テストが 50ms 未満で緑、Editor 専用可視性テストが緑
  - _Requirements: 9.4, 11.2, 13.2_
  - _Boundary: Editor.Sampling, asmdef_
  - _Depends: 2.1, 2.2, 2.3_

---

## Phase 3: Domain Expression / LayerSlot / FacialProfile 破壊書換

- [x] 3. Phase 3 — Domain 中核モデル置換と Tests 大量書換（Phase 1〜2 で bridge した上で破壊実行）

- [x] 3.1 Expression struct を SnapshotId 参照型へ破壊置換し、派生 5 値の field を撤去する
  - 旧 field 撤去: `TransitionDuration / TransitionCurve / BlendShapeValues / LayerSlots`（Req 1.5）
  - 新 field: `Id / Name / Layer / OverrideMask: LayerOverrideMask / SnapshotId: string`
  - **Red**: 既存 `ExpressionTests` を全面書き換え。新規追加: `Ctor_StoresAllFields`, `OverrideMask_DefaultsToNone_AllowedByDomain`, `SnapshotId_NonEmpty`
  - **Green**: `Runtime/Domain/Models/Expression.cs` を破壊書換。`Application/UseCases/ExpressionUseCase.cs` の `Activate(Expression)` シグネチャは維持
  - **Refactor**: `ToString()` を `$"{Id}:{Name}@{Layer}"` 形式に統一
  - 完了基準: `ExpressionTests` 緑、Application 層の既存テスト緑（API 維持確認）
  - _Requirements: 1.1, 1.5, 5.1, 13.1_
  - _Boundary: Domain.Models.Expression_
  - _Depends: 1.4, 2.4_

- [x] 3.2 LayerSlot struct を撤去し、LayerOverrideMask への置換を完了する
  - `Runtime/Domain/Models/LayerSlot.cs` を物理削除
  - 旧 `LayerSlot` を参照していた箇所（Adapters Serializable / Json DTO / Tests）を全て `LayerOverrideMask` 経由に置換
  - **Red**: 既存 `LayerSlotTests.cs` を物理削除し、内容を `LayerOverrideMaskTests` 側へ移行確認（既に Phase 1 で網羅済）
  - **Green**: `LayerSlot` 参照を grep で 0 件にする。`Adapters/ScriptableObject/Serializable/LayerSlotSerializable.cs` を `LayerOverrideMaskSerializable.cs` に rename + 内容置換
  - **Refactor**: 旧 layer 名 + BlendShapeValues 配列構造を持つ Tests を全面削除
  - 完了基準: コードベース全体で `LayerSlot` 参照ゼロ、CI 緑
  - _Requirements: 3.1, 3.4, 13.1_
  - _Boundary: Domain.Models.LayerSlot, Adapters.ScriptableObject.Serializable.LayerSlotSerializable_
  - _Depends: 3.1_

- [x] 3.3 FacialProfile から BonePoses 配列を撤去し、Expression snapshot 経路に一元化する
  - `FacialProfile.BonePoses` プロパティを撤去（Req 5.1）
  - `BonePose.cs` / `BonePoseEntry.cs` を物理削除（snapshot 系へ完全移行）
  - 既存 `BonePoseComposer` は Adapters Bone 層から呼ばれるため保持。但し input は `BoneSnapshot` 経由に変更
  - **Red**: 既存 `FacialProfileBonePosesBackwardCompatTests.cs`, `BonePoseTests.cs`, `BonePoseEntryTests.cs`, `BonePoseHotPathCtorTests.cs` を物理削除。`FacialProfileTests` を BonePoses 不在で書き換え
  - **Green**: `Runtime/Domain/Models/FacialProfile.cs` から BonePoses 関連 API を削除。`BonePoseComposer` の引数を `BoneSnapshot` 受けに変更
  - **Refactor**: Adapters の `AnalogBonePoseProvider` / `BoneTransformResolver` / `BoneWriter` の入力型を `BoneSnapshot` 経路に揃える
  - 完了基準: `BonePose` / `BonePoseEntry` 参照ゼロ、`FacialProfile.BonePoses` 参照ゼロ、CI 緑
  - _Requirements: 5.1, 5.2, 5.3, 13.1_
  - _Boundary: Domain.Models.FacialProfile, Domain.Models.BonePose, Adapters.Bone_
  - _Depends: 3.1, 1.2_

- [x] 3.4 ExpressionResolver サービスを新設し、SnapshotId → BlendShape / Bone 値の preallocated 解決を提供する
  - `ExpressionResolver` 構築時に `IReadOnlyDictionary<string, ExpressionSnapshot>` を受け取り内部に preallocate
  - `TryResolve(snapshotId, Span<float> blendShapeOutput, Span<BoneSnapshot> boneOutput)` を提供
  - 0-alloc 維持（Req 11.1, 11.4）
  - **Red**: `ExpressionResolverTests`（EditMode）に追加
    - `TryResolve_KnownId_FillsOutputs`
    - `TryResolve_UnknownId_ReturnsFalse`
    - `TryResolve_OutputBufferTooSmall_ReturnsFalse`
  - **Green**: `Runtime/Domain/Services/ExpressionResolver.cs` を新規作成。Application 層 `ExpressionUseCase.Activate` から呼出される経路を追加
  - **Refactor**: 既存 `AnalogMappingEvaluator.cs` を物理削除（Decision 7 / OSC は別 spec で扱う）
  - 完了基準: 3 本のテストが緑、`AnalogMappingEvaluator` 参照ゼロ
  - _Requirements: 3.2, 9.3, 11.1, 11.4_
  - _Boundary: Domain.Services.ExpressionResolver_
  - _Depends: 3.1, 3.3_

- [x] 3.5 Domain AnalogBindingEntry から Mapping field を撤去し、AnalogMappingFunction を物理削除する
  - `Domain.Models.AnalogBindingEntry` の `Mapping` field 撤去（Req 6.3）
  - `Domain.Models.AnalogMappingFunction.cs` 物理削除
  - Domain 側は `SourceId / SourceAxis / TargetKind / TargetIdentifier / TargetAxis` のみ保持（Adapters 側で `InputActionReference` を保持: Req 13.3 / Decision 4）
  - **Red**: `AnalogBindingEntryTests` を Mapping field 不在で書換、`AnalogMappingFunctionTests.cs` を物理削除、`AnalogMappingEvaluatorTests.cs` を物理削除
  - **Green**: `Runtime/Domain/Models/AnalogBindingEntry.cs` から Mapping を撤去、`AnalogMappingFunction.cs` 削除、関連 Adapters Serializable も連動削除
  - **Refactor**: OSC 側の互換は別 spec とすることを Domain README コメントに残す
  - 完了基準: `AnalogMappingFunction` 参照ゼロ、Domain `AnalogBindingEntry` の field 数が 5
  - _Requirements: 6.2, 6.3, 13.1_
  - _Boundary: Domain.Models.AnalogBindingEntry, Domain.Models.AnalogMappingFunction, Domain.Services.AnalogMappingEvaluator_
  - _Depends: 3.4_

- [x] 3.6 SystemTextJsonParser と FacialCharacterProfileConverter を schema v2.0 専用に書換える
  - JSON parser: `schemaVersion: "2.0"` 以外を `Debug.LogError` + `InvalidOperationException` で拒否（Req 10.1）
  - Converter: SO Serializable → Domain `FacialProfile` 変換で snapshot 展開
  - 旧 `BonePoseDto` / `BonePoseEntryDto` / `LayerSlotDto` を物理削除
  - **Red**: `SystemTextJsonParserV2Tests` に以下を追加
    - `Parse_SchemaV2_ReturnsExpectedProfile`
    - `Parse_SchemaV1_ThrowsAndLogsError`
    - `Parse_MissingSchemaVersion_ThrowsAndLogsError`
  - 既存 `SystemTextJsonParserBonePoseTests`, `SystemTextJsonParserBonePoseBackwardCompatTests`, `BonePoseDtoRoundTripTests` を物理削除。`SystemTextJsonParserRoundTripTests` を v2.0 で書き換え
  - **Green**: `Adapters/Json/SystemTextJsonParser.cs` を schema v2.0 専用化、`FacialCharacterProfileConverter.cs` で snapshot 展開ロジックを実装
  - **Refactor**: `Adapters/ScriptableObject/Serializable/AnalogMappingFunctionSerializable.cs` を物理削除
  - 完了基準: schema v1.0 拒否確認、v2.0 round-trip 緑
  - _Requirements: 9.1, 9.2, 9.7, 10.1, 13.1_
  - _Boundary: Adapters.Json, Adapters.ScriptableObject.Serializable_
  - _Depends: 3.4, 3.5, 2.3_

- [x] 3.7 Phase 3 完了レビュー — Domain 全層の破壊書換が CI 緑であることを確認
  - `grep` で `LayerSlot` / `BonePose` / `BonePoseEntry` / `AnalogMappingFunction` / `AnalogMappingEvaluator` 参照ゼロ
  - Application 層の `ExpressionUseCase.Activate(Expression)` API 維持を Application Tests で確認
  - 完了基準: Unity Test Runner EditMode 全緑
  - _Requirements: 1.5, 3.1, 5.1, 6.3, 13.1_
  - _Boundary: Domain, Adapters_
  - _Depends: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6_

---

## Phase 4: InputProcessor 6 種 + ExpressionInputSourceAdapter + Category 自動推定

- [x] 4. Phase 4 — InputSystem 連携の Processor 化と Adapter 統合

- [x] 4.1 (P) Analog deadzone / scale / offset / clamp の 4 種 InputProcessor を stateless で実装する
  - `AnalogDeadZoneProcessor`: `min / max` の `float` field、`Process(value, control)` で abs ≤ min は 0、ノーマライズして `Sign(value) * Clamp01(normalized)`
  - `AnalogScaleProcessor`: `factor: float`、`value * factor`
  - `AnalogOffsetProcessor`: `offset: float`、`value + offset`
  - `AnalogClampProcessor`: `min / max: float`、`Mathf.Clamp(value, min, max)`
  - 全 processor は `public` な `float` field のみ serialize、AnimationCurve / string 不可（Topic 3）
  - **Red**: `AnalogProcessorTests`（EditMode）に各 processor の `Process_KnownInput_ReturnsExpected` テストを追加（4 processor × 3 ケース = 12 本）
  - **Green**: `Runtime/Adapters/Processors/AnalogDeadZoneProcessor.cs` 他 3 ファイルを新規作成
  - **Refactor**: 共通の `InputProcessor<float>` 派生定型コードをコメントで明示
  - 完了基準: 12 本のテストが緑
  - _Requirements: 6.1, 6.4_
  - _Boundary: Adapters.Processors.AnalogDeadZone, Adapters.Processors.AnalogScale, Adapters.Processors.AnalogOffset, Adapters.Processors.AnalogClamp_
  - _Depends: 3.7_

- [x] 4.2 (P) Analog curve / invert の 2 種 InputProcessor を stateless で実装する
  - `AnalogInvertProcessor`: `-value`
  - `AnalogCurveProcessor`: `preset: int`（0=Linear, 1=EaseIn `v*v`, 2=EaseOut `1-(1-v)*(1-v)`, 3=EaseInOut）。Custom Hermite は v2.1 以降（Decision 3）
  - **Red**: `AnalogProcessorTests` に追加（invert 3 ケース + curve 4 preset × 3 ケース = 15 本）
  - **Green**: `Runtime/Adapters/Processors/AnalogInvertProcessor.cs` / `AnalogCurveProcessor.cs` を新規作成
  - **Refactor**: preset enum 定数を private const で定義
  - 完了基準: 15 本のテストが緑
  - _Requirements: 6.1, 6.4_
  - _Boundary: Adapters.Processors.AnalogInvert, Adapters.Processors.AnalogCurve_
  - _Depends: 3.7_

- [x] 4.3 InputProcessor 6 種を Editor / Runtime 両方で一括登録する初期化フックを設置する
  - `AnalogProcessorRegistration` 静的クラス: `[InitializeOnLoad]` + `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]` で 6 processor を `InputSystem.RegisterProcessor` 一括登録
  - **Red**: `AnalogProcessorRegistrationTests`（PlayMode）に「`InputSystem.TryGetProcessor("deadZone")` 等で全 6 種が解決できる」テストを追加
  - **Green**: `Runtime/Adapters/Processors/AnalogProcessorRegistration.cs` を新規作成
  - **Refactor**: 登録対象の processor 名定数を一覧化し Migration Guide からも参照可能にする
  - 完了基準: PlayMode テストで 6 processor 全て解決
  - _Requirements: 6.1_
  - _Boundary: Adapters.Processors.AnalogProcessorRegistration_
  - _Depends: 4.1, 4.2_

- [x] 4.4 InputDeviceCategorizer を新設し、InputBinding.path から DeviceCategory を 0-alloc で推定する
  - `enum DeviceCategory { Keyboard, Controller }`
  - `static Categorize(string bindingPath, out bool wasFallback)`: prefix 判別（`<Keyboard>` → Keyboard、`<Gamepad>` / `<Joystick>` / `<XRController>` / `<Pen>` / `<Touchscreen>` → Controller、未認識は Controller fallback + `wasFallback = true`）
  - 0-alloc: `string.StartsWith(prefix, StringComparison.Ordinal)`
  - **Red**: `InputDeviceCategorizerTests`（EditMode）に
    - `Categorize_Keyboard_ReturnsKeyboard`
    - `Categorize_Gamepad_ReturnsController`
    - `Categorize_XRController_ReturnsController`
    - `Categorize_UnknownPrefix_ReturnsControllerWithFallbackFlag`
    - `Categorize_NullOrEmpty_ReturnsControllerWithFallbackFlag`
  - **Green**: `Runtime/Adapters/Input/InputDeviceCategorizer.cs` を新規作成
  - **Refactor**: 認識 prefix リストを `static readonly string[]` で定数化
  - 完了基準: 5 本のテストが緑
  - _Requirements: 7.2, 7.3, 7.4, 7.5_
  - _Boundary: Adapters.Input.InputDeviceCategorizer_
  - _Depends: 4.3_

- [x] 4.5 ExpressionInputSourceAdapter を新設し、Keyboard/Controller を統合した MonoBehaviour として動作させる
  - `[DisallowMultipleComponent]` の MonoBehaviour 1 個
  - 内部に keyboard 用 / controller 用の 2 つの `ExpressionTriggerInputSourceBase` インスタンスを composition で保持（D-12 既存挙動）
  - InputAction の `bindings[0].path` を `InputDeviceCategorizer` で分類して dispatch
  - 未認識 device は Controller 側 + `Debug.LogWarning` 1 回（Req 7.5）
  - unsupported processor 検知時は raw value で続行 + `Debug.LogWarning`（Req 6.6）
  - per-frame 0-alloc 維持（Req 11.3）
  - **Red**: `ExpressionInputSourceAdapterTests`（PlayMode）に以下を追加
    - `OnKeyboardAction_ActivatesExpression_ViaKeyboardSink`
    - `OnGamepadAction_ActivatesExpression_ViaControllerSink`
    - `OnUnknownDevice_LogsWarning_AndUsesControllerSink`
    - `MultipleEnableDisable_DoesNotLeakSubscriptions`
  - **Green**: `Runtime/Adapters/InputSources/ExpressionInputSourceAdapter.cs` を新規作成
  - **Refactor**: `OnEnable` / `OnDisable` の Subscribe / Unsubscribe を symmetric に整理
  - 完了基準: PlayMode テストで InputTestUtils 経由の keyboard/gamepad device simulation が緑
  - _Requirements: 7.2, 7.3, 7.4, 7.5, 8.1, 8.3, 8.4, 6.6, 11.3_
  - _Boundary: Adapters.InputSources.ExpressionInputSourceAdapter_
  - _Depends: 4.4_

- [x] 4.6 Keyboard/Controller 個別 InputSource を物理削除し、ExpressionBindingEntry.Category を撤去する
  - `KeyboardExpressionInputSource.cs` / `ControllerExpressionInputSource.cs` 物理削除（Req 8.2）
  - `KeyboardExpressionInputSourceTests.cs` / `ControllerExpressionInputSourceTests.cs` 物理削除
  - `ExpressionBindingEntry.Category` field 撤去（Req 7.1）
  - `InputSourceCategory.cs`: OQ1 で grep 確認後、参照ゼロなら物理削除（OSC 別 spec で再導入の余地）
  - `FacialCharacterSO.GetExpressionBindings(InputSourceCategory category)` のシグネチャを引数なし版に縮退（Req 7.1, 8.1）
  - `FacialCharacterInputExtension.BindCategory` の 二重ループを 1 ループに統合（adapter が吸収）
  - **Red**: `FacialCharacterSOTests` を category 引数なしで書換、`FacialCharacterSOInspectorTests` を category UI 不在で書換
  - **Green**: 上記ファイル削除 + シグネチャ縮退
  - **Refactor**: 削除 file の `.meta` も同時削除
  - 完了基準: `Keyboard/Controller InputSource` 参照ゼロ、`InputSourceCategory` 参照ゼロ（OSC 別 spec の場合は Obsolete 1 か所のみ残し OQ1 を closed にする）
  - _Requirements: 7.1, 8.1, 8.2_
  - _Boundary: Adapters.InputSources, Adapters.ScriptableObject.ExpressionBindingEntry, Adapters.ScriptableObject.FacialCharacterSO, Adapters.Input.FacialCharacterInputExtension_
  - _Depends: 4.5_

- [x] 4.7 InputSystemAdapter に device 推定経路を追加し、AnalogBindingEntrySerializable を簡素化する
  - `InputSystemAdapter.BindExpression(action, expression)` の入口で `InputDeviceCategorizer.Categorize(action.bindings[0].path)` を呼んで分岐
  - `AnalogBindingEntrySerializable`: `inputActionRef + targetIdentifier + targetAxis` のみに縮退（Req 6.2）
  - 旧 `mapping` field 撤去
  - **Red**: `InputSystemAdapterTests`（PlayMode）に「Bind 後の dispatch 先が categorizer の判定通り」テストを追加。`AnalogInputBindingDtoRoundTripTests` を mapping field 不在で書換
  - **Green**: 既存 `InputSystemAdapter.cs` を改修。`AnalogBindingEntrySerializable.cs` を改修
  - **Refactor**: 旧 `AnalogMappingFunctionSerializable.cs` の物理削除を確認（Phase 3.6 で削除済）
  - 完了基準: PlayMode テスト緑、Serializable の field 数が 3
  - _Requirements: 6.2, 6.4, 7.2_
  - _Boundary: Adapters.Input.InputSystemAdapter, Adapters.ScriptableObject.Serializable.AnalogBindingEntrySerializable_
  - _Depends: 4.5, 4.6_

- [x] 4.8 0-alloc 検証 PlayMode/Performance テストを整備する
  - `ExpressionResolverAllocationTests`: 100 frames で `GC.GetTotalMemory(false)` delta = 0
  - `ExpressionInputSourceAdapterAllocationTests`: InputAction.performed を 100 回 dispatch して 0-alloc
  - `AnalogProcessorAllocationTests`: 6 種 processor 連結で per-Process 0-alloc
  - **Red**: 上記 3 テストクラスを `Tests/PlayMode/Performance/` 配下に新規追加
  - **Green**: 既存実装で 0-alloc を満たしていることを確認。満たさない場合は preallocated cache を追加
  - **Refactor**: テストを Unity Performance Testing API に統一
  - 完了基準: 3 テスト全て GC delta 0 で緑
  - _Requirements: 11.1, 11.3, 11.4, 11.5, 12.7_
  - _Boundary: Performance Tests_
  - _Depends: 4.5, 4.7, 3.4_

---

## Phase 5: Editor Inspector / ExpressionCreatorWindow / AutoExporter 全面改修

- [x] 5. Phase 5 — Editor UX 全面改修

- [x] 5.1 FacialCharacterSOInspector を UI Toolkit で全面改修し、新 UX を提供する
  - 旧 BonePose / RendererPath 手入力 UI を物理削除（Req 4.1, 5.1）
  - 旧 ExpressionBindingEntry.Category UI を物理削除（Req 7.1）
  - 新 UI 要素:
    - Expression リストの各行に AnimationClip ObjectField（Req 1.1）
    - Layer DropdownField（ソース: `FacialCharacterProfileSO.Layers`）（Req 1.4）
    - LayerOverrideMask MaskField（multi-select、ラベルは Layers 名）（Req 3.3）
    - read-only RendererPath summary view（UI Toolkit ListView readonly）（Req 4.3）
  - AnimationClip 変更時に `IExpressionAnimationClipSampler.SampleSummary` を呼び read-only summary を refresh（Req 4.5）
  - validation エラー時の HelpBox + Save ボタン disabled:
    - AnimationClip null（Req 1.6）
    - 重複 Id（Req 1.7）
    - zero LayerOverrideMask（Req 3.5）
  - 新規 Expression 作成時に `System.Guid.NewGuid().ToString("N")` で Id 自動採番（Req 1.3）
  - AnimationClip 名から Name を派生（拡張子なしファイル名）（Req 1.2）
  - AnimationClip 不在時の informational hint（Req 4.4）
  - **Red**: `FacialCharacterSOInspectorTests`（EditMode、UI Toolkit element 探索）に以下を追加
    - `AnimationClipNull_DisplaysValidationError_AndDisablesSave`
    - `DuplicateId_DisplaysValidationError_AndDisablesSave`
    - `ZeroLayerOverrideMask_DisplaysValidationError`
    - `NewExpression_GeneratesGuidId`
    - `AnimationClipChanged_RefreshesRendererPathSummary`
    - `AnimationClipAssigned_PopulatesNameFromFileName`
  - **Green**: `Editor/Inspector/FacialCharacterSOInspector.cs` を全面改修。UXML/USS は `Editor/Inspector/FacialCharacterSOInspector.uxml` に分離（既存パターン踏襲）
  - **Refactor**: 重複 Id 検出は List 全走査（O(N²) だが N ≤ 512 で許容）
  - 完了基準: 6 本の Inspector テストが緑、IMGUI 新規追加なし
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.6, 1.7, 3.3, 3.5, 4.1, 4.3, 4.4, 4.5, 7.1, 13.5_
  - _Boundary: Editor.Inspector.FacialCharacterSOInspector_
  - _Depends: 4.6, 4.7, 2.4_

- [x] 5.2 (P) ExpressionCreatorWindow を AnimationClip ベイク経路に改修する
  - 既存の PreviewRenderWrapper / BlendShapeNameProvider / BoneNameProvider は再利用
  - 出力先を「Domain Expression として保存」から「AnimationClip にベイク」に変更:
    - `AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 0f, value))`
    - `AnimationUtility.SetAnimationEvents(clip, events)` で TransitionDuration / TransitionCurvePreset を埋める
  - 逆ロード時は `IExpressionAnimationClipSampler.SampleSummary` で初期スライダー値を復元
  - TransitionDuration / Curve preset 編集 UI は既存スライダーペイン下部に foldout 配置（OQ4 で Phase 5 中に確定）
  - **Red**: `ExpressionCreatorWindowTests`（EditMode）に
    - `Bake_BlendShapeSliders_WritesEditorCurves`
    - `Bake_TransitionMetadata_WritesAnimationEvents`
    - `LoadExistingClip_RestoresSliderValues`
  - **Green**: `Editor/Tools/ExpressionCreatorWindow.cs` を改修
  - **Refactor**: ベイクロジックを `ExpressionClipBakery` static helper に抽出
  - 完了基準: 3 本のテストが緑、既存 Preview 機能は維持
  - _Requirements: 2.1, 2.2, 13.5_
  - _Boundary: Editor.Tools.ExpressionCreatorWindow_
  - _Depends: 2.2_

- [x] 5.3 FacialCharacterSOAutoExporter を AnimationClip サンプラ経路に改修し、進捗 + abort を提供する
  - `OnWillSaveAssets(string[] paths)` で FacialCharacterSO 由来 .asset を対象に各 Expression をサンプリング（Req 9.1）
  - 出力先: `Application.streamingAssetsPath/FacialControl/{soName}/profile.json`（schema v2.0）
  - 200ms 超で `EditorUtility.DisplayProgressBar` 発火、try/finally で `ClearProgressBar` 保証（Req 9.5）
  - 1 Expression のサンプリング失敗で当該 SO の save を abort（paths から除外）+ `Debug.LogError`（Req 9.6）
  - cachedSnapshot を `ExpressionSerializable` に保持して Runtime fallback 経路で参照可能に
  - **Red**: `FacialCharacterSOAutoExporterTests`（EditMode）に
    - `OnWillSaveAssets_ValidSO_WritesV2JsonToStreamingAssets`
    - `OnWillSaveAssets_SamplerThrows_AbortsSaveAndLogsError`
    - `OnWillSaveAssets_ProgressBarShown_When_LongerThan200ms`（fake stopwatch）
    - `OnWillSaveAssets_NonFacialAsset_PassesThrough`
  - **Green**: `Editor/AutoExport/FacialCharacterSOAutoExporter.cs` を改修
  - **Refactor**: progress bar 表示判定 helper を抽出
  - 完了基準: 4 本のテストが緑、StreamingAssets に schema v2.0 JSON が書き出される
  - _Requirements: 9.1, 9.2, 9.3, 9.5, 9.6_
  - _Boundary: Editor.AutoExport.FacialCharacterSOAutoExporter_
  - _Depends: 5.1, 2.4_

- [x] 5.4 (P) FacialControllerEditor の概要表示を新モデルに合わせて小修正する
  - BonePose カウント表示等の文字列を撤去（BonePose 概念が消えるため）
  - Expression 数 / Layer 数 / Snapshot 数の概要表示に置換
  - **Red**: `FacialControllerEditorTests`（EditMode）に「概要表示に BonePose 文字列が含まれない」テストを追加
  - **Green**: `Editor/Inspector/FacialControllerEditor.cs` を小修正
  - **Refactor**: 表示文字列を const 化
  - 完了基準: 1 本のテストが緑
  - _Requirements: 5.1_
  - _Boundary: Editor.Inspector.FacialControllerEditor_
  - _Depends: 3.3_

---

## Phase 6: Sample 再生成 + Migration Guide + CHANGELOG

- [x] 6. Phase 6 — preview ユーザー向け配布物の整備（Documentation 系を除く実装作業のみ含める）

- [x] 6.1 MultiSourceBlendDemo サンプルを schema v2.0 で再生成する
  - `Samples~/MultiSourceBlendDemo/` 配下に AnimationClip 4 種（Smile / Anger / Surprise / Lipsync_A）を新規生成
  - 各 AnimationClip に `FacialControlMeta_Set` AnimationEvent でメタデータを埋め込む（transitionDuration / transitionCurvePreset）
  - `MultiSourceBlendDemoCharacter.asset` を新 schema（schemaVersion=2.0、AnimationClip 参照ベース、LayerOverrideMask flags）で再構成
  - `profile.json` を v2.0 で書き出し
  - `Assets/Samples/FacialControl InputSystem/0.1.0-preview.X/Multi Source Blend Demo/` 側にも対応する asset を **手動 copy で同期**（CLAUDE.md 二重管理ルール）
  - 完了基準: Unity Editor で Sample Scene を開き、Inspector に validation error が出ず、Play 時に 4 表情が切り替わる
  - _Requirements: 1.1, 3.1, 9.1, 10.1_
  - _Boundary: Samples~/MultiSourceBlendDemo, Assets/Samples_
  - _Depends: 5.1, 5.2, 5.3_

- [x] 6.2 (P) AnalogBindingDemo サンプルを mapping field 不在 + processors 埋込 で再生成する
  - `Samples~/AnalogBindingDemo/*.inputactions` に deadzone / scale / clamp processor を埋め込む（processors 文字列例: `"deadZone(min=0.1),scale(factor=2),clamp(min=0,max=1)"`）
  - `AnalogBindingDemoCharacter.asset` を mapping field 不在で再構成
  - `analog_bindings.json` を `inputActionRef + targetIdentifier + targetAxis` のみの schema で再生成
  - `Assets/Samples/.../Analog Binding Demo/` 側にも手動 copy 同期
  - 完了基準: Unity Editor で Sample Scene を開いて Play、Gamepad の左スティック入力に対し processors 経由の値が BlendShape に反映される
  - _Requirements: 6.1, 6.2, 6.4, 10.1_
  - _Boundary: Samples~/AnalogBindingDemo, Assets/Samples_
  - _Depends: 4.7, 5.1_

- [x] 6.3 Migration Guide ドキュメントを Documentation~ に新規作成する
  - 配置: `Packages/com.hidano.facialcontrol.inputsystem/Documentation~/MIGRATION-v0.x-to-v1.0.md`
  - 構成（design.md "Migration Guide 構造" セクション準拠）:
    1. Schema 差分一覧表
    2. Step-by-Step 手順（Backup / LayerSlot.blendShapeValues → AnimationClip / FacialProfile.BonePoses → AnimationClip Transform curves / AnalogMappingFunction → InputAction processors 文字列対応表 / ExpressionBindingEntry.category 削除 / Keyboard/Controller InputSource → ExpressionInputSourceAdapter 差し替え）
    3. AnimationClip メタデータ運搬規約（`FacialControlMeta_Set` 予約名）
    4. 既知の制限
    5. 自動移行 menu の不在に関する説明（Req 10.5）
  - AnalogMappingFunction → processors 文字列の対応表は具体的な変換例を 4 ケース以上（deadZone / scale / offset / curve preset）含める
  - 完了基準: ファイルが Documentation~ 配下に存在し、UPM Package Manager の Documentation リンクから到達可能
  - _Requirements: 5.4, 6.5, 8.5, 10.2, 10.3, 10.4, 10.5_
  - _Boundary: Documentation~_
  - _Depends: 6.1, 6.2_

- [x] 6.4 CHANGELOG.md と package.json バージョンを Breaking Change 表記で更新する
  - `Packages/com.hidano.facialcontrol/CHANGELOG.md` と `Packages/com.hidano.facialcontrol.inputsystem/CHANGELOG.md` の両方を更新
  - 新 entry: `## [1.0.0-preview.X] - YYYY-MM-DD` の冒頭に **BREAKING CHANGES** ラベル + Migration Guide へのリンク（Req 10.6）
  - 撤去 API 一覧: `LayerSlot`, `BonePose`, `BonePoseEntry`, `AnalogMappingFunction`, `AnalogMappingEvaluator`, `KeyboardExpressionInputSource`, `ControllerExpressionInputSource`, `ExpressionBindingEntry.Category`
  - 新 API 一覧: `ExpressionSnapshot`, `LayerOverrideMask`, `BlendShapeSnapshot`, `BoneSnapshot`, `ExpressionResolver`, `ExpressionInputSourceAdapter`, `InputDeviceCategorizer`, 6 種 InputProcessor
  - schema 破壊: `schemaVersion: "2.0"`、v1.0 ロード不可
  - `package.json` の `version` を `0.1.0-preview.2` 等に bump（preview ファミリー継続を維持）
  - 完了基準: 両 CHANGELOG に Breaking Change marker と Migration Guide リンク、`package.json` の version 文字列が更新済
  - _Requirements: 10.1, 10.6_
  - _Boundary: Packages/{core, inputsystem}/CHANGELOG.md, package.json_
  - _Depends: 6.3_

- [x] 6.5 Phase 6 完了レビュー — Sample 結線確認と CI 全 Phase 緑
  - Unity Editor で `Multi Source Blend Demo` / `Analog Binding Demo` の両 Scene を開いて Play、表情遷移と analog 入力が正常動作することを手動確認
  - UPM Package Manager の `Import Sample` 経由で空プロジェクトに Sample を import し、同等動作することを手動確認
  - Unity Test Runner で EditMode + PlayMode + Performance の全 categories が緑
  - `grep` で旧 API 参照がコードベース全体（Sample 含む）で 0 件
  - 完了基準: 上記 4 点を全て確認済、Hidano + Junki Hiroi の 2 名でレビュー完了
  - _Requirements: 10.1, 11.5, 12.1, 12.2, 12.3_
  - _Boundary: Samples~, Assets/Samples, Tests/EditMode, Tests/PlayMode, Tests/PlayMode/Performance_
  - _Depends: 6.1, 6.2, 6.3, 6.4, 4.8_

---

## Requirements Coverage Matrix

| Requirement | Tasks |
|-------------|-------|
| 1.1 | 3.1, 5.1, 6.1 |
| 1.2 | 5.1 |
| 1.3 | 5.1 |
| 1.4 | 5.1 |
| 1.5 | 1.3, 3.1, 3.7 |
| 1.6 | 5.1 |
| 1.7 | 5.1 |
| 2.1 | 1.2, 2.1, 5.2, 6.1 |
| 2.2 | 1.2, 2.1, 5.2 |
| 2.3 | 2.1 |
| 2.4 | 2.2 |
| 2.5 | 1.3, 2.2 |
| 2.6 | 1.3, 2.2 |
| 2.7 | 2.1 |
| 3.1 | 1.1, 3.2, 6.1 |
| 3.2 | 3.4 |
| 3.3 | 5.1 |
| 3.4 | 1.1, 3.2 |
| 3.5 | 5.1 |
| 4.1 | 5.1 |
| 4.3 | 5.1 |
| 4.4 | 5.1 |
| 4.5 | 5.1 |
| 5.1 | 3.1, 3.3, 3.7, 5.4 |
| 5.2 | 3.3 |
| 5.3 | 3.3 |
| 5.4 | 6.3 |
| 6.1 | 4.1, 4.2, 4.3, 6.2 |
| 6.2 | 3.5, 4.7, 6.2 |
| 6.3 | 3.5, 3.7 |
| 6.4 | 4.1, 4.2, 4.7, 6.2 |
| 6.5 | 6.3 |
| 6.6 | 4.5 |
| 7.1 | 4.6, 5.1 |
| 7.2 | 4.4, 4.5, 4.7 |
| 7.3 | 4.4, 4.5 |
| 7.4 | 4.4, 4.5 |
| 7.5 | 4.4, 4.5 |
| 8.1 | 4.5, 4.6 |
| 8.2 | 4.6 |
| 8.3 | 4.5 |
| 8.4 | 4.5 |
| 8.5 | 6.3 |
| 9.1 | 2.3, 5.3, 6.1 |
| 9.2 | 1.2, 1.3, 2.3, 5.3 |
| 9.3 | 3.4, 5.3 |
| 9.4 | 2.1, 2.4 |
| 9.5 | 5.3 |
| 9.6 | 5.3 |
| 9.7 | 1.3, 2.3, 3.6 |
| 10.1 | 2.3, 3.6, 6.1, 6.2, 6.4, 6.5 |
| 10.2 | 6.3 |
| 10.3 | 6.3 |
| 10.4 | 6.3 |
| 10.5 | 6.3 |
| 10.6 | 6.4 |
| 11.1 | 3.4, 4.8 |
| 11.2 | 2.4 |
| 11.3 | 4.5, 4.8 |
| 11.4 | 3.4, 4.8 |
| 11.5 | 4.8, 6.5 |
| 12.1 | 6.5 |
| 12.2 | 6.5 |
| 12.3 | 6.5 |
| 12.4 | 2.1, 2.2, 2.4 |
| 12.5 | 4.4, 4.5 |
| 12.6 | 全タスク（テスト命名規則は steering 既定） |
| 12.7 | 4.8 |
| 13.1 | 1.1, 1.2, 1.3, 3.1, 3.2, 3.3, 3.5, 3.6 |
| 13.2 | 2.1, 2.4 |
| 13.3 | 3.5, 4.7 |
| 13.4 | 1.1, 1.4 |
| 13.5 | 5.1, 5.2 |

---

## Phase 並列性メモ

- **Phase 1**: 1.1 / 1.2 は並列可（`(P)`）。1.3 は 1.2 に依存。1.4 は Phase 1 全完了後の review。
- **Phase 2**: 2.1 → 2.2（同じサンプラを拡張）。2.3 は DTO で独立、Phase 1.3 完了後に並列着手可。2.4 は Phase 2 全完了後の保証。
- **Phase 3**: 3.1 → 3.2 → 3.3 → 3.4 → 3.5 → 3.6 を逐次（中核 Domain の連鎖破壊変更）。3.7 は最終レビュー。
- **Phase 4**: 4.1 / 4.2 は別 processor のため並列可（`(P)`）。4.3 はそれらに依存。4.4 / 4.5 / 4.6 / 4.7 は逐次（adapter 統合の連鎖）。4.8 は Phase 4 性能保証。
- **Phase 5**: 5.1 / 5.4 は別 Editor 担当範囲のため並列可（`(P)`）。5.2 は 2.2 の AnimationEvent 経路に依存して並列可（`(P)`）。5.3 は 5.1 の Inspector cachedSnapshot 経路に依存。
- **Phase 6**: 6.1 / 6.2 は別 sample のため並列可（`(P)`）。6.3 は両 sample 完了後。6.4 は 6.3 後。6.5 は最終確認。

---

## Open Questions Tracking（design.md より引継ぎ）

| OQ | 解決時期 | 関連タスク |
|----|---------|-----------|
| OQ1 (`InputSourceCategory` 撤去判断) | Phase 4 着手前 grep | 4.6 |
| OQ2 (LayerOverrideMask bit-name 対応表責務) | Phase 1 実装中 | 1.1 |
| OQ3 (AnalogCurveProcessor preset 不足時 path) | preview feedback 待ち | 4.2（v2.1 で再評価） |
| OQ4 (ExpressionCreatorWindow TransitionMeta UI 配置) | Phase 5 実装中 | 5.2 |
| OQ5 (AnimationClip サンプリング 0-alloc 制約緩和度合い) | Phase 2 benchmark | 2.4 |
| OQ6 (`LayerInputSources` の v2.0 round-trip 互換) | Phase 2 着手前 | 2.3 |
