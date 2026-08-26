# Research & Design Decisions: adapter-binding-architecture

## Summary
- **Feature**: `adapter-binding-architecture`
- **Discovery Scope**: Complex Integration（既存 core + 2 アダプタパッケージの破壊的再構成 + VContainer 新規導入 + `[SerializeReference]` 初導入）
- **Key Findings**:
  - **MultiSourceBlend の Domain 層 0-alloc 実装は完全に既存資産** (`LayerInputSourceAggregator` / `LayerInputSourceWeightBuffer` / `LayerInputSourceRegistry`) として完成しており、Req 5（core 機能昇格）は概念的にはほぼ達成済み。設計の中心は Adapters 層の glue（旧 `IFacialControllerExtension` 経路）の全面置換になる。
  - **VContainer は manifest.json / asmdef のいずれにも未参照**で初導入。`IStartable` / `ITickable` / `ILateTickable` / `IFixedTickable` / `IDisposable` はすべて 1.17 系で公式提供されており、PlayerLoop へ自動注入される。例外発生時はデフォルトで `Debug.LogException` ログのみ。`RegisterEntryPointExceptionHandler` で上書き可能（D-15 の skip 動作はここに乗せず、`AdapterBindingHost` 内 try/catch で実装するのが安全）。
  - **`[SerializeReference]` + `TypeCache.GetTypesWithAttribute` も project 内未使用**。Unity 6 では型欠落時に list 要素は `null` 化され、`SerializationUtility.GetManagedReferencesWithMissingTypes` で missing 型情報を取り出せる。`MovedFromAttribute` で型 rename 互換が取れる（preview 段階のため必須ではないが、型移動時は付与する慣例）。

## Research Log

### Topic 1: VContainer の lifecycle interface とサポート範囲
- **Context**: Req 4.7-4.10 / Req 13 / D-5 / D-9 / D-10 / D-12 が VContainer 採用を前提とし、`IStartable` / `ITickable` / `ILateTickable` / `IFixedTickable` / `IDisposable` を `AdapterBindingHost` で実装する設計。version pinning と例外モデルの確定が必要。
- **Sources Consulted**:
  - [Plain C# Entry point | VContainer](https://vcontainer.hadashikick.jp/integrations/entrypoint)
  - [VContainer GitHub](https://github.com/hadashiA/VContainer)
  - [VContainer Lifetime Overview](https://vcontainer.hadashikick.jp/scoping/lifetime-overview)
  - [Generate child scope with code first](https://vcontainer.hadashikick.jp/scoping/generate-child-with-code-first)
  - [OpenUPM: jp.hadashikick.vcontainer](https://openupm.com/packages/jp.hadashikick.vcontainer/)
- **Findings**:
  - VContainer の Plain C# Entry Point として `IInitializable` / `IPostInitializable` / `IStartable` / `IPostStartable` / `IFixedTickable` / `IPostFixedTickable` / `ITickable` / `IPostTickable` / `ILateTickable` / `IPostLateTickable` / `IDisposable` が公式提供。`builder.RegisterEntryPoint<T>()` で登録すると Unity の PlayerLoop に自動挿入される。
  - 例外時の既定動作は `Debug.LogException` のみ。`RegisterEntryPointExceptionHandler` で差替えできるが、Handler 側で再 throw しない限り「次フレームも当該 entry point の Tick が呼ばれ続ける」。Req 13.4-13.5「skip して以後呼ばない」を実装するには `AdapterBindingHost` 内 try/catch + 自前の skip フラグが必要（VContainer の Handler に丸投げできない）。
  - LifetimeScope は `CreateChild()` / `CreateChildFromPrefab()` でツリー分割可能。Resolve 自体は 0-alloc だが、子 scope の build / dispose 自体に若干の overhead がある（公式のベンチマーク数値は未公開）。同時 10 体以上では「app-level scope を 1 個 + per-FacialController scope を N 個」の 2 階層が公式 pattern と整合する（D-12）。
  - 最新 stable は 1.17.x（OpenUPM, 2026-05 時点）。Unity 6 (6000.x) での既知の互換性問題は OpenUPM の issue tracker / GitHub に未報告。
- **Implications**:
  - `manifest.json` に OpenUPM scoped registry (`https://package.openupm.com`, scope `jp.hadashikick.vcontainer`) を追加し、`jp.hadashikick.vcontainer` を `1.17.x` で pin する。
  - core の Adapters layer asmdef (`Hidano.FacialControl.Adapters`) に `VContainer` の precompiled reference を追加。Domain asmdef は引き続き VContainer 非参照（Req 4.8）。
  - `AdapterBindingHost` 内で `try { binding.OnTick(...); } catch (Exception ex) { Debug.LogError(...); _skipped = true; }` 形式を取り、VContainer の Exception Handler は使わない（Req 13.4 / D-15 に対応）。
  - 1 binding = 1 host とし、host を `LifetimeScope` に複数 RegisterEntryPoint する。これにより 1 binding が skip されても他 host の Tick は VContainer 側で独立に呼ばれ続ける（Req 13.5 を最も確実に満たす）。

### Topic 2: Unity 6 の `[SerializeReference]` 仕様（R-1）
- **Context**: Req 2.1 / 2.3 / 2.7 / 6.6 が `[SerializeReference] List<AdapterBindingBase>` の polymorphic round-trip と「型欠落時の Missing Adapter placeholder」を要求。Unity 6 仕様の確認が前提。
- **Sources Consulted**:
  - [Unity Scripting API: SerializeReference (6000.2)](https://docs.unity3d.com/6000.2/Documentation/ScriptReference/SerializeReference.html)
  - [Unity Scripting API: ManagedReferenceMissingType (6000.0)](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ManagedReferenceMissingType.html)
  - [Unity Scripting API: SerializationUtility.GetManagedReferencesWithMissingTypes](https://docs.unity3d.com/ScriptReference/SerializationUtility.GetManagedReferencesWithMissingTypes.html)
  - [SerializeReference improvements in Unity 2021 LTS](https://discussions.unity.com/t/serializereference-improvements-in-unity-2021-lts/886491)
  - [Unity-Editor-PolymorphicReorderableList (community sample)](https://github.com/CristianQiu/Unity-Editor-PolymorphicReorderableList)
- **Findings**:
  - 型欠落時、deserialize 結果は `null` 要素になり、serialized 情報自体は保存される（型を後で復活させれば `SerializationUtility.GetManagedReferencesWithMissingTypes` 経由で missing 型情報が得られ、復元可能）。これにより Req 2.7 の "Missing Adapter placeholder" は「null 要素を専用 row として描画する」UI 側ロジックで実装できる。
  - 型 rename / namespace 移動時は `[MovedFrom(autoUpdateApi: true, ...)]` を付与すれば旧シリアライズデータを自動マイグレーションできる（preview 段階のため必須ではないが、core から `Hidano.FacialControl.Domain` へ移動する型には付与する慣例とする）。
  - `RefId` は同 SO 内で安定。同型 binding の複数追加（D-4）は問題なくサポートされる。
  - PropertyDrawer 解決順序は「具象型 → 親型 → デフォルト」の順。各アダプタパッケージが `[CustomPropertyDrawer(typeof(InputSystemAdapterBinding))]` を提供すれば、core 側 `ListView` の `bindItem` で `PropertyField` を生成するだけで自動的に当該 PropertyDrawer が呼ばれる（Req 3.3）。
  - 公式 / コミュニティ実装で確立されている polymorphic reorderable list の構成: UI Toolkit `ListView` + `bindItem`/`makeItem` + `PropertyField`、Add ドロップダウンは `AdvancedDropdown` か `GenericMenu`、Remove は `OnRemoveCallback`。
- **Implications**:
  - `FacialCharacterProfileSO._adapterBindings` は `[SerializeReference] List<AdapterBindingBase>`。Round-trip は Unity 標準で保証される。
  - 型欠落 row の検出は `SerializedProperty.managedReferenceFullTypename == ""`（型欠落時に空文字列になる挙動）で判定し、専用 `MissingAdapterPlaceholderElement` を描画する（Req 2.7）。
  - 将来の型移動に備え、`AdapterBindingBase` および `FacialAdapterBindingAttribute` を最初から最終形の namespace（`Hidano.FacialControl.Domain.Adapters`）に置く。preview 段階のため `MovedFromAttribute` は適用せず、破壊変更を許容する（Req 8.1）。

### Topic 3: `TypeCache.GetTypesWithAttribute` の挙動と確定タイミング（R-2）
- **Context**: Req 1.3 / 1.4 / 1.7 が Editor load 時の auto-discovery を要求。assembly reload や domain reload との整合確認が必要。
- **Sources Consulted**:
  - [Unity Scripting API: TypeCache.GetTypesWithAttribute (6000.2)](https://docs.unity3d.com/6000.2/Documentation/ScriptReference/TypeCache.GetTypesWithAttribute.html)
  - [Unity Scripting API: TypeCache](https://docs.unity3d.com/ScriptReference/TypeCache.html)
  - [TypeCache discussion thread](https://discussions.unity.com/t/unityeditor-typecache-api-for-fast-extraction-of-type-attributes-in-the-editor-tooling/745446)
- **Findings**:
  - `TypeCache` は Editor が load し domain 内のすべての assembly が ready になった時点で有効。`InitializeOnLoad` / `[InitializeOnLoadMethod]` から呼び出して問題ない（公式推奨パターン）。
  - 戻り値の `TypeCollection` は read-only かつ thread-safe。順序は **undefined** なので、Req 1.4 の「`displayName` 順 sort」は明示的に sort する必要がある。
  - assembly reload / domain reload 時に自動で再構築される。Editor 操作中（asmdef rebuild 後）も透過的に最新が返る。
  - 派生 attribute も match するため、`[FacialAdapterBinding]` に派生 attribute（例: `[InputSystemAdapterBindingAttribute : FacialAdapterBindingAttribute]`）を導入してもまとめて拾える。
- **Implications**:
  - core Editor の `AdapterBindingDiscovery` は `[InitializeOnLoad]` static class で `TypeCache.GetTypesWithAttribute<FacialAdapterBindingAttribute>()` を実行し、`displayName.OrdinalIgnoreCase` で sort してから static cache（`IReadOnlyList<AdapterBindingDescriptor>`）に保持する。
  - 同 `displayName` の重複は `Dictionary<string, List<Type>>` で集計し、複数あれば `Debug.LogWarning` + 当該 entry に `( {TypeName} )` suffix を付ける（Req 1.7）。
  - キャッシュは Editor session 単位で問題ない（実体は TypeCache が assembly reload を吸収する）。

### Topic 4: per-FacialController LifetimeScope の build / dispose コスト（R-3）
- **Context**: Req 9.4 が「`(adapterBindingCount + sourcesPerChar) × charCount` の線形スケール」を要求。同時 10 体制御で per-character LifetimeScope を毎ロードごとに build / dispose する場合の overhead が懸念。
- **Sources Consulted**:
  - [VContainer Lifetime Overview](https://vcontainer.hadashikick.jp/scoping/lifetime-overview)
  - [VContainer Project root LifetimeScope](https://vcontainer.hadashikick.jp/scoping/project-root-lifetimescope)
  - [VContainer GitHub README](https://github.com/hadashiA/VContainer/blob/master/README.md)
- **Findings**:
  - VContainer は **resolve は GC-free**。container build 自体は registration の数に応じた一回限りの allocation を行う（公式に「extra fast」と記載されているが具体ベンチマーク数値は未公開）。
  - 公式 pattern は「app-level scope (project root)」+「scene 単位 child scope」+「prefab 単位 child scope」。本仕様では「app-level scope」+「per-`FacialController` child scope」とする（D-12）。
  - 子 scope の build / dispose は `FacialController.Initialize` / `Cleanup` ライフサイクルに乗せれば 1 体あたり 1 回のみで steady-state には影響しない。Req 9.1（毎フレーム 0-alloc）と分離して扱える。
- **Implications**:
  - per-`FacialController` child scope は `Initialize()` で build、`Cleanup()` で dispose。`ReloadProfile()` / `LoadCharacter()` は dispose → rebuild の 2 ステップを踏む（既存 `_isInitialized` フラグの flow を踏襲）。
  - app-level scope は `[DefaultExecutionOrder(-9999)]` の bootstrap MonoBehaviour（`FacialControlAppScopeBootstrap`、auto-spawn）か、`ProjectLifetimeScope`（VContainer 公式の RuntimeInitializeOnLoadMethod ベース）で構築する。デフォルトは前者（dev project への侵入を最小化）。
  - 性能テスト（Req 9.5 / 10.7）で「3 binding × 10 体の child scope を毎フレーム回しても 0-alloc」を assert する。

### Topic 5: `AssetDatabase.SaveAssetIfDirty` のキャンセル可否（R-4）
- **Context**: Req 12.3「重複 slug がある間は asset の save をブロックする」を実装する手段の確認。
- **Sources Consulted**:
  - Unity AssetModificationProcessor 公式ドキュメント（Editor 標準 API）
  - SerializedObject `OnValidate` / `Editor.ApplyModifiedProperties` のドキュメント
- **Findings**:
  - `AssetModificationProcessor.OnWillSaveAssets(string[])` で **save 対象 path の filtering** が可能。「return された配列に含まれない path は save から除外される」。
  - `AssetDatabase.SaveAssetIfDirty` 自体に「キャンセル」API は無いが、上記 hook で実質的にブロックできる。`Debug.LogError` でユーザーに通知 + 当該 path を return 配列から除外する。
  - 代替として、Inspector の `OnValidate` 相当タイミングで slug 重複を検出した場合に `EditorUtility.DisplayDialog` で警告を出し、`SerializedObject.ApplyModifiedProperties` を呼ばないことで dirty 状態のまま放置する手もある（ただし他 field の編集まで巻き添えになる）。
  - **推奨**: 「Inspector で重複 slug を赤表示」+「`AssetModificationProcessor` で save から除外」+「`Debug.LogError` で path 名と理由を記録」の 3 段構え。これにより既存 dirty 状態は保持しつつ disk への永続化のみを止められる。
- **Implications**:
  - core Editor に `FacialCharacterProfileAssetGuard : AssetModificationProcessor` を追加し、`OnWillSaveAssets` で `FacialCharacterProfileSO` 型のみフィルタ → `_adapterBindings` 内の slug uniqueness を再検証 → 重複あれば対象 path を return 配列から除外し `Debug.LogError("[FacialControl] Save blocked: duplicate slug '{slug}' in adapter bindings of {assetPath}")` を出す。
  - Inspector 側 PropertyDrawer for slug field は `IMGUIContainer` ではなく UI Toolkit の `TextField` + `class: facial-control-error` でエラー表示し、Bind の値変更 callback で `RebuildSlugErrors()` を呼ぶ。

### Topic 6: UI Toolkit ListView での `[SerializeReference]` polymorphic list 編集（R-5）
- **Context**: Req 7.1 が「reorderable list + Add ドロップダウン + Remove + Move-Up/Down」を要求。Req 11.4 が UI Toolkit を強制。
- **Sources Consulted**:
  - [GitHub: Unity-Editor-PolymorphicReorderableList](https://github.com/CristianQiu/Unity-Editor-PolymorphicReorderableList)
  - [GitHub: Unity-SerializeReferenceExtensions](https://github.com/mackysoft/Unity-SerializeReferenceExtensions)
  - [GitHub: SerializeReferenceDropdown](https://github.com/AlexeyTaranov/SerializeReferenceDropdown)
  - 既存 `FacialCharacterProfileSOInspector.cs:116`（UI Toolkit 採用済み）
- **Findings**:
  - UI Toolkit `ListView` は `bindingPath` で `SerializedProperty` の List をバインド可能。`makeItem` で row を作り、`bindItem` で `PropertyField` を生成すれば、各要素の `SerializedProperty.managedReferenceValue` の具象型 PropertyDrawer が自動解決される。
  - reorder は `ListView.reorderable = true` + `reorderMode = ListViewReorderMode.Animated` で標準対応。Add は `ListView.makeNoneElement` ではなく独立した `Button` + `AdvancedDropdown` を併設するのが慣例。
  - `AdvancedDropdown` は階層メニュー対応の Add ドロップダウン UI。`displayName` を一覧する用途に最適（Req 1.4）。
  - Missing entry は `bindItem` 内で `prop.managedReferenceValue == null && prop.managedReferenceFullTypename != ""` を判定して赤色 row を描画する。
- **Implications**:
  - `Editor/Inspector/AdapterBindings/AdapterBindingsListView.cs`（UI Toolkit `VisualElement` 派生）として実装。`ListView` + `Button + AdvancedDropdown for Add` + `bindItem` で `PropertyField` 配置 + missing 検出。
  - PropertyDrawer 内例外時の per-element fallback（Req 3.6）は `bindItem` 側で try/catch して `class: facial-control-binding-error` の placeholder element を差し込む。

### Topic 7: 既存 `OscReceiver` を helper MonoBehaviour として binding 配下で `AddComponent` する場合の共存（R-6）
- **Context**: Req 13.6 / D-11 が「binding が `AddComponent` で helper MonoBehaviour を生成、HideFlags は通常表示」を規定。既存 scene で手動配置されている `OscReceiver` との共存方針が必要。
- **Sources Consulted**:
  - 既存 `Runtime/Adapters/OSC/OscReceiver.cs`（osc package）
  - 既存 `Runtime/OscFacialControllerExtension.cs`（osc package, 76 行、`OscReceiver` を `[SerializeField]` で受ける）
  - Unity GameObject.AddComponent / Object.Destroy 公式 API
- **Findings**:
  - 既存 scene の `OscReceiver` は `OscFacialControllerExtension._receiver` フィールドで参照されるが、Req 6.9 でこの Extension 自体が削除される。preview 段階のため、scene 上の `OscReceiver` も削除/再オーサリング対象（Req 8.4）。
  - `AddComponent<OscReceiverHost>(gameObject)` で動的生成し、binding が直接保持。`HideFlags = HideFlags.None`（= Inspector で見える、Req 13.6）。`OnDestroy` 内では `Object.Destroy(_helperHost)` で先に helper を破棄してから socket close する（Req 13.7）。
  - 「ユーザーが手動で OscReceiver を追加してしまった場合」は preview スコープ外として扱う（Req 8.4 で再オーサリング前提）。binding は同 GameObject 上に既存 helper があってもそれを参照せず、新規 `AddComponent` する。重複時は dispose 時に自分が `AddComponent` した helper のみを Destroy する（reference 等値で識別）。
- **Implications**:
  - `OscReceiverAdapterBinding.OnStart(in AdapterBuildContext ctx)` で `_helperHost = ctx.HostGameObject.AddComponent<OscReceiverHost>()` し、`_helperHost.Configure(port, endpoint, _doubleBuffer)` を呼ぶ。`Dispose()` で `if (_helperHost != null) Object.Destroy(_helperHost);`。
  - 同様の pattern を `OscSender`（送信用）にも適用する。
  - 既存 `OscReceiver.cs` / `OscSender.cs` の MonoBehaviour 自体は名称・責務を温存し、Public Setter (`SetEndpoint(...)`, `SetBuffer(...)`) を追加して binding から `Configure` できるようにする。インスペクタからの手動配置 path は preview.2 以降に再評価。

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| A. Big-bang 全置換 | 1 PR で core + osc + inputsystem を新 model に総入れ替え | 並走コードを残さない / 設計純度最大 | 30+ ファイル / 5,000+ LOC の単一 PR、レビュー困難、回帰範囲広 | preview 破壊許容なら有効だが PR レビュー負荷が高い |
| B. 二段階移行 | Phase 1: core 基盤 + Mock サンプル + VContainer / Phase 2: osc / inputsystem migration + 旧資産削除 | PR が中規模、Phase 1 で新 model の単独検証が可能 | Phase 1 〜 2 間に並走コード残存（一時的に core で `IFacialControllerExtension` と新 host を共存） | **採用**。各 PR 1.5〜2 週で完結 |
| C. Hybrid（既存 InputSourceFactory を slug factory として再生） | `InputSourceFactory` を完全削除せず slug-keyed `InputSourceRegistry` にリネーム＆責務縮小 | Domain 層 MultiSourceBlend のコード変更ゼロ、既存 0-alloc / テスト資産を最大温存 | `InputSourceRegistry` の名称と内容の整合確認が必要 | **B と組み合わせて採用**（Phase 1 で `InputSourceFactory` → `InputSourceRegistry` リネーム + `RegisterReserved` 削除 + slug-keyed `Register(slug, source)` のみ残す） |

**結論**: **Option B（二段階移行） + 内部的に Option C** を採用。

## Design Decisions

### Decision: VContainer LifetimeScope を 2 階層で構成する（D-12 詳細化）
- **Context**: Req 4.7 / D-9 / D-12 が「app-level scope + per-`FacialController` child scope」を規定。具体的な scope 構成と service の置き場が未確定。
- **Alternatives Considered**:
  1. 1 階層（per-`FacialController` のみ）: app-level の共有 service（TimeProvider 等）が無く、各 FC が個別に new する → 同一インスタンスを使いたい場面で困難。
  2. 3 階層（app + scene + per-FC）: scene 単位の共有 service を想定するが、本仕様では不要（同時 10 体は同一 scene 想定）。
  3. **2 階層（app + per-FC）**: 採用。
- **Selected Approach**:
  - **App-level `LifetimeScope`**: `FacialControlAppLifetimeScope`（VContainer の `LifetimeScope` 派生 MonoBehaviour）を `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` で auto-spawn。`ITimeProvider` / `ILipSyncProvider`（オプショナル） / `IInputSourceRegistryFactory` を register。
  - **Per-`FacialController` child scope**: `FacialController.Initialize()` 内で `appScope.CreateChild(builder => { ... })` を呼び、`AdapterBuildContext`、各 binding の `AdapterBindingHost`、`InputSourceRegistry`（per-FC instance）、`FacialProfile` を register。
- **Rationale**: 同時 10 体制御を想定した scalability、共有 service の再利用、per-FC の独立 dispose を満たす（D-12 / Req 9.4）。
- **Trade-offs**: app scope の auto-spawn は dev project に侵入する。`RuntimeInitializeOnLoadMethod` での lazy spawn により、利用側がコンポーネントを置かなくても動作するが、binding を 1 つも使わない project でもインスタンスが生成される（メモリ < 1KB の static singleton 相当でコスト低）。
- **Follow-up**: 性能テスト（PlayMode `Tests/Performance/AdapterBindingHostAllocationTests`）で「app scope 1 個 + per-FC scope 10 個 + 各 3 binding」の steady-state が 0-alloc を満たすことを assert。

### Decision: 1 binding = 1 `AdapterBindingHost` = 1 RegisterEntryPoint（D-10 詳細化）
- **Context**: Req 13.4-13.5 が「skip 後も他 binding と core が継続」を要求。VContainer の例外モデルとの整合が必要。
- **Alternatives Considered**:
  1. **1 binding = 1 host**: 採用。各 host が独立に `RegisterEntryPoint` され、PlayerLoop に individual entry point として挿入される。1 host が skip しても他 host は VContainer 側で自然に独立呼び出し。
  2. N binding = 1 host: N binding を 1 host が for loop で順次呼ぶ。実装は単純だが、host 内 catch + 自前 skip フラグで個別管理が必要 → 結局複雑度は変わらず、tickable 列を内蔵する分メモリ増。
- **Selected Approach**: per-FC scope の `Configure` 内で `bindings.Count` 個の `AdapterBindingHost` を `RegisterEntryPoint`。各 host は `_binding` フィールドと `_skipped` フラグのみ持ち、各 lifecycle method で try/catch + LogError + `_skipped = true`。
- **Rationale**: VContainer の "register at any time + PlayerLoop で独立 dispatch" を最大活用。Req 13.5 を構造的に保証。
- **Trade-offs**: 1 FC × 3 binding で 3 host インスタンスが生成される。10 FC で 30 インスタンス、各 200 byte 程度の見込みで scalability 上問題なし。
- **Follow-up**: `AdapterBindingHost` のメモリフットプリントを EditMode テストで `sizeof` 相当確認（`Marshal.SizeOf` は class に使えないため、構造的レビューで確認）。

### Decision: `AdapterBuildContext` を中立 `readonly struct` として定義（D-5 詳細化）
- **Context**: Req 4.10 / D-5 が「binding は VContainer interface を直接 import しない」「`AdapterBuildContext` で必要 service を中立に渡す」を規定。具体的フィールドが未確定。
- **Alternatives Considered**:
  1. interface（`IAdapterBuildContext`）: 拡張性は高いが、各 binding 実装が dynamic dispatch を経由 → わずかながら overhead。
  2. **`readonly struct`**: 採用。値型として stack 渡し、0-alloc。
- **Selected Approach**: フィールド構成
  ```csharp
  public readonly struct AdapterBuildContext
  {
      public readonly FacialProfile Profile;            // 現在ロード中のプロファイル
      public readonly IReadOnlyList<string> BlendShapeNames;
      public readonly IInputSourceRegistry InputSourceRegistry; // slug-keyed lookup（Option C 後継）
      public readonly ITimeProvider TimeProvider;
      public readonly GameObject HostGameObject;        // FacialController と同じ GO（D-11 helper AddComponent 用）
      public readonly ILipSyncProvider LipSyncProvider; // 任意（null 可）
  }
  ```
- **Rationale**: D-9（Domain 純度）は守りつつ、binding が必要な依存にすべてアクセス可能。`HostGameObject` は Engine 型だが、`AdapterBuildContext` 自体を Adapters 層 (`Hidano.FacialControl.Adapters`) に置くことで Domain 純度に影響しない（`AdapterBindingBase` だけが Domain 配置）。
- **Trade-offs**: フィールド追加時は struct のメモリレイアウトが変わるため、preview 中の breaking change は許容（D-13 と同方針）。
- **Follow-up**: フィールド追加は preview の minor bump で行い、CHANGELOG に記録。

### Decision: `InputSourceFactory` を `InputSourceRegistry` にリネームし slug-keyed 薄い lookup に縮小（Option C 採用）
- **Context**: 既存 `InputSourceFactory` (367 行) は (id, options) → IInputSource ディスパッチ + JSON deserialize を担当。Req 12.5 で reserved id 体系が廃止される。binding が `OnStart` で `IInputSource` を直接 produce し、registry に slug 登録する形へ責務を縮小する。
- **Alternatives Considered**:
  1. `InputSourceFactory` を完全削除し、`LayerInputSourceRegistry` に slug-keyed lookup を直接実装する: Domain 層に slug ロジックが入り込み Adapters 層の責務を Domain が持つ違和感。
  2. **`InputSourceFactory` → `InputSourceRegistry` リネーム + 責務縮小**: 採用。slug-keyed `Register(string slug, IInputSource source)` / `TryGet(string slug, out IInputSource source)` のみ。JSON deserialize / reserved id / RegisterReserved / RegisterExtension はすべて削除。
- **Selected Approach**:
  ```csharp
  public interface IInputSourceRegistry
  {
      void Register(string slug, IInputSource source);                      // binding が OnStart 内で呼ぶ
      void Register(string slug, string sub, IInputSource source);          // <slug>:<sub> 複合 id
      bool TryResolve(string layerInputSourceId, out IInputSource source);  // layer.inputSources[].id 解決
      IReadOnlyList<string> RegisteredSlugs { get; }                        // 診断用
  }
  ```
- **Rationale**: Domain 層の MultiSourceBlend 既存資産（`LayerInputSourceAggregator` / `LayerInputSourceWeightBuffer` / `LayerInputSourceRegistry`）を 1 行も変更せずに済み、既存 0-alloc / テスト資産を温存（Option C 推奨度 Medium → 採用根拠）。Adapters 層の glue だけで全要件を消化。
- **Trade-offs**: `InputSourceRegistry` という名称が Domain 層の `LayerInputSourceRegistry` と紛らわしい（後者は (layerIdx, sourceIdx) スキーマ、前者は slug → IInputSource）。命名規約として「Layer〜 = Domain layer 層所属」「Input〜 = Adapters 層所属の slug lookup」と明記する。
- **Follow-up**: Phase 2 の PR で `JsonSchemaDefinition` / `InputSourceDto` / `InputSourceOptionsDto` の処遇を再評価（slug 体系移行で JSON DTO の役割が変わる）。詳細は Phase 2 の design phase で再 dig。

### Decision: Slug 値オブジェクト `AdapterSlug` を Domain 層に追加（Req 12.6 / D-7）
- **Context**: 既存 `InputSourceId` の正規表現 `^[a-zA-Z0-9_.-]{1,64}$` を slug にも継続適用する（Req 12.6）。`InputSourceId` は legacy 経路（reserved id 体系）と紐づくため、slug を別 value-object として定義し責務分離する。
- **Alternatives Considered**:
  1. `InputSourceId` をそのまま流用: reserved id 概念削除（Req 12.5）と矛盾。 `IsReserved` プロパティを削除する形でも残ると意味的に混乱。
  2. **`AdapterSlug` 値オブジェクトを新設**: 採用。`InputSourceId` は preview 中に削除予定（Phase 2）。
  3. plain `string`: 検証を呼び出し側に分散させると保守性が落ちる。
- **Selected Approach**:
  ```csharp
  public readonly struct AdapterSlug : IEquatable<AdapterSlug>
  {
      public string Value { get; }
      public static bool TryParse(string input, out AdapterSlug slug);    // 正規表現検証 + IsValid
      public static AdapterSlug Parse(string input);                       // FormatException 投出
      public static AdapterSlug FromDisplayName(string displayName);       // kebab-case 自動生成 (Req 12.2)
      public static bool TryParseComposite(string input, out AdapterSlug slug, out string sub); // <slug>:<sub> 解析 (Req 12.4)
  }
  ```
- **Rationale**: Domain 純度（Req 11.1）を守りつつ、Adapters / Editor / Tests から再利用可能。`InputSourceId` の値オブジェクト pattern を踏襲し、既存テスト資産（`InputSourceIdTests`）を `AdapterSlugTests` として書き写せる。
- **Trade-offs**: Phase 1 で `InputSourceId` と `AdapterSlug` が一時的に並存する（Phase 2 で `InputSourceId` 削除予定）。Phase 1 の混乱はコメントで解消。
- **Follow-up**: `x-` prefix の third-party convention は `AdapterSlug` の static helper として残すが、必須化しない（preview 段階の柔軟性を優先）。

### Decision: core サンプル名は `MultiSourceBlendBasicSample`、Mock binding は `Tests/Shared/` に配置せず Sample 内に同梱（D-14 / Remaining Risk）
- **Context**: D-14 は `MultiSourceBlendBasicSample` / `MultiSourceBlendSelfCheck` 等の候補から design phase で確定する。Mock binding の置き場（`Samples~/.../Mocks/` か `Tests/Shared/`）も未確定。
- **Alternatives Considered**:
  1. `MultiSourceBlendBasicSample` + Sample 内 Mock: 採用。サンプル単独で完結、UPM 利用者が即動作確認可能。
  2. `MultiSourceBlendSelfCheck`: "selfcheck" は機能テスト連想で利用者向けサンプル名として違和感。
  3. Mock binding を `Tests/Shared/`: テストアセットがサンプルに引き込まれると asmdef 設計が崩れる。
- **Selected Approach**:
  - サンプル名: **`MultiSourceBlendBasicSample`**
  - 配置: `Packages/com.hidano.facialcontrol/Samples~/MultiSourceBlendBasicSample/`
  - 構成: 最小コード `MultiSourceBlendBasicRunner.cs`（Edit Mode から呼べる static API） + `MockTriggerAdapterBinding.cs` + `MockAnalogAdapterBinding.cs` + `multi_source_blend_basic.json`（プロファイル）+ README.md。HUD なし。
  - `Tests/EditMode/Domain/MultiSourceBlend/` から Mock binding を再利用したい場合は、Tests 側で別 Mock を持つ（Mock binding は Sample と Tests それぞれに専用版を置く方針）。
- **Rationale**: D-14 の「demo 感の薄い名前」要件を満たし、Sample 単独で UPM Import → 1 click 動作確認が可能。Mock binding を Tests と Sample で重複させても 100 LOC 以下に収まり保守負荷は低い。
- **Trade-offs**: Mock binding 2 系統の重複保守。drift 防止のためどちらを編集したらもう一方もコピーする旨を README に記載（structure.md の二重管理 Sample ルールと同方針）。
- **Follow-up**: Phase 2 で `inputsystem/Samples~/MultiSourceBlendDemo/` HUD の slug 化と core Sample との整合を再確認。

### Decision: ARKit binding は `com.hidano.facialcontrol.osc` 内に新設（C-6 解決）
- **Context**: 現状 ARKit 入力経路は OSC 経由 `ArKitOscAnalogSource`（osc package）のみ。ARKit binding は (a) osc package 内 / (b) 新パッケージ / (c) core 内 のどこに置くかが未確定。
- **Alternatives Considered**:
  1. **osc package 内に `ArKitOscAdapterBinding`**: 採用。ARKit 入力は OSC 受信経路を再利用するため osc package と同居が自然。`OscReceiverAdapterBinding` と `ArKitOscAdapterBinding` は別 binding として並存（Req 6.3 が「binding として提供」を明示）。
  2. 新パッケージ `com.hidano.facialcontrol.arkit`: package 数が増え管理コスト増。preview 段階では osc 同居で十分。
  3. core 内: core は ARKit / OSC を知らない契約（Req 4.1）に違反。
- **Selected Approach**:
  - osc package の `Runtime/Adapters/ARKit/ArKitOscAdapterBinding.cs` として実装。`displayName: "ARKit / PerfectSync"`。
  - 既存 `ArKitOscAnalogSource` は binding 配下の helper として再構成し、binding `OnStart` で OSC receiver に subscribe する。
- **Rationale**: ARKit 検出 (`ARKitDetector`) 自体は core が提供（Editor のみ）し、binding は OSC 経由で float 列を受信する形に統一。osc package と心地よく同居。
- **Trade-offs**: osc package が ARKit 用語をラベルとして公開するが、依存方向上は問題なし（osc package が core を参照する片方向依存を維持）。
- **Follow-up**: Phase 2 で `Runtime/Adapters/ARKit/` 配下のレイアウト（DTO / parser / sub-id 命名）を詳細決定。

## Risks & Mitigations

- **R-A: VContainer 1.17.x の Unity 6 互換未確認** — Phase 1 着手の最初に `[InitializeOnLoadMethod]` で `LifetimeScope` 構築 → resolve → dispose の smoke test を EditMode テストに追加し early fail を検出する。
- **R-B: `[SerializeReference]` の null 要素表示が Unity 6 で expected 通りか未検証** — Phase 1 の最初に `Tests/EditMode/Editor/Inspector/AdapterBindingsListViewTests.cs` で「missing 型 simulation（assembly unload を再現できないので、削除予定の Mock 型を使った差し替えで擬似する）」を実装し動作確認する。
- **R-C: per-FC LifetimeScope を 10 体並列で建てた際の build/dispose コストが線形でない可能性** — Phase 1 の PlayMode performance test で `MultiCharacterPerformanceTests` パターンを踏襲し、`(adapterBindingCount + sourcesPerChar) × charCount` の線形性を Profiler markers で確認する。
- **R-D: `InputSourceFactory` リネームの影響範囲（既存テスト群）** — gap-analysis の C-9 で挙がった影響テストファイル一覧（`OscControllerBlendingIntegrationTests.cs` 等）を Phase 2 の `tasks.md` で明示し、preview 破壊変更として一括書換する。
- **R-E: 既存 `IFacialControllerExtension` 並走期間（Phase 1）の整合** — Phase 1 では `_adapterBindings` が空 list でも既存挙動を 100% 維持する（既存 `ApplyExtensions` 経路は変更なし）。CI で Phase 1 PR が既存テスト全 green を維持することを確認する。
- **R-F: AssetModificationProcessor での save block が user 体験を損なう** — Save Block 発動時に `EditorUtility.DisplayDialog` で「重複 slug が原因」を明示し、当該 SO を Inspector で開けるよう Selection.activeObject にセットする。

## References

- [Plain C# Entry point | VContainer](https://vcontainer.hadashikick.jp/integrations/entrypoint) — VContainer の lifecycle interface 一覧と PlayerLoop 統合
- [VContainer Lifetime Overview](https://vcontainer.hadashikick.jp/scoping/lifetime-overview) — LifetimeScope のスコープ階層と `CreateChild` の使い方
- [VContainer GitHub Repository](https://github.com/hadashiA/VContainer) — 最新版 (1.17.x) の API リファレンス
- [OpenUPM: jp.hadashikick.vcontainer](https://openupm.com/packages/jp.hadashikick.vcontainer/) — UPM scoped registry 経由のインストール方法
- [Unity Scripting API: SerializeReference (6000.2)](https://docs.unity3d.com/6000.2/Documentation/ScriptReference/SerializeReference.html) — Unity 6 の polymorphic serialization 仕様
- [Unity Scripting API: ManagedReferenceMissingType](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ManagedReferenceMissingType.html) — 型欠落時の placeholder 挙動
- [Unity Scripting API: SerializationUtility.GetManagedReferencesWithMissingTypes](https://docs.unity3d.com/ScriptReference/SerializationUtility.GetManagedReferencesWithMissingTypes.html) — Missing 型の検出 API
- [Unity Scripting API: TypeCache.GetTypesWithAttribute (6000.2)](https://docs.unity3d.com/6000.2/Documentation/ScriptReference/TypeCache.GetTypesWithAttribute.html) — 属性付き型の高速列挙
- [SerializeReference improvements in Unity 2021 LTS](https://discussions.unity.com/t/serializereference-improvements-in-unity-2021-lts/886491) — null 要素 / RefId 安定性 / MovedFromAttribute の議論
- [Unity-Editor-PolymorphicReorderableList](https://github.com/CristianQiu/Unity-Editor-PolymorphicReorderableList) — UI Toolkit ListView での polymorphic list 実装例
- [Unity-SerializeReferenceExtensions](https://github.com/mackysoft/Unity-SerializeReferenceExtensions) — Add ドロップダウンの実装パターン
