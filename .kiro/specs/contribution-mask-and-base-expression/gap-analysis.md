# Gap Analysis: contribution-mask-and-base-expression

## 1. Analysis Summary

- **影響範囲**: `IInputSource` インターフェース変更により Runtime / Application / Tests / Samples 横断で **18 ファイル以上の実装** が必要。実装本体 5 種に加え、Test fake 9 種、内部実装 1 種 (`LayerExpressionSource`)、Wrapper 1 種 (`AnalogInputSourceWrapper`) も対応必須。
- **アーキテクチャ親和性**: 既存 `LayerInputSourceAggregator` は per-layer scratch / per-layer output / preallocated `LayerInput[]` を持っており、mask 用 `BitArray` の追加配置先として **既存 buffer 設計と完全に整合する**（ヒープ確保ゼロ契約も維持可能）。
- **mask 伝搬経路の明確化が design phase のキーリスク**: PlayableGraph (`LayerPlayable.OutputWeights` → `FacialControlMixer.ComputeOutput`) は現状 `NativeArray<float>` でレイヤー値のみ運ぶため、`BitArray` は managed 型ゆえに NativeArray 化できない。**Mixer 側で aggregator を直接保持し PlayableGraph 経由は値だけ運ぶ**ような hybrid 経路の検討が必要。
- **OSC / ARKit の D-9 解釈にギャップ**: 要件は `OscFloatAnalogSource` / `ArKitOscAnalogSource` を `IInputSource` 実装として扱うが、実コードでは `IAnalogInputSource` 実装。実 `IInputSource` は `AnalogBlendShapeInputSource` であり binding map を持つ。**design phase で「mask は AnalogBlendShapeInputSource 側で構築」と再定義する必要がある**。
- **既存 `ExpressionSnapshot` / `cachedSnapshot` 経路を再利用可能**: `BaseExpressionSnapshot` は新型を作らず `ExpressionSnapshot` をそのまま流用するか、`BlendShapes` のみの薄いラッパーにできる。

## 2. Requirement-to-Asset Map

| Req | 既存資産 | ギャップ種別 |
|-----|---------|------------|
| R1 (mask API) | `IInputSource` (1ファイル), Base 2種 (Trigger/Value), 実装 5種 + テストfake 9種 | **Missing**: mask getter / preallocated BitArray |
| R2 (Aggregator OR集約) | `LayerInputSourceAggregator` (per-layer buffer 設計あり) | **Extension**: BitArray[] フィールド + OR集約ループ追加 |
| R3 (LayerBlender mask駆動) | `LayerBlender.LayerInput` struct + `Blend` static | **Extension**: struct field 追加 + Blend 内 mask 分岐 |
| R4 (BaseExpression SO) | `FacialCharacterProfileSO` (Gaze と同じ root field パターンあり), `ExpressionSerializable.cachedSnapshot` 流用可 | **Missing**: `_baseExpression` field, `BaseExpressionSerializable`, `BaseExpressionSnapshot` |
| R5 (Mixer 初期化) | `FacialControlMixer.ComputeOutput` (Array.Clear で 0 初期化中) | **Extension**: 0 初期化 → base values copy に変更 |
| R6 (Sampler binding抽出) | `AnimationClipExpressionSampler.SampleSummary` が **既に BlendShape 名集合を返している** | **Reuse**: index 抽出版 API 追加または `ClipSummary` 流用 |
| R7 (自動保存透過 bake) | `OnSerializedObjectChanged` → `delayCall FlushAutoSave` → `FacialCharacterProfileExporter.SampleAnimationClipsIntoCachedSnapshots` | **Extension**: BaseExpression 用の sampling パスを exporter に追加 |
| R8 (Inspector UI) | `FacialCharacterProfileSOInspector` (UI Toolkit, GazeConfigs と同パターン) | **Extension**: `BuildBaseExpressionSection` を `BuildLayersSection` と `BuildGazeConfigsSection` の間に挿入 |
| R9 (JSON schema) | `ProfileSnapshotDto`, `ExpressionSnapshotDto`, `SystemTextJsonParser` | **Extension**: `baseExpression` field 追加 (forward compat) |
| R10 (テスト) | LayerBlender テストは未存在、AggregatorTests / LipSync テスト等は存在 | **Missing**: LayerBlenderTests、Mixer ベース表情テスト、AnimationClipSampler index抽出テスト |

## 3. 既存コード分析: 部分実装と再利用ポイント

### 3.1 既に持っている資産（再利用可）
- **`LayerInputSourceAggregator`**:
  - per-layer `float[]` 出力バッファを構築時 1 回確保 (`_perLayerOutput[layer]`)。同じパターンで `BitArray[] _perLayerMask` を追加すればよい。
  - per-source scratch buffer も `LayerInputSourceRegistry` 経由で preallocated。
  - 集約ループ (`AggregateInternal`) は既に source ごとに valid 判定 + write を回しているので、「mask への OR」を同ループ内に挿入するだけで O(N) は変わらない。
  - 出力 `LayerBlender.LayerInput[]` への詰込み (line 384-385) が拡張ポイント。
- **`LayerBlender.LayerInput` struct**: `readonly struct` で 3 field のみ。BitArray を 4 番目に追加するだけで shape 変更最小。
- **`AnimationClipExpressionSampler`**:
  - 既に `SampleSummary` が `ClipSummary` で `IReadOnlyList<string> BlendShapeNames` を返す (line 116-150)。これに index 解決を加えれば R6 はほぼ達成。
  - 完全に新規 API を追加する必要はなく、既存 `SampleSummary` / `SampleSnapshot` のオーバーロードや helper を 1 つ足す程度で済む。
- **`ExpressionSnapshot` value type**: `BlendShapeSnapshot[]` を持つ既存型。BaseExpression 用に新型は作らず流用可（D-2 の transparent bake と整合）。
- **`ExpressionSerializable.cachedSnapshot` (`ExpressionSnapshotDto`)**: AnimationClip → snapshot の自動保存パスが既に `FacialCharacterProfileExporter.SampleAnimationClipsIntoCachedSnapshots` で動作中。同じパスを BaseExpression に拡張可能。
- **Inspector の `BuildGazeConfigsSection` パターン**: `_rootGazeConfigsProperty = serializedObject.FindProperty("_gazeConfigs")` → `MakeSectionFoldout` → `BuildXxxRow` の構造そのまま。BaseExpression は単一値なので row なし、ObjectField + HelpBox のみで簡素化される。

### 3.2 完全新規が必要な資産
- `IInputSource.GetContributeMask()` (or property) の宣言と全実装
- `FacialCharacterProfileSO._baseExpression` field
- `BaseExpressionSerializable`（`ExpressionSerializable` の slim 版 — id/name/layer/kind 等のメタを持たない、AnimationClip + cachedSnapshot のみ）
- `BaseExpressionSnapshotDto` または `ExpressionSnapshotDto` 流用
- `FacialControlMixer` 側のベース表情参照経路（後述の伝搬問題と関連）

## 4. 統合経路（mask flow）の整理

```
[binding 初期化]
  ExpressionTriggerInputSource  → 各 Expression に preallocated BitArray
  LipSyncInputSource           → phoneme entry 集合の union BitArray
  AnalogBlendShapeInputSource  → bindings の BlendShape index 集合 BitArray (D-9 実装の実態)

[runtime per-frame]
  IInputSource.ContributeMask (参照取得、書き換えなし)
    ↓
  LayerInputSourceAggregator._perLayerMask[layer] = OR(各 source.ContributeMask)
    ↓
  LayerBlender.LayerInput { Priority, Weight, BlendShapeValues, ContributeMask }
    ↓
  LayerBlender.Blend: mask が立つ index のみ lerp、立たない index は output 不変
    ↓
  FacialControlMixer.ComputeOutput
    1) 出力バッファ ← FacialCharacterProfileSO._baseExpression 値 (現状 Array.Clear)
    2) LayerBlender.Blend(layers, output)
```

**問題点**: `LayerPlayable.OutputWeights` は `NativeArray<float>` でレイヤー値を運ぶが、mask は managed `BitArray` で運ばない。

- **選択肢 A (推奨)**: `FacialControlMixer` が `LayerInputSourceAggregator` を直接保持し、`AggregateAndBlend` を呼ぶ形に経路再構成。`LayerPlayable.UpdateTransition` は内部状態進行のみ担当、Mixer が aggregator 経由で mask + values を 1 経路で取得。
- **選択肢 B**: PlayableGraph 上は値のみ運び、mask は Mixer が保持する layer→aggregator 参照から直接取得する hybrid。
- **選択肢 C**: `BitArray` を `ulong[]` 化して `NativeArray<ulong>` に乗せる（過剰、Domain 層の non-Unity 制約を侵す可能性）。

## 5. 影響を受けるファイル一覧

### 5.1 修正必須（実装）
| ファイル | 影響 |
|---------|------|
| `Runtime/Domain/Interfaces/IInputSource.cs` | mask getter プロパティ追加 (breaking) |
| `Runtime/Domain/Services/ExpressionTriggerInputSourceBase.cs` | per-Expression mask preallocate + active参照切替 + transition 中 union (D-7) |
| `Runtime/Domain/Services/ValueProviderInputSourceBase.cs` | mask field + 派生公開 |
| `Runtime/Domain/Services/LayerInputSourceAggregator.cs` | `BitArray[] _perLayerMask` + OR 集約ループ + LayerInput への詰込み |
| `Runtime/Domain/Services/LayerBlender.cs` | LayerInput struct 拡張 + Blend ループ mask 分岐 |
| `Runtime/Adapters/InputSources/AnalogBlendShapeInputSource.cs` | binding 初期化時に mask 構築 |
| `Runtime/Adapters/InputSources/LipSyncInputSource.cs` | provider から mask 取得経路 |
| `Packages/com.hidano.facialcontrol.lipsync/Runtime/Adapters/ULipSyncProvider.cs` | phoneme entry 集合の union mask 公開 (`PhonemeSnapshot.Weights[i] != 0` の index) |
| `Packages/com.hidano.facialcontrol.inputsystem/Runtime/Adapters/InputSources/ExpressionTriggerInputSource.cs` | base 委譲のみ (実質変更なし) |
| `Packages/com.hidano.facialcontrol.inputsystem/Runtime/Adapters/AdapterBindings/InputSystemAdapterBinding.cs` | 内部 `AnalogInputSourceWrapper` (line 483) に mask 追加 |
| `Runtime/Application/UseCases/LayerUseCase.cs` | 内部 `LayerExpressionSource` (line 429) に mask 追加 |
| `Runtime/Adapters/Playable/FacialControlMixer.cs` | base expression 初期化 + mask 経由出力 (経路 A/B 選択依存) |
| `Runtime/Adapters/Playable/LayerPlayable.cs` | mask 伝搬経路設計 (経路 A なら最小変更) |
| `Runtime/Adapters/ScriptableObject/FacialCharacterProfileSO.cs` | `_baseExpression` field + 公開プロパティ |
| `Runtime/Adapters/Json/Dto/ProfileSnapshotDto.cs` | `baseExpression` field |
| `Runtime/Adapters/Json/SystemTextJsonParser.cs` | parse / serialize 経路 |
| `Editor/Sampling/AnimationClipExpressionSampler.cs` | BlendShape index 集合抽出 helper |
| `Editor/Inspector/FacialCharacterProfileSOInspector.cs` | `BuildBaseExpressionSection` 追加 |
| `Editor/AutoExport/FacialCharacterProfileExporter.cs` | BaseExpression bake パス |

### 5.2 新規ファイル
| ファイル | 役割 |
|---------|------|
| `Runtime/Adapters/ScriptableObject/Serializable/BaseExpressionSerializable.cs` | AnimationClip + cachedSnapshot のみ持つ slim 型 |
| `Runtime/Domain/Models/BaseExpressionSnapshot.cs` (任意) | または `ExpressionSnapshot` 流用 |
| `Tests/EditMode/Domain/LayerBlenderTests.cs` | **新規 — 旧テスト不在** |
| `Tests/EditMode/Domain/LayerBlenderMaskTests.cs` | mask 駆動契約 |
| `Tests/EditMode/Adapters/Playable/FacialControlMixerBaseExpressionTests.cs` | Mixer 初期化テスト (PlayMode ↔ EditMode 配置基準確認) |
| `Tests/EditMode/Editor/Sampling/AnimationClipExpressionSamplerContributeMaskTests.cs` | curve binding → index 集合 |
| `Tests/EditMode/Adapters/InputSources/AnalogBlendShapeInputSourceMaskTests.cs` | binding → mask |
| `Tests/PlayMode/Integration/EmotionLipSyncBlendIntegrationTests.cs` | R10.7 統合テスト |

### 5.3 テスト fake 全更新（軽微だが多数）
すべて `IInputSource` 実装 fake が mask getter を空 BitArray 等で実装する必要がある:
- `Tests/EditMode/Domain/IInputSourceContractTests.NoOpInvalidInputSource`
- `Tests/EditMode/Domain/LayerInputSourceAggregatorTests.{FixedValueSource, PartialWriteSource, ToggleableValidSource}` (3種)
- `Tests/EditMode/Domain/WeightOneExactOutputContractTests.PatternValueSource`
- `Tests/EditMode/Domain/LayerInputSourceRegistryTests.FakeInputSource`
- `Tests/EditMode/Adapters/InputSources/InputSourceRegistryTests.StubInputSource`
- `Tests/PlayMode/Domain/MultiSourceBlendThreeBindingsTests.FixedValuesInputSource`
- `Tests/PlayMode/Performance/{SetWeightZeroAllocationTests, FacialControllerLifetimeScopePerformanceTests, MultiCharacterAggregatorPerformanceTests}`
- `Samples~/MultiSourceBlendBasicSample/{MockTriggerAdapterBinding, MockAnalogAdapterBinding}` 内の Mock + 同名 mirror in `Assets/Samples/` (二重管理ルール)

## 6. 実装アプローチ Options

### Option A: Pure Extension (推奨)
既存 IInputSource / LayerBlender / LayerInput を拡張するだけ。Mixer も `LayerInputSourceAggregator` を直接保持する形に再配線。

- **長所**: 設計の認知的負荷が低い、既存 buffer 設計をそのまま活かせる、test 構造維持
- **短所**: IInputSource は breaking change → 全実装更新（preview 段階のため許容）
- **Effort**: M (3-7 日) — 修正点は明瞭だが横断ファイル数が多い
- **Risk**: Medium — PlayableGraph と aggregator の経路統合 (mask 伝搬) が要設計判断

### Option B: 並列 Mask Pipeline
`IInputSource` は変更せず、`IContributeMaskProvider` という別インターフェースで mask だけ供給。Aggregator が両方を見る。

- **長所**: IInputSource 非破壊 → test fake / sample 変更不要
- **短所**: 同一 source instance で 2 経路を辿る複雑化、cast や辞書経由が増えてキャッシュ追跡コスト発生
- **Effort**: M (3-5 日)
- **Risk**: High — 「source と mask の整合性」を実行時にしか検証できず、preview のメリット (breaking 許容) を活かさない

### Option C: Hybrid — Domain層に mask、 Adapters層 wrapper で結合
Domain layer の `IInputSource` は変更しない。Adapters 層に `IMaskedInputSource : IInputSource` を導入し、aggregator は両方を受け付け、mask 不在時は「全 index contribute」のフォールバック。

- **長所**: 既存コードが部分的に動き続ける移行容易性、incremental 適用可
- **短所**: 「全 index contribute」フォールバックは現状の lerp 互換挙動に戻るため、結局全 source を mask 対応させないと R3 の主目的（高優先度 layer の意図せぬ 0 補間問題）は解消しない
- **Effort**: L (1-2 週) — 二経路の維持コスト + フォールバック条件の文書化
- **Risk**: Medium — incremental 移行できる点は preview.2 へ持ち越し対応にも開ける

**推奨: Option A**。preview 段階で破壊的変更が許容されており（spec 制約 / D-1 / D-9）、要件 R10.1 で「mask 全立て + base 全 0 = 旧 lerp 完全互換」という後方互換契約が明示されているため、incremental 経路を保つメリットが薄い。

## 7. リスク領域

| 領域 | リスク | 軽減策 |
|------|--------|--------|
| `BitArray` を NativeArray に乗せられない | PlayableGraph 経由の伝搬不可 | Mixer が aggregator を直接保持する経路に再配線 (Option A 前提) |
| `ULipSyncProvider` から source 全体の union mask を出すタイミング | runtime で active phoneme は変わるが mask は静的 (D-6) | `PhonemeSnapshot[].Weights[i] != 0` の OR を構築時に 1 度算出 |
| OSC / ARKit `IAnalogInputSource` の D-9 解釈差異 | 要件文と実コード型階層がズレ | design phase で「実 IInputSource (= AnalogBlendShapeInputSource) で mask、OSC source は IAnalogInputSource のまま」と明文化 |
| `ExpressionTriggerInputSource` の transition 中 union (D-7) | A∪B 用 BitArray を 1 個 preallocate、`_unionMask.SetAll(false); _unionMask.Or(maskA); _unionMask.Or(maskB)` で 0-alloc 維持 | Domain Test で union 期間の挙動を専用テスト化 |
| BlendShape 数の動的変更 | BitArray 長さ不一致 | 現状 `BlendShapeCount` は profile lifetime 不変契約。preview では追加対応不要 |
| Inspector 自動保存パスの競合 | base expression と通常 expression の bake が同 delayCall で実行 | 既存 `FlushAutoExport` 経路に追記する形で 1 トランザクション化 |
| Test fake が 18+ 箇所散在 | 全更新漏れでビルドエラー | grep `: IInputSource` でリスト化済 (本レポート 5.3 節)、checklist 化 |
| `MultiSourceBlendThreeBindingsTests` 等の既存テスト期待値 | mask 全立て = 旧挙動と等価のはず | テスト fake で「全立て BitArray」をデフォルト返却にすれば既存挙動維持。expectation 変更不要 |

## 8. テスト影響の整理

| 既存テスト | 期待値変更の有無 | 備考 |
|------------|---------------|------|
| `LayerInputSourceAggregatorTests` | **不要** (fake が全立て mask を返せば旧挙動維持) | fake の mask getter 実装のみ追加 |
| `MultiSourceBlendThreeBindingsTests` | **不要** | 同上 |
| `WeightOneExactOutputContractTests` | **不要** | 同上 |
| `IInputSourceContractTests` | テスト追加 | `ContributeMask` プロパティの契約テスト追加 |
| `AnalogBlendShapeInputSourceDirectionTests` | **不要** | binding scale/direction はそのまま、mask は別観点 |
| `ULipSyncProviderTests` | テスト追加 (mask 公開) | 既存 |
| Performance tests (alloc-zero) | **不要** | preallocated BitArray 設計で 0-alloc 維持 |

新規テスト:
- LayerBlender mask 駆動 (R10.1, R10.2, R10.9)
- Aggregator OR 集約 (R10.3)
- Mixer ベース表情初期化 (R10.4, R10.5)
- AnimationClipSampler index 抽出 (R10.6)
- emotion + lipsync 統合 (R10.7) — PlayMode 配置 (Playable 必要)
- OSC/ARKit mask カバレッジ (R10.10)

## 9. Migration 懸念（preview phase）

- **既存 Asset の `FacialCharacterProfileSO`**: `_baseExpression` field 追加は Unity の SerializedObject 互換で問題なし（欠如時 default null）。
- **`profile.json`**: forward-compat 設計 (R9.3) で欠如時は 0 ベース。読み込み側 `SystemTextJsonParser` の null check のみで対応可。
- **PlayableGraph 再構築**: Mixer の経路を Option A で aggregator 直接保持に変えると `FacialController` の初期化フローに影響。`FacialControllerLifetimeScopePerformanceTests` の lifecycle 確認が必要。
- **`AnimationClipCache`**: Repo 内で grep したが該当なし。snapshot キャッシュは `cachedSnapshot` (per-Expression) で完結しており、global cache 不在 → 影響軽微。
- **既存ユーザー**: spec で「ユーザーゼロ」明記、migration tool 不要。

## 10. Effort & Recommendation

- **総合 Effort**: **L (1-2 週)** — IInputSource breaking + Mixer 経路再配線 + Inspector + JSON + 多数 fake 更新 + 統合テスト
- **総合 Risk**: **Medium** — 設計判断 (mask 伝搬経路) と D-9 の意味解釈以外は既存パターン拡張で済む
- **推奨アプローチ**: **Option A (Pure Extension)**
  - 経路 A (Mixer が aggregator を直接保持) を採用すれば `BitArray` を PlayableGraph に乗せる悩みが消える
  - D-7 (transition union) と D-6 (静的確定) の組み合わせは既存 `ExpressionTriggerInputSourceBase._snapshotValues` / `_targetValues` のダブルバッファ手法と完全に同型 → preallocated BitArray 3本（current / target / union）で済む

## 11. Research Needed (design phase で確定)

1. **mask 伝搬経路の最終決定**: Mixer-direct-aggregator (推奨) vs PlayableGraph 経由 vs ハイブリッド
2. **`BaseExpressionSnapshot` を専用型化するか `ExpressionSnapshot` 流用か**: Domain 層の API 肥大とのトレードオフ
3. **D-9 の OSC/ARKit mask 仕様の最終解釈**: 「`IAnalogInputSource` 自体に mask 付与しない (実 IInputSource = AnalogBlendShapeInputSource で完結)」を design.md で明文化するか
4. **`ULipSyncProvider` から `LipSyncInputSource` への mask 提供 API**: 新メソッド追加 vs `PhonemeSnapshot[]` を `LipSyncInputSource` 構築時に渡す形に変更
5. **`ContributeMask` の公開形**: `BitArray` を直接公開 (mutable リスク) vs `IReadOnlyBitArray` 風 wrapper vs `ReadOnlySpan<int>` の bit-packed view
6. **BitArray のライフサイクル管理**: aggregator/source/expression のどこで `Dispose`、BlendShape 数変更時の再確保手順
7. **Inspector の bake プレビュー UI**: HelpBox + ObjectField 単独 vs ベイク済み weight の readonly テーブル表示 (R8.3 「任意」)

## 12. Next Step

Design phase に進み、上記 Research Needed 7 項目を `.kiro/specs/contribution-mask-and-base-expression/design.md` で確定する。特に 1 (mask 伝搬経路) は他の全実装方針を従属させるため最優先で決定すべき。
