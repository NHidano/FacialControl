# Implementation Plan

> **構成方針**: 本プランは design.md の `## Migration Strategy > Phase 動作契約マトリクス` に基づき **Phase 1（core 基盤 + Mock サンプル + VContainer 並走）** と **Phase 2（osc / inputsystem 移行 + 旧資産削除）** の 2 段階で構成する。Phase 1 の最初に R-A（VContainer smoke test）と R-B（[SerializeReference] round-trip test）を early-fail 検証として配置する（research.md `## Risks & Mitigations`）。
>
> **TDD 規約**: 各実装サブタスクは Red-Green-Refactor サイクルで進め、EditMode/PlayMode の対応テストを実装前に作成する（CLAUDE.md / steering）。
>
> **Design Decisions Drift 防止**: DD-1（slug を public field 化、UnityEngine 非参照維持）、DD-2（`_adapterBindings.Count > 0` empty-list ゲート + 旧経路同時検出時の `Debug.LogWarning`）、DD-3（0-alloc 3 シナリオ分解）の 3 判断は関連サブタスクの詳細で都度注記する。
>
> **`(P)` マーカー**: 異なる responsibility boundary 間で並走可能なタスクに `(P)` を付与する。`_Boundary:_` で対象境界を明示する。

---

## Phase 0: Foundation（Phase 1 / Phase 2 共通の足場と early-fail 検証）

- [x] 1. プロジェクト基盤と early-fail 検証スモークテストを整備する
- [x] 1.1 VContainer scoped registry を `manifest.json` に追加し asmdef references を更新する
  - `Packages/manifest.json` に OpenUPM scoped registry（`https://package.openupm.com`、scope `jp.hadashikick.vcontainer`）を追加し、`jp.hadashikick.vcontainer` を 1.17.x で pin する
  - `Hidano.FacialControl.Adapters.asmdef` と `Hidano.FacialControl.Editor.asmdef` の references に `VContainer` を追加する
  - `Hidano.FacialControl.Domain.asmdef` は変更しないこと（VContainer 非参照を維持、Req 4.8）
  - 観測可能完了条件: `Editor` をリロードして `VContainer.Unity.IStartable` 等が Adapters / Editor 層からだけ resolve でき、Domain 層からは未解決エラーが出る
  - _Requirements: 4.7, 4.8_

- [x] 1.2 R-A: VContainer Unity 6 互換 smoke test を EditMode に追加する
  - `[InitializeOnLoadMethod]` ではなく EditMode テスト経由で `LifetimeScope` の build → resolve → dispose を 1 サイクル実行するスモークテストを書く
  - VContainer 1.17.x が Unity 6000.3.2f1 で `Lifetime.Scoped` を含めて動作することを assert する
  - 観測可能完了条件: テストが green、もしくは fail した場合は本仕様の Phase 1 全体を停止して別 DI 検討（design.md `## Migration Strategy > Rollback Triggers`）に切替える判断材料となる
  - _Requirements: 4.7, 10.2_
  - _Boundary: Tests/EditMode/Adapters_

- [x] 1.3 R-B: `[SerializeReference]` round-trip smoke test を EditMode に追加する
  - 一時的な Mock `AdapterBindingBase` 派生型 2 種を `Tests/EditMode` 内に閉じて定義する
  - `ScriptableObject.CreateInstance<FacialCharacterProfileSO>` のテスト Stub に `_adapterBindings` を持たせ、`AssetDatabase.CreateAsset` → `AssetDatabase.LoadAssetAtPath` → 内容一致を assert する
  - null 要素（型欠落シミュレーション）に対し `SerializedProperty.managedReferenceFullTypename` の挙動を assert する
  - 観測可能完了条件: テストが green、もしくは fail した場合は Phase 1 を停止して `[SerializeReference]` 仕様を再調査する判断材料となる
  - _Requirements: 2.3, 2.7, 10.2_
  - _Boundary: Tests/EditMode/Adapters/ScriptableObject_

---

## Phase 1: Core 基盤（Domain / Adapters / Editor + Mock Sample）

- [x] 2. Domain 層の値オブジェクトと抽象基底を実装する
- [x] 2.1 (P) `AdapterSlug` 値オブジェクトの EditMode テストを書く（Red）
  - `TryParse` / `Parse` / `FromDisplayName` / `TryParseComposite` / equality / `ToString` の各観測仕様を網羅するテストを書く
  - `FromDisplayName("OSC") == "osc"`、`FromDisplayName("Input System") == "input-system"` などの kebab-case 変換を検証する
  - 正規表現 `^[a-zA-Z0-9_.-]{1,64}$` の境界（長さ 64 / 65、不正文字、ASCII 限定）を assert する
  - 既存 `InputSourceIdTests` から「`osc` / `lipsync` / `input` reserved id 禁止」相当のケースは持ち込まない（D-13 廃止）
  - 観測可能完了条件: テストが Red の状態でコミット可能であり、対応実装ファイルがまだ存在しない
  - _Requirements: 12.1, 12.2, 12.4, 12.6, 10.1, 10.2_
  - _Boundary: Domain.Models, Tests/EditMode/Domain_

- [x] 2.2 `AdapterSlug` 値オブジェクトを実装し Red を Green にする
  - `Hidano.FacialControl.Domain.Models.AdapterSlug` を `readonly struct` として実装する
  - kebab-case 変換ロジック（空白 / 記号 → `-`、連続 `-` 圧縮、ToLowerInvariant）を実装する
  - 複合 id `<slug>:<sub>` のパースを `TryParseComposite` に実装する
  - 観測可能完了条件: 2.1 のテストが全て green になる
  - _Requirements: 12.1, 12.2, 12.4, 12.6_

- [x] 2.3 (P) `AdapterBindingBase` 抽象基底と `FacialAdapterBindingAttribute` を Domain 層に実装する
  - `Hidano.FacialControl.Domain.Adapters.AdapterBindingBase` を `[Serializable]` の abstract class として実装する
  - **DD-1 注記**: `Slug` は `public string` field として宣言し、`[UnityEngine.SerializeField]` を使わず Unity の Script Serialization rule（public non-static field 自動 serialize）に乗せる。Domain 純度（UnityEngine 非参照）を維持しつつ Req 12.1 の serialized slug field を満たす
  - `OnStart(in AdapterBuildContext ctx)` / `OnTick` / `OnLateTick` / `OnFixedTick` / `Dispose` を default no-op virtual で定義する（VContainer interface は import しない）
  - `FacialAdapterBindingAttribute` を `AttributeTargets.Class` + `AllowMultiple = false` + `Inherited = false` で定義し `DisplayName` プロパティを持たせる
  - 観測可能完了条件: Domain asmdef ビルドが UnityEngine / VContainer / Unity.Collections 以外の依存を持たずに通る
  - _Requirements: 1.1, 1.2, 4.2, 4.4, 4.8, 4.10, 11.1, 11.5, 12.1, 13.1, 13.3_
  - _Boundary: Domain.Adapters_

- [x] 3. Adapters 層の DI / Lifecycle / Registry を実装する
- [x] 3.1 (P) `AdapterBuildContext` readonly struct を実装する
  - `Hidano.FacialControl.Adapters.DependencyInjection.AdapterBuildContext` を `readonly struct` として実装する
  - `Profile` / `BlendShapeNames` / `InputSourceRegistry` / `TimeProvider` / `HostGameObject` / `LipSyncProvider` を全て public readonly field として保持する
  - コンストラクタで `profile` / `inputSourceRegistry` / `timeProvider` / `hostGameObject` の null チェックを行う（`LipSyncProvider` のみ null 許容）
  - 観測可能完了条件: struct が `in` パラメータで関数に渡せ、boxing なしで field アクセスできる EditMode 単体テストが green になる
  - _Requirements: 4.10, 13.1_
  - _Boundary: Adapters.DependencyInjection_

- [x] 3.2 `AdapterBindingHost` の EditMode テストを書く（Red）
  - Mock binding が `OnStart` / `OnTick` / `OnLateTick` / `OnFixedTick` / `Dispose` で例外を投げた際に `_skipped = true` に遷移し以後の Tick 系が no-op になることを assert する
  - `Dispose` は `_skipped` の値に関わらず必ず呼ばれること、また `Dispose` 自体の例外も catch + LogError されることを assert する
  - 1 host = 1 binding の対応関係（複数 host を独立に Tick できる）を assert する
  - 観測可能完了条件: テストが Red、対応実装が未存在
  - _Requirements: 4.9, 9.1, 13.2, 13.4, 13.5, 10.2_
  - _Boundary: Adapters.DependencyInjection, Tests/EditMode/Adapters_

- [x] 3.3 `AdapterBindingHost` を実装し Red を Green にする
  - VContainer の `IStartable` / `ITickable` / `ILateTickable` / `IFixedTickable` / `System.IDisposable` を実装する sealed class として定義する
  - 各 lifecycle method 内で `if (_skipped) return;` の fast path → try / catch（`Debug.LogError` + `_skipped = true`）の構造を持たせる
  - **DD-3 注記**: `if (_skipped) return;` と通常 dispatch は 0-alloc fast path（正常系 steady-state / skip 確定後 steady-state の両方）。例外発生フレーム単発の `Debug.LogError($"...")` の string interpolation アロケは < 1 KB の許容範囲とし strict 0-alloc 対象外とする
  - メンバは `_binding` / `_buildContext` / `_skipped` の 3 フィールドのみに保つ
  - 観測可能完了条件: 3.2 のテストが全て green になる
  - _Requirements: 4.9, 9.1, 13.2, 13.4, 13.5_

- [x] 3.4 (P) `IInputSourceRegistry` / `InputSourceRegistry` の EditMode テストを書く（Red）
  - `Register(slug, source)` / `Register(slug, sub, source)` / `TryResolve("slug")` / `TryResolve("slug:sub")` の 4 系統を network なしで検証するテストを書く
  - 重複 register が「LogError + 後勝ち」になることを `LogAssert.Expect` で assert する
  - 未登録 id に対する `TryResolve` が false を返すこと、`RegisteredIds` の列挙が安定であることを assert する
  - 観測可能完了条件: テストが Red、対応実装が未存在
  - _Requirements: 5.4, 6.10, 12.4, 12.5, 10.2_
  - _Boundary: Adapters.InputSources, Tests/EditMode/Adapters_

- [x] 3.5 `IInputSourceRegistry` / `InputSourceRegistry` を実装し Red を Green にする
  - 旧 `InputSourceFactory` の (id, options) ディスパッチ + JSON deserialize + reserved id チェックは **本タスクでは実装しない**（責務縮小）
  - 内部は `Dictionary<string, IInputSource>` 1 個保持、`<slug>` / `<slug>:<sub>` 文字列をキーに格納する
  - **DD-1 注記**: API の slug 引数は `AdapterSlug` 値オブジェクトを受け、内部で `slug.Value` を string キーに変換する（Domain 純度に整合）
  - 観測可能完了条件: 3.4 のテストが全て green になる
  - _Requirements: 5.4, 6.10, 12.4, 12.5_

- [x] 3.6 `FacialControlAppLifetimeScope` / `FacialControllerLifetimeScope` を実装する
  - `FacialControlAppLifetimeScope` を `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` で auto-spawn する singleton MonoBehaviour として実装し、`DontDestroyOnLoad` を付与する
  - `Configure(IContainerBuilder)` で `ITimeProvider` / `ILipSyncProvider`（オプショナル）を register する
  - `FacialControllerLifetimeScope` を plain C# class として実装し、`appScope.CreateChild` で per-FC 子 scope を動的生成する API を提供する
  - 子 scope では `InputSourceRegistry`、`AdapterBuildContext`、`AdapterBindingHost`（List<AdapterBindingBase> ぶん） を `Lifetime.Scoped` で register する
  - **#if UNITY_EDITOR** で Edit Mode 中の auto-spawn を抑止する
  - 観測可能完了条件: PlayMode で auto-spawn 後 `FacialControlAppLifetimeScope.Instance` が non-null、子 scope を build → dispose した後に Container がリークしない
  - _Requirements: 4.7, 9.1, 9.4_
  - _Depends: 3.3, 3.5_

- [x] 4. `FacialCharacterProfileSO` に AdapterBindings field を追加する
- [x] 4.1 SO 修正の round-trip テストを EditMode に書く（Red）
  - Mock `AdapterBindingBase` 派生型 2 種を `_adapterBindings` に複数追加して `AssetDatabase.CreateAsset` → `AssetDatabase.LoadAssetAtPath` → 内容（slug / 各 field）一致を assert する
  - 同型 binding を 2 個登録できる（Req 2.4）こと、空 list を許容する（Req 2.2）こと、null 要素が後段の load を破壊しない（Req 2.7）ことを assert する
  - 観測可能完了条件: テストが Red、SO の field 追加がまだ未実装
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.7, 10.2_
  - _Boundary: Adapters.ScriptableObject, Tests/EditMode/Adapters/ScriptableObject_

- [x] 4.2 `FacialCharacterProfileSO` を修正して field を追加し Red を Green にする
  - `_adapterBindings` を `[SerializeReference] protected List<AdapterBindingBase>` として追加する（初期値は空 list）
  - 既存 `_layers` / `_expressions` / `_rendererPaths` / `_schemaVersion` フィールドは無変更で維持する
  - 既存 `abstract class` の `abstract` を解除し、`[CreateAssetMenu(fileName = "NewFacialCharacterProfile", menuName = "FacialControl/Facial Character Profile")]` を新規付与する
  - public 読み取り API `IReadOnlyList<AdapterBindingBase> AdapterBindings => _adapterBindings;` を提供する
  - 観測可能完了条件: 4.1 のテストが全て green、かつ既存 SO 関連 EditMode テスト群が全て green を維持する
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.7, 6.6, 7.2_

- [x] 5. Editor 層の Discovery / ListView / SaveGuard を実装する
- [x] 5.1 `AdapterBindingDiscovery` の EditMode テストを書く（Red）
  - `[FacialAdapterBinding]` 付き Mock 型 2 種（同名 displayName 1 ペア + 単独 1 種）を含めた discovery で `displayName` 順 sort された結果が返ることを assert する
  - 同 displayName が 2 つ以上ある場合に `Debug.LogWarning`（FQTN 列挙）が出ること、suffix `(FullTypeName)` が付与されることを `LogAssert.Expect` で assert する
  - 観測可能完了条件: テストが Red、対応実装が未存在
  - _Requirements: 1.3, 1.4, 1.7, 10.2, 10.4_
  - _Boundary: Editor.Inspector.AdapterBindings, Tests/EditMode/Editor_

- [x] 5.2 `AdapterBindingDiscovery` を実装し Red を Green にする
  - `[InitializeOnLoad]` static class として実装し、`TypeCache.GetTypesWithAttribute<FacialAdapterBindingAttribute>()` の結果を `OrdinalIgnoreCase` で sort + 重複検出 + suffix 付与してキャッシュする
  - `IReadOnlyList<AdapterBindingDescriptor> GetDescriptors()` と `FindByType` を提供する
  - `OnDescriptorsRebuilt` イベントを公開し、domain reload 時の再構築を通知できるようにする
  - 観測可能完了条件: 5.1 のテストが全て green になる
  - _Requirements: 1.3, 1.4, 1.7_

- [x] 5.3 `AdapterBindingsListView` の EditMode テストを書く（Red）
  - Add ドロップダウン → `Activator.CreateInstance` → list append + slug auto-populate（`AdapterSlug.FromDisplayName`）の流れを assert する（Req 2.5, 12.2）
  - Remove ボタン押下で list から要素削除 + dirty 化されることを assert する（Req 2.6）
  - Reorder（ListView.reorderable）で要素順が変わることを assert する
  - null 要素（型欠落 simulation：`SerializedProperty.managedReferenceFullTypename` が空文字列の状態を再現）で `MissingAdapterPlaceholderElement` が描画されることを assert する（Req 2.7）
  - 同 SO 内 slug 重複時に当該 row に error class が付与され summary banner が出ることを assert する（Req 12.3）
  - PropertyDrawer 例外時に per-element fallback element が表示され他 row の描画が止まらないことを assert する（Req 3.6）
  - 観測可能完了条件: テストが Red、対応実装が未存在
  - _Requirements: 1.4, 2.4, 2.5, 2.6, 2.7, 3.3, 3.5, 3.6, 7.1, 12.2, 12.3, 10.2, 10.4_
  - _Boundary: Editor.Inspector.AdapterBindings, Tests/EditMode/Editor_

- [x] 5.4 `AdapterBindingsListView` / `AdapterBindingAddDropdown` / `MissingAdapterPlaceholderElement` を実装し Red を Green にする
  - UI Toolkit の `ListView` + `bindingPath = "_adapterBindings"` で SerializedProperty にバインドする
  - `makeItem` / `bindItem` / Add / Remove / Reorder の各 callback を実装する
  - Add ボタンは `AdvancedDropdown` 派生 `AdapterBindingAddDropdown` を開き、選択された `AdapterBindingDescriptor` から具象を生成して append する
  - bindItem では `propAtIndex.managedReferenceValue == null && propAtIndex.managedReferenceFullTypename != ""` で型欠落 row を判定し、`MissingAdapterPlaceholderElement` を表示する（remove ボタン付き）
  - 通常 row では `PropertyField` を `el.Add` し、try / catch で PropertyDrawer 例外を捕捉して fallback element を表示する
  - slug 重複検出は list rebind 時に全 row 走査し `class: facial-control-error` を付与、Inspector 上端に summary banner を出す
  - **新規 IMGUI panel は導入しないこと（Req 3.5, 11.4）**
  - 観測可能完了条件: 5.3 のテストが全て green、Inspector 上で 3 種以上の Mock binding を Add / Reorder / Remove できる
  - _Requirements: 1.4, 2.4, 2.5, 2.6, 2.7, 3.1, 3.3, 3.4, 3.5, 3.6, 7.1, 7.3, 11.4, 12.2, 12.3_

- [x] 5.5 (P) `FacialCharacterProfileAssetGuard` を実装する
  - `UnityEditor.AssetModificationProcessor` を継承し `OnWillSaveAssets(string[] paths)` を実装する
  - 各 path について `AssetDatabase.GetMainAssetTypeAtPath` で `FacialCharacterProfileSO` 以外を即 skip、一致時のみ slug 重複を再検証する
  - 重複時は当該 path を return 配列から除外し、`Debug.LogError("[FacialControl] Save blocked: duplicate slug '{slug}' in {assetPath}")` を出力 + `Selection.activeObject = so` で当該 SO に focus する
  - **R-F 注記**: `EditorUtility.DisplayDialog` で「重複 slug が原因」を併せて表示し UX 損失を緩和する（research.md `## Risks & Mitigations` R-F）
  - 観測可能完了条件: EditMode テストで重複 slug を持つ SO の save がブロックされ、修復後は save が通る
  - _Requirements: 12.3_
  - _Boundary: Editor.Inspector.AdapterBindings_

- [x] 5.6 `FacialCharacterProfileSOInspector` に AdapterBindings セクションを追加する
  - 既存 `FacialCharacterProfileSOInspector.CreateInspectorGUI` の build pipeline に `AdapterBindingsListView` を挿入する
  - 既存セクション（Layers / Expressions / Renderer Paths）との表示順を design.md `## Components and Interfaces > AdapterBindingsListView` の Implementation Notes に従って配置する
  - 観測可能完了条件: 1 個の `FacialCharacterProfileSO` Inspector で AdapterBindings + 既存セクション全てが UI Toolkit で表示・編集できる
  - _Requirements: 7.1, 7.2, 7.3_
  - _Depends: 5.4_

- [x] 6. `FacialController` を Phase 1 並走仕様で改修する
- [x] 6.1 `FacialController` Phase 1 並走仕様の PlayMode テストを書く（Red）
  - **DD-2 注記**: `_adapterBindings.Count == 0` の SO で既存 `IFacialControllerExtension` 経路のみ駆動し既存 PlayMode テスト群が全 green を維持することを assert する
  - `_adapterBindings.Count > 0` の SO で child scope build が走り、Mock binding の `OnStart` / `OnLateTick` / `Dispose` が VContainer 経由で呼ばれることを assert する
  - 旧 Extension コンポーネント（`GetComponents<IFacialControllerExtension>()` 非空）と `_adapterBindings.Count > 0` を同時検出した場合に `Debug.LogWarning` が出ることを assert する
  - 観測可能完了条件: テストが Red、`FacialController` の改修が未実施
  - _Requirements: 4.7, 6.8, 6.9, 9.1, 13.5, 10.3, 10.5_
  - _Boundary: Adapters.Playable, Tests/PlayMode/Adapters_

- [x] 6.2 `FacialController.Initialize` / `Cleanup` に VContainer child scope build を追加し Red を Green にする
  - `Initialize` 冒頭で app scope 取得 → `_adapterBindings.Count > 0` の場合のみ `FacialControllerLifetimeScope.Build` で child scope 生成 → 既存 `LayerUseCase` 構築 → `_isInitialized = true` の流れにする
  - `LateUpdate` は既存 Aggregator 駆動と BlendShape 適用を継続し、binding の `OnLateTick` は VContainer の `ILateTickable` 経由で呼ばれるため触らない
  - `Cleanup` で child scope を build していた場合のみ `Dispose()` を最初に呼び host 群の Dispose を完了させてから既存 cleanup を行う
  - **DD-2 衝突防御**: `Initialize` 冒頭で旧 Extension コンポーネント検出 + 新 binding 検出の両方が真なら `Debug.LogWarning` を出力する
  - 既存 `ApplyExtensions` / `BuildAdditionalInputSources` は **Phase 1 では削除しない**（Phase 2 で削除）
  - 観測可能完了条件: 6.1 のテストが全て green、既存 PlayMode テスト群が全 green を維持する
  - _Requirements: 4.7, 6.8, 6.9, 9.1, 13.5_

- [x] 7. Phase 1 PlayMode 統合テストと Performance テストを追加する
- [x] 7.1 (P) `AdapterBindingHostLifecycleTests` を実装する
  - VContainer LifetimeScope を build し Mock binding 3 個を register、1 frame で `IStartable` / `ITickable` / `ILateTickable` / `IFixedTickable` が順に呼ばれることを assert する
  - 例外を投げた binding が以後の Tick で skip され、他 binding と core パイプラインは継続することを assert する
  - 観測可能完了条件: PlayMode テストが green、host が VContainer の PlayerLoop に正しく挿入されている
  - _Requirements: 4.9, 13.2, 13.4, 13.5, 10.3_
  - _Boundary: Tests/PlayMode/Adapters_

- [x] 7.2 (P) `AdapterBindingHostAllocationTests` を 3 シナリオ分解で実装する
  - **DD-3 注記** に従い 3 シナリオに分解する：
    - (a) 正常系 steady-state: 3 binding × 10 体で例外なし、60 フレーム実行して `GC.GetTotalMemory` delta が 0 byte
    - (b) skip 確定後 steady-state: 1 binding が `OnTick` で例外を投げて `_skipped = true` 確定後、後続 60 フレームの delta が 0 byte
    - (c) 例外発生フレーム単発: 例外を投げたフレーム単発の delta が < 1 KB
  - 既存 `Tests/PlayMode/Performance/MultiCharacterPerformanceTests` のパターンを踏襲する
  - 観測可能完了条件: 3 シナリオが全て pass、CI が GC alloc を検出した場合は test fail として通知される
  - _Requirements: 9.1, 9.5, 10.3, 10.7_
  - _Boundary: Tests/PlayMode/Performance_

- [x] 7.3 (P) `MultiSourceBlendThreeBindingsTests` を実装する
  - Mock trigger + Mock analog + 既存 `LipSyncInputSource`（または Fake）の 3 binding 構成で MultiSourceBlend が `output[k] = clamp01(Σ wᵢ · values_i[k])` の期待値を出すことを assert する
  - Domain 層 `LayerInputSourceAggregator` が無変更のまま slug-based registry 経由で機能していることを確認する
  - 観測可能完了条件: PlayMode テストが green、3 binding 入力からの加重和が期待値と一致する
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 10.3_
  - _Boundary: Tests/PlayMode/Domain_

- [x] 7.4 R-C: per-FC LifetimeScope の build / dispose 線形性を Profiler markers で検証する
  - `(adapterBindingCount + sourcesPerChar) × charCount` の線形スケールを Profiler markers で測定するテストを追加する
  - 1 体あたり child scope build 時間 < 1 ms（design.md `## Performance & Scalability > 目標値` テーブル）を assert する
  - 観測可能完了条件: 10 体・3 binding 構成で線形スケールが確認できる
  - _Requirements: 9.4, 10.3, 10.7_
  - _Boundary: Tests/PlayMode/Performance_

- [x] 8. core 同梱サンプル `MultiSourceBlendBasicSample` を整備する
- [x] 8.1 Mock binding と Runner を実装する
  - `Samples~/MultiSourceBlendBasicSample/MockTriggerAdapterBinding.cs`（`[FacialAdapterBinding(displayName: "Mock Trigger")]`） を実装する
  - `Samples~/MultiSourceBlendBasicSample/MockAnalogAdapterBinding.cs`（`[FacialAdapterBinding(displayName: "Mock Analog")]`） を実装する
  - `MultiSourceBlendBasicRunner.cs` で MultiSourceBlend ロジックを呼ぶ最小コード（HUD なし、Scene なし）を実装し、`Tools > FacialControl > Run MultiSourceBlend Basic Sample` メニューから 1 click 実行可能にする
  - `multi_source_blend_basic.json` でサンプル用プロファイル（layer + inputSources 構成例、slug 形式）を提供する
  - `README.md` で Sample 使用手順を記述する
  - 観測可能完了条件: Package Manager から Sample を Import 後、メニュー実行で Mock binding 2 種が discovery され MultiSourceBlend の出力がコンソールに表示される
  - _Requirements: 5.6_
  - _Boundary: Samples~/MultiSourceBlendBasicSample_

- [x] 8.2 `package.json` の `samples[]` に Sample を登録する
  - `Packages/com.hidano.facialcontrol/package.json` の `samples` 配列に `MultiSourceBlendBasicSample` 1 件を追加する
  - 名称は `Demo` を含めず `MultiSourceBlendBasicSample` とする（D-14）
  - 観測可能完了条件: Package Manager の Samples タブから当該 sample が Import 可能
  - _Requirements: 5.6_
  - _Depends: 8.1_

---

## Phase 2: Adapter 移行と旧資産削除

- [x] 9. OSC adapter package を新アーキテクチャに移行する
- [x] 9.1 `OscReceiverAdapterBinding` の EditMode / PlayMode テストを書く（Red）
  - EditMode: `[Serializable]` + `[FacialAdapterBinding(displayName: "OSC")]` 付きであり TypeCache discovery で列挙されることを assert する
  - PlayMode 統合: 実 UDP loopback で `OnStart` 内 `AddComponent<OscReceiverHost>` + `Configure` が走り、`InputSourceRegistry.Register(slug, source)` で primary IInputSource が解決可能になることを assert する
  - PlayMode 統合: `Dispose` 時に `Object.Destroy(_helperHost)` で helper MonoBehaviour が破棄され、socket がクローズされることを assert する
  - HelperHost の `HideFlags` が `HideInInspector` を含まず Inspector で見えることを assert する（Req 13.6）
  - 観測可能完了条件: テストが Red、`OscReceiverAdapterBinding` 未実装
  - _Requirements: 6.2, 6.5, 6.9, 7.4, 7.5, 13.6, 13.7, 10.3, 10.5_
  - _Boundary: com.hidano.facialcontrol.osc, Tests/PlayMode/Integration_

- [x] 9.2 `OscReceiverHost` / `OscSender` helper MonoBehaviour と `OscReceiverAdapterBinding` を実装する
  - `OscReceiverHost` を public `Configure(endpoint, port, buffer)` 付きの helper MonoBehaviour として実装し、既存 `OscReceiver` のロジックを呼び出すラッパーにする（または内部リファクタする）
  - `OscReceiverAdapterBinding` を `[Serializable]` + `[FacialAdapterBinding(displayName: "OSC")]` で実装し、`_endpoint` / `_port` / `_blendShapeMappings` を inline `[SerializeField]` で保持する
  - `OnStart(in ctx)` で helper を `AddComponent`、`Configure` 呼び出し、`OscInputSource` 構築、`ctx.InputSourceRegistry.Register(AdapterSlug.Parse(Slug), _inputSource)` を実行する
  - `OnFixedTick` で受信 buffer swap 等を実行する（既存 `OscReceiver` の Update 経路に依存しないよう自前 tick 化）
  - `Dispose()` で `Object.Destroy(_helperHost)` → `_inputSource.Dispose()` の順で解放する
  - 観測可能完了条件: 9.1 のテストが全て green になる
  - _Requirements: 6.2, 6.9, 13.6, 13.7_

- [x] 9.3 (P) `ArKitOscAdapterBinding` を実装する
  - `[FacialAdapterBinding(displayName: "ARKit / PerfectSync")]` 付き具象を実装する
  - 既存 `ArKitOscAnalogSource` を helper として再構成し、binding が `OnStart` で OSC receiver に subscribe する
  - ARKit 自動検出 (`ARKitDetector`) は Editor only のため binding には含めない
  - 観測可能完了条件: 単一 SO で `OscReceiverAdapterBinding` + `ArKitOscAdapterBinding` が同時に保持・round-trip できる EditMode テストが green
  - _Requirements: 6.3, 6.6_
  - _Boundary: com.hidano.facialcontrol.osc/Adapters/ARKit_

- [x] 9.4 (P) `OscReceiverAdapterBindingDrawer` / `ArKitOscAdapterBindingDrawer` を提供する
  - 各 binding の inline UI（endpoint / port / blendshape マッピング 等）を UI Toolkit で実装する
  - `[CustomPropertyDrawer(typeof(<ConcreteAdapterBinding>))]` を付与し、core の `PropertyField` から自動解決される配置にする
  - **core では `[CustomPropertyDrawer]` を登録しないこと（Req 3.1）**
  - 観測可能完了条件: Inspector 上で 2 種類の OSC binding が独自 UI で表示・編集できる
  - _Requirements: 3.1, 3.2, 3.3, 6.5, 7.4, 7.5, 11.4_
  - _Boundary: com.hidano.facialcontrol.osc/Editor/AdapterBindings_

- [x] 9.5 OSC package の旧資産を削除する
  - `OscFacialControllerExtension` を削除する（Req 6.9）
  - `Registration/OscRegistration.cs` を削除し、`InputSourceRegistry.RegisterReserved` 経路への登録を解除する
  - `_adapterBindings` 経路に置換された旧 `IFacialControllerExtension` 関連 PlayMode 統合テストを削除または書換する（gap-analysis C-9 影響範囲を参考）
  - 観測可能完了条件: `Grep` で `OscFacialControllerExtension` / `OscRegistration` が repository 内 0 件
  - _Requirements: 6.9, 6.10_

- [x] 10. InputSystem adapter package を新アーキテクチャに移行する
- [x] 10.1 `InputSystemAdapterBinding` の EditMode / PlayMode テストを書く（Red）
  - EditMode: `[Serializable]` + `[FacialAdapterBinding(displayName: "Input System")]` 付きであり TypeCache discovery で列挙されることを assert する
  - PlayMode 統合: InputAction 仮想 device → ExpressionTrigger / Analog / Gaze の 3 経路（D-8 集約）が動作することを assert する
  - PlayMode 統合: `Dispose` で ActionMap.Disable + Asset destroy + provider dispose が走ることを assert する
  - 観測可能完了条件: テストが Red、`InputSystemAdapterBinding` 未実装
  - _Requirements: 6.1, 6.5, 6.8, 7.4, 7.5, 10.3, 10.5_
  - _Boundary: com.hidano.facialcontrol.inputsystem, Tests/PlayMode/Integration_

- [x] 10.2 `InputSystemAdapterBinding` を実装し旧 `FacialCharacterInputExtension` ロジックを移植する
  - `[Serializable]` + `[FacialAdapterBinding(displayName: "Input System")]` で実装する
  - 旧 `FacialCharacterSO` のフィールド（`_inputActionAsset` / `_actionMapName` / `_expressionBindings` / `_gazeConfigs` 等）を inline `[SerializeField]` で移植する
  - `OnStart(in ctx)` で `InputActionAsset.Instantiate` + `ActionMap.Enable` + `ExpressionInputSourceAdapter` 構築 + `InputSourceRegistry.Register` + 旧 `BindAllExpressions` 相当を実行する
  - `OnLateTick(deltaTime)` で analog Tick + BonePose の BuildAndPush（旧 `LateUpdate` 内ロジック）を実行する
  - `Dispose()` で ActionMap.Disable + Asset destroy + provider dispose を順次行う
  - **旧 `FacialCharacterInputExtension` (576 行) のロジックを `OnStart` / `OnLateTick` / `Dispose` に責務分割する**
  - `_inputActionAsset` 未設定時は `OnStart` で warn + 早期 return する
  - 観測可能完了条件: 10.1 のテストが全て green になる
  - _Requirements: 6.1, 6.8_

- [x] 10.3 (P) `InputSystemAdapterBindingDrawer` を提供する
  - 既存 `FacialCharacterSOInspector` の UI 構成を `InputSystemAdapterBindingDrawer` に移植する
  - `[CustomPropertyDrawer(typeof(InputSystemAdapterBinding))]` を付与し core の `PropertyField` から自動解決される配置にする
  - UI Toolkit ベースで実装し新規 IMGUI panel は導入しない（Req 11.4）
  - 観測可能完了条件: Inspector 上で `InputSystemAdapterBinding` が旧 `FacialCharacterSOInspector` 相当の UX で編集可能
  - _Requirements: 3.1, 3.2, 3.3, 6.5, 7.4, 7.5, 11.4_
  - _Boundary: com.hidano.facialcontrol.inputsystem/Editor/AdapterBindings_

- [x] 10.4 InputSystem package の旧資産を削除する
  - `FacialCharacterSO` (派生 SO) を削除する（Req 6.4）
  - `FacialCharacterInputExtension` を削除する（Req 6.8）
  - `InputFacialControllerExtension` を削除する（Req 6.8）
  - `Registration/InputRegistration.cs` を削除する（Req 6.10）
  - `Editor/Inspector/FacialCharacterSOInspector.cs` を削除する
  - `Editor/AutoExport/FacialCharacterSOAutoExporter.cs` を削除する
  - `_adapterBindings` 経路に置換された旧 `IFacialControllerExtension` 関連 PlayMode 統合テストを削除または書換する
  - 観測可能完了条件: `Grep` で `FacialCharacterSO` / `FacialCharacterInputExtension` / `InputFacialControllerExtension` / `InputRegistration` / `FacialCharacterSOInspector` が repository 内 0 件
  - _Requirements: 6.4, 6.8, 6.10_

- [x] 10.5 inputsystem `Samples~/MultiSourceBlendDemo/` HUD を slug 駆動に書き換える
  - 既存 HUD の `_inputSourceIndex` / `_controllerSourceIndex` 駆動を新 slug ベース（`<slug>` または `<slug>:<sub>`）に書換する
  - 二重管理ミラーである `Assets/Samples/com.hidano.facialcontrol/.../MultiSourceBlendDemoHUD.cs` も同期更新する（CLAUDE.md `## Samples の二重管理ルール`）
  - 観測可能完了条件: dev project / UPM import 双方の HUD で slug 駆動の MultiSourceBlend Demo が動作する
  - _Requirements: 6.7, 12.7_
  - _Depends: 10.2_

- [x] 11. Core 側の旧資産を Phase 2 で削除する
- [x] 11.1 `IFacialControllerExtension` 関連を core から削除する
  - `IFacialControllerExtension` interface を削除する（Req 6.8）
  - `FacialController.ApplyExtensions` / `BuildAdditionalInputSources` を削除する
  - `FacialController.Initialize` の `_adapterBindings.Count > 0` empty-list ゲートを除去し、無条件で child scope を build する仕様に変更する（DD-2 並走期終了）
  - 旧 Extension + 新 binding 同時検出時の `Debug.LogWarning` を削除する
  - 観測可能完了条件: `Grep` で `IFacialControllerExtension` / `ApplyExtensions` / `BuildAdditionalInputSources` が repository 内 0 件、新 binding 経路のみで全 PlayMode テストが green
  - _Requirements: 6.8, 6.9_
  - _Depends: 9.5, 10.4_

- [x] 11.2 `InputSourceFactory` / `InputSourceId.ReservedIds` 旧資産を削除する
  - `InputSourceFactory` を削除し `InputSourceRegistry` のみに一本化する（Req 6.10）
  - `InputSourceId.ReservedIds` / `IsReserved` / `IsReservedId` を削除する（Req 12.5、D-13）
  - 既存テストが `InputSourceId` に依存している箇所を `AdapterSlug` ベースに書換える（gap-analysis C-9 影響範囲）
  - 観測可能完了条件: `Grep` で `InputSourceFactory` / `ReservedIds` / `IsReserved` / `IsReservedId` が repository 内 0 件
  - _Requirements: 6.10, 12.5, 12.6_

- [x] 12. Phase 2 検証 Checkpoints と Documentation を整備する
- [x] 12.1 (P) `OscReceiverAdapterBindingIntegrationTests` を Phase 2 完了後の構成で実行する
  - 実 UDP loopback + `OscReceiverHost` AddComponent + binding `Dispose` 時の helper destroy が完了することを assert する
  - 旧 `OscFacialControllerExtension` 経由のテストが repository に残っていないことを確認する
  - 観測可能完了条件: PlayMode テストが green、旧経路依存が 0 件
  - _Requirements: 6.9, 13.6, 13.7, 10.3, 10.5_
  - _Boundary: Tests/PlayMode/Integration_

- [x] 12.2 (P) `InputSystemAdapterBindingIntegrationTests` を Phase 2 完了後の構成で実行する
  - InputAction 仮想 device → Trigger / Analog / Gaze の 3 経路が動作することを assert する
  - 旧 `FacialCharacterInputExtension` / `InputFacialControllerExtension` 経由のテストが repository に残っていないことを確認する
  - 観測可能完了条件: PlayMode テストが green、旧経路依存が 0 件
  - _Requirements: 6.1, 6.8, 10.3, 10.5_
  - _Boundary: Tests/PlayMode/Integration_

- [x] 12.3 (P) 単一 SO に Input + OSC + ARKit 同時保持の round-trip 統合テストを追加する
  - 1 個の `FacialCharacterProfileSO` に `InputSystemAdapterBinding` + `OscReceiverAdapterBinding` + `ArKitOscAdapterBinding` を同時に保持する EditMode テストを書く
  - `AssetDatabase.CreateAsset` → `LoadAssetAtPath` の round-trip で 3 種の concrete type identity が維持されることを assert する（Req 6.6）
  - 観測可能完了条件: テストが green、3 種 binding が `[SerializeReference]` 経由で全て round-trip 可能
  - _Requirements: 2.3, 6.6_
  - _Boundary: Tests/EditMode/Adapters/ScriptableObject_

- [x] 12.4 0-alloc perf test を 3 binding × 10 体構成で再実行する
  - 7.2 の 3 シナリオ (a) / (b) / (c) を Phase 2 完了後の実 binding 構成（OSC + InputSystem + ARKit + Mock）で再実行する
  - 同時 10 体以上の制御で 0-alloc 目標を維持していることを assert する
  - 観測可能完了条件: 3 シナリオ全てが Phase 2 完了後も pass
  - _Requirements: 9.1, 9.5, 10.3, 10.7_
  - _Depends: 7.2, 9.2, 10.2_
  - _Boundary: Tests/PlayMode/Performance_

- [x] 12.5 CHANGELOG と migration-guide を整備する
  - `Packages/com.hidano.facialcontrol/CHANGELOG.md` に破壊的変更エントリ（旧 `FacialCharacterSO` 削除、`IFacialControllerExtension` 削除、reserved id 廃止、Adapter Binding アーキテクチャ移行）を追加する
  - `Packages/com.hidano.facialcontrol/Documentation~/migration-guide.md` を新規作成し、旧モデルからの移行手順（旧 SO の再作成、`layer.inputSources[].id` を slug 形式に書換 等）を記述する
  - Phase 1 並走期に存在した「両経路同時利用は非推奨」記述は Phase 2 で「並走期終了」と更新する
  - 削除型一覧（`FacialCharacterSO` / `FacialCharacterInputExtension` / `InputFacialControllerExtension` / `OscFacialControllerExtension` / `IFacialControllerExtension` / `InputSourceFactory` / `InputSourceId.ReservedIds` 等）を明示する
  - 観測可能完了条件: CHANGELOG に breaking change エントリが残り、migration-guide.md が現状を反映する
  - _Requirements: 6.7, 8.1, 8.2, 8.3, 8.4, 8.5, 12.7_

- [x] 12.6 Phase 2 完了 grep 検証と最終 sanity check
  - `Grep` で旧資産（`FacialCharacterSO` / `IFacialControllerExtension` / `OscFacialControllerExtension` / `FacialCharacterInputExtension` / `InputFacialControllerExtension` / `InputSourceFactory` / `ReservedIds` / `IsReserved` / `IsReservedId`）が repository 内 0 件であることを最終確認する
  - 既存 PlayMode テスト全 green、新 PropertyDrawer を持つ各 binding が Inspector で正常表示、0-alloc perf test が pass を最終確認する
  - 観測可能完了条件: design.md `## Migration Strategy > Phase 2 検証 Checkpoints` を全て満たす
  - _Requirements: 6.4, 6.7, 6.8, 6.9, 6.10, 8.1, 9.1, 12.5_
  - _Depends: 11.1, 11.2, 12.1, 12.2, 12.3, 12.4, 12.5_
