# Requirements Document

## Project Description (Input)
preview.1 リリース直前の Editor UX 改善 + バグ修正パック。`docs/backlog.md` の短期項目 S-10〜S-16 を 1 spec として集約し、各サブタスクを `/kiro:spec-run` で連続実行する。詳細は `docs/backlog.md`（S-10〜S-16 の各ブロック）を必ず参照すること。

### 含まれるタスク

#### S-10: OSC 受信側 ContributeMask / TryWriteValues の index 空間ずれ修正
- **現象**: `OscReceiverDemo` + `OscOutputDemo` 同一プロセス起動で `ArgumentException: Array lengths must be the same.` が `BitArray.Or` 経由で毎フレーム発生（`LayerInputSourceAggregator.AggregateInternal` → `LayerUseCase.UpdateWeights` → `FacialController.LateUpdate`）。
- **根本原因**: `HeartbeatConsistencyChecker._contributeMask` / `_skipMask` が OSC マッピング数長で構築されるのに対し、`LayerInputSourceAggregator._perLayerMask[l]` は mesh の BlendShape 数長で構築される。Or で長さ不一致。
- **潜在バグ**: `OscInputSource.TryWriteValues` が `readBuffer[i] → output[i]` 同一 index でコピーしているが、`readBuffer` は mapping index 空間、`output` は mesh BlendShape index 空間なので順序が異なれば値が誤った BlendShape に乗る。
- **対応方針候補**:
  - (a) `OscReceiverAdapterBinding.OnStart` で `ctx.BlendShapeNames` を `HeartbeatConsistencyChecker` に渡し、`_contributeMask` / `_skipMask` を mesh BlendShape 数長で構築（mapping name → mesh index 逆引き）。
  - (b) `OscInputSource` 内部に `mappingIndex → meshIndex` 逆引きを保持させ、`TryWriteValues` を mesh-index 空間で書き込むよう改修。
  - (c) `IInputSource.ContributeMask` interface コメントと Aggregator 前提のズレを契約として明文化。
  - 参考実装: `AnalogBlendShapeInputSource` の `nameToIndex` 構築 + mesh-index 空間 ContributeMask 立て。
- **テスト**: 既存 `Tests/EditMode/Adapters/OSC/HeartbeatConsistencyCheckerTests.cs` を mesh-index 空間追従、新規 `Tests/EditMode/Adapters/InputSources/OscInputSourceMaskTests.cs`（mapping 数 ≠ mesh 数のケース）。
- **影響範囲**: `Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/HeartbeatConsistencyChecker.cs`, `Adapters/AdapterBindings/OscReceiverAdapterBinding.cs`, `Adapters/InputSources/OscInputSource.cs`。

#### S-11: Expression 遷移メタデータの AnimationClip ベイク撤去
- **現状**: `ExpressionClipBakery.Bake` が AnimationClip に (a) 0F 目の BlendShape 値、(b) 遷移時間 / カーブの `AnimationEvent` メタの 2 種を書き込む。`ExpressionCreatorWindow` 右ペインに「遷移メタデータ」Foldout が出る。一方、`ExpressionSerializable.transitionDuration` / `transitionCurve` は Profile Inspector の Expression Row でも編集可能（`FacialCharacterProfileSOInspector.cs:1796` の Slider）。同じ値が 2 か所で編集できる混乱。
- **方針**: AnimationClip 内は 0F 目の BlendShape / Bone キーのみ記録し、遷移時間 / カーブの `AnimationEvent` は書かない。source of truth は Profile Inspector の Expression Row 一本化。
- **対応**: `ExpressionClipBakery.Bake` から `AnimationEvent` 書き込み除去、`AnimationClipExpressionSampler.SampleSummary` を「メタが無ければ既定値を返す」挙動に揃える、`ExpressionCreatorWindow` から「遷移メタデータ」Foldout（`_transitionDurationField` / `_curveTypeDropdown`）を削除、AutoExporter / SampleSummary 参照経路の動作確認、Domain の `Expression` bridge ctor（`TransitionDuration` / `TransitionCurve` を受ける方）の撤去可否を別 PR で判断。
- **影響範囲**: `Packages/com.hidano.facialcontrol/Editor/Tools/ExpressionClipBakery.cs`, `Editor/Tools/ExpressionCreatorWindow.cs`, `Editor/Sampling/AnimationClipExpressionSampler.cs`, `Editor/AutoExport/FacialCharacterProfileExporter.cs`, 対応 EditMode テスト。

#### S-12: ExpressionCreatorWindow UI 改善
- **(1) ボタン潰れ**: `ExpressionCreatorWindow.cs:192-204` bottomSection に flex-end 配置された「AnimationClip にベイク」ボタンが幅を確保できず潰れる。`FacialControlStyles.ActionButton` の幅指定 / `flexShrink` 組合せが原因と推定。`style.flexShrink = 0` + `style.minWidth` 追加で修正。
- **(2) 画像書き出し**: 現プレビュー出力（256×256 RenderTexture）を PNG で保存するボタンを leftPanel に追加。`PreviewRenderWrapper` から `Texture2D.ReadPixels` で取得 → `EditorUtility.SaveFilePanel` で保存先指定。
- **(3) Clip 無し新規作成**: ObjectField 隣に「新規作成」ボタンを追加。`EditorUtility.SaveFilePanelInProject` で保存先指定 → 空 AnimationClip 生成 → `_clipField.value` に代入して `OnClipFieldChanged` を発火。
- **影響範囲**: `Editor/Tools/ExpressionCreatorWindow.cs`, `Editor/Common/PreviewRenderWrapper.cs`（PNG 出力 API 追加）, `Tests/EditMode/Editor/Tools/ExpressionCreatorWindowTests.cs`。

#### S-13: Expression Row の Gaze 用 AnimationClip スロット非表示（isGaze=true 時）
- **現状**: Profile Inspector の Expression Row では `isGaze=true` のときに遷移時間 Slider は非表示（`FacialCharacterProfileSOInspector.cs:1805`）にしているが、AnimationClip ObjectField は同行内で表示され続けている。
- **方針**: `isGaze=true` では AnimationClip による補間は適用されないため、UI 上もスロット自体を `DisplayStyle.None` にして混乱を避ける。
- **影響範囲**: `Editor/Inspector/FacialCharacterProfileSOInspector.cs` の Expression Row 構築箇所、対応 EditMode テスト。

#### S-14: GazeConfigs の一括再生成ボタン + Undo 経路復元
- **現状**: GazeConfig 手動追加 dropdown は `RebuildGazeConfigsUI` で `DisplayStyle.None` に隠されている（`FacialCharacterProfileSOInspector.cs:915`）。アナログ表情追加時の自動生成のみが正規ルートで、誤って GazeConfig 行を削除した場合の再生成手段が UI 上に無く、Undo でも復元されないケースが報告されている。
- **方針**:
  - (a) `GazeConfigBulkResolveButton` 近辺に「GazeConfig を一括再生成」ボタンを明示配置し、未生成の Gaze 用 Expression を一括スキャンして補完。
  - (b) GazeConfig 削除経路に `Undo.RecordObject` を正しく挿し、Ctrl+Z で復元できることを EditMode テストで保証。
- **影響範囲**: `Editor/Inspector/FacialCharacterProfileSOInspector.cs`, 対応 EditMode テスト。

#### S-15: Gaze 左右別 Action UI 配置見直し
- **現状**: `InputSystemAdapterBindingDrawer.BindExpressionBindingRow` 並び順は `Action 名 → useDistinct チェック → 左 Action → 右 Action → 表情 ID → 動作モード → トリガモード`。Gaze + `useDistinct=true` で元「Action 名」を `DisplayStyle.None` で非表示（`UpdateGazeActionFieldVisibility`, `InputSystemAdapterBindingDrawer.cs:526-553`）。
- **方針**: 並び順を `表情 ID → 動作モード → useDistinct チェック → 左 Action → 右 Action → Action 名 → トリガモード` に変更。Gaze + `useDistinct=true` 時の「Action 名」は非表示ではなくグレーアウト（`SetEnabled(false)`）にして「左右別を ON にしたから単一 Action が無効化されている」ことを視覚化。
- **影響範囲**: `Packages/com.hidano.facialcontrol.inputsystem/Editor/AdapterBindings/InputSystemAdapterBindingDrawer.cs`, 対応 EditMode テスト。

#### S-16: uLipSync PhonemeEntry Undo 後の表示不整合
- **現象**: BlendShape ↔ AnimationClip モード切り替え時に Undo 経路で値自体が古いまま表示される。
- **根本原因推定**: `PhonemeEntryListView.SetEntryKind` が `managedReferenceValue` を差し替え後 `ApplyModifiedPropertiesAndRefresh` → `_listView.Rebuild()` を呼ぶが、Undo 経路は `Undo.undoRedoPerformed` を購読していないため、`bindItem` が直近 SerializedProperty 値で再描画されない。
- **方針**: `PhonemeEntryListView` コンストラクタで `Undo.undoRedoPerformed += OnUndoRedoPerformed` を購読、Detach 時（`UnregisterCallback<DetachFromPanelEvent>`）に解除。`OnUndoRedoPerformed` で `serializedObject.Update()` + `_listView.Rebuild()`。Undo → モード切替 → 表示値が SerializedProperty と一致することを EditMode テストで保証。
- **影響範囲**: `Packages/com.hidano.facialcontrol.lipsync/Editor/Inspector/PhonemeEntryListView.cs`, 対応 EditMode テスト。

### 共通要件
- 全タスク TDD 厳守（Red-Green-Refactor）
- EditMode テストは新規追加 / 既存更新の両方を想定
- Domain / Application / Adapters / Presentation のレイヤー分離維持
- 言語: 日本語（`requirements.md` / `design.md` / `tasks.md` は日本語で記述）
- preview 段階のため破壊的変更（`AnimationEvent` 撤去、UI 並び順変更）は許容
- `backlog.md` の対応エントリ（S-10〜S-16）は完了時に削除すること

## Introduction

本 spec は preview.1 リリース直前の Editor UX 改善 + バグ修正パックである。`docs/backlog.md` の短期項目 S-10〜S-16 を 7 つの独立した Requirement として集約し、`/kiro:spec-run` で連続実行できる単位にまとめる。対象は OSC 受信側のランタイムバグ（index 空間ずれ）、Editor の二重編集経路（Expression 遷移メタの二重管理）、`ExpressionCreatorWindow` の UX 改善（ボタン潰れ / 画像書き出し / Clip 新規作成）、Gaze 関連の Inspector / Drawer UI 整理、`uLipSync` PhonemeEntry の Undo 整合性で、いずれも preview.1 を「触ってまず詰まる」箇所を解消することを狙う。

preview 段階のため、`AnimationEvent` メタの撤去や Input Drawer の並び順変更といった**破壊的変更を許容**する。永続化されたユーザーデータの整合性は preview.1 リリース時点のスキーマに揃え、過去 preview からの自動移行は提供しない。

## Boundary Context

- **In scope**:
  - OSC 受信側の `ContributeMask` / `_skipMask` を mesh BlendShape index 空間に揃え、`OscReceiverDemo` + `OscOutputDemo` 同一プロセスでの `ArgumentException` を解消する（S-10）。
  - Expression の遷移時間 / 遷移カーブの source of truth を Profile Inspector の Expression Row に一本化し、AnimationClip 側の `AnimationEvent` メタを撤去する（S-11）。
  - `ExpressionCreatorWindow` の (1) ベイクボタン潰れ修正 / (2) プレビュー PNG 書き出し / (3) Clip 無し新規作成導線（S-12）。
  - Expression Row における Gaze 用 AnimationClip スロットの `DisplayStyle.None` 化（S-13）。
  - Profile Inspector の「GazeConfig を一括再生成」ボタン追加と GazeConfig 削除経路の `Undo.RecordObject` 経路復元（S-14）。
  - `InputSystemAdapterBindingDrawer` の Gaze 左右別 Action UI の並び順変更と「Action 名」のグレーアウト化（S-15）。
  - `PhonemeEntryListView` の `Undo.undoRedoPerformed` 購読による表示整合性復元（S-16）。
  - 上記すべてに対応する EditMode テストの新規追加 / 既存更新。
- **Out of scope**:
  - Domain `Expression` bridge ctor（`TransitionDuration` / `TransitionCurve` を受ける方）の撤去判断は別 PR（S-11 のうち本 spec が扱うのは Editor / Sampling / AutoExport 経路まで）。
  - PlayMode テストの追加（実 OSC 送受信は既存 PlayMode テスト群に委ねる）。
  - リップシンク音声解析・新規入力ソースの追加。
  - preview.1 以前のユーザー JSON / SO データの自動マイグレーション。
- **Adjacent expectations**:
  - `IInputSource.ContributeMask` の前提（mesh BlendShape index 空間）は `LayerInputSourceAggregator` 側の契約に従う。本 spec で interface コメントを契約として明文化する。
  - `ExpressionClipBakery` / `AnimationClipExpressionSampler` / `FacialCharacterProfileExporter` の三者は AnimationClip を経由する一貫した経路を構成しており、メタ撤去後も AutoExporter からのラウンドトリップが破綻しないこと。
  - `FacialCharacterProfileSOInspector` と `InputSystemAdapterBindingDrawer` の Gaze UI 変更は preview.1 リリースノートで「UI 並び順とスロット表示が変わる」旨を周知する。

## Requirements

### Requirement 1: OSC 受信側 ContributeMask / TryWriteValues の index 空間整合（S-10）

**Objective:** Unity エンジニアとして、`OscReceiverDemo` と `OscOutputDemo` を同一プロセスで起動しても毎フレーム例外が出ず、受信した OSC 値が正しい BlendShape に反映されるようにしたい。それにより preview.1 サンプルが箱出しで動作することを保証する。

#### Acceptance Criteria
1. When `OscReceiverDemo` と `OscOutputDemo` が同一プロセスで実行されるとき、the OSC Receiver Subsystem shall `LayerInputSourceAggregator.AggregateInternal` 経由の `BitArray.Or` で `ArgumentException: Array lengths must be the same.` を発生させない。
2. The OSC Receiver Subsystem shall `HeartbeatConsistencyChecker._contributeMask` および `_skipMask` を mesh の BlendShape 数長で構築する。
3. When `OscReceiverAdapterBinding.OnStart` が呼ばれるとき、the OSC Receiver Subsystem shall `ctx.BlendShapeNames` を `HeartbeatConsistencyChecker` に供給し、mapping name → mesh index の逆引きを通じて mesh-index 空間のマスクを初期化する。
4. When `OscInputSource.TryWriteValues` が呼ばれるとき、the OSC Receiver Subsystem shall mapping index 空間で受信した値を mesh BlendShape index 空間に再マッピングして `output` に書き込む。
5. If OSC マッピング数と mesh の BlendShape 数が異なるとき、then the OSC Receiver Subsystem shall 値を誤った BlendShape に書き込まないこと（mapping name と一致しない mesh インデックスは未書き込みのまま保持する）。
6. The OSC Receiver Subsystem shall `IInputSource.ContributeMask` の interface コメントで「mask は mesh BlendShape index 空間であること」を契約として明文化する。
7. The Test Suite shall 既存 `Tests/EditMode/Adapters/OSC/HeartbeatConsistencyCheckerTests.cs` を mesh-index 空間に追従して更新する。
8. The Test Suite shall 新規 `Tests/EditMode/Adapters/InputSources/OscInputSourceMaskTests.cs` を追加し、mapping 数 ≠ mesh 数のケースで `ContributeMask` / `TryWriteValues` が正しい mesh index に書き込むことを検証する。

### Requirement 2: Expression 遷移メタデータの AnimationClip ベイク撤去（S-11）

**Objective:** Unity エンジニアとして、Expression の遷移時間と遷移カーブを Profile Inspector の Expression Row だけで編集できるようにしたい。AnimationClip 側との二重編集経路を取り除くことで、preview.1 ユーザーが「どちらが優先されるのか」で混乱しないようにする。

#### Acceptance Criteria
1. When `ExpressionClipBakery.Bake` が呼ばれるとき、the Expression Bakery shall 0F 目の BlendShape / Bone キーのみを AnimationClip に書き込み、遷移時間 / 遷移カーブを表す `AnimationEvent` を一切書き込まない。
2. When `AnimationClipExpressionSampler.SampleSummary` が遷移メタを保持しない AnimationClip に対して呼ばれるとき、the AnimationClip Sampler shall 遷移時間と遷移カーブを既定値（既定: 0.25 秒 / Linear）で返す。
3. While Expression Creator Window が表示されているとき、the Expression Creator Window shall 「遷移メタデータ」Foldout（`_transitionDurationField` / `_curveTypeDropdown`）を UI 上に提示しない。
4. The Profile Inspector shall Expression Row の遷移時間 Slider と遷移カーブ選択を Expression 遷移メタの唯一の編集経路として維持する。
5. When `FacialCharacterProfileExporter` が AutoExport を実行するとき、the AutoExport Pipeline shall 遷移メタを `ExpressionSerializable.transitionDuration` / `transitionCurve` から直接参照し、AnimationClip 由来の値を読まない。
6. The Preview1 Polish Pack shall preview 段階の破壊的変更として `AnimationEvent` 由来の遷移メタ撤去を許容し、過去 preview の AnimationClip 自動移行を提供しない。
7. The Test Suite shall `ExpressionClipBakery` / `AnimationClipExpressionSampler` / `ExpressionCreatorWindow` の対応 EditMode テストを「`AnimationEvent` を期待しない」挙動に揃えて更新する。

### Requirement 3: ExpressionCreatorWindow UI 改善（S-12）

**Objective:** Unity エンジニアとして、`ExpressionCreatorWindow` で「AnimationClip にベイク」ボタンが常に操作可能であり、現在のプレビューを PNG として保存でき、AnimationClip を未指定のままでも新規作成して編集を開始できるようにしたい。preview.1 で AnimationClip 作成支援フローを完結させるためである。

#### Acceptance Criteria
1. The Expression Creator Window shall bottomSection の「AnimationClip にベイク」ボタンに `style.flexShrink = 0` と最小幅（`style.minWidth`）を適用し、ウィンドウ幅が縮んでもボタンが潰れて操作不能にならないことを保証する。
2. The Expression Creator Window shall leftPanel に「プレビューを PNG として保存」ボタンを追加する。
3. When ユーザーが「プレビューを PNG として保存」ボタンをクリックするとき、the Expression Creator Window shall 現在のプレビュー RenderTexture（256×256）を `Texture2D.ReadPixels` で取得し、`EditorUtility.SaveFilePanel` で指定された保存先へ PNG 形式で書き出す。
4. The Expression Creator Window shall AnimationClip ObjectField の隣に「新規作成」ボタンを追加する。
5. When ユーザーが「新規作成」ボタンをクリックするとき、the Expression Creator Window shall `EditorUtility.SaveFilePanelInProject` で保存先を取得し、空の AnimationClip を生成して `_clipField.value` に代入し、`OnClipFieldChanged` を発火させる。
6. If ユーザーが「新規作成」のファイルダイアログをキャンセルしたとき、then the Expression Creator Window shall AnimationClip を生成せず `_clipField.value` を変更しない。
7. The Preview Render Wrapper shall PNG 出力に必要な API を Editor 側から呼べる形で公開する。
8. The Test Suite shall `Tests/EditMode/Editor/Tools/ExpressionCreatorWindowTests.cs` にボタン潰れ防止 / PNG 保存導線 / Clip 新規作成導線を覆う EditMode テストを追加する。

### Requirement 4: Expression Row の Gaze 用 AnimationClip スロット非表示（S-13）

**Objective:** Unity エンジニアとして、`isGaze=true` の Expression Row では補間に使われない AnimationClip スロットを表示しないようにしたい。Profile Inspector で Gaze 用 Expression を編集する際に「ここに AnimationClip を挿しても効かない」という誤解を防ぐためである。

#### Acceptance Criteria
1. When Profile Inspector が Expression Row を構築し、その Expression の `isGaze` が `true` であるとき、the Profile Inspector shall AnimationClip ObjectField の `style.display` を `DisplayStyle.None` に設定する。
2. When Profile Inspector が Expression Row を構築し、その Expression の `isGaze` が `false` であるとき、the Profile Inspector shall AnimationClip ObjectField を `DisplayStyle.Flex` で表示する。
3. When ユーザーが Expression Row の `isGaze` トグルを動的に切り替えたとき、the Profile Inspector shall 同じ行内の AnimationClip ObjectField の表示状態を再評価して即時反映する。
4. While `isGaze=true` で AnimationClip スロットが非表示のとき、the Profile Inspector shall 内部の AnimationClip 参照値を破壊せず、`isGaze=false` に戻したときに直前の参照を復元可能にする。
5. The Test Suite shall `FacialCharacterProfileSOInspector` の Expression Row 構築テストを EditMode で追加または更新し、`isGaze` 値ごとの AnimationClip スロット表示状態を検証する。

### Requirement 5: GazeConfigs 一括再生成 + Undo 経路復元（S-14）

**Objective:** Unity エンジニアとして、誤って削除した GazeConfig 行を Profile Inspector から復元できるようにしたい。アナログ表情追加時の自動生成だけでは取りこぼされる削除事故を、明示的な再生成ボタンと正しい Undo 経路でカバーするためである。

#### Acceptance Criteria
1. The Profile Inspector shall `GazeConfigBulkResolveButton` の近傍に「GazeConfig を一括再生成」ボタンを表示する。
2. When ユーザーが「GazeConfig を一括再生成」ボタンをクリックするとき、the Profile Inspector shall Gaze 用 Expression を一括スキャンし、GazeConfig が未生成の Expression に対して GazeConfig を補完する。
3. When 「GazeConfig を一括再生成」が実行されるとき、the Profile Inspector shall 既に GazeConfig が存在する Expression の値を上書きしない。
4. When GazeConfig 行が UI から削除されるとき、the Profile Inspector shall 削除前に `Undo.RecordObject` を呼んで FacialCharacterProfileSO の状態を Undo スタックに登録する。
5. When ユーザーが GazeConfig 削除直後に Ctrl+Z を実行するとき、the Profile Inspector shall 削除された GazeConfig 行を SerializedProperty レベルで復元し、UI 上にも再表示する。
6. The Test Suite shall GazeConfig 一括再生成と Undo 復元の双方を EditMode で検証する。

### Requirement 6: Gaze 左右別 Action UI 配置見直し（S-15）

**Objective:** Unity エンジニアとして、`InputSystemAdapterBindingDrawer` の Expression Binding 行が表情 ID 起点で読めるようにしたい。Gaze で「左右別を ON にした」ことが視覚的に分かるよう、単一 Action フィールドが非表示で消えるのではなくグレーアウトする UI に変更するためである。

#### Acceptance Criteria
1. The InputSystem Adapter Binding Drawer shall Expression Binding 行の並び順を `表情 ID → 動作モード → useDistinct チェック → 左 Action → 右 Action → Action 名 → トリガモード` に変更する。
2. When Expression が Gaze 用であり `useDistinct` が `true` であるとき、the InputSystem Adapter Binding Drawer shall 「Action 名」フィールドに `SetEnabled(false)` を適用してグレーアウトさせ、`DisplayStyle.None` で非表示にはしない。
3. When Expression が Gaze 用であり `useDistinct` が `false` であるとき、the InputSystem Adapter Binding Drawer shall 「Action 名」フィールドを `SetEnabled(true)` の通常表示で提示する。
4. When ユーザーが `useDistinct` トグルを動的に切り替えたとき、the InputSystem Adapter Binding Drawer shall 「Action 名」「左 Action」「右 Action」の enable / disable 状態を即時に更新する。
5. The Preview1 Polish Pack shall preview 段階の破壊的変更として、本 UI 並び順変更を許容し過去 preview との見た目互換を維持しない。
6. The Test Suite shall `InputSystemAdapterBindingDrawer` の Expression Binding 行の並び順と「Action 名」フィールドの enable 状態を EditMode で検証する。

### Requirement 7: uLipSync PhonemeEntry Undo 表示整合（S-16）

**Objective:** Unity エンジニアとして、`PhonemeEntryListView` で BlendShape ↔ AnimationClip のモード切り替えを Undo した直後にも、表示値が SerializedProperty の現在値と一致するようにしたい。preview.1 リップシンク Inspector の Undo 操作後に「値が古いまま」見える表示不整合を解消するためである。

#### Acceptance Criteria
1. When `PhonemeEntryListView` のインスタンスが生成されるとき、the LipSync Phoneme Entry List View shall `Undo.undoRedoPerformed` に `OnUndoRedoPerformed` ハンドラを購読する。
2. When `PhonemeEntryListView` のルート要素が `DetachFromPanelEvent` を受け取るとき、the LipSync Phoneme Entry List View shall `Undo.undoRedoPerformed` から `OnUndoRedoPerformed` の購読を解除する。
3. When `OnUndoRedoPerformed` が呼び出されるとき、the LipSync Phoneme Entry List View shall `serializedObject.Update()` を呼んだ後に `_listView.Rebuild()` を呼び、表示中の各 PhonemeEntry を最新の SerializedProperty 値で再描画する。
4. When ユーザーが BlendShape ↔ AnimationClip のモード切り替えを Undo するとき、the LipSync Phoneme Entry List View shall 直後の表示値を SerializedProperty の現在値と一致させる。
5. If `Undo.undoRedoPerformed` が複数回連続して発火するとき、then the LipSync Phoneme Entry List View shall 各イベントで例外を発生させずに再描画を完了する。
6. The Test Suite shall Undo → モード切替 → 表示値が SerializedProperty と一致することを EditMode で検証する。
