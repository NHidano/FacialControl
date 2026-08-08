# Research & Design Decisions — gaze-channel-redesign

## Summary
- **Feature**: `gaze-channel-redesign`
- **Discovery Scope**: Extension（既存システムの大型リファクタ + 破壊的スキーマ変更。外部依存の新規調査は不要のため integration-focused discovery を実施）
- **Key Findings**:
  - 現行の gaze identity（isGaze Expression + `_gazeConfigs` + binding 側設定 + registry id）の全接点をコードで実測確認し、gap analysis の置換インベントリと一致することを検証した
  - 規約 id `"gaze"` の既定チャネル化により、iFacialMocap の既存ハードコード `gaze.left` / `gaze.right` は**新規約の合成結果と文字列レベルで一致**する（`ComposeSub("gaze", Left) == "gaze.left"`）。iFM は挙動無変更で新規約に吸収できる
  - JSON schemaVersion は `SystemTextJsonParser.SchemaVersionV2 == "1.0"` の strict 一致チェックで運用されており、バージョンを上げると旧ファイルが**全体拒否**され D-3（gaze 部分のみ読み捨て・他は通常読み込み）と矛盾する。よって "1.0" を維持しキー形状で新旧を判別する
  - リフレクション注入（`FindGazeConfigureMethod`）は「末尾引数型で Configure を探し、残りの引数を binding 自身のフィールドから読み戻して渡す」二重に脆い構造であり、データ型が `GazeBindingConfig` → `GazeChannel` に変わる本 spec が型付き化の唯一の好機である

## Research Log

### 現行 identity モデルの接点インベントリ（コード実測）
- **Context**: gap analysis の置換インベントリを design の前提として検証する
- **Sources Consulted**: コードベース直接読解（2026-08-09 HEAD）
- **Findings**:
  - `ExpressionSerializable.isGaze`（L36）: 廃止対象はこの 1 フィールドのみ。JSON v2 の `ExpressionDto` に isGaze は存在しない（JSON 境界を越えない）
  - `FacialCharacterProfileSO._gazeConfigs`（L23）+ `IFacialCharacterProfile.GazeConfigs`: SO ルートリスト。`FacialController` は L266-278 で取得し、L311-410 のリフレクションで binding へ注入、L504-563 で `GazeSnapshot` 生成、L691-751 で `SetupGazeBoneProvider`、L852-891 で Subscribe
  - リフレクション注入の実態: `FindGazeConfigureMethod` は「Configure という名前 + 末尾引数が `IReadOnlyList<GazeBindingConfig>`」で探し、**先頭側の引数は binding 自身の property / field 名から読み戻して渡す**（`TryReadConfigureArgument` L412-468 の `asset` / `actionMapName` / `expressionBindings` 特例 + PascalCase 変換）。シグネチャ・フィールド名のどちらの変更でも無警告で注入が消える
  - `.left` / `.right` リテラル 4 箇所を確認: `GazeBindingConfigResolver.cs:33-34`（定数）/ `IFacialMocapReceiverAdapterBinding.cs:61-62`（`GazeLeftSub = "gaze.left"` 等）/ `InputSystemAdapterBinding.cs:393,398`（`$"{entry.expressionId}.left"` 補間）/ `OscReceiverAdapterBinding` の gaze 登録部（連結）
  - `InputSystemAdapterBinding` の gaze 登録は「実体 `{slug}:{actionName}` を Register + 規約 id `{slug}:{expressionId}[.left/.right]` をエイリアス Register」の二重登録（`BuildAnalogSources` L372-420）。`_injectedGazeConfigs` の消費は `HasInjectedGazeConfig(expressionId)`（存在チェック）のみで bone データは使わない
  - `OscSenderAdapterBinding`: serialized `_gazeExpressionIds`（L39）+ 公開 API `GazeExpressionIds` / `ConfigureGazeExpressionIds` + options JSON `gazeExpressionIds`。空の場合 `ResolveGazeExpressionIds`（L668-723）が host GameObject → `FacialController.CharacterSO.GazeConfigs` を**直読み**する
  - `RecToTimelineExporter.CollectGazeSourceIds`（L512-531）: GazeConfigs の `sourceIdLeft` / `sourceIdRight` **明示値のみ**を Ordinal 収集。リテラルは無いが、規約合成 id（distinct 未使用構成）は分類できない穴がある
  - `TimelineValueChannelConfig`（TimelineAdapterBinding.cs L192-223）: `sub` / `axisCount` / `isGaze` / `takeoverSourceId`。isGaze は Timeline 独自のチャネル種別フラグ（Req 2.1 の別概念）
  - Inspector: `_rootGazeConfigsProperty` 参照は追加導線 3 系統（隠し dropdown L1189-1212 / 一括 L1426-1470 / 表情行ボタン L2660-2696）、`GazeConfigDeletionTrigger` 3 トリガ（L1513 / L1991 / L2734）、isGaze Toggle 行 UI（L2777-2790）、候補列挙（L1392-1410）に分布
- **Implications**: 置換インベントリは gap analysis と一致。design の File Structure Plan / Components はこのインベントリを正とする

### Spec 1（osc-gaze-auto-mapping）との統合面
- **Context**: 本 spec の実装は Spec 1 実装完了後。Spec 1 design の Revalidation Triggers を引き継ぐ
- **Sources Consulted**: `.kiro/specs/osc-gaze-auto-mapping/design.md`
- **Findings**:
  - Spec 1 が新設するシンボル（未実装）: `GazeAdvertisementResolver` / `GazeBindingConfigResolver.ComposeSourceId` + `ComposeSourceSub` + `GazeSide` enum / `OscReceiverAdapterBinding.Configure(IReadOnlyList<GazeBindingConfig>)` / `GazeInputReader` / `FacialController.SubscribeGazeInputSources` の候補 id 全合成
  - Spec 1 の Revalidation Triggers: (1) gaze source id 合成規約の変更 → `GazeSourceIdConvention` 設計へ通知（本 spec が吸収）、(2) リフレクション契約変更 → receiver の Configure を置換対象リストに含める（本 spec Req 8.2 が対応）
  - 広告 payload は string flat pairs `[id, format, ...]`。id は文字列で運ぶため、規約定数 `"gaze"` 化はプロトコル無変更で成立（S1-3 どおり）
  - Spec 1 の候補 id 全合成 Subscribe は「slug 一覧 × GazeConfigs × 3 形」。本 spec ではデータ源が「slug 一覧 × Gaze チャネル × 3 形」に置き換わるだけで機構は流用できる
- **Implications**: `ComposeSourceId` / `ComposeSourceSub` / `GazeSide` は `GazeSourceIdConvention`（Domain）へ統合し、Adapters 側の合成 helper は削除する。`GazeAdvertisementResolver` は id 合成箇所のみ改修（Req 11.4 の改修限定）

### JSON スキーマの前例（schemaVersion / キー命名 / preprocessing）
- **Context**: Open Question 3（Gaze セクションの JSON スキーマ）の確定材料
- **Sources Consulted**: `SystemTextJsonParser.cs` / `ProfileSnapshotDto.cs` / `GazeBindingConfigDto.cs` / `Documentation~/migration-guide.md`（gaze-config-promotion の記述）
- **Findings**:
  - `SchemaVersionV2 = "1.0"`。パーサは `dto.schemaVersion != SchemaVersionV2` で**例外拒否**する strict 実装（L85-92）
  - v2 JSON のトップレベルキーは `schemaVersion` / `layers` / `slots` / `expressions` / `baseExpression` / `rendererPaths` / `defaultOverlays` と **camelCase が基本**。`gaze_configs` だけが snake_case で、`PreprocessGazeConfigsKey` / `PostprocessGazeConfigsKey`（L425-437）の文字列置換で JsonUtility フィールド名 `gazeConfigs` と橋渡ししている（例外的な追加実装）
  - gaze-config-promotion（implemented）は SO ルート昇格時に `_schemaVersion: "1.0"` 統一の前例を作った（migration-guide L256, L273）
  - `JsonUtility` は未知キーを無警告で無視する（旧 `gaze_configs` キーは DTO からフィールドを消せば自然に読み捨てられる）
- **Implications**: schemaVersion は "1.0" 維持 + 新ルートキーは camelCase（`gaze`）で preprocessing 不要にする。旧キー検出は raw JSON の `"gaze_configs"` 文字列走査で行う（Design Decision 3）

### id 規約の文字集合と衝突リスク
- **Context**: D-1 の追加チャネル id validation（InputSourceId 規約）と `TryParse` の曖昧性
- **Sources Consulted**: `Runtime/Domain/Models/InputSourceId.cs` / `AdapterSlug.cs`
- **Findings**:
  - `InputSourceId` は `[a-zA-Z0-9_.\-:]{1,64}`。`:` は registry の `slug:sub` 合成キー区切りで、slug 自体は `:` を含めない
  - 文字集合に `.` が含まれるため、ユーザー命名チャネル id が `.left` / `.right` で終わる場合（例: `my.left`）に side suffix と曖昧になる
  - `IAdapterBindingDeclaredInputs`（Domain/Adapters）が「binding の静的宣言 interface を Domain に置き、Editor（`SourcePortEnumerator`）が列挙する」配置前例を確立している
- **Implications**: チャネル id validation に「`.left` / `.right` 終端禁止」「`:` 禁止」を加える。`IGazeSourceProvider` / `IGazeChannelConsumer` は `IAdapterBindingDeclaredInputs` と同じ Domain/Adapters 配置とする

### ボーン path の起点とフォールバック挙動
- **Context**: Req 7.3（フルパス化と起点定義・不一致時の扱い）
- **Sources Consulted**: `BoneTransformResolver.cs` / `FacialController.SetupGazeBoneProvider`（L748-750）/ Inspector `AutoAssignGazeBonesFromReferenceModel`（L2975-3021）
- **Findings**:
  - ランタイム解決 root は `new BoneTransformResolver(_animator.transform)` — **Animator の Transform** が起点
  - resolver は `/` 入り文字列を `root.Find(path)` の相対 path として解決済み（改修不要は事実）。ただし path 解決失敗時は警告 + null で、**単純名フォールバックは無い**
  - 現行 auto-assign は `leftEye.name`（単純名）のみ保存（L2992 / L3004）。Humanoid の `HumanBodyBones.LeftEye/RightEye` → 名前ヒューリスティックの 2 段解決
- **Implications**: 保存 path の起点は「参照モデル内の Animator の Transform」と定義し、ランタイム起点（`_animator.transform`）と一致させる。不一致構成（path 解決失敗）は resolver に「末尾セグメント単純名フォールバック + 警告」を追加して従来同等の挙動へ縮退させる（Design Decision 6）

### look*Clip / look*Samples の消費者確認（D-2 の裏取り方針）
- **Context**: D-2（新スキーマから完全削除）に伴う `GazeClipBlendShapeSampler` / `GazeBlendShapeSampleEntry` の削除可否
- **Findings**: `GazeBindingConfig` のコメントは「runtime の BuildAnalogProfile は本キャッシュから AnalogBindingEntry を構築する」と記すが、multi-spec プラン / dig（D-2）は「ランタイム消費者ゼロ（M-29 未配線）」と結論している。`InputSystemAdapterBinding._injectedGazeConfigs` の実消費は存在チェックのみで samples を読まないことをコードで確認
- **Implications**: 削除を既定とするが、実装タスク冒頭に「`lookLeftSamples` 等 8 フィールドと `GazeClipBlendShapeSampler` の参照ゼロを grep で確認してから削除する」検証ステップを置く

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| A: 規約 id 定数 + チャネルリスト（採用） | Gaze セクション = `GazeChannel` リスト 1 本、先頭は id 固定 `"gaze"`。id 合成/パースは Domain の `GazeSourceIdConvention` に一元化 | 突合作業が構造的に消える。iFM 既存ハードコードと文字列一致。シリアライズ/JSON/Inspector が単純 | 既定チャネル不変条件の自己修復実装が必要 | D-1 / G-1 で確定済み |
| B: gaze を Expression の一種として Domain 抽象化（M-14 案 a） | isGaze を temp 温存し Domain に Gaze Expression 型を導入 | レイヤー機構と統一 | 二重管理が温存され複雑さの根本が残る | プランで放棄済み（ユーザー確認済み） |
| C: データモデル維持 + Inspector 統合のみ（案 Y） | UI だけ 1 タブへ集約 | 変更面積最小 | 「UI で隠したが構造は複雑なまま」。JSON 直編集で壊れる経路が残存 | プランで棄却済み |

## Design Decisions

### Decision 1: リフレクション Configure 注入 → 型付きインターフェース `IGazeChannelConsumer` へ置換（Open Question 1）
- **Context**: `FindGazeConfigureMethod` は「コメント上の契約」で、シグネチャ変更・フィールド名変更のどちらでも無警告で注入が消える（Req 8 の動機）。本 spec で注入データ型自体が `GazeBindingConfig` → チャネル定義に変わるため、全呼び出し点がどのみち変更される
- **Alternatives Considered**:
  1. 現状維持（末尾引数型を `IReadOnlyList<GazeChannel>` に変えてリフレクション探索を温存）— 変更面積は最小だが、無警告注入漏れのクラスが温存され spec の目的（検証可能な契約）に反する
  2. core Adapters 層に `IReadOnlyList<GazeChannel>` を受ける interface — binding が bone データまで受け取れるが、実消費調査の結果、全 binding が必要とするのは**チャネル id 列のみ**（receiver: 突合警告 / InputSystem: 存在チェック / sender: 広告 id）で過剰。Adapters 配置になり Domain 純度も下がる
  3. **（採用）Domain/Adapters に `IGazeChannelConsumer { void ConfigureGazeChannels(IReadOnlyList<string> channelIds); }`** — `is` キャストによる型付き注入。Unity 非依存で `IAdapterBindingDeclaredInputs` と同格の配置
- **Selected Approach**: 3。`FacialController` は `binding is IGazeChannelConsumer` で判定して直接呼ぶ。実装は `OscReceiverAdapterBinding`（Spec 1 の Configure を置換）/ `InputSystemAdapterBinding`（Configure 末尾引数を削除し interface 実装へ）/ `OscSenderAdapterBinding`（`CharacterSO.GazeConfigs` 直読みフォールバックを注入へ置換 — Req 11.6(b) の解も兼ねる）
- **Rationale**: コンパイル時検証・asmdef 依存方向（拡張 → core Domain）維持・注入内容の最小化が同時に成立する。リフレクションの引数読み戻し機構（`TryReadConfigureArgument`）ごと削除できる
- **Trade-offs**: bone データが必要になる将来の binding は SO 参照など別経路が必要（現時点で該当なし）。旧契約の Configure を持つサードパーティ binding は呼ばれなくなるが、`GazeBindingConfig` 型自体を削除するため旧シグネチャはコンパイル不能となり、無警告沈黙は構造的に発生しない
- **Follow-up**: `TryReadConfigureArgument` / `ToPascalCase` / `FindGazeConfigureMethod` の削除で他機能（gaze 以外の Configure 注入）が巻き込まれないことを確認（現実装は gaze 注入専用であることを確認済み）

### Decision 2: eye レイヤーと gaze の概念整理 — 「レイヤー外の別経路」を正式仕様として文書化（Open Question 2）
- **Context**: docs/requirements.md L169 は「目（eye）レイヤー = まばたき・目線操作」と記すが、実アーキテクチャでは gaze は `GazeBonePoseProvider` の Transform 直駆動でレイヤー合成（LayerUseCase / Aggregator）に参加しない
- **Alternatives Considered**:
  1. gaze をレイヤー合成へ統合する — multi-source blending（M-13）と BlendShape gaze（M-29）の再設計が前提で、本 spec のスコープ外
  2. **（採用）現行構造を正式仕様として文書化** — 「目レイヤー = まばたき等の BlendShape 表情（レイヤー合成）」「Gaze = ボーン駆動の独立チャネル（レイヤー外、Gaze セクションで構成）」と用語を分離し、docs/requirements.md L169 / mental-model / technical-spec §12/§17 を更新
- **Selected Approach**: 2。Req 1.4 の「現行アーキテクチャの維持」と整合
- **Rationale**: 統合は別 spec の大型案件。ドキュメントの用語分離だけで「目レイヤーに gaze を設定しようとして迷子になる」混乱は解消できる
- **Trade-offs**: BlendShape gaze（M-29 将来 spec）実現時には「Gaze チャネル → 目レイヤーへの寄与」という追加の概念接続が必要になる（その時点で再文書化）
- **Follow-up**: M-29 の将来 spec 起票時に本 decision を参照させる

### Decision 3: Gaze セクションの JSON スキーマ — ルートキー `gaze`（camelCase）+ schemaVersion "1.0" 維持（Open Question 3）
- **Context**: Req 2.3。gaze-config-promotion の "1.0" 統一前例と、`gaze_configs` の snake/camel preprocessing 前例がある
- **Alternatives Considered**:
  1. schemaVersion を "1.1" 等へ bump — パーサの strict 一致チェックにより旧 "1.0" ファイルが**全体拒否**され、D-3「gaze 以外は通常どおり読み込む」と矛盾。受理側の複数バージョン対応もスコープ外
  2. 新キーも snake_case（`gaze` セクション内 `left_eye_bone_path` 等）+ preprocessing 拡張 — `gaze_configs` の文字列置換 preprocessing は例外実装であり、拡張は置換誤爆リスクとメンテコストを増やす
  3. **（採用）ルートキー `"gaze"`（オブジェクト）+ 内部 `channels[]`、フィールドは JsonUtility フィールド名そのまま（camelCase）** — v2 スキーマの他キー（`schemaVersion` / `rendererPaths` / `defaultOverlays`）と同じ流儀。preprocessing 不要
- **Selected Approach**: 3。旧スキーマ検出は raw JSON の `"gaze_configs"` キー文字列走査（AC 2.7 の指定どおり root キーのみ）で行い、警告 + 読み捨て（DTO からフィールド削除により JsonUtility が自然に無視）。`PreprocessGazeConfigsKey` / `PostprocessGazeConfigsKey` は削除
- **Rationale**: preview 破壊方針（S2-4）の下で「同一バージョン内のキー形状変更 + キー存在による新旧判別」が最も安全（旧ファイルの部分読み込みが成立）かつ実装最小
- **Trade-offs**: schemaVersion からは新旧を判別できない（キー存在判定に依存）。将来 correctness が必要になったら次の破壊的変更でバージョン運用を再設計する
- **Follow-up**: Exporter 出力・Converter 読み戻しのラウンドトリップテスト（Req 2.5）で新キーの網羅を固定

### Decision 4: `OscSenderAdapterBinding` の `_gazeExpressionIds` 系 API は廃止（Req 11.6(a)）
- **Context**: serialized `_gazeExpressionIds` + `GazeExpressionIds` / `ConfigureGazeExpressionIds` + options JSON `gazeExpressionIds` は「送信する gaze id の明示リスト」。新モデルでは id はチャネル定義そのもの
- **Alternatives Considered**:
  1. チャネル id フィルタ（`gazeChannelIds`）へ読み替えて温存 — 「手動 id リスト管理」という本 spec が消したい設定クラスを別名で残すことになる。既定チャネルのみの典型構成でフィルタの用途がない
  2. **（採用）廃止** — 広告 id / GazeSnapshot 送出の供給源を `IGazeChannelConsumer` で注入されたチャネル id 列に一本化
- **Selected Approach**: 2。serialized フィールド・公開 API・options JSON キーを削除し、`OscOutputDemo/OscSenderOptions.json` から `gazeExpressionIds` を除去。CHANGELOG に Breaking として記録
- **Rationale**: 設定項目の総量削減（spec の中心目的）。フィルタが将来必要なら additive に再導入できる
- **Trade-offs**: 「Profile の gaze 構成と独立に送信 id を差し替える」運用は不可能になる（新モデルでは Profile のチャネルが唯一の真実源であるべき、という設計判断に含める）
- **Follow-up**: options JSON パーサが未知キーを無警告で無視するか確認し、旧キー検出の警告を出すか実装時に判断（最低限 README / 移行ガイドに記載）

### Decision 5: SO 経路の旧スキーマ検出 — 検出専用 legacy フィールドを採用（AC 2.7 の design 確定事項）
- **Context**: Unity デシリアライズは旧 `_gazeConfigs` YAML を無警告で捨てるため、D-3 の「明示的な警告」が SO 経路で実現できない
- **Alternatives Considered**:
  1. 検出しない（JSON 経路のみ警告）— SO 資産（Fork 実機・サンプル）こそ主要な移行対象であり、無警告沈黙の禁止（D-3）に反する
  2. `GazeBindingConfig` 型を温存して検出に使う — D-2/D-9 で削除する型が検出のためだけに生き残り、削除の目的（死んだスキーマの一掃）と矛盾
  3. **（採用）検出専用マーカー型 `LegacyGazeConfigEntry { public string expressionId; }` の `[SerializeField, HideInInspector] List<> _gazeConfigs` を非公開温存** — YAML の旧キーが件数と expressionId のみデシリアライズされる
- **Selected Approach**: 3。検出時の警告は (a) Inspector 目線タブの HelpBox + 1 回の `Debug.LogWarning`（移行ガイド誘導 + 「クリア」ボタンで stale 行を除去）、(b) `FacialController` rebuild 時の 1 回警告（Editor を介さないランタイム構成向け）の 2 点
- **Rationale**: AC 2.7 が明示的に許容した方式。件数 + id が取れるため警告文言に具体性を持たせられる
- **Trade-offs**: SO に非公開フィールドが 1 本残る（AC 2.2 違反ではないと requirements が明記済み）。次の破壊的変更（1.0.0 前）で削除する
- **Follow-up**: サンプル Profile 再保存（Req 12.5）で stale キーが実際に消えることをアセット diff で確認

### Decision 6: ボーン path の起点 =「参照モデル内 Animator の Transform」・path 解決失敗時は単純名フォールバック（Req 7.3）
- **Context**: ランタイム解決 root は `_animator.transform`。参照モデル root と Animator の位置がずれる構成では起点不一致が起きうる
- **Alternatives Considered**:
  1. 参照モデル root 起点 — Editor 側は簡単だが、ランタイム root（Animator）と恒常的にずれる構成（root 直下に Animator が無いプレハブ）で全滅する
  2. **（採用）Animator 起点** — auto-assign 時に参照モデルから `GetComponentInChildren<Animator>` を探し、その Transform からの相対 path を保存。Animator が見つからない場合のみ参照モデル root 起点 + 注意ログ
- **Selected Approach**: 2 + `BoneTransformResolver` に「相対 path 解決失敗時、末尾セグメントの単純名解決へフォールバック + 警告 1 回」を追加
- **Rationale**: 起点をランタイムと定義一致させるのが正道。フォールバックにより、起点不一致・階層改変済みモデルでも現行（単純名解決）と同等以上の挙動に縮退し、退行しない
- **Trade-offs**: フォールバック時は同名ボーン衝突リスクが復活する（既存の複数ヒット警告がそのまま機能する）
- **Follow-up**: フォールバック発火の警告文言に「path 再解決（目線タブの再解決ボタン）」への誘導を含める

### Decision 7: `GazeSnapshot.ExpressionId` を `ChannelId` へリネーム
- **Context**: Domain 型 `GazeSnapshot` の id フィールドは今後チャネル id を運ぶ。名前を温存すると「expressionId」という廃止概念が公開 API に残る
- **Alternatives Considered**: 名前温存 + doc 更新（変更面積ゼロだが概念の残骸が恒久化） / **（採用）リネーム**（preview 破壊。コンパイルエラーで全使用箇所が表面化する）
- **Selected Approach**: リネーム。影響は core（FacialController / IFacialOutputBus 消費者）・osc sender・rec 系で、いずれもリポジトリ内。CHANGELOG に Breaking 記録
- **Follow-up**: .fcrec の永続化に文字列 id が入る場合、記録済みデータの id が旧 expressionId のままになる点を移行ガイドの既知事項に記載

### Decision 8: 死んだ BlendShape gaze 資産の削除（D-2 の実装確定）
- **Context**: `look*Clip` ×4 / `look*Samples` ×4 / `GazeBlendShapeSampleEntry` / Editor の `GazeClipBlendShapeSampler` はランタイム消費者ゼロ（M-29 未配線）
- **Selected Approach**: 型ごと削除（`GazeChannel` 新スキーマに含めない + sampler / entry / 関連テスト削除）。実装タスク冒頭で参照ゼロを grep 検証してから削除する
- **Rationale**: D-2 で確定済み。「UI 非表示」では JSON/SO スキーマに死んだデータが残り一掃にならない
- **Trade-offs**: M-29 実現時は新データモデルで再設計（dig で合意済み）

### Decision 9: 「自動」解決アルゴリズムは現行 3 段フォールバック + slug Ordinal を維持（D-4 の実装確定）
- **Context**: D-4 で `preferredSlug` 不採用が確定。優先度制御は provider 明示選択（channel の `providerSlug`）に一本化
- **Selected Approach**: `GazeBindingConfigResolver` の解決アルゴリズム（distinct → side-pair → shared、複数 slug 競合時 Ordinal 最小 + Warning）を `GazeChannelResolver` に引き継ぎ、入力を `GazeChannel` に置き換える。`providerSlug` 指定時は当該 slug の合成 id のみを探索
- **Rationale**: 実績あるアルゴリズムの温存で退行リスクを抑える。明示選択は「探索空間の slug 制限」という最小の追加で実現できる

## Risks & Mitigations
- **変更面積が 7 パッケージ + サンプル + ドキュメントに及ぶ** — File Structure Plan で削除/改修/新規を明示し、tasks 分割を Requirements 単位でなくレイヤー単位（core データモデル → 規約 → binding → Inspector → サンプル/docs）に構成する。Spec 1 完了が前提のため実装順序ゲートを tasks に明記
- **既定チャネル不変条件（存在・id 固定）の破れ** — SO アクセサでの自己修復 + Inspector validation + Converter 正規化の 3 点で担保し、EditMode テストで「空リスト読み込み」「id 改変 YAML」を固定
- **`SampleAssetsAreInSyncTests` 4 件の pre-existing 赤（M-28）との交差** — Req 12.6 どおり FAIL 判定から除外し、tasks に明記。blink snapshot 欠落分は gaze 無関係のため取り込まない
- **Spec 1 実装とのシンボル drift** — 本 design は Spec 1 の design 記載シンボル名を正とする。Spec 1 実装で名称が変わった場合は本 design の Revalidation Triggers に従い追従レビューする
- **isGaze ダミー Expression の残骸**（旧 SO で isGaze フィールドが黙って落ち、clip 無しの通常表情として残る）— 移行ガイドに削除手順を明記。Inspector の既存 clip 未設定警告が可視化の役割を果たす（追加実装なし）

## References
- `.kiro/multi-spec/gaze-control-overhaul.md` — プラン本文（G-1〜G-5 / S2-1〜S2-4 / 捨てた選択肢）
- `.kiro/specs/osc-gaze-auto-mapping/design.md` — Spec 1 design（広告プロトコル / Revalidation Triggers / ComposeSourceId）
- `.kiro/specs/gaze-channel-redesign/requirements.md` — D-1〜D-4 と 14 Requirements
- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Models/InputSourceId.cs` — id 文字集合規約
- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Adapters/IAdapterBindingDeclaredInputs.cs` — 宣言 interface の配置前例
- `FacialControl/Packages/com.hidano.facialcontrol/Documentation~/migration-guide.md` — gaze-config-promotion の "1.0" 前例と陳腐化箇所（L232-273）
