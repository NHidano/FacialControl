# Implementation Plan — layer-input-source-blending

本計画は `requirements.md` の R1.1〜R8.6 と `design.md` の全コンポーネント／契約／フロー／移行事項を、TDD（Red → Green → Refactor）で段階的に実装するためのタスク分解である。各サブタスクは 1〜3 時間を目安とし、最初に失敗するテストを書き、次に最小実装、最後にリファクタという順で進める。

**タスク粒度と配置方針**:
- Domain 層 → Adapters 層 → JSON 統合 → ランタイム配線 → Editor → 検証テスト、の順で積み上げる。
- EditMode テストはモック／フェイクで決定論的に検証できるもの全て。PlayMode は MonoBehaviour ライフサイクル / コルーチン / 実 UDP / GC 測定が要るものに限定（CLAUDE.md テスト配置基準）。
- `(P)` マーカーは直前の兄弟タスクに対し同時並行で着手できる場合にのみ付与。共有ファイル書込や先行依存がある場合は外す。

---

- [x] 1. テスト基盤とフェイクの整備（Foundation）
- [x] 1.1 `ManualTimeProvider` フェイクを Tests/Shared に用意する
  - 書込可能な `UnscaledTimeSeconds` プロパティを持つ `ITimeProvider` の単純実装を用意する。
  - Tests/Shared asmdef から EditMode/PlayMode 双方で参照できる形にする。
  - 観測完了条件: `ManualTimeProvider` を new して値をセット→取得で等値が返ること、および `OscInputSource` の EditMode staleness テストから参照可能であることが確認できる。
  - _Requirements: 8.2_

- [x] 1.2 (P) Tests/EditMode / Tests/PlayMode のフォルダ雛形と asmdef を追加する
  - `Tests/EditMode/{Domain,Application,Adapters}` と `Tests/PlayMode/{Integration,Performance}` を作成し、それぞれ asmdef を配置する（既存があれば参照設定のみ更新）。
  - Tests/Shared への参照を全テスト asmdef に追加する。
  - 観測完了条件: Unity EditMode/PlayMode Test Runner で空テストが両プラットフォーム上で実行可能になる。
  - _Requirements: 8.2_
  - _Boundary: Tests asmdef 構成_

- [x] 2. Domain 層: 契約と値オブジェクト
- [x] 2.1 (P) `IInputSource` 契約と `InputSourceType` 列挙を定義する（Red → Green → Refactor）
  - まず最小のインターフェース契約を検証するコンパイル時テスト（型付きモックを通して `Tick` / `TryWriteValues` / `Id` / `Type` / `BlendShapeCount` が呼べるか）を EditMode に書く。
  - 次に `IInputSource` 本体と `InputSourceType { ExpressionTrigger, ValueProvider }` を実装する。
  - 観測完了条件: モックソースが `Tick(0f)` → `TryWriteValues(Span<float>)` で false を返し、`output` が変更されないことを EditMode テストで確認できる。
  - _Requirements: 1.1, 1.4, 1.5_
  - _Boundary: Domain.Interfaces.IInputSource_

- [x] 2.2 (P) `ITimeProvider` 契約を Domain 層に定義する
  - 失敗テストとして `ManualTimeProvider` に経過秒数を進めた際に単調増加することを確認する EditMode テストを書く。
  - `ITimeProvider` インターフェース（`double UnscaledTimeSeconds`）を Domain.Interfaces に追加する。
  - 観測完了条件: `ManualTimeProvider.UnscaledTimeSeconds = 1.0` の後に `= 2.0` を代入すると前回値より大きいことが EditMode で検証される（単調性契約）。
  - _Requirements: 8.2, 5.5_
  - _Boundary: Domain.Interfaces.ITimeProvider_

- [x] 2.3 (P) `InputSourceId` value-object と識別子規約バリデータを実装する
  - `TryParse` / `IsReserved` / 予約 ID 一覧（`osc`, `lipsync`, `controller-expr`, `keyboard-expr`, `input`）および `x-` プレフィックス判定を網羅する EditMode テストを先に書く。
  - `[a-zA-Z0-9_.-]{1,64}` regex を通り、`legacy` が拒否され、予約 ID 判定が正しいことを満たす実装を追加する。
  - 観測完了条件: `InputSourceId.TryParse("legacy", out _) == false`、`InputSourceId.TryParse("osc", out var id)` → `id.IsReserved == true` が EditMode テストで通る。
  - _Requirements: 1.7_
  - _Boundary: Domain.Models.InputSourceId_

- [x] 2.4 (P) `LayerSourceWeightEntry` と関連 DTO を定義する
  - `LayerIdx / SourceId / Weight / IsValid / Saturated` の 5 フィールドを持つ readonly value-struct を追加する。
  - フィールド読取と等値比較の EditMode テストを先に書く。
  - 観測完了条件: 同一値で構築した 2 インスタンスが値等価として振る舞い、Snapshot API から配列で受け取れることが確認できる。
  - _Requirements: 8.1, 2.1_
  - _Boundary: Domain.Models.LayerSourceWeightEntry_

- [x] 3. Domain 層: ダブルバッファとレジストリ
- [x] 3.1 `LayerInputSourceWeightBuffer` の基本 SetWeight / GetWeight / silent clamp を実装する（Red → Green）
  - 範囲外 weight が 0〜1 に silent clamp される EditMode テストを先に書く。
  - `NativeArray<float> × 2` + `_writeIndex` + `_dirtyTick` の最小構造を実装する（この時点では SwapIfDirty の copy-forward は未実装可）。
  - 観測完了条件: `SetWeight(l, s, -0.5f)` 後に `GetWeight(l, s) == 0f`、`SetWeight(l, s, 2.0f)` 後に `GetWeight(l, s) == 1.0f` が swap 後観測できる。
  - _Requirements: 2.1, 2.5, 4.1_
  - _Boundary: Domain.Services.LayerInputSourceWeightBuffer_

- [x] 3.2 `SwapIfDirty` の index flip + copy-forward アルゴリズムを実装する（Critical 1 の回帰防止）
  - Critical 1 の回帰テストを先に書く: フレーム 1 で `SetWeight(0,0,0.5)` → `SwapIfDirty`、フレーム 2 は Set なしで `SwapIfDirty` → `GetWeight(0,0) == 0.5` が保たれていること（スタレデータバグ防止）。
  - `Interlocked.Exchange` で writeIndex を flip し、続けて新 readBuffer から新 writeBuffer へ `NativeArray.CopyTo` で全内容をコピーする実装を追加する。
  - `_dirtyTick == _observedTick` のときは no-op とする。
  - 観測完了条件: 上記回帰テストおよび「Set なしの SwapIfDirty は値を変化させない」テストが EditMode で両方通る。
  - _Requirements: 4.2, 4.4_
  - _Depends: 3.1_
  - _Boundary: Domain.Services.LayerInputSourceWeightBuffer_

- [x] 3.3 BulkScope による atomic flush を実装する
  - `BeginBulk()` → 複数 `SetWeight` → `Dispose()`（= CommitBulk）までは外部から観測されず、Dispose 後の SwapIfDirty で一括反映されることを検証する EditMode テストを書く。
  - プール化された pending dict を持ち、scope Dispose 時に writeBuffer へ一括 flush、`_dirtyTick` を 1 回だけ進める実装を追加する。
  - 観測完了条件: bulk scope 中に単発 `SetWeight` と混在させても bulk の書込が scope 終了時に一括観測されることが EditMode で確認される。
  - _Requirements: 4.5_
  - _Depends: 3.2_
  - _Boundary: Domain.Services.LayerInputSourceWeightBuffer_

- [x] 3.4 範囲外 (layer, source) への Set を警告 + no-op にする
  - `LayerCount` を超える layerIdx、`MaxSourcesPerLayer` を超える sourceIdx への `SetWeight` が警告ログを出して既存値を変更しないことを EditMode で検証する。
  - Unity LogAssert 等を用い、警告が 1 回だけ出ることを確認する。
  - 観測完了条件: 範囲外 Set 後に全 weight が不変であること、および `LogAssert.Expect(LogType.Warning, ...)` が成立することが EditMode テストで通る。
  - _Requirements: 4.3_
  - _Depends: 3.1_
  - _Boundary: Domain.Services.LayerInputSourceWeightBuffer_

- [x] 3.5 `LayerInputSourceRegistry` の初期化とプール確保を実装する
  - `(layerIdx, sourceIdx, IInputSource)` bindings から `layerCount × maxSourcesPerLayer × blendShapeCount` の scratch buffer を 1 本確保し、`Memory<float>` slice で各 (layer, source) に配る実装を追加する。
  - 確保サイズ・スライス境界・`GetSource` / `GetSourceCountForLayer` / `GetScratchBuffer` の挙動を EditMode でテストする。
  - 観測完了条件: 3 layer × 2 source × 200 blendShape の Registry を構築すると、各 (layer, source) の scratch が連続・非重複・固定アドレスで取得でき、`Dispose` で NativeArray が解放されることが確認できる。
  - _Requirements: 1.2, 6.2, 6.5_
  - _Boundary: Domain.Services.LayerInputSourceRegistry_

- [x] 3.6 (P) `Registry.TryAddSource` / `TryRemoveSource` の低頻度ランタイム API を実装する
  - シーン切替を想定した追加・削除操作で、既存 scratch buffer が再確保されること、存在しない id の削除が警告 + no-op であることを EditMode で検証する。
  - 観測完了条件: 追加後に `GetSource(layerIdx, newSourceIdx)` が新アダプタを返し、削除後は `GetSourceCountForLayer` が 1 減ることが EditMode テストで通る。
  - _Requirements: 1.2, 4.3_
  - _Depends: 3.5_
  - _Boundary: Domain.Services.LayerInputSourceRegistry_

- [x] 4. Domain 層: 入力源抽象基底
- [x] 4.1 `ValueProviderInputSourceBase` を実装する
  - `Tick` が no-op であること、派生クラスが `TryWriteValues` のみ実装すれば成立することを検証する EditMode テストを先に書く。
  - 基底で `Id / Type / BlendShapeCount` を保持する最小実装を追加する。
  - 観測完了条件: 派生フェイクが `TryWriteValues` で true/false を切替えると Aggregator 呼出側がその結果どおり寄与を判定できることが EditMode で確認できる。
  - _Requirements: 1.1, 1.4_
  - _Boundary: Domain.Services.ValueProviderInputSourceBase_

- [x] 4.2 `ExpressionTriggerInputSourceBase` の Expression スタック + Transition 状態を実装する
  - 独立スタックと独立 TransitionCalculator 状態を保持し、`TriggerOn` / `TriggerOff` でスタック push/remove、`Tick` で elapsed 前進、`TryWriteValues` で補間結果を書込む仕様を EditMode テスト（Red）で記述する。
  - `TransitionCalculator.ComputeBlendWeight` / `ExclusionResolver.ResolveLastWins` / `ResolveBlend` を純関数として呼び出す Green 実装を追加する。
  - 観測完了条件: 2 インスタンス（controller 相当, keyboard 相当）を同レイヤーで走らせ smile / angry を同時 ON した際、各インスタンス内部の `CurrentValues` が独立に遷移し互いに干渉しないことが EditMode で確認できる。
  - _Requirements: 1.6, 1.8_
  - _Depends: 4.1_
  - _Boundary: Domain.Services.ExpressionTriggerInputSourceBase_

- [x] 4.3 スタック深度超過時の最古 drop と per-instance 1 回 warning を実装する
  - `maxStackDepth` 超過時に最古 Expression が自動 pop され、警告が per-instance で 1 回だけ出ることを EditMode で検証する。
  - 観測完了条件: 深度 2 のインスタンスで 3 件連続 `TriggerOn` すると最古が pop、同一インスタンスでさらに超過させても警告が再発しないことを確認できる。
  - _Requirements: 1.6_
  - _Depends: 4.2_
  - _Boundary: Domain.Services.ExpressionTriggerInputSourceBase_

- [x] 5. Domain 層: 集約サービス
- [x] 5.1 `LayerInputSourceAggregator` の per-layer 加重和 + 最終クランプを実装する
  - 3 source × 2 layer の固定値入力に対し `output[k] = clamp01(Σ wᵢ · values_i[k])` が手計算と一致することを EditMode で検証する Red テストを書く。
  - Aggregator コンストラクタ（registry / weightBuffer / blendShapeCount）と `Aggregate(deltaTime, Span<LayerInput>)` の主ループを実装する。
  - 観測完了条件: `w1=0.5, w2=0.5, v1[k]=1, v2[k]=1` → `output[k]=1.0`（>1 でもクランプのみ）が通る。
  - _Requirements: 2.2, 2.3, 6.3_
  - _Depends: 3.2, 3.5, 4.1, 4.2_
  - _Boundary: Domain.Services.LayerInputSourceAggregator_

- [x] 5.2 長さ不一致時の overlap-only 処理と無効ソースのゼロ寄与を実装する
  - `TryWriteValues` が短い Span を書く場合に余剰インデックスが前フレーム値を保つ／ゼロ初期化される契約を EditMode で検証する。
  - 無効ソース（`TryWriteValues` が false）の寄与がゼロであり例外を出さないことを EditMode で検証する。
  - 観測完了条件: 3 source のうち 1 source だけ IsValid=false のとき残り 2 source の加重和のみが出力され、例外が発生しないことが通る。
  - _Requirements: 1.3, 1.4_
  - _Depends: 5.1_
  - _Boundary: Domain.Services.LayerInputSourceAggregator_

- [x] 5.3 空レイヤー検出とセッション 1 回 warning を実装する
  - 全 source が IsValid=false または source 登録ゼロのレイヤーで出力ゼロ + セッション単位 1 回の警告ログが出ることを EditMode で検証する。
  - 観測完了条件: 空レイヤーを含む設定で複数フレーム `Aggregate` を回しても警告が 1 回のみ、出力はゼロ配列であることが通る。
  - _Requirements: 2.4_
  - _Depends: 5.1_
  - _Boundary: Domain.Services.LayerInputSourceAggregator_

- [x] 5.4 2 段パイプライン: Aggregator → 既存 `LayerBlender.Blend` 接続
  - Aggregator 出力を `LayerBlender.LayerInput[]` にラップし `LayerBlender.Blend(ReadOnlySpan<LayerInput>, Span<float>)` を呼ぶ経路を組む。
  - source weight と `LayerInput.Weight`（inter-layer weight）が独立適用されることを EditMode で検証する。
  - `LayerBlender` のシグネチャは一切変更しないこと（Req 7.1）を確認する契約テストを追加する。
  - 観測完了条件: 既存 `LayerBlender` の挙動を壊さず、per-layer 集約値が inter-layer blend を経由して最終出力に届くことが EditMode で通る。
  - _Requirements: 2.6, 2.7, 6.4, 7.1_
  - _Depends: 5.1_
  - _Boundary: Domain.Services.LayerInputSourceAggregator, LayerBlender_

- [x] 5.5 診断スナップショット API（`TryWriteSnapshot` / `GetSnapshot`）を実装する
  - GC フリーの `TryWriteSnapshot(Span<LayerSourceWeightEntry>, out int written)` と Editor 向け `GetSnapshot()` の双方を実装する。
  - Weight 変更後の次 `Aggregate` でスナップショットに反映されることを EditMode で検証する。
  - `Saturated` フラグが Σw > 1 のレイヤーで true となる挙動を EditMode で検証する。
  - 観測完了条件: TryWriteSnapshot が 0-alloc（`Is.Not.AllocatingGCMemory()`）で、GetSnapshot が List を返し、内容が一致することが通る。
  - _Requirements: 8.1, 8.3_
  - _Depends: 5.1_
  - _Boundary: Domain.Services.LayerInputSourceAggregator_

- [x] 5.6 (P) verbose logging の per-layer per-second レートリミッタを実装する
  - `SetVerboseLogging(true)` 時、同一レイヤーの weight ログが 1 秒あたり最大 1 回に制限されることを `ManualTimeProvider` 注入で EditMode 検証する。
  - 観測完了条件: 1.0 秒内に 100 回 `Aggregate` を回してもログが 1 回だけ出、1.0 秒経過後にもう 1 回出ることが通る。
  - _Requirements: 8.5_
  - _Depends: 5.1, 2.2_
  - _Boundary: Domain.Services.LayerInputSourceAggregator_

- [x] 6. Adapters 層: 時刻と入力源アダプタ
- [x] 6.1 (P) `UnityTimeProvider` を実装する
  - `UnityEngine.Time.unscaledTimeAsDouble` をそのまま返す薄いラッパーを Adapters 層に追加する。
  - PlayMode テストで複数フレーム進行後の値が単調増加することを検証する。
  - 観測完了条件: 1 フレーム後の `UnscaledTimeSeconds` が直前値より大きいことが PlayMode テストで通る。
  - _Requirements: 8.2, 5.5_
  - _Boundary: Adapters.InputSources.UnityTimeProvider_

- [x] 6.2 (P) `InputBindingProfileSO` に `InputSourceCategory` フィールドを追加する
  - `enum InputSourceCategory { Controller, Keyboard }` を serialized フィールドとして追加、既定値は Controller。
  - `OnValidate` で「Category=Controller だが InputAction が Keyboard デバイスのみ」を best-effort 検出し警告ログを出す。
  - CHANGELOG / 移行ノート用の破壊的変更注記箇所に preview 破壊的変更である旨のコメントをコードに残す（ドキュメント整備タスクは別途）。
  - 観測完了条件: 既存 Asset をロードすると Category=Controller が付与され、Keyboard デバイスのみバインドの Asset では OnValidate で警告が出ることが EditMode で確認できる。
  - _Requirements: 5.1, 5.7, 7.4_
  - _Boundary: Adapters.ScriptableObject.InputBindingProfileSO_

- [x] 6.3 `ControllerExpressionInputSource` を実装する
  - 予約 id `controller-expr` を持ち、`InputSystemAdapter` からのトリガーを自身の Expression スタックに流すアダプタを実装する。
  - InputSystemAdapter fake を使った EditMode テストで `TriggerOn("smile")` 後に `TryWriteValues` が smile の BlendShape 値を書込むことを検証する。
  - 観測完了条件: Controller カテゴリのバインディングだけに反応し、Keyboard カテゴリの入力では自アダプタが反応しないことが EditMode で通る。
  - _Requirements: 5.1, 5.7_
  - _Depends: 4.2, 6.2_
  - _Boundary: Adapters.InputSources.ControllerExpressionInputSource_

- [x] 6.4 (P) `KeyboardExpressionInputSource` を実装する
  - 予約 id `keyboard-expr` を持ち、Keyboard カテゴリのバインディングにのみ反応する。Controller アダプタと独立した Expression スタック / TransitionCalculator を保持する（D-13 独立性の再確認テスト）。
  - EditMode テストで Controller と Keyboard を同レイヤーに並べ、smile / angry を同時トリガー → BlendShape 値が加算されることを検証する（D-12 意図挙動）。
  - 観測完了条件: 同レイヤーの 2 アダプタが独立に遷移し、Aggregator 出力は両者の加重和になることが EditMode で通る。
  - _Requirements: 1.6, 1.8, 5.1, 5.7_
  - _Depends: 4.2, 6.2_
  - _Boundary: Adapters.InputSources.KeyboardExpressionInputSource_

- [x] 6.5 `OscDoubleBuffer` に `WriteTick` カウンタを追加する
  - 既存 `Write` 経路で `Interlocked.Increment(ref _writeTick)` が進み、`Volatile.Read(ref _writeTick)` で読取可能になる最小改修を加える。
  - 既存の `Write` / `GetReadBuffer` / `Swap` の API シグネチャは不変に保つ。
  - 観測完了条件: 既存 OSC テストが全て通った上で、`WriteTick` が Write 回数に等しく進むことが EditMode / PlayMode の両方で確認できる。
  - _Requirements: 5.4, 5.5_
  - _Boundary: Adapters.OSC.OscDoubleBuffer_

- [x] 6.6 `OscInputSource` と staleness 判定を実装する
  - `ValueProviderInputSourceBase` を継承し、`OscDoubleBuffer` の read バッファを `output` Span に書込むアダプタを実装する。
  - `WriteTick` 監視で新規受信を検出し `ITimeProvider.UnscaledTimeSeconds` で `_lastDataTime` を更新する。
  - `stalenessSeconds > 0` かつ経過が閾値超過なら `TryWriteValues` が false を返す実装を追加する。
  - `ManualTimeProvider` を注入した EditMode テストで `stalenessSeconds=1.0` / time=0→write→time=2.0 シナリオを決定論的に検証する（Critical 3）。
  - 観測完了条件: EditMode で staleness 超過時 false、受信直後 true、`stalenessSeconds=0` なら恒常 true であることが通る。
  - _Requirements: 5.2, 5.4, 5.5, 5.7_
  - _Depends: 4.1, 6.5, 2.2_
  - _Boundary: Adapters.InputSources.OscInputSource_

- [x] 6.7 (P) `LipSyncInputSource` を実装する
  - `ILipSyncProvider.GetLipSyncValues(Span<float>)` を呼び、値合計が 1e-4 未満なら IsValid=false を返す。
  - EditMode フェイク ILipSyncProvider で無音 / 発話時の IsValid 切替を検証する。
  - 観測完了条件: 無音時 `TryWriteValues == false`、発話時 `true` かつ output に provider の値がそのままコピーされることが EditMode で通る。
  - _Requirements: 5.3, 5.6, 5.7_
  - _Depends: 4.1_
  - _Boundary: Adapters.InputSources.LipSyncInputSource_

- [x] 7. Adapters 層: JSON 統合とファクトリ
- [x] 7.1 `InputSourceDto` と `InputSourceOptionsDto` 階層を定義する
  - `InputSourceDto { id, weight, optionsJson }` と、基底 `InputSourceOptionsDto` + 派生 `OscOptionsDto`（`stalenessSeconds`）/ `ExpressionTriggerOptionsDto`（`maxStackDepth`）/ `LipSyncOptionsDto`（空）を定義する。
  - `JsonUtility.FromJson<TOptions>` の往復が機能することを EditMode で確認する単純テストを先に書く。
  - 観測完了条件: `{"stalenessSeconds":2.5}` を `OscOptionsDto` に逆シリアライズすると `stalenessSeconds == 2.5f` となることが EditMode で通る（Critical 2）。
  - _Requirements: 3.1, 3.7_
  - _Boundary: Adapters.Json.Dto.InputSourceDto, InputSourceOptionsDto_

- [x] 7.2 `SystemTextJsonParser` で `layers[].inputSources` を必須フィールドとして parse する
  - `inputSources` 欠落 / 空配列が `FormatException` を投げることを EditMode で先に検証する Red テストを書く。
  - `JsonSchemaDefinition` に `"inputSources"` と `"id" / "weight" / "options"` 定数を追加する。
  - options フィールドは生 JSON サブ文字列として抜き出し `InputSourceDto.optionsJson` に保持する（JsonUtility の 1 段ネスト制約を回避）。
  - 観測完了条件: 欠落時は `FormatException`、正常 JSON は `InputSourceDto[]` に変換され options が raw JSON 文字列で保持されることが EditMode で通る。
  - _Requirements: 3.1, 3.2, 7.3_
  - _Depends: 7.1_
  - _Boundary: Adapters.Json.SystemTextJsonParser_

- [x] 7.3 未知 id / 重複 id / regex 違反時の警告 + skip / last-wins を実装する
  - 未知 id（Factory 未登録）で警告 + skip、重複 id で警告 + 最後を採用、regex 違反で警告 + skip、のそれぞれを EditMode で検証する。
  - 観測完了条件: 各シナリオで LogAssert が期待どおりの警告を捕捉し、profile ロード自体は継続することが通る。
  - _Requirements: 3.3, 3.4, 1.7_
  - _Depends: 7.2_
  - _Boundary: Adapters.Json.SystemTextJsonParser_

- [x] 7.4 順序保持と round-trip 安定性を実装する
  - `SerializeProfile` → `ParseProfile` → `SerializeProfile` が同一文字列を返すことを EditMode で検証する。
  - `inputSources` の宣言順が保持され、既定値（weight=1.0）の省略規則が一貫することを確認する。
  - 観測完了条件: 複数の代表プロファイルで round-trip 文字列等価が成立することが EditMode で通る。
  - _Requirements: 3.5, 8.4_
  - _Depends: 7.2_
  - _Boundary: Adapters.Json.SystemTextJsonParser_

- [x] 7.5 `InputSourceFactory` のディスパッチと options 型付きデシリアライズを実装する
  - 予約 id ごとに DTO 型をマップし、`TryDeserializeOptions(id, optionsJson)` が `JsonUtility.FromJson<TOptions>` を呼ぶ実装を追加する。
  - `TryCreate(id, optionsDto, blendShapeCount, profile)` が対応アダプタを生成し、未登録 id で null を返すこと（呼出側が警告 + skip）を EditMode で検証する。
  - 観測完了条件: `factory.TryDeserializeOptions("osc", "{\"stalenessSeconds\":2.5}")` が `OscOptionsDto{stalenessSeconds=2.5}` を返し、`TryCreate("osc", ...)` が `OscInputSource` インスタンスを返すことが EditMode で通る。
  - _Requirements: 3.1, 3.3, 3.7, 5.7_
  - _Depends: 7.1, 6.6, 6.7, 6.3, 6.4_
  - _Boundary: Adapters.InputSources.InputSourceFactory_

- [x] 7.6 `RegisterExtension<TOptions>` によるサードパーティ拡張登録を実装する
  - `x-` プレフィックス id の登録と、対応する DTO 型での JSON 逆シリアライズが動作することを EditMode で検証する。
  - 観測完了条件: テスト用 `x-test-sensor` を登録し、options JSON が typed DTO に正しくデシリアライズされ、creator が呼ばれて `IInputSource` を返すことが通る。
  - _Requirements: 1.7, 3.7_
  - _Depends: 7.5_
  - _Boundary: Adapters.InputSources.InputSourceFactory_

- [x] 8. Application 層と実行時配線
- [x] 8.1 `LayerUseCase` の内部実装を Aggregator 委譲へ差し替える（API 非破壊）
  - 既存公開 `UpdateWeights` / `GetBlendedOutput` / `SetLayerWeight` のシグネチャを維持したまま内部を Aggregator + WeightBuffer + Registry 呼出しに差し替える。
  - 既存 `LayerUseCaseTests` が全て Green のまま通ることを確認する（旧 `_layerTransitions` への依存があればテストを契約ベースに書き直す）。
  - 観測完了条件: refactor 前後で `GetBlendedOutput` の返値が代表入力について一致することが EditMode 契約テストで通る。
  - _Requirements: 2.6, 6.4, 7.1_
  - _Depends: 5.1, 5.4_
  - _Boundary: Application.UseCases.LayerUseCase_

- [x] 8.2 `FacialController` 初期化で Factory / Registry / Aggregator / WeightBuffer / TimeProvider を組み立てる
  - プロファイルロード時に `InputSourceFactory.TryCreate` で全アダプタをインスタンス化し、`LayerInputSourceRegistry` に登録、`LayerInputSourceWeightBuffer` を初期化、`LayerInputSourceAggregator` を `LayerUseCase` に注入する配線を追加する。
  - `UnityTimeProvider` は単一インスタンスで DI コンテナ / 初期化経路で共有する。
  - プロファイル再ロード時は Registry / WeightBuffer を Dispose して再構築する。
  - 観測完了条件: `FacialController` 起動後 `FacialControllerTests` の既存シナリオが通り、プロファイル差替で Aggregator が再構築されることが PlayMode で確認できる。
  - _Requirements: 2.6, 6.5, 7.1, 5.7_
  - _Depends: 6.1, 7.5, 8.1_
  - _Boundary: Application/Adapters 初期化経路_

- [x] 8.3 ランタイム weight 変更 API を `FacialController` 経由で公開する
  - 単発 `SetInputSourceWeight(layer, source, w)` とバルク `BeginInputSourceWeightBatch(): IDisposable` を公開し、WeightBuffer.BulkScope に委譲する。
  - 任意スレッドからの呼出が次フレームの `Aggregate` で観測されることを PlayMode 統合テストで確認する。
  - 観測完了条件: メインスレッド外スレッドから `SetInputSourceWeight` を呼んでも次フレームの BlendShape 出力に反映されることが PlayMode で通る。
  - _Requirements: 4.1, 4.2, 4.4, 4.5_
  - _Depends: 3.3, 8.2_
  - _Boundary: Application/Adapters 公開 API_

- [x] 9. Editor 層
- [x] 9. `FacialProfileSO` Inspector に読取専用の InputSources ビューを追加する
  - UI Toolkit の `ListView` で `LayerInputSourceAggregator.GetSnapshot()` を表示する `FacialProfileSO_InputSourcesView` を実装する。
  - Play Mode 外ではプレースホルダを表示し、Play Mode 中は現在の `FacialController` への参照をキャッシュ（`EditorApplication.update` 約 100ms 間隔で健全性確認）して、Inspector repaint は O(1) で済ませる。
  - Category=Controller のまま Keyboard デバイスのみの `InputBindingProfileSO` を参照しているレイヤーがあれば Inspector 上でも注意喚起を表示する（best-effort）。
  - 観測完了条件: Play Mode 中にウェイト変更が Inspector に 1 秒以内で反映され、大規模シーンでも repaint のたびに `FindObjectOfType` が走らないことが手動 Editor テストで確認できる。
  - _Requirements: 8.6, 7.4_
  - _Depends: 5.5, 8.2_
  - _Boundary: Editor.Inspectors.FacialProfileSO_InputSourcesView_

- [x] 10. 統合テスト・性能テスト・移行
- [x] 10.1 EditMode 統合テスト: Fake OSC + Fake Controller の 50/50 合成を end-to-end で検証
  - Fake `OscInputSource` と Fake `ControllerExpressionInputSource` を同レイヤーに 0.5/0.5 で配置し、Aggregator → LayerBlender を通した最終出力が手計算と一致することを検証する。
  - 要件 boundary のユースケース 1〜3（状況切替 / 重み付き / 特定入力源固定）をパラメトリックに検証する。
  - 観測完了条件: 3 ユースケースそれぞれの期待出力が EditMode 統合テストで通り、全パイプラインが GC フリーで完走する。
  - _Requirements: 2.6, 5.1, 5.2, 8.2_
  - _Depends: 5.4, 6.3, 6.6_
  - _Boundary: Tests/EditMode/Integration_

- [x] 10.2 EditMode: weight=1 単独ソース時の exact 出力契約を検証する
  - 1 source が weight=1、他 source が weight=0 のとき、最終出力がその source の値と浮動小数点誤差範囲で一致することを検証する。
  - 観測完了条件: 複数の代表値で `Mathf.Approximately` 相当の比較が通る EditMode テストが通る。
  - _Requirements: 7.2_
  - _Depends: 5.1_
  - _Boundary: Tests/EditMode/Domain_

- [x] 10.3 EditMode: profile 欠落時に暗黙 legacy フォールバックしないことを検証する
  - `inputSources` 欠落 JSON の parse が `FormatException` を投げ、Aggregator が暗黙に既存 Expression パイプラインへフォールバックしないことを契約テストで確認する。
  - 観測完了条件: `FormatException` が発生し、例外メッセージに欠落フィールド名が含まれることが通る。
  - _Requirements: 3.2, 7.3, 7.4_
  - _Depends: 7.2_
  - _Boundary: Tests/EditMode/Adapters_

- [x] 10.4 PlayMode Performance: per-frame `SetWeight` 1000 回 × 60 frame で GC ゼロを検証する
  - `Profiler.GetTotalAllocatedMemoryLong` 差分でゼロアロケーションを確認する。
  - verbose logging OFF 前提で、Registry / WeightBuffer / Aggregator の全ホットパスが 0-alloc であることを確認する。
  - 観測完了条件: 60 フレーム × 1000 Set のシナリオで managed 差分が 0 バイトとなる PlayMode Performance テストが通る。
  - _Requirements: 6.1, 6.2_
  - _Depends: 3.3, 5.1, 8.3_
  - _Boundary: Tests/PlayMode/Performance_

- [x] 10.5 (P) PlayMode Performance: 10 体 × 3 layer × 4 source × 200 BlendShape で 60 FPS 維持
  - 複数 `FacialController` インスタンスを同時稼働させ、フレーム時間が目標内に収まることを計測する。
  - 観測完了条件: 平均フレーム時間が 16.6ms 以内、スパイクが許容範囲に収まることを PlayMode Performance テストで確認できる。
  - _Requirements: 6.3, 6.5_
  - _Depends: 8.2_
  - _Boundary: Tests/PlayMode/Performance_

- [x] 10.6 (P) PlayMode: OSC staleness タイムアウト実時間動作と Bulk/Single 混在の原子性を検証
  - 実 `UnityTimeProvider` 下で `stalenessSeconds=1.0` の 1 秒超過で `IsValid=false` になることを確認する。
  - 同フレーム内 Bulk と Single Set の混在が次フレームで期待どおり観測できることを確認する。
  - 観測完了条件: 上記 2 シナリオが PlayMode 統合テストで通る。
  - _Requirements: 4.5, 5.5_
  - _Depends: 6.6, 8.3_
  - _Boundary: Tests/PlayMode/Integration_

- [x] 10.7 既存サンプルプロファイルを `inputSources` 必須化に合わせて移行する
  - リポジトリ同梱の既定プロファイル 3 種（emotion / lipsync / eye）に、design.md の推奨テンプレートに従って `inputSources` を明示的に追記する。
  - 移行後のプロファイルが新 parser で正しくロードされ、Aggregator が起動することを EditMode で確認する。
  - 観測完了条件: 既存テストフィクスチャ JSON が全て新 parser で `FormatException` を出さず、3 ユースケースのシナリオテストが Green のまま通る。
  - _Requirements: 3.2, 3.6, 7.3, 7.4_
  - _Depends: 7.2, 8.1_
  - _Boundary: サンプルプロファイル JSON_

- [x] 10.8 (P) CHANGELOG / 移行ガイドに preview 破壊的変更を明記する
  - `inputSources` 必須化、`legacy` 予約 ID 廃止、`InputBindingProfileSO.InputSourceCategory` の既定値 Controller による暗黙的挙動変更、の 3 項目を CHANGELOG に追記する。
  - `docs/` 配下の移行ノートに、既存 `InputBindingProfileSO` Asset を Project ビューでレビューし Category を正しく再設定する手順を記述する。
  - 観測完了条件: CHANGELOG と移行ノートの該当エントリが存在し、preview 破壊的変更ポリシー FR-001 への参照リンクが含まれていることが手動レビューで確認できる。
  - _Requirements: 3.6, 7.4_
  - _Depends: 6.2, 7.2_
  - _Boundary: ドキュメント_

- [x]* 10.9 (P) 診断ログ rate-limiter の長時間安定性の補助テスト
  - verbose logging を 10 分間 ON にした状態で、想定レート（per-layer per-second）以上のログが出ないことを EditMode で検証する補助テスト。
  - 観測完了条件: rate-limit が長時間でもドリフトしないことが EditMode テストで通る（MVP 後に追加可の補助カバレッジ）。
  - _Requirements: 8.5_
  - _Depends: 5.6_
  - _Boundary: Tests/EditMode/Domain_

---

## Requirements Coverage Matrix

| Requirement | Covered By Tasks |
|-------------|------------------|
| 1.1 | 2.1, 4.1, 5.2 |
| 1.2 | 3.5, 3.6 |
| 1.3 | 5.2 |
| 1.4 | 2.1, 4.1, 5.2 |
| 1.5 | 2.1 |
| 1.6 | 4.2, 4.3, 6.4 |
| 1.7 | 2.3, 7.3, 7.6 |
| 1.8 | 4.2, 6.4 |
| 2.1 | 2.4, 3.1 |
| 2.2 | 5.1 |
| 2.3 | 5.1 |
| 2.4 | 5.3 |
| 2.5 | 3.1 |
| 2.6 | 5.4, 8.1, 8.2, 10.1 |
| 2.7 | 5.4 |
| 3.1 | 7.1, 7.2, 7.5 |
| 3.2 | 7.2, 10.3, 10.7 |
| 3.3 | 7.3, 7.5 |
| 3.4 | 7.3 |
| 3.5 | 7.4 |
| 3.6 | 10.7, 10.8 |
| 3.7 | 7.1, 7.5, 7.6 |
| 4.1 | 3.1, 8.3 |
| 4.2 | 3.2, 8.3 |
| 4.3 | 3.4, 3.6 |
| 4.4 | 3.2, 8.3 |
| 4.5 | 3.3, 8.3, 10.6 |
| 5.1 | 6.3, 6.4, 10.1 |
| 5.2 | 6.6, 10.1 |
| 5.3 | 6.7 |
| 5.4 | 6.5, 6.6 |
| 5.5 | 2.2, 6.1, 6.5, 6.6, 10.6 |
| 5.6 | 6.7 |
| 5.7 | 6.2, 6.3, 6.4, 6.6, 6.7, 7.5, 8.2 |
| 6.1 | 10.4 |
| 6.2 | 3.5, 10.4 |
| 6.3 | 5.1, 10.5 |
| 6.4 | 5.4, 8.1 |
| 6.5 | 3.5, 8.2, 10.5 |
| 7.1 | 5.4, 8.1, 8.2 |
| 7.2 | 10.2 |
| 7.3 | 7.2, 10.3, 10.7 |
| 7.4 | 6.2, 9, 10.3, 10.7, 10.8 |
| 8.1 | 2.4, 5.5 |
| 8.2 | 1.1, 2.2, 6.1, 6.6, 10.1 |
| 8.3 | 5.5 |
| 8.4 | 7.4 |
| 8.5 | 5.6, 10.9 |
| 8.6 | 9 |

---

## Parallel Execution Notes

- **Foundation-phase clusters**: 1.2 は 1.1 の後、独立に着手可（asmdef 構成のみ）。
- **Domain value-objects cluster** (2.1 / 2.2 / 2.3 / 2.4): いずれも独立ファイル・独立責務のため並行可。
- **Registry runtime API** (3.6) は 3.5 完了後に分離可。3.5 と 3.3 / 3.4 は同一 WeightBuffer / Registry を触るため sequential。
- **Adapters cluster** (6.1 / 6.2 / 6.4 / 6.7): それぞれ別コンポーネント。6.3 と 6.6 は 4.2 / 4.1 / 6.5 への依存があり sequential 寄り。
- **Performance & docs cluster** (10.5 / 10.6 / 10.8 / 10.9): 異なるテストシーン・ドキュメントファイルのため並行可。
- `- [x]*` の 10.9 は MVP 後に回せる補助テスト（Req 8.5 の rate-limit 契約は 5.6 で既に検証済み）。
