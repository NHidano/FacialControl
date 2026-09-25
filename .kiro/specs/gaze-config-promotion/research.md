# Research & Design Decisions

> **Note (post-implementation amendment, 2026-05-06)**: 本 research は `profile.json` の `schemaVersion` を `"2.0"` → `"2.1"` に bump する前提で記述しているが、preview.1 リリース前段階のため最終実装では `"1.0"` に統一された。本文の `"2.0"` / `"2.1"` 言及は historical な調査結果として残し、最終仕様は `"1.0"` strict として読み替えること。

## Summary

- **Feature**: `gaze-config-promotion`
- **Discovery Scope**: Extension（既存 SO / AdapterBinding 構造の責務再配置）
- **Key Findings**:
  - `FacialCharacterProfileSO` には `[SerializeReference] AdapterBindingBase` 経由で `InputSystemAdapterBinding` がぶら下がる構造で、現状 `_gazeConfigs` は binding 内部に格納されている。SO ルートに同名フィールドを新設しても `[SerializeReference]` 構造に干渉しないため、移植コストは小さい。
  - `AdapterBindingBase.OnStart(in AdapterBuildContext ctx)` は Domain 層に置かれており、`AdapterBuildContext` は所有 SO への参照（子 binding から親 SO 方向の参照、いわゆる back-reference）を持たない。runtime で SO ルートの `_gazeConfigs` を参照するには (a) `AdapterBuildContext` への field 追加 / (b) `OnStart` 引数の拡張 / (c) runtime 構築側で SO から `_gazeConfigs` を抜き出して binding に注入、のいずれかが必要。本 spec は最小破壊かつ Domain → Adapters 依存方向違反を避ける (c) 「親が子に値を push する injection パターン」を採用する。
  - `FacialCharacterProfileConverter` / `FacialCharacterProfileExporter` は v2.0 では `gaze_configs[]` を扱わず、`InputSystemAdapterBinding` 内部の YAML serialization にしか存在しない。JSON ⇄ SO ラウンドトリップに gaze を載せるには Converter/Exporter/DTO 三点改修が必要。
  - `SystemTextJsonParser.ParseProfileSnapshotV2` は schemaVersion を strict で `"2.0"` 固定で要求するため、bump にあわせ parser 側の許容バージョンも改修必須。
  - 既存 Inspector は Expression 行内に Eye/Look フィールドを混入させる構造（`BuildEyeLookFields` / `BuildGazeClipField`）になっており、これらは新セクション分離後に dead code として削除する必要がある。

## Research Log

### Topic: SO ルートへの gaze データ追加と SerializeReference の影響

- **Context**: `FacialCharacterProfileSO` は `[SerializeReference] List<AdapterBindingBase> _adapterBindings` を持つ。`_gazeConfigs` を SO ルートに `[SerializeField] List<GazeBindingConfig>` として追加した場合、既存の SerializeReference YAML 構造（`references.RefIds`）に副作用がないかを確認した。
- **Sources Consulted**: 既存 `MultiSourceBlendDemoCharacter.asset` YAML、`FacialCharacterProfileSO.cs`、`AdapterBindingBase.cs` の Slug field 定義。
- **Findings**:
  - `[SerializeField]` フィールドは SO ルート direct fields として平坦に書き出され、`[SerializeReference]` の `references` ブロックとは独立。既存 YAML の `_adapterBindings` 構造を破壊しない。
  - `GazeBindingConfig` は `[Serializable]` plain class で UnityEngine.Object 派生ではないため、`List<GazeBindingConfig>` は値配列として inline 出力される。`AnimationClip lookLeftClip` 等の Object 参照は file ID で sub-reference される（YAML 構造は既存の binding 内部と同等）。
- **Implications**:
  - SO ルートへの field 追加は YAML 構造の一段増加で済む。Sample asset 移植は「既存 binding 内部の `_gazeConfigs` ブロックを SO ルート直下へ移動 + binding 側を `_gazeInputBindings` 形式へ書換」の YAML 直手術で完結する。
  - `[SerializeField]` のため Inspector default drawer から自動表示されるが、本 spec では `BuildGazeConfigsSection` で UI Toolkit カスタム描画する。

### Topic: AdapterBinding が SO ルート `_gazeConfigs` を取得する経路

- **Context**: `InputSystemAdapterBinding.OnStart(in AdapterBuildContext ctx)` は Domain の `AdapterBuildContext` から service 群を受け取るが、自身を所有する `FacialCharacterProfileSO` への参照（子 binding から親 SO 方向、いわゆる back-reference）は持たない。SO ルートの `_gazeConfigs` を runtime で参照する経路を検討した。なお Domain 層の `AdapterBindingBase` から Adapters 層の `FacialCharacterProfileSO` を直接参照する設計は Clean Architecture の依存方向違反になるため、子→親の参照を確立せず親→子に値を push する injection パターンを優先する。
- **Sources Consulted**: `AdapterBindingBase.cs`、`AdapterBuildContext.cs`、`FacialController.cs`（runtime build 経路、未読のため推測ベース。実装時に最終確認する）。
- **Alternatives Considered**:
  1. `AdapterBuildContext` に `IReadOnlyList<GazeBindingConfig> RootGazeConfigs` を追加。Domain 層が `GazeBindingConfig`（Adapters 名前空間）を import することになり、依存方向の反転が起きる。
  2. `AdapterBindingBase.OnStart` の signature を拡張して SO 参照を渡す。Domain 層の純度を傷つけ、他 binding（OSC 等）にも波及。
  3. **採用**: runtime 構築コード（FacialController 系）が SO から `_gazeConfigs` を取り出し、`InputSystemAdapterBinding.Configure(... gazeConfigs)` 経由で binding に注入する。binding は構築時に snapshot として `IReadOnlyList<GazeBindingConfig>` を保持し、`OnStart` 内で `_gazeInputBindings` と pairing する。
- **Findings**:
  - 既存の `Configure` API は `IReadOnlyList<GazeExpressionConfig>` を受け取っており、これを `IReadOnlyList<GazeBindingConfig>` に置き換えれば最小変更。
  - Inspector 経由の dev 時シリアライズは binding 内部に `_gazeConfigs` を持たないため、テストや runtime 注入専用のフィールド（`[NonSerialized] private IReadOnlyList<GazeBindingConfig> _injectedGazeConfigs`）として保持できる。runtime 構築側は SO の `_gazeConfigs` を読み取って Configure する。
- **Implications**:
  - Domain 層を変更せずに済む。
  - runtime 結線の責務は「SO → binding に gaze configs を注入する」コードに集中する。テスト（PlayMode）からも Configure で同経路を再現できるため testability も向上。
  - 不一致 binding は warning + skip で fail-safe（Req 1.6, 10.4）。

### Topic: profile.json schemaVersion bump と strict parser

- **Context**: 既存 `SystemTextJsonParser.ParseProfileSnapshotV2` は schemaVersion `"2.0"` 以外を `NotSupportedException` で拒否する。schemaVersion を `"2.1"` に bump した際、parser 側の許容バージョン拡張方式を決定する必要がある。
- **Sources Consulted**: `SystemTextJsonParser.cs:85`、`ProfileSnapshotDto.cs`。
- **Alternatives Considered**:
  1. `SchemaVersionV2 = "2.0"` を `"2.1"` に書き換え（旧 v2.0 を一律拒否）。preview 段階のため最も簡潔。
  2. 許容バージョン集合 `{"2.0", "2.1"}` で互換読み込み（`gaze_configs[]` は v2.0 では空扱い）。
  3. schemaVersion ごとに分岐する parser を新設。
- **Selected Approach**: 1. `SchemaVersionV2` を `"2.1"` に書き換え、旧 v2.0 を strict に拒否する。preview 段階で破壊的変更を許容し、migration コードは書かない（Req 2.6）方針と整合。
- **Findings**:
  - `ProfileSnapshotDto` に `gazeConfigs: List<GazeBindingConfigDto>` field を追加し、SO 側 `_gazeConfigs` と双方向に変換する。
  - parser は schemaVersion mismatch を例外で投げる現状動作を維持。runtime crash を避けたい場合は `FacialCharacterProfileSO.LoadProfile` 側で例外 catch → fallback profile を返す既存ガードに乗る。
- **Implications**:
  - 旧スキーマの `profile.json` は読み込み失敗 → fallback profile（SO ルート data）が使われる。SO 自体を新スキーマで保存しなおせば一貫した状態になる。
  - migration-guide には「JSON を hand-edit するか、SO を再保存すれば自動的に新スキーマで書き直される」旨を記載する。

### Topic: Inspector の Undo パイプライン一貫性

- **Context**: 孤児削除（Expression 削除に伴う GazeConfig 削除、Analog→Digital に伴う削除）を 1 ユーザー操作 = 1 Undo step に集約する手法を確認した。
- **Sources Consulted**: Unity Manual の `Undo.IncrementCurrentGroup` / `Undo.SetCurrentGroupName` / `Undo.CollapseUndoOperations`。`FacialCharacterProfileSOInspector.RemoveExpression` の現行実装。
- **Findings**:
  - `serializedObject.ApplyModifiedProperties()` は Unity の標準 Undo に乗るが、複数の `ApplyModifiedProperties` 呼び出しは個別の Undo step になる。
  - `Undo.IncrementCurrentGroup()` で新規グループを開始 → mutate → `Undo.CollapseUndoOperations(currentGroup)` で同 group の操作を 1 step に集約する手法が標準。
  - `Undo.SetCurrentGroupName("Remove Expression with GazeConfig")` で Undo メニューに表示する名称を設定できる。
- **Implications**:
  - `RemoveExpression(int)` と「孤児 GazeConfig 削除」は同じ group ID 内で連続呼び出しし、`CollapseUndoOperations` で 1 step に折り畳む。
  - kind 変更時の自動削除も同様に group 化する。

### Topic: Sample 二重管理同期

- **Context**: `Assets/Samples/.../MultiSourceBlendDemoCharacter.asset` と `Packages/com.hidano.facialcontrol.inputsystem/Samples~/.../MultiSourceBlendDemoCharacter.asset` の二重管理ルール（CLAUDE.md）を新スキーマ移植時にどう運用するかを確認した。
- **Sources Consulted**: `CLAUDE.md` の Samples の二重管理ルール、両 asset の現状 YAML 構造。
- **Findings**:
  - `Samples~/` は Unity のコンパイル対象外のため、scene 結線できるのは `Assets/Samples/` 側のみ。dev 時の動作確認は `Assets/Samples/` でやり、UPM 経由配布は `Samples~/` から行う。
  - 同名ファイルはどちらかを編集したら必ずもう一方を copy 同期する運用。
  - YAML 直手術（GUI で開かず手書き）が許されるのは Unity が両者を import 経路の違い以外で同一として扱うため（schemaVersion bump 先で同じ）。
- **Implications**:
  - 本 spec の Sample 移植タスクは `Assets/Samples/.../MultiSourceBlendDemoCharacter.asset` と `Samples~/.../MultiSourceBlendDemoCharacter.asset`、および双方の `profile.json` の計 4 ファイルを **同一内容で直手術** する。
  - HUD 動作確認は `Assets/Samples/` 側の scene を Play して目視確認する（Sample 二重管理は手動同期、回帰テストは PlayMode integration test と HUD 目視のハイブリッド）。

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| (採用) Side-channel injection | runtime 構築コードが SO → binding に gaze configs を注入。Domain 層は無改修。 | Domain 純度維持 / 最小変更 / testability 向上（Configure で再現） | 「SO ↔ binding 間の暗黙契約」を runtime 側コードでガードする必要 | Req 1.5 / 10.1 と整合。最有力 |
| Context 拡張 | `AdapterBuildContext` に `RootGazeConfigs` を追加 | binding 側 API 1 本で完結 | Domain 層が Adapters 型 `GazeBindingConfig` を import → 依存方向反転 | NG（steering 違反） |
| OnStart signature 拡張 | OnStart に SO 参照を追加 | 全 binding が SO 全量にアクセス可 | 他 binding に副作用波及 / Domain 純度劣化 | NG |

## Design Decisions

### Decision: GazeConfig を SO ルートへ昇格、入力源には expressionId+InputActionRef のみ残す

- **Context**: 現状 `InputSystemAdapterBinding._gazeConfigs: List<GazeExpressionConfig>` が bone path / look-clip 等のキャラ固有情報まで保持しており、入力源と密結合している。
- **Selected Approach**: SO ルートに `_gazeConfigs: List<GazeBindingConfig>` を新設し、binding 側は `_gazeInputBindings: List<InputSystemGazeBinding>`（`expressionId` + `InputActionReference` のみ）に縮退。`GazeExpressionConfig` 派生型は削除。
- **Rationale**: キャラ固有データと入力源の責務を分離。OSC / ARKit native 経路が将来追加されても SO ルート `_gazeConfigs` は再利用可能。
- **Trade-offs**:
  - Pros: 責務分離、JSON schema が自然、Inspector UX も「キャラ設定 → 入力結線」の素直な流れになる。
  - Cons: 破壊的変更（schema bump、Sample 移植、テスト書き換え）が必要。preview 段階のため許容。
- **Follow-up**: Sample 二重管理 drift を CI gate で防げないため、レビュー手順書（HANDOVER 等）に「Samples~ と Assets/Samples を必ず diff 確認する」を明記する。

### Decision: schemaVersion を `"2.0"` → `"2.1"` に bump（互換読み込みなし）

- **Context**: `gaze_configs[]` の root 移動は JSON 構造の breaking change。
- **Selected Approach**: `SystemTextJsonParser.SchemaVersionV2` を `"2.1"` に書き換え。strict に旧 v2.0 を拒否する。例外発生時は既存の `LoadProfile` の catch → `BuildFallbackProfile` フォールバック経路に乗る。
- **Rationale**: preview 段階で破壊的変更を許容（CLAUDE.md 方針）。互換コードを書かないことで保守コストを削減。
- **Trade-offs**:
  - Pros: コード単純、parser ロジック変更最小。
  - Cons: 旧 JSON を読み込もうとすると例外 → SO の Inspector データへフォールバック。エンドユーザー（ライブラリ利用者）の既存 JSON 資産は手で書き換える必要あり。
- **Follow-up**: CHANGELOG / migration-guide に hand-edit 手順を明記。

### Decision: SO → binding 間の gaze configs 注入は runtime 構築コードで行う

- **Context**: Domain 層の `AdapterBuildContext` を変更せずに binding が SO ルート `_gazeConfigs` を取得する経路を確立する必要がある。
- **Selected Approach**: runtime 構築側（FacialController 系）が SO の `_gazeConfigs` を取得し、`InputSystemAdapterBinding.Configure(asset, actionMapName, expressionBindings, gazeInputBindings, gazeConfigs)` で binding に注入する。binding 側は `[NonSerialized] IReadOnlyList<GazeBindingConfig> _injectedGazeConfigs` として保持し、`OnStart` 内で `_gazeInputBindings` と pairing する。
- **Rationale**: Domain 純度維持、最小変更、testability 確保。
- **Trade-offs**:
  - Pros: Domain 層変更不要、Configure 経由でテスト可能。
  - Cons: 「SO ルート ↔ binding」間の暗黙契約は runtime 構築コードに集中する。
- **Follow-up**: 実装時、現行の runtime 構築コード（FacialController.cs / 相当）を読み、SO から `_gazeConfigs` を抜き出して Configure する 1 行を追加する。テストでも同経路を Configure 直叩きで再現する。

### Decision: 自動補完は `_referenceModelProperty.TrackPropertyValue` で監視

- **Context**: 参照モデル割当て時の自動補完（Req 6.1）の検出方法を決定する必要がある。
- **Selected Approach**: UI Toolkit の `VisualElement.TrackPropertyValue(_referenceModelProperty, callback)` で参照モデル変更を検出。callback 内で `_gazeConfigs` を走査し、`leftEyeBonePath` / `rightEyeBonePath` が両方空のものに対して既存の `AutoAssignGazeBonesFromReferenceModel` ロジックを適用。
- **Rationale**: UI Toolkit 標準のプロパティ変更検出 API で、Inspector の rebuild 不要。手動値（非空）は条件分岐で温存。
- **Trade-offs**:
  - Pros: UI Toolkit 標準、過剰な rebuild なし。
  - Cons: callback 内で SerializedObject.Update / ApplyModifiedProperties を正しく扱う必要あり。
- **Follow-up**: 実装時、`previousValue` の保持は `[NonSerialized]` field か callback closure で扱う。

### Decision: Inspector の孤児削除は Undo group で 1 step に集約

- **Context**: Expression 削除に伴う GazeConfig 削除を「1 ユーザー操作 = 1 Undo step」にする必要がある（Req 7.3）。
- **Selected Approach**: `Undo.IncrementCurrentGroup()` + `Undo.SetCurrentGroupName("Remove Expression with GazeConfig")` でグループ開始 → SerializedObject mutate → `Undo.CollapseUndoOperations(group)` で集約。
- **Rationale**: Unity 標準の Undo 集約手法。`serializedObject.ApplyModifiedProperties()` 経由なら Undo 自動記録に乗る。
- **Trade-offs**:
  - Pros: 標準手法、メンテ容易。
  - Cons: グループ開始忘れ / collapse 忘れで Undo がバラバラになる。実装テストでガードする。
- **Follow-up**: Editor test で `Undo.PerformUndo()` 後の状態が両方の削除を巻き戻すことを確認する。

## Risks & Mitigations

- **R1: SO ↔ binding 間 gaze configs 注入の漏れ** — runtime 構築コード（FacialController 系）の改修忘れで binding が空 gaze configs で起動する可能性。Mitigation: PlayMode integration test `OnStart_GazePath_*` を新構造で書き換え、SO から configs を渡す経路を strict に検証する。warn ログも E2E 確認する。
- **R2: Sample 二重管理 drift** — `Assets/Samples/` と `Samples~/` の手動同期忘れ。Mitigation: 実装タスクで「同期チェックリスト」を tasks.md に必須項目として書き、PR レビューでも diff 確認する。preview.2 以降で `Import Sample` 経路へリファクタする backlog 項目化（CLAUDE.md 既記載）。
- **R3: Undo 不整合** — 孤児削除の Undo collapse 忘れで Undo step がバラバラになる。Mitigation: Editor test で `Undo.PerformUndo()` 後の SerializedObject 状態を assert する。
- **R4: schemaVersion bump の strict 拒否でユーザーが詰まる** — 旧 v2.0 JSON が runtime crash しないこと（fallback 経路で対応）、CHANGELOG / migration-guide で手順を案内すること。Mitigation: `LoadProfile` の catch 経路を Editor test でも確認、migration-guide に hand-edit 例を載せる。
- **R5: Inspector の dead code 削除での test 破壊** — `BuildEyeLookFields` / `BuildGazeClipField` 等を削除した後、Editor test が古い VisualElement name を参照していると失敗する。Mitigation: tasks.md で「test 更新を Inspector 改修と同 PR で実施」を必須化。

## References

- [Unity Manual - Undo.IncrementCurrentGroup](https://docs.unity3d.com/ScriptReference/Undo.IncrementCurrentGroup.html)
- [Unity Manual - Undo.CollapseUndoOperations](https://docs.unity3d.com/ScriptReference/Undo.CollapseUndoOperations.html)
- [Unity Manual - VisualElement.TrackPropertyValue](https://docs.unity3d.com/ScriptReference/UIElements.BindingExtensions.TrackPropertyValue.html) — UI Toolkit の SerializedProperty 変更追跡 API
- [Unity Manual - SerializeReference](https://docs.unity3d.com/ScriptReference/SerializeReference.html) — `_adapterBindings` の polymorphic serialize の仕様確認
- リポジトリ内: `.kiro/specs/adapter-binding-architecture/` — 直前 spec の `AdapterBindingBase` / `AdapterBuildContext` 設計
- リポジトリ内: `CLAUDE.md` — Samples の二重管理ルール、JSON ファースト方針
- リポジトリ内: `.kiro/steering/{tech,structure,product}.md` — 全体方針
