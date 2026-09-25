# Research & Design Decisions — contribution-mask-and-base-expression

## Summary

- **Feature**: `contribution-mask-and-base-expression`
- **Discovery Scope**: Extension (既存 brownfield Unity 6 C# ライブラリへの拡張)
- **Key Findings**:
  - PlayableGraph (`NativeArray<float>`) は managed `BitArray` を運べないため、`FacialControlMixer` が `LayerInputSourceAggregator` を直接保持する経路再配線が必須。
  - `BaseExpressionSnapshot` は専用型を作らず、 既存 `ExpressionSnapshot` / `ExpressionSnapshotDto` をそのまま流用する方が Domain 層の API 表面積を増やさず実装コストも最小。
  - D-9 (OSC / ARKit を `IInputSource` 実装として扱う) は文言通りに実装すると現コード階層と齟齬を起こすため、「mask は実 `IInputSource` (= `AnalogBlendShapeInputSource`) で完結」 と再定義する必要がある。

## Research Log

### Topic 1: Mask 伝搬経路 (PlayableGraph vs Mixer-direct-aggregator)

- **Context**: `IInputSource.ContributeMask` は managed `BitArray`。 既存 `LayerPlayable.OutputWeights` は `NativeArray<float>` で、 BitArray を載せられない。 Mask を毎フレーム Mixer に届ける経路の選択が他の全実装方針を従属させるため最優先で確定する必要がある (gap-analysis Section 11)。
- **Sources Consulted**:
  - 実コード `Runtime/Adapters/Playable/FacialControlMixer.cs` の `ComputeOutput` 実装 (line 143-199)。
  - 実コード `Runtime/Domain/Services/LayerInputSourceAggregator.cs` (preallocated buffer 設計、`AggregateAndBlend` API)。
  - gap-analysis.md Section 4 (mask flow 整理)、Section 7 (リスク領域)。
- **Findings**:
  - 既存 `LayerInputSourceAggregator.AggregateAndBlend` は per-layer 加重和 + LayerBlender.Blend を 1 呼出で回すエントリポイントを既に提供している。
  - 既存 `_perLayerOutput[][]` / `_layerInputScratch[]` は profile lifetime で 1 回のみ確保される preallocated buffer 設計。 同パターンで `_perLayerMask[]` を追加できる。
  - `FacialControlMixer.ComputeOutput` は `_layerInputBuffer[i] = new LayerBlender.LayerInput(priority, weight, values)` の組立を毎フレーム実行している。 `aggregator.AggregateAndBlend(...)` 呼出に置換すれば組立も aggregator 内部に閉じる。
- **Implications**:
  - 経路 A (Mixer が aggregator を直接保持) を採用することで mask flow が単一経路に統合され、 PlayableGraph 経由では難しい BitArray の 0-alloc 伝搬が解決する。
  - `LayerPlayable` の責務は「transition state 進行と Expression スタック管理」 のみに簡素化可能。`OutputWeights` (NativeArray) を経由しない。

### Topic 2: BaseExpressionSnapshot 専用型 vs ExpressionSnapshot 流用

- **Context**: Requirements R4.4 / R7.3 で「BaseExpression の bake 結果を `BaseExpressionSnapshot` 等のメタデータに保存する」 と述べているが、 専用型作成と既存 `ExpressionSnapshot` 流用のどちらが適切か。
- **Sources Consulted**:
  - 実コード `Runtime/Domain/Models/ExpressionSnapshot.cs` — `ReadOnlyMemory<BlendShapeSnapshot> BlendShapes` を持つ value type。
  - 実コード `Runtime/Adapters/ScriptableObject/Serializable/ExpressionSerializable.cs` — `ExpressionSnapshotDto cachedSnapshot` フィールド。
  - gap-analysis.md Section 3.1 (既存資産流用ポイント)。
- **Findings**:
  - `ExpressionSnapshot` は「BlendShape 名 + 値の配列」 のみを表現する純粋な値オブジェクト。 Expression 固有のメタ (id/layer/kind/transitionDuration) は `ExpressionSerializable` 側にあり、`ExpressionSnapshot` 自体には含まれない。
  - そのため BaseExpression が「単一の BlendShape 値配列」 を保持する用途であれば、 `ExpressionSnapshot` をそのまま流用しても意味的に矛盾しない。
  - 専用型 `BaseExpressionSnapshot` を作ると Domain 層の API 表面積が無闇に増え、 Snapshot の同型多重化を招く。
- **Implications**:
  - `ExpressionSnapshot` / `ExpressionSnapshotDto` を流用する。 Serializable 側で `BaseExpressionSerializable` という slim 型 (Expression メタ非搭載) を新設するだけで R4.4 / R7.3 を満たす。

### Topic 3: D-9 の OSC / ARKit Input Source の最終解釈

- **Context**: 要件 R1.6 と D-9 は「`OscFloatAnalogSource` / `ArKitOscAnalogSource` を `IInputSource` 実装として扱い mask API 対応する」 と述べているが、実コードでは両者は `IAnalogInputSource` 実装で、`IInputSource` 実装は `AnalogBlendShapeInputSource`。
- **Sources Consulted**:
  - 実コード `Runtime/Adapters/InputSources/AnalogBlendShapeInputSource.cs`。
  - 実コード `Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/InputSources/OscFloatAnalogSource.cs`。
  - 実コード `Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/InputSources/ArKitOscAnalogSource.cs`。
  - gap-analysis.md Section 1 (D-9 解釈ギャップ)、Section 7 (リスク領域)。
- **Findings**:
  - `AnalogBlendShapeInputSource` は OSC / ARKit などの `IAnalogInputSource` 実装をラップして binding map (BlendShape index 配列) を保持する。
  - mask は「source が contribute する BlendShape index 集合」 = binding map の対象 index 集合と一致。
  - OSC / ARKit ソース自身に mask API を持たせるのは「同じ情報を 2 箇所に持つ」 結果になり、 整合性管理が増える。
- **Implications**:
  - mask API は `AnalogBlendShapeInputSource` 側で実装する。OSC / ARKit ソース (`IAnalogInputSource` 実装) は mask API を持たない。
  - R1.6 の Acceptance Criteria 「`OscFloatAnalogSource` または `ArKitOscAnalogSource` が値を出力する時、 the source shall binding 初期化時に確定した BlendShape index 集合を mask として返す」 は、 「上記 OSC ソースをラップする `AnalogBlendShapeInputSource` の mask が binding 初期化時に確定する」 と読み替えて実装する (mask は当該 wrapper で完結)。
  - R10.10 「OSC / ARKit の mask API を EditMode テストでカバー」 は `AnalogBlendShapeInputSource` のテストケースを OSC / ARKit binding シナリオで拡充して達成する。

### Topic 4: ULipSyncProvider → LipSyncInputSource の mask 提供 API

- **Context**: phoneme entry 集合は `ULipSyncProvider` が保持。 `LipSyncInputSource` は値書込み専門。 mask をどちらが構築・公開するか。
- **Sources Consulted**:
  - 実コード `Packages/com.hidano.facialcontrol.lipsync/Runtime/Adapters/ULipSyncProvider.cs`。
  - 実コード `Runtime/Adapters/InputSources/LipSyncInputSource.cs`。
  - 実コード `Runtime/Adapters/PhonemeEntries/PhonemeSnapshot.cs`。
- **Findings**:
  - phoneme entry の集合 (および各 entry の BlendShape weight 配列) は `ULipSyncProvider` 構築時に与えられる。
  - mask は「全 phoneme entry の `Weights[i] != 0` の OR」 で静的に算出可能 (D-6)。
- **Implications**:
  - `ULipSyncProvider` 側で構築時に union mask を BitArray として 1 度生成し、 `BitArray ContributeMask { get; }` プロパティで公開。
  - `LipSyncInputSource` は構築時に provider から参照を受取り保持、 `IInputSource.ContributeMask` getter は provider 参照を返す。

### Topic 5: ContributeMask の公開形 (mutable BitArray vs ReadOnly wrapper)

- **Context**: `BitArray` は mutable。 公開すると呼出側 (Aggregator 等) が誤って中身を変更するリスクがある。
- **Sources Consulted**:
  - .NET BCL `System.Collections.BitArray` ドキュメント (mutable、 indexer setter / `Or` / `SetAll` などで in-place 変更可)。
  - 検討した代替: `IReadOnlyBitArray` 風の wrapper、 `ReadOnlySpan<int>` の bit-packed view、 `ulong[]` への置換。
- **Findings**:
  - 0-alloc 契約のため呼出側 (Aggregator) の `BitArray.Or(source.ContributeMask)` は mutable BitArray を直接受け取る必要がある (`BitArray.Or` は mutable BitArray のみ受け付ける BCL API)。
  - ReadOnly wrapper を作ると Aggregator 側で OR するために unwrap が必要になり、 0-alloc を壊すリスクが高い。
  - Domain 契約として「呼出側は中身を変更してはならない」 を明記すれば運用は十分。
- **Implications**:
  - `IInputSource.ContributeMask` は生の `BitArray` を返す。
  - Domain 契約コメントで「呼出側は中身を変更してはならない (実装側のみ変更可)」 を明記。
  - Aggregator のみが `Or` を呼出すが、 `_perLayerMask[l].Or(source.ContributeMask)` は `_perLayerMask[l]` を変更し source 側 BitArray は変更しないため契約を維持する。

### Topic 6: BitArray のライフサイクル管理

- **Context**: BitArray の確保・解放主体、 BlendShape 数変更時の再確保手順。
- **Sources Consulted**:
  - 実コード `Runtime/Domain/Services/LayerInputSourceAggregator.cs` のコンストラクタ確保パターン。
  - 実コード `Runtime/Domain/Services/ExpressionTriggerInputSourceBase.cs` の `_snapshotValues` / `_targetValues` 二重バッファ。
- **Findings**:
  - 既存 buffer (`_perLayerOutput[][]`, `_snapshotBuffer[]` 等) はすべて構築時 1 回確保で profile lifetime 中再確保なし。
  - BlendShape 数の動的変更は preview phase スコープ外 (steering: profile lifetime 不変契約)。
- **Implications**:
  - BitArray は各オブジェクト (Aggregator、Source、Expression) の構築時に 1 回確保。
  - 解放は GC に委ねる (オブジェクトが破棄されたら BitArray も到達不能になり回収される)。
  - BlendShape 数変更は本 spec のスコープ外。 ArgumentException による fail-fast で防ぐ。
  - `Dispose` は実装しない (BitArray は IDisposable ではない、 unmanaged resource を持たない)。

### Topic 7: Inspector の bake プレビュー UI

- **Context**: R8.3 「ベース表情未設定時の説明 HelpBox」 は必須、 「bake された BlendShape weight プレビュー」 は要件文書で「任意」 と明記。
- **Sources Consulted**:
  - 実コード `Editor/Inspector/FacialCharacterProfileSOInspector.cs` の `BuildGazeConfigsSection` 等のセクション構築パターン。
  - 既存 Expression Inspector がプレビュー UI を持つかどうか → 持たない (cachedSnapshot はバックエンド側の責務)。
- **Findings**:
  - Expression 側にもプレビュー UI は存在せず、 Inspector はあくまで「設定の入力枠」 と位置付けられている。
  - bake プレビュー UI は要件「任意」 + Inspector 自動保存の透過性 (D-2) と整合し、 initial scope では追加しないのが自然。
- **Implications**:
  - 初回実装ではプレビュー UI 省略、 ObjectField + HelpBox のみ。
  - 必要になったら別 PR で readonly テーブル表示などを追加可能 (backlog 候補)。

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| A: Pure Extension + Mixer-direct-aggregator | `IInputSource` に `ContributeMask` を追加、 `LayerInputSourceAggregator` を `FacialControlMixer` が直接保持し `AggregateAndBlend` を呼ぶ | mask flow 単一経路、 既存 buffer 設計と完全に整合、 PlayableGraph の制約を回避 | preview 段階の breaking change (許容済)、 `LayerPlayable` の責務再定義が必要 | gap-analysis 推奨。本 spec で採択。 |
| B: 並列 Mask Pipeline | `IInputSource` 不変、 `IContributeMaskProvider` で並走 | 既存 fake 変更不要、 incremental 移行可 | source 整合性管理が runtime 検証になる、 preview の breaking 許容を活かせない | 採択しない。 |
| C: Hybrid (Adapters wrapper) | `IMaskedInputSource : IInputSource` を Adapters 層に追加 | 既存コード partially 動作維持 | 「全 index contribute」フォールバック導入が必要、 主目的 (高優先度層の意図せぬ 0 補間) を解消し切れない | 採択しない。 |

**Selected**: Option A (本 spec の design.md File Structure Plan / Components and Interfaces で全実装が経路 A 前提で記述されている)。

## Design Decisions

### Decision 1: Mixer-direct-aggregator 経路 (PlayableGraph 経由を諦める)

- **Context**: managed `BitArray` を毎フレーム Mixer に届ける経路の確定。
- **Alternatives Considered**:
  1. PlayableGraph 経由 (`LayerPlayable.OutputWeights` に値 + 別 NativeArray<ulong> で mask) — 検討対象だが Domain 層の non-Unity 制約を侵すリスク。
  2. Mixer-direct-aggregator (経路 A) — Mixer が `LayerInputSourceAggregator` を直接保持し `AggregateAndBlend` を呼ぶ。
  3. ハイブリッド (PlayableGraph で値、 Mixer 側 dictionary で mask) — 二重経路の整合性管理コスト。
- **Selected Approach**: Option 2 (Mixer-direct-aggregator)。
- **Rationale**:
  - 既存 `AggregateAndBlend` API がすでに「per-layer 加重和 → LayerBlender.Blend」 を 1 呼出で回すエントリポイント。
  - mask は per-layer scratch / per-layer output と同様 preallocated パターンで自然に追加可能。
  - PlayableGraph (`NativeArray<float>`) に managed BitArray を載せる workaround (ulong[] 化など) は Domain 層 Unity 非依存契約に侵食する。
- **Trade-offs**: `LayerPlayable` は transition state 進行のみ担う簡素な責務に再定義。 既存 `OutputWeights` は不要になるが、 Mixer 側で対応するため Adapters 層の局所変更で済む。
- **Follow-up**: `FacialControllerLifetimeScopePerformanceTests` の lifecycle 確認、 およびリポジトリ内で `LayerPlayable.OutputWeights` を直接読む箇所がないことの最終確認 (実装フェーズで grep)。

### Decision 2: BaseExpressionSnapshot は ExpressionSnapshot 流用

- **Context**: Snapshot 型の API 表面積最小化。
- **Alternatives Considered**:
  1. 専用 `BaseExpressionSnapshot` 値型を新設 — Domain API 表面積増。
  2. 既存 `ExpressionSnapshot` 流用 — Expression 固有メタを持たない値オブジェクトのため意味的に流用可。
- **Selected Approach**: Option 2。
- **Rationale**: `ExpressionSnapshot` は純粋な「BlendShape 値配列」 の値オブジェクトで、 Expression メタは含まない。 BaseExpression の用途と意味的に矛盾しない。
- **Trade-offs**: 「BaseExpression に Expression メタを将来追加したい」 場合は再設計が必要だが、 spec の Out of Boundary に該当 (BaseExpressionVariant 等は preview.2 以降)。
- **Follow-up**: 実装時に `BaseExpressionSerializable.cachedSnapshot` を `ExpressionSnapshotDto` 型として宣言、 専用型を定義しない。

### Decision 3: D-9 の OSC / ARKit mask は AnalogBlendShapeInputSource で完結

- **Context**: 要件 R1.6 と実コード階層の齟齬解消。
- **Alternatives Considered**:
  1. OSC / ARKit ソース自身に mask API 追加 — `IAnalogInputSource` インターフェース変更 + `AnalogBlendShapeInputSource` 経由の mask と二重管理。
  2. `AnalogBlendShapeInputSource` で完結 — binding map から mask を一意に決定。
- **Selected Approach**: Option 2。
- **Rationale**:
  - mask = binding 対象 BlendShape index 集合。 binding map は `AnalogBlendShapeInputSource` が保持。
  - OSC / ARKit 側に mask を持たせると同じ情報を 2 箇所に持ち、整合性管理が増える。
- **Trade-offs**: R1.6 の文言と実装が文字通りには対応しないため、 design.md / research.md の両方で「mask は wrapper で完結」 と明文化が必要 (済)。
- **Follow-up**: R10.10 のテスト (`OscFloatAnalogSource` / `ArKitOscAnalogSource` の mask カバレッジ) は `AnalogBlendShapeInputSourceMaskTests` を OSC / ARKit binding シナリオで拡充して満たす。

### Decision 4: ULipSyncProvider が union mask を構築・公開

- **Context**: phoneme entry 集合の所有者 = mask の構築主体とする。
- **Alternatives Considered**:
  1. `LipSyncInputSource` 構築時に provider から PhonemeSnapshot[] を受け取り、 自分で OR して保持。
  2. `ULipSyncProvider` で OR して BitArray プロパティを公開、 `LipSyncInputSource` は参照のみ保持。
- **Selected Approach**: Option 2。
- **Rationale**: phoneme entry 集合の所有者が mask の真の所有者。 `LipSyncInputSource` は値書込み専門で mask 算出ロジックを持たない方が責務分離が明瞭。
- **Trade-offs**: なし。
- **Follow-up**: `ULipSyncProvider` のテスト (`ULipSyncProviderTests`) を mask 構築検証で拡充。

### Decision 5: ContributeMask は生 BitArray を公開、 中身変更禁止を契約で表現

- **Context**: BitArray の mutable 性と 0-alloc 契約のトレードオフ。
- **Alternatives Considered**:
  1. ReadOnly wrapper を新設。
  2. ulong[] による bit-packed view + `ReadOnlySpan<ulong>`。
  3. 生 BitArray + 契約コメントで「呼出側は中身を変更してはならない」。
- **Selected Approach**: Option 3。
- **Rationale**:
  - `BitArray.Or` は mutable BitArray のみ受け付ける BCL API。 wrapper を介すと unwrap が必要で 0-alloc を壊すリスク。
  - 唯一 BitArray を変更し得る経路は Aggregator の `_perLayerMask[l].Or(source.ContributeMask)` だが、 これは `_perLayerMask[l]` を変更し source 側 mask は変更しないため契約を維持する。
- **Trade-offs**: 呼出側が誤って `source.ContributeMask.Set(i, true)` 等を呼ぶ実装ミスは契約違反として実装テストで防ぐ必要あり。
- **Follow-up**: `IInputSourceContractTests` に「ContributeMask の中身が外部から変更されない」 という契約テストを追加することは難しい (BitArray は ref 型) ため、 コメント / コードレビューで担保。

### Decision 6: BitArray ライフサイクルは preallocate + GC、 Dispose しない

- **Context**: 0-alloc 契約と解放責務。
- **Alternatives Considered**:
  1. `IDisposable` 化して Dispose 連鎖。
  2. preallocate + GC 任せ。
- **Selected Approach**: Option 2。
- **Rationale**: BitArray は `IDisposable` ではなく unmanaged resource を持たない。 各オブジェクト (Aggregator, Source, Expression) の lifetime に従って GC で回収される。 BlendShape 数変更は profile lifetime 中許可しない。
- **Trade-offs**: BlendShape 数変更時は profile 再構築が必要 (preview 許容)。
- **Follow-up**: 実装時に各 BitArray 確保が「構築時 1 回のみ」 であることをコードコメントに明記。

### Decision 7: Inspector の bake プレビュー UI は initial scope で省略

- **Context**: R8.3 で要件「任意」。
- **Alternatives Considered**:
  1. ObjectField + HelpBox のみ。
  2. ObjectField + HelpBox + 読み取り専用 BlendShape weight テーブル。
- **Selected Approach**: Option 1。
- **Rationale**: D-2 (bake をユーザーに意識させない) と整合し、 Expression 側にも同等のプレビュー UI が存在しない。 initial scope を簡潔に保つ。
- **Trade-offs**: 利用者が bake 結果を確認するには SO アセットを直接覗くか、 別の手段が必要。
- **Follow-up**: backlog (`docs/backlog.md`) に「ベース表情の bake プレビュー UI 拡張」 を追記候補として記録 (preview.2 以降)。

## Risks & Mitigations

- **mask 伝搬経路の境界面**: PlayableGraph と aggregator の経路統合は、 `FacialController` の初期化フローと `FacialControllerLifetimeScopePerformanceTests` に影響。 → 実装フェーズで lifecycle 検証を最優先に。
- **BitArray 中身の意図しない変更**: `IInputSource.ContributeMask` getter が生 BitArray を返すため、 呼出側のミスで mask が破壊される可能性。 → コードレビューと契約コメントで防ぐ。
- **D-9 の文言と実装の解釈差**: R1.6 をそのまま実装すると過剰な API 拡張に走るリスク。 → 本 research.md Decision 3 で「wrapper で完結」 を明文化済。実装フェーズで `IAnalogInputSource` に mask を追加しない方針を厳守。
- **BlendShape 数変更**: preview スコープ外。 → ArgumentException で fail-fast、 ドキュメントコメントに「profile lifetime 不変契約」 を明記。
- **Test fake 多数 (18+)**: 全 `IInputSource` 実装 fake が `ContributeMask` を返す必要あり。 → 「全立て BitArray」 をデフォルトとする helper を `Tests/Shared/` に提供して boilerplate を削減。

## References

- `requirements.md` — Acceptance Criteria 完全版 (R1〜R10)、 Dig 決定 (D-1〜D-9)。
- `gap-analysis.md` — Section 4 (mask flow)、Section 7 (リスク)、Section 11 (Research Needed 7 項目)。
- `CLAUDE.md` — JSON ファースト永続化、 0-alloc 契約、 Domain 非 Unity 契約。
- `.kiro/steering/tech.md` — Technology Stack、 Architectural Contracts。
- `.kiro/steering/structure.md` — Asmdef Dependency Direction、 Samples 二重管理ルール。
- 実コード:
  - `Runtime/Domain/Interfaces/IInputSource.cs`
  - `Runtime/Domain/Services/LayerBlender.cs`
  - `Runtime/Domain/Services/LayerInputSourceAggregator.cs`
  - `Runtime/Adapters/Playable/FacialControlMixer.cs`
  - `Runtime/Adapters/ScriptableObject/FacialCharacterProfileSO.cs`
  - `Runtime/Adapters/ScriptableObject/Serializable/ExpressionSerializable.cs`
  - `Runtime/Domain/Models/ExpressionSnapshot.cs`
