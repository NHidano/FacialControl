# Implementation Plan

破壊的変更（旧 `LipSyncInputSource` / `lipsync` Layer 直書き経路の撤去）を含むため、Phase 単位で影響範囲を明示する。各 leaf は TDD (Red → Green → Refactor を 1 leaf 内で完結) を厳守し、観察可能な完了条件としてテストファイルパス + Green 状態を明記する。

**Phase 別の破壊的変更影響範囲**:
- **Phase 1 (Foundation)**: Domain に新規定数追加のみ。既存コード破壊なし。
- **Phase 2 (Provider API 拡張)**: `ULipSyncProvider` に per-phoneme 公開 API を追加。既存 `GetLipSyncValues` は維持。破壊なし。
- **Phase 3 (新 InputSource 追加)**: 新型 `LipSyncPhonemeOverlayInputSource` 追加のみ。既存 `LipSyncInputSource` はまだ残置。
- **Phase 4 (Binding 出力経路再配線)**: `ULipSyncAdapterBinding` の `OnStart` で旧 `LipSyncInputSource` 登録を撤去し、phoneme overlay 登録へ切替。既存依存テストの並行修正が必要。
- **Phase 5 (Default Layer Inputs 多重化 interface)**: `IAdapterBindingDefaultLayer.DefaultLayerInputSourceId` の単一 id 前提を `IAdapterBindingDefaultLayerInputs` で多重化に拡張。Editor の Layer 自動補充ロジックを破壊的に変更する。
- **Phase 6 (旧 LipSyncInputSource 撤去)**: クラス削除 + 参照箇所の物理撤去。CI Grep ガードを追加。
- **Phase 7 (Editor UI 拡張)**: Inspector の Slots Init Button + Expression Row Foldout 追加。既存 UI への non-breaking 追加。
- **Phase 8 (Sample asset migration)**: `MicLipSyncDemoProfile` の `slots` / `expressions[*].overlays` / Layer 構成を新スキーマへ移行。Asset / JSON 双方を更新。
- **Phase 9 (PlayMode 統合 / Performance / Documentation)**: E2E 統合検証 + zero-GC 性能テスト + Documentation~ 移行ガイド。

---

- [x] 1. Foundation: 予約 phoneme slot 名定数と Provider テスト下準備
- [x] 1.1 Phoneme slot 予約名定数を Domain に追加
  - 失敗テストを `Packages/com.hidano.facialcontrol/Tests/EditMode/Domain/PhonemeOverlaySlotsTests.cs` に追加: `IsReserved_LowercaseFiveSlots_ReturnsTrue` / `IsReserved_UppercaseA_ReturnsFalse` / `MapReservedToPhonemeId_LowercaseA_ReturnsUppercaseA` / `ReservedNames_Length_IsFive`
  - Domain.Models 配下に `PhonemeOverlaySlots` 静的クラスを追加 (定数 `A/I/U/E/O`、`ReadOnlySpan<string> ReservedNames`、`IsReserved`、`MapReservedToPhonemeId`)
  - `StringComparison.Ordinal` で casing 違いを別 slot として扱う (canonicalize しない)
  - 観察可能な完了条件: 上記テストファイルが Green 状態で EditMode で全件通過
  - _Requirements: 1.1, 1.4_
  - _Boundary: Domain.Models.PhonemeOverlaySlots_

- [x] 1.2 (P) `FakeULipSyncProvider` test double を Shared に追加
  - 失敗テストを `Packages/com.hidano.facialcontrol.lipsync/Tests/EditMode/Adapters/FakeULipSyncProviderTests.cs` に追加: `TryComposePhonemeWeights_WhenScripted_ReturnsScriptedWeights` / `IsActive_WhenStopped_ReturnsFalse`
  - `Packages/com.hidano.facialcontrol.lipsync/Tests/Shared/FakeULipSyncProvider.cs` を新設し、`TryComposePhonemeWeights(phonemeId, output)` の戻り値・書込み内容・`IsActive` を test から制御可能にする
  - per-phoneme weight をテストドライバから注入する API (`SetPhonemeWeights`, `SetActive`) を露出
  - 観察可能な完了条件: 上記テストファイルが Green 状態。実 audio device 無しでも Provider 振る舞いをエミュレートできる
  - _Requirements: 9.5_
  - _Boundary: Adapters.LipSync.Test.Fakes_

- [x] 2. ULipSyncProvider: per-phoneme weight 公開 API 追加 (per-frame zero-GC 保証)
- [x] 2.1 per-frame snapshot 状態と `EnsureCurrentFrameComposed` 内部メソッドを追加
  - `Packages/com.hidano.facialcontrol.lipsync/Tests/EditMode/Adapters/ULipSyncProviderTests.cs` に失敗テスト追加: `TryComposePhonemeWeights_CalledFiveTimesPerFrame_DoesNotAdvanceDtTwice` / `TryComposePhonemeWeights_AfterFrameTick_RecomposesOnce`
  - `ULipSyncProvider` 内部に `_currentFrameStamp` (double) を追加し、`EnsureCurrentFrameComposed` で per-frame 1 回のみ SmoothDamp / sum=1 正規化 / volume 更新を実行
  - 既存 `GetLipSyncValues` も `EnsureCurrentFrameComposed` 経由で呼び出すよう内部リファクタ (外部挙動は同一)
  - 観察可能な完了条件: 5 回連続呼び出しで dt が 1 frame 分しか進まないことが Green
  - _Requirements: 4.3, 8.3_
  - _Boundary: Adapters.LipSync.ULipSyncProvider_

- [x] 2.2 `TryGetPhonemeIndex` / `TryComposePhonemeWeights` / `GetPhonemeContributeMask` 公開 API を実装
  - 失敗テスト追加: `TryGetPhonemeIndex_KnownPhonemeId_ReturnsTrueWithIndex` / `TryGetPhonemeIndex_UnknownPhonemeId_ReturnsFalse` / `TryComposePhonemeWeights_KnownPhonemeId_WritesExpectedWeights` / `TryComposePhonemeWeights_UnknownPhonemeId_ReturnsFalse` / `GetPhonemeContributeMask_KnownPhonemeId_ReturnsNonNullBitArray`
  - `ULipSyncProvider` に上記 3 メソッドを実装。`TryComposePhonemeWeights` は per-phoneme の `_phonemeSmoothedWeights[idx] * _smoothedVolume * _snapshotWeights[idx][i]` を `output[i]` へ書込む
  - 未一致 phonemeId は `Debug.LogWarning` を 1 度だけ出力し、以降は warning 抑止フラグで silenced
  - 観察可能な完了条件: 上記テスト群が Green。`Packages/com.hidano.facialcontrol.lipsync/Tests/EditMode/Performance/ULipSyncProviderAllocationTests.cs` の既存 zero-alloc テストも引き続き Green
  - _Requirements: 3.1, 4.3, 8.3_
  - _Boundary: Adapters.LipSync.ULipSyncProvider_

- [x] 3. 新 InputSource: `LipSyncPhonemeOverlayInputSource` 追加
- [x] 3.1 `LipSyncPhonemeOverlayInputSource` を Red-Green-Refactor で実装
  - 失敗テスト追加: `Packages/com.hidano.facialcontrol.lipsync/Tests/EditMode/Adapters/LipSyncPhonemeOverlayInputSourceTests.cs` に `Constructor_NullProvider_Throws` / `Constructor_UnknownPhonemeId_LogsWarningOnce` / `TryWriteValues_PhonemeNotRegistered_ReturnsFalse` / `TryWriteValues_ProviderActiveWithVolume_WritesScaledWeights` / `TryWriteValues_ProviderSilent_ReturnsFalse` / `ContributeMask_AfterConstruction_MatchesProviderMask`
  - 新クラス `LipSyncPhonemeOverlayInputSource : ValueProviderInputSourceBase` を `Packages/com.hidano.facialcontrol.lipsync/Runtime/Adapters/LipSyncPhonemeOverlayInputSource.cs` に追加
  - ctor で `_scratch = new float[blendShapeCount]` を 1 度だけ確保、`_providerPhonemeIndex` を `TryGetPhonemeIndex` で解決し未一致は -1 センチネル
  - `TryWriteValues(Span<float> output)` は `Array.Clear(_scratch, 0, _scratch.Length)` + provider 呼び出し + sum が `SilenceThreshold (1e-4f)` 未満なら false
  - `FakeULipSyncProvider` を使い、実 audio device 無しで全テストパス
  - 観察可能な完了条件: 上記 6 テストが Green、`_scratch` 確保が ctor 内 1 回のみであること (テスト内で確認)
  - _Requirements: 2.4, 3.1, 3.4, 3.5, 4.4, 8.3_
  - _Boundary: Adapters.LipSync.LipSyncPhonemeOverlayInputSource_

- [x] 3.2 (P) `LipSyncPhonemeOverlayInputSource` の per-frame zero-allocation 検証テストを追加
  - 失敗テスト追加: `Packages/com.hidano.facialcontrol.lipsync/Tests/EditMode/Performance/LipSyncPhonemeOverlayInputSourceAllocationTests.cs` に `TryWriteValues_RepeatedCalls_AllocatesZeroBytes`
  - Unity `NUnit` の `Allocator` 計測 (既存 `ULipSyncProviderAllocationTests` パターンを踏襲) で 1000 frame 連続呼び出し時に GC alloc がゼロであること
  - 観察可能な完了条件: テストが Green。失敗時 (将来の regression 時) は明確に失敗する
  - _Requirements: 8.1, 8.3, 8.5_
  - _Boundary: Adapters.LipSync.LipSyncPhonemeOverlayInputSource_
  - _Depends: 3.1_

- [x] 4. ULipSyncAdapterBinding: 出力経路を Overlay レイヤーへ再配線 (旧 LipSyncInputSource 登録撤去)
- [x] 4.1 `OnStart` の出力経路再配線: 旧経路撤去 + phoneme overlay 登録 loop 追加
  - 失敗テスト追加: `Packages/com.hidano.facialcontrol.lipsync/Tests/PlayMode/Lifecycle/ULipSyncAdapterBindingPhonemeOverlayTests.cs` に `OnStart_WithReservedSlotsDeclared_RegistersLipSyncPhonemeOverlayInputSources` / `OnStart_NoReservedSlotsDeclared_LogsWarningAndSkips` / `OnStart_PartialSlotsDeclared_RegistersOnlyDeclaredSlots` / `OnStart_DoesNotRegisterLegacyLipSyncInputSource` / `Dispose_UnregistersAllPhonemeOverlaySlots`
  - `ULipSyncAdapterBinding.OnStart` で `LipSyncInputSource` 生成 + Register 経路を撤去
  - `PhonemeOverlaySlots.ReservedNames` を走査し、`Slots` に declared かつ `_provider.TryGetPhonemeIndex` が成功する slot にのみ `LipSyncPhonemeOverlayInputSource` を生成して `InputSourceRegistry.Register(slug, $"lipsync-overlay:{slot}", source)` で登録
  - `_registeredPhonemeSlots` を `List<string>` で保持し、`Dispose` で逆順 Unregister
  - declared 0 件時は `Debug.LogWarning` を 1 回出力 (Requirement 7.2 互換性チェック)
  - 観察可能な完了条件: 5 テストすべて Green。`Tests/PlayMode/Lifecycle/ULipSyncAdapterBindingLifecycleTests.cs` の既存テストも Green を維持 (回帰なし)
  - _Requirements: 3.1, 3.2, 3.6, 7.1, 7.2, 7.3_
  - _Boundary: Adapters.LipSync.ULipSyncAdapterBinding_
  - _Depends: 1.1, 3.1_

- [x] 4.2 二重書き検知 warning と整合性チェックを追加
  - 失敗テスト追加: `Packages/com.hidano.facialcontrol.lipsync/Tests/EditMode/Adapters/ULipSyncAdapterBindingConflictDetectionTests.cs` に `OnStart_LegacyLipSyncSourceAlsoRegistered_LogsConflictWarningOnce`
  - `OnStart` で `InputSourceRegistry` 内の同 layer に旧 slug `ulipsync` が残存している場合 (外部 binding が登録済み等)、`Debug.LogWarning` を 1 回出力し migration step を案内
  - 観察可能な完了条件: テストが Green。`LogAssert.Expect` で warning メッセージ内容を検証
  - _Requirements: 7.4_
  - _Boundary: Adapters.LipSync.ULipSyncAdapterBinding_

- [x] 5. Default Layer Inputs 多重化 interface 導入 (破壊的変更: Layer 自動補充ロジック)
- [x] 5.1 新 interface `IAdapterBindingDefaultLayerInputs` を Domain に追加
  - 失敗テスト追加: `Packages/com.hidano.facialcontrol/Tests/EditMode/Domain/IAdapterBindingDefaultLayerInputsContractTests.cs` に `GetDefaultLayerInputSources_OverlayLayer_ReturnsPhonemeSlotIds` (ダミー実装でシグネチャ契約のみ検証)
  - `Packages/com.hidano.facialcontrol/Runtime/Domain/Adapters/IAdapterBindingDefaultLayerInputs.cs` を追加し `IEnumerable<(string id, float weight)> GetDefaultLayerInputSources(string layerName)` を定義
  - 既存 `IAdapterBindingDefaultLayer.DefaultLayerInputSourceId` は維持 (deprecation コメント追加のみ)
  - 観察可能な完了条件: interface ファイルが asmdef 配下に追加されコンパイル通過、契約テスト Green
  - _Requirements: 3.6, 7.1, 7.3_
  - _Boundary: Domain.Adapters.IAdapterBindingDefaultLayerInputs_

- [x] 5.2 `ULipSyncAdapterBinding` で `IAdapterBindingDefaultLayerInputs` を実装
  - 失敗テスト追加: 5.1 と同テストファイルに `ULipSyncAdapterBinding_OverlayLayer_ReturnsLipSyncOverlaySlotIds` を追加し、`overlay` layer 要求時に `lipsync-overlay:{a,i,u,e,o}` 5 件と weight=1.0 を返すこと検証
  - `ULipSyncAdapterBinding` に interface 実装を追加し、`PhonemeOverlaySlots.ReservedNames` を元に `(id, weight)` ペアを yield return
  - 観察可能な完了条件: テスト Green。`ULipSyncAdapterBinding` の interface 実装が cast 可能
  - _Requirements: 3.6, 7.1, 7.3_
  - _Boundary: Adapters.LipSync.ULipSyncAdapterBinding_
  - _Depends: 5.1_

- [x] 5.3 Editor 側 Layer 自動補充ロジックを多重化 interface へ対応
  - 失敗テスト追加: `Packages/com.hidano.facialcontrol/Tests/EditMode/Editor/AdapterBindingsListViewDefaultLayerInputsTests.cs` に `AutoFillDefaultInputSources_BindingImplementsMultipleInputs_AddsAllIdsToLayer`
  - `Packages/com.hidano.facialcontrol/Editor/Inspector/AdapterBindings/AdapterBindingsListView.cs` の自動 Layer 補充ロジックで、`IAdapterBindingDefaultLayerInputs` が cast 成功する場合は複数 id を Layer の `inputSources` 配列に追加するように分岐
  - 既存 `IAdapterBindingDefaultLayer` 経路は fallback として維持
  - 観察可能な完了条件: テスト Green。既存 `AdapterBindingsListView` 関連テストも Green を維持
  - _Requirements: 7.1, 7.3_
  - _Boundary: Editor.Inspector.AdapterBindings_
  - _Depends: 5.1_

- [x] 6. 旧 LipSyncInputSource クラスおよび参照箇所の撤去
- [x] 6.1 `LipSyncInputSource` クラスと関連テストを物理削除
  - `Packages/com.hidano.facialcontrol/Runtime/Adapters/InputSources/LipSyncInputSource.cs` を削除 (`.meta` 同時削除)
  - 同クラスを参照する既存テスト (Tests/EditMode 配下) を grep で洗い出し、`LipSyncPhonemeOverlayInputSource` ベースのテストへ書き換え or 削除
  - 観察可能な完了条件: Unity Editor リコンパイルが成功し、`LipSyncInputSource` 名のシンボルが Package 内に残らないこと
  - _Requirements: 3.6_
  - _Boundary: Adapters.Core.InputSources.LipSyncInputSource_
  - _Depends: 4.1_

- [x] 6.2 (P) CI Grep ガードを追加し、`LipSyncInputSource` 残存を検知
  - 失敗テスト追加: `Packages/com.hidano.facialcontrol.lipsync/Tests/EditMode/Adapters/LegacyLipSyncInputSourceRemovalGuardTests.cs` に `Codebase_DoesNotContainLegacyLipSyncInputSourceSymbol` を追加し、`AppDomain.CurrentDomain.GetAssemblies()` から `LipSyncInputSource` 型名が解決不可であることを検証
  - 観察可能な完了条件: テスト Green。将来 revive された場合は明確に失敗する
  - _Requirements: 3.6_
  - _Boundary: Tests.Guard_
  - _Depends: 6.1_

- [x] 7. OverlayInputSource を phoneme slot 向けに登録する中立 host 拡張
- [x] 7.1 中立 host (`AdapterBindingHost` 等) に phoneme slot 用 `OverlayInputSource` 自動登録経路を追加
  - 失敗テスト追加: `Packages/com.hidano.facialcontrol/Tests/EditMode/Adapters/OverlayInputSourcePhonemeRegistrationTests.cs` に `Initialize_PhonemeSlotsDeclared_RegistersOverlayInputSourcePerSlot` / `Initialize_PhonemeSlotNotDeclared_DoesNotRegister` / `Initialize_NonPhonemeSlot_NotAffected`
  - `FacialControllerLifetimeScope` または同等の中立 host の `Slots` 走査 loop に `PhonemeOverlaySlots.IsReserved(slot)` ブランチを追加し、phoneme slot に対しては `OverlayInputSource` を `overlay:{slot}` slug で登録
  - 重複登録を防ぐ `HashSet<string> registeredSlots` で idempotent 化
  - 観察可能な完了条件: 3 テストすべて Green。`OverlayInputSource` 内部コードは変更しないこと
  - _Requirements: 2.1, 2.2, 2.3, 2.5, 4.1, 4.2, 4.5_
  - _Boundary: Adapters.Core.OverlayInputSource registration host_

- [x] 7.2 (P) JSON / SO round-trip テストで phoneme overlay binding の永続化を検証
  - 失敗テスト追加: `Packages/com.hidano.facialcontrol/Tests/EditMode/Adapters/FacialCharacterProfileConverterPhonemeOverlayTests.cs` に `RoundTrip_PhonemeOverlayBinding_PreservesSlotSuppressSnapshot` / `RoundTrip_PhonemeOverlaySuppress_PreservesFlag` / `Parse_UnknownPhonemeSlotReference_EmitsInvalidSlotReference`
  - `SystemTextJsonParser` と `FacialCharacterProfileConverter` の既存経路で phoneme `OverlaySlotBinding` が `slot` / `suppress` / `snapshot` を保持して round-trip すること検証
  - 未宣言 phoneme slot 参照時に `InvalidSlotReference` 診断が emit されること検証
  - 観察可能な完了条件: 3 テスト Green。既存 JSON スキーマバージョンは bump しないこと
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 9.3, 9.4_
  - _Boundary: Adapters.ScriptableObject.FacialCharacterProfileConverter_

- [x] 8. Editor: Inspector UI 拡張 (Slots Init Button + Expression Row Foldout)
- [x] 8.1 Slots セクションに "Phoneme slots を初期化" Button を追加
  - 失敗テスト追加: `Packages/com.hidano.facialcontrol/Tests/EditMode/Editor/FacialCharacterProfileSOInspectorPhonemeSlotsInitTests.cs` に `Click_PhonemeSlotsInitButton_AddsMissingReservedSlots` / `Click_PhonemeSlotsInitButton_DoesNotDuplicateExistingSlots` / `Click_PhonemeSlotsInitButton_DoesNotRemoveCustomSlots`
  - `FacialCharacterProfileSOInspector` の Slots Foldout 内に Button (name `slots-init-phoneme-button`, label `"Phoneme slots を初期化 (a/i/u/e/o)"`) を UI Toolkit で追加
  - クリック時は `_slots` SerializedProperty に未存在の予約 phoneme 名のみを末尾追加し、既存 slot は不変
  - 観察可能な完了条件: 3 テスト Green。Inspector を開いてボタンクリックで `Slots` に a/i/u/e/o が追加される
  - _Requirements: 1.2, 1.3, 6.6_
  - _Boundary: Editor.Inspector.FacialCharacterProfileSOInspector_

- [x] 8.2 Expression Row に phoneme overlay Foldout (折り畳み + 1 行サマリ + 5 行展開 UI) を追加
  - 失敗テスト追加: `Packages/com.hidano.facialcontrol/Tests/EditMode/Editor/FacialCharacterProfileSOInspectorPhonemeOverlayFoldoutTests.cs` に `Foldout_Collapsed_ShowsSummaryWithDeclaredCount` / `Foldout_Expanded_ShowsPerSlotEditorForDeclaredSlots` / `Foldout_Expanded_ShowsHelpBoxForUndeclaredSlot` / `Foldout_SummaryLabel_ReflectsOverrideAndSuppressCounts` / `Foldout_OnSlotBindingEdit_TriggersCachedSnapshotBake`
  - Expression Row の `expression-row-overlays-section` 配下に Foldout (name `expression-row-phoneme-overlays-foldout`) を追加
  - 折り畳み時: Label (name `expression-row-phoneme-overlays-summary`) でテキスト `"{declared}/5 declared (override={N}, suppress={M})"` を表示
  - 展開時: `PhonemeOverlaySlots.ReservedNames` 走査で `Slots` declared な slot のみ既存 `BuildOverlayRow` パターンで AnimationClip Field / Suppress Toggle / cachedSnapshot プレビューを描画
  - 未宣言 slot は HelpBox で「Slots セクションで '{slot}' を追加すると編集できます」案内
  - 編集時に `FacialCharacterProfileExporter` の cachedSnapshot bake 経路を呼ぶ既存 hook を流用
  - 観察可能な完了条件: 5 テスト Green。Inspector で Smile Expression の Foldout を展開すると 5 slot 編集 UI が出る
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6_
  - _Boundary: Editor.Inspector.FacialCharacterProfileSOInspector_
  - _Depends: 8.1_

- [x] 9. Sample asset migration: `MicLipSyncDemoProfile` を新スキーマへ
- [x] 9.1 `MicLipSyncDemoProfile/profile.json` を新スキーマへ移行
  - 失敗テスト追加: `Packages/com.hidano.facialcontrol.lipsync/Tests/EditMode/Adapters/MicLipSyncDemoProfileMigrationTests.cs` に `Load_MicLipSyncDemoProfileJson_ContainsFivePhonemeSlots` / `Load_MicLipSyncDemoProfileJson_OverlayLayerHasLipSyncOverlayIds` / `Load_MicLipSyncDemoProfileJson_DoesNotReferenceLegacyUlipsyncSlug`
  - `FacialControl/Assets/StreamingAssets/FacialControl/MicLipSyncDemoProfile/profile.json` の `slots` に `["a","i","u","e","o"]` を追加
  - `layers` から旧 `lipsync` layer の `ulipsync` inputSource を撤去し、`overlay` layer の `inputSources` に `overlay:{slot}` + `lipsync-overlay:{slot}` 5 組を追加
  - Smile Expression に `"あ"` slot の override snapshot サンプルを 1 件追加 (移行例として)
  - 観察可能な完了条件: 3 テスト Green。Editor Play モードで `MicLipSyncDemoProfile` が新経路でロード可能
  - _Requirements: 5.1, 5.2, 7.3, 10.2_
  - _Boundary: Sample.MicLipSyncDemoProfile_
  - _Depends: 4.1, 5.2_

- [x] 9.2 (P) `MicLipSyncDemoProfile.asset` (ScriptableObject) を JSON と同期更新
  - 失敗テスト追加: `Packages/com.hidano.facialcontrol.lipsync/Tests/EditMode/Adapters/MicLipSyncDemoProfileAssetMigrationTests.cs` に `Asset_MicLipSyncDemoProfile_HasFivePhonemeSlotsDeclared` / `Asset_SmileExpression_HasPhonemeOverlayBinding`
  - 該当 `.asset` を Inspector の Slots Init Button + Expression Row Foldout 経由で更新し、`a/i/u/e/o` を Slots に追加、Smile の "あ" slot に override snapshot を bake
  - `Packages/com.hidano.facialcontrol.lipsync/Samples~/` 配下に同等の sample profile が存在する場合は同期更新
  - 観察可能な完了条件: 2 テスト Green。SO と JSON が schema レベルで一致
  - _Requirements: 5.2, 7.3, 10.2_
  - _Boundary: Sample.MicLipSyncDemoProfile_
  - _Depends: 8.2_

- [x] 10. Integration: PlayMode 統合テスト (Expression 切替 / Override / Suppress / Default fallback)
- [x] 10.1 phoneme overlay 統合 PlayMode テストを追加
  - 失敗テスト追加: `Packages/com.hidano.facialcontrol.lipsync/Tests/PlayMode/Integration/PhonemeOverlayIntegrationTests.cs` に `ActiveExpressionWithOverride_PhonemeSlot_ProducesSnapshotInOneFrame` / `ActiveExpressionWithSuppress_PhonemeSlot_BlocksLipSyncOutput` / `NoActiveOverride_PhonemeSlot_DelegatesToLipSyncDefault` / `ExpressionSwitch_BetweenTwoOverrides_ReflectsNewSnapshotInOneFrame` / `BaseExpressionOnly_PhonemeSlot_UsesDefaultOverlaysThenLipSync`
  - `FakeULipSyncProvider` でリップシンク入力を制御し、`OverlayInputSource` + `LipSyncPhonemeOverlayInputSource` 並列登録下で precedence (Expression Override → Suppress → DefaultOverlays → LipSync default) を検証
  - 観察可能な完了条件: 5 テスト Green。1 frame 内で Override 切替が BlendShape 出力に反映
  - _Requirements: 2.2, 2.3, 2.4, 2.5, 2.6, 4.1, 4.2, 4.4, 9.1, 9.2, 9.5_
  - _Boundary: Integration.PhonemeOverlay_
  - _Depends: 4.1, 7.1, 9.1_

- [x] 11. Performance: 10 体 × 5 slot × 5 Expression override 構成で per-frame zero-GC 検証
- [x] 11.1 phoneme overlay aggregate 性能テストを追加
  - 失敗テスト追加: `Packages/com.hidano.facialcontrol.lipsync/Tests/PlayMode/Performance/PhonemeOverlayPerformanceTests.cs` に `TenCharacters_FiveSlotsEach_PerFrameZeroAllocation` / `SingleCharacter_FiveSlotsFiveOverrides_PerFrameZeroAllocation` / `ExpressionSwitch_PerFrameZeroAllocation`
  - Unity TestRunner の `Allocator` 計測で 1000 frame 連続実行時の GC alloc がゼロであること
  - `OverlayInputSource._resolvedBySlot` lookup O(1) が維持されることを実測
  - 観察可能な完了条件: 3 テスト Green。zero-GC contract が将来 regression したら明確に失敗
  - _Requirements: 4.3, 8.1, 8.2, 8.3, 8.4, 8.5_
  - _Boundary: Performance.PhonemeOverlay_
  - _Depends: 10.1_

- [x] 12. Documentation: 移行ガイドと予約名仕様の公開
- [x] 12.1 `phoneme-overlay-migration.md` を新設し migration-guide.md から参照
  - `Packages/com.hidano.facialcontrol.lipsync/Documentation~/phoneme-overlay-migration.md` を新設し以下を記載: (1) Slots 宣言手順、(2) Expression overlay 編集手順、(3) Layer.inputSources 多重化への移行、(4) 旧 `lipsync` Layer 互換動作と warning メッセージ解釈、(5) 5 予約名 (`a/i/u/e/o`) 一覧と custom phoneme set が out-of-scope であること、(6) Backlog S-17 / overlay-clip-redesign への back-link、(7) precedence 実装が design.md と乖離する場合の rationale 記述欄
  - `Packages/com.hidano.facialcontrol/Documentation~/migration-guide.md` (既存) に phoneme-overlay-migration.md へのリンクと preview 段階の破壊的変更注意書きを追記
  - 観察可能な完了条件: 両 Markdown ファイルが lint パス、README からリンク到達可能、Backlog S-17 と overlay-clip-redesign spec へのリンクが解決
  - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5_
  - _Boundary: Documentation~_

---

## Requirements Coverage Check

| Requirement | Covered By Tasks |
|-------------|------------------|
| 1.1 予約 slot 名 5 種 | 1.1 |
| 1.2 opt-in 既定 (Init Button) | 8.1 |
| 1.3 Slots 削除後の永続化 | 8.1 |
| 1.4 casing 違いは別 slot | 1.1 |
| 1.5 slot 未宣言は inactive | 7.1 |
| 2.1 Expression に 5 binding 追加可能 | 7.1, 10.1 |
| 2.2 Override active で snapshot 出力 | 10.1 |
| 2.3 Suppress active で false | 10.1 |
| 2.4 default fallback で LipSync 委譲 | 3.1, 10.1 |
| 2.5 binding 無し → default fallback | 10.1 |
| 2.6 suppress vs snapshot 同時時の優先 | 10.1 |
| 3.1 ULipSync 起動後 weight 公開 | 2.2, 3.1, 4.1 |
| 3.2 slot 宣言時 OverlayInputSource registered | 4.1, 7.1 |
| 3.3 Override / Suppress が LipSync preempt | 10.1 |
| 3.4 未宣言時 phoneme entry weight 出力 | 3.1 |
| 3.5 Binding 未起動時 mask 空 | 3.1 |
| 3.6 旧経路と新経路の二重書き禁止 | 4.1, 5.1, 6.1, 6.2 |
| 4.1 precedence 順序 | 7.1, 10.1 |
| 4.2 base 表情時 DefaultOverlays → LipSync | 10.1 |
| 4.3 per-frame zero-GC | 2.1, 2.2, 11.1 |
| 4.4 Override / Suppress 無し → false | 3.1, 10.1 |
| 4.5 `_resolvedBySlot` / SlotKey 再利用 | 7.1 |
| 5.1 JSON シリアライズ互換 | 7.2, 9.1 |
| 5.2 SO round-trip | 7.2, 9.1, 9.2 |
| 5.3 Exporter は phoneme slot 非特化 | 7.2 |
| 5.4 InvalidSlotReference 診断 | 7.2 |
| 5.5 スキーマバージョン bump なし | 7.2 |
| 6.1 Foldout 集約 | 8.2 |
| 6.2 1 行サマリ | 8.2 |
| 6.3 展開時 5 行 UI | 8.2 |
| 6.4 Slots 未宣言時は HelpBox | 8.2 |
| 6.5 binding 編集時 cachedSnapshot 自動 bake | 8.2 |
| 6.6 UI Toolkit | 8.1, 8.2 |
| 7.1 single source-of-truth 出力経路 | 4.1, 5.1, 5.2, 5.3 |
| 7.2 未宣言時は警告 + no-op | 4.1 |
| 7.3 opt-in 時 Overlay 経路統合 | 5.1, 5.2, 5.3, 9.1, 9.2 |
| 7.4 二重書き検知時 warning | 4.2 |
| 7.5 互換戦略 design 記録 | (design.md セクション 7 で完了) |
| 8.1 zero-GC 5 slot × 5 Expression | 3.2, 11.1 |
| 8.2 `_resolvedBySlot` O(1) | 11.1 |
| 8.3 起動後 zero alloc | 2.2, 3.2 |
| 8.4 10 体同時で aggregate zero alloc | 11.1 |
| 8.5 PlayMode performance test | 3.2, 11.1 |
| 9.1 EditMode 5 slot × 3 state | 3.1, 10.1 |
| 9.2 Expression 切替 1 フレーム内 | 10.1 |
| 9.3 JSON round-trip 検証 | 7.2 |
| 9.4 InvalidSlotReference 診断 | 7.2 |
| 9.5 E2E (stub provider) | 1.2, 10.1 |
| 9.6 命名規約 | (全テストファイル) |
| 10.1 Documentation~ 更新 | 12.1 |
| 10.2 移行手順 | 9.1, 12.1 |
| 10.3 予約名一覧 | 12.1 |
| 10.4 Backlog S-17 / overlay-clip-redesign 参照 | 12.1 |
| 10.5 実装と precedence の乖離記録欄 | 12.1 |
