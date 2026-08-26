# Requirements Document

## Project Description (Input)
LipSync の AnimationClip 形式が動かない件の根本対応。`ULipSyncAdapterBinding.TryFillAnimationClipSnapshot` は `entry.Clip.SampleAnimation(ctx.HostGameObject, sampleTime)` 経由で口形状クリップを採取するが、ユーザー作成 AnimationClip の rendererPath とキャラ階層が一致しない / クリップが BlendShape カーブを持たない等で snapshot が空になり結果として ContributeMask が空になる → BlendShape 直指定形式（`BlendShapePhonemeEntry`）では動くが AnimationClip 形式（`AnimationClipPhonemeEntry`）では動かない、という挙動になる。直近 PR で「sample 結果が全 0 のとき LogWarning」「Inspector に AnimationClip 未割り当て HelpBox」を追加して原因切り分けはできるようにしたが、ユーザー視点の体験としては依然「動かない」。候補根本対応: (a) 「FacialProfile 内 Expression（既登録の表情）を音素として直接指定する `ExpressionPhonemeEntry` を新設」して AnimationClip 採取を AdapterBinding 内では行わず profile 既存値を流用、(b) AnimationClip の path を `AnimationUtility.GetCurveBindings`（Editor only）で事前検証し runtime で path 自動補正する、(c) sample 失敗時に `BlendShapePhonemeEntry` 風の暗黙 fallback を提供。Backlog の S-9 に対応する spec。

## Introduction

本 spec は、uLipSync アダプタ（`com.hidano.facialcontrol.lipsync`）で AnimationClip 形式の音素エントリ（`AnimationClipPhonemeEntry`）がユーザー視点で「動かない」問題を根本解決するための要件を定義する。現状 `BlendShapePhonemeEntry`（BlendShape 名直指定）は動作するが、Inspector でデフォルト追加される `AnimationClipPhonemeEntry` は `Clip.SampleAnimation` で採取した結果が空になりがちで、ユーザーが何も操作していないように見える。

本要件はユーザーが「Inspector で AIUEO 音素ごとに口形状を指定したら、リップシンク中にその口形状が確実に反映される」状態を達成することをゴールとする。後方互換のため既存の `BlendShapePhonemeEntry` / `AnimationClipPhonemeEntry` の挙動は保ち、新方式（`ExpressionPhonemeEntry` 等）の追加と fallback 戦略によって解決する。

## Boundary Context

- **In scope**:
  - 新規音素エントリ型 `ExpressionPhonemeEntry`（`FacialProfile` の `Expression` を音素として参照する型）の追加
  - `AnimationClipPhonemeEntry` の sample 失敗時の挙動定義（fallback 有無、警告レベル）
  - `ULipSyncAdapterBinding.BuildSnapshots` における snapshot 構築経路の再設計
  - Inspector（`PhonemeEntryListView`）への新エントリ型の追加と、選択肢の優先順位提示
  - `ApplyInitialDefaults` でデフォルト追加する音素エントリ型の見直し
  - 既存 `BlendShapePhonemeEntry` / `AnimationClipPhonemeEntry` を破壊しない後方互換維持
- **Out of scope**:
  - uLipSync 本体（`uLipSync.uLipSync` / `uLipSync.Profile`）の改修
  - 音声解析アルゴリズム自体の改善
  - Bones / テクスチャ切り替え / UV アニメ系の音素表現（BlendShape スナップショットのみを対象）
  - リップシンク以外のアダプタ（OSC / InputSystem 等）の `PhonemeEntryBase` 派生
  - リップシンク用 AnimationClip の Editor 上での作成支援ツール（本 spec はあくまで「動かない」根本解決）
- **Adjacent expectations**:
  - `FacialProfile` の `Expression` / `ExpressionSnapshot` は既に BlendShape 名 + 値の配列を保持済みであり、これを参照すれば AnimationClip 採取は不要
  - `OverlayInputSource` は既に `ExpressionSnapshot.BlendShapes` から `name → index` 解決と `BitArray` マスク構築を行っており、同様の経路を踏襲可能
  - `ULipSyncProvider` / `LipSyncInputSource` は `PhonemeSnapshot[]`（`PhonemeId` + BlendShape 配列）を期待しており、入力経路を切り替えても下流は変更不要

## Requirements

### Requirement 1: 採用方針の確定（候補 (a) / (b) / (c) の選択）
**Objective:** As a パッケージ開発者, I want 3 候補対応のうちどれを根本対応として実装するかを要件レベルで確定する, so that 設計フェーズで実装方針が一意に決まり手戻りが発生しない

#### Acceptance Criteria
1. The lipsync-animationclip-rework spec shall 採用方針として候補 (a) `ExpressionPhonemeEntry` 新設（profile 既存 Expression を音素として参照）を第一手段として採用すると明記する。
2. The lipsync-animationclip-rework spec shall 候補 (c)「`AnimationClipPhonemeEntry` の sample 失敗時に暗黙 fallback を提供」を第二手段（補助）として採用すると明記する。
3. The lipsync-animationclip-rework spec shall 候補 (b)「`AnimationUtility.GetCurveBindings` による Editor 事前検証 + runtime path 補正」をスコープ外（将来課題）として明記する。
4. If 設計フェーズ以降で候補 (a) / (c) 以外の代替案を採用する必要が生じた場合, then the lipsync-animationclip-rework spec shall 要件 1.1〜1.3 を更新し再承認フローを経る。

### Requirement 2: `ExpressionPhonemeEntry` の新設
**Objective:** As a ライブラリ利用者（Unity エンジニア）, I want FacialProfile に既に登録した「あ」「い」「う」「え」「お」表情を音素エントリとして直接指定できる, so that AnimationClip の rendererPath / BlendShape カーブ構造に依存せず、確実にリップシンクが動く

#### Acceptance Criteria
1. The `com.hidano.facialcontrol.lipsync` package shall `PhonemeEntryBase` を継承する `ExpressionPhonemeEntry` 型を Runtime/Adapters/PhonemeEntries 配下に追加する。
2. The `ExpressionPhonemeEntry` shall `PhonemeId`（基底）と `MaxWeight`（基底）に加えて、参照先 `Expression` を特定するための識別子（`ExpressionId` または `SnapshotId`、いずれかを採用）を `[SerializeField]` で保持する。
3. When `ULipSyncAdapterBinding.BuildSnapshots` が `ExpressionPhonemeEntry` を処理する, the binding shall `AdapterBuildContext` から到達可能な `FacialProfile` を参照し、該当 Expression の `ExpressionSnapshot.BlendShapes` を `PhonemeSnapshot` に変換する。
4. When `ExpressionPhonemeEntry` から snapshot を構築する, the binding shall `AnimationClip.SampleAnimation` を一切呼ばない（rendererPath 不一致や BlendShape カーブ欠如の影響を受けない）。
5. If 参照先 `Expression` が profile 内に存在しない、または `ExpressionSnapshot.BlendShapes` が空である, then the binding shall `Debug.LogWarning` で警告を出力し、その音素エントリをスキップする（他の音素エントリの処理は継続する）。
6. The `ExpressionPhonemeEntry` shall `MaxWeight`（0〜100）をスケール係数として参照 Expression の BlendShape 値に適用し、結果を `Mathf.Clamp01` でクランプして `PhonemeSnapshot.Weights` に格納する。

### Requirement 3: Inspector への `ExpressionPhonemeEntry` 統合
**Objective:** As a ライブラリ利用者, I want Inspector の音素エントリリストで `ExpressionPhonemeEntry` を選択・編集できる, so that GUI 操作だけで profile 既存表情を音素として指定できる

#### Acceptance Criteria
1. The `PhonemeEntryListView` shall 「BlendShape 形式」「AnimationClip 形式」に加えて「Expression 形式」を `EntryKind` enum と DropdownField の選択肢に追加する。
2. When ユーザーが Add メニューから「Expression 形式」を選択する, the `PhonemeEntryListView` shall `ExpressionPhonemeEntry` インスタンスを生成し SerializedProperty に `managedReferenceValue` として設定する。
3. When ユーザーが既存エントリの形式を「Expression 形式」へ切り替える, the `PhonemeEntryListView` shall `PhonemeId` / `MaxWeight` を保持したまま型のみを `ExpressionPhonemeEntry` に置き換える。
4. The Expression 形式行 shall 参照対象 Expression を選択するための UI（ドロップダウンまたは ObjectField）を表示する。
5. If 参照対象 Expression が未選択の状態である, then the Expression 形式行 shall `HelpBox` で「Expression 未割り当てです」相当の警告を表示する。
6. When 参照対象 Expression が選択されている, the Expression 形式行 shall 警告 `HelpBox` を `DisplayStyle.None` で非表示にする。

### Requirement 4: デフォルト追加エントリ型の見直し
**Objective:** As a ライブラリ利用者, I want Inspector で uLipSync binding を新規追加した直後のデフォルト音素エントリが「動く形式」である, so that ユーザーが何も追加設定しなくてもリップシンクが機能する状態に近づける

#### Acceptance Criteria
1. The `ULipSyncAdapterBinding.ApplyInitialDefaults` shall AIUEO 5 音素分のデフォルトエントリを `ExpressionPhonemeEntry`（参照先未設定）で生成する。
2. When `ApplyInitialDefaults` が呼ばれた時点で profile 内に AIUEO 表情（Id または Name が "A" / "I" / "U" / "E" / "O" 等のリップシンク向け表情）が既に存在する, the binding shall それらを各エントリの参照先として自動設定する（heuristic match）。
3. If profile に AIUEO 表情が存在しない, then the binding shall 参照先未設定のまま `ExpressionPhonemeEntry` のみを生成し、ユーザーが Inspector で割り当てることを期待する。
4. The `ApplyInitialDefaults` shall 既存仕様（呼び出し時に `_phonemeEntries` が空でなければ何もしない）を維持する。

### Requirement 5: `AnimationClipPhonemeEntry` の挙動維持と sample 失敗時 fallback
**Objective:** As a ライブラリ利用者, I want 既に動作している `AnimationClipPhonemeEntry` ベースの設定を破壊されず, かつ sample 失敗時の体験が改善される, so that 既存ユーザーは何もしなくても従来通り動き、新規ユーザーは fallback で「全く動かない」状態を避けられる

#### Acceptance Criteria
1. The `ULipSyncAdapterBinding` shall `AnimationClipPhonemeEntry` を引き続き受け付け、`Clip.SampleAnimation` 経由で snapshot を構築する既存経路を保持する。
2. When `TryFillAnimationClipSnapshot` の sample 結果が全 BlendShape で 0 (`anyNonZero == false`) である, the binding shall その音素エントリの `PhonemeId` と関連付け可能な Expression（同名 Expression / Id 一致）が profile に存在するか検査する。
3. If sample 結果が全 0 で、かつ `PhonemeId` 一致する Expression が profile 内に存在する, then the binding shall 該当 Expression の `ExpressionSnapshot.BlendShapes` から `PhonemeSnapshot` を再構築し fallback として採用する（fallback 採用時は `Debug.LogWarning` で fallback された旨と推奨される設定方法を案内する）。
4. If sample 結果が全 0 で、かつ `PhonemeId` 一致する Expression が profile 内に存在しない, then the binding shall 現行の `Debug.LogWarning`（rendererPath 不一致疑い）を維持し、その音素エントリをスキップする。
5. The `BlendShapePhonemeEntry` の処理経路 shall 本 spec の変更で影響を受けない（regression なし）。

### Requirement 6: 参照 Expression の解決経路
**Objective:** As a 内部実装, I want `ULipSyncAdapterBinding` が `FacialProfile` / `Expression` / `ExpressionSnapshot` へアクセスする経路を明確にする, so that 既存のクリーンアーキテクチャ（Adapters → Application → Domain の依存方向）と asmdef を破らない

#### Acceptance Criteria
1. The `ULipSyncAdapterBinding` shall `AdapterBuildContext`（または同等の build 時コンテキスト）経由で `FacialProfile` インスタンスを取得する。
2. If `AdapterBuildContext` が現状 `FacialProfile` を公開していない, then the lipsync-animationclip-rework design shall コンテキスト拡張または `IFacialProfileProvider` 相当の抽象を追加する設計を選択する（実装の詳細は design フェーズで確定）。
3. The `ExpressionPhonemeEntry` resolution shall `OverlayInputSource` で既に使用されている `ExpressionSnapshot.BlendShapes` → `nameToIndex` → `weights[]` の変換パターンに準拠する。
4. The lipsync runtime asmdef shall 本 spec の変更後も `Hidano.FacialControl.Domain` / `Hidano.FacialControl.Application` への外向き参照（依存方向反転）を追加しない。

### Requirement 7: パフォーマンス契約の維持
**Objective:** As a パッケージ品質基準, I want 本変更が `tech.md` の「毎フレームのヒープ確保ゼロを目標」契約を破らない, so that 同時 10 体以上のキャラクター制御や preview リリースの性能要件を維持できる

#### Acceptance Criteria
1. The `ULipSyncProvider.Update` 経路（毎フレーム実行されるホットパス） shall 本 spec 実装後も新規ヒープ確保を発生させない。
2. The snapshot 構築（`BuildSnapshots` / `ExpressionPhonemeEntry` 解決） shall `OnStart` 時の 1 回のみで完結し、毎フレーム発生させない。
3. The `ExpressionPhonemeEntry` resolution shall 内部の `Dictionary<string, int>` / `int[]` / `float[]` 等の一時バッファを `OnStart` 終了時点までに解放可能な範囲に閉じ込める（GC 圧の長期化を防ぐ）。
4. The lipsync-animationclip-rework spec shall 性能 regression を検出するための EditMode/PlayMode 性能テストの追加または既存テスト（`ULipSyncProviderAllocationTests` / `EndToEndGcAllocationTests`）への ExpressionPhonemeEntry ケース追加を要件とする。

### Requirement 8: 後方互換性
**Objective:** As a 既存ユーザー, I want 本 spec 実装後も自分の既存 FacialProfile / 既存 binding 設定が壊れない, so that upgrade で手動マイグレーションを強制されない

#### Acceptance Criteria
1. The 既存の `BlendShapePhonemeEntry` を含む binding shall 本 spec 実装後も従来と同一の `PhonemeSnapshot` を生成する（API 互換 / 挙動互換）。
2. The 既存の `AnimationClipPhonemeEntry` を含む binding shall 本 spec 実装後も `Clip.SampleAnimation` 経由の snapshot を採取する経路を維持する（要件 5.1 と整合）。
3. The `PhonemeEntryBase` の public フィールド (`PhonemeId` / `MaxWeight`) shall 名前・型ともに維持される（rename / 型変更禁止）。
4. The `[SerializeReference]` で永続化された既存の `_phonemeEntries` データ shall 本 spec 実装後の binding でも `managedReferenceValue` 解決可能であり、loss なくロードできる。
5. Where preview 段階の破壊的変更が必要な箇所がある, the lipsync-animationclip-rework design shall 当該変更点を design.md の「Migration Notes」セクションで明示し、`docs/backlog.md` の S-9 にも反映する。

### Requirement 9: 警告・ログ運用
**Objective:** As a 利用者がトラブルシュートする立場, I want リップシンクが動かない原因がログから一意に追跡できる, so that 「動かない」体験を最短で原因特定できる

#### Acceptance Criteria
1. The `ULipSyncAdapterBinding` shall 警告ログにフォーマット接頭辞 `[ULipSyncAdapterBinding]` を維持する（既存ログ運用との整合）。
2. When `ExpressionPhonemeEntry` の参照先が未解決である, the binding shall ログに「対象 PhonemeId」「参照 ExpressionId（または未割り当ての旨）」「推奨される対処（Inspector で割り当て）」を含める。
3. When `AnimationClipPhonemeEntry` sample が全 0 となり要件 5.3 の fallback が発動した, the binding shall ログに「fallback 採用済み」と「より確実な代替として ExpressionPhonemeEntry の使用」を案内する文言を含める。
4. The 警告ログ出力 shall `Debug.LogWarning`（エラーではない）を使用し、ユーザーがログ抑制設定をしていてもデフォルトで気付ける重大度に保つ。
5. The 警告ログ出力 shall 同一 binding / 同一原因に対して `OnStart` ごとに 1 回以上発生しないよう、必要に応じて one-shot 抑制を導入する（毎フレーム再発を回避する）。

### Requirement 10: テスト要件
**Objective:** As a TDD ベースの開発, I want 新機能と回帰防止のテストが網羅される, so that Red-Green-Refactor サイクルで品質を保ち、CI（GitHub Actions + Windows セルフホストランナー）で自動検証できる

#### Acceptance Criteria
1. The lipsync-animationclip-rework tasks shall `ExpressionPhonemeEntry` の構築・シリアライズ・`PhonemeSnapshot` 変換を検証する EditMode テスト（`PhonemeEntryTests` または専用クラス）の追加を含む。
2. The lipsync-animationclip-rework tasks shall `ULipSyncAdapterBinding.BuildSnapshots` が `ExpressionPhonemeEntry` を正しく snapshot 化することを検証する EditMode テスト（モック profile を使用）の追加を含む。
3. The lipsync-animationclip-rework tasks shall `AnimationClipPhonemeEntry` の sample 失敗 → 要件 5.3 fallback 経路の動作を検証する EditMode テスト（同名 Expression の有無 2 パターン）の追加を含む。
4. The lipsync-animationclip-rework tasks shall `PhonemeEntryListView` の Expression 形式選択時 UI が `ExpressionPhonemeEntry` を生成・編集することを検証する EditMode テスト（`PhonemeEntryListViewTests` への追加）を含む。
5. The lipsync-animationclip-rework tasks shall 既存テスト（`PhonemeEntryTests` / `PhonemeEntryListViewTests` / `ULipSyncProviderAllocationTests` 等）が本 spec 実装後も全て pass することを CI で確認する。
6. While 性能テストを実行している, the `ULipSyncProviderAllocationTests` shall `ExpressionPhonemeEntry` ベースの snapshot を含むケースで毎フレーム 0 byte の GC 確保を維持することを検証する。

### Requirement 11: ドキュメント更新
**Objective:** As a 新規ユーザー, I want LipSync の AnimationClip 形式 vs Expression 形式 vs BlendShape 形式の使い分けが README / Documentation~ から明確にわかる, so that どの形式を選べばよいか迷わない

#### Acceptance Criteria
1. The `com.hidano.facialcontrol.lipsync/Documentation~` shall 3 種類の `PhonemeEntryBase` 派生（BlendShape / AnimationClip / Expression）の使い分けガイドを追加する。
2. The Documentation~ ガイド shall 「Expression 形式が最も確実に動く」「AnimationClip 形式は rendererPath 整合が必要」「BlendShape 形式は単一 BlendShape 名で簡潔だが組み合わせ口形状には不向き」の 3 観点を含む比較を提供する。
3. The Documentation~ ガイド shall `Multi Source Blend Demo` サンプル等の既存サンプルが本 spec 実装後にどの形式を採用するか（migration 方針）を明記する。
4. The `docs/backlog.md` の S-9 項目 shall 本 spec 完了時にステータス更新（完了 or 別 PR ネタへ昇格）される。
