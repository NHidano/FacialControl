# Gap Analysis: inspector-and-data-model-redesign

## 分析サマリー

- **改修範囲**: Domain モデル 3 種 (`Expression` / `LayerSlot` / `BonePose`) を破壊的に再設計し、AnimationClip を Source of Truth とする統一モデルへ移行する。同時に InputSystem パッケージ側で `AnalogMappingFunction`・`Category`・Keyboard/Controller 二系統を InputProcessor + デバイスクラス自動推定 + 統合アダプタで simplification する。
- **影響規模**: 13 Requirements 全てに対応するコードがすでに存在し、実装は **XL** 相当。Domain 5 ファイル + Adapters の Json/Serializable/InputSources 計 20+ ファイル + Editor の Inspector/Window が直接書き換え対象。Tests も Domain (Expression/BonePose 系) と Adapters (Json round-trip) 中心に **30+ ファイル** 以上の刷新が必要。
- **核心的アーキテクチャ判断**: AnimationClip 参照は Domain に持ち込めない (Req 13.1 / 13.4)。**Editor 専用サンプラ** が AnimationClip → 中間 JSON に焼き、Domain は中間 JSON のスナップショットだけを見る ── という Editor / Runtime 境界が成立する。BonePose は Expression から削除されるが、Domain には Expression から派生した「BlendShape スナップショット + Bone 姿勢スナップショット」を保持する新たな Snapshot 型が必要となる。
- **大きな未確定事項**: (A) AnimationClip メタデータ (TransitionDuration/Curve) を AnimationClip に埋める方法 (clip events vs naming convention), (B) 中間 JSON の schema 設計 (versioning + snapshot vs keyframe table), (C) Inspector の AnimationClip ベース Expression 編集 UI を ExpressionCreatorWindow に統合するか別画面とするか。これらは design フェーズで Research Needed として深掘る必要がある。
- **推奨戦略**: **Hybrid (C)** を強く推奨。Domain は新型に置換 (Option B 寄り)、Adapters / Editor の周辺 (Json DTO・Inspector) は既存 file を再構成 (Option A 寄り)。preview 段階のため schema 互換は明示的に破壊し、移行ガイドで吸収する。

---

## 1. Current State Investigation

### 1.1 既存 Domain (再設計の中心)

| 既存ファイル | 状態 | 役割 |
|---|---|---|
| `Runtime/Domain/Models/Expression.cs` | **置換** | `readonly struct`。`Id/Name/Layer/TransitionDuration/TransitionCurve/BlendShapeValues/LayerSlots` をフィールドで保持 (Req 1.5 で全廃)。 |
| `Runtime/Domain/Models/LayerSlot.cs` | **置換** | `readonly struct`。`Layer` 名 + `BlendShapeValues[]` の override データを内包 (Req 3.1/3.4 でビットフラグへ縮退)。 |
| `Runtime/Domain/Models/BonePose.cs` + `BonePoseEntry.cs` | **保持しつつ役割変更** | Domain 値型として残してよいが、`FacialProfile.BonePoses` 配列に直接ぶら下がる構造は廃止 (Req 5.1)。BonePoseEntry はサンプリング結果を保持する snapshot 型として再利用可能。 |
| `Runtime/Domain/Models/FacialProfile.cs` | **改修** | `BonePoses` フィールドを削除し、Expression 内部の派生スナップショット (BlendShape + Bone) を持つ形に再設計。 |
| `Runtime/Domain/Services/BonePoseComposer.cs` | **再利用** | Euler→Quaternion + basis 合成。サンプラでも Adapters Bone 層でも引き続き使える。 |
| `Runtime/Domain/Services/TransitionCalculator.cs` | **再利用** | `TransitionCurve` に対する評価器。AnimationClip 由来の curve metadata に置換しても評価ロジックは流用可能。 |
| `Runtime/Application/UseCases/ExpressionUseCase.cs` | **API 維持・内部適応** | `Activate(Expression)` の API は変えず、Expression が中間 JSON 由来になっても呼び出し方は同じ。`profile.GetEffectiveLayer` の対象が「最初のフラグ位置」になる影響あり。 |

> 注: 仕様書冒頭のタスク説明に「ExpressionResolver / TransitionInterpolator」とあるが、現コードベースではそれぞれ `BonePoseComposer` / `TransitionCalculator` に相当する。`ExpressionResolver` 名のクラスは存在しない (`grep` で 0 件)。Requirements 内の「Expression Resolver」記述は新規導入の概念名と解釈するのが妥当。

### 1.2 既存 Adapters JSON

| ファイル | 状態 | 備考 |
|---|---|---|
| `Adapters/Json/SystemTextJsonParser.cs` | **大規模改修** | `ExpressionDto` から `transitionDuration/transitionCurve/blendShapeValues/layerSlots` を全て廃止し、`animationSnapshot`/`layerOverrideMask`/`renderers` 等の新フィールドへ置換。schema versioning (Req 9.7) を `schemaVersion: "2.0"` 等で破壊宣言。 |
| `Adapters/Json/Dto/BonePoseEntryDto.cs` + `BonePoseDto.cs` | **撤去 or 再目的化** | Expression に統合される (Req 5.1) ため、`bonePoses[]` トップレベル配列は廃止。代わりに Expression snapshot 内の bone curve 集合になる。 |
| `Adapters/Json/Dto/InputSourceDto.cs` 等 | **保持** | レイヤー入力源宣言は影響を受けない (LayerInputSources は本 spec の対象外)。 |

### 1.3 既存 Adapters ScriptableObject

| ファイル | 状態 |
|---|---|
| `Adapters/ScriptableObject/FacialCharacterProfileSO.cs` | **改修**: `_bonePoses` フィールド削除 (Req 5.1)、`_rendererPaths` の Inspector 入力を read-only サマリ化 (Req 4.1/4.3)、`_expressions` の中身を ExpressionSerializable 新型に差し替え (Req 1.5)、`BuildFallbackProfile` の経路で AnimationClip サンプリング不可なため、SO 編集時に Editor サンプラが書き戻した中間 JSON を保持する serializable cache を持つ必要あり。 |
| `Adapters/ScriptableObject/Serializable/ExpressionSerializable.cs` | **置換**: `id/name/layer/transitionDuration/transitionCurve/blendShapeValues/layerSlots` から `id/name/layer/animationClip(Editor) + cachedSnapshot(Runtime)` 形へ。 |
| `Adapters/ScriptableObject/Serializable/LayerSlotSerializable.cs` | **置換**: `layer + blendShapeValues[]` → `LayerOverrideMask flags` 1 個へ縮退。 |
| `Adapters/ScriptableObject/Serializable/BonePoseSerializable.cs` + `BonePoseEntrySerializable` | **撤去** (Req 5.1)。 |
| `Adapters/ScriptableObject/Serializable/AnalogMappingFunctionSerializable.cs` | **撤去** (Req 6.3)。 |
| `Adapters/ScriptableObject/Serializable/AnalogBindingEntrySerializable.cs` | **大幅縮退**: `sourceId/sourceAxis/targetKind/targetIdentifier/targetAxis/mapping` → `inputActionReference + targetIdentifier + targetAxis` (Req 6.2)。 |
| `Adapters/ScriptableObject/Serializable/FacialCharacterProfileConverter.cs` | **改修**: BonePoses 配列削除、AnalogMapping 変換の廃止、AnimationClip スナップショット展開ロジックを新設。 |
| `Adapters/Bone/AnalogBonePoseProvider.cs` / `BoneTransformResolver.cs` / `BoneWriter.cs` | **保持**: BonePose Domain モデルが残るため再利用可能。但し Expression の AnimationClip サンプリング由来 snapshot から bone curve を引っ張るパスを新設要。 |

### 1.4 既存 InputSystem 連携

| ファイル | 状態 |
|---|---|
| `Runtime/Adapters/InputSources/KeyboardExpressionInputSource.cs` | **撤去** (Req 8.2)。`ExpressionInputSourceAdapter` に統合 (Req 8.1)。 |
| `Runtime/Adapters/InputSources/ControllerExpressionInputSource.cs` | **撤去** (Req 8.2)。 |
| `Runtime/Adapters/InputSources/InputActionAnalogSource.cs` | **保持・拡張**: 既に `InputAction.controls[0]` を直叩きする 0-alloc 設計。Processor チェーン後の値をそのまま採用するため (Req 6.4)、変更は最小。但し `Tick` で読む際に「processor 通過済みの値」が来ることを前提として、Domain 側の `AnalogMappingEvaluator` を呼ばないルートを `ExpressionInputSourceAdapter` 経由で確立する。 |
| `Runtime/Adapters/InputSources/InputSourceFactory.cs` (コア) | **保持** |
| `Runtime/Domain/Services/AnalogMappingEvaluator.cs` (コア) | **撤去または non-default 化**: Req 6 で Processor 化されるため Domain 側の評価器は不要となる。但し OSC など InputSystem 経由しない経路の互換に注意。 |
| `Runtime/Adapters/Input/InputSystemAdapter.cs` | **改修**: `BindExpression(action, expression)` の入口で `action.bindings[0].path` の device class を見て `Keyboard` / `Gamepad` / `XRController` を分岐し、内部で振り分け (Req 7.2-7.5)。 |
| `Runtime/Adapters/ScriptableObject/ExpressionBindingEntry.cs` | **`category` フィールド削除** (Req 7.1)。 |
| `Runtime/Adapters/ScriptableObject/InputSourceCategory.cs` | **撤去候補** (Req 7 完了後、参照される箇所がなくなれば)。 |
| `Runtime/Adapters/ScriptableObject/FacialCharacterSO.cs` | **改修**: `GetExpressionBindings(InputSourceCategory category)` の category 引数を撤去または無視 (Req 7.1/7.2)。 |
| `Runtime/Adapters/Input/FacialCharacterInputExtension.cs` | **改修**: `BindCategory(so, InputSourceCategory.Controller); BindCategory(so, InputSourceCategory.Keyboard);` の二重分岐を 1 ループに統合し、内部でデバイス推定。 |

### 1.5 既存 Editor

| ファイル | 状態 |
|---|---|
| `Editor/Tools/ExpressionCreatorWindow.cs` | **再利用 + 改修**: BlendShape スライダープレビュー、PreviewRenderWrapper、レイヤードロップダウン、ARKit Detector との連携 (`BlendShapeNameProvider`) は **AnimationClip サンプラと UI の核として 100% 再利用可能**。但し「現在のスライダー状態を Domain Expression として保存する」現状の出力を「現在のスライダー状態を AnimationClip にベイクする / 既存 AnimationClip から逆ロードする」フローに置換。 |
| `Editor/Inspector/FacialControllerEditor.cs` | **小修正**: プロファイル概要セクションの BonePose カウント表示等の文字列を更新。 |
| `com.hidano.facialcontrol.inputsystem/Editor/Inspector/FacialCharacterSOInspector.cs` | **大改修**: BonePose セクション削除 (Req 5.1)、Layer/Expression セクションの再構築 (Req 1.4 ドロップダウン、Req 4.1 RendererPath 廃止、Req 4.3 read-only summary view、Req 1.6 validation、Req 1.7 重複 ID validation、Req 3.3 multi-select flags)。BonePose foldout / RendererPath foldout 周りの VisualElement と SerializedProperty 解決をまるごと差し替え。 |
| `Editor/AutoExport/FacialCharacterSOAutoExporter.cs` | **改修**: `OnWillSaveAssets` の経路で AnimationClip サンプラを呼び出して中間 JSON を生成 → StreamingAssets に書き出し (Req 9.1-9.2)。サンプリング失敗時は abort save (Req 9.6)。 |
| `Editor/Common/PreviewRenderWrapper.cs` / `BlendShapeNameProvider.cs` / `BoneNameProvider.cs` | **保持**: AnimationClip サンプラと共存可能。 |

### 1.6 既存テスト

直接書き換えが必要な test 群 (網羅ではないが代表):

- **Domain**: `ExpressionTests.cs`, `LayerSlotTests.cs`, `BonePoseTests.cs`, `BonePoseEntryTests.cs`, `BonePoseHotPathCtorTests.cs`, `FacialProfileBonePosesBackwardCompatTests.cs`, `FacialProfileTests.cs`, `Models/AnalogMappingFunctionTests.cs`, `Models/AnalogBindingEntryTests.cs`, `Services/AnalogMappingEvaluatorTests.cs`
- **Adapters Json**: `BonePoseDtoRoundTripTests.cs`, `SystemTextJsonParserBonePoseTests.cs`, `SystemTextJsonParserBonePoseBackwardCompatTests.cs`, `SystemTextJsonParserRoundTripTests.cs`
- **Adapters ScriptableObject**: `FacialCharacterProfileConverterTests.cs`, `FacialCharacterProfileSOTests.cs`
- **InputSystem (EditMode)**: `KeyboardExpressionInputSourceTests.cs`, `ControllerExpressionInputSourceTests.cs`, `FacialCharacterSOTests.cs`, `FacialCharacterSOInspectorTests.cs`, `AnalogInputBindingJsonLoaderTests.cs`, `AnalogInputBindingDtoRoundTripTests.cs`
- **InputSystem (PlayMode)**: `InputSystemAdapterTests.cs`, `FacialCharacterInputExtensionTests.cs`

加えて新規追加が必要 (Req 12):

- `Editor/AnimationClipExpressionSamplerTests.cs` (EditMode, Req 12.4)
- `Adapters/ScriptableObject/LayerOverrideMaskTests.cs` (EditMode)
- `Adapters/Json/IntermediateJsonSchemaV2Tests.cs` (EditMode)
- `PlayMode/Adapters/ExpressionInputSourceAdapterTests.cs` (Req 12.5)
- `PlayMode/Performance/ExpressionInputSourceAdapterAllocationTests.cs` (Req 11.5 / 12.7)

### 1.7 既存 Sample アセット

破壊的変更で再生成 / 移行が必要:

- `Samples~/MultiSourceBlendDemo/MultiSourceBlendDemoCharacter.asset` + `StreamingAssets/.../profile.json`
- `Samples~/AnalogBindingDemo/AnalogBindingDemoCharacter.asset` + `StreamingAssets/.../profile.json` + `analog_bindings.json`
- `Assets/Samples/FacialControl InputSystem/0.1.0-preview.1/Multi Source Blend Demo/MultiSourceBlendDemoCharacter.asset`
- `Assets/Samples/FacialControl InputSystem/0.1.0-preview.1/Analog Binding Demo/AnalogBindingDemoCharacter.asset`

`analog_bindings.json` の schema (`mapping.deadZone/scale/offset/...`) は Req 6.5 に従い `mapping` フィールド全廃 + InputAction Asset の processors への移行になる。これは UPM 配布される sample なのでユーザー目線の sample 体験も同時に変わる (`*.inputactions` ファイルに deadzone/scale/clamp processor を埋め込んだ form を新たに同梱する必要あり)。

---

## 2. Requirement-to-Asset Map (Gaps tagged)

| Req | 既存資産 | ギャップ種別 | 内容 |
|---|---|---|---|
| 1.1-1.7 | `Expression.cs`, `ExpressionSerializable.cs`, `FacialCharacterSOInspector.cs` | **Missing** | AnimationClip 参照を Adapters 層 (ExpressionSerializable) に持たせ、Domain Expression は中間 JSON 由来の snapshot のみ持つ新型に置換。Inspector の Layer ドロップダウン、GUID 自動採番、null clip / 重複 Id の validation エラー UI は新規実装。 |
| 2.1-2.7 | `BonePoseComposer.cs`, `TransitionCalculator.cs` | **Missing** | AnimationClip → snapshot 抽出器 (Editor 専用) は新規。`AnimationUtility.GetCurveBindings` + `AnimationCurve.Evaluate` 経由で BlendShape / Transform カーブを sample。TransitionDuration / Curve のメタデータ運搬手段 (clip events / naming convention) は **Research Needed**。Linear fallback (Req 2.5/2.6) は既存 `TransitionCurve.Linear` で対応。 |
| 3.1-3.5 | `LayerSlot.cs`, `LayerSlotSerializable.cs` | **Missing** | `[System.Flags] enum LayerOverrideMask` を Domain に新設。`Expression.LayerSlots` の配列構造を `LayerOverrideMask` 1 個に置換。Inspector の multi-select checkboxes UI (`MaskField` 系)、ゼロフラグ validation 新規。 |
| 4.1-4.5 | `_rendererPaths` (FacialCharacterProfileSO), `RendererPaths` (FacialProfile) | **Constraint** | Inspector からの手入力欄を撤去 (UI 削除のみ)。Read-only summary view の VisualElement 新規。AnimationClip 由来の path 計算は `EditorCurveBinding.path` から拾うため Editor 専用ロジックに集約。 |
| 5.1-5.4 | `BonePose.cs`, `BonePoseEntry.cs`, `FacialProfile.BonePoses` | **Missing** | FacialProfile からトップレベル BonePoses 配列を撤去。BonePose snapshot は Expression 内に統合。移行ガイドの BonePose → AnimationClip transform curves 変換手順は新規ドキュメント。 |
| 6.1-6.6 | `AnalogMappingFunction.cs`, `AnalogMappingEvaluator.cs`, `AnalogBindingEntry.cs`, `AnalogMappingFunctionSerializable.cs` | **Missing + Constraint** | InputProcessor 派生クラス 6 種 (deadzone/scale/offset/curve/invert/clamp) を `com.hidano.facialcontrol.inputsystem` に新設。`InputSystem.RegisterProcessor` を初期化フックで一括登録。`AnalogBindingEntry` は **Domain 値型のままにするか、Adapters 専用 Serializable のみに残すか** が判断点 — Req 6.2/6.3 は Adapters のみ言及するが、Domain `AnalogBindingEntry` も連動して縮退させるのが整合的 (Mapping フィールド削除 + InputActionReference を持てない Domain では適用不可なので、`InputActionReference` は Adapters serializable 側に置き、Domain は引き続き SourceId 文字列で受ける案)。**Research Needed**: 既存 `AnalogBindingTargetKind.BonePose` が Req 6.2 後も targetIdentifier だけで識別できるか。 |
| 7.1-7.5 | `ExpressionBindingEntry.cs`, `InputSourceCategory.cs`, `FacialCharacterSO.GetExpressionBindings(category)`, `FacialCharacterInputExtension.BindCategory` | **Missing** | デバイスクラス推定 helper 新規。Unity InputSystem の `InputBinding.path` (`<Keyboard>/...` / `<Gamepad>/...` / `<XRController>/...`) を解析するロジック。未認識デバイスは Controller fallback + warning (Req 7.5)。 |
| 8.1-8.5 | `KeyboardExpressionInputSource.cs`, `ControllerExpressionInputSource.cs`, `ExpressionTriggerInputSourceBase.cs` | **Missing** | `ExpressionInputSourceAdapter` MonoBehaviour 新規 (Adapters 層)。内部で 1 つの `InputSourceFactory.RegisterReserved(...)` で keyboard/controller 両方に応える。`ExpressionTriggerInputSourceBase` は基底として残し、派生 1 個 (`ExpressionInputSource`) に絞る。 |
| 9.1-9.7 | `FacialCharacterSOAutoExporter.cs` | **Missing** | AnimationClip サンプラ `IExpressionAnimationClipSampler` (Editor 専用 interface) 新規。`AnimationUtility.GetCurveBindings` + `clip.SampleAnimation()` を組み合わせて BlendShape + Bone snapshot を組み立てる。Progress bar (Req 9.5) は `EditorUtility.DisplayProgressBar`。サンプリング失敗時の abort save は AssetModificationProcessor で `paths` から該当 SO を除外。 |
| 10.1-10.6 | `Documentation~/`, `CHANGELOG.md` | **Missing** | 移行ガイド `MIGRATION-v0.x-to-v1.0.md` を `Documentation~` 配下に新規。CHANGELOG に Breaking Change marker。自動移行 menu command の実装可否は **Research Needed** (preview 段階の現実的判断としては「手動再構築を推奨し、自動 migration は提供しない」が妥当)。 |
| 11.1-11.5 | 既存 GC tests (`GCAllocationTests.cs`, `BoneWriterGCAllocationTests.cs`, etc.) | **Constraint** | Runtime 経路が中間 JSON snapshot 直読みになるため 0-alloc は維持しやすい。但し snapshot 配列の lookup を Dictionary でやる場合は preallocate。`ExpressionInputSourceAdapter` の event callback で boxing が起きないか PlayMode perf test で検証。 |
| 12.1-12.7 | 既存テスト群 | **Missing** | Red→Green→Refactor 厳守。AnimationClip サンプリング系は EditMode (Req 12.4)、InputSystem device categorization は PlayMode (Req 12.5)。 |
| 13.1-13.5 | asmdef 構成 | **Constraint** | `Hidano.FacialControl.Domain.asmdef` は `Unity.Collections` のみ参照。`UnityEngine.AnimationClip` 参照を Domain に絶対追加しないこと。サンプラは `Hidano.FacialControl.Editor` (`includePlatforms: ["Editor"]`) に置き、Runtime asmdef から不可視にする。Inspector は UI Toolkit のみ (IMGUI 新規禁止)。 |

---

## 3. Implementation Approach Options

### Option A: Extend Existing Components

**該当度低**。`Expression.cs` / `LayerSlot.cs` の field set が大幅に削減 (Req 1.5, 3.4) されるため、既存 struct を「拡張」する余地がなく事実上書き換えになる。`SystemTextJsonParser.cs` の DTO も `ExpressionDto.transitionDuration/transitionCurve/blendShapeValues/layerSlots` の 4 フィールド全廃が前提なので、既存 DTO のままフィールド追加で済まない。

**Trade-off**:
- 採用するメリット少
- ファイル名・class 名を維持できる程度のメリット

### Option B: Create New Components

**該当度中**。新型として:

- `Domain/Models/ExpressionSnapshot.cs` (新): AnimationClip 由来の (BlendShape values + bone snapshots + renderer paths + transition meta) を 1 つの readonly struct に集約。
- `Domain/Models/LayerOverrideMask.cs` (新): `[Flags]` enum。
- `Editor/Tools/AnimationClipExpressionSampler.cs` (新): AnimationClip → ExpressionSnapshot JSON。
- `InputSystem/Adapters/InputSources/ExpressionInputSourceAdapter.cs` (新): MonoBehaviour 統合版。
- `InputSystem/Adapters/Processors/AnalogDeadZoneProcessor.cs` ほか 5 種 (新): InputProcessor 派生。

旧 `Expression.cs` / `LayerSlot.cs` / `BonePose` 周りは spec の preview 破壊宣言にしたがって削除 or `[Obsolete]` で deprecate。

**Trade-off**:
- ✅ 新旧の責務が file レベルで明確
- ✅ 削除予定 file の影響範囲が grep で追跡しやすい
- ❌ Expression は既存名で残すと外部参照 (Tests / Adapters) と衝突するため、`Expression` の名前を維持しつつ実装を入れ替える必要があり、Pure な Option B にはなりきらない

### Option C: Hybrid Approach (推奨)

**戦略**:

| サブシステム | アプローチ |
|---|---|
| Domain `Expression` / `LayerSlot` / `FacialProfile` | **既存 struct の中身を入れ替え** (Option A の名前維持 + Option B の field set 全置換)。`Expression.cs` のファイル名は維持、struct 定義を破壊的に書き換える。Tests も同 file 上で書き換え。 |
| Domain `ExpressionSnapshot` / `LayerOverrideMask` | **新規 file** (Option B)。 |
| Adapters `Json/Dto/*` | **既存ファイルを再構成**。`ExpressionDto` の field を入れ替え、`BonePoseDto` / `BonePoseEntryDto` を撤去。 |
| Adapters `ScriptableObject/Serializable/*` | **既存ファイル再構成 + 新規 LayerOverrideMaskSerializable**。 |
| Adapters `InputSources/Keyboard*/Controller*` | **削除**。 |
| Adapters `InputSources/InputActionAnalogSource` | **保持** (Processor 化後も Tick 経路は流用可)。 |
| Adapters `Input/InputSystemAdapter` | **既存改修** (BindExpression に device 推定追加)。 |
| `InputSystem/Adapters/Processors/*` | **新規 file 6 個** (Option B)。 |
| `InputSystem/Adapters/InputSources/ExpressionInputSourceAdapter` | **新規 file 1 個** (Option B)。 |
| Editor `Inspector/FacialCharacterSOInspector` | **既存改修**。BonePose / RendererPath section を削除し、Layer dropdown + AnimationClip ObjectField + read-only renderer paths summary に再構成。 |
| Editor `Tools/ExpressionCreatorWindow` | **既存改修**。BlendShape スライダー編集の出力先を「AnimationClip ベイク」に変更。Preview 部分は再利用。 |
| Editor `AnimationClipExpressionSampler` | **新規** (Option B)。 |
| Editor `AutoExport/FacialCharacterSOAutoExporter` | **既存改修** (サンプラ呼び出しに置換)。 |

**Phased Implementation (推奨フェーズ分割)**:

1. **Phase 1**: Domain 新型 (`ExpressionSnapshot`, `LayerOverrideMask`) を新規追加。既存 `Expression` / `LayerSlot` / `BonePose` は当面そのまま (Adapters 経路で旧型からの bridge を容易にするため)。Tests Red。
2. **Phase 2**: AnimationClip サンプラ (Editor 専用) を実装。Inspector の AnimationClip ObjectField 経由でサンプリング → `ExpressionSnapshot` を生成 → 中間 JSON v2.0 schema へシリアライズ。Tests Green。
3. **Phase 3**: Domain `Expression` / `LayerSlot` / `FacialProfile.BonePoses` を破壊的に書き換え、旧 field 経路を撤去。Tests 大量書き換え。
4. **Phase 4**: InputSystem 連携の Processor 化 + ExpressionInputSourceAdapter 統合 + Category 自動推定。
5. **Phase 5**: Editor Inspector / ExpressionCreatorWindow 全面改修。
6. **Phase 6**: 既存 Sample アセット再生成 + 移行ガイド作成 + CHANGELOG。

**Trade-off**:
- ✅ Phase ごとに Red-Green-Refactor が回せる (Req 12.1)
- ✅ 旧→新の bridge 期間で他 spec (例: OSC) の作業を阻害しない
- ❌ Phase 1〜3 で Domain と Adapters の双方向 bridge を一時的に維持する複雑さ
- ❌ Sample アセット再生成は手作業 (HatsuneMiku モデル依存) のため Phase 6 の負荷大

---

## 4. Effort & Risk

### Effort: **XL** (2+ weeks)

根拠:
- Domain 構造改修 (Expression/LayerSlot/FacialProfile/BonePose) + 派生する Adapters Json / Serializable / Converter / SO Inspector の連鎖改修で 30+ files
- AnimationClip サンプラは Editor 専用の新規機能 (Req 9.1-9.7)
- InputProcessor 6 種 + ExpressionInputSourceAdapter + Category 自動推定 + ExpressionBindingEntry / FacialCharacterSO / FacialCharacterInputExtension 改修
- Tests Red→Green の網羅 (既存 30+ tests 書き換え + 新規 5+ tests)
- 移行ガイド執筆 + Sample アセット再生成

### Risk: **High**

リスク要因:
1. **AnimationClip メタデータ運搬手段**: TransitionDuration / TransitionCurve を AnimationClip にどう埋めるか確定していない。clip events 案は Unity の API で Float 値が運べないため工夫が必要、naming convention 案は人為的ミスを誘発、独立 ScriptableObject 案は AnimationClip との 1:1 リンクが切れやすい。 (Research Needed)
2. **InputProcessor + AnimationCurve**: Unity InputSystem の processor は stateless 制約 (検索結果より)。`AnimationCurve` インスタンス参照を持つ "curve" processor は serializable string parameter 経由で curve key 列を渡す必要があり、preset 4 種 (Linear/EaseIn/EaseOut/EaseInOut) + Custom の表現方法をどうするかが未確定。 (Research Needed)
3. **InputBinding device class 推定**: `InputBinding.path` の `<Keyboard>` / `<Gamepad>` / `<XRController>` プレフィックスからの推定は通常時は単純だが、binding が "Any" / Composite / `<InputDevice>` 等の汎用形になっている場合のフォールバックが面倒 (Req 7.5 で Controller fallback + warning と指定済み、但し処理コードは既存 0 行)。
4. **既存テスト 30+ ファイルの書き換え**: BonePose 系・Json round-trip 系のテストは数百行規模になっているものが多く、書き換え工数が読みにくい。
5. **Sample アセット再生成**: `MultiSourceBlendDemoCharacter.asset` / `AnalogBindingDemoCharacter.asset` を 4 箇所 (Samples~ + Assets/Samples の dual location × 2 sample) で同期し直す必要があり、手作業のミスが発生しやすい (CLAUDE.md の二重管理ルール参照)。
6. **VRM / 将来拡張への影響**: 本改修は VRM 対応 (リリース後マイルストーン) より前に通る破壊的改修なので、VRM の BlendShapeProxy 経路と AnimationClip 由来 RendererPath 解決の整合は将来確認が要る (現段階はスコープ外)。

---

## 5. Research Needed (design フェーズで深掘り)

1. **AnimationClip → TransitionDuration / Curve メタデータ運搬手段**: clip events vs naming convention vs companion ScriptableObject の比較。Unity の AnimationEvent.functionName + floatParameter による埋め込みが現実的か。 (Req 2.4-2.6)
2. **中間 JSON schema v2.0 の詳細**: BlendShape values をスナップショット形式 (時刻ゼロ点の値だけ) で持つか、AnimationCurve 全体をシリアライズして runtime 評価するか。後者は Req 11.1 (0-alloc) と相性が悪い。スナップショット案が有力。 (Req 9.7)
3. **AnimationCurve を InputProcessor のパラメータとして渡す方法**: `[SerializeField] string curveKeysSerialized` 経由で文字列化するか、もしくは preset enum + parameters でカバーする (Linear/EaseIn/EaseOut/EaseInOut の 4 種に絞れば string 不要)。Custom Hermite を InputProcessor で受け持つかは要判断。 (Req 6.1)
4. **AnalogBindingEntry の Domain ↔ Adapters 境界**: Domain `AnalogBindingEntry` は `InputActionReference` を持てない (Domain の Unity 非依存契約)。Domain は SourceId 文字列のみ持ち、Adapters の `AnalogBindingEntrySerializable` だけが `InputActionReference` を持つ階層分離が必要。Domain `AnalogBindingEntry.Mapping` フィールドは廃止 (Req 6.3)。 (Req 13.3)
5. **`AnimationClip.SampleAnimation` のサンプリング戦略**: GameObject に SkinnedMeshRenderer + Animator が存在しないと `SampleAnimation` は機能しないため、Inspector で AnimationClip を assign した瞬間に bind 先 GameObject (リファレンスモデル) が必要。`_referenceModel` フィールド (既存) との連携設計が要件。
6. **Inspector `MaskField` for LayerOverrideMask**: Unity UI Toolkit の `MaskField` は `int` ベースの flags enum しか扱えない。Layer 数が 32 を超えるか、layer 名 / index の整合をどう取るかは layers list と LayerOverrideMask の serialization 形式に依存。
7. **既存 BonePoses をユーザーが AnimationClip transform curves に手動移行する具体手順**: 移行ガイドの記述粒度。Editor menu command を提供するか否か (Req 10.5)。
8. **LayerOverrideMask の Domain 表現**: `Expression` 構造体に `int LayerOverrideMask` を持たせるか、Domain 専用の `LayerOverrideMask` 値型 (将来 64 layer 対応) として独立させるか。

---

## 6. Recommendations for Design Phase

### 6.1 Preferred Approach

**Hybrid (Option C) + 6-Phase Incremental Implementation**。Phase ごとに Tests を Red→Green→Refactor で回し、Domain → Adapters → Editor の順に外側へ展開。

### 6.2 Key Decisions to Make in Design

1. **AnimationClip メタデータ表現**: clip events 採用 (推奨) or naming convention or companion SO。
2. **中間 JSON schema 形式**: snapshot table (推奨) or curve-preserving。
3. **TransitionCurve の表現**: AnimationClip 内の curve から完全復元するか、preset 4 種 + Custom フラグに縮退するか。
4. **AnalogBindingEntry の Domain / Adapters 分離**: Domain は SourceId のみ保持し、Adapters serializable で `InputActionReference` を扱う 2 層構成 (推奨)。
5. **既存 `Expression` / `LayerSlot` を struct のまま中身置換するか、新型へ class 名変更するか**: struct のまま中身置換 (file 名維持) を推奨。外部参照 (Tests / Adapters) の breakage が局所化する。
6. **`AnalogMappingEvaluator` (Domain) の処遇**: InputProcessor 経路では不要となるが、OSC など InputSystem を通らない analog source の互換のため Domain に残す option あり。 (要検討)
7. **`KeyboardExpressionInputSource` / `ControllerExpressionInputSource` の削除タイミング**: Phase 4 で `ExpressionInputSourceAdapter` 完成後に物理削除。`[Obsolete]` 中継期間は preview 段階のため不要。
8. **Inspector validation の error 表示形式**: HelpBox vs Label red text vs UI Toolkit の `Notification`。既存 Inspector が HelpBox を多用しているため踏襲 (Req 1.6, 1.7, 3.5)。

### 6.3 Research Items to Carry Forward (design.md に明記すべき)

- `AnimationUtility.GetCurveBindings` + `AnimationCurve.Evaluate(0f)` を用いた snapshot 生成のパフォーマンス特性
- Unity InputSystem の `InputProcessor<T>` で AnimationCurve を持たせる stateless の制約 (検索結果より stateless 必須)
- `InputBinding.path` のパース regex (`<Keyboard>/...`, `<Gamepad>/...`, `<XRController>/...`) と未認識デバイスのフォールバック仕様
- `AssetModificationProcessor.OnWillSaveAssets` 内で `EditorUtility.DisplayProgressBar` を呼ぶことの安全性

---

## 関連ファイル (絶対パス)

### 仕様書
- `D:\Personal\Repositries\FacialControl\.kiro\specs\inspector-and-data-model-redesign\spec.json`
- `D:\Personal\Repositries\FacialControl\.kiro\specs\inspector-and-data-model-redesign\requirements.md`

### Domain (重点改修対象)
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Domain\Models\Expression.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Domain\Models\LayerSlot.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Domain\Models\BonePose.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Domain\Models\BonePoseEntry.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Domain\Models\FacialProfile.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Domain\Models\AnalogMappingFunction.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Domain\Models\AnalogBindingEntry.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Domain\Services\BonePoseComposer.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Domain\Services\TransitionCalculator.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Domain\Services\AnalogMappingEvaluator.cs`

### Adapters (Json + ScriptableObject + InputSources)
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Adapters\Json\SystemTextJsonParser.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Adapters\Json\Dto\BonePoseEntryDto.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Adapters\Json\Dto\BonePoseDto.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Adapters\ScriptableObject\FacialCharacterProfileSO.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Adapters\ScriptableObject\Serializable\ExpressionSerializable.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Adapters\ScriptableObject\Serializable\LayerSlotSerializable.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Adapters\ScriptableObject\Serializable\BonePoseSerializable.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Adapters\ScriptableObject\Serializable\AnalogMappingFunctionSerializable.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Adapters\ScriptableObject\Serializable\AnalogBindingEntrySerializable.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Adapters\ScriptableObject\Serializable\FacialCharacterProfileConverter.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.inputsystem\Runtime\Adapters\ScriptableObject\FacialCharacterSO.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.inputsystem\Runtime\Adapters\ScriptableObject\ExpressionBindingEntry.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.inputsystem\Runtime\Adapters\ScriptableObject\InputSourceCategory.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.inputsystem\Runtime\Adapters\InputSources\KeyboardExpressionInputSource.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.inputsystem\Runtime\Adapters\InputSources\ControllerExpressionInputSource.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.inputsystem\Runtime\Adapters\InputSources\InputActionAnalogSource.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.inputsystem\Runtime\Adapters\Input\InputSystemAdapter.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.inputsystem\Runtime\Adapters\Input\FacialCharacterInputExtension.cs`

### Editor
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Editor\Tools\ExpressionCreatorWindow.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Editor\Inspector\FacialControllerEditor.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Editor\Common\PreviewRenderWrapper.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Editor\Common\BlendShapeNameProvider.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Editor\Common\BoneNameProvider.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.inputsystem\Editor\Inspector\FacialCharacterSOInspector.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.inputsystem\Editor\AutoExport\FacialCharacterSOAutoExporter.cs`

### Sample アセット (再生成対象)
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.inputsystem\Samples~\MultiSourceBlendDemo\MultiSourceBlendDemoCharacter.asset`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.inputsystem\Samples~\MultiSourceBlendDemo\StreamingAssets\FacialControl\MultiSourceBlendDemoCharacter\profile.json`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.inputsystem\Samples~\AnalogBindingDemo\AnalogBindingDemoCharacter.asset`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.inputsystem\Samples~\AnalogBindingDemo\StreamingAssets\FacialControl\AnalogBindingDemoCharacter\analog_bindings.json`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.inputsystem\Samples~\AnalogBindingDemo\StreamingAssets\FacialControl\AnalogBindingDemoCharacter\profile.json`
- `D:\Personal\Repositries\FacialControl\FacialControl\Assets\Samples\FacialControl InputSystem\0.1.0-preview.1\Multi Source Blend Demo\MultiSourceBlendDemoCharacter.asset`
- `D:\Personal\Repositries\FacialControl\FacialControl\Assets\Samples\FacialControl InputSystem\0.1.0-preview.1\Analog Binding Demo\AnalogBindingDemoCharacter.asset`

### asmdef (依存方向確認)
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Domain\Hidano.FacialControl.Domain.asmdef`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Editor\Hidano.FacialControl.Editor.asmdef`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.inputsystem\Editor\Hidano.FacialControl.InputSystem.Editor.asmdef`

---

## Sources

- [Unity Input System Processors documentation (1.11.2)](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.11/manual/Processors.html)
- [Unity Input System Processors documentation (1.4.4)](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.4/manual/Processors.html)
- [Unity AnimationUtility.GetCurveBindings ScriptReference](https://docs.unity3d.com/ScriptReference/AnimationUtility.GetCurveBindings.html)
- [Unity EditorCurveBinding ScriptReference](https://docs.unity3d.com/ScriptReference/EditorCurveBinding.html)
- [Unity AnimationClip ScriptReference (Unity 6)](https://docs.unity3d.com/6000.2/Documentation/ScriptReference/AnimationClip.html)
- [UnityCsReference AnimationUtility.bindings.cs](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/Animation/AnimationUtility.bindings.cs/)
