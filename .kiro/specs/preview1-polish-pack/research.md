# Research & Design Decisions — preview1-polish-pack

## Summary

- **Feature**: `preview1-polish-pack`
- **Discovery Scope**: Extension（既存パッケージ群に対する Editor UX 改善 + 限定的なバグ修正）
- **Key Findings**:
  - S-10 の根本原因は `HeartbeatConsistencyChecker._contributeMask` / `_skipMask` の長さ次元（mapping index 空間）が `LayerInputSourceAggregator._perLayerMask[l]` の次元（mesh BlendShape index 空間）と一致しないこと。`BitArray.Or` は片方が短いだけでも `ArgumentException` を投げる。同時に `OscInputSource.TryWriteValues` も mapping index → output index を 1:1 でコピーしており、mapping 順 ≠ mesh 順では値が誤った BlendShape に乗る。`AnalogBlendShapeInputSource` が既に持つ「`nameToIndex` 逆引き + mesh-index 空間 ContributeMask」が参考実装として一致する。
  - S-11 の二重編集経路は AnimationClip 側 `AnimationEvent`（Bake が書き込み、Sampler が読み出す）と `ExpressionSerializable.transitionDuration` / `transitionCurve`（Profile Inspector スライダーが書き込み、`FacialCharacterProfileExporter` が JSON 出力時に読み出す）の 2 系統。Exporter は既に `dto.transitionDuration = expr.transitionDuration` で SO を真値と扱っており、AnimationClip 側のメタは Editor サンプリング経路でしか使われていない。
  - 既存テスト基盤は EditMode 中心。`HeartbeatConsistencyCheckerTests`, `OscInputSourceTests`, `ExpressionCreatorWindowTests`, `AnimationClipExpressionSamplerMetadataTests`, `FacialCharacterProfileSOInspectorGazeConfigsTests`, `InputSystemAdapterBindingDrawerTests`, `PhonemeEntryListViewTests` がすべて該当パッケージ配下に存在。S-10〜S-16 はそれぞれ既存テスト fixture の更新 + 新規 fixture 追加で TDD 経路に乗る。

## Research Log

### S-10: OSC 受信側 index 空間ずれ

- **Context**: `OscReceiverDemo` と `OscOutputDemo` 同一プロセスで毎フレーム `ArgumentException: Array lengths must be the same.` が `BitArray.Or` 経由で発生する報告。`LayerInputSourceAggregator.AggregateInternal:323` の `layerMask.Or(source.ContributeMask)` が trigger 箇所。
- **Sources Consulted**:
  - `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/HeartbeatConsistencyChecker.cs:56-58`
  - `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/AdapterBindings/OscReceiverAdapterBinding.cs:302-303, 312, 332-340`
  - `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/InputSources/OscInputSource.cs:99-136`
  - `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Adapters/InputSources/AnalogBlendShapeInputSource.cs:73-125`（参考実装）
  - `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Services/LayerInputSourceAggregator.cs:155-167, 295-298, 313-323`
  - `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Interfaces/IInputSource.cs:62-71`
  - `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Adapters/AdapterBuildContext.cs`（`BlendShapeNames` を持つ）
- **Findings**:
  1. `HeartbeatConsistencyChecker` は `receiverMappings.Count` 個の `_receiverBlendShapeNames` を保持し、その長さで `_contributeMask` / `_skipMask` を確保する。これは **mapping 個数** 次元。
  2. `LayerInputSourceAggregator` は `blendShapeCount`（= mesh の `SkinnedMeshRenderer.sharedMesh.blendShapeCount`）で `_perLayerMask[l]` を確保する。これは **mesh BlendShape 個数** 次元。
  3. `OscInputSource.ContributeMask` を `LayerInputSourceAggregator.AggregateInternal` 内で `layerMask.Or(...)` するため、2 つの BitArray の長さが一致しない限り例外で死ぬ。
  4. `OscInputSource.TryWriteValues` は `for (i in 0..copyLength) output[i] = readBuffer[i]` と書いており、`readBuffer` (= `OscDoubleBuffer` の mapping index 空間) と `output` (= layer scratch の mesh index 空間) で同 index を使うのは構造的に誤り。値が「mapping 順で並んだ output」になり、mesh の BlendShape 並びと一致しなければ値が誤った BlendShape に書かれる。
  5. `AnalogBlendShapeInputSource` は同種の問題を、ctor で `blendShapeNames` を受けて `nameToIndex` 逆引きを 1 回構築し、`_contributeMask` を mesh-index 空間で立て、`TryWriteValues` でも mesh-index 空間に書き込むことで解決済み。同じパターンを OSC 側にも適用するのが整合的。
  6. `AdapterBuildContext` には既に `BlendShapeNames` が含まれており、`OscReceiverAdapterBinding.OnStart(in ctx)` から `ctx.BlendShapeNames` を `HeartbeatConsistencyChecker` と `OscInputSource` に渡せる。
- **Implications**:
  - `HeartbeatConsistencyChecker` を mesh BlendShape 数 + 各 mapping name → mesh index 逆引きで再構築する必要がある（対応方針候補 (a)）。
  - 同時に `OscInputSource.TryWriteValues` を mapping index → mesh index 空間で書き込む経路に揃える必要がある（候補 (b)）。
  - `IInputSource.ContributeMask` の interface コメントに「mask は mesh BlendShape index 空間（`BlendShapeCount` と同長）」を XML doc-comment として明文化する（候補 (c)）。
  - 3 候補は排他ではなく **すべて同時適用** が必要（(a)/(b) はランタイム整合のため、(c) は再発防止のため）。

### S-11: Expression 遷移メタの真値一元化

- **Context**: AnimationClip 側 `AnimationEvent` メタ（`ExpressionClipBakery.Bake` が書き込み、`AnimationClipExpressionSampler.SampleSummary` が読み出す）と Profile Inspector スライダー（`ExpressionSerializable.transitionDuration` / `transitionCurve`）が同じ値を 2 か所で持つため「どちらが優先か」が不明瞭。
- **Sources Consulted**:
  - `FacialControl/Packages/com.hidano.facialcontrol/Editor/Tools/ExpressionClipBakery.cs:66-113`
  - `FacialControl/Packages/com.hidano.facialcontrol/Editor/Sampling/AnimationClipExpressionSampler.cs:30-41, 117-151, 220-273`
  - `FacialControl/Packages/com.hidano.facialcontrol/Editor/Tools/ExpressionCreatorWindow.cs:44-55, 158-175, 506-554, 559-600`
  - `FacialControl/Packages/com.hidano.facialcontrol/Editor/AutoExport/FacialCharacterProfileExporter.cs:50-74, 256-266`
- **Findings**:
  1. AutoExport は既に Inspector スライダー値を真値として扱っており（`dto.transitionDuration = expr.transitionDuration`）、AnimationClip 由来の `AnimationEvent` 値は使われていない（exporter 経路では `cachedSnapshot.transitionDuration` を上書きする）。
  2. `AnimationClipExpressionSampler.SampleSummary` は `AnimationEvent` が無ければ既に `Expression.DefaultTransitionDuration` / `TransitionCurvePreset.Linear` を返す。撤去後はこの「無ければ既定値」経路が常に走る形になり、外部 API 形状は維持できる。
  3. `ExpressionCreatorWindow.RestoreSliderValuesFromTargetClip` は AnimationClip サンプリング後に `summary.TransitionDuration` をスライダーに復元しているが、メタ撤去後はそもそも AnimationClip 側からこれらを取らないため UI から除去するのが整合的。
  4. preview.1 リリースノートは backlog.md の S-11 ブロックで「過去 preview からの自動移行は提供しない」方針が明文化されている。requirements.md AC2.6 もこれを破壊的変更として認める形になっている。
- **Implications**:
  - `ExpressionClipBakery.Bake` から `AnimationUtility.SetAnimationEvents` 経路を撤去（BlendShape Curve 書き込みのみ残す）。
  - `AnimationClipExpressionSampler` の `ExtractTransitionMetadata` ロジックは「AnimationEvent が無ければ既定値」だけが残るため、シグネチャは維持しつつ実体を簡略化する。
  - `ExpressionCreatorWindow` の `_transitionDurationField` / `_curveTypeDropdown` を含む `transitionFoldout` 全体を UI から除去。
  - `RestoreSliderValuesFromTargetClip` 内の `summary.TransitionDuration` / `summary.TransitionCurve` 復元コードと、`OnBakeClicked` 内の `transitionDuration` / `transitionCurvePreset` 引数経路を整理。
  - Domain `Expression` bridge ctor の撤去は本 spec では Out-of-scope（requirements.md Out-of-scope セクションで明示）。

### S-12: ExpressionCreatorWindow UI 3 件

- **Context**: (1) bottomSection の `flex-end` 配置で `ActionButton` の幅指定が機能せずボタンが潰れる。(2) プレビュー RenderTexture の保存導線が無い。(3) AnimationClip 無しで新規作成できる導線が無く、別途 Project View で作成して指定する必要がある。
- **Sources Consulted**:
  - `FacialControl/Packages/com.hidano.facialcontrol/Editor/Tools/ExpressionCreatorWindow.cs:192-213`
  - `FacialControl/Packages/com.hidano.facialcontrol/Editor/Common/PreviewRenderWrapper.cs:88-101, 184-213`
- **Findings**:
  1. `bottomSection` は `flexDirection = Row + justifyContent = FlexEnd` で、`bakeButton` のみが子要素。`FacialControlStyles.ActionButton` は USS で `min-width` を持たないため、親が `flex-end` 配置で flex 幅 0 を許すと潰れる。`style.flexShrink = 0f` + `style.minWidth = (適切値)` の併用で解消可能。
  2. `PreviewRenderWrapper` は `PreviewRenderUtility.EndPreview()` の戻り値（Texture）を `GUI.DrawTexture` で描画して破棄しているだけで、外部に渡す API が無い。PNG 保存のためには `Render` 経路を分離し、Texture を `Texture2D.ReadPixels` でコピーできる API が必要。`PreviewRenderUtility.EndStaticPreview()` は `Texture2D` を直接返すため、PNG 出力用には別経路でも実装できる。
  3. `EditorUtility.SaveFilePanelInProject` は project relative パスを返し、`AssetDatabase.CreateAsset` で AnimationClip を作成する流れは Unity 標準。空 AnimationClip は `new AnimationClip()` で生成し、`AssetDatabase.CreateAsset(clip, projectRelativePath)` で保存後 `AssetDatabase.LoadAssetAtPath<AnimationClip>` で参照を取り戻して `_clipField.value` に代入する。
- **Implications**:
  - `bottomSection` 構成は維持し、`bakeButton` に `style.flexShrink = 0f` と `style.minWidth = ~140px`（既存ボタン文字列の幅に合わせる）を追加するのみで修正可能。
  - PNG 保存は `PreviewRenderWrapper.CapturePngBytes(int width, int height)` 等の新規 API を追加し、内部で `PreviewRenderUtility` 経由のフレームを `Texture2D.ReadPixels` で取得して PNG エンコードする経路に揃える。`EditorUtility.SaveFilePanel` で保存先を取得後、`File.WriteAllBytes` で書き出す。
  - 新規 AnimationClip ボタンは `_clipField` の左右どちらかに置くと既存レイアウトを壊しにくい（ObjectField は label + field 構造で、隣接配置は `VisualElement` で wrap して行う）。

### S-13: Gaze 用 AnimationClip スロット非表示

- **Context**: `isGaze=true` の Expression Row には遷移時間 Slider を非表示にしているが、AnimationClip ObjectField は表示が残る。Gaze 表情は GazeConfig 駆動で AnimationClip による補間が使われないため、UI 上にあると「割り当てれば効く」と誤解する。
- **Sources Consulted**:
  - `FacialControl/Packages/com.hidano.facialcontrol/Editor/Inspector/FacialCharacterProfileSOInspector.cs:1796-1812, 2226-2286`
- **Findings**:
  1. AnimationClip ObjectField は `BuildAnimationClipFields(row, exprIndex)` で構築される。`ExpressionRowClipFieldName` を name に持つ `ExpressionClipObjectField` インスタンスが該当。
  2. その直下に `isGazeToggle` も同関数内で生成され、`ChangeExpressionIsGaze(exprIndex, evt.newValue)` で `isGaze` を切り替える。トグル変更後は行全体を rebuild する経路があるはずだが、本対応では「行内で `style.display` を切り替える」のが最小変更。
  3. ChangeExpressionIsGaze が Rebuild する場合、`BuildAnimationClipFields` 内で `currentIsGaze` を見て初期 `style.display` を決めるだけで Step 1〜3 のうち AC1, AC2 がカバーされる。動的反映（AC3）は `isGazeToggle` の `RegisterValueChangedCallback` 内で同じ clipField への `style.display` 更新を行えば良い（Rebuild 経路が走るならその副作用で吸収）。
  4. AnimationClip 参照値は SerializedProperty `animationClip` に bind されているため、UI の `style.display` だけ切り替える限りデータは破壊されない（AC4）。
- **Implications**:
  - `BuildAnimationClipFields` 内で `clipField.style.display` を `currentIsGaze ? DisplayStyle.None : DisplayStyle.Flex` で初期化する。
  - `isGazeToggle.RegisterValueChangedCallback` 内で、同じ row の clipField を再取得して `style.display` を更新する経路を追加（または `ChangeExpressionIsGaze` 後の row rebuild に任せる）。

### S-14: GazeConfigs 一括再生成 + Undo 復元

- **Context**: GazeConfig 行は誤って削除すると Inspector からは復元手段がない（手動追加 dropdown は `DisplayStyle.None` で隠されている）。`RemoveGazeConfigAt` は `BeginUndoGroup("Remove GazeConfig")` を呼ぶが `_rootGazeConfigsProperty.DeleteArrayElementAtIndex` 単体では SerializedObject 全体の Undo に乗らないケースがあり、Ctrl+Z で復元できない事例が報告されている。
- **Sources Consulted**:
  - `FacialControl/Packages/com.hidano.facialcontrol/Editor/Inspector/FacialCharacterProfileSOInspector.cs:97-99, 884-963, 1010-1018, 1155-1168, 2298-2330`
  - Unity 公式 Undo API: `Undo.RecordObject`（直前の状態を記録）と `Undo.RegisterCompleteObjectUndo`（一括変更）。SerializedProperty 経由の変更は `ApplyModifiedProperties` で Undo に乗るが、`BeginUndoGroup` だけでは保証されない場合がある。
- **Findings**:
  1. `GazeConfigBulkResolveButtonName` 定数は既に定義されているが、本 spec の対象は「`GazeConfigBulkResolveButton` の近傍に新規ボタンを追加」する経路。実装場所は `RebuildGazeConfigsUI` 周辺で `_gazeConfigsContainer` の先頭 / 末尾に配置する。
  2. 一括再生成のロジックは「Expression を走査し、`isGaze=true` の Expression が GazeConfig を持たない場合に新規 GazeConfig を `_rootGazeConfigsProperty` に追加し、`ResetGazeConfigToDefaults` で初期化する」流れ。既存値の上書きは行わない（AC5.3）。
  3. `RemoveGazeConfigAt` で `Undo.RecordObject(target, "Remove GazeConfig")` を明示的に呼ぶことで、Ctrl+Z 時に SerializedProperty レベルで配列要素が復元される。`BeginUndoGroup` は group 単位の Undo 集約のためのものであり、`RecordObject` の代わりにはならない。
- **Implications**:
  - 「GazeConfig を一括再生成」ボタンを `_gazeConfigsContainer` の上部に追加し、`BulkRegenerateGazeConfigs()` メソッドで Expression を走査して欠落 GazeConfig を補完する。
  - `RemoveGazeConfigAt` の `_rootGazeConfigsProperty.DeleteArrayElementAtIndex` の直前に `Undo.RecordObject(target, "Remove GazeConfig")` を追加。`BeginUndoGroup` は維持。
  - 既存の `BulkResolveButton` のロジックと混同しないよう、新規ボタン名 / handler 名は別にする（例: `GazeConfigBulkRegenerateButton`）。

### S-15: Gaze 左右別 Action UI 並び順 + Action 名のグレーアウト

- **Context**: 現状の Expression Binding 行は `Action 名 → useDistinct → 左 Action → 右 Action → 表情 ID → 動作モード → トリガモード` の順。Gaze + useDistinct=true で「Action 名」を `DisplayStyle.None` で非表示にしているが「単一 Action が無効化されている」ことが視覚的に伝わらない。
- **Sources Consulted**:
  - `FacialControl/Packages/com.hidano.facialcontrol.inputsystem/Editor/AdapterBindings/InputSystemAdapterBindingDrawer.cs:278-524, 526-553`
- **Findings**:
  1. `BindExpressionBindingRow` 内で `element.Add(...)` を 7 回呼んでおり、その呼出し順がそのまま並び順になる。順序入れ替えは `element.Add` 呼出しの並び換えだけで実現できる。
  2. `UpdateGazeActionFieldVisibility` の `actionField.style.display = ... ? DisplayStyle.Flex : DisplayStyle.None` を、`actionField.SetEnabled(...)` 経路に書き換える。`DisplayStyle.Flex` を常時保持しつつ `SetEnabled(false)` でグレーアウトする。
  3. `useDistinctLeftRightToggle` と `bindingModeField` の `RegisterValueChangedCallback` から呼ばれる `UpdateGazeActionFieldVisibility` も同じシグネチャで動作するが、表示制御の意味が変わるためメソッド名は `UpdateGazeActionFieldState` 等に変えるのが整合的。
  4. 要求された順序：`表情 ID → 動作モード → useDistinct → 左 Action → 右 Action → Action 名 → トリガモード`。expressionDropdown → bindingModeField → useDistinctLeftRightToggle → actionLeftDropdown → actionRightDropdown → actionDropdown → triggerModeField の呼出し順に揃える。Overlay 用 field 群（`overlaySlotField` / `overlayTargetLayerField`）は元の位置（triggerModeField の前後）に維持して影響を最小化する。
- **Implications**:
  - `BindExpressionBindingRow` 内の `element.Add` 順を上記要求順に書き換える。Overlay フィールドは triggerModeField の前後どちらに置くか確定する必要があり、本 spec では「triggerModeField の後（最後尾）」に置く方針とする（既存の `UpdateOverlayFieldVisibility` 制御を維持しやすい）。
  - `UpdateGazeActionFieldVisibility` のロジックを `actionField.SetEnabled(!(isGaze && useDistinctLeftRight))` に書き換え、`style.display` 制御を撤廃。
  - 既存 `UpdateGazeActionFieldVisibility` 呼出し箇所（3 箇所）すべてに同じ意味変更を適用。

### S-16: PhonemeEntryListView Undo 表示整合

- **Context**: BlendShape ↔ AnimationClip モード切替（`SetEntryKind`）後の Undo で `_listView` の表示値が SerializedProperty の現在値と乖離する。`SetEntryKind` 経路は `ApplyModifiedPropertiesAndRefresh` 内で `_listView.Rebuild()` を呼ぶが、Undo 経路ではこの再 build が走らない。
- **Sources Consulted**:
  - `FacialControl/Packages/com.hidano.facialcontrol.lipsync/Editor/Inspector/PhonemeEntryListView.cs:61-105, 155-180, 462-474`
  - Unity 公式: `Undo.undoRedoPerformed` は Undo / Redo 実行後に発火する静的イベント。Editor UI 側で SerializedObject を再 `Update()` してから ListView を `Rebuild()` するのが標準パターン。
- **Findings**:
  1. `PhonemeEntryListView` は VisualElement 派生で、コンストラクタで ListView を構築する。`DetachFromPanelEvent` を購読すれば破棄タイミングで cleanup できる。
  2. `Undo.undoRedoPerformed` は `static` イベントのため、subscribe / unsubscribe を必ず対称に行わないとリーク（Editor reload 時の null handler 呼出し）が発生する。`AttachToPanelEvent` で subscribe、`DetachFromPanelEvent` で unsubscribe するパターンが推奨。
  3. `OnUndoRedoPerformed` 内では `_listProperty.serializedObject.Update()` を呼んでから `RebuildIndexProxy(_indexProxy, GetListProperty())` を再実行し、`_listView.Rebuild()` で UI 全行を再描画する。これにより `bindItem` が最新 SerializedProperty の値で呼び直される。
  4. SerializedObject の targetObject が破棄された後の Undo 通知でも例外が出ないよう、`_listProperty?.serializedObject?.targetObject == null` のガードが必要（AC7.5）。
- **Implications**:
  - コンストラクタ末尾で `RegisterCallback<AttachToPanelEvent>(OnAttachToPanel)` / `RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel)` を登録し、それぞれの中で `Undo.undoRedoPerformed += / -= OnUndoRedoPerformed` を行う。Attach 経路に置くことで Editor reload 時の handler リークを避ける。
  - `OnUndoRedoPerformed` は `_listProperty?.serializedObject` の null safety を確認後 `serializedObject.Update()` → `RebuildIndexProxy` → `_listView.Rebuild()` の順で実行。

## Architecture Pattern Evaluation

S-10 については「mesh-index 空間で揃える」3 つの候補が requirements 段階で提示されている。本 design では候補 (a)/(b)/(c) を排他選択ではなく、すべて適用する形を採る。

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| (a) `HeartbeatConsistencyChecker` を mesh BlendShape 数長で構築 | mask 配列の長さを Aggregator 側と一致させる | `BitArray.Or` 例外の直接原因を除去 | mapping name → mesh index の逆引きが必須 | requirements.md AC1.2/1.3 |
| (b) `OscInputSource.TryWriteValues` を mesh-index 空間で書き込む | 受信値の宛先 index を mesh-index 空間に整列 | 値が誤った BlendShape に乗る潜在バグも同時解消 | mapping count ≠ mesh count 時のコピー長計算と未書込み index の扱いを契約として定義する必要あり | requirements.md AC1.4/1.5 |
| (c) `IInputSource.ContributeMask` の契約を XML doc-comment 化 | 再発防止 + 他 InputSource 実装者への docs | XML doc-comment 修正だけだとランタイム例外は止まらない | 単独では不十分だが、再発防止には必須 | requirements.md AC1.6（spec-requirements-agent からの確認ポイント: 「コメント追記」ではなく「XML doc-comment 追加」を採用） |

すべてを同時適用する理由: (a) だけだとマスクは正しくなるが値の宛先がずれたまま誤動作する。(b) だけだとマスクが mesh 空間で立たないため Aggregator 側が常に「mapping 個数」の長さを期待することになり構造が歪む。(c) を入れないと将来の InputSource 実装者が同じ罠を踏む。3 件をまとめて適用するのが最も整合的。

## Design Decisions

### Decision 1: `IInputSource.ContributeMask` 契約は XML doc-comment 追加で明文化する

- **Context**: requirements.md AC1.6 で「`IInputSource.ContributeMask` の interface コメントで mask は mesh BlendShape index 空間であることを契約として明文化する」が要求されている。spec-requirements-agent から「コメント追記のみ」か「XML doc-comment 追加」かの確認ポイントとして提示された。
- **Alternatives Considered**:
  1. 行コメント `// mesh BlendShape index 空間である必要がある` を実装に近い位置に追加する。
  2. `IInputSource.ContributeMask` の `<remarks>` セクションを更新し、`BlendShapeCount` との一致を契約として明示する XML doc-comment を書く。
- **Selected Approach**: 2 を採用。`IInputSource.ContributeMask` の `<remarks>` を「返される `BitArray` は **mesh BlendShape index 空間**であり、`BitArray.Length` は `BlendShapeCount` と一致する」と書き換える（既に「`BitArray.Length` は `BlendShapeCount` と一致する」記述はあるため、空間軸の明示を追加する）。
- **Rationale**: docfx / IDE Quick Info 経由で実装者が読める形式にすることで再発防止効果が高い。`IInputSource` は本パッケージのコア契約であり、入力源を追加する開発者が最初に参照する箇所。本プロジェクトは XML doc-comment ベースのドキュメント文化が既にある（`OscInputSource` / `AnalogBlendShapeInputSource` の `<remarks>` が長文）。
- **Trade-offs**: 行コメントより diff が大きくなるが、長期メンテ視点で利点が大きい。
- **Follow-up**: 既存 `AnalogBlendShapeInputSource` / `OscInputSource` の `<remarks>` 記述と整合させる。

### Decision 2: preview スキーマ凍結方針を design.md / リリースノートで明示

- **Context**: requirements.md AC2.6 が「過去 preview からの自動移行は提供しない」を明文化している。spec-requirements-agent から「preview.1 リリースノート方針との一致確認」が提示された。
- **Alternatives Considered**:
  1. design.md 内に「preview.1 schema freeze policy」セクションを作り、`AnimationEvent` 撤去 + Input Drawer 並び順変更を破壊的変更として記載する。
  2. リリースノート側でのみ告知し、design.md には記載しない。
- **Selected Approach**: 1 を採用。design.md `Migration Strategy`（任意セクション）に preview スキーマ凍結方針を記載し、本 spec が「自動マイグレーションを提供しない」決定を明記する。
- **Rationale**: design.md は実装者と reviewer の真値であり、後続 spec が同じ判断を踏襲する根拠としても必要。リリースノートのみだと spec レビュー時にコンテキストが欠ける。
- **Trade-offs**: design.md がやや長くなる。
- **Follow-up**: preview.1 リリースノート（`docs/release-notes/preview.1.md` 等、別 PR で作成）と本 design の文言整合を確認する。

### Decision 3: Expression Binding 行の並び順を固定する

- **Context**: spec-requirements-agent から「`表情 ID → 動作モード → useDistinct → 左 / 右 Action → Action 名 → トリガモード` の実使用感」確認ポイントが提示された。
- **Alternatives Considered**:
  1. 提示された順序をそのまま採用。
  2. `表情 ID → 動作モード → トリガモード → useDistinct → 左 / 右 Action → Action 名`（mode 系を先に固めるパターン）。
  3. `Action 名 → 左 Action → 右 Action → useDistinct → 表情 ID → 動作モード → トリガモード`（現状維持で並び順だけ最小変更）。
- **Selected Approach**: 1（要求どおり `表情 ID → 動作モード → useDistinct → 左 Action → 右 Action → Action 名 → トリガモード`）。
- **Rationale**: 表情 ID が一意キーとして先頭に来るのが読みやすく、動作モードで「Gaze か否か」が即座に判別できる。useDistinct → 左 / 右 Action → Action 名 の順は「左右別が ON のとき左右 Action を編集し、OFF のとき Action 名（単一）を編集」というユーザー視点の流れと一致する。トリガモードは Normal モード時のみ意味を持つため最後に置くのが整合的。
- **Trade-offs**: 既存ユーザーは UI 位置が変わるため再学習が必要だが、preview 段階のため破壊的変更を許容する（requirements.md AC6.5 と整合）。
- **Follow-up**: Overlay モード時の `overlaySlotField` / `overlayTargetLayerField` は `triggerModeField` の後（最後尾）に置く。これは既存の `UpdateOverlayFieldVisibility` 制御を維持しやすくするためで、要求外の挙動変更は導入しない。

### Decision 4: PreviewRenderWrapper の PNG 出力 API は新規メソッド追加

- **Context**: requirements.md AC3.7「Preview Render Wrapper shall PNG 出力に必要な API を Editor 側から呼べる形で公開する」をどう実装するか。
- **Alternatives Considered**:
  1. `Texture2D CapturePreviewTexture(int width, int height)` を `PreviewRenderWrapper` に追加し、呼出側で `EncodeToPNG()` + `File.WriteAllBytes` する。
  2. `void SavePreviewPng(string path, int width, int height)` を `PreviewRenderWrapper` に追加し、呼出側はパスのみ指定。
- **Selected Approach**: 1 を採用。
- **Rationale**: `PreviewRenderWrapper` は Render 周辺の責務に閉じるべきで、ファイル I/O は呼出側の責務。テスト容易性も Texture を返す方が高い（PNG エンコード結果のバイト列はテストで検証しづらいが、Texture2D は pixel 単位で検証できる）。
- **Trade-offs**: 呼出側に `EncodeToPNG` + `File.WriteAllBytes` の 2 行を書く必要があるが、ユーザーキャンセル時の制御も呼出側に任せやすい。
- **Follow-up**: ExpressionCreatorWindow.OnSavePreviewClicked で `EditorUtility.SaveFilePanel` → `CapturePreviewTexture` → `EncodeToPNG` → `File.WriteAllBytes` の経路を組む。

## Risks & Mitigations

- **R1: S-10 修正で OSC mapping count ≠ mesh count のケースが新たに顕在化する** — OscInputSource を mesh-index 空間に書き換えると、過去にたまたま動いていた「mapping 数 = mesh 数 + 順序一致」設定以外も正しく動くようになる一方、テストカバレッジが不足する。Mitigation: 新規 `OscInputSourceMaskTests` を `mapping count > mesh count`, `mapping count < mesh count`, `mapping order ≠ mesh order` の 3 シナリオでカバーする。
- **R2: S-11 AnimationClip メタ撤去で AutoExport の transitionDuration 値が AnimationClip 由来に戻る回帰** — AutoExport は SO 側を真値として読んでいることを確認済みだが、bridge ctor 経由の他経路が見逃される可能性。Mitigation: 既存 `FacialCharacterProfileExporter_BaseExpressionBakeTests` / `FacialCharacterProfileExporter_GazeConfigsTests` を実行し、`transitionDuration` 値が SO 側 Inspector スライダー値と一致することを EditMode テストで検証する。
- **R3: S-14 Undo 経路が ScriptableObject の Undo モード（GroupAndApply vs RegisterCompleteObjectUndo）で挙動が変わる** — `BeginUndoGroup` + SerializedProperty 経由の変更が `Undo.RecordObject` を伴わないと復元しないケース。Mitigation: `RemoveGazeConfigAt` の直前で明示的に `Undo.RecordObject(target, "Remove GazeConfig")` を呼び、EditMode テストで「削除 → `Undo.PerformUndo` → 配列要素数が元に戻る」を検証。
- **R4: S-16 Undo subscribe / unsubscribe の非対称によるリーク** — `Undo.undoRedoPerformed` は static event で、subscribe が対称化されないと Editor reload 時に null handler を呼び出して NRE。Mitigation: `AttachToPanelEvent` / `DetachFromPanelEvent` の対で subscribe / unsubscribe を組み、EditMode テストで Attach → Detach → Detach 後の Undo 通知が NoOp であることを検証する。
- **R5: S-15 並び順変更で既存 EditMode テストが name 検索ベースで動いていれば壊れないが、index ベースのテストがあれば破綻する** — `InputSystemAdapterBindingDrawerTests` は name で field を検索するため安全だが念のため確認が必要。Mitigation: テスト実行で確認。
- **R6: S-12 PNG 保存が PreviewRenderUtility の RenderTexture 公開タイミングと噛み合わない** — `EndPreview()` 後の `Texture` は次フレームで上書きされる可能性。Mitigation: `CapturePreviewTexture` 内で `RenderTexture.active` を一時的に切り替えて `Texture2D.ReadPixels` で複製してから返す。

## References

- [Unity Manual: Undo](https://docs.unity3d.com/ScriptReference/Undo.html) — `RecordObject` と `undoRedoPerformed` の使い分け
- [Unity Manual: AnimationUtility.SetAnimationEvents](https://docs.unity3d.com/ScriptReference/AnimationUtility.SetAnimationEvents.html) — AnimationClip イベント書き込み API
- [Unity Manual: PreviewRenderUtility](https://docs.unity3d.com/ScriptReference/PreviewRenderUtility.html) — Editor プレビュー描画 + texture 取得経路
- [Unity Manual: BitArray](https://learn.microsoft.com/en-us/dotnet/api/system.collections.bitarray) — `Or` メソッドが長さ不一致で `ArgumentException` を投げる仕様
- 内部資料: `docs/backlog.md` S-10〜S-16 ブロック — 本 spec の起点となるバックログエントリ
- 内部資料: `.kiro/specs/preview1-polish-pack/requirements.md` — 本 design の入力要件
