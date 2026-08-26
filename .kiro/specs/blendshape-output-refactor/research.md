# Research & Design Decisions — blendshape-output-refactor

## Summary

- **Feature**: `blendshape-output-refactor`
- **Discovery Scope**: Extension（既存システムのリファクタ。Light Discovery を採用）
- **Key Findings**:
  1. **デッド PlayableGraph は二重にデッド**である。出力 NativeArray（`FacialControlMixer.OutputWeights` / `LayerPlayable.OutputWeights`）が `AnimationStream` に一切書かれず誰にも読まれないだけでなく、`FacialController.Activate/Deactivate` が呼ぶ `ApplyExpressionToPlayable` / `RemoveExpressionFromPlayable` 経由の `LayerPlayable` 状態変更も最終出力に寄与していない。実際の出力実体は `LayerUseCase.UpdateWeights → BlendedOutputSpan` → `FacialController.LateUpdate` の `SetBlendShapeWeight` 直書きである。
  2. **`UnityEngine.Playables` / `UnityEngine.Animations` / `Unity.Animation` への参照は撤去 4 ファイル（＋`FacialController.cs` の `using`）に限局**している。Runtime 全体を grep した結果、これらの API（`PlayableGraph` / `ScriptPlayable` / `AnimationPlayableOutput` / `PropertyStreamHandle` / `AnimationStream`）を使うのは `FacialControlMixer` / `LayerPlayable` / `PlayableGraphBuilder` / `PropertyStreamHandleCache` / `FacialController` の 5 ファイルのみ。
  3. **毎フレーム GC 発生源は 2 箇所に確定**。`ExpressionUseCase.GetActiveExpressions`（毎フレ `new List<Expression>` + `AddRange`）と `LayerUseCase.GroupByLayer`（毎フレ `new Dictionary<string,List<Expression>>` + per-layer `new List<Expression>`）。両者は `LayerUseCase.UpdateWeights` の先頭で毎フレ呼ばれる。Domain サービス（`LayerInputSourceAggregator` / `LayerInputSourceWeightBuffer` / `LayerBlender` / `LayerExpressionSource`）は既に事前確保バッファ運用で GC ゼロ契約を満たしている。
  4. **ベース AnimationClip サンプリングは PlayableGraph 非依存**。ベース表情は Editor の AutoExporter が `BaseExpressionSerializable.cachedSnapshot`（`ExpressionSnapshotDto`）へ bake 済みで、ランタイムは bake 値を参照する。`FacialControlMixer.BuildBaseExpressionValues` はデッド経路でのみベース値を使う。撤去してもベースサンプリング経路に影響しない。
  5. **既存の GC / 性能 / リーク / 統合テストは軒並みデッド経路にぶら下がっている**。`GCAllocationTests` / `MultiCharacterPerformanceTests` / `NativeArrayLeakTests` / `TransitionIntegrationTests` / `EmotionLipSyncBlendIntegrationTests` は `PlayableGraphBuilder.Build` と `LayerPlayable` / `FacialControlMixer` を直接叩いており、ライブ経路（`LayerUseCase`）を検証していない。撤去に伴いライブ経路へ付け替えが必要。

## Research Log

### デッド PlayableGraph の二重死亡の確認

- **Context**: requirements の「PlayableGraph はデッド」を実コードで検証し、`Activate/Deactivate` 経由の `LayerPlayable` 変更が出力に寄与していないかを確認する必要があった（撤去で挙動が変わらないことの保証）。
- **Sources Consulted**:
  - `Runtime/Adapters/Playable/FacialController.cs`（`LateUpdate` / `Activate` / `Deactivate` / `ApplyExpressionToPlayable` / `RemoveExpressionFromPlayable` / `InitializeInternal` / `Cleanup`）
  - `Runtime/Adapters/Playable/FacialControlMixer.cs`（`PrepareFrame` / `ComputeOutput` / `OutputWeights`）
  - `Runtime/Adapters/Playable/LayerPlayable.cs`（`UpdateTransition` / `ComputeBlendOutput` / `OutputWeights`）
- **Findings**:
  - `FacialControlMixer` は `PlayableBehaviour`（ScriptPlayable）であり `ProcessFrame`/`AnimationStream` 書込みを持たない。`PrepareFrame` で `_outputWeights`（NativeArray）を計算するが、`AnimationPlayableOutput.SetSourcePlayable(mixer)` で接続されても ScriptPlayable は stream を書かないため Animator へ何も流れない。
  - `FacialController.LateUpdate` は `output = _layerUseCase.BlendedOutputSpan` を読み、`SetBlendShapeWeight(idx, weight*100f)` で直書きする（コメント「PlayableGraph の出力はバイパスする」）。これがライブ経路。
  - `Activate/Deactivate` は `_expressionUseCase.Activate/Deactivate`（ライブ）と `ApplyExpressionToPlayable/RemoveExpressionFromPlayable`（デッド `LayerPlayable` 変更）の両方を呼ぶが、後者の出力は誰も読まない。
- **Implications**: 撤去対象 4 ファイルと `FacialController` の graph 連携（`_graphBuildResult` フィールド・`ApplyExpressionToPlayable`/`RemoveExpressionFromPlayable`・`_graphBuildResult.Dispose`）を除去しても、ライブ出力（`LateUpdate` 直書き）は不変。Req 1.1 / 1.6 を実コードで裏付け。

### Engine API 依存の局在性と asmdef への影響

- **Context**: 撤去後に Adapters asmdef から `Unity.Animation` 参照を外せるか、ベース sampling 等で `UnityEngine.Animations` を使う残存コードが無いかを確認する必要があった。
- **Sources Consulted**: Runtime 配下を `UnityEngine.Playables|UnityEngine.Animations|Unity.Animation|AnimationPlayableOutput|PropertyStreamHandle|AnimationStream|ScriptPlayable|PlayableGraph` で grep。
- **Findings**:
  - ヒットしたのは撤去 4 ファイル + `FacialController.cs` のみ。
  - `AnimationClipCache` / `BaseExpressionSerializable` が使う `AnimationClip` は `UnityEngine.AnimationModule`（基本 Engine 参照）であり `Unity.Animation`（Playables/Stream）パッケージとは別。`Unity.Animation` 参照は撤去ファイル専用。
- **Implications**: 撤去後、Adapters asmdef の `references` から `Unity.Animation` を外せる見込み。ただし最終確定は実装フェーズでコンパイル確認する（テスト asmdef 側が `Unity.Animation` / `UnityEngine.Playables` を参照しているため、テスト撤去・付替えと同時に行う）。Req 7.2 のクリーン化に資する副次効果。

### 毎フレーム GC 発生源の特定

- **Context**: Req 2 の核心。撲滅すべき per-frame アロケーションの正確な所在とデータ構造を確定する。
- **Sources Consulted**: `Runtime/Application/UseCases/LayerUseCase.cs`（`UpdateWeights` / `GroupByLayer`）、`Runtime/Application/UseCases/ExpressionUseCase.cs`（`GetActiveExpressions`）、`Runtime/Domain/Services/LayerInputSourceAggregator.cs` / `LayerInputSourceWeightBuffer.cs`。
- **Findings**:
  - `ExpressionUseCase.GetActiveExpressions()`: `var result = new List<Expression>(); foreach(...) result.AddRange(...)` を毎回新規確保。`LayerUseCase.UpdateWeights` 内で毎フレ呼ばれる。
  - `LayerUseCase.GroupByLayer(activeExpressions)`: `new Dictionary<string,List<Expression>>()` を毎フレ確保し、レイヤーごとに `new List<Expression>()` を確保。
  - Domain 側（Aggregator / WeightBuffer / Blender / LayerExpressionSource）は pre-allocated バッファ・`stackalloc`・`Span` 運用で既に GC ゼロ。`_layer2ActiveBuffer` のように `LayerUseCase` 内に再利用バッファを持つ前例あり（layerOverrideMask 抑制計算で実装済み）。
- **Implications**: 撲滅手段は「Application 層に再利用バッファを持たせ、`GetActiveExpressions` / `GroupByLayer` を非確保版に置換」。`ExpressionUseCase` に「呼出側バッファへ書き込む」収集 API を追加、`LayerUseCase` に「レイヤー名 → 再利用 `List<Expression>` の固定スロット辞書」を事前確保して毎フレ `Clear` で再利用する方式が既存パターンに整合（`_layer2ActiveBuffer` と同型）。

### ベース AnimationClip サンプリング経路の graph 非依存性

- **Context**: D-1 リスク（撤去後にベース Clip サンプリングが graph 依存だと方針修正が要る）の検証。
- **Sources Consulted**: `BaseExpressionSerializable.cs`（`cachedSnapshot` / `EnsureCachedSnapshot`）、`FacialCharacterProfileSO.cs` の base 参照、`contribution-mask-and-base-expression/design.md`。
- **Findings**:
  - ベース表情は Editor 自動保存パイプライン（`OnSerializedObjectChanged → delayCall FlushAutoSave`）で `cachedSnapshot` に bake され、ランタイムは bake 済み値を参照する。runtime sampling は介在しない。
  - ライブ経路（`LayerUseCase.UpdateWeights`）は `Array.Clear(_finalOutput)` 後に `LayerBlender.Blend` を呼ぶ。ベース表情のライブ適用は `ContributeMask`（触らない index は前値保持）機構に依存しており、`FacialControlMixer.BuildBaseExpressionValues` のベース seed はデッド経路専用。
- **Implications**: 撤去はベースサンプリングに無影響（Req 1.6 充足）。**本スペックはライブ経路のベース表情挙動を一切変更しない**（現状の `Array.Clear` 起点の挙動をそのまま温存）。ベース seed のライブ適用是非は `contribution-mask-and-base-expression` の領分であり本スペック外。

### 既存テスト資産のデッド経路依存

- **Context**: Req 8（テスト整合・回帰検証）。撤去で壊れるテストと、ライブ経路へ付替えるべきテストを棚卸し。
- **Sources Consulted**: `Tests/PlayMode/Adapters/PlayableGraphBuilderTests.cs` / `LayerPlayableTests.cs`、`Tests/EditMode/Adapters/PropertyStreamHandleCacheTests.cs` / `Playable/FacialControlMixerBaseExpressionTests.cs`、`Tests/PlayMode/Performance/{GCAllocationTests,MultiCharacterPerformanceTests,NativeArrayLeakTests}.cs`、`Tests/PlayMode/Integration/{TransitionIntegrationTests,EmotionLipSyncBlendIntegrationTests}.cs`。
- **Findings**:
  - 完全撤去対象（撤去クラス専用）: `PlayableGraphBuilderTests` / `LayerPlayableTests` / `PropertyStreamHandleCacheTests` / `FacialControlMixerBaseExpressionTests`。
  - 付替え必要（デッド経路で挙動検証している）: `GCAllocationTests`（`LayerPlayable.UpdateTransition`/`ComputeBlendOutput` で GC 計測）/ `MultiCharacterPerformanceTests`（`PlayableGraphBuilder.Build` を 10 体）/ `NativeArrayLeakTests`（`BuildResult.Dispose` のリーク）/ `TransitionIntegrationTests` / `EmotionLipSyncBlendIntegrationTests`（`LayerPlayable`/`FacialControlMixer` 直叩き）。これらはライブ経路（`LayerUseCase.UpdateWeights` / `BlendedOutputSpan` / `LayerInputSourceAggregator` / `LayerExpressionSource`）へ等価な検証として付替える。
  - `NativeArrayPool` / `AnimationClipCache` は runtime 未使用（テスト専用）だが requirements の撤去対象外。現状維持し、本スペックでは触らない（隣接デッドコードとして backlog 候補に留める）。
- **Implications**: Req 8.2（撤去依存テストの整理）と Req 8.3/8.4（ライブ経路の非回帰・GC ゼロ検証）を両立するため、撤去 4 テスト + 付替え 5 テストの 2 系統で計画する。GC ゼロ検証はライブ `LayerUseCase.UpdateWeights` を 100 フレーム回す方式へ刷新する（旧テストは `NativeArrayPool`/`LayerPlayable` を測っており Req 2 を実証していない）。

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| 出力ライター抽象を Domain interface 化 | `IBlendShapeOutputWriter` を Domain に置き、Adapters が SMR 実装 | 依存方向が綺麗、将来 Job 実装も Domain 契約で差替え | Domain は Unity 型（SkinnedMeshRenderer）を知らない契約。書込み先 Engine 型を抽象に出せない | 不採用。Domain に SMR を漏らせない |
| 出力ライター抽象を Adapters interface 化（採用） | `IBlendShapeOutputWriter` を Adapters 層に置き、`FacialController` は writer 経由で書込む。既定実装 `SkinnedMeshRendererBlendShapeWriter` | Engine 依存を Adapters に封じ込めたまま差替え点を作れる。asmdef 契約を破らない | `FacialController` から writer への配線が必要 | 採用。Req 1.2/1.3/1.4/7.2 に整合 |
| 抽象を作らず `FacialController` 内のヘルパーメソッド分離のみ | 直書きを private メソッドに切るだけ | 最小変更 | 「将来 Job/Burst 実装を差し込める」契約（Req 1.4 / 7.1 / backlog M-8）を満たさない | 不採用 |

## Design Decisions

### Decision: 出力ライター抽象を Adapters 層 interface として導入する

- **Context**: Req 1.2/1.3/1.4/7.1/7.2。現行 `SetBlendShapeWeight` 直書きを差替え可能にしつつ、Engine 依存を Adapters に封じ込める。
- **Alternatives Considered**:
  1. Domain interface 化 — Domain が `SkinnedMeshRenderer` を知る必要が生じ契約違反。
  2. Adapters interface 化（`IBlendShapeOutputWriter` を Adapters に） — Engine 型を Adapters に閉じたまま差替え点を提供。
  3. 抽象なし（メソッド分離のみ） — Req 1.4 の将来差替え契約を満たさない。
- **Selected Approach**: Adapters 層に `IBlendShapeOutputWriter` を新設。インターフェースは「事前確保済みのターゲットマッピングに対し、正規化済み（0..1）の出力 Span を受け取り Renderer へ反映する」契約のみを持つ。既定実装 `SkinnedMeshRendererBlendShapeWriter` が現行の `BlendShapeTarget[][]` マッピング構築（`CollectBlendShapeNames` 由来）と `weight*100f` スケール変換・`SetBlendShapeWeight` 直書きを担う。`FacialController.LateUpdate` は writer に出力 Span を渡すだけにする。
- **Rationale**: 差替え点を最小の表面積で確保しつつ、BoneWriter 経路（Req 1.5）・OutputBus publish（`PublishFacialOutput`）・スケール変換規約（anim 0..100 ⇄ domain 0..1）を維持できる。将来 Job/Burst 実装は同 interface の別実装として差し込む（backlog M-8）。
- **Trade-offs**: writer 配線の一手間が増えるが、テスト可能性（Fake writer で出力検証）と将来拡張性を得る。
- **Follow-up**: writer 抽象の境界は「正規化 Span → Renderer 反映」までとし、集約/ブレンド（Application/Domain）は writer の外に残す。`weight*100f` スケール変換は writer 実装に内包する（規約の単一所在）。

### Decision: 毎フレ GC を Application 層の再利用バッファで撲滅する（アルゴリズム不変）

- **Context**: Req 2.1/2.2/2.3。`GetActiveExpressions` の `List` と `GroupByLayer` の `Dictionary`/`List` を per-frame 確保しない。
- **Alternatives Considered**:
  1. `ExpressionUseCase` に「呼出側バッファへ書く」収集 API を追加 + `LayerUseCase` にレイヤー名固定スロットの再利用辞書を事前確保。
  2. Domain へグルーピングを移管 — 責務越境かつ `Dictionary<string,...>` の再利用設計が複雑化。
  3. 出力構造を index ベースへ作り替え — スコープ（アルゴリズム不変）を超える。
- **Selected Approach**: (1)。`ExpressionUseCase` に `CollectActiveExpressions(List<Expression> buffer)`（呼出側が渡す再利用バッファを `Clear`→`Add`）を追加し、既存 `GetActiveExpressions()`（防御コピー返却）は Editor/外部 API 用に温存。`LayerUseCase` は `_activeBuffer`（再利用 List）と「プロファイルのレイヤー名で事前確保した `Dictionary<string,List<Expression>>` + 各 List を `Clear` で再利用」する `_groupedByLayer` を持ち、`GroupByLayer` を非確保版に置換。レイヤー集合はプロファイル確定時に固定なので辞書キーは初期化時に確定できる。
- **Rationale**: 既存の `_layer2ActiveBuffer` 再利用パターンと同型で、集約/ブレンドのアルゴリズム（weighted-sum→clamp01 / 優先度ブレンド / LastWins/Blend）に一切手を入れない。Req 2 を最小侵襲で満たす。
- **Trade-offs**: `ExpressionUseCase` / `LayerUseCase` に再利用バッファ状態が増える。プロファイル切替時（`SetProfile`/`InitializeInternal`）にバッファ再構築が必要。
- **Follow-up**: 公開 API `FacialController.GetActiveExpressions()`（防御コピー返却）と `LayerUseCase.GetInputSourceWeightsSnapshot()`（Editor 用）の GC 許容はそのまま（毎フレ経路ではない）。GC ゼロは「定常運転の `UpdateWeights` 経路」に対してのみ保証する。

### Decision: ネイティブ確保は一度だけ・破棄時に解放（撤去 graph の解放処理は除去）

- **Context**: Req 2.4/2.5。`NativeArray` の毎フレ再確保禁止と破棄時解放。
- **Alternatives Considered**: 現行どおり Domain（`LayerInputSourceWeightBuffer`）が Persistent NativeArray を一度確保 / `LayerUseCase.Dispose` で解放、を維持。撤去 graph の `_graphBuildResult.Dispose`（graph.Destroy + mixer/layer の NativeArray 解放）は除去。
- **Selected Approach**: ライブ経路の NativeArray 所有者は `LayerInputSourceWeightBuffer`（`_bufferA`/`_bufferB`）のみ。これは構築時 1 回確保・`LayerUseCase.Dispose` 経由で解放済み。撤去に伴い `FacialController` から `_graphBuildResult` フィールドと `Cleanup` 内の `_graphBuildResult.Dispose()` を除去。`FacialControlMixer`/`LayerPlayable` が確保していた NativeArray は撤去でまるごと消える。
- **Rationale**: ネイティブ確保の単一所有を維持し、破棄漏れの面を縮小する。Req 2.4/2.5 を満たし、`Cleanup` を簡素化。
- **Trade-offs**: なし（撤去で解放経路が減るだけ）。
- **Follow-up**: `Cleanup` から graph 解放を抜いても、`LayerUseCase.Dispose` / `BoneWriter.Dispose` / child LifetimeScope.Dispose の順序を維持すること（既存順序を温存）。

### Decision: テストはライブ経路へ付替え、GC ゼロ検証もライブ経路で行う

- **Context**: Req 8.2/8.3/8.4。撤去でデッド経路テストが消えると、現状デッド経路でしか検証されていない遷移補間/ブレンド/GC が無検証になる。
- **Selected Approach**: 撤去 4 テスト（`PlayableGraphBuilderTests` / `LayerPlayableTests` / `PropertyStreamHandleCacheTests` / `FacialControlMixerBaseExpressionTests`）を削除。挙動検証テスト（Transition / EmotionLipSyncBlend / Performance / NativeArrayLeak / GCAllocation）はライブ経路（`LayerUseCase.UpdateWeights` / `LayerInputSourceAggregator` / `LayerExpressionSource` / `LayerBlender`）に等価検証として付替える。GC ゼロは `LayerUseCase.UpdateWeights` を warmup 後 N フレーム回し `ProfilerRecorder("GC.Alloc")` で 0 を assert。
- **Rationale**: 「撤去でカバレッジが落ちる」事故を防ぎ、Req 2 を実際に守る経路で計測する。
- **Trade-offs**: テスト付替えの工数。ただし FacialControlMixer 直叩き相当のブレンド検証は `LayerInputSourceAggregator`/`LayerBlender` の EditMode 同期テストで再現でき、PlayMode 依存を減らせる利点もある。
- **Follow-up**: 配置基準（EditMode=同期/Fake、PlayMode=ライフサイクル/実 I/O）に従い、純ロジック検証は EditMode へ寄せる余地を検討（強制はしない）。

## Risks & Mitigations

- **R1: 撤去で `Activate/Deactivate` のライブ副作用が変わる** — `Activate/Deactivate` から `ApplyExpressionToPlayable/RemoveExpressionFromPlayable`（デッド `LayerPlayable` 変更）を除くが、ライブ active 状態は `_expressionUseCase`（系1）と `ExpressionTriggerInputSource`（系2）が保持しているため出力は不変。緩和: PlayMode で `Activate→1 フレーム→出力` の非回帰テストを残す。
- **R2: GC 撲滅リファクタで遷移補間結果がズレる** — `LayerExpressionSource.Tick`/`TransitionCalculator`/`LayerBlender` のアルゴリズムには手を入れず、収集/グルーピングのバッファ化のみに限定する。緩和: 付替え後の Transition/Blend 非回帰テストで before/after 等価を assert（Req 6.4）。
- **R3: テスト asmdef の `Unity.Animation`/`UnityEngine.Playables` 参照残存でコンパイル不整合** — 撤去ファイルと同時にテスト撤去・付替えを行い、Adapters/Tests asmdef の参照を実装フェーズで再確認する。緩和: 実装タスクで asmdef 参照削減（`Unity.Animation`）の可否をコンパイル確認に含める。
- **R4: 事前確保レイヤー名辞書がプロファイル切替に追従しない** — `SetProfile`/`InitializeInternal`/`ReloadProfile`/`LoadCharacter` でバッファを再構築する。緩和: プロファイル切替の非回帰テスト（active 反映）を残す。

## References

- 既存設計: `.kiro/specs/contribution-mask-and-base-expression/design.md` — ベース表情 bake パイプラインと `ContributeMask` 駆動ブレンドの所在（本スペックは非回帰のみ保証）。
- 既存設計: `.kiro/specs/bone-control/design.md` — BoneWriter 経路（Req 1.5 の非回帰対象、backlog M-8）。
- steering: `.kiro/steering/tech.md`（Architectural Contracts / Performance Standards / 配置基準）, `.kiro/steering/structure.md`（asmdef 依存方向）。
- 実コード: `Runtime/Adapters/Playable/{FacialController,FacialControlMixer,LayerPlayable,PlayableGraphBuilder,PropertyStreamHandleCache}.cs`, `Runtime/Application/UseCases/{LayerUseCase,ExpressionUseCase}.cs`, `Runtime/Domain/Services/{LayerInputSourceAggregator,LayerInputSourceWeightBuffer,LayerBlender}.cs`。
