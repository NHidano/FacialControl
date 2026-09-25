# Implementation Plan

## スコープ外メモ（実装中に再オープンしない）

- **bone-control 公開 API は変更禁止**: `IBonePoseProvider.SetActiveBonePose(in BonePose)` / `IBonePoseSource.GetActiveBonePose()` / `FacialController.SetActiveBonePose` / `BonePose` / `BonePoseEntry` のシグネチャは保持する。Phase 2 の hot-path ctor 追加は **internal 加算的拡張のみ**で、既存 public ctor を温存する。
- **layer-input-source-blending 公開 API は変更禁止**: `LayerInputSourceAggregator` / `IInputSource` / `ValueProviderInputSourceBase` / `InputSourceFactory.RegisterReserved<T>` / `InputSourceId` 規約 (`[a-zA-Z0-9_.-]{1,64}`) を保持。本 spec は予約 ID 配列に `analog-blendshape` / `analog-bonepose` を**追記**するのみ（既存 ID `osc` / `lipsync` / `controller-expr` / `keyboard-expr` / `input` は保持）。
- **input-binding-persistence 公開 API は変更禁止**: `InputBindingProfileSO` / `FacialInputBinder` / `InputSystemAdapter` には触れず、`AnalogInputBindingProfileSO` + `FacialAnalogInputBinder` として並走する別アセット・別 MonoBehaviour で実装する。`FacialControlDefaultActions.inputactions` の `Trigger1〜Trigger12` 離散スロットは**保持**し、アナログ用は新 ActionMap `Analog` を追加する。
- **BonePose 多重 provider のブレンド合成は preview.2 以降**: 本 spec は単一 active BonePose を組み立てる側のみに責任を持ち、`SetActiveBonePose` の呼出は per-frame 1 回。
- **視線追従の Vector3 ターゲット指定は preview.2 以降**: 本 spec は `BonePose` Euler 直接指定に閉じる。Phase 9 の optional に preview を切り出す候補のみを残す。
- **フル GUI のマッピングカーブ編集 Editor は preview.2 以降**: preview.1 は読取専用 Inspector + JSON Import/Export + Humanoid 自動割当ボタンに留める。
- **Timeline / AnimationClip からのアナログ値駆動はスコープ外**: 本 spec は InputSystem / OSC / ARKit の 3 経路のみ。

## 設計レビュー繰越事項（タスクに反映済み）

1. **R-2 BonePose hot-path ctor 追加**: `bone-control` の `BonePose` に `internal BonePose(string id, BonePoseEntry[] entries, bool skipValidation)` ctor を加算追加し、`AnalogBonePoseProvider` が `_entryBuffer` を共有して毎フレーム alloc=0 を達成する。既存 public ctor は温存。Phase 2 の独立 leaf として実装する（横断影響のため）。
2. **R-5 InputAction.ReadValue<T> per-frame alloc**: `InputActionAnalogSource` の `Tick` 内で `ReadValue<Vector2>` / `ReadValue<float>` を 1 回キャッシュし、`TryRead*` はフィールド参照のみに閉じる。Phase 3 の GC ゼロ検証で boxing 兆候を Profiler で検出し、必要なら `InputControl<Vector2>.ReadValue()` 直叩きに切替える。Phase 8.1 の GC テストで再確認する。
3. **ExecutionOrder の Binder = -50 採用**: `FacialController.LateUpdate` (order=0) より早く `FacialAnalogInputBinder.LateUpdate` を回し、同フレーム内で `SetActiveBonePose` → `BoneWriter.Apply` の順にする。Phase 6 で `[DefaultExecutionOrder(-50)]` を明示付与する。
4. **TransitionCurve の Custom カーブ再利用**: `AnalogMappingFunction.Curve` は `TransitionCurve` を直接保持し、評価は `TransitionCalculator.Evaluate(in TransitionCurve, float t)` に委譲する。Phase 1.2 で実装する。
5. **OscReceiver への加算 API**: `RegisterAnalogListener(string address, Action<float>)` / `UnregisterAnalogListener` を追加（既存 `_addressToIndex` ルーティングは保持し、`HandleOscMessage` 末尾に通知ループを 1 つ加算）。Phase 3.1 で実装する。

> **粒度方針**: 1 leaf 内で「失敗テスト先行 → 実装 → 緑化 → 必要なら最小 refactor」を完結させる。Red / Green を別 leaf に切らない。観測可能な完了条件（ファイルパス + テスト結果）を各 leaf 末尾に明記する。1 leaf = 1 commit 想定。

---

## Phase 1: Foundation — Domain 抽象一式

- [ ] 1.1 IAnalogInputSource 契約 + AnalogSample/Shape 補助型 + InputSourceId 予約 ID 追記 (P)
  - TDD: 失敗テスト先行 → 実装 → 緑化 を 1 leaf 内で完結させる
  - `IAnalogInputSource` インターフェースを Domain/Interfaces 配下に定義（`Id` / `IsValid` / `AxisCount` / `Tick(deltaTime)` / `TryReadScalar(out float)` / `TryReadVector2(out float, out float)` / `TryReadAxes(Span<float>)`）
  - `Id` の `[a-zA-Z0-9_.-]{1,64}` 規約整合、`IsValid==false` のとき `TryRead*` が false を返し output 不変、`TryReadAxes` の overlap-only 書込（output 短/長双方）をテスト
  - `AnalogInputShape` enum (Scalar / Vector2) を補助型として定義
  - `Domain/Models/InputSourceId.cs` の `ReservedIds` 配列に `"analog-blendshape"` と `"analog-bonepose"` を**追記**（既存 ID は保持）。`InputSourceId.Parse` で予約 ID 双方が解決でき、3rd-party の `x-` prefix が rejected されないことをテスト
  - Domain 層のため `UnityEngine.*` 不参照（`Unity.Collections` のみ）
  - 観測可能な完了条件: `Tests/EditMode/Domain/Interfaces/IAnalogInputSourceContractTests.cs` と `Tests/EditMode/Domain/Models/InputSourceIdAnalogReservationTests.cs` が Green、Domain asmdef が Engine 参照ゼロでビルド成功
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 3.8, 6.8, 9.2_
  - _Boundary: Domain.Interfaces.IAnalogInputSource, Domain.Models.AnalogInputShape, Domain.Models.InputSourceId_

- [ ] 1.2 AnalogMappingFunction 値型 + AnalogMappingEvaluator 静的サービス (P)
  - TDD: 失敗テスト先行 → 実装 → 緑化 を 1 leaf 内で完結させる
  - `AnalogMappingFunction` を Domain/Models 配下に readonly struct として定義（`DeadZone` / `Scale` / `Offset` / `Curve: TransitionCurve` / `Invert` / `Min` / `Max` + `Identity` static）。ctor で `min > max` → `ArgumentException` をテスト
  - `Identity` のデフォルト値 (deadZone=0, scale=1, offset=0, curve=Linear, invert=false, min=0, max=1) をテスト
  - `AnalogMappingEvaluator.Evaluate(in AnalogMappingFunction, float)` を Domain/Services 配下に static で実装し、適用順 `dead-zone(re-center) → scale → offset → curve → invert → clamp(min, max)` をユニットテストで個別段階ごとに検証
  - dead-zone 内で出力厳密ゼロ、`Curve==Linear` のとき `TransitionCalculator.Evaluate` 委譲、`Custom` カーブで `CurveKeyFrame[]` の Hermite 評価が既存 `TransitionCalculator` と bit-exact に一致することをテスト
  - hot path で alloc ゼロ（`Profiler.GetMonoUsedSizeLong` 差分 0）を EditMode test で確認
  - 観測可能な完了条件: `Tests/EditMode/Domain/Models/AnalogMappingFunctionTests.cs` と `Tests/EditMode/Domain/Services/AnalogMappingEvaluatorTests.cs` が Green
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 8.1_
  - _Boundary: Domain.Models.AnalogMappingFunction, Domain.Services.AnalogMappingEvaluator_

- [ ] 1.3 AnalogBindingEntry + AnalogInputBindingProfile + 補助 enum (P)
  - TDD: 失敗テスト先行 → 実装 → 緑化 を 1 leaf 内で完結させる
  - `AnalogBindingTargetKind` enum (`BlendShape=0`, `BonePose=1`)、`AnalogTargetAxis` enum (`X=0`, `Y=1`, `Z=2`) を Domain/Models に定義
  - `AnalogBindingEntry` readonly struct を定義（`SourceId` / `SourceAxis` / `TargetKind` / `TargetIdentifier` / `TargetAxis` / `Mapping`）。`SourceAxis < 0` で `ArgumentOutOfRangeException`、`TargetIdentifier` null/whitespace で `ArgumentException` をテスト
  - `AnalogInputBindingProfile` readonly struct を定義（`Version` / `ReadOnlyMemory<AnalogBindingEntry> Bindings`）。bindings 0 件で構築可能、防御的コピー（外部配列を後から書換えても profile 内部値が不変）をテスト
  - Domain 配置のため `UnityEngine.*` 不参照
  - 観測可能な完了条件: `Tests/EditMode/Domain/Models/AnalogBindingEntryTests.cs` と `Tests/EditMode/Domain/Models/AnalogInputBindingProfileTests.cs` が Green
  - _Requirements: 6.2, 6.3, 6.7_
  - _Boundary: Domain.Models.AnalogBindingEntry, Domain.Models.AnalogInputBindingProfile, Domain.Models.AnalogBindingTargetKind, Domain.Models.AnalogTargetAxis_

## Phase 2: bone-control core への hot-path ctor 加算

- [ ] 2.1 BonePose internal hot-path ctor 追加（skipValidation, alloc=0）
  - TDD: 失敗テスト先行 → 実装 → 緑化 を 1 leaf 内で完結させる
  - **横断影響のため独立 leaf**: `bone-control` の `Domain/Models/BonePose.cs` に `internal BonePose(string id, BonePoseEntry[] entries, bool skipValidation)` ctor を**加算追加**する。既存 public ctor (`BonePose(string id, BonePoseEntry[] entries)`) は**シグネチャ・挙動とも温存**し、その回帰を既存 `BonePoseTests` の無修正パスで保証
  - `skipValidation==true` のとき: 防御的コピー・boneName 重複チェックをスキップし、引数配列を直接 `Entries` の backing として保持する（呼出側が同一インスタンス再利用前提）
  - hot-path で `new BonePose(id, sharedBuffer, skipValidation:true)` を 10000 回回したときの GC alloc が 0 バイトであることを `Profiler.GetMonoUsedSizeLong` 差分テストで保証
  - InternalsVisibleTo 設定により `Hidano.FacialControl.Adapters` から internal ctor が見えることを確認（既存設定を流用、新規追加が必要なら `Domain/AssemblyInfo.cs` に追記）
  - 観測可能な完了条件: `Tests/EditMode/Domain/Models/BonePoseHotPathCtorTests.cs` が Green、既存 `BonePoseTests` が無修正で全件パス
  - _Requirements: 8.1, 8.2, 9.1_
  - _Boundary: Domain.Models.BonePose (internal ctor only)_

## Phase 3: Source Adapters

- [ ] 3.1 OscReceiver.RegisterAnalogListener API 追加 + OscFloatAnalogSource + ArKitOscAnalogSource
  - TDD: 失敗テスト先行 → 実装 → 緑化 を 1 leaf 内で完結させる
  - `com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/OscReceiver.cs` に `RegisterAnalogListener(string address, Action<float> listener)` / `UnregisterAnalogListener(string address, Action<float> listener)` を**加算追加**。既存 `_addressToIndex` ルーティングは保持し、`HandleOscMessage` 末尾に `_analogListeners[address]?.Invoke(value)` を発火するループを追加（既存 BlendShape index ルーティング後に通知）
  - `OscFloatAnalogSource` (`Adapters/InputSources/OscFloatAnalogSource.cs`): `IAnalogInputSource` を実装し、ctor で `(InputSourceId id, OscReceiver receiver, string address, float stalenessSeconds)` を受け取り `RegisterAnalogListener` で購読。受信スレッドからは `Volatile.Write(_pendingValue)`、`Tick` で `Volatile.Read` してキャッシュへ転写。`stalenessSeconds > 0` で最終受信から経過時 `IsValid=false`、0 で last-valid 永続
  - `ArKitOscAnalogSource` (`Adapters/InputSources/ArKitOscAnalogSource.cs`): N-axis source として 52ch を 1 インスタンスで公開。ctor で `(InputSourceId id, OscReceiver receiver, string[] arkitParameterNames, float stalenessSeconds)` を受け取り、内部で 52 個の `/ARKit/{name}` を `RegisterAnalogListener` で購読
  - PlayMode テスト: 既存 OSC テストパターン（受信スレッド → メインスレッド）に倣い、scalar 1 アドレス + ARKit 52ch 双方で値伝搬と staleness を検証。`Dispose` で listener 解除されることを確認
  - 観測可能な完了条件: `Tests/PlayMode/Adapters/OSC/OscReceiverAnalogListenerTests.cs` と `Tests/PlayMode/Adapters/InputSources/OscFloatAnalogSourceTests.cs` と `Tests/PlayMode/Adapters/InputSources/ArKitOscAnalogSourceTests.cs` が Green、既存 OSC テストが無修正でパス
  - _Requirements: 1.6, 5.3, 5.4, 5.5, 5.6, 5.7, 8.6_
  - _Boundary: com.hidano.facialcontrol.osc.Adapters.OSC.OscReceiver, Adapters.InputSources.OscFloatAnalogSource, Adapters.InputSources.ArKitOscAnalogSource_

- [ ] 3.2 InputActionAnalogSource (P)
  - TDD: 失敗テスト先行 → 実装 → 緑化 を 1 leaf 内で完結させる
  - `com.hidano.facialcontrol.inputsystem/Runtime/Adapters/InputSources/InputActionAnalogSource.cs` を新規追加
  - ctor で `(InputSourceId id, InputAction action, AnalogInputShape shape)` を受け取り `AxisCount` を確定（Scalar=1, Vector2=2）
  - `Tick(deltaTime)` 内で shape に応じて `_action.ReadValue<Vector2>()` または `ReadValue<float>()` を 1 回呼びキャッシュ。`_action.enabled == false` または `_action.controls.Count == 0` で `IsValid=false`（throw しない）
  - PlayMode テスト: `InputSystem.QueueDeltaStateEvent` で Vector2 / float を注入し `TryReadVector2` / `TryReadScalar` の出力をアサート。disable / unbound 経路で `IsValid=false` と例外なし。`Tick` 1000 回で GC alloc 0 バイトを `Profiler` 差分でアサート（boxing 兆候があれば `InputControl<Vector2>.ReadValue()` 直叩きに切替）
  - 観測可能な完了条件: `Tests/PlayMode/Adapters/InputSources/InputActionAnalogSourceTests.cs` が Green
  - _Requirements: 1.6, 5.1, 5.2, 5.6, 5.7, 8.1_
  - _Boundary: com.hidano.facialcontrol.inputsystem.Adapters.InputSources.InputActionAnalogSource_

## Phase 4: Output Adapters — BlendShape / BonePose 駆動

- [ ] 4.1 AnalogBlendShapeInputSource + AnalogBonePoseProvider 一括実装
  - TDD: 失敗テスト先行 → 実装 → 緑化 を 1 leaf 内で完結させる
  - `Adapters/InputSources/AnalogBlendShapeInputSource.cs` を `ValueProviderInputSourceBase` 継承で実装（`Id="analog-blendshape"`, `Type=ValueProvider`）。ctor で `(InputSourceId, blendShapeCount, IReadOnlyList<string> blendShapeNames, IReadOnlyDictionary<string, IAnalogInputSource> sources, IReadOnlyList<AnalogBindingEntry> bindings)` を受け取り、init 時に `TargetIdentifier` → BS index を逆引きキャッシュ（未存在 BS は `Debug.LogWarning` + skip）。`_outputCache: float[BlendShapeCount]` を 1 つ pre-alloc し `TryWriteValues(Span<float>)` で再利用。同一 BS index への複数 binding は post-mapping 値を**sum**（二重 clamp なし、Aggregator の clamp01 を信頼）。N-axis passthrough を `(int srcAxis, int bsIdx)[]` 配列で表現（foreach allocation 回避）
  - `Adapters/Bone/AnalogBonePoseProvider.cs` を `IDisposable` 実装で追加。ctor で `(IBonePoseProvider boneProvider, IReadOnlyDictionary<string, IAnalogInputSource> sources, IReadOnlyList<AnalogBindingEntry> bonePoseBindings)`。init 時にユニークな (boneName, axis) キーごとの集約バケットを構築し `_entryBuffer: BonePoseEntry[uniqueBonesCount]` を pre-alloc。`BuildAndPush()` で binding を毎フレーム評価 → entries 配列の中身だけ書換え → **Phase 2.1 で追加した internal `BonePose(id, _entryBuffer, skipValidation:true)` ctor** で構築 → `boneProvider.SetActiveBonePose(in pose)` を 1 フレーム 1 回呼出。bindings 0 件 / 全ソース無効で空 BonePose 発行（BoneWriter は no-op）
  - EditMode テスト（Fake `IAnalogInputSource` 使用）:
    - BlendShape 側: 単一 binding の sum、同一 BS の 2 binding sum、未存在 BS skip + warn、N-axis passthrough、`TryWriteValues` の overlap-only、`_outputCache` 再利用で alloc=0
    - BonePose 側: 同一 (bone, axis) の sum、空 bindings → 空 BonePose、`SetActiveBonePose` 呼出回数 1/frame、Fake `IBonePoseProvider` で in-渡し契約確認、hot-path で alloc=0（Phase 2.1 ctor 経由）
  - 観測可能な完了条件: `Tests/EditMode/Adapters/InputSources/AnalogBlendShapeInputSourceTests.cs` と `Tests/EditMode/Adapters/Bone/AnalogBonePoseProviderTests.cs` が Green
  - _Requirements: 1.4, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 4.1, 4.2, 4.3, 4.5, 4.6, 4.7, 4.8, 4.9, 8.1, 8.2, 8.3_
  - _Boundary: Adapters.InputSources.AnalogBlendShapeInputSource, Adapters.Bone.AnalogBonePoseProvider_
  - _Depends: 1.1, 1.2, 1.3, 2.1_

## Phase 5: Persistence — JSON + ScriptableObject

- [ ] 5.1 JSON DTO 一式 + AnalogInputBindingJsonLoader / Serializer
  - TDD: 失敗テスト先行 → 実装 → 緑化 を 1 leaf 内で完結させる
  - `com.hidano.facialcontrol.inputsystem/Runtime/Adapters/Json/Dto/` 配下に `AnalogInputBindingProfileDto` (`version`, `List<AnalogBindingEntryDto> bindings`) / `AnalogBindingEntryDto` (`sourceId`, `sourceAxis`, `targetKind`, `targetIdentifier`, `targetAxis`, `AnalogMappingDto mapping`) / `AnalogMappingDto` (`deadZone`, `scale`, `offset`, `curveType`, `List<CurveKeyFrameDto> curveKeyFrames`, `invert`, `min`, `max`) を `[Serializable]` で定義
  - `Adapters/Json/AnalogInputBindingJsonLoader.cs` に `Load(string json) → AnalogInputBindingProfile` / `Save(in AnalogInputBindingProfile) → string` を実装。`JsonUtility.FromJson<AnalogInputBindingProfileDto>` → Domain 変換。malformed entry（unknown sourceId 形式 / unknown targetKind / 欠損 targetIdentifier / 非数値 mapping パラメータ）は `Debug.LogWarning` + skip + 続行（throw しない、Req 6.5）
  - EditMode テスト: 設計 §AnalogInputBindingJsonLoader のサンプル JSON（`analog-bonepose.right_stick` + `analog-blendshape.arkit_jaw` を含む）で round-trip（JSON → Domain → JSON）アサート。空 bindings / null bindings 双方が空プロファイルになることをアサート。malformed entry skip ＋ 残余ロード継続。`curveKeyFrames` ありの Custom カーブが値同値で round-trip
  - 観測可能な完了条件: `Tests/EditMode/Adapters/Json/AnalogInputBindingJsonLoaderTests.cs` と `Tests/EditMode/Adapters/Json/Dto/AnalogInputBindingDtoRoundTripTests.cs` が Green
  - _Requirements: 6.3, 6.4, 6.5, 6.7, 6.8, 9.6_
  - _Boundary: com.hidano.facialcontrol.inputsystem.Adapters.Json.AnalogInputBindingJsonLoader, Adapters.Json.Dto.AnalogInputBindingProfileDto, AnalogBindingEntryDto, AnalogMappingDto_
  - _Depends: 1.2, 1.3_

- [ ] 5.2 AnalogInputBindingProfileSO + ToDomain / Import / Export
  - TDD: 失敗テスト先行 → 実装 → 緑化 を 1 leaf 内で完結させる
  - `com.hidano.facialcontrol.inputsystem/Runtime/Adapters/ScriptableObject/AnalogInputBindingProfileSO.cs` を `[CreateAssetMenu(menuName = "FacialControl/Analog Input Binding Profile")]` 付きで追加。`[SerializeField, TextArea] _jsonText` + `[SerializeField] _streamingAssetPath` を保持し、`JsonText` getter/setter、`ToDomain(): AnalogInputBindingProfile`（ランタイム JSON パース、`AnalogInputBindingJsonLoader` に委譲）、Editor 用 `ImportJson(string path)` / `ExportJson(string path)` を提供
  - `InputBindingProfileSO`（離散トリガー側）と独立した別アセットであることを Inspector / asset 種別で確認するテスト
  - EditMode テスト: SO アセット round-trip（JSON → SO._jsonText → ToDomain → 再保存 → 再ロードで値同値）、空 jsonText で空プロファイル、ランタイムから `ToDomain` 呼出可能（Editor 専用 API 不要、Req 6.4）、`ImportJson` / `ExportJson` で JSON ファイル経由 round-trip
  - 観測可能な完了条件: `Tests/EditMode/Adapters/ScriptableObject/AnalogInputBindingProfileSOTests.cs` が Green
  - _Requirements: 6.1, 6.2, 6.4, 6.6, 6.7, 9.3_
  - _Boundary: com.hidano.facialcontrol.inputsystem.Adapters.ScriptableObject.AnalogInputBindingProfileSO_
  - _Depends: 5.1_

## Phase 6: Runtime Wiring — FacialAnalogInputBinder

- [ ] 6.1 FacialAnalogInputBinder MonoBehaviour + AnalogBlendShapeRegistration + 統合検証
  - TDD: 失敗テスト先行 → 実装 → 緑化 を 1 leaf 内で完結させる
  - `Adapters/Input/FacialAnalogInputBinder.cs` を `[DefaultExecutionOrder(-50)]` + `[AddComponentMenu("FacialControl/Facial Analog Input Binder")]` 付き MonoBehaviour で実装。`[SerializeField] _facialController / _profile / _actionAsset / _actionMapName="Analog"` を保持
  - OnEnable: profile の `ToDomain` → bindings から sourceId に応じて `InputActionAnalogSource` / `OscFloatAnalogSource` / `ArKitOscAnalogSource` を生成 → BlendShape 側 bindings は `AnalogBlendShapeInputSource` を `InputSourceFactory.RegisterReserved<AnalogBlendShapeOptionsDto>` で登録（`AnalogBlendShapeRegistration : IFacialControllerExtension` 経由） → BonePose 側 bindings は `AnalogBonePoseProvider` を直接保持し `_facialController` を `IBonePoseProvider` として渡す
  - LateUpdate: 全 source の `Tick(Time.deltaTime)` → `AnalogBonePoseProvider.BuildAndPush()`
  - OnDisable: 登録解除、全 source の `Dispose`、`InputAction.Disable`、`OscReceiver.UnregisterAnalogListener` 呼出。離散トリガー側 `FacialInputBinder` の登録には**触れない**
  - `SetProfile(AnalogInputBindingProfileSO profile)`: 内部で OnDisable 等価 → OnEnable 等価を実行（次フレーム反映、Req 7.4）。profile 内の未存在 layer 参照は warn + skip（throw しない）
  - PlayMode 統合テスト: Scene に `FacialController` + `FacialAnalogInputBinder` + `FacialInputBinder` を併置し、両者が互いの登録を破壊せず並走することをアサート（Req 7.6, 9.3）。`SetProfile` 差替え時に進行中の他レイヤー Expression transition が中断されないことをアサート（Req 7.4, 9.4）。`OnDisable` で全 binding が解除され、Transform / BlendShape が初期姿勢へ戻ることをアサート
  - 観測可能な完了条件: `Tests/PlayMode/Adapters/Input/FacialAnalogInputBinderTests.cs` が Green
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 9.1, 9.3, 9.4, 9.5_
  - _Boundary: com.hidano.facialcontrol.inputsystem.Adapters.Input.FacialAnalogInputBinder, com.hidano.facialcontrol.inputsystem.Registration.AnalogBlendShapeRegistration_
  - _Depends: 3.1, 3.2, 4.1, 5.2_

## Phase 7: Editor + Sample

- [ ] 7.1 AnalogInputBindingProfileSOEditor (UI Toolkit + JSON I/E + Humanoid 自動割当) (P)
  - TDD: 失敗テスト先行 → 実装 → 緑化 を 1 leaf 内で完結させる
  - `com.hidano.facialcontrol.inputsystem/Editor/Inspector/AnalogInputBindingProfileSOEditor.cs` を UI Toolkit (`UnityEngine.UIElements` / `UnityEditor.UIElements`) で実装。IMGUI 不使用
  - 表示: `Foldout` + `ListView` で各 binding の `sourceId` / `targetKind` / `targetIdentifier` / mapping summary（curveType, scale, offset）を読取専用表示（Req 10.1）
  - ボタン群:
    - `Import JSON...`: `EditorUtility.OpenFilePanel` → `AnalogInputBindingJsonLoader.Load` → SO の `_jsonText` を上書き + `EditorUtility.SetDirty`
    - `Export JSON...`: 現在の `_jsonText` を `EditorUtility.SaveFilePanel` で保存
    - `Humanoid 自動割当`: `bone-control` の `HumanoidBoneAutoAssigner.ResolveEyeBoneNames(animator)` を呼び、BonePose-target binding の `targetIdentifier` に LeftEye / RightEye を充填。非 Humanoid のとき disabled（または押下時 warn + no-op）
  - フル GUI のマッピングカーブ編集は preview.2 以降（本タスクでは扱わない、Req 10.6）
  - EditMode テスト: SO を読込んだ Inspector が ListView に bindings を列挙、Import で `_jsonText` が更新されダーティ化、Export で JSON ファイル生成、Humanoid 自動割当ボタンで eye 2 binding の `targetIdentifier` が設定される。Editor asmdef 内に閉じ、ランタイム asmdef から参照不可であることをアサート
  - 観測可能な完了条件: `Tests/EditMode/Editor/Inspector/AnalogInputBindingProfileSOEditorTests.cs` が Green、Inspector で実 SO アセットを開いて編集できる
  - _Requirements: 4.4, 10.1, 10.2, 10.3, 10.4, 10.5, 10.6_
  - _Boundary: com.hidano.facialcontrol.inputsystem.Editor.Inspector.AnalogInputBindingProfileSOEditor_
  - _Depends: 5.2_

- [ ] 7.2 AnalogBindingDemo Sample 一式（Samples~ + Assets/Samples 二重管理） (P)
  - TDD: サンプルアセットの整合性（Scene が結線済 / JSON が parse 可 / SO が round-trip 可）を smoke テストで担保する
  - `Packages/com.hidano.facialcontrol.inputsystem/Samples~/AnalogBindingDemo/` 配下に以下を canonical で配置:
    - `AnalogBindingDemo.unity`: Scene。`FacialController` + `FacialAnalogInputBinder` + `FacialInputBinder` を併置
    - `AnalogBindingDemoHUD.cs`: 入力値・出力 BlendShape weight・BonePose Euler を on-screen 表示する MonoBehaviour
    - `AnalogBindingProfile.asset`: `AnalogInputBindingProfileSO` 実体。右スティック → LeftEye/RightEye の Y/X Euler、ARKit `jawOpen` または OSC `/avatar/parameters/jawOpen` → mouth-open BS の 2 binding を含む
    - `analog_binding_demo.json`: SO の JSON 表現（Req 11.4 の対応）
  - `package.json` の `samples` 配列に AnalogBindingDemo を登録
  - `FacialControl/Assets/Samples/AnalogBindingDemo/` に**ミラー**を配置（HatsuneMiku 等のモデル参照を Scene にベイク済の状態で同梱、二重管理ルール準拠）。両者の `*.cs` / `*.json` は一致すること
  - サンプル README にユーザー側で必要な Humanoid rig（eye bones + mouth-open BS）を明記（モデル非同梱、Req 11.6）
  - PlayMode smoke テスト: Sample Scene を Test Runner からロードして 30 frame 回し、HUD が初期化済かつエラーログ 0 であることをアサート。`InputSystem.QueueDeltaStateEvent` で Vector2 注入 → eye Transform.localRotation 変化、OSC `/avatar/parameters/jawOpen` 0.0→1.0 注入 → mouth BS weight 変化を確認（Req 11.5）
  - 観測可能な完了条件: `Tests/PlayMode/Samples/AnalogBindingDemoSmokeTests.cs` が Green、Package Manager から Sample Import が成立し、dev mirror が同期している
  - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6_
  - _Boundary: Samples~/AnalogBindingDemo/, Assets/Samples/AnalogBindingDemo/_
  - _Depends: 6.1_

## Phase 8: Verification — GC ゼロ + パフォーマンス + E2E

- [ ] 8.1 GC ゼロアロケーション検証 + マルチキャラクター Performance テスト
  - TDD: 失敗テスト先行 → 実装 → 緑化 を 1 leaf 内で完結させる（既存 `BoneWriterGCAllocationTests` と同形パターン）
  - `Tests/PlayMode/Performance/AnalogBindingGCAllocationTests.cs` を新規追加し、warmup 後の以下経路で per-frame ヒープ確保が **0 バイト** であることを `Profiler.GetMonoUsedSizeLong` 差分または `Is.Not.AllocatingGCMemory()` 相当でアサート:
    - `AnalogMappingEvaluator.Evaluate` を 10000 回（Req 2.6, 8.1）
    - `AnalogBlendShapeInputSource.TryWriteValues` を 10 binding / 64 BlendShape で 1000 frame 継続（Req 3.6, 8.1）
    - `AnalogBonePoseProvider.BuildAndPush` を 5 bone × 3 axis bindings で 1000 frame 継続（Phase 2.1 ctor 経由での共有 entries 配列確認、Req 4.7, 8.1, 8.2）
    - `InputActionAnalogSource.Tick` の `ReadValue` 経路（boxing 兆候検出、Req 8.1）
    - OSC `Volatile.Read/Write` 経路（D-7 整合、Req 8.6）
  - `Tests/PlayMode/Performance/AnalogBindingMultiCharacterPerformanceTests.cs`: 10 GameObject に `FacialAnalogInputBinder` を載せ、各 8 binding（mixed BlendShape + BonePose）を流して `LateUpdate` を 100 frame 回したときの total alloc が 0、フレーム時間が budget 内に収まることをアサート（Req 8.4、steering「同時 10 体以上」想定）
  - bindings=64 で per-frame O(N) であること（quadratic 兆候があれば fail、Req 8.3）も同テストで確認
  - 観測可能な完了条件: 上記 2 ファイルが Green、CI のパフォーマンスゲート通過
  - _Requirements: 2.6, 3.6, 4.7, 8.1, 8.2, 8.3, 8.4, 8.5, 8.6_
  - _Boundary: cross-component performance verification_
  - _Depends: 4.1, 6.1_

- [ ] 8.2 E2E テスト一式（右スティック → 目線 / ARKit jawOpen → BlendShape / OSC float → BlendShape） (P)
  - TDD: 失敗テスト先行 → 実装 → 緑化 を 1 leaf 内で完結させる
  - `Tests/PlayMode/Integration/AnalogBindingEndToEndTests.cs` を新規追加し、JSON → SO → Domain → BoneWriter / Aggregator → Transform / BlendShape の全経路を 3 シナリオで検証:
    - **シナリオ A**: 右スティック (`InputAction Vector2`) → `AnalogBonePoseProvider` → `IBonePoseProvider.SetActiveBonePose` → `BoneWriter.Apply`（next-frame）→ LeftEye/RightEye Transform.localRotation が期待 Euler（マッピング適用後）に到達。body tilt 込みのモーション中でも basis 相対が保たれることをアサート（Req 4.5 / 11.2）
    - **シナリオ B**: ARKit `/ARKit/jawOpen` 0.0→1.0 を OSC 受信スレッドから注入 → `ArKitOscAnalogSource` の Volatile 経路 → `AnalogBlendShapeInputSource.TryWriteValues` → `LayerInputSourceAggregator` weighted-sum + clamp01 → `SkinnedMeshRenderer.GetBlendShapeWeight` が期待値に到達（Req 11.3）
    - **シナリオ C**: OSC `/avatar/parameters/eyeBrowsY` の float → `OscFloatAnalogSource` → BlendShape 経路。VRChat 互換アドレスでの動作確認（Req 5.3）
  - 各シナリオで `FacialController._boneWriter.Apply` の next-frame セマンティクスが守られ、`SetActiveBonePose` 呼出フレームの 1 つ次のフレームから Transform 値が変わることをアサート
  - 既存 `BlendShapeNonRegressionTests` / `FacialControllerLifecycleTests` が無修正で全件パスし続けることをアサート（Req 9.3, 9.5）
  - 観測可能な完了条件: `AnalogBindingEndToEndTests.cs` の 3 シナリオが Green、既存テストスイート無修正パス
  - _Requirements: 4.5, 5.1, 5.3, 5.5, 9.1, 9.2, 9.3, 9.5, 11.2, 11.3, 11.5_
  - _Boundary: cross-layer integration_
  - _Depends: 6.1, 7.2_

## Phase 9: Optional / preview.2 候補

- [ ] 9.1* [optional, preview.2] BlendShape ターゲットへの multi-source weighted blending と LayerInputSourceAggregator との統合最適化
  - **optional 判定理由**: preview.1 では Aggregator の clamp01 + adapter 内 sum で要件達成（Req 3.3）。複数 `AnalogBlendShapeInputSource` インスタンスをレイヤー内 weighted blend で混合する高度ケースは preview.2 候補
  - 複数アナログソースを同レイヤー上で個別重みで blend し、Aggregator の既存 weighted-sum と整合する API 拡張を試作
  - 性能影響を計測し、preview.2 開始時に正式タスク化判定する
  - _Requirements: 3.1, 3.3_
  - _Boundary: Adapters.InputSources.AnalogBlendShapeInputSource (multi-source blending)_

- [ ] 9.2* [optional, preview.2] 視線追従 Vector3 ターゲット指定の preview 実装
  - **optional 判定理由**: 本 spec のスコープ外（design §Non-Goals）。preview.2 後半の別 spec の素地として最小プロトタイプのみ用意
  - Vector3 world ターゲットを受け、頭/眼球の look-at から (Pitch, Yaw) を逆算 → `AnalogBonePoseProvider` の入力経路に `IAnalogInputSource` 互換で接続するアダプタを試作
  - 別 spec への移行可能性を評価する材料に留める
  - _Requirements: (将来 spec で再定義)_
  - _Boundary: Adapters.Bone.GazeTargetAnalogSource (prototype)_
