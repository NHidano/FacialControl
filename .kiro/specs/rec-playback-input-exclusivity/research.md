# Research & Design Decisions — rec-playback-input-exclusivity

## Summary
- **Feature**: `rec-playback-input-exclusivity`
- **Discovery Scope**: Extension（既存 rec / core / inputsystem への統合改修。light discovery + 実コード検証）
- **Key Findings**:
  - ライブの `ExpressionInputSourceAdapter` は sink をフィールド直参照（`_keyboardSink` / `_controllerSink`）で押すため、registry `Replace` ではライブトリガーを遮断できない。遮断は core の `ExpressionTriggerInputSourceBase` 自身に持たせるしかない
  - `RecTriggerInjector` は現状 `EstablishBaseline` + `InjectTriggerOn/Off` のみで排他機構がなく、`PlaybackUseCase.StopPlayback` はアナログ側 `EndInjection` のみ呼ぶ（トリガー側に解放点が存在しない）
  - `RecAnalogInjector.BeginInjection` は baseline の `AnalogEntries` のみ走査するため、記録後に追加されたアナログ / gaze ソースは再生中も素通しになる（残余ギャップ）

## Research Log

### ライブトリガー遮断の実現点（core ゲートの必然性）
- **Context**: 再生中にコントローラ操作が混入する根本原因の特定
- **Sources Consulted**: `ExpressionInputSourceAdapter.cs`（inputsystem）、`RecTriggerInjector.cs`、rec-recording-playback design.md:302/798
- **Findings**:
  - rec-recording-playback は「トリガーは差し替えず原本インスタンスを直接駆動」（design.md:302）を設計判断として採用。この帰結として、ライブの `TriggerOn/Off` も同一原本に素通しで届く
  - adapter は `ResolveSink` で sink フィールドを直接返すため、registry を経由する遮断は構造的に不可能
  - `SetTriggerEventObserver`（観測面の後付け注入）が「core に面だけ追加し rec を知らせない」前例として存在する
- **Implications**: 遮断ゲートは `ExpressionTriggerInputSourceBase` の per-instance 状態として追加する（Approach A）。rec-recording-playback Req 6.1（観測・注入面の追加に限定）の枠内に収まる

### 既存注入経路とライフサイクルの非対称
- **Context**: Req 2.4（トリガー注入ポートのアナログ対称化）の実装コスト評価
- **Sources Consulted**: `ITriggerInjectionPort.cs`、`IAnalogInjectionPort.cs`、`PlaybackUseCase.cs`、`RecCharacterBinding.cs`
- **Findings**:
  - `ITriggerInjectionPort` の利用箇所は `RecCharacterBinding.EnsurePlaybackSession`（L217-222）と `PlaybackUseCaseTests` の Fake のみ。`BeginInjection/EndInjection` 形への再形成の破壊的変更コストは低い
  - `RecAnalogInjector.BeginInjection` は冒頭で自己 `EndInjection()` を呼ぶ再入吸収パターンを既に持つ。トリガー側も同型にすれば Completed→再 Start の再入が自然に吸収される
  - `PlaybackUseCase.Load` 冒頭の `StopPlayback()` により、Load 経路も新しい解放順序に自動的に乗る
- **Implications**: `ITriggerInjectionPort` を `BeginInjection(baseline)` / `InjectTriggerOn/Off` / `EndInjection` に再形成し、`EstablishBaseline` を `BeginInjection` へ吸収する

### アナログ側の残余ギャップ（ベースライン外ソース）
- **Context**: Req 3（全ソースへの拡大）の実装方式
- **Sources Consulted**: `RecAnalogInjector.cs`、`RecPlaybackAnalogSource.cs`、`RecCharacterBinding.CaptureBaseline`
- **Findings**:
  - `RecPlaybackAnalogSource` は `IInjectedInputSource` 占有規則・`ReplacedSource` 復元・axisCount 固定を既に備え、ベースライン外ソースにもそのまま流用できる
  - ベースライン外ソースの axisCount はライブソースの `AxisCount` から構築できる。gaze は 2 軸アナログとして統一済み（rec-recording-playback の既存判断）
  - `EndInjection` は `_attachedSources` 全件を復元するため、装着元（baseline / registry 走査）を区別せず同一経路で復元できる
- **Implications**: `BeginInjection` の走査を「baseline ∪ registry 登録済み全アナログソース」へ拡大するだけでよい。復元系は無改修

### Toggle 状態整合のテスト継ぎ目
- **Context**: Req 5.6（Fake のみ EditMode）と Req 7 の両立
- **Sources Consulted**: `ExpressionInputSourceAdapter.cs`（`BindingEntry` / `DispatchPerformed` / `Tick`）、既存テスト配置、各パッケージの `InternalsVisibleTo` 前例
- **Findings**:
  - 既存の adapter テストは PlayMode（`InputTestFixture`）。`InputAction.CallbackContext` に依存したままでは EditMode 検証不能
  - Toggle 反転判定と解除時同期は `CallbackContext` に依存しない純ロジックに切り出せる（必要情報は `IsTriggerInputSuspended` / `entry.IsActive` / `sink.ActiveExpressionIds` のみ）
  - core（`Hidano.FacialControl.Domain`）と lipsync に `InternalsVisibleTo` の前例あり。inputsystem Runtime asmdef には未設定（追加が必要）
  - `Tick(float)` は FacialController から毎フレーム呼ばれるため、遮断解除エッジ検出のポーリング地点に使える
- **Implications**: 反転抑止・解除時同期を internal な純ロジック（`ToggleStateReconciler`）へ切り出し、`InternalsVisibleTo` で EditMode テストから検証する

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| A: 既存コンポーネント拡張 | `ExpressionTriggerInputSourceBase` に per-instance ゲート + 注入面を直接追加 | 既存構造（観測面の前例）に素直、未使用時コスト bool 分岐 1 個、rec-recording-playback Req 6.1/6.5 充足 | 基底クラスの API 面がやや広がる | **採用（ユーザー決定）** |
| B-2: 集中ゲートサービス | core に遮断状態を集約するサービスを新設し各ソースが参照 | 遮断状態の一元管理 | 新規依存の注入経路が増え、既存ソース全てに参照配線が必要。Req 6.5 の「未使用時不変」証明が難しくなる | 不採用 |
| C: rec 側一貫性クラス分離 | rec 側に suspend/resume の一貫性を担う専用クラスを新設 | rec 内で完結 | ライブ adapter の sink 直参照は rec からは遮断できず、根本原因を解決しない | 不採用 |

## Design Decisions

### Decision: ベースライン外アナログソースの seed 値 = 0 埋め
- **Context**: Req 3.3。記録ベースライン外のソースを差し替える際の初期値方式
- **Alternatives Considered**:
  1. 現在消費値での凍結 — 再生開始時の表情の跳びがない
  2. 0 埋め（gaze は中立 0,0） — 再生結果が環境の現在値に依存せず決定的
- **Selected Approach**: 常に 0 埋め（**ユーザー決定**）
- **Rationale**: 再生結果の決定的再現を優先。ライブ値が読めない場合（`TryReadAxes` 失敗 / `IsValid == false`）のフォールバックも同一挙動に統合され、分岐が消える
- **Trade-offs**: 再生開始時にベースライン外ソース由来の表情跳びが起こり得る（許容）
- **Follow-up**: gaze 中立 (0,0) が 0 埋めで表現されることをテストで確認

### Decision: Toggle 状態整合 = 遮断中抑止 + 解除時同期の両方
- **Context**: Req 7.1。遮断中の Toggle 押下で `entry.IsActive` が実スタックと乖離する問題
- **Alternatives Considered**:
  1. 遮断中の反転抑止のみ — 再生の baseline リセットでスタックが変わった場合に乖離が残る
  2. 解除時同期のみ — 遮断中に UI 等から `IsActive` を読む診断経路が一時的に不整合
- **Selected Approach**: 両方実装（**ユーザー決定**）。(a) 遮断中は反転自体を抑止、(b) 遮断解除エッジを `Tick` で検出し `sink.ActiveExpressionIds` と同期
- **Rationale**: 抑止で遮断中の乖離増加を止め、同期で baseline リセット由来の乖離も吸収する。二重の防御で Req 7.2（空振りなし）が方式単体の穴に依存しない
- **Trade-offs**: inputsystem に遮断状態の参照（`IsTriggerInputSuspended` の sink 直読み）が入る（rec-recording-playback Req 6.7 の inputsystem 限定緩和）
- **Follow-up**: 解除と同一フレームの押下順序（InputSystem 更新 → LateUpdate Tick）の整理をテストで担保

### Decision: 排他解放点は StopPlayback のみ（自然完了では維持）
- **Context**: Req 4.3。Completed 到達時の排他の扱い
- **Selected Approach**: 自然完了では解放せず、`StopPlayback` を唯一の解放点とする
- **Rationale**: アナログ側の既存挙動（Completed でも `EndInjection` を呼ばない）と対称。最終イベント到達後も「再生された状態」を保持するのが rec の既存 UX
- **Trade-offs**: Completed 後もライブ操作が効かない期間が続く（明示的な Stop が必要）。既存挙動と同一のため新たな驚きはない

## Risks & Mitigations
- ゲート追加による既存挙動の意図しない変化 — `TriggerOn/Off` の本体を private コアへ切り出す純リファクタ + 既存 `ExpressionTriggerInputSourceBaseTests` の全緑維持で担保
- 再生中に Unregister されたソースの Resume 漏れ — `RecTriggerInjector` が Suspend したソースの参照を保持し、registry 非経由で Resume する
- Toggle 同期の解除エッジ検出漏れ（keyboard / controller が同一インスタンスのケース） — `ReferenceEquals` による重複処理スキップをテストで固定
- `InternalsVisibleTo` 追加による意図しない internal 依存の拡大 — 対象を inputsystem EditMode テストアセンブリのみに限定

## References
- `.kiro/specs/rec-recording-playback/design.md` — 上書き対象の設計判断（:302/:306/:798）と Req 6 制約群
- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Services/ExpressionTriggerInputSourceBase.cs` — ゲート追加対象の現行実装
- `FacialControl/Packages/com.hidano.facialcontrol.rec/Runtime/Adapters/Playback/` — 既存注入アダプタ（流用パターンの正）
- `FacialControl/Packages/com.hidano.facialcontrol.inputsystem/Runtime/Adapters/InputSources/ExpressionInputSourceAdapter.cs` — Toggle 分岐と Tick ポーリング地点
