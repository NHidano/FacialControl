# Implementation Plan

本タスクは TDD (Red-Green-Refactor) に従って進める。各実装サブタスクは原則としてその直前の test-first サブタスク (Red) と対をなす。インターフェース変更により多数の test fake が壊れるため、契約変更直後の足場修復を Foundation フェーズに含める。

## 1. Foundation: Domain 契約と既存 fake の足場整備

- [x] 1.1 Domain 層の `IInputSource` 契約に contribute mask getter を追加する
  - `BitArray ContributeMask { get; }` プロパティを `IInputSource` に追加し、`Length == BlendShapeCount` を契約として明示する (D-1)
  - 戻り値は `System.Collections.BitArray` で `UnityEngine` 型を持ち込まないこと、 preallocated 参照を返し中身を runtime 中に書き換えないことをコメントで明記する
  - 観測可能な完了条件: パッケージのコンパイルが通り、 全 `IInputSource` 実装ファイル (本体 5 種 + fake 9 種以上) のコンパイルエラーが顕在化する
  - _Requirements: 1.1, 1.7, 1.8_
  - _Boundary: Domain.Interfaces.IInputSource_

- [x] 1.2 Tests/Shared に「全 index 立て BitArray を返す」 test fake helper を提供する
  - 共通 helper `AllSetContributeMask(int blendShapeCount)` を Tests/Shared に追加し、 既存テスト fake が後方互換 (旧 lerp 挙動) を維持できるデフォルト mask を返せるようにする
  - 観測可能な完了条件: helper の単体テストが通り、 helper 経由で生成した BitArray の全 bit が true、 Length が BlendShapeCount 一致を確認できる
  - _Requirements: 10.8_
  - _Boundary: Tests.Shared_
  - _Depends: 1.1_

- [x] 1.3 既存 `IInputSource` 実装系 fake と内部 source を `ContributeMask` 対応で更新する
  - `IInputSourceContractTests`, `LayerInputSourceAggregatorTests` の各 fake (`FixedValueSource`, `PartialWriteSource`, `ToggleableValidSource` 等), `WeightOneExactOutputContractTests.PatternValueSource`, `LayerInputSourceRegistryTests.FakeInputSource`, `InputSourceRegistryTests.StubInputSource`, PlayMode の `MultiSourceBlendThreeBindingsTests.FixedValuesInputSource`, Performance テストの fake, Samples の Mock (`MockTriggerAdapterBinding`, `MockAnalogAdapterBinding`) を helper 経由の「全立て mask」 デフォルトに揃える
  - Application 内部 `LayerExpressionSource` (`LayerUseCase.cs` 内) と Adapters 内部 `AnalogInputSourceWrapper` (`InputSystemAdapterBinding.cs` 内) も ContributeMask を返す形に更新する
  - 観測可能な完了条件: 既存テストスイート (EditMode + PlayMode) のコンパイルが通り、 既存 fake を使う全テストが緑のまま (mask 全立てで旧 lerp 挙動が維持される)
  - _Requirements: 1.1, 10.8_
  - _Boundary: Tests fakes, Samples Mock, Application.UseCases.LayerUseCase, Adapters.InputSystem.AdapterBindings_
  - _Depends: 1.1, 1.2_

## 2. Domain サービス: LayerBlender mask 駆動化と Aggregator OR 集約

- [x] 2.1 LayerBlender の mask 駆動契約を red テストで定義する
  - `LayerBlenderTests` (新設) に「mask 全立て + 出力初期値 0 → 旧 lerp 出力と一致」 を追加 (R10.1, R3.4)
  - `LayerBlenderMaskTests` (新設) に「mask 部分立て → 立たない index は呼出前の output 値を保持」 を追加 (R10.2, R3.3)
  - 「mask null → 全立てフォールバック」 のケースも red として追加する
  - 観測可能な完了条件: 追加した `LayerBlenderTests` / `LayerBlenderMaskTests` が現実装で red になる
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 10.1, 10.2_
  - _Boundary: Tests.EditMode.Domain.LayerBlender_

- [x] 2.2 LayerBlender に ContributeMask フィールドを追加し mask 駆動 Blend ループを実装する
  - `LayerBlender.LayerInput` に `BitArray ContributeMask` フィールドを追加し、 4 引数コンストラクタ (mask 省略時 null) を提供する (D-5)
  - `Blend` の per-index ループで「mask が null または該当 bit が true」 のときのみ `output[i] = Clamp01(output[i] + (values[i] - output[i]) * weight)` を適用する
  - 0-alloc 契約を維持 (stackalloc / 既存パターン) し、 `UnityEngine` 型を持ち込まない
  - 観測可能な完了条件: 2.1 の red テスト全件が green、 既存 `LayerInputSourceAggregatorTests` / `WeightOneExactOutputContractTests` 等が後方互換のまま green
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6_
  - _Boundary: Domain.Services.LayerBlender_
  - _Depends: 2.1_

- [x] 2.3 LayerInputSourceAggregator の OR 集約を red テストで定義する
  - `LayerInputSourceAggregatorMaskTests` (新設) に「複数 source が同じ index に contribute → OR で true」「全 source が非 contribute の index → false」「invalid な source の mask は OR 集約対象外」「source 0 本の layer mask → 全 false」 を追加する (R10.3)
  - 観測可能な完了条件: 追加テストが現実装で red になる
  - _Requirements: 2.1, 2.2, 2.3, 2.5, 10.3_
  - _Boundary: Tests.EditMode.Domain.LayerInputSourceAggregator_
  - _Depends: 1.3_

- [x] 2.4 LayerInputSourceAggregator に per-layer BitArray バッファと OR 集約ループを実装する
  - 構築時に `BitArray[] _perLayerMask` (長さ LayerCount、 各要素 `new BitArray(blendShapeCount)`) を 1 回だけ確保する
  - 各 layer ループ開始時に `_perLayerMask[l].SetAll(false)`、 valid な source のみ `_perLayerMask[l].Or(source.ContributeMask)` で集約する
  - `LayerBlender.LayerInput` 詰込み時に `_perLayerMask[l]` を渡す
  - 観測可能な完了条件: 2.3 の red テストが green、 既存 `LayerInputSourceAggregatorTests` も後方互換維持で green、 0-alloc Performance テストが赤化していないこと
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_
  - _Boundary: Domain.Services.LayerInputSourceAggregator_
  - _Depends: 2.2, 2.3_

## 3. Domain: ExpressionTrigger active 参照切替と transition union mask

- [x] 3.1 ExpressionTrigger の transition union 挙動を red テストで定義する
  - `LayerBlenderMaskTests` (または専用 `ExpressionTriggerInputSourceBaseMaskTests`) に「Expression A → B 遷移中に ContributeMask が A ∪ B (union)」「遷移完了後に B 単独」「同一 Expression の mask 参照は構築後不変」 を追加する (R10.9)
  - 観測可能な完了条件: 追加テストが現実装で red になる
  - _Requirements: 1.2, 1.3, 10.9_
  - _Boundary: Tests.EditMode.Domain.ExpressionTriggerInputSourceBase_

- [x] 3.2 ExpressionTriggerInputSourceBase に Expression 単位 mask と union バッファを実装する
  - 各 Expression 構築時に「この Expression が contribute する BlendShape index 集合」 を BitArray として preallocate する (D-6)
  - 構築時に共通 `_unionMask` を 1 本 preallocate し、 transition 中に `SetAll(false) → Or(maskA) → Or(maskB)` で union を計算して参照を返す (D-7)
  - active 切替・遷移完了は参照差し替えのみで処理し、 BitArray の中身書換は `_unionMask` のみに限定する
  - `Packages/.../inputsystem/Runtime/Adapters/InputSources/ExpressionTriggerInputSource.cs` は base 委譲のみ確認する
  - 観測可能な完了条件: 3.1 の red テストが green、 既存 ExpressionTrigger 関連テストが後方互換維持で green
  - _Requirements: 1.1, 1.2, 1.3, 1.7_
  - _Boundary: Domain.Services.ExpressionTriggerInputSourceBase_
  - _Depends: 2.4, 3.1_

## 4. Adapters: InputSource 実装の mask 構築 (LipSync / Analog)

- [x] 4.1 (P) LipSync 系 mask 公開の red テストを追加する
  - `ULipSyncProviderTests` を拡張し「全 phoneme entry の `Weights[i] != 0` index が union mask に含まれる」「mask Length == BlendShapeCount」「runtime 中に mask 参照不変」 を red で追加する
  - 観測可能な完了条件: 追加テストが現実装で red になる
  - _Requirements: 1.4, 10.10_
  - _Boundary: Tests.EditMode.Adapters.ULipSyncProvider_

- [x] 4.2 (P) AnalogBlendShapeInputSource の binding → mask 整合 red テストを追加する
  - `AnalogBlendShapeInputSourceMaskTests` (新設、 EditMode) に「binding map の BlendShape index 集合と mask の true index 集合が一致」「OSC binding シナリオでも一致」「ARKit binding シナリオでも一致」 を red で追加する (R1.5, R1.6, R10.10)
  - 観測可能な完了条件: 追加テストが現実装で red になる
  - _Requirements: 1.5, 1.6, 10.10_
  - _Boundary: Tests.EditMode.Adapters.AnalogBlendShapeInputSource_

- [x] 4.3 ULipSyncProvider に union mask 構築・公開を実装し LipSyncInputSource から参照する
  - `ULipSyncProvider` 構築時 (binding 初期化フェーズ) に全 `PhonemeSnapshot.Weights[i] != 0` index を OR して union BitArray を 1 本 preallocate し、 `BitArray ContributeMask { get; }` で公開する
  - `LipSyncInputSource` は構築時に provider から union mask 参照を受け取り、 `IInputSource.ContributeMask` でその参照を返す
  - 観測可能な完了条件: 4.1 の red テスト全件が green、 既存 LipSync テストが後方互換維持で green
  - _Requirements: 1.4, 1.7, 1.8_
  - _Boundary: Adapters.LipSync.ULipSyncProvider, Adapters.InputSources.LipSyncInputSource_
  - _Depends: 1.1, 4.1_

- [x] 4.4 AnalogBlendShapeInputSource に binding map 由来の mask 構築を実装する (D-9 wrapper 完結)
  - binding 初期化時に `BitArray(BlendShapeCount)` を preallocate し、 binding 対象 BlendShape index に true を立てる (D-6)
  - OSC / ARKit ソース (`OscFloatAnalogSource` / `ArKitOscAnalogSource`) には mask API を持たせず、 `AnalogBlendShapeInputSource` がラップする binding map を唯一の source of truth とする (research.md Decision 3)
  - 観測可能な完了条件: 4.2 の red テスト全件が green、 既存 `AnalogBlendShapeInputSourceDirectionTests` が green を維持
  - _Requirements: 1.5, 1.6, 1.7, 1.8_
  - _Boundary: Adapters.InputSources.AnalogBlendShapeInputSource_
  - _Depends: 1.1, 4.2_

## 5. Adapters: BaseExpression データモデル (SO + Serializable + Snapshot 流用)

- [x] 5.1 (P) BaseExpression の SO round-trip red テストを追加する
  - `FacialCharacterProfileSO_BaseExpressionTests` (新設、 EditMode) に「`_baseExpression` 未設定 → BaseExpression が空 (cachedSnapshot.blendShapes が空) として扱われる」「`_baseExpression` の AnimationClip + cachedSnapshot を設定 → 再ロード後も値が保持される」「null セーフ getter が例外を投げない」 を red で追加する
  - 観測可能な完了条件: 追加テストが現実装で red になる
  - _Requirements: 4.1, 4.2, 4.3, 4.4_
  - _Boundary: Tests.EditMode.Adapters.ScriptableObject.FacialCharacterProfileSO_

- [x] 5.2 BaseExpressionSerializable (slim 型) を新設する
  - `Runtime/Adapters/ScriptableObject/Serializable/BaseExpressionSerializable.cs` に `AnimationClip animationClip` + `ExpressionSnapshotDto cachedSnapshot` のみを持つ slim 型を定義する (Expression メタは持たない、 D-4)
  - `cachedSnapshot.blendShapes` が空または null のとき「empty」 と判定する helper を提供する (Mixer / Exporter から参照される)
  - `BaseExpressionSnapshot` 専用型は作らず `ExpressionSnapshot` / `ExpressionSnapshotDto` を流用する (research.md Decision 2)
  - 観測可能な完了条件: 単体テスト (空判定 / 値保持) が green になる
  - _Requirements: 4.1, 4.2, 4.4, 4.5_
  - _Boundary: Adapters.ScriptableObject.Serializable.BaseExpressionSerializable_

- [x] 5.3 FacialCharacterProfileSO に `_baseExpression` フィールドと公開プロパティを追加する
  - `[SerializeField] BaseExpressionSerializable _baseExpression` をルートに追加し、 `public BaseExpressionSerializable BaseExpression => _baseExpression ?? <empty>` の null セーフ getter を提供する
  - GazeConfigs と同じ root field パターンに合わせる (D-4)
  - 観測可能な完了条件: 5.1 の red テスト全件が green、 既存 SO テストが後方互換維持で green
  - _Requirements: 4.1, 4.3_
  - _Boundary: Adapters.ScriptableObject.FacialCharacterProfileSO_
  - _Depends: 5.1, 5.2_

## 6. Adapters: Playable 経路再配線と Mixer base 初期化

- [x] 6.1 Mixer の BaseExpression 初期化を red テストで定義する
  - `FacialControlMixerBaseExpressionTests` (新設、 EditMode) に「BaseExpression の AnimationClip null → 出力バッファが全 0 で初期化 (現状互換)」「BaseExpression に値あり → 出力バッファが per-blendshape weight で初期化」「全 layer mask が false の index は base 値が出力に残る」 を red で追加する (R10.4, R10.5)
  - 観測可能な完了条件: 追加テストが現実装で red になる
  - _Requirements: 5.1, 5.2, 5.3, 10.4, 10.5_
  - _Boundary: Tests.EditMode.Adapters.Playable.FacialControlMixer_

- [x] 6.2 FacialControlMixer を aggregator 直接保持型に再配線する (Mixer-direct-aggregator 経路 A)
  - 構築時に `LayerInputSourceAggregator` を直接保持し、 PlayableGraph 経由 `LayerPlayable.OutputWeights` から値を組立てていた既存経路を撤去する (research.md Decision 1)
  - `LayerPlayable` / `LayerBehaviour` は transition state 進行と Expression スタック管理のみに簡素化し、 BitArray を NativeArray に乗せない
  - `ComputeOutput` で `aggregator.AggregateAndBlend(deltaTime, priorities, weights, _outputBuffer)` を呼出し、 結果を `_outputWeights` (`NativeArray<float>`) にコピーする
  - 観測可能な完了条件: `FacialControllerLifetimeScopePerformanceTests` が green を維持し、 既存 PlayMode 統合テスト (emotion 単独 / lipsync 単独) が green
  - _Requirements: 5.1, 5.4_
  - _Boundary: Adapters.Playable.FacialControlMixer, Adapters.Playable.LayerPlayable, Adapters.Playable.LayerBehaviour_
  - _Depends: 2.4_

- [x] 6.3 FacialControlMixer に BaseExpression 値での出力初期化を実装する
  - 構築時に `_baseExpressionValues = new float[blendShapeCount]` を確保し、 `FacialCharacterProfileSO.BaseExpression.cachedSnapshot.blendShapes` から per-blendshape weight をコピーする (clip null なら全 0)
  - `ComputeOutput` 開始時に `Array.Clear` を `Array.Copy(_baseExpressionValues, _outputBuffer, blendShapeCount)` に置換する
  - BlendShape 数の動的変更は profile lifetime 不変契約により対象外 (preview)
  - 観測可能な完了条件: 6.1 の red テスト全件が green、 0-alloc Performance テストが green を維持
  - _Requirements: 5.1, 5.2, 5.3, 5.4_
  - _Boundary: Adapters.Playable.FacialControlMixer_
  - _Depends: 5.3, 6.1, 6.2_

## 7. Adapters: JSON schema (`baseExpression` フィールド)

- [x] 7.1 profile.json の baseExpression schema round-trip red テストを追加する
  - JSON parser テスト (例: `SystemTextJsonParserTests` 拡張) に「`baseExpression` フィールド付き JSON を読み込むと `_baseExpression.cachedSnapshot.blendShapes` に反映される」「`baseExpression` フィールド欠如 → 空 (全 0 base) として読み込まれる (forward compat)」「serialize → 同形式で `baseExpression` が出力される」「AnimationClip path は JSON に載らない」 を red で追加する
  - 観測可能な完了条件: 追加テストが現実装で red になる
  - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5_
  - _Boundary: Tests.EditMode.Adapters.Json.SystemTextJsonParser_

- [x] 7.2 ProfileSnapshotDto と JSON parser に baseExpression フィールドを追加する
  - `ProfileSnapshotDto` に `baseExpression` フィールドを追加し、 内部スキーマは通常 Expression と同形式 (`{blendShapeName, weight}` 配列) とする (D-3)
  - `SystemTextJsonParser` の parse / serialize 経路に `baseExpression` を組み込み、 既存 Expression と同じ DTO / serializer を流用する
  - フィールド欠如時は空 (全 0 base、 空 mask) として読み込む forward compat を実装する
  - migration ツールは提供しない (preview phase 制約)
  - 観測可能な完了条件: 7.1 の red テスト全件が green、 既存 profile.json (baseExpression 欠如) が後方互換維持で読み込めること
  - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6_
  - _Boundary: Adapters.Json.Dto.ProfileSnapshotDto, Adapters.Json.SystemTextJsonParser_
  - _Depends: 5.2, 5.3, 7.1_

## 8. Editor: AnimationClipExpressionSampler の contribute index 抽出

- [x] 8.1 AnimationClipExpressionSampler の contribute index 抽出 red テストを追加する
  - `AnimationClipExpressionSamplerContributeMaskTests` (新設、 EditMode) に「複数 BlendShape curve を持つ AnimationClip → 全 BlendShape 名が漏れなく集合化される」「BlendShape 以外の curve (Transform / Material) のみ → 空集合」「2 バイト文字 / 特殊記号を含む BlendShape 名でも正しく解決」 を red で追加する (R10.6)
  - 観測可能な完了条件: 追加テストが現実装で red になる
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 10.6_
  - _Boundary: Tests.EditMode.Editor.Sampling.AnimationClipExpressionSampler_

- [x] 8.2 AnimationClipExpressionSampler に curve binding → BlendShape index 解決 helper を実装する
  - 既存 `SampleSummary` の `BlendShapeNames` 取得ロジックを基に、 BlendShape 名集合 + Mesh 上の index 集合を返す helper を追加する
  - 出力 BitArray (呼出側 preallocated) に curve binding に含まれる BlendShape index を立てる動作とし、 clip null / BlendShape curve 不在のときは false を返して BitArray は変更しない
  - BlendShape 命名規則を固定せず、 Editor 専用 (`Editor/Sampling/` 配下、 `includePlatforms: ["Editor"]`) として提供する
  - 観測可能な完了条件: 8.1 の red テスト全件が green
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5_
  - _Boundary: Editor.Sampling.AnimationClipExpressionSampler_
  - _Depends: 8.1_

## 9. Editor: Exporter (透過 bake) と Inspector (BaseExpression セクション)

- [x] 9.1 Exporter の BaseExpression bake 経路 red テストを追加する
  - Exporter テスト (例: `FacialCharacterProfileExporterTests` 拡張) に「`_baseExpression.animationClip` 設定 → delayCall FlushAutoSave 内で `_baseExpression.cachedSnapshot` が更新される」「clip を null へ変更 → cachedSnapshot.blendShapes が空に再生成される」「Sampler 経由で sample される」 を red で追加する
  - 観測可能な完了条件: 追加テストが現実装で red になる
  - _Requirements: 7.1, 7.3, 7.4, 7.5_
  - _Boundary: Tests.EditMode.Editor.AutoExport.FacialCharacterProfileExporter_

- [x] 9.2 FacialCharacterProfileExporter に BaseExpression bake パスを追加する
  - 既存 `SampleAnimationClipsIntoCachedSnapshots` の延長線上に BaseExpression sampling パスを追加し、 Expression 群と同一 delayCall 内で順次処理する (Inspector 競合回避)
  - `_baseExpression.animationClip == null` の場合は `_baseExpression.cachedSnapshot.blendShapes` を空リストに置換する透過再生成を実装する
  - bake / rebake ボタンは追加しない (D-2 / R7.6)
  - 観測可能な完了条件: 9.1 の red テスト全件が green
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5_
  - _Boundary: Editor.AutoExport.FacialCharacterProfileExporter_
  - _Depends: 5.3, 8.2, 9.1_

- [x] 9.3 FacialCharacterProfileSOInspector に BuildBaseExpressionSection を追加する
  - Layers セクションと GazeConfigs セクションの間に「ベース表情」 専用セクションを挿入する (D-8)
  - セクション内に AnimationClip ObjectField を配置し、 clip 未設定時は用途説明 (常時固定表情キャラ / 衣装固定 BlendShape) の HelpBox を表示する
  - bake / rebake ボタンは露出させない (D-2 / R8.4)、 UI Toolkit で実装し IMGUI を新規導入しない (R8.5)
  - 自動保存パイプライン (`OnSerializedObjectChanged → delayCall FlushAutoSave`) に既存パターンで接続する (R7.1)
  - 観測可能な完了条件: Unity Editor を起動し SO を選択するとセクションが Layers と GazeConfigs の間に表示され、 AnimationClip をアサインすると自動保存後に cachedSnapshot が更新される
  - _Requirements: 7.1, 7.6, 8.1, 8.2, 8.3, 8.4, 8.5_
  - _Boundary: Editor.Inspector.FacialCharacterProfileSOInspector_
  - _Depends: 5.3, 9.2_

## 10. Validation: emotion + lipsync 統合テストと契約テスト追補

- [x] 10.1 IInputSource 契約テストに ContributeMask の不変条件を追加する
  - `IInputSourceContractTests` に「`ContributeMask.Length == BlendShapeCount`」「同一 source の `ContributeMask` Length は不変」 を追加する
  - 観測可能な完了条件: 契約テストが green、 違反する fake は失敗する形で検出可能になる
  - _Requirements: 1.1, 1.7, 1.8_
  - _Boundary: Tests.EditMode.Domain.IInputSourceContractTests_
  - _Depends: 1.1, 1.3_

- [x] 10.2 emotion + lipsync 統合シナリオの PlayMode 統合テストを追加する
  - `EmotionLipSyncBlendIntegrationTests` (新設、 PlayMode) に「emotion layer (priority=0) で目・眉・口を駆動 + lipsync layer (priority=1) で口関連 BlendShape のみ駆動 → 目・眉は emotion 値、 口は lipsync で上書き、 contribute されない index は BaseExpression 値のまま」 を追加する (R10.7)
  - BaseExpression に値を持たせたケース (固定怒り顔) と空 (現状互換) の両方を検証する
  - PlayMode 配置基準 (Playable / 実時間補間が必要) に従う (R10.8)
  - 観測可能な完了条件: 統合テストが green、 emotion + lipsync の自然な合成が `LayerBlender.Blend` mask 駆動 + Mixer base 初期化で達成されることが確認できる
  - _Requirements: 5.3, 10.7, 10.8_
  - _Boundary: Tests.PlayMode.Integration.EmotionLipSyncBlendIntegrationTests_
  - _Depends: 4.3, 4.4, 6.3, 9.3_

- [x] 10.3 0-alloc 維持の Performance テストを再確認する
  - 既存 `SetWeightZeroAllocationTests`, `FacialControllerLifetimeScopePerformanceTests`, `MultiCharacterAggregatorPerformanceTests` を実行し、 mask / BaseExpression 追加後も per-frame ヒープ確保ゼロが維持されていることを確認する
  - 観測可能な完了条件: 全 Performance テストが green、 alloc 計測値が以前と同等以下
  - _Requirements: 1.7, 2.4, 3.6, 5.4_
  - _Boundary: Tests.PlayMode.Performance_
  - _Depends: 2.4, 3.2, 4.3, 4.4, 6.3_

## Requirements Coverage Map

- R1.1: 1.1, 1.3, 3.2, 4.3, 4.4, 10.1
- R1.2: 3.1, 3.2
- R1.3: 3.1, 3.2
- R1.4: 4.1, 4.3
- R1.5: 4.2, 4.4
- R1.6: 4.2, 4.4
- R1.7: 1.1, 3.2, 4.3, 4.4, 10.1, 10.3
- R1.8: 1.1, 4.3, 4.4, 10.1
- R2.1〜2.5: 2.3, 2.4, 10.3
- R3.1〜3.6: 2.1, 2.2, 10.3
- R4.1〜4.5: 5.1, 5.2, 5.3
- R5.1〜5.4: 6.1, 6.2, 6.3, 10.2, 10.3
- R6.1〜6.5: 8.1, 8.2
- R7.1〜7.6: 9.1, 9.2, 9.3
- R8.1〜8.5: 9.3
- R9.1〜9.6: 7.1, 7.2
- R10.1〜10.10: 2.1, 2.3, 3.1, 4.1, 4.2, 6.1, 8.1, 10.1, 10.2, 10.3 (R10.8 配置基準は各 EditMode/PlayMode テスト配置で遵守)
