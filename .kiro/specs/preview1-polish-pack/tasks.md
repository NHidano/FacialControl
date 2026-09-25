# Implementation Plan — preview1-polish-pack

本 spec は `docs/backlog.md` の短期項目 S-10〜S-16 を集約した preview.1 polish パックである。各 leaf task は `/kiro:spec-run` での連続実行を前提に **1 commit に収まる粒度** に分解しており、TDD（Red-Green-Refactor）の流れに沿って「テスト追加 → 実装 → 統合確認」の順で並べている。EditMode テストは各パッケージの `Tests/EditMode/` 配下に追加し、Unity テストランナーは `run_in_background` を使わず `timeout: 600000` の同期 Bash 呼び出しで実行する。

- [x] 1. S-10: OSC 受信側 ContributeMask / TryWriteValues の mesh-index 空間整合
- [x] 1.1 IInputSource.ContributeMask の mesh-index 空間契約を XML doc-comment に明文化する
  - `IInputSource` インターフェースの `ContributeMask` プロパティに `<remarks>` を追加し、「返される `BitArray` は mesh BlendShape index 空間であり、長さは `BlendShapeCount` と一致する」契約を明文化する
  - 既存 `AnalogBlendShapeInputSource` / `OscInputSource` の `<remarks>` 表現と整合させ、IDE Quick Info で読める形にする
  - 観測可能な完了条件: `IInputSource` の Quick Info（XML doc-comment）が更新され、mesh-index 空間と `BlendShapeCount` 一致の契約が記載されている
  - _Requirements: 1.6_
  - _Boundary: IInputSource_
- [x] 1.2 HeartbeatConsistencyChecker を mesh BlendShape 数長で構築するテストを追加する（Red）
  - `HeartbeatConsistencyCheckerTests` を mesh-index 空間追従に書き換え、`_contributeMask` / `_skipMask` の長さが mesh BlendShape 数と一致することを検証するケースを追加する
  - mapping 数 ≠ mesh 数 / mapping 順 ≠ mesh 順 / mapping name が mesh に存在しないケースを網羅する
  - 観測可能な完了条件: 新ケースが Red（コンパイル失敗または assertion 失敗）でテストランナーから報告される
  - _Requirements: 1.2, 1.3, 1.7_
  - _Boundary: HeartbeatConsistencyChecker, HeartbeatConsistencyCheckerTests_
- [x] 1.3 HeartbeatConsistencyChecker を mesh BlendShape 数長で構築するよう実装する（Green）
  - 新 ctor overload `(IReadOnlyList<string> meshBlendShapeNames, IReadOnlyList<OscMapping> receiverMappings, bool warnLogEnabled)` を追加し、`_contributeMask` / `_skipMask` を mesh 数長で確保する
  - `UpdateFromHeartbeat` 経路を mapping name → mesh index 逆引きで bit 立て直すロジックに書き換える
  - 既存 ctor は preview 段階のため `[Obsolete]` を付けずに残す
  - 観測可能な完了条件: 1.2 で追加した EditMode テストがすべて Green で通る
  - _Requirements: 1.2, 1.7_
  - _Boundary: HeartbeatConsistencyChecker_
- [x] 1.4 OscInputSource の mesh-index 空間書込みテストを新規追加する（Red）
  - 新規 `Tests/EditMode/Adapters/InputSources/OscInputSourceMaskTests.cs` を追加し、`ContributeMask` / `TryWriteValues` が mesh-index 空間に書き込むことを検証する
  - `mapping count > mesh count` / `mapping count < mesh count` / `mapping order ≠ mesh order` / `mapping name が mesh に存在しない` の 4 シナリオを網羅する
  - 観測可能な完了条件: 新規テストクラスがビルドされ、未実装のため Red 状態でテストランナーから報告される
  - _Requirements: 1.4, 1.5, 1.8_
  - _Boundary: OscInputSourceMaskTests_
- [x] 1.5 OscInputSource.TryWriteValues を mesh-index 空間書込みに書き換える（Green）
  - `OscInputSource` の ctor overload に `mappingIndexToMeshIndex` を受ける形を追加し、`_contributeMask` を mesh-index 空間で保持する
  - `TryWriteValues` 内で `readBuffer[mappingIdx] → output[mappingIndexToMeshIndex[mappingIdx]]` のリマップを行い、`-1` の mapping は未書込みとしてスキップする
  - 観測可能な完了条件: 1.4 の `OscInputSourceMaskTests` がすべて Green で通り、mapping count ≠ mesh count でも値が誤った mesh index に書かれない
  - _Requirements: 1.4, 1.5_
  - _Boundary: OscInputSource_
- [x] 1.6 OscReceiverAdapterBinding.OnStart で mesh-index 空間整列経路を結線する
  - `OscReceiverAdapterBinding.OnStart(in ctx)` 内で `ctx.BlendShapeNames` から `mappingIndexToMeshIndex` を 1 度だけ構築し、新規 ctor overload で `HeartbeatConsistencyChecker` と `OscInputSource` の双方を mesh-index 空間で初期化する
  - mapping name が `ctx.BlendShapeNames` に存在しない場合 `-1` を入れ、毎フレームの逆引きを避けて GC ゼロ契約を維持する
  - 観測可能な完了条件: 1.2〜1.5 までの EditMode テストがすべて Green を維持したまま、`OscReceiverAdapterBinding` 経由で Checker / Source が mesh-index 空間で初期化される
  - _Requirements: 1.1, 1.3_
  - _Boundary: OscAdapterBinding_
- [x] 1.7 OscReceiverDemo + OscOutputDemo 同時起動の例外消失を EditMode で再現検証する
  - mapping 数 ≠ mesh 数の構成で `LayerInputSourceAggregator.AggregateInternal` 経由の `BitArray.Or` が `ArgumentException` を投げないことを EditMode テストで確認する
  - 既存 PlayMode 群（実 UDP）に踏み込まず、Aggregator + OscInputSource の合成経路のみを EditMode Fake で組む
  - 観測可能な完了条件: 「mapping ≠ mesh の構成で Aggregator 合成が例外なく完了する」EditMode テストが Green で通る
  - _Requirements: 1.1_
  - _Boundary: OscInputSource, LayerInputSourceAggregator (test only)_

- [x] 2. S-11: Expression 遷移メタの AnimationClip ベイク撤去
- [x] 2.1 AnimationClipExpressionSampler のメタ既定値返却テストを更新する（Red）
  - 既存 `AnimationClipExpressionSamplerTests` / `AnimationClipExpressionSamplerMetadataTests` の「AnimationEvent からメタを抽出する」期待を「メタが無ければ既定値（0.25 秒 / Linear）を返す」期待に書き換える
  - AnimationEvent が存在する旧 Clip を読んでもエラーにはならず既定値経路に落ちることを確認するケースを追加する
  - 観測可能な完了条件: 更新後のテストが Red（旧ロジック期待で失敗）で報告される
  - _Requirements: 2.2, 2.7_
  - _Boundary: AnimationClipExpressionSamplerTests, AnimationClipExpressionSamplerMetadataTests_
- [x] 2.2 ExpressionClipBakery.Bake から AnimationEvent 書き込みを撤去する（Green）
  - `ExpressionClipBakery.Bake` から `AnimationUtility.SetAnimationEvents` 経路を削除し、BlendShape / Bone Curve 書込みのみを残す
  - `transitionDuration` / `transitionCurvePreset` 引数は後方互換のため shape を維持し、内部では未使用にする
  - 観測可能な完了条件: `Bake` 実行後の AnimationClip に `AnimationEvent` が含まれないことを EditMode テストで確認できる
  - _Requirements: 2.1, 2.6_
  - _Boundary: ExpressionClipBakery_
- [x] 2.3 AnimationClipExpressionSampler.SampleSummary をメタ無し既定値返却に統一する（Green）
  - `ExtractTransitionMetadata` のロジックを「AnimationEvent が無ければ `Expression.DefaultTransitionDuration` / `TransitionCurvePreset.Linear` を返す」だけに簡略化する
  - `MetaSetFunctionName` 等の定数は後方互換のため残し、内部で未使用であることを doc-comment に明示する
  - 観測可能な完了条件: 2.1 で更新したテストがすべて Green で通る
  - _Requirements: 2.2_
  - _Boundary: AnimationClipExpressionSampler_
- [x] 2.4 ExpressionCreatorWindow から「遷移メタデータ」Foldout を削除する
  - `_transitionDurationField` / `_curveTypeDropdown` を含む `transitionFoldout` 全体を UI から除去する
  - `OnBakeClicked` の引数経路は既定値（0.25 / Linear）で `ExpressionClipBakery.Bake` を呼ぶ形に揃え、`RestoreSliderValuesFromTargetClip` のメタ復元コードも撤去する
  - `ExpressionCreatorWindowTests` の対応ケースを「メタ Foldout が存在しない」期待に書き換える
  - 観測可能な完了条件: Window 表示時に「遷移メタデータ」Foldout が一切現れず、対応 EditMode テストが Green で通る
  - _Requirements: 2.3, 2.4, 2.7_
  - _Boundary: ExpressionCreatorWindow, ExpressionCreatorWindowTests_
- [x] 2.5 FacialCharacterProfileExporter の SO 真値経路を回帰確認する
  - `FacialCharacterProfileExporter_BaseExpressionBakeTests` 系の既存 EditMode テストで `dto.transitionDuration` が Inspector スライダー値（SO 側）と一致することを確認するケースを追加または明示化する
  - AnimationEvent 撤去後も AutoExport の出力 JSON / DTO が回帰しないことを保証する
  - 観測可能な完了条件: 既存 + 追加の AutoExporter EditMode テストがすべて Green で通る
  - _Requirements: 2.5_
  - _Boundary: FacialCharacterProfileExporter (test only)_

- [x] 3. S-12: ExpressionCreatorWindow UI 改善
- [x] 3.1 ベイクボタン潰れ防止の EditMode テストを追加し flexShrink / minWidth を適用する
  - `ExpressionCreatorWindowTests` に「bottomSection の bake ボタンが `style.flexShrink == 0` かつ `style.minWidth` を持つ」期待ケースを追加する
  - `ExpressionCreatorWindow.CreateGUI` の bottomSection で `bakeButton.style.flexShrink = 0f` と `bakeButton.style.minWidth = 140f` を適用する
  - 観測可能な完了条件: 追加したテストが Green で通り、ウィンドウ幅を縮めてもベイクボタンが潰れず操作可能である
  - _Requirements: 3.1, 3.8_
  - _Boundary: ExpressionCreatorWindow, ExpressionCreatorWindowTests_
- [x] 3.2 PreviewRenderWrapper に PNG 出力用 Texture2D 取得 API を追加する
  - `PreviewRenderWrapper` に `Texture2D CapturePreviewTexture(int width, int height)` を追加し、`PreviewRenderUtility` 経由のフレームを `RenderTexture.active` 切替 + `Texture2D.ReadPixels` で複製して返す経路を実装する
  - 既存 Setup / Render / Dispose ライフサイクルを破壊しないことを EditMode テストで確認する
  - 観測可能な完了条件: `CapturePreviewTexture` が指定サイズの `Texture2D` を返し、`ReadPixels` 経由で pixel が取得できる EditMode テストが Green で通る
  - _Requirements: 3.3, 3.7_
  - _Boundary: PreviewRenderWrapper_
- [x] 3.3 ExpressionCreatorWindow leftPanel に PNG 書き出しボタンを追加する
  - leftPanel に「プレビューを PNG として保存」ボタンを追加し、クリックで `EditorUtility.SaveFilePanel` → `PreviewRenderWrapper.CapturePreviewTexture` → `Texture2D.EncodeToPNG` → `File.WriteAllBytes` の経路を結線する
  - `ExpressionCreatorWindowTests` に「ボタンが leftPanel に存在し、PNG 保存 handler が結線されている」期待ケースを追加する
  - 観測可能な完了条件: ボタンクリック相当の handler 呼出しで指定パスに PNG ファイルが生成されることを EditMode テスト（一時パス）で確認できる
  - _Requirements: 3.2, 3.3, 3.8_
  - _Boundary: ExpressionCreatorWindow, ExpressionCreatorWindowTests_
- [x] 3.4 ExpressionCreatorWindow に AnimationClip 新規作成ボタンを追加する
  - `_clipField` 隣に「新規作成」ボタンを追加し、クリックで `EditorUtility.SaveFilePanelInProject` → `AssetDatabase.CreateAsset(new AnimationClip())` → `_clipField.value` 代入 → `OnClipFieldChanged` 発火の経路を結線する
  - ファイルダイアログがキャンセルされた場合（path が空）は AnimationClip を生成せず `_clipField.value` を変更しないことを保証する
  - `ExpressionCreatorWindowTests` に「新規作成 handler 呼出しで Clip 生成、キャンセル時に未変更」を検証するケースを追加する
  - 観測可能な完了条件: 追加した EditMode テストがすべて Green で通り、キャンセル経路で AssetDatabase に Asset が作成されない
  - _Requirements: 3.4, 3.5, 3.6, 3.8_
  - _Boundary: ExpressionCreatorWindow, ExpressionCreatorWindowTests_

- [x] 4. S-13: Expression Row の Gaze 用 AnimationClip スロット非表示
- [x] 4.1 isGaze 値に応じた AnimationClip スロット表示制御の EditMode テストを追加する（Red）
  - 新規または既存の `FacialCharacterProfileSOInspectorExpressionRowTests` に、`isGaze=true` で AnimationClip ObjectField が `DisplayStyle.None`、`isGaze=false` で `DisplayStyle.Flex` になることを検証するケースを追加する
  - `isGaze` トグルの動的切替で同じ row の clipField 表示が即時更新されるケースも追加する
  - 観測可能な完了条件: 追加ケースが Red（未実装のため失敗）でテストランナーから報告される
  - _Requirements: 4.1, 4.2, 4.3, 4.5_
  - _Boundary: FacialCharacterProfileSOInspectorExpressionRowTests_
- [x] 4.2 FacialCharacterProfileSOInspector の Expression Row clip スロット表示制御を実装する（Green）
  - `BuildAnimationClipFields` 内で `clipField.style.display = currentIsGaze ? DisplayStyle.None : DisplayStyle.Flex` を初期化時に適用する
  - `isGazeToggle.RegisterValueChangedCallback` 内で同じ row の clipField を再取得して `style.display` を更新する経路を追加し、SerializedProperty 値は破壊しない
  - 観測可能な完了条件: 4.1 のテストがすべて Green で通り、`isGaze=true` 状態の Expression Row に AnimationClip スロットが描画されない
  - _Requirements: 4.1, 4.2, 4.3, 4.4_
  - _Boundary: FacialCharacterProfileSOInspector_

- [x] 5. S-14: GazeConfigs 一括再生成 + Undo 経路復元
- [x] 5.1 GazeConfig 削除 Undo 経路の EditMode テストを追加する（Red）
  - `FacialCharacterProfileSOInspectorGazeConfigsTests` に「GazeConfig 行を削除 → `Undo.PerformUndo` → 配列要素数と内容が削除前に戻る」検証ケースを追加する
  - 観測可能な完了条件: 追加ケースが Red（`Undo.RecordObject` 未挿入のため Undo で復元されない）でテストランナーから報告される
  - _Requirements: 5.4, 5.5, 5.6_
  - _Boundary: FacialCharacterProfileSOInspectorGazeConfigsTests_
- [x] 5.2 RemoveGazeConfigAt に Undo.RecordObject を挿入する（Green）
  - `RemoveGazeConfigAt` 内の `_rootGazeConfigsProperty.DeleteArrayElementAtIndex` 直前に `Undo.RecordObject(target, "Remove GazeConfig")` を挿入し、`BeginUndoGroup` 経路は維持する
  - 観測可能な完了条件: 5.1 のテストが Green で通り、Ctrl+Z 相当で GazeConfig 行が SerializedProperty レベルで復元される
  - _Requirements: 5.4, 5.5_
  - _Boundary: FacialCharacterProfileSOInspector_
- [x] 5.3 GazeConfigs 一括再生成ロジックの EditMode テストを追加する（Red）
  - 「`isGaze=true` の Expression で GazeConfig 未生成のものに対して GazeConfig が補完される」「既存 GazeConfig は上書きされない」期待ケースを `FacialCharacterProfileSOInspectorGazeConfigsTests` に追加する
  - 観測可能な完了条件: 追加ケースが Red（未実装のため失敗）でテストランナーから報告される
  - _Requirements: 5.2, 5.3, 5.6_
  - _Boundary: FacialCharacterProfileSOInspectorGazeConfigsTests_
- [x] 5.4 「GazeConfig を一括再生成」ボタンと補完ロジックを実装する（Green）
  - `RebuildGazeConfigsUI` 周辺に「GazeConfig を一括再生成」ボタンを `GazeConfigBulkResolveButton` 近傍に配置する
  - `BulkRegenerateGazeConfigs()` を実装し、Expression を走査して `isGaze=true` かつ GazeConfig 未生成の Expression にだけ新規 GazeConfig を `_rootGazeConfigsProperty` に追加し、`ResetGazeConfigToDefaults` で初期化する（既存値は触らない）
  - 観測可能な完了条件: 5.3 のテストが Green で通り、UI 上に「GazeConfig を一括再生成」ボタンが表示される
  - _Requirements: 5.1, 5.2, 5.3_
  - _Boundary: FacialCharacterProfileSOInspector_

- [x] 6. S-15: Gaze 左右別 Action UI 配置見直し
- [x] 6.1 Expression Binding 行の並び順 + Action 名 enable 状態の EditMode テストを更新する（Red）
  - `InputSystemAdapterBindingDrawerTests` に「Expression Binding 行が `表情 ID → 動作モード → useDistinct → 左 Action → 右 Action → Action 名 → トリガモード` の順で並ぶ」期待ケースを追加する
  - 「Gaze + `useDistinct=true` で Action 名フィールドが `SetEnabled(false)` でグレーアウトし `DisplayStyle.None` にはならない」「Gaze + `useDistinct=false` で `SetEnabled(true)`」のケースを追加する
  - 観測可能な完了条件: 追加ケースが Red（旧並び順 / `DisplayStyle.None` のため失敗）でテストランナーから報告される
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.6_
  - _Boundary: InputSystemAdapterBindingDrawerTests_
- [x] 6.2 InputSystemAdapterBindingDrawer の並び順と Action 名 enable 制御を実装する（Green）
  - `BindExpressionBindingRow` 内の `element.Add(...)` 順を `expressionDropdown → bindingModeField → useDistinctLeftRightToggle → actionLeftDropdown → actionRightDropdown → actionDropdown → triggerModeField → overlay 関連` に並び替える
  - `UpdateGazeActionFieldVisibility` を `UpdateGazeActionFieldState` 相当に書き換え、`actionField.style.display` 制御を撤廃して `actionField.SetEnabled(!(isGaze && useDistinctLeftRight))` のみで制御する
  - `useDistinctLeftRightToggle` / `bindingModeField` の `RegisterValueChangedCallback` 経路でも同じ enable 制御が即時反映されるようにする
  - 観測可能な完了条件: 6.1 のテストがすべて Green で通り、useDistinct=true でも Action 名フィールドが画面上から消えずグレーアウトで残る
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5_
  - _Boundary: InputSystemAdapterBindingDrawer_

- [x] 7. S-16: uLipSync PhonemeEntry Undo 表示整合
- [x] 7.1 PhonemeEntryListView の Undo 後表示整合 EditMode テストを追加する（Red）
  - `PhonemeEntryListViewTests` に「BlendShape ↔ AnimationClip モード切替 → `Undo.PerformUndo` 後の ListView 表示値が SerializedProperty の現在値と一致する」検証ケースを追加する
  - 「Undo が連続発火しても例外なく Rebuild が完了する」ケースも追加する
  - 観測可能な完了条件: 追加ケースが Red（`Undo.undoRedoPerformed` 未購読のため失敗）でテストランナーから報告される
  - _Requirements: 7.4, 7.5, 7.6_
  - _Boundary: PhonemeEntryListViewTests_
- [x] 7.2 PhonemeEntryListView に Undo.undoRedoPerformed 購読 / 解除を実装する（Green）
  - コンストラクタで `RegisterCallback<AttachToPanelEvent>(OnAttachToPanel)` / `RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel)` を登録し、Attach 時に `Undo.undoRedoPerformed += OnUndoRedoPerformed`、Detach 時に `-=` で対称化する
  - `OnUndoRedoPerformed` で `_listProperty?.serializedObject?.targetObject` の null safety を確認後、`serializedObject.Update()` → `RebuildIndexProxy(_indexProxy, GetListProperty())` → `_listView.Rebuild()` を実行する
  - 観測可能な完了条件: 7.1 のテストがすべて Green で通り、Attach → Detach 後の Undo 通知が NoOp で例外を発生させない
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5_
  - _Boundary: PhonemeEntryListView_

- [x] 8. 仕上げ: backlog 整理とリリースノート更新
- [x] 8.1 docs/backlog.md から S-10〜S-16 エントリを削除する
  - 完了した S-10 / S-11 / S-12 / S-13 / S-14 / S-15 / S-16 ブロックを `docs/backlog.md` から削除し、見出し / 目次の整合を取る
  - 観測可能な完了条件: `docs/backlog.md` に S-10〜S-16 の項目が存在せず、preview.1 polish パック起因の短期項目が一掃された状態になる
  - _Requirements: 2.6, 6.5_
  - _Boundary: docs/backlog.md_
- [x] 8.2 preview.1 リリースノートと CHANGELOG / README に破壊的変更を反映する
  - `AnimationEvent` 由来の遷移メタ撤去（Req 2.6）と Input Drawer の並び順変更（Req 6.5）が preview.1 リリースノート / 該当パッケージの CHANGELOG / README に「破壊的変更」「自動マイグレーション無し」として記載されている状態にする
  - 該当パッケージ（`com.hidano.facialcontrol`, `com.hidano.facialcontrol.inputsystem`）の更新有無は実ファイル状況に応じて調整する
  - 観測可能な完了条件: preview.1 リリースノート相当のドキュメント上で S-11 / S-15 の破壊的変更が明示的にユーザー向けに告知されている
  - _Requirements: 2.6, 6.5_
  - _Boundary: Release notes / CHANGELOG / README_

## Requirements Coverage Map

| Requirement | Covered by Tasks |
|-------------|------------------|
| 1.1 | 1.6, 1.7 |
| 1.2 | 1.2, 1.3 |
| 1.3 | 1.2, 1.6 |
| 1.4 | 1.4, 1.5 |
| 1.5 | 1.4, 1.5 |
| 1.6 | 1.1 |
| 1.7 | 1.2, 1.3 |
| 1.8 | 1.4 |
| 2.1 | 2.2 |
| 2.2 | 2.1, 2.3 |
| 2.3 | 2.4 |
| 2.4 | 2.4 |
| 2.5 | 2.5 |
| 2.6 | 2.2, 8.1, 8.2 |
| 2.7 | 2.1, 2.4 |
| 3.1 | 3.1 |
| 3.2 | 3.3 |
| 3.3 | 3.2, 3.3 |
| 3.4 | 3.4 |
| 3.5 | 3.4 |
| 3.6 | 3.4 |
| 3.7 | 3.2 |
| 3.8 | 3.1, 3.3, 3.4 |
| 4.1 | 4.1, 4.2 |
| 4.2 | 4.1, 4.2 |
| 4.3 | 4.1, 4.2 |
| 4.4 | 4.2 |
| 4.5 | 4.1 |
| 5.1 | 5.4 |
| 5.2 | 5.3, 5.4 |
| 5.3 | 5.3, 5.4 |
| 5.4 | 5.1, 5.2 |
| 5.5 | 5.1, 5.2 |
| 5.6 | 5.1, 5.3 |
| 6.1 | 6.1, 6.2 |
| 6.2 | 6.1, 6.2 |
| 6.3 | 6.1, 6.2 |
| 6.4 | 6.1, 6.2 |
| 6.5 | 6.2, 8.1, 8.2 |
| 6.6 | 6.1 |
| 7.1 | 7.2 |
| 7.2 | 7.2 |
| 7.3 | 7.2 |
| 7.4 | 7.1, 7.2 |
| 7.5 | 7.1, 7.2 |
| 7.6 | 7.1 |
