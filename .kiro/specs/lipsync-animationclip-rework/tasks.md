# Implementation Plan

> TDD ベース: 各 leaf は Red（失敗するテスト追加）→ Green（最小実装）→ Refactor を 1 leaf 内で完結させる。完了条件は「追加テストが Green」「既存テストが regression なし」「Editor / Player でコンパイル通過」の 3 点をベースラインとする。
>
> 破壊的変更（Inspector デフォルト追加型を `AnimationClipPhonemeEntry` → `ExpressionPhonemeEntry` に変更）の影響範囲は Phase 4（ApplyInitialDefaults）で発火する。Phase 1〜3 は新規型と並走経路のみで既存挙動を維持する。
>
> Open Questions 解決方針:
> - Q1 (`ApplyInitialDefaults` の profile 参照経路): 既存 `IAdapterBindingInitialDefaults.ApplyInitialDefaults()` を保ったまま、`ApplyInitialDefaults` は空 `ExpressionId` の `ExpressionPhonemeEntry` を 5 件生成のみ行う。`AdapterBuildContext` 経由の heuristic auto-link は `BuildSnapshots` 起点で `OnStart` 1 回だけ実行する（既存 `AdapterBuildContext.Profile` を活用、新規 API 追加なし）。
> - Q2 (`PhonemeEntryListView` から profile 参照): Editor では `SerializedObject.targetObject` から `FacialProfileSO` をたどり `Expression` 列挙を取得する（既存 `FacialProfileSO` Inspector パターンと整合）。取得不能時は `TextField` で `ExpressionId` 直接編集に degrade する。

## 1. Foundation: 新規型と参照経路の足場

- [ ] 1.1 ExpressionPhonemeEntry 型を追加してシリアライズ契約を確立する
  - `PhonemeEntryBase` を継承する新規エントリ型を新設し、`PhonemeId` / `MaxWeight`（基底）に加えて `[SerializeField] private string _expressionId` を保持し、`public string ExpressionId` getter を公開する
  - `[System.Serializable]` 属性付きで `Hidano.FacialControl.LipSync.Adapters.PhonemeEntries` 名前空間に配置し、`[SerializeReference]` 経由で永続化可能にする
  - Red: 新規型の存在と `_expressionId` round-trip を検証する EditMode テストを追加し、まず fail させる
  - Green: 型を追加してテストを通す
  - 観察可能な完了条件: `Tests/EditMode/Adapters/PhonemeEntryTests.cs` に追加した `ExpressionPhonemeEntry_SerializedPropertyRoundTrip_PreservesCommonAndExpressionId` が Green、既存 `PhonemeEntryTests` も全て Green
  - _Requirements: 2.1, 2.2, 8.3, 8.4_
  - _Boundary: ExpressionPhonemeEntry_

- [ ] 1.2 (P) `BuildSnapshots` 共有ヘルパーの抽出と 3 経路分岐スケルトンを準備する
  - `nameToIndex` 構築を `BuildNameToIndex` ヘルパーに切り出し、`BlendShape` / `AnimationClip` / `Expression` 3 経路で共有可能にする
  - `_phonemeEntries` 走査ループに `is ExpressionPhonemeEntry` の分岐を未実装スタブ（return false + Debug.LogWarning placeholder）として挿入し、既存 2 経路の挙動を維持する
  - Red: 「`ExpressionPhonemeEntry` を含む binding を `BuildSnapshots` に投入すると現状では skip される」ことを assert するテストを追加して fail させる（後続 2.1 で Green 化）
  - 観察可能な完了条件: `BuildSnapshots` のリファクタ後も既存 `BlendShapePhonemeEntry` / `AnimationClipPhonemeEntry` の挙動テストが全て Green、`ExpressionPhonemeEntry` 経路は明示的に skip + warning を返す状態
  - _Requirements: 5.5, 6.1, 6.3, 6.4_
  - _Boundary: ULipSyncAdapterBinding_

- [ ] 1.3 (P) one-shot 警告抑制基盤を追加する
  - `[NonSerialized] HashSet<string> _loggedWarnings` を `ULipSyncAdapterBinding` に追加し、`OnStart` 開始時に Clear する
  - 警告ログ関数（`LogExpressionResolutionWarning` / `LogAnimationClipFallbackWarning`）の skeleton を `[ULipSyncAdapterBinding]` 接頭辞付きで導入し、`_loggedWarnings` キー（例: `expr-empty:{PhonemeId}`）で重複抑制する
  - Red: 同一原因の警告を 2 回連続発火させ、`LogAssert.Expect(LogType.Warning, ...)` で 1 回のみ呼ばれることを期待するテストを追加して fail させる
  - 観察可能な完了条件: 追加した one-shot 抑制テストが Green、`Debug.LogWarning` レベルが維持される（Error/Exception へ昇格していない）
  - _Requirements: 9.1, 9.4, 9.5_
  - _Boundary: ULipSyncAdapterBinding_

## 2. Core: ExpressionPhonemeEntry の snapshot 構築

- [ ] 2.1 Expression 解決経路（`TryFillExpressionSnapshot`）を実装する
  - `AdapterBuildContext.Profile.FindExpressionById` で `ExpressionPhonemeEntry.ExpressionId` を解決し、見つかった `Expression.BlendShapeValues` から `nameToIndex` 経由で `weights[]` を埋める
  - `weights[index] = Clamp01(mapping.Value * (MaxWeight / 100f))` の正規化規則を適用し、`SampleAnimation` を一切呼ばないことを保証する
  - 最低 1 つの非 0 weight が書き込めた場合のみ snapshot を Add する
  - Red: モック profile に "A" 表情（BlendShapeValues 非空）を仕込み、`BuildSnapshots` で `ExpressionPhonemeEntry` から正しい `PhonemeSnapshot.Weights` を得るテストを追加（現状 fail）
  - Green: `TryFillExpressionSnapshot` を実装してテストを通す
  - 観察可能な完了条件: `BuildSnapshots_WithExpressionPhonemeEntry_ResolvesFromProfile` が Green、`SkinnedMeshRenderer.GetBlendShapeWeight` への書き戻し / 復元呼び出しが Expression 経路では発生しない（テスト内で確認）
  - _Requirements: 2.3, 2.4, 2.6_
  - _Boundary: ULipSyncAdapterBinding_

- [ ] 2.2 (P) 未解決ケースの警告 + skip 挙動を実装する
  - `ExpressionId` 空 / 該当 Expression 不在 / `BlendShapeValues` 空 の 3 原因を区別し、`LogExpressionResolutionWarning(phonemeId, expressionId, cause)` で one-shot ログを出す
  - メッセージに「対象 PhonemeId」「ExpressionId（または未割り当て表記）」「Inspector での割り当て案内」を含める
  - 警告対象エントリは snapshot に追加せず skip する。他エントリの処理は継続する
  - Red: 3 原因それぞれで warning が 1 回ずつ発火し、対象エントリが skip され、他のエントリ（BlendShape 形式 1 件）は正常に snapshot 化されることを検証するテストを追加して fail させる
  - 観察可能な完了条件: `BuildSnapshots_WithExpressionPhonemeEntry_MissingExpression_LogsWarningAndSkips` が 3 原因サブケースで Green
  - _Requirements: 2.5, 9.1, 9.2, 9.4_
  - _Boundary: ULipSyncAdapterBinding_
  - _Depends: 1.3, 2.1_

- [ ] 2.3 (P) Expression 解決時の一時バッファを OnStart 内に閉じ込める
  - `BuildNameToIndex` で生成した `Dictionary<string, int>` を `BuildSnapshots` のローカルスコープに閉じ込め、`OnStart` 終了時に GC 対象になることを保証する
  - hot path（毎フレームの `ULipSyncProvider.Update`）で `ExpressionPhonemeEntry` 解決を呼ばない（`OnStart` 1 回限定）契約を維持する
  - Red: `ULipSyncProviderAllocationTests` に「`ExpressionPhonemeEntry` ベース snapshot を流したフレーム経路で 0 byte GC」を assert するケースを追加して fail させる
  - 観察可能な完了条件: `Update_WithExpressionPhonemeEntrySnapshots_ZeroGCPerFrame` が Green、`BuildSnapshots` 内に新規 `new` が `OnStart` 1 回しか実行されないことを観測する
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 10.6_
  - _Boundary: ULipSyncAdapterBinding, ULipSyncProvider_
  - _Depends: 2.1_

## 3. Core: AnimationClipPhonemeEntry の fallback 経路

- [ ] 3.1 PhonemeId heuristic で profile から Expression を逆引きする
  - `TryFindExpressionByPhonemeIdHeuristic(ctx, phonemeId, out expression)` を実装し、`Expression.Id` または `Expression.Name` が `phonemeId` と完全一致するものを線形走査で探す
  - 日本語表情名（"あ" 等）は完全一致しないため heuristic 対象外であることをコメントで明示し、ドキュメント側（Phase 7）で明示割り当てを案内する
  - Red: profile に Id="A" の Expression を仕込み heuristic がヒット、Id="あ" のみのケースでは miss するテストを追加
  - 観察可能な完了条件: heuristic ヒット / miss 2 ケースの EditMode テストが Green
  - _Requirements: 5.2_
  - _Boundary: ULipSyncAdapterBinding_
  - _Depends: 2.1_

- [ ] 3.2 sample 全 0 検出 → Expression fallback を実装する
  - `TryFillAnimationClipSnapshot` 内で `anyNonZero == false` を検出した時点で `TryFindExpressionByPhonemeIdHeuristic` を呼び、ヒットすれば `TryFillExpressionSnapshotById` 同等経路で snapshot を構築する
  - fallback 採用時は `LogAnimationClipFallbackWarning(phonemeId, clipName)` で one-shot 警告を出し、メッセージに「fallback 採用済み」「より確実な代替として ExpressionPhonemeEntry の使用」案内を含める
  - fallback 不可（heuristic miss）の場合は既存の rendererPath 不一致疑い警告（`Debug.LogWarning`）をそのまま維持する
  - Red: 「sample 全 0 + 同名 Expression あり → fallback で snapshot 構築 + warning」「sample 全 0 + 一致 Expression なし → 既存警告維持 + skip」の 2 ケースを追加して fail させる
  - 観察可能な完了条件: `BuildSnapshots_AnimationClipFallback_WhenSampleAllZeroAndPhonemeMatchExpression` が Green、`BuildSnapshots_AnimationClipFallback_WhenSampleAllZeroAndNoMatch_PreservesExistingWarning` が Green
  - _Requirements: 1.1, 1.2, 5.2, 5.3, 5.4, 9.3, 9.5_
  - _Boundary: ULipSyncAdapterBinding_
  - _Depends: 1.3, 2.1, 3.1_

- [ ] 3.3 (P) AnimationClip 経路の既存挙動が壊れていないことを回帰検証する
  - 「sample 結果が非 0 を含む」ケースで fallback ロジックが起動せず既存経路をそのまま通ることを確認するテストを追加する
  - 既存 `TryFillAnimationClipSnapshot` の `RestoreBlendShapeWeights` 復元処理が変更されていないことを `SkinnedMeshRenderer` の事前 / 事後 weight 比較で観察する
  - 観察可能な完了条件: 既存 `AnimationClipPhonemeEntry` 関連の全テストが Green、追加した「sample 非 0 → fallback 不発動」テストが Green
  - _Requirements: 5.1, 5.5_
  - _Boundary: ULipSyncAdapterBinding_
  - _Depends: 3.2_

## 4. Core: デフォルト追加型の見直し（破壊的変更）

- [ ] 4.1 ApplyInitialDefaults のデフォルト生成型を ExpressionPhonemeEntry に変更する
  - `ApplyInitialDefaults` を AIUEO 5 音素分の `ExpressionPhonemeEntry`（`ExpressionId` 空文字）を生成するよう変更する
  - 既存ガード「`_phonemeEntries.Count > 0` なら no-op」を維持する
  - 破壊的変更ポイント: 既存テストで `AnimationClipPhonemeEntry` 型を期待していた assertion を `ExpressionPhonemeEntry` 期待に書き換える
  - Red: 「空 binding に `ApplyInitialDefaults` を呼ぶと 5 件の `ExpressionPhonemeEntry`（PhonemeId=A/I/U/E/O, ExpressionId=空）が生成される」テストを追加して fail させる
  - 観察可能な完了条件: `ApplyInitialDefaults_OnEmpty_GeneratesFiveExpressionEntries` が Green、`ApplyInitialDefaults_OnNonEmpty_DoesNothing` が Green、既存 `AnimationClipPhonemeEntry` ベースの defaults テストは新挙動に追随済み
  - _Requirements: 4.1, 4.3, 4.4_
  - _Boundary: ULipSyncAdapterBinding_

- [ ] 4.2 BuildSnapshots 起点で AIUEO heuristic auto-link を実行する
  - `BuildSnapshots` 開始時、`_phonemeEntries` 内の `ExpressionPhonemeEntry` で `ExpressionId` 空かつ `PhonemeId` が "A"/"I"/"U"/"E"/"O" のいずれかであるものに対し、heuristic match (Id/Name 完全一致) で `ExpressionId` を埋める
  - auto-link はメモリ上の解決にとどめ、`_phonemeEntries` のシリアライズデータ自体は書き換えない（preview 段階で挙動の予測性を保つ）
  - profile に該当 Expression がない場合は空のまま warning + skip（要件 2.5 / 4.3 と整合）
  - Red: 「profile に Id="A" Expression あり、空 `ExpressionPhonemeEntry`(PhonemeId=A) を含む binding を `BuildSnapshots` するとそのエントリが正しく snapshot 化される」テストを追加
  - 観察可能な完了条件: auto-link ヒット / miss 両ケースが Green、auto-link 後の `_phonemeEntries[i].ExpressionId` が永続化されていないこと（再ロードで空に戻る）を確認するテストが Green
  - _Requirements: 4.2, 4.3_
  - _Boundary: ULipSyncAdapterBinding_
  - _Depends: 2.1, 4.1_

## 5. Core: Inspector への Expression 形式統合

- [ ] 5.1 EntryKind.Expression と Add メニュー項目を追加する
  - `PhonemeEntryListView.EntryKind` に `Expression` を追加し、`EntryTypeChoices` / `ToLabel` / `TryParseKind` / `ResolveEntryKind` を拡張する
  - `OpenAddMenu` に「Expression 形式」項目を追加し、選択時に `CreateEntry(EntryKind.Expression)` 経由で新規 `ExpressionPhonemeEntry` を `managedReferenceValue` に設定する
  - Red: 「Add メニューから Expression 形式を選択すると `ExpressionPhonemeEntry` が 1 件追加される」テストを `PhonemeEntryListViewTests` に追加して fail させる
  - 観察可能な完了条件: `AddEntry_Expression_InsertsExpressionPhonemeEntry` が Green、`managedReferenceFullTypename` が `ExpressionPhonemeEntry` の FQN と一致することを SerializedProperty 経由で確認
  - _Requirements: 3.1, 3.2_
  - _Boundary: PhonemeEntryListView_

- [ ] 5.2 形式切替時の共通フィールド保持を実装する
  - 既存 entry から `PhonemeId` / `MaxWeight` を読み出し、新形式インスタンス（`ExpressionPhonemeEntry`）に値を移送した上で `managedReferenceValue` を差し替える
  - 切替対象は BlendShape ⇔ AnimationClip ⇔ Expression の 6 方向すべて
  - Red: 「BlendShape → Expression 切替後も `PhonemeId` / `MaxWeight` が保持される」テストを追加して fail させる
  - 観察可能な完了条件: `SetEntryKind_FromBlendShapeToExpression_PreservesCommonFields` が Green、AnimationClip → Expression / Expression → BlendShape の対称ケースも Green
  - _Requirements: 3.3, 8.3_
  - _Boundary: PhonemeEntryListView_
  - _Depends: 5.1_

- [ ] 5.3 (P) Expression 行 UI（DropdownField + HelpBox）を構築する
  - `AddExpressionFields(row, entryProperty)` を実装し、`SerializedObject.targetObject` から `FacialProfileSO` の `Expression` 一覧を取得して DropdownField の choices に「Name (Id)」形式で並べる
  - 取得不能（targetObject から profile に到達できない）時は `TextField` で `ExpressionId` 直接編集に degrade する
  - 選択値変更時は表示文字列から `Id` を逆引きして `_expressionId` SerializedProperty に書き戻し、`ApplyModifiedProperties` を呼ぶ
  - Red: profile に 3 つの Expression を仕込み、DropdownField の choices.Count == 3 + 空選択肢を含むこと、選択で `_expressionId` が更新されることを検証するテストを追加
  - 観察可能な完了条件: Expression 行 UI 構築テストが Green、degrade ケース（profile 不在）でも例外を出さず TextField が表示される
  - _Requirements: 3.4_
  - _Boundary: PhonemeEntryListView_
  - _Depends: 5.1_

- [ ] 5.4 (P) 未割り当て HelpBox の表示切替を実装する
  - `ApplyExpressionWarningVisibility(warning, expressionIdValue)` を実装し、空文字なら `DisplayStyle.Flex`、非空なら `DisplayStyle.None` に切替える
  - HelpBox メッセージ「Expression 未割り当てです。リップシンクが動作しません」を表示する
  - Red: 「`ExpressionId` 空 → HelpBox 表示」「`ExpressionId` 非空 → HelpBox 非表示」の 2 ケーステストを追加
  - 観察可能な完了条件: `BindRow_ExpressionWithoutId_ShowsWarningHelpBox` が Green、`ExpressionId` 設定後に `BindRow` を再呼び出しすると HelpBox が `DisplayStyle.None` になることを検証
  - _Requirements: 3.5, 3.6_
  - _Boundary: PhonemeEntryListView_
  - _Depends: 5.1_

## 6. Validation: 後方互換性とパフォーマンスの総合検証

- [ ] 6.1 BlendShapePhonemeEntry / AnimationClipPhonemeEntry の後方互換ロードを検証する
  - 既存形式（BlendShape / AnimationClip）の `[SerializeReference]` データを含む binding asset を fixture として用意し、本 spec 適用後でも `managedReferenceValue` が loss なく解決されることを確認する
  - `PhonemeEntryBase.PhonemeId` / `MaxWeight` の名前・型が維持されており、既存 `BlendShapePhonemeEntry` の処理経路が regression していないことを assertion する
  - 観察可能な完了条件: 既存 fixture（BlendShape のみ / AnimationClip のみ / 混在）3 種で `BuildSnapshots` 結果が本 spec 適用前と bit 単位で一致することを検証するテストが Green
  - _Requirements: 8.1, 8.2, 8.3, 8.4, 5.1, 5.5_
  - _Boundary: ULipSyncAdapterBinding, PhonemeEntries_

- [ ] 6.2 (P) End-to-end の GC アロケーション regression テストを追加する
  - `EndToEndGcAllocationTests` に `ExpressionPhonemeEntry` ベース binding ケースを追加し、1 秒以上の `ULipSyncProvider.Update` ループで毎フレーム 0 byte を維持することを観測する
  - 既存の AnimationClip / BlendShape ケースも引き続き 0 byte であることを確認（regression なし）
  - 観察可能な完了条件: 追加した E2E アロケーションテストが Green、CI（Windows セルフホストランナー）でも安定して pass する
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 10.5, 10.6_
  - _Boundary: ULipSyncProvider, EndToEndGcAllocationTests_
  - _Depends: 2.3, 4.2_

- [ ] 6.3 (P) 全テストスイートの regression 確認
  - `com.hidano.facialcontrol.lipsync` パッケージ配下の EditMode テスト全件を Unity batchmode で実行し、全 Green を確認する
  - 既存 `PhonemeEntryTests` / `PhonemeEntryListViewTests` / `ULipSyncProviderAllocationTests` / `ULipSyncAdapterBindingTests` を含む
  - 観察可能な完了条件: `test-results/editmode.xml` で全テストが Pass、failure / inconclusive ともに 0 件
  - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5_
  - _Boundary: Tests/EditMode_
  - _Depends: 6.1_

## 7. Validation: ドキュメントと backlog 反映

- [ ] 7.1 Documentation~ に 3 形式の使い分けガイドを追加する
  - `Packages/com.hidano.facialcontrol.lipsync/Documentation~/PhonemeEntry形式ガイド.md` を新規作成し、BlendShape / AnimationClip / Expression の 3 形式の使い分けを記述する
  - 「Expression 形式が最も確実に動く」「AnimationClip 形式は rendererPath 整合が必要」「BlendShape 形式は単一 BlendShape 名で簡潔だが組み合わせ口形状には不向き」の 3 観点を比較表として含める
  - Migration Notes セクションを設け、Inspector デフォルト型が `AnimationClipPhonemeEntry` → `ExpressionPhonemeEntry` に変更された旨と既存 SerializeReference データは loss なくロードできる旨を明記する
  - 既存サンプル（`Multi Source Blend Demo` 等）が本 spec 後にどの形式を採用するかの方針を記述する
  - 観察可能な完了条件: ガイド md が存在し、3 形式比較表 + Migration Notes + サンプル方針の 3 セクションを含む
  - _Requirements: 8.5, 11.1, 11.2, 11.3_
  - _Boundary: Documentation~_

- [ ] 7.2 backlog.md の S-9 項目を更新する
  - `docs/backlog.md` の S-9 項目を本 spec 完了状態に更新する
  - 候補 (b)「`AnimationUtility.GetCurveBindings` による Editor 事前検証 + runtime path 補正」を将来課題として残置する旨を明記する
  - 観察可能な完了条件: `docs/backlog.md` の S-9 セクションが「本 spec で対応済み」「候補 (b) は将来課題として残置」の 2 点を含む状態
  - _Requirements: 1.3, 1.4, 11.4_
  - _Boundary: docs/backlog.md_

## Requirements Coverage Matrix

| Requirement | Covered by Tasks |
|-------------|------------------|
| 1.1 | 3.2 |
| 1.2 | 3.2 |
| 1.3 | 7.2 |
| 1.4 | 7.2 |
| 2.1 | 1.1 |
| 2.2 | 1.1 |
| 2.3 | 2.1 |
| 2.4 | 2.1 |
| 2.5 | 2.2 |
| 2.6 | 2.1 |
| 3.1 | 5.1 |
| 3.2 | 5.1 |
| 3.3 | 5.2 |
| 3.4 | 5.3 |
| 3.5 | 5.4 |
| 3.6 | 5.4 |
| 4.1 | 4.1 |
| 4.2 | 4.2 |
| 4.3 | 4.1, 4.2 |
| 4.4 | 4.1 |
| 5.1 | 3.3, 6.1 |
| 5.2 | 3.1, 3.2 |
| 5.3 | 3.2 |
| 5.4 | 3.2 |
| 5.5 | 1.2, 3.3, 6.1 |
| 6.1 | 1.2, 2.1 |
| 6.2 | 1.2 |
| 6.3 | 1.2, 2.1 |
| 6.4 | 1.2 |
| 7.1 | 2.3, 6.2 |
| 7.2 | 2.3, 6.2 |
| 7.3 | 2.3, 6.2 |
| 7.4 | 2.3, 6.2 |
| 8.1 | 6.1 |
| 8.2 | 6.1 |
| 8.3 | 1.1, 5.2, 6.1 |
| 8.4 | 1.1, 6.1 |
| 8.5 | 7.1 |
| 9.1 | 1.3, 2.2 |
| 9.2 | 2.2 |
| 9.3 | 3.2 |
| 9.4 | 1.3, 2.2 |
| 9.5 | 1.3, 3.2 |
| 10.1 | 1.1, 6.3 |
| 10.2 | 2.1, 2.2, 6.3 |
| 10.3 | 3.2, 6.3 |
| 10.4 | 5.1, 5.2, 5.3, 5.4, 6.3 |
| 10.5 | 6.2, 6.3 |
| 10.6 | 2.3, 6.2 |
| 11.1 | 7.1 |
| 11.2 | 7.1 |
| 11.3 | 7.1 |
| 11.4 | 7.2 |
