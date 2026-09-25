# Research & Design Decisions

## Summary
- **Feature**: `input-source-routing-graph-editor`
- **Discovery Scope**: Extension（既存 Unity 6000.3 brownfield への Editor 拡張追加）
- **Key Findings**:
  - データ層・保存経路・JSON エクスポートは既存資産（`LayerDefinitionSerializable.inputSources` / `FacialCharacterProfileSO._layers/_slots/_defaultOverlays/_adapterBindings`）で完結し、**無改修**で要件 7 を満たす。本機能は既存 SO の「別ビュー」であり、新たな永続化先を導入しない。
  - canonical id 列挙の中核ロジック（`IAdapterBindingDefaultLayerInputs.GetDefaultLayerInputSources` / `AdapterBindingsListView.ResolveDefaultInputSources` / `AddMissingPhonemeSlots` / `PhonemeOverlaySlots.ReservedNames`）は既存。これを GraphView 非依存の純粋ヘルパへ抽出して Inspector と新ウィンドウで共用する（重複ゼロ＝slug 不一致再発の温床を断つ）。
  - **★ Editor 時点で `IInputSourceRegistry` は空**（runtime の `binding.OnStart()` で populate される）。ソースノードのポート id 列挙に registry を使ってはならず、binding 宣言（`GetDefaultLayerInputSources` + `Slug` + slot 宣言）から**静的に算出**する。
  - `UnityEditor.Experimental.GraphView` は Unity 6000.3 で利用可・**asmdef 追加参照不要**（using のみ。`Hidano.FacialControl.Editor.asmdef` は GraphView module を自動参照）。ただし experimental namespace のため API 変更リスクを薄い描画層へ封じ込める。

## Research Log

### GraphView の experimental ステータスと可用性（Unity 6000.3）
- **Context**: 要件 2.2 が基盤に `UnityEditor.Experimental.GraphView` を指定。experimental namespace のリスクと asmdef 影響を確定する必要があった。
- **Sources Consulted**:
  - [GraphView Scripting API](https://docs.unity3d.com/ScriptReference/Experimental.GraphView.GraphView.html)
  - [Edge Scripting API (6000.3)](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Experimental.GraphView.Edge.html)
  - [GraphViewEditorWindow (UnityCsReference)](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Modules/GraphViewEditor/Windows/GraphViewEditorWindow.cs)
- **Findings**:
  - GraphView は Unity 6 系で引き続き `UnityEditor.Experimental.GraphView` に存在し、experimental 扱いのまま（6000.4 ドキュメントでも experimental）。Shader Graph 等が依然これに依存。
  - GraphView module は Editor アセンブリから using のみで利用可能（既存 `Hidano.FacialControl.Editor.asmdef` の references 変更不要を確認）。
- **Implications**: GraphView 依存（`Node` / `Port` / `Edge` / `GraphView` / `MiniMap` / `GraphViewEditorWindow`）は **Presentation 薄層**（`Editor/Windows/Routing/Graph/`）に限定し、id 列挙・配線↔SO 変換・無効 id 検証は GraphView 型を一切参照しない純粋ロジック層へ分離する（要件 10.1）。experimental API が将来変わっても純粋ロジックとテストは影響を受けない。

### カスタム weight ノブ付き Edge と「宙ぶらりんエッジ」（Research Needed A）
- **Context**: 要件 5.4（エッジ上に weight ノブ）と要件 9.2（接続元ポートを持たない赤い破線エッジ）の GraphView での実現方式を確定する。
- **Sources Consulted**:
  - [Edge.cs (UnityCsReference)](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Modules/GraphViewEditor/Elements/Edge.cs)
  - [EdgeControl Scripting API](https://docs.unity3d.com/ScriptReference/Experimental.GraphView.EdgeControl.html)
- **Findings**:
  - `Edge` は `GraphElement`（VisualElement 派生）であり、子 `VisualElement` を `Add` でき、`EdgeControl` の制御点（`from`/`to`）から中点座標を算出して子要素を配置できる。weight ノブは `Edge` 派生クラス（`RoutingEdge`）が中点に配置する小さな VisualElement（ドラッグ Manipulator + `FloatField` ポップ）として実装可能。
  - `Edge.output`/`Edge.input` は null 許容。`output = null` のまま `input` のみ接続した Edge を生成すれば「接続元ポートを持たない宙ぶらりんエッジ」が描ける。USS クラスで破線・赤色を付与（`edgeControl` の線描画は USS でなく `Edge` の `edgeControl.inputColor/outputColor` 経由が確実なため、無効エッジは専用 `DanglingEdge` 派生で色を固定し、from 端点を input ポート手前の固定オフセットに置く）。
- **Implications**:
  - 描画は `RoutingEdge`（通常配線・weight ノブ保持）と `DanglingEdge`（無効 id 可視化・読み取り専用・赤破線）の 2 種の `Edge` 派生で表現。
  - weight 編集の値反映は描画層→純粋ロジック層→SerializedProperty の一方向に流す（描画層は値を保持しない）。

### ソースノードの全ポート静的列挙（Research Needed B）
- **Context**: `GetDefaultLayerInputSources(layerName)` はレイヤー名依存のため、ソースノードが「公開しうる全ポート」を列挙するには全レイヤー名で評価集約する必要がある。binding 契約は変更不可（要件 10.5）。
- **Sources Consulted**: `IAdapterBindingDefaultLayerInputs.cs` / `IAdapterBindingDefaultLayer.cs` / `ULipSyncAdapterBinding.cs:126-138` / `AdapterBindingsListView.cs:231-255`。
- **Findings**:
  - canonical id の供給源は 3 経路: (1) `IAdapterBindingDefaultLayerInputs.GetDefaultLayerInputSources(layerName)`（レイヤー名依存、未該当レイヤーでは空 yield）、(2) legacy `IAdapterBindingDefaultLayer.DefaultLayerInputSourceId`（単一）、(3) overlay slot 宣言（`PhonemeOverlaySlots.ReservedNames` × `Slug`/prefix）。
  - ソースノードの全ポートは「プロファイル内の全レイヤー名 × binding の `GetDefaultLayerInputSources` 評価」の和集合 + legacy 単一 id + overlay slot 由来 id を distinct 集約することで、レイヤー名に依存せず静的に確定できる。
  - uLipSync は overlay レイヤー名でのみ a/i/u/e/o を産出するため、全レイヤー名集約により a/i/u/e/o の 5 ポートが得られる（要件 3.5 を満たす）。
- **Implications**: 純粋ヘルパ `SourcePortEnumerator` が `(IReadOnlyList<AdapterBindingBase> bindings, IReadOnlyList<string> allLayerNames)` を入力に `SourceNodeModel`（binding → ポート id 群）を返す。`AdapterBindingsListView.ResolveDefaultInputSources` のロジックはこのヘルパへ移設し Inspector からも呼ぶ（実装アプローチ A 採用）。

### グラフ操作 ↔ SerializedObject / Undo の同期粒度（Research Needed C）
- **Context**: 要件 7.4 が Undo 登録 + dirty マークを要求。GraphView の連続操作（ドラッグ中の weight 変更等）と Undo グルーピングの粒度を決める。
- **Sources Consulted**: 既存 `AdapterBindingsListView.cs`（`so.ApplyModifiedProperties()` パターン）/ Unity `SerializedObject` / `Undo.RegisterCompleteObjectUndo` の標準慣習。
- **Findings**:
  - 既存 Inspector は `SerializedObject` + `ApplyModifiedProperties` で編集し、配列操作も SerializedProperty 経由。本ウィンドウも同一 `SerializedObject` を介して書き込むことで、Undo 履歴・dirty マーキングが Unity 標準経路で機能し、Inspector との整合も取れる。
  - 離散操作（エッジ追加/切断・属性確定・一括配線）は 1 操作 = 1 Undo グループ（`Undo.RegisterCompleteObjectUndo` または `SerializedObject.ApplyModifiedProperties` の自動 Undo）。weight ドラッグのような連続操作は **ドラッグ開始で 1 回 Undo 記録 → ドラッグ中は `ApplyModifiedPropertiesWithoutUndo` 相当で逐次反映 → 確定時に確定**、の粒度で「ドラッグ 1 回 = Undo 1 段」に collapse する。
- **Implications**: 純粋ロジック層は「SerializedObject に対する宣言的な編集コマンド」（追加/削除/weight 設定/属性設定）を提供し、Undo グルーピングは描画層が操作境界に合わせて制御する。純粋ロジックは `SerializedObject`/`SerializedProperty` のみ知り、GraphView を知らない。

### 同一 SO を Inspector とルーティングウィンドウで同時に開いた場合の双方向反映（Research Needed D）
- **Context**: 要件 1.3（Inspector 併存）と要件 7（同一 SO への保存）の競合可能性。
- **Sources Consulted**: Unity `SerializedObject.Update` / `EditorApplication.update` / `Undo.undoRedoPerformed` の標準パターン、既存 Inspector の UI Toolkit `TrackSerializedObjectValue` 利用状況。
- **Findings**:
  - 各エディタ（Inspector / 本ウィンドウ）は同一アセットに対し独立した `SerializedObject` を持つ。一方の `ApplyModifiedProperties` 後、他方が `Update()` を呼べば最新値を読める。
  - UI Toolkit の `TrackSerializedObjectValue` / `schedule.Execute` ポーリング、または `Undo.undoRedoPerformed` で外部変更を検知し再描画する。
  - 双方が同時に同一プロパティを編集する競合は「後勝ち」で許容（preview 段階の破壊的変更許容方針に整合）。本機能は競合解決ロジックを持たず、外部変更検知→グラフ再構築（rebuild）で最新状態に追従する。
- **Implications**: ウィンドウは編集対象 SO の変更を監視し、外部変更（Inspector 編集・Undo/Redo・binding 追加＝要件 3.6）を検知したらグラフを再構築する。再構築は冪等（同じ SO 状態から同じグラフを生成）であることを純粋ロジック側で保証する。

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| 純粋ロジック層 + GraphView 薄層（採用） | id 列挙・配線↔SO 変換・検証を GraphView 非依存の純粋クラス群に切り出し、描画層は SerializedObject 編集コマンドを発行するだけ | 要件 10.1/10.2 直接充足、experimental API 変更耐性、EditMode テスト容易 | 層分割の設計コスト | 既存 clean architecture の Editor 層内部での再分割 |
| GraphView 直結（描画層に全ロジック内包） | Node/Edge コールバックに直接 SO 編集を書く | 実装が短い | テスト不可（要件 10 違反）、experimental 変更に脆弱、slug 列挙が描画に埋没 | 不採用 |

## Design Decisions

### Decision: 既存 Editor ロジックを純粋ヘルパへ抽出して Inspector と共用（実装アプローチ A）
- **Context**: gap 分析が 3 案提示（A: 抽出共用 / B: 完全新規二重化 / C: 段階導入）。canonical id 列挙・slot 初期化ロジックが Inspector に既存。
- **Alternatives Considered**:
  1. A — `AdapterBindingsListView.ResolveDefaultInputSources` / `AddMissingPhonemeSlots` を GraphView 非依存の純粋ヘルパへ抽出し、Inspector と新ウィンドウで共用。
  2. B — 既存に触れず新ウィンドウ側で id 列挙・slot 初期化を再実装。
  3. C — 純粋ロジック先行（EditMode テスト）→ GraphView 描画後付け → 自動配線/無効可視化を段階導入。
- **Selected Approach**: **A を採用し、実装順序として C のフェーズ分割を併用**。`ResolveDefaultInputSources` 相当を `SourcePortEnumerator`、`AddMissingPhonemeSlots` 相当を `PhonemeSlotInitializer` として純粋ヘルパに移設し、Inspector はこのヘルパを呼ぶ薄いラッパへ置換。新ウィンドウも同ヘルパを使う。
- **Rationale**:
  - slug 不一致再発（MEMORY の最有力真因）の温床は「id 列挙ロジックの二重化」。B はこれを再生産するため不適。A は単一の真実源を保証し、本機能の中心思想（slug 不一致を物理的に発生不能にする）と一致。
  - 純粋ヘルパは EditMode テストで検証でき、要件 10.1/10.2 を直接満たす。
  - 実装リスク（既存 Inspector リファクタ）は、ヘルパ抽出を「振る舞い不変のリファクタ」として先に EditMode テストで保護してから行う（C の段階性で吸収）。
- **Trade-offs**: 既存 Inspector に手を入れるため回帰リスクが B より高いが、ロジック単一化の価値（事故防止）が上回る。回帰は抽出前に既存挙動を固めるテストで担保。
- **Follow-up**: 抽出後、Inspector の `AddMissingPhonemeSlots` / `ResolveDefaultInputSources` 経路が従来と同一結果を返すことを EditMode テストで確認。

### Decision: ソースノードのポート id は registry でなく binding 宣言から静的算出
- **Context**: Editor 時点で `IInputSourceRegistry` は空（runtime populate）。
- **Alternatives Considered**:
  1. registry を Editor で先行 populate して列挙 — runtime lifecycle を Editor で再現する必要があり脆弱・契約違反。
  2. binding 宣言（`GetDefaultLayerInputSources` 全レイヤー集約 + legacy 単一 id + overlay slot 宣言）から静的算出。
- **Selected Approach**: 2。`SourcePortEnumerator` が binding と全レイヤー名のみから決定的にポート id を算出。
- **Rationale**: registry は runtime 専用。Editor で空であることが確定しており、これを使うと「ポートが 0 個」になり機能不全。binding 宣言は Editor で参照可能で決定的。
- **Trade-offs**: binding が runtime で動的に id を増やす実装をした場合、Editor の静的列挙と乖離しうる。ただし既存 binding 群は宣言と runtime 登録が一致する契約のため現状問題なし。
- **Follow-up**: 静的列挙と runtime 登録の乖離は無効 id 可視化（要件 9）で事後的に検知できる。

### Decision: SerializedObject 経由の編集と Inspector 併存時の rebuild 同期
- **Context**: 要件 1.3 / 7.4 / 3.6 / Research Needed C・D。
- **Selected Approach**: 本ウィンドウは編集対象 SO の `SerializedObject` を介してのみ書き込み（Undo・dirty を標準経路で得る）。外部変更（Inspector 編集 / Undo/Redo / binding 追加）を検知したらグラフを冪等 rebuild。
- **Rationale**: 新規永続化先を作らず（要件 7.2）既存 JSON エクスポートを無改修で通す（要件 7.3）。SerializedObject 共有で Inspector との整合・Undo 統合を Unity 標準で得る。
- **Trade-offs**: 同時編集の競合は後勝ちで未解決（preview の破壊的変更許容で受容）。
- **Follow-up**: rebuild の冪等性を EditMode テスト（同一 SO 状態 → 同一グラフモデル）で担保。

## Risks & Mitigations
- **experimental GraphView API の将来変更** — GraphView 型参照を `Editor/Windows/Routing/Graph/` 薄層に封じ込め、純粋ロジック・テストを隔離。API 変更時の修正面積を描画層に限定。
- **既存 Inspector リファクタによる回帰** — ヘルパ抽出前に既存挙動（slot 初期化・default input source 解決）を固定する EditMode テストを先行作成（C の段階導入）。
- **id 列挙ロジック二重化による slug 不一致再発** — `SourcePortEnumerator` を単一の真実源とし Inspector / 新ウィンドウ双方が共用。二重実装を設計で禁止。
- **同時編集競合** — 後勝ち受容 + 外部変更検知 rebuild。競合解決は本 spec のスコープ外と明示。
- **無効 id の誤削除** — 検出しても自動削除しない（要件 9.4）。`DanglingEdge` は読み取り専用描画のみ。

## References
- [GraphView Scripting API](https://docs.unity3d.com/ScriptReference/Experimental.GraphView.GraphView.html) — 基盤クラスと experimental ステータス
- [Edge Scripting API (6000.3)](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Experimental.GraphView.Edge.html) — output/input null 許容・子要素配置
- [EdgeControl Scripting API](https://docs.unity3d.com/ScriptReference/Experimental.GraphView.EdgeControl.html) — 制御点からの中点算出（weight ノブ配置）
- [Edge.cs (UnityCsReference)](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Modules/GraphViewEditor/Elements/Edge.cs) — Edge 実装詳細
- [GraphViewEditorWindow (UnityCsReference)](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Modules/GraphViewEditor/Windows/GraphViewEditorWindow.cs) — ウィンドウ統合パターン
- 既存コード: `IAdapterBindingDefaultLayerInputs.cs` / `AdapterBindingsListView.cs:231-255` / `ULipSyncAdapterBinding.cs:126-138` / `FacialCharacterProfileSO.cs` / `PhonemeOverlaySlots.cs`
