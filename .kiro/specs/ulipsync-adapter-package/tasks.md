# Implementation Plan

> **構成方針**: 本プランは design.md の `## Open Questions / Risks` で **Spike** として示された Risk-2（ネスト `[SerializeReference]` round-trip）を Phase 0 の最先頭に配置し、**早期失敗**の判断材料を確保したうえで Phase 1（パッケージ scaffolding）→ Phase 2（Runtime 実装、TDD）→ Phase 3（Editor PropertyDrawer）→ Phase 4（Samples 二重管理）→ Phase 5（ドキュメント）→ Phase 6（PlayMode 統合・性能）の順で進める。
>
> **TDD 規約**: 各 Runtime 実装サブタスクは Red → Green → Refactor の流れで進め、対応する EditMode / PlayMode テストを実装前に作成する（CLAUDE.md / steering）。
>
> **Design Decisions Drift 防止**: DD-A（NAudio 直接参照）/ DD-B（複数 SMR 解決）/ DD-C（hot-swap deferred）/ DD-D（slug = `ulipsync`）/ DD-E（profile 注入タイミング）/ DD-AddOrder（コンポーネント追加順）/ DD-PhonemeLookup（`TryGetValue` ループ）/ DD-Settle / DD-StartupSettle / DD-AnimSampling は対応サブタスク詳細で都度注記する。
>
> **`(P)` マーカー**: 異なる責務境界で並走可能なサブタスクに `(P)` を付与する。`_Boundary:_` で対象境界を明示する。

---

## Phase 0: Spike — ネスト [SerializeReference] round-trip 検証（gating）

- [x] 1. ネスト `[SerializeReference]` の二重多態リスト round-trip を検証する
- [x] 1.1 ネスト `[SerializeReference]` round-trip スモークテストを EditMode に追加する
  - コア既存の `Tests/EditMode/Adapters/ScriptableObject/SerializeReferenceRoundTripSmokeTests.cs` のパターンを 2 段ネスト用に拡張し、本パッケージの `Tests/EditMode/Adapters/PhonemeEntrySerializeReferenceSmokeTests.cs` に新規スモークテストを作成する
  - `_adapterBindings`（`[SerializeReference]`）→ `ULipSyncAdapterBinding._phonemeEntries`（`[SerializeReference]` 多態リスト）の二重ネスト構造を検証対象にする
  - `BlendShapePhonemeEntry` / `AnimationClipPhonemeEntry` を仕様の本実装より前に **テスト内 Stub として最小宣言**（後続タスクで本実装へ差し替え）し、各派生型を 1 個ずつ含む `List<PhonemeEntryBase>` を `FacialCharacterProfileSO` 経由で保存する
  - Editor 再起動相当の round-trip（`AssetDatabase.SaveAssets` → `Resources.UnloadUnusedAssets` → 再 `AssetDatabase.LoadAssetAtPath`）後も `managedReferenceFullTypename` 解決が成功し、両派生型のフィールド値が保持されていることを assert する
  - 観測可能完了条件: スモークテストが green。fail した場合は本仕様 Phase 2 以降を停止し、`PhonemeEntryBase` を抽象クラスから `enum + 共通 struct` に切り替える設計修正タスクを別途起票する退路に切替える判断材料となる
  - **配置パス相違**: 実装は `com.hidano.facialcontrol/Tests/EditMode/Adapters/PhonemeEntrySerializeReferenceSmokeTests.cs`（コアパッケージ側）に配置。Phase 0 Spike としての目的（早期失敗の判断材料）は達成済み。本パッケージへの移送は backlog（preview.2 以降）で検討
  - _Requirements: 3.3, 4.1, 12.3_
  - _Boundary: Tests/EditMode/Adapters_

---

## Phase 1: パッケージ scaffolding

- [x] 2. パッケージ `com.hidano.facialcontrol.lipsync` の足場を整備する
- [x] 2.1 `package.json` と標準 UPM ディレクトリレイアウトを作成する
  - `Packages/com.hidano.facialcontrol.lipsync/package.json` を新規作成し、id・displayName・version・unity 6000.3.2f1・description・keywords（windows-only 明記）・samples 配列・dependencies（コア + `com.hidano.ulipsync-asio`）を宣言する
  - `Runtime/` / `Editor/` / `Tests/EditMode/` / `Tests/PlayMode/` / `Tests/Shared/` / `Samples~/` / `Documentation~/` の空ディレクトリと `README.md` / `CHANGELOG.md` / `LICENSE.md` 雛形を配置する
  - `FacialControl/Packages/manifest.json` にローカルパッケージ参照（`file:` プロトコル）を追加し、`packages-lock.json` を同期する
  - 観測可能完了条件: Unity Editor を起動した際に Package Manager に `com.hidano.facialcontrol.lipsync` が認識され、依存（コア / `com.hidano.ulipsync-asio`）が解決済みになる
  - _Requirements: 1.1, 1.2, 1.5, 1.6_

- [x] 2.2 Runtime / Editor / Tests asmdef を作成し参照を確立する
  - `Runtime/Hidano.FacialControl.LipSync.asmdef`（`includePlatforms: ["Editor", "WindowsStandalone64"]`、references: コアの 3 asmdef + `uLipSync.Runtime` + `uLipSync.Runtime.Windows` + `Unity.Animation`）を作成する
  - `Editor/Hidano.FacialControl.LipSync.Editor.asmdef`（`includePlatforms: ["Editor"]`、Runtime asmdef + コア Editor を参照）を作成する
  - `Tests/EditMode/...EditMode.asmdef` / `Tests/PlayMode/...PlayMode.asmdef` / `Tests/Shared/...Shared.asmdef` を `nunit` / `UnityEngine.TestRunner` を含めて作成する
  - **DD-A 注記**: `uLipSync.Runtime.Windows` 経由で `NAudio.Asio.dll` が precompiled reference として transitive 解決されることを確認
  - 観測可能完了条件: Editor が再ロードされ、Runtime / Editor / Tests の各 asmdef がコンパイルエラー無くビルドされる（Domain 純度違反の参照は無いこと）
  - _Requirements: 1.3, 1.4, 1.6, 14.7, 14.8_

- [x] 2.3 (P) `Hidano.FacialControl.LipSync` 名前空間の placeholder 型を配置する
  - 各 asmdef に対応する空 namespace ファイル（`Adapters/.gitkeep` 相当の placeholder C#）を配置し、後続タスクで具体型を追加する基盤を作る
  - 観測可能完了条件: `Hidano.FacialControl.LipSync.*` 名前空間ルート配下で型を追加すれば自動的に asmdef が拾うことを Editor で確認できる
  - _Requirements: 1.3, 14.8_
  - _Boundary: Runtime/Adapters_

---

## Phase 2: Runtime 実装（TDD）

- [x] 3. `PhonemeEntryBase` と派生 2 種を実装する
- [x] 3.1 `PhonemeEntryBase` / `BlendShapePhonemeEntry` / `AnimationClipPhonemeEntry` の EditMode テストを書く（Red）
  - 各派生型の `[Serializable]` 属性、共通フィールド（`PhonemeId` / `MaxWeight`）、固有フィールド（`BlendShapeName` / `Clip`）を `SerializedProperty` 経由で round-trip するテストを書く
  - `MaxWeight` は `[0..100]` を許容しビルダー側で `100` 換算する仕様を明文化したテストを書く
  - 観測可能完了条件: テストが Red の状態でコミット可能であり、対応実装ファイルがまだ存在しない
  - _Requirements: 3.3, 4.1, 4.3, 4.4, 11.5, 14.5_
  - _Boundary: Tests/EditMode/Adapters_

- [x] 3.2 `PhonemeEntryBase` 抽象クラスと派生 2 種を実装する
  - `Hidano.FacialControl.LipSync.Adapters.PhonemeEntries.PhonemeEntryBase` abstract class を `[Serializable]` で定義し、共通 public field（`PhonemeId` / `MaxWeight`）を保持する
  - `BlendShapePhonemeEntry`（`BlendShapeName: string`）と `AnimationClipPhonemeEntry`（`Clip: AnimationClip`）を `sealed [Serializable]` で実装する
  - 内部値オブジェクト `PhonemeSnapshot`（`readonly struct (PhonemeId, Weights[])`）を実装する
  - 観測可能完了条件: 3.1 のテストが全て green になり、Phase 0 のスモークテスト（1.1）も実 Type を参照した状態で green を維持
  - _Requirements: 3.3, 4.1, 4.3, 4.4, 11.5_

- [x] 4. `IULipSyncEventSource` 抽象と既定実装を整備する
- [x] 4.1 `FakeULipSyncEventSource` を `Tests/Shared` に追加する
  - 任意 `LipSyncInfo` を `Invoke(LipSyncInfo info)` 経由で公開ハンドラに流す Fake を実装する
  - `IULipSyncEventSource` インタフェース（`event Action<uLipSync.LipSyncInfo> OnLipSyncUpdate`）も同 PR で定義する（Production 実装の前にテスト境界を確定）
  - 観測可能完了条件: Fake をテストから new し、`OnLipSyncUpdate += handler` → `Invoke(info)` で handler が指定回数呼ばれる EditMode テストが green
  - _Requirements: 5.2, 14.1_
  - _Boundary: Tests/Shared, Runtime/Adapters_

- [x] 4.2 `ULipSyncEventBridge` を実装し UnityEvent と接続する
  - `internal sealed class ULipSyncEventBridge : IULipSyncEventSource, IDisposable` を実装し、ctor で `uLipSync.uLipSync.onLipSyncUpdate.AddListener(...)`、`Dispose` で `RemoveListener` を呼ぶ
  - 内部の C# event 中継で UnityEvent からの `LipSyncInfo` を購読者へフォワードする
  - 観測可能完了条件: Bridge を実装後、production の binding が `IULipSyncEventSource` 抽象を介して uLipSync コンポーネントに接続できることを EditMode（Bridge を direct new して `uLipSync.uLipSync` の偽 onLipSyncUpdate を Invoke）または PlayMode で確認できる
  - _Requirements: 5.2, 5.9_

- [x] 5. `ULipSyncProvider`（ホットパス GC 0 byte）を TDD で実装する
- [x] 5.1 `ULipSyncProviderTests` を書く（Red）
  - `Constructor_NullSource_ThrowsArgumentNullException`（5.8）
  - `OnLipSyncUpdate_PhonemeRatiosWithKnownKeys_AccumulatesWeightedSum`（5.2, 5.3）
  - `OnLipSyncUpdate_UnknownPhonemeKey_IsIgnoredWithoutLog`（5.6）
  - `OnLipSyncUpdate_ConfiguredEntryAbsentInFrame_ContributesZero`（5.7）
  - `GetLipSyncValues_SilentFrame_ProducesSubThresholdSum`（5.5）
  - `BlendShapeNames_AfterConstruction_ReturnsFixedOrder`（5.4）
  - `Dispose_AfterCall_NoLongerReceivesEvents`（5.9）
  - `GetLipSyncValues_AfterRequestZeroOutput_ProducesZeroSpan`（DD-Settle / DD-StartupSettle）
  - すべて `FakeULipSyncEventSource` で `LipSyncInfo` を制御注入し、テスト対象の `_accum` を介した出力を assert する
  - 観測可能完了条件: 全テストが Red の状態でコミット可能で、`ULipSyncProvider` 実装は未存在
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9, 14.1, 14.5_
  - _Boundary: Tests/EditMode/Adapters_

- [x] 5.2 `ULipSyncProvider` 本体を実装し Red を Green にする
  - `sealed class ULipSyncProvider : ILipSyncProvider, IDisposable` をコンストラクタ `(IULipSyncEventSource, IReadOnlyList<PhonemeSnapshot>, int blendShapeCount)` で構築する
  - 内部バッファ `_accum: float[blendShapeCount]` / `_phonemeKeys: string[]` / `_phonemeIndices: int[]` を構築時にのみ確保する
  - `OnLipSyncUpdate(LipSyncInfo info)` で `_phonemeKeys[i]` を `TryGetValue` ループ参照（**DD-PhonemeLookup**）し、`ratio * info.volume * snapshot.Weights[k]` を `_accum` に加算する
  - `GetLipSyncValues(Span<float> output)` は `_zeroOutputRequested` フラグ時 `output.Clear()` のみ実行 + フラグ reset、通常時は `_accum` を `output` へコピー（不足長は overlap のみ）
  - `RequestZeroOutputForNextFrame()` public API を提供する
  - `Dispose` で `eventSource.OnLipSyncUpdate -= OnLipSyncUpdate` を実施し、以降 `_isDisposed = true` 経路を維持する
  - 観測可能完了条件: 5.1 のテストが全て green になる
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9, 11.1, 11.3_

- [x] 5.3 (P) `ULipSyncProvider` ホットパス 0 byte ベンチマークテストを追加する
  - `Tests/EditMode/Performance/ULipSyncProviderAllocationTests.cs` を新規作成し、`GC.GetTotalAllocatedBytes(true)` 差分計測パターン（既存 `Tests/PlayMode/Performance/GCAllocationTests.cs` 等を踏襲）で `OnLipSyncUpdate` + `GetLipSyncValues` を 10000 回連続呼出した差分が 0 byte であることを assert する
  - 観測可能完了条件: ベンチマークテストが green、即ちホットパスでヒープ確保が発生しないことが保証される
  - _Requirements: 11.1, 11.3, 11.4, 14.1_
  - _Boundary: Tests/EditMode/Performance_

- [x] 6. デバイス列挙抽象とリゾルバを TDD で実装する
- [x] 6.1 (P) `FakeAsioDriverEnumerator` / `FakeMicrophoneDeviceEnumerator` を `Tests/Shared` に追加する
  - 任意の `string[]` を返す Fake をそれぞれ実装し、空配列・1 要素・同名重複（disambiguator 検証用）の各シナリオを構築可能にする
  - 観測可能完了条件: Fake を Test から new し、所定配列を返すことを EditMode テストで確認できる
  - _Requirements: 7.2, 7.3, 7.4, 14.1_
  - _Boundary: Tests/Shared_

- [x] 6.2 `IAsioDriverEnumerator` / `IMicrophoneDeviceEnumerator` インタフェースと Default 実装を追加する
  - `IAsioDriverEnumerator.GetDriverNames()` / `IMicrophoneDeviceEnumerator.GetDeviceNames()` を Runtime asmdef に定義する
  - `DefaultAsioDriverEnumerator` を `#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN` ガード付きで実装し、`NAudio.Wave.Asio.AsioOut.GetDriverNames()` を try/catch + LogError + 空配列で安全化する（**DD-A**）
  - `DefaultMicrophoneDeviceEnumerator` は `UnityEngine.Microphone.devices` をそのまま返す
  - 非 Windows 経路は空配列を返す stub にする（Req 1.6 整合）
  - 観測可能完了条件: Default 実装を直接 new した EditMode 単体テスト（または PlayMode 動作確認）で `GetDriverNames()` / `GetDeviceNames()` が文字列配列を返却し、例外時も空配列で fallback する
  - _Requirements: 1.6, 7.2, 7.3_

- [x] 6.3 `DeviceResolverTests` を書き、`DeviceResolver` を TDD で実装する
  - `Resolve_AsioMatch_ReturnsAsioKind`（7.2）、`Resolve_MicMatch_ReturnsMicrophoneKind`（7.3）、`Resolve_DisambiguatorIndex_SelectsNthMatch`（7.4）、`Resolve_NoMatch_ReturnsUnresolvedWithSnapshots`（9.1）、`Resolve_NullEnumerator_ThrowsArgumentNullException` を Red で書く
  - `static class DeviceResolver` の `Resolve(DeviceDescriptor, IAsioDriverEnumerator, IMicrophoneDeviceEnumerator)` を実装し、ASIO を Mic より先に検査する純粋関数とする
  - 同名重複時は `DisambiguatorIndex` を消費して N 番目を選択し、不一致時 `Unresolved` に列挙スナップショットを持たせる
  - LINQ を使わず for ループで実装（**11.6**）
  - 観測可能完了条件: テストが全て green、`Resolve` が GC を発生させない手動 for ループ実装になっている
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 9.1, 9.2, 11.6, 14.1, 14.5_

- [x] 7. `ULipSyncAdapterBinding` lifecycle を TDD で実装する
- [x] 7.1 `ULipSyncAdapterBinding` 構築・OnStart 成功経路の PlayMode テストを書く（Red）
  - `Tests/PlayMode/Lifecycle/ULipSyncAdapterBindingLifecycleTests.cs` を新規作成し、Mic 経路で `OnStart` 成功時に `AudioSource` → `uLipSync.uLipSync` → `uLipSyncMicrophone` の順で `HostGameObject` に AddComponent され、`Provider` / `Analyzer` プロパティが非 null になることを assert する（**DD-AddOrder**）
  - `OnStart` 末尾で `Provider.RequestZeroOutputForNextFrame()` が呼ばれ、初回 `GetLipSyncValues` が zero settle されることを assert する（**DD-StartupSettle**）
  - 観測可能完了条件: テストが Red の状態でコミット可能で、`ULipSyncAdapterBinding` 実装は未存在
  - _Requirements: 2.1, 2.2, 2.3, 6.1, 6.2, 6.3, 6.7, 9.3, 14.2, 14.5_
  - _Boundary: Tests/PlayMode/Lifecycle_

- [x] 7.2 `ULipSyncAdapterBinding` を実装し OnStart Green を達成する
  - `[Serializable] [FacialAdapterBinding(displayName: "uLipSync")] sealed class ULipSyncAdapterBinding : AdapterBindingBase` を引数なし ctor で実装する
  - `[SerializeField] DeviceDescriptor _deviceDescriptor` / `[SerializeField] uLipSync.Profile _analyzerProfile`（任意）/ `[SerializeReference] List<PhonemeEntryBase> _phonemeEntries` / `[SerializeField] string _targetMeshHint` / `[SerializeField] float _maxWeightScale = 1f` を配置する
  - `OnStart(in AdapterBuildContext ctx)` で (a) ガード、(b) `DeviceResolver.Resolve` 呼出、(c) Analyzer Profile 解決（`_analyzerProfile` 優先 → `Resources.Load` でパッケージ同梱の既定 Profile にフォールバック）、(d) BuildSnapshots 呼出、(e) AudioSource → uLipSync.uLipSync → profile 注入 → Mic/Asio AddComponent の順序、(f) `ULipSyncProvider` 構築 + `LipSyncInputSource` 構築 + `ctx.InputSourceRegistry.Register(slug, source)`、(g) `Provider.RequestZeroOutputForNextFrame()`、`_started = true`、を順序立てて実行する（**DD-AddOrder / DD-StartupSettle**）
  - `Slug` 既定値は `"ulipsync"`（**DD-D**）
  - `_started` ガードと `ctx.HostGameObject == null` ガードで冪等にする
  - 観測可能完了条件: 7.1 のテストが green、`HostGameObject` への AddComponent 順序と Provider 初期化が assert を通過
  - _Requirements: 2.1, 2.2, 2.3, 3.2, 3.4, 3.5, 6.1, 6.2, 6.3, 6.7, 10.2, 10.4_

- [x] 7.3 `BuildSnapshots` を実装し AnimationClip time-0 サンプリングを安全化する
  - `_phonemeEntries` を走査して `PhonemeSnapshot[]` を構築する private メソッドを実装する
  - BlendShape 形式: `BlendShapeName` を SMR の BlendShape 名と照合し、対応 index に `MaxWeight / 100 * _maxWeightScale` を fill。未解決時 `Debug.LogWarning` + skip（**4.7**）
  - AnimationClip 形式: `ctx.HostGameObject.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true)` で全 SMR 列挙 → 各 SMR の全 BlendShape weight を `_savedWeights[][]` に退避 → `try { foreach (clip) AnimationClip.SampleAnimation(host, clip, 0f); 各 SMR から weight 読み取り → snapshot[entry] } finally { 全 SMR 復元 }` の順で実行（**DD-AnimSampling**）
  - 対象 SMR は `_targetMeshHint` 優先（相対パス）、未解決または空時は `GetComponentInChildren<SkinnedMeshRenderer>()` の depth-first 最初を採用（**DD-B**）。`_targetMeshHint` で指定されたが見つからない場合 `Debug.LogWarning` + first SMR フォールバック
  - 観測可能完了条件: BlendShape / AnimationClip / 混在エントリの各シナリオで `PhonemeSnapshot[]` が期待値どおり構築される EditMode `PhonemeSnapshotBuilderTests` が green
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.7, 11.5, 14.1_

- [x] 7.4 (P) `PhonemeSnapshotBuilderTests` を EditMode に追加する
  - `Build_BlendShapeEntryDirectFill_FillsCorrectIndex`（4.3）/ `Build_AnimationClipEntryTimeZero_ExtractsBlendShapeWeights`（4.4）/ `Build_MixedEntries_BothApplyConsistently`（4.5）/ `Build_UnresolvedBlendShapeName_LogsWarningAndSkips`（4.7）/ `Build_AfterAnimationClipSampling_RestoresSmrWeights`（DD-AnimSampling 副作用 finally 検証）/ `Build_TargetMeshHintMissing_FallsBackToFirstSmr`（DD-B）を追加する
  - PlayMode 必須とせず EditMode で完結させる（mock SMR + 単一 GameObject 構成）
  - 観測可能完了条件: 全テスト green、AnimationClip サンプリング後にホスト SMR の BlendShape weight が元値に復元されていることを assert で確認
  - _Requirements: 4.3, 4.4, 4.5, 4.7, 14.1, 14.5_
  - _Boundary: Tests/EditMode/Adapters_

- [x] 7.5 `Dispose` と OnFixedTick を実装する
  - `Dispose` で (a) `Provider.Dispose`（購読解除）、(b) `ctx.InputSourceRegistry.Unregister(slug)`、(c) AddComponent した uLipSync 系コンポーネント全件を `UnityEngine.Object.Destroy`、(d) `_started = false`、を逆順で実行する
  - `OnFixedTick(float dt)` は `_swapPending` フラグ判定経路のみ持ち、フラグ未セット時は no-op（GC 0 byte 維持、**11.2**）
  - 重複 binding 検知: `Register` が既に同 slug 登録済みの場合 `Debug.LogError` + 後続初期化を skip（**10.3**）
  - 観測可能完了条件: PlayMode テスト `Dispose_AfterStart_RemovesAllAddedComponents` が green、Host から uLipSync 系コンポーネントが全件消える
  - _Requirements: 2.4, 6.5, 6.6, 10.3, 11.2_

- [x] 8. ホットスワップと silence モードを TDD で実装する
- [x] 8.1 `DeviceHotSwapTests` を PlayMode に書く（Red）
  - `SwapDevice_MicToMicSecondDevice_ZeroSettlesThenRebinds`（8.1, 8.2）/ `SwapDevice_UnresolvedTarget_EntersSilenceModeWithoutBrokenComponents`（8.5, 9.3）/ `SwapDevice_PreservesULipSyncAndProviderInstances`（8.3）を Red で書く
  - 観測可能完了条件: テストが Red、`SwapDevice` 実装は未存在
  - _Requirements: 8.1, 8.2, 8.3, 8.5, 9.3, 9.4, 9.5, 14.2, 14.5_
  - _Boundary: Tests/PlayMode/HotSwap_

- [x] 8.2 `SwapDevice` 公開 API（deferred swap）を実装する
  - `public void SwapDevice(string deviceName, int disambiguatorIndex)` を実装し、(a) `Provider.RequestZeroOutputForNextFrame()`、(b) `_pendingDescriptor = new DeviceDescriptor(...)`、(c) `_swapPending = true` のみ実施する（**DD-C**）
  - `OnFixedTick` で `_swapPending` 検出時に旧 Mic/Asio コンポーネントを `Destroy` → `DeviceResolver.Resolve` → 解決成功時のみ新コンポーネントを AddComponent し uLipSync.uLipSync と再連結。解決失敗時は `Debug.LogError` + silence モード維持（新コンポーネント未付加）
  - swap 中も `uLipSync.uLipSync` 本体と `ULipSyncProvider` インスタンス、`InputSourceRegistry` 登録は破棄せず維持する（**8.3**）
  - 観測可能完了条件: 8.1 のテストが全て green、Mic2 番目への swap で旧 Mic component が消え、新 Mic component が AddComponent され、Provider / Analyzer のインスタンス参照が swap 前後で同一
  - _Requirements: 8.1, 8.2, 8.3, 8.5, 9.3, 9.4, 9.5_

---

## Phase 3: Editor PropertyDrawer

- [x] 9. `ULipSyncAdapterBindingDrawer` を UI Toolkit で実装する
- [x] 9.1 (P) `DeviceDescriptorPopup` を実装する
  - UI Toolkit `PopupField<string>` で choices を `IAsioDriverEnumerator` + `IMicrophoneDeviceEnumerator` の Default 実装から動的取得する
  - 接続中でないデバイス名を入力可能な手動 override `TextField` と `disambiguatorIndex` の `IntegerField`（既定 0）を併設する
  - ASIO/Mic toggle UI は出さない（**7.5**）
  - 観測可能完了条件: ダミー Inspector で `DeviceDescriptorPopup` を表示し、ASIO / Mic 両ドライバ名候補が popup に並び、手動 override テキスト入力で descriptor が更新される
  - _Requirements: 7.5, 12.4_
  - _Boundary: Editor/Inspector_

- [x] 9.2 `PhonemeEntryListView` を実装する（多態リスト + 型セレクタ + reorderable）
  - UI Toolkit `ListView` を使用し、`makeItem` で row container、`bindItem` で `managedReferenceFullTypename` を判定して BlendShape 形式 / AnimationClip 形式の row を切替（**12.3**）
  - Add ボタンは `AdvancedDropdown` または GenericMenu で 2 種類の派生型を提示し、選択時 `managedReferenceValue = new BlendShapePhonemeEntry()` 等を代入 → `ApplyModifiedProperties`（コア `AdapterBindingsListView` の Add ドロップダウン手法を踏襲）
  - Remove / Reorder は ListView 標準機能を使用
  - 未設定 `BlendShapeName` は HelpBox で警告表示する（**12.6**）
  - 観測可能完了条件: Inspector で `_phonemeEntries` を編集できる Sample Profile を開いた際、BlendShape 形式 / AnimationClip 形式の混在 row が ListView に表示され、Add / Remove / Reorder と型セレクタが動作する
  - _Requirements: 12.3, 12.6_

- [x] 9.3 `ULipSyncAdapterBindingDrawer` を統合する
  - `[CustomPropertyDrawer(typeof(ULipSyncAdapterBinding))]` を `Editor/Inspector/ULipSyncAdapterBindingDrawer.cs` に実装し、`CreatePropertyGUI(SerializedProperty property)` を override する
  - VisualElement ツリー: Slug 行（`PropertyField`）/ `DeviceDescriptorPopup` / Analyzer Profile `ObjectField`（未指定時「パッケージ同梱既定」プレースホルダ）/ `PhonemeEntryListView` / `_maxWeightScale` 行を配置する
  - 既存 `OscReceiverAdapterBindingDrawer` の折りたたみ・余白・Validation 配置と整合させる（**12.7**）
  - 観測可能完了条件: Inspector に `ULipSyncAdapterBinding` を含む `FacialCharacterProfileSO._adapterBindings` を表示すると、Drawer が UI Toolkit で構築された各セクションを描画し、編集後に `ApplyModifiedProperties` 経由で値が round-trip 保存される
  - _Requirements: 12.1, 12.2, 12.5, 12.7_

---

## Phase 4: Samples 二重管理

- [x] 10. `MicLipSyncDemo` Sample を Samples~ と Assets/Samples 双方に配置する
- [x] 10.1 `Samples~/MicLipSyncDemo/` を canonical サンプルとして整備する
  - `Samples~/MicLipSyncDemo/Scenes/MicLipSyncDemo.unity`（`Character` 空 GameObject、uLipSync 系コンポーネントなし）/ `Profiles/MicLipSyncDemoProfile.asset`（`ULipSyncAdapterBinding` を inline serialized で配線済み、A/I/U/E/O 等の音素を BlendShape 形式エントリで設定）/ `README.md`（自前モデル差し込み手順）を配置する
  - `package.json` の `samples` 配列に `MicLipSyncDemo` を登録する
  - 観測可能完了条件: Package Manager から `MicLipSyncDemo` を Import し、Scene 再生時に `ULipSyncAdapterBinding` が動的に uLipSync 系コンポーネントを AddComponent することが目視確認できる（Prefab-Clean Contract、**13.3**）
  - _Requirements: 13.1, 13.2, 13.3_

- [x] 10.2 (P) ~~`FacialControl/Assets/Samples/com.hidano.facialcontrol.lipsync/MicLipSyncDemo/` ミラーを配置する~~ **方針変更により廃止**
  - **方針変更（preview.1 検証時）**: 二重管理が紛らわしいため `Assets/Samples/com.hidano.facialcontrol.lipsync/` 配下を全削除。`Samples~/` のみを正本とし、dev での動作確認も Package Manager の Sample Import 機能経由で `Assets/Samples/FacialControl uLipSync Adapter/<version>/<sampleName>/` へ展開して行う方針へ変更
  - 関連: パッケージ README の「Samples 構成」節、CLAUDE.md の二重管理ルール記述（後者は本変更を受けて更新検討要）
  - _Requirements: 13.6_
  - _Boundary: Assets/Samples_

- [x] 10.3 (P) `Samples~/AnimationClipLipSyncDemo/`（任意）を整備する
  - 任意 Sample として AnimationClip 形式と BlendShape 形式の混在エントリを使う `FacialCharacterProfileSO` と Scene を配置する
  - `package.json` の `samples` 配列に登録する
  - 観測可能完了条件: Package Manager から Import 可能、混在エントリを使った時 0 サンプリング動作が目視確認できる
  - _Requirements: 13.4_
  - _Boundary: Samples~/AnimationClipLipSyncDemo_

---

## Phase 5: ドキュメント

- [x] 11. README / CHANGELOG / Documentation~ を整備する
- [x] 11.1 `README.md` をパッケージルートに配置する
  - インストール手順、最小サンプル手順（`MicLipSyncDemo` Import）、Windows 限定スコープ、コア（`com.hidano.facialcontrol`）と `com.hidano.ulipsync-asio` への依存、preview 期の破壊変更可能性、Hot Path GC 0 byte 方針（**11.6**）、`Samples~/` ↔ `Assets/Samples/` 二重管理ルールを日本語で記述する
  - 観測可能完了条件: Package Manager で `com.hidano.facialcontrol.lipsync` を選択した際に README が日本語で表示される
  - _Requirements: 1.6, 11.6, 13.6, 14.6, 14.7_

- [x] 11.2 (P) `CHANGELOG.md` を Keep a Changelog 形式で初版エントリを配置する
  - `Unreleased` または初版バージョン（例: `0.1.0-preview.1`）の Added エントリで主要機能（パッケージ scaffolding / ULipSyncAdapterBinding / Mic & ASIO 自動判定 / Hot-swap / Multi-character / GC 0 byte / Editor PropertyDrawer / MicLipSyncDemo）を列挙する
  - 観測可能完了条件: Keep a Changelog 形式に従ったヘッダ・セクション・日付が日本語で記述されている
  - _Requirements: 14.6, 14.7_
  - _Boundary: Documentation_

- [x] 11.3 (P) `Documentation~/usage.md` を作成し AnimationClip time-0 サンプリングと ASIO 利用手順を記述する
  - AnimationClip 形式エントリは **time-0 のみサンプリング**され、時間軸再生は行われないこと（0.5 秒等の口開け遷移は再生されない、**4.6**）を明記する
  - ASIO 利用手順（プロジェクト固有ドライバ名・ASIO 4 ALL 等への対応）を明記し、ASIO Sample Scene を同梱しない理由を説明する（**13.5**）
  - 観測可能完了条件: `Documentation~/usage.md` が日本語で配置され、time-0 制約と ASIO 手順の 2 章が読める状態
  - _Requirements: 4.6, 13.5, 14.6, 14.7_
  - _Boundary: Documentation_

- [x] 11.4 (P) `Documentation~/migration-guide.md` を作成し旧手結線からの移行 4 ステップを記述する
  - 旧 `uLipSyncBlendShape` 手結線パターンからの移行: (1) 旧コンポーネントを Prefab から取り外す → (2) `FacialCharacterProfileSO._adapterBindings` に `ULipSyncAdapterBinding` を追加 → (3) 音素エントリを Inspector で配線 → (4) Scene を再生して動作確認、の 4 ステップを日本語で記述する
  - 観測可能完了条件: 4 ステップそれぞれにスクリーンショット手順想定の plain text 説明が記述されている（スクリーンショット添付は preview.2 以降）
  - _Requirements: 14.6, 14.7_
  - _Boundary: Documentation_

---

## Phase 6: PlayMode 統合と性能テスト

- [x] 12. PlayMode テストスイートを完成させる
- [x] 12.1 `ULipSyncAdapterBindingLifecycleTests` を完成させる（OnStart 失敗経路を含む）
  - `OnStart_UnresolvedDevice_LogsErrorAndDoesNotRegister`（9.1, 9.2: OS 既定への自動フォールバック禁止）
  - `OnStart_AnalyzerProfileMissing_LogsErrorAndRollsBack`（6.6）
  - `OnStart_DuplicateBindingOnSameCharacter_LogsErrorAndSkips`（10.3）
  - `Dispose_AfterStart_RemovesAllAddedComponents`（2.4, 6.5）
  - 既存 `OscReceiverAdapterBindingTests` のライフサイクル / Registry / 例外時安全停止項目と 1:1 対応する surface を確保する（**14.3**）
  - 観測可能完了条件: 全シナリオが PlayMode で green、Console に期待通りの LogError / LogWarning が出力される
  - _Requirements: 2.4, 6.5, 6.6, 9.1, 9.2, 10.3, 14.2, 14.3, 14.5_
  - _Boundary: Tests/PlayMode/Lifecycle_

- [x] 12.2 (P) `TenCharacterIsolationTests` を PlayMode に追加する
  - 同時に 10 個の `FacialCharacter` インスタンスを生成し、それぞれに独立した `ULipSyncAdapterBinding` と異なる `DeviceDescriptor` を割り当てる
  - 1 個の binding に対し `SwapDevice` を呼んでも、他 9 個の binding の入力チェーン（Provider / LipSyncInputSource / Registry 登録）が一切影響を受けないことを assert する（**8.4, 10.1**）
  - 観測可能完了条件: 10 体同時動作下で 1 件 swap がフレーム落ちなく完了し、他 9 体の Provider 出力が継続する
  - _Requirements: 8.4, 10.1, 10.2, 10.4, 14.2_
  - _Boundary: Tests/PlayMode/MultiCharacter_

- [x] 12.3 (P) end-to-end GC 0 byte PlayMode 計測を追加する
  - `Tests/PlayMode/Performance/EndToEndGcAllocationTests.cs` を新規作成し、UnityEvent invoke を含む実 `uLipSync.uLipSync` → `ULipSyncProvider` → `LipSyncInputSource` → `LayerInputSourceAggregator` 経路を 1000 フレーム回した際の GC 差分が 0 byte（または Risk-1 micro-alloc 範囲内）であることを assert する
  - 計測は `GC.GetTotalAllocatedBytes(true)` 差分パターン
  - 観測可能完了条件: end-to-end 経路の GC 計測テストが green、Risk-1 が実質影響なしであると確認できる
  - _Requirements: 11.1, 11.2, 11.3, 11.4, 14.2_
  - _Boundary: Tests/PlayMode/Performance_

- [x] 12.4 CI 設定に EditMode + PlayMode テストランを組み込む
  - 既存 GitHub Actions セルフホストランナー設定に `com.hidano.facialcontrol.lipsync` の EditMode + PlayMode 両テスト実行を追加し、失敗ゼロ必須要件を反映する
  - 観測可能完了条件: PR 上で本パッケージのテストがランされ、結果が `test-results/` に出力される
  - _Requirements: 14.4_
