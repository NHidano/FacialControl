# Gap Analysis: adapter-binding-architecture

## Analysis Summary

- 既存の core パッケージは **`FacialCharacterProfileSO` 単一継承モデル** を採用しており（`FacialCharacterProfileSO.cs` を `FacialCharacterSO`（inputsystem）が継承）、本仕様の根幹である「単一の `FacialCharacterProfileSO` に複数アダプタ設定を抱える」コンポジション化に対して **構造的に対立** している。OSC / InputSystem は MonoBehaviour Extension（`OscFacialControllerExtension` / `InputFacialControllerExtension` / `FacialCharacterInputExtension`）+ `IFacialControllerExtension` インターフェース + `InputSourceFactory.RegisterReserved` 経路で配線されており、Req 6.8/6.9（Extension 撤廃）・Req 13（VContainer Lifecycle 委譲）と全面的に競合する。
- **`InputSourceFactory` + 予約 ID 体系（`InputSourceId` の `osc` / `lipsync` / `input` / `analog-blendshape` / `analog-bonepose`）は現状動作の軸**。Req 12.5（`IsReservedId` 廃止）+ D-13（slug 統一）はこのコア機構を root から塗り替える破壊変更となる。`InputSourceId` の正規表現バリデーション（Req 12.6）は再利用可能。
- **MultiSourceBlend ロジック自体は既に Domain 側で実装済み**（`LayerInputSourceAggregator` / `LayerInputSourceWeightBuffer` / `LayerInputSourceRegistry` は既に `output[k] = clamp01(Σ wᵢ · values_i[k])` を 0-alloc で実装、`AggregateAndBlend` で 2 段パイプライン完備）。Req 5 の「core 機能昇格」は **概念的にはほぼ完了済み** であり、core パッケージから `inputsystem` への参照は無い。残作業は core の `Samples~` 配下に Mock binding ベースの最小サンプルを切り出し（D-14）、現行 `inputsystem` 側 `MultiSourceBlendDemo` HUD を slug ベースへ書き換える程度。
- **`[SerializeReference]` / `TypeCache.GetTypesWithAttribute<T>` / `[CustomPropertyDrawer]` のいずれもプロジェクト内で未使用**（Grep でゼロヒット）。Req 1〜3 の根幹はすべて新規導入で、Unity の polymorphic serialization / `SerializeReference` Inspector のクセ（null entry、型欠落、null deserialization）に対する経験値ゼロからスタートとなる（Risk: High）。
- **VContainer は manifest.json/asmdef のいずれにも未参照**。Req 4 (Addendum) / Req 13 / D-5, D-9, D-10, D-12（VContainer 採用 + 中立 `AdapterBuildContext` + `AdapterBindingHost` ラッパー + per-character LifetimeScope）はゼロからの設計・依存追加が必要で、本 spec で最も Risk が高い領域。
- 既存の `FacialController` ライフサイクル（`OnEnable` / `LateUpdate` / `OnDisable` で MonoBehaviour Extension 駆動）は Req 13.2（`IStartable` / `ITickable` / `ILateTickable` / `IFixedTickable` 委譲）に置換が必要。`FacialController` の `LateUpdate` は本仕様の `OnLateTick` に相当する自然な移行対象。
- **既存の OSC のライセンス実装は `OscReceiver` という MonoBehaviour に直接 socket 寿命を委ねている**。Req 13.6 / D-11 の「binding が `AddComponent` で補助 MonoBehaviour を動的生成し binding 自身が破棄」という方針への移行は、`OscReceiver` 自体を helper 化する形に再構成する必要あり。

---

## 1. Requirement-to-Asset Map

### Requirement 1: `AdapterBindingBase` 抽象 + 属性ベース discovery

| AC | 既存資産 / ファイル | ギャップ |
|----|-------------------|---------|
| 1.1 `AdapterBindingBase` 抽象クラス（Domain、Unity 非依存） | **Missing**。比較対象は `Hidano.FacialControl.Domain` asmdef（`Unity.Collections` のみ参照）= `Runtime/Domain/Hidano.FacialControl.Domain.asmdef` | 新規作成（Domain 層）。`IInputSource`（`Runtime/Domain/Interfaces/IInputSource.cs:29`）と同じ純粋 Domain 配置パターンを踏襲可能 |
| 1.2 `FacialAdapterBindingAttribute(displayName)` | **Missing**。属性派生型は project 全体でゼロ | 新規作成（Domain 層）。System.Attribute の 1 個追加のみで low effort |
| 1.3 `TypeCache.GetTypesWithAttribute<T>()` による discovery | **Missing**。`Grep TypeCache` で 0 件 | 新規。Editor 専用 `AdapterBindingDiscovery` ヘルパー（`Hidano.FacialControl.Editor` asmdef）として実装。Editor load 時に static cache 化 |
| 1.4 Add Adapter Binding ドロップダウン（`displayName` 順） | **Missing**。`FacialCharacterProfileSOInspector.cs` は UI Toolkit ベースだが、polymorphic コレクション編集 UI は持たない | 新規。UI Toolkit `DropdownField` + `MenuController` 等で構築 |
| 1.5 Core が adapter 具象型を import しない契約 | **Constraint Already Met (asmdef レベル)**。`Hidano.FacialControl.Adapters.asmdef` は `Hidano.FacialControl.InputSystem` / `.Osc` を参照しない | 維持。asmdef テストで自動チェックを追加するのが望ましい |
| 1.6 新規 adapter package 追加で core 改変ゼロ | **Constraint Already Met (asmdef)** + **Missing (実装)**。現 `IFacialControllerExtension` は同等の哲学だが MonoBehaviour ベース | 新 binding ベースに置換 |
| 1.7 重複 `displayName` 警告 + 型名 suffix | **Missing** | 新規ロジック（Editor 側）|

### Requirement 2: `FacialCharacterProfileSO.AdapterBindings` フィールド

| AC | 既存資産 | ギャップ |
|----|---------|---------|
| 2.1 `[SerializeReference] List<AdapterBindingBase> AdapterBindings` | **Missing**。`FacialCharacterProfileSO.cs:15-18` は `_layers` / `_expressions` / `_rendererPaths` を持つが `[SerializeReference]` 採用ゼロ | 新規。ProjectSettings の serialization 互換確認が必要（preview 段階のため破壊変更は許容） |
| 2.2 上限なし | **Already supported by `List<T>`** | なし |
| 2.3 polymorphic round-trip | **Missing**。Unity の `[SerializeReference]` 仕様調査必要（型移動時の `MissingReferenceException` 等） | Research Needed: Unity 6 の `SerializeReference` 動作（型 rename / 名前空間移動時の挙動） |
| 2.4 同型 binding 複数追加可（D-4 slug で衝突解決） | **Missing** | 新規。Inspector レイヤーでの slug 重複検証（Req 12.3）と組合せ |
| 2.5 `Activator.CreateInstance` で append | **Missing** | 新規。Inspector ロジック |
| 2.6 Remove で dirty 化 | **Missing** | 新規。Inspector ロジック |
| 2.7 missing assembly 時の "Missing Adapter" placeholder | **Missing**。Unity の `[SerializeReference]` は型欠落時 null になる仕様の確認が必要 | Research Needed |

### Requirement 3: PropertyDrawer 提供契約

| AC | 既存資産 | ギャップ |
|----|---------|---------|
| 3.1 Core が PropertyDrawer を登録しない | **Constraint** | 新規実装で意識的に守る |
| 3.2 各 adapter package が `[CustomPropertyDrawer(typeof(...))]` 提供 | **Missing**。`Grep CustomPropertyDrawer` で 0 件 | 新規。InputSystem / OSC それぞれの Editor asmdef 配下に PropertyDrawer 追加（既存 `FacialCharacterSOInspector` の UI 構成を参考に転用） |
| 3.3 Core の list レンダリングが PropertyDrawer 解決を委任 | **Missing** | 新規。UI Toolkit の `PropertyField` を使えば自動解決される |
| 3.4 PropertyDrawer 不在時のデフォルト fallback | **Already supported by Unity 標準** | なし |
| 3.5 surrounding chrome は UI Toolkit | **Constraint Already Met**。`FacialCharacterProfileSOInspector.cs:116` `CreateInspectorGUI` で UI Toolkit 採用 | 維持 |
| 3.6 PropertyDrawer 例外時の per-element placeholder | **Missing** | 新規。try/catch + fallback element |

### Requirement 4: パッケージ依存方向と Domain 純度

| AC | 既存資産 | ギャップ |
|----|---------|---------|
| 4.1 Core が adapter asmdef を参照しない | **Already Met**。`Hidano.FacialControl.Domain/Application/Adapters/Editor.asmdef` のいずれも `InputSystem` / `Osc` を参照しない | 維持 |
| 4.2 `AdapterBindingBase` が Domain & Unity 非依存（`Unity.Collections` のみ） | **Constraint**。Domain asmdef は既に該当（`Hidano.FacialControl.Domain.asmdef:4-6`） | 守る |
| 4.3 具象 binding が adapter asmdef + core Domain 参照 | **Already supported by current asmdef graph** | 維持 |
| 4.4 `FacialAdapterBindingAttribute` が core 提供 | 同上 | 新規（Req 1.2 と一体） |
| 4.5 TypeCache クエリが asmdef hard reference を取らない | **Constraint Already Met**（TypeCache の仕様上） | 維持 |
| 4.6 adapter symbol 参照は redesign 拒否 | **Process constraint** | レビュー方針 |

### Requirement 4 (Addendum): VContainer 統合

| AC | 既存資産 | ギャップ |
|----|---------|---------|
| 4.7 Adapters 層で VContainer 採用 + per-`FacialController` LifetimeScope + app-level parent scope | **Missing**。`Grep VContainer` ゼロヒット、`manifest.json` に未追加 | **新規導入**。`com.hidano.vcontainer`（OpenUPM）or `jp.hadashikick.vcontainer` の package 追加 + `FacialController` を VContainer 統合 (Risk: High) |
| 4.8 Domain は VContainer 非参照 | **Constraint** | Adapters 層側でのみ参照する asmdef 設計が必要 |
| 4.9 `AdapterBindingHost` ラッパーが `IStartable`/`ITickable`/`ILateTickable`/`IFixedTickable`/`IDisposable` 実装 | **Missing** | 新規（Adapters 層） |
| 4.10 具象 binding は VContainer interface 非依存 | **Constraint** | binding 実装者は plain virtual override のみ |

### Requirement 5: MultiSourceBlend の core 機能昇格

| AC | 既存資産 / ファイル | ギャップ |
|----|-------------------|---------|
| 5.1 MultiSourceBlend ドメインモデルが core / Domain・Application 提供 | **Already Met**。`Runtime/Domain/Services/LayerInputSourceAggregator.cs` / `LayerInputSourceWeightBuffer.cs` / `LayerInputSourceRegistry.cs` が完全実装。`Application/UseCases/LayerUseCase.cs` でユースケース化済 | なし |
| 5.2 core が adapter package 非依存 | **Already Met**。core asmdef は inputsystem / osc を参照しない | なし |
| 5.3 `output[k] = clamp01(Σ wᵢ · values_i[k])`（正規化なし） | **Already Met**。`LayerInputSourceAggregator.cs:319-378` で実装（per-source weight × scratch 加算 → 0/1 clamp） | なし |
| 5.4 Core service が任意 `AdapterBindingBase` 派生を受け入れ | **Partial**。現状は `IInputSource` を受ける契約。binding が IInputSource を produce するブリッジ（D-3）が必要 | 新規。`AdapterBinding` → `IInputSource` 登録経路を `InputSourceFactory` 後継として設計 |
| 5.5 `LayerBlender` API 維持 + per-layer 集約供給 | **Already Met**。`LayerInputSourceAggregator.AggregateAndBlend`（同 ln 259-271）が `LayerBlender.Blend` を直接呼ぶ | なし |
| 5.6 `Samples~/MultiSourceBlendBasicSample/`（or 等価名）で Mock binding 利用 | **Missing**。core `package.json:21` の `samples` 配列は空。現 demo は `inputsystem/Samples~/MultiSourceBlendDemo/` のみ | 新規。core 側に Mock binding ベースの最小サンプル追加。HUD なし。Sample 名は D-14 で確定（design phase 課題） |
| 5.7 GC 0-alloc | **Already Met**。Aggregator は 0-alloc 設計（`Runtime/Domain/Services/LayerInputSourceAggregator.cs` ヘッダーコメント、`Tests/PlayMode/Performance/SetWeightZeroAllocationTests.cs` 等） | なし |

### Requirement 6: 既存 adapter の新アーキテクチャ移行

| AC | 既存資産 | ギャップ |
|----|---------|---------|
| 6.1 `InputSystemAdapterBinding`（`displayName: "Input System"`）提供 | **Missing**。`FacialCharacterSO.cs` + `FacialCharacterInputExtension.cs` + `InputFacialControllerExtension.cs` + `InputRegistration.cs` の責務を 1 binding に統合する大規模作業 | 新規実装 + 既存 4 ファイル削除 |
| 6.2 `OscReceiverAdapterBinding`（`displayName: "OSC"`）提供 | **Missing**。`OscFacialControllerExtension.cs` + `OscRegistration.cs` の責務を 1 binding に統合。さらに `OscReceiver` の socket lifecycle を binding 配下へ移管 | 新規 + 既存 2 ファイル削除 + `OscReceiver` を helper 化 |
| 6.3 `ARKitAdapterBinding`（`displayName: "ARKit / PerfectSync"`）提供 | **Constraint**。現状 ARKit は `Runtime/Application/UseCases/ARKitUseCase.cs` + `Runtime/Domain/Services/ARKitDetector.cs` + `Editor/Windows/ARKitDetectorWindow.cs` で **Editor 検出 → Expression 自動生成のみ**（runtime input source ではない）。OSC 経由で `ArKitOscAnalogSource` (osc package) を介して入力する形 | Research Needed: ARKit binding が「runtime 入力源」として何を produce するのか（ARKit OSC 受信を担うのか、別チャネルなのか）の決定。requirement 6.3 は **新規パッケージ or osc package 内 binding として再構成** |
| 6.4 `FacialCharacterSO`（inputsystem 派生）削除 | **Existing Asset**。`Runtime/Adapters/ScriptableObject/FacialCharacterSO.cs:36`（200 行超） | 削除対象 |
| 6.5 各 migrated adapter package が PropertyDrawer 提供 | **Partial**。現 `FacialCharacterSOInspector.cs` の UI ロジックは流用可能だが PropertyDrawer 形式への再構成が必要 | 新規（既存 inspector 流用） |
| 6.6 単一 SO に Input/OSC/ARKit 同時保持・round-trip | **Missing** | Req 2 + 6.1〜6.3 完了後に検証 |
| 6.7 preview-stage 破壊変更扱い | **Process constraint** | CHANGELOG / Documentation 更新で対応 |
| 6.8 `FacialCharacterInputExtension` / `InputFacialControllerExtension` 削除 + `InputSystemAdapterBinding.OnStart` 等へ移植 | **Existing**。`FacialCharacterInputExtension.cs:48`（576 行）のロジック（ActionMap Instantiate / Enable, Analog source 構築, BonePose / GazeBone provider 構築, BindAllExpressions, LateUpdate Tick）すべて binding lifecycle へ再配置 | 大規模リファクタ。`OnStart`（asset/action map setup）→ `OnLateTick`（Tick + Apply）→ `Dispose`（teardown）への分割が必要 |
| 6.9 `OscFacialControllerExtension` 削除 + `OscReceiverAdapterBinding` が socket lifecycle 管理（補助 MonoBehaviour `AddComponent` 許容） | **Existing**。`OscFacialControllerExtension.cs:19`（76 行）+ 別 MonoBehaviour `OscReceiver` / `OscSender`（`Runtime/Adapters/OSC/`） | 大規模リファクタ。binding 内で `_facialController.gameObject.AddComponent<OscSocketHelper>()` 形に再構成 |
| 6.10 reserved-id 登録経路撤廃 | **Existing**。`InputRegistration.cs` / `OscRegistration.cs` / `InputSourceFactory` ビルトイン登録（`InputSourceFactory.cs:104-120`）を全廃 | 削除対象 |

### Requirement 7: 単一 SO ファイル UX

| AC | 既存資産 | ギャップ |
|----|---------|---------|
| 7.1 reorderable list + Add ドロップダウン + Remove + Move | **Missing**。`FacialCharacterProfileSOInspector.cs` は layers / expressions の reorderable 機能あり | 新規。UI Toolkit `ListView` + drag handle 等 |
| 7.2 単一 asset への永続化 | **Already Met**（`[SerializeReference]` 採用後） | 維持 |
| 7.3 secondary asset / SO / component 不要 | **Conflict**。現状は `FacialCharacterInputExtension` 等の MonoBehaviour 配置が必須。Req 6.8 / 6.9 移行で解消 | 移行で対応 |
| 7.4 inline Asset 参照（`InputActionAsset` 等） | **Conflict**。`InputActionAsset` への参照は `FacialCharacterSO._inputActionAsset` で持っている既存資産あり。binding に inline で同等のフィールドを再配置 | 移行で対応 |
| 7.5 inline 設定（OSC port 等） | **Missing**。現 `OscReceiver` は MonoBehaviour 上で port を持っているため、`OscReceiverAdapterBinding` 内に port フィールドを移す | 移行で対応 |

### Requirement 8: 破壊的変更ポリシー

| AC | 既存資産 | ギャップ |
|----|---------|---------|
| 8.1 旧 SO load 不可 | **Process constraint** | preview policy で許容 |
| 8.2 CHANGELOG 破壊明記 | **Existing convention**（`Packages/com.hidano.facialcontrol/CHANGELOG.md`） | エントリ追加 |
| 8.3 Migration code 不要 | **Constraint** | 何もしない |
| 8.4 ユーザー再オーサリング | **Process** | Documentation~ で説明 |
| 8.5 削除型一覧の Documentation 記載 | **Missing**。現 `Documentation~/` の有無確認が必要 | 新規ドキュメント |

### Requirement 9: GC 0-alloc

| AC | 既存資産 | ギャップ |
|----|---------|---------|
| 9.1 steady-state 0-alloc | **Already Met for MultiSourceBlend**（`LayerInputSourceAggregator` 0-alloc）。binding lifecycle 経路は新規のため要設計 | 新規（binding `OnTick` が 0-alloc であること） |
| 9.2 reflection を per-frame に出さない | **Constraint**。新規 discovery は Editor / load-time のみ | 守る |
| 9.3 load-time pre-allocation | **Already Met**。`InitializeInternal` で per-source / per-layer 配列を pre-alloc | 維持 |
| 9.4 線形スケール | **Already Met**（既存 `MultiCharacterPerformanceTests.cs` で検証） | 維持 |
| 9.5 perf test スイート（>=3 binding で 0-alloc 検証） | **Missing**。テストインフラ（`Tests/PlayMode/Performance/GCAllocationTests.cs`）は流用可能 | 新規テスト |

### Requirement 10: テスト要件

| AC | 既存資産 | ギャップ |
|----|---------|---------|
| 10.1 TDD | **Process constraint** | 守る |
| 10.2 EditMode テスト（serialization / TypeCache discovery / MultiSourceBlend with fakes / dropdown sort） | **Partial**。MultiSourceBlend EditMode テスト（`Tests/EditMode/Domain/LayerInputSourceAggregatorTests.cs` 等）は既存 | 新規 binding 用テスト追加 |
| 10.3 PlayMode テスト（end-to-end / per-frame / 0-alloc） | **Partial**。`Tests/PlayMode/Performance/` の既存パターンを流用可能 | 新規 |
| 10.4 Inspector テストは EditMode | **Constraint** | 守る |
| 10.5 実 device dispatch は PlayMode | **Constraint**。既存 `Tests/PlayMode/Adapters/InputSystemAdapterTests.cs` 等の patterns | 流用 |
| 10.6 命名規則 | **Existing convention**（CLAUDE.md / steering） | 守る |
| 10.7 perf test 自体が 0-alloc | **Constraint** | 守る |

### Requirement 12: Slug 命名と ID 解決（D-13 reserved id 廃止）

| AC | 既存資産 | ギャップ |
|----|---------|---------|
| 12.1 `slug: string` フィールド（serialized） | **Missing** | 新規。`AdapterBindingBase.slug` |
| 12.2 displayName から auto-populate（kebab-case） | **Missing** | 新規。Editor 側 helper |
| 12.3 SO 内 slug ユニーク検証 + duplicate 時に save block | **Missing**。Editor save block API の調査必要 | Research Needed: `AssetDatabase.SaveAssetIfDirty` のキャンセル可否 |
| 12.4 `<slug>` または `<slug>:<sub>` 形式の lookup | **Partial**。現 `LayerInputSourceRegistry.cs` は (layerIdx, sourceIdx) ベース。slug → factory lookup は `InputSourceFactory.IsRegistered(InputSourceId)` 相当を持つが、reserved id 体系 | 新規。Slug 解決ロジックを `InputSourceFactory` 後継に追加 |
| 12.5 `InputSourceId.IsReservedId` / `ReservedIds` 廃止 | **Existing**。`Runtime/Domain/Models/InputSourceId.cs:31-38` の `ReservedIds[]` 配列、`IsReservedId(string)`（96 行）、`IsReserved` プロパティ。`InputSourceFactory.cs:259-264` で `RegisterExtension` が `IsReserved` をブロックしている | 削除 + 該当 caller 調整 |
| 12.6 既存 `InputSourceId` バリデーション（length ≤ 64, charset, x- prefix）は slug にも継続適用 | **Already Met**。`InputSourceId.cs:25-26` の正規表現 `^[a-zA-Z0-9_.-]{1,64}$` を slug に流用可 | 維持。x- prefix 概念は slug に残すか design で決定 |
| 12.7 既存 `layer.inputSources[].id` の slug 書換（implicit fallback なし） | **Process**。preview 段階のため explicit | サンプル / Documentation 更新 |

### Requirement 13: Lifecycle 契約（VContainer Entry Points）

| AC | 既存資産 | ギャップ |
|----|---------|---------|
| 13.1 `OnStart` / `OnTick` / `OnLateTick` / `OnFixedTick` / `Dispose` virtual no-op | **Missing** | 新規（Domain）|
| 13.2 `AdapterBindingHost` が VContainer interface 実装 | **Missing**。VContainer 自体未導入 | 新規（Adapters）。VContainer pkg 追加が前提 |
| 13.3 binding は必要な lifecycle のみ override | **Constraint** | 守る |
| 13.4 例外 catch + LogError + skip | **Partial**。`FacialController.ApplyExtensions` (`FacialController.cs:281-289`) で `IFacialControllerExtension` の例外を catch + Debug.LogError する patterns あり。同等の方針を binding host に拡張 | 流用 + 拡張 |
| 13.5 skip 後も他 binding と core が継続 | 同上 | 守る |
| 13.6 `AddComponent` で helper MonoBehaviour 作成可（HideFlags 通常表示） | **New pattern**。現状 `OscReceiver` 等は手動配置 | 新規 |
| 13.7 `Dispose` で helper を `Object.Destroy` | **New pattern** | 新規 |

### Requirement 11: クリーンアーキテクチャ契約

| AC | 既存資産 | ギャップ |
|----|---------|---------|
| 11.1 Domain & Unity 非依存（`Unity.Collections` のみ）| **Already Met**（Domain asmdef） | 維持 |
| 11.2 Engine-side 依存を持つ binding は Adapters 配置 | **Constraint** | 守る |
| 11.3 auto-discovery / per-element renderer は Editor 専用 | **Constraint**。`Editor/Hidano.FacialControl.Editor.asmdef` は `includePlatforms: ["Editor"]` | 維持 |
| 11.4 UI Toolkit | **Already Met** | 維持 |
| 11.5 Domain は plain C# / `Unity.Collections` | **Constraint** | 守る |
| 11.6 Runtime UI 提供禁止 | **Constraint**（プロジェクト規約） | 守る |

---

## 2. Architectural Patterns Already in Use

新 feature が踏襲すべき確立 pattern：

1. **`InputSourceFactory` の `Register<TOptions>` + `Func<TOptions, int, FacialProfile, IInputSource>` ジェネリック登録** — `InputSourceFactory.cs:188-214`（`RegisterReserved`）。Slug-based factory 後継でも同じ generic + closure pattern を採用可能。
2. **`InputSourceId` 値オブジェクト方式 + `TryParse` バリデーション** — `Runtime/Domain/Models/InputSourceId.cs:68-78`。Slug の値オブジェクト化（例: `AdapterSlug` struct）に流用。
3. **Domain サービスの 0-alloc 設計（pre-alloc + scratch + double buffer）** — `LayerInputSourceAggregator` / `LayerInputSourceWeightBuffer` / `LayerInputSourceRegistry`。binding 経路でも踏襲。
4. **`IFacialControllerExtension` MonoBehaviour 自動検出 + 例外 isolation** — `FacialController.cs:275-291`（`GetComponents<IFacialControllerExtension>()` + try/catch）。Req 13.4 / 13.5 のスキップ動作の参考。
5. **UI Toolkit カスタム Inspector の階層化（base inspector + derived hook）** — `FacialCharacterProfileSOInspector.cs:163-200`（virtual hook：`OnResolveDerivedSerializedProperties` / `OnBuildPreLayersSections` / `FindGazeConfigsProperty`）。Req 1.4 ドロップダウン UI のホスト場所として活用可能。
6. **`FacialController.Initialize` / `Cleanup` 対称ライフサイクル + `_isInitialized` フラグ** — binding 集合の build / dispose にも同形を継承。
7. **Tests/EditMode/Domain と Tests/PlayMode/Performance の振り分け** — Req 10 の配置基準と一致。
8. **package.json `samples[]` 配列で Sample 配布** — `inputsystem/package.json:15-21`。Req 5.6 では core/package.json に同形で `MultiSourceBlendBasicSample` を追加。
9. **二重管理 Sample (`Samples~/` ⇄ `Assets/Samples/`)** — 構造（structure.md）。Req 5.6 の core サンプルでも踏襲（HUD なし最小コードのため運用負荷は低い見込み）。
10. **`StreamingAssets/FacialControl/{name}/profile.json` 命名** — `FacialCharacterProfileSO.cs:11-14`。新 binding が JSON 形式の永続を持つ場合の参考。

---

## 3. Conflicts / Contradictions

| # | Conflict | 影響範囲 | 解決方針 |
|---|----------|---------|---------|
| C-1 | **`FacialCharacterSO` (inputsystem) が `FacialCharacterProfileSO` を継承する単一継承モデル** vs Req 2（コンポジション化） | `Runtime/Adapters/ScriptableObject/FacialCharacterSO.cs:36`、`Editor/Inspector/FacialCharacterSOInspector.cs`、`Editor/AutoExport/FacialCharacterSOAutoExporter.cs`、関連テスト群 | preview 破壊許容（Req 6.4, 8.1）。SO・関連 inspector・auto-exporter ・テストすべて削除 |
| C-2 | **`IFacialControllerExtension` MonoBehaviour 拡張モデル** vs Req 6.8 / 6.9（Extension 撤廃 + binding 自動管理） | `Runtime/Adapters/Playable/IFacialControllerExtension.cs`、`OscFacialControllerExtension.cs`、`InputFacialControllerExtension.cs`、`FacialCharacterInputExtension.cs`、`FacialController.ApplyExtensions`（`FacialController.cs:275-291`） | `IFacialControllerExtension` interface ごと削除し、`FacialController` を VContainer LifetimeScope build 経路に置換 |
| C-3 | **`InputSourceId.ReservedIds` + `IsReserved` + `RegisterExtension` の x- prefix 強制** vs Req 12.5 / D-13（reserved id 廃止 + slug 統一） | `InputSourceId.cs:31-112`、`InputSourceFactory.cs:217-276`、`OscRegistration.cs:35` / `InputRegistration.cs:48` 等の `Parse(ReservedId)` 呼び出し全部 | 「reserved」概念を全廃。`AdapterSlug` 等の新値オブジェクトに置換 |
| C-4 | **`InputSourceFactory.TryCreate` ベースの (id, options) → IInputSource ディスパッチ** vs Req 5.4 / D-3（binding が IInputSource を直接 produce） | `Runtime/Adapters/InputSources/InputSourceFactory.cs`、`FacialController.BuildAdditionalInputSources`（`FacialController.cs:293-336`）、`Runtime/Adapters/Json/Dto/InputSourceDto.cs` 等の DTO 系 | 新 binding factory model へ移行。`InputSourceFactory` 自体は段階的廃止（slug-based の薄い lookup として再生 or 削除） |
| C-5 | **`OscReceiver` / `OscSender` MonoBehaviour 直接配置** vs Req 7.5（OSC 設定が SO inline） + D-11（binding が helper MonoBehaviour を AddComponent） | `Runtime/Adapters/OSC/OscReceiver.cs`、`Runtime/Adapters/OSC/OscSender.cs`、`OscFacialControllerExtension.cs` | binding 内で `AddComponent` する補助 MonoBehaviour として再配置。Inspector 側は port / endpoint を binding inline で編集 |
| C-6 | **`ARKitDetector` / `ARKitUseCase` は Editor 検出 + Expression 自動生成のみ**（runtime 入力源ではない） vs Req 6.3（`ARKitAdapterBinding` 実装要求） | `Runtime/Application/UseCases/ARKitUseCase.cs`、`Runtime/Domain/Services/ARKitDetector.cs`、`Editor/Windows/ARKitDetectorWindow.cs`、`Runtime/Adapters/InputSources/ArKitOscAnalogSource.cs`（osc package） | Research Needed: `ARKitAdapterBinding` を「OSC 経由 ARKit float 入力源」として osc package 内 or 別 package で実装するか design で決定 |
| C-7 | **`MultiSourceBlendDemo`（inputsystem package）の HUD `_inputSourceIndex` ベースの直接 index 駆動** vs Req 12（slug-based id） | `Samples~/MultiSourceBlendDemo/MultiSourceBlendDemoHUD.cs:30-32`（`_inputSourceIndex = 1`） + 二重管理ミラー `Assets/Samples/...` | HUD 改修必要。`SetInputSourceWeight(int layerIdx, int sourceIdx, ...)` を slug-based lookup に置換するか、新 API を追加 |
| C-8 | **既存 `FacialController.LateUpdate` での Aggregator 駆動 + BoneWriter Apply** vs Req 13.2（VContainer ITickable 経路） | `FacialController.cs:117-149` | `FacialController` を LifetimeScope ホストに再構成し、現 `LateUpdate` は `ILateTickable` の host が委譲する形へ。BoneWriter Apply のタイミング契約は維持 |
| C-9 | **既存テスト群が `IFacialControllerExtension` / `FacialCharacterSO` / `InputSourceId.IsReserved` 等を直接 assert** | `Tests/EditMode/Adapters/Playable/FacialControllerCharacterSOTests.cs`、`Tests/EditMode/Editor/Inspector/FacialCharacterSOInspectorTests.cs`、`Tests/EditMode/Domain/InputSourceIdTests.cs`、`Tests/EditMode/Integration/OscControllerBlendingIntegrationTests.cs`、その他 InputSystem / OSC package 配下テスト群 | テスト群の大規模書き換えが必要。preview 段階のため削除 + 新規が許容 |

---

## 4. Implementation Approach Options

### Option A: Big-bang 全置換（推奨度: Low-Medium）

新 binding model + VContainer + slug 体系を 1 PR でまとめて core + osc + inputsystem に投入し、旧 `FacialCharacterSO` / `IFacialControllerExtension` / `InputSourceFactory` / `InputSourceId.ReservedIds` をすべて同時削除。

- **トレードオフ**: 最終形と途中形の不整合を残さない / preview 段階なら破壊許容で問題なし。一方でレビュー粒度が巨大（推定 30+ ファイル / 5,000+ LOC change）。回帰範囲が広く、PlayMode 性能テスト・実 OSC・実 InputSystem の多面的検証が同時に必要。
- **適合度**: 仕様書は preview 破壊許容を明示しており、実装方針として最も誠実。ただし PR レビュー負荷の観点では推奨しにくい。

### Option B: 二段階移行（core 新基盤 → adapter 移行）（推奨）

**Phase 1**: core に `AdapterBindingBase` / `FacialAdapterBindingAttribute` / `[SerializeReference] AdapterBindings` / VContainer 統合 / `AdapterBindingHost` / Mock binding ベースの core サンプルを追加し、core の単体テストを通す。**この時点では既存の `IFacialControllerExtension` / `InputSourceFactory` / `FacialCharacterSO` は併存** させる（新旧並走）。
**Phase 2**: osc / inputsystem package の binding 化 + 旧 Extension / `FacialCharacterSO` / `InputSourceFactory` / `InputSourceId.ReservedIds` 一括削除 + Sample HUD 移行 + CHANGELOG。

- **トレードオフ**: 各 PR が中規模で済む。Phase 1 完了時点で「新 model がコア独立で動く」検証が可能。一方で Phase 1 〜 2 の間に並走コードが Repo に残る期間あり。Phase 1 で新旧両方を維持するため core 側に小規模な glue 追加が必要（`FacialController` が新旧両モードを判定）。
- **適合度**: 仕様書 Req 6 の「新アーキテクチャに移行する」を 2 PR に分割。preview 段階の実情に合致。

### Option C: Hybrid - 既存 `InputSourceFactory` を slug factory として再生（推奨度: Medium）

`InputSourceFactory` を完全削除せず、**slug-keyed の薄い lookup table として保持** し、binding が `OnStart(ctx)` で `ctx.SourceFactory.RegisterSlug(slug, source)` を呼ぶ形に再生。`AdapterBuildContext` struct（D-5）に `IInputSourceFactory` を埋め込めば、現 `LayerInputSourceRegistry` / `LayerInputSourceAggregator` の (layerIdx, sourceIdx) スキーマを温存しつつ、layer.inputSources[].id の resolve 経路だけ slug ベースに換装できる。

- **トレードオフ**: Domain 層 MultiSourceBlend のコード変更を最小化（既存 0-alloc / 既存テスト資産を最大温存）。一方で `InputSourceFactory` という名前が「reserved id ベース」を連想させ、命名と内容が乖離。リネーム（例: `InputSourceRegistry`）も検討。
- **適合度**: Domain 層のリスク最小化と Req 5（MultiSourceBlend 機能の維持）を最も確実に守る。Adapters 層の変更だけで Req 1-4, 6-13 を消化できる戦略。

**推奨: Option B（二段階移行） + 内部的に Option C を採用**（既存 `InputSourceFactory` の lookup 機能をリネーム保持し binding が直接登録する形に再生）。

---

## 5. Effort & Risk

| 区分 | 規模 | Risk | 根拠 |
|------|------|------|------|
| Domain: `AdapterBindingBase` / `FacialAdapterBindingAttribute` / `AdapterBuildContext` / Slug VO | **S** (1〜2 日) | Low | 既存 `InputSourceId` value-object pattern を踏襲。Plain C# 抽象クラス + Attribute 1 個 |
| Adapters: VContainer 統合 + `AdapterBindingHost` + per-`FacialController` LifetimeScope | **L** (1〜2 週) | **High** | VContainer 未経験 + manifest.json 追加 + lifecycle 委譲 + 例外 isolation + 0-alloc 維持。version pinning 含めた research が必要 |
| Adapters: `FacialController` の Initialize / Cleanup を VContainer build 経路へ移行 | **M** (3〜5 日) | High | 既存 `BuildAdditionalInputSources` / `ApplyExtensions` を全置換。回帰テスト範囲広 |
| Editor: `[SerializeReference] List<AdapterBindingBase>` 編集 UI（Add ドロップダウン + Remove + Reorder + Missing placeholder + Slug 自動生成 + 重複検証） | **L** (1〜2 週) | **High** | Unity の `[SerializeReference]` + UI Toolkit の polymorphic list は project 内未経験。null entry / type missing / save block API の研究必要 |
| Editor: `AdapterBindingDiscovery`（`TypeCache.GetTypesWithAttribute` + 重複 displayName 警告） | **S** (1〜2 日) | Low | Editor 専用 static helper |
| osc: `OscReceiverAdapterBinding` + PropertyDrawer + `OscReceiver` を helper MonoBehaviour として再構成 | **M** (5〜7 日) | Medium | socket lifecycle の binding 内自動管理は新 pattern。実 UDP テスト維持必要 |
| inputsystem: `InputSystemAdapterBinding`（trigger + analog + gaze 全部統合） + PropertyDrawer + 既存 4 ファイル削除 | **L** (1〜2 週) | Medium | `FacialCharacterInputExtension.cs` 576 行のロジックを binding lifecycle へ再分割。Gaze / BonePose / AnalogBlendShape 3 系統の動作維持が必要 |
| ARKit binding（Req 6.3 解釈次第） | **M** (3〜5 日) | Medium | osc package 内に `ArKitOscAdapterBinding` を新設するか core or 新 package で実装するかは design phase で決定 |
| `InputSourceId.IsReservedId` / `RegisterExtension` 廃止 + slug-based id resolve（`<slug>` / `<slug>:<sub>`） | **M** (3〜5 日) | High | 既存全コード（`InputRegistration.cs`、`OscRegistration.cs`、`FacialController.BuildAdditionalInputSources`、テスト群）の依存箇所抽出 + 置換 |
| MultiSourceBlend の core 機能昇格（Req 5）— 実装側 | **S** (既存実装そのまま) | Low | 既に Domain / Application 完備。confirm のみ |
| core Sample（`Samples~/MultiSourceBlendBasicSample/` Mock binding） | **S** (1〜2 日) | Low | HUD なし最小コード。`package.json` `samples[]` 追加 |
| `MultiSourceBlendDemo`（inputsystem）HUD の slug 化 | **S** (1〜2 日) | Low | `_inputSourceIndex` → slug への置換 |
| Tests: EditMode（serialization round-trip / discovery / dropdown / slug 重複） + PlayMode（end-to-end / 0-alloc with 3+ binding） | **L** (1〜2 週) | Medium | TDD 厳守。新規 fake binding + 既存 perf test infra 流用 |
| Documentation~ + CHANGELOG + Migration ガイド | **S** (1〜2 日) | Low | 既存テンプレ流用 |

**全体規模: XL (4〜6 週、 主要 PR 4〜6 本)** / **全体 Risk: High**（VContainer 未経験 + `[SerializeReference]` 未経験 + 既存 osc / inputsystem の大規模書換）。

---

## 6. Recommendations for Design Phase

### Preferred Approach
- **Option B（二段階移行）** を選択。Phase 1（core 基盤 + Mock サンプル + VContainer 統合）→ Phase 2（osc / inputsystem migration）。
- Domain MultiSourceBlend は手をつけず、Adapters 層の glue を全面リファクタする方針。Option C の発想で `InputSourceFactory` を `InputSourceRegistry` 等にリネームしつつ slug-keyed lookup として再生し、`LayerInputSourceRegistry` / `LayerInputSourceAggregator` の既存 0-alloc / テスト資産を最大温存する。

### Key Decisions to Confirm in Design Phase

1. **VContainer のバージョン pin と取得経路** — `jp.hadashikick.vcontainer`（OpenUPM）か Git URL か。`IFixedTickable` 実装可否を確認。`manifest.json` への scoped registry 追加方針。
2. **`AdapterBuildContext` struct の正確なフィールドセット** — `IInputSourceRegistry`（slug factory）/ `ITimeProvider` / `BlendShapeNames` / `FacialProfile` / `FacialController` GameObject 参照（D-11 helper AddComponent 用）/ etc.
3. **`AdapterBindingHost` の 1 binding = 1 host か、N binding = 1 host か** — VContainer LifetimeScope 内に N 個の host を register するか、1 host に N binding を保持させるか。例外 isolation の粒度に影響。
4. **`MultiSourceBlend` core sample の最終名称** — `MultiSourceBlendBasicSample` / `MultiSourceBlendSelfCheck` / 他候補（D-14）。
5. **ARKit binding の所属 package とインターフェース** — Req 6.3 を osc package 内 / 別新 package / core 内のどこで実装するか（C-6）。現状 ARKit 入力経路は OSC の `ArKitOscAnalogSource` のみ存在。
6. **`InputSourceFactory` の再生方針 vs 完全廃止** — Option C の glue pattern を採るか、binding が直接 `LayerInputSourceRegistry` に slot 登録する純粋形にするか。
7. **`<slug>:<sub>` 複合 id の sub の付与責任** — binding 側が登録時に sub を確定させるか、layer 設定側で indexed sub を許容するか。
8. **Slug auto-population のタイミング** — Add 時のみか、save 時にも空ならキック補完するか。

### Research Needed (carry to design phase)

- **R-1**: Unity 6 の `[SerializeReference]` 仕様詳細 — 型 rename / namespace 移動時の挙動、null entry の表示、`SerializeReference.RefId` の安定性、PropertyDrawer 解決順序。
- **R-2**: `TypeCache.GetTypesWithAttribute<T>()` のキャッシュ無効化タイミング（Editor 起動 / asmdef rebuild / domain reload）と Editor load 時の確定タイミング。
- **R-3**: VContainer の `IStartable` / `ITickable` / `ILateTickable` / `IFixedTickable` のサポート範囲（version 別）と、per-`FacialController` LifetimeScope を高速に build / dispose する patterns。同時 10 体制御時の overhead。
- **R-4**: `AssetDatabase.SaveAssetIfDirty` のキャンセル可否（Req 12.3 「重複 slug で save block」）。代替案として OnValidate での dirty 状態保持。
- **R-5**: UI Toolkit `ListView` での `[SerializeReference]` polymorphic list 編集（Add ドロップダウン / Remove / Reorder / Missing placeholder）の参考実装。
- **R-6**: 既存 `OscReceiver` / `OscSender` を helper MonoBehaviour として binding 配下で `AddComponent` する場合、既存の手動配置 helper との共存方針（preview 利用者の実 scene）。

### Migration / Sequencing Notes

- Phase 1 の core PR で `AdapterBindings` フィールドを追加するが、Phase 2 着手前は空 list で運用 → `IFacialControllerExtension` 経路は変更なし、で並走する。Phase 1 PR では既存テスト群を一切壊さないことが目標。
- Phase 2 着手時に `IFacialControllerExtension` / `InputSourceFactory.RegisterReserved` / `InputSourceId.ReservedIds` / `FacialCharacterSO` / `Tests/EditMode/Editor/Inspector/FacialCharacterSOInspectorTests.cs` 等の旧 asset を一括削除。同 PR 内で osc + inputsystem の binding 移行 + Sample HUD 移行 + CHANGELOG breaking change 記載まで完結させる。
- `Assets/Samples/FacialControl InputSystem/0.1.0-preview.2/Multi Source Blend Demo/` と `Packages/com.hidano.facialcontrol.inputsystem/Samples~/MultiSourceBlendDemo/` の **二重管理ミラーは Phase 2 で同期更新が必要**（structure.md / CLAUDE.md の運用ルール）。
