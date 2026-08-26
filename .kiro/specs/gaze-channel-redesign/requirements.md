# Requirements Document

## Project Description (Input)
gaze-channel-redesign: gaze の識別を規約 id "gaze" 1 本の既定チャネルに再設計し、isGaze ダミー Expression + GazeConfigs の二重管理を廃止して Profile 直下の「Gaze セクション」へ統合する。各 binding は gaze source を宣言インターフェース（IGazeSourceProvider 仮称）で公開し、ユーザーは Gaze セクションの入力ソース選択（ドロップダウン）だけで手法（コントローラ / iFacialMocap / OSC 受信など）を切り替えられる。GazeSourceIdConvention ヘルパーで id 合成・パースを一元化し（iFacialMocap の gaze.left/right ハードコード、InputSystem のエイリアス後付け、5 箇所に散った .left/.right リテラルを吸収）、目ボーン設定は参照モデル割当時に自動解決、ボーン path はフルパス化（backlog S-1 同時解消）する。詳細な背景・決定済み事項・スコープ・未決事項は .kiro/multi-spec/gaze-control-overhaul.md の「全体コンテクスト」「共通の決定済み事項」「Spec: gaze-channel-redesign」の各節に記載されており、requirements 生成・dig インタビュー・design の前提として必ず読み込むこと。関連 spec: .kiro/specs/osc-gaze-auto-mapping/（先行 Spec 1。広告プロトコル /_facialcontrol/gaze と受信側 gaze 動的登録経路を確立済み。本 spec では広告される id が規約定数 "gaze" になるだけでプロトコルは変更しない）も参照。

## Introduction

本 spec は、multi-spec プラン `gaze-control-overhaul` の Spec 2 として、gaze（目線）の identity モデルを刷新する。現状の gaze はユーザー任意の `expressionId` を 4 箇所以上（isGaze Expression / `GazeBindingConfig` / binding 側設定 / registry 登録 id、OSC 構成ではさらに送受信マシン間）で Ordinal 完全一致させる手動突合が前提であり、「開発者が LLM に聞きながらでないと設定できない」複雑さの最大の源泉になっている。

本 spec は決定済み事項 G-1〜G-3 / S2-1〜S2-4 に基づき、(1) gaze の既定 id を規約定数 `"gaze"` 1 本に固定し、(2) isGaze ダミー Expression + `_gazeConfigs` の二重管理を廃止して `FacialCharacterProfileSO` 直下の「Gaze セクション」へ統合し、(3) `GazeSourceIdConvention` ヘルパーで id 合成・パースを一元化し、(4) 各 binding が gaze source を宣言インターフェース（`IGazeSourceProvider` 仮名）で公開して入力ソースをドロップダウン 1 つで切り替えられるようにする。残る手動設定はモデル依存の目ボーン設定（参照モデルからの自動解決あり）のみとし、Spec 1（osc-gaze-auto-mapping）が D-1 案 (c) で先送りした「GazeConfig 突合も含めた受信側完全ゼロ設定」を本 spec の完了をもって成立させる。

preview 段階の破壊的変更として実施する（S2-4）。JSON スキーマ・SO・Inspector・全アダプタ（OSC / InputSystem / iFacialMocap / Timeline / rec）・サンプル 3 系統・ドキュメントに波及する。プランで未決とされた 7 件は本ドキュメント末尾の「Open Questions」に列挙し、後続の dig インタビュー / design phase で確定する。

## Boundary Context

- **In scope**:
  - **データモデル**: `FacialCharacterProfileSO` 直下の Gaze セクション（規約 id `"gaze"` の既定 1 件 + 上級者向け追加系統。目ボーン path・軸・可動角・入力ソース選択を保持）。`ExpressionSerializable.isGaze` と `_gazeConfigs` の廃止。JSON スキーマ / `FacialCharacterProfileConverter` / `FacialCharacterProfileExporter` / `SystemTextJsonParser` の追従
  - **id 規約の一元化**: `GazeSourceIdConvention` ヘルパー（`Compose` / `TryParse`）の core 新設。`.left` / `.right` リテラル散在 4 箇所（`GazeBindingConfigResolver.cs:33-34` / `IFacialMocapReceiverAdapterBinding.cs:61-62` / `InputSystemAdapterBinding.cs:393,398` / `OscReceiverAdapterBinding.cs:768-769`）の置換と、`RecToTimelineExporter` の gaze 分類ロジック（`sourceIdLeft/Right` 明示値の Ordinal 突合。リテラルは無い）の `TryParse` ベース化。Spec 1 が局所化した `GazeBindingConfigResolver.ComposeSourceId` の統合
  - **binding の gaze source 宣言**: `IGazeSourceProvider`（仮名）を OSC Receiver / InputSystem / iFacialMocap / Timeline の各 binding に実装。Gaze セクションの入力ソースドロップダウンが宣言を列挙。既定「自動」は従来の規約解決 + 明示優先度。iFM の `gaze.left/right` ハードコード吸収
  - **孤児ライフサイクルの撤去**: `GazeConfigDeletionTrigger` 3 トリガ、隠し dropdown デッドパス（`FacialCharacterProfileSOInspector.cs:1197-1202`）、isGaze Toggle 連動 validation の削除
  - **Inspector UX**: 目線タブ 1 か所完結（3 タブ往復の解消）。`useDistinctLeftRight` / `sourceIdLeft/Right` 相当の上級設定の露出。参照モデル割当時の目ボーン自動解決の自動実行
  - **ボーン path のフルパス化**: `AutoAssignGazeBonesFromReferenceModel` が `transform.name` のみ保存する問題の修正（backlog S-1 同時解消）
  - **リフレクション注入の解消（検討）**: `FacialController.FindGazeConfigureMethod` の型付きインターフェース化検討。置換対象は `InputSystemAdapterBinding` + Spec 1 で追加された `OscReceiverAdapterBinding.Configure`（Spec 1 design の Revalidation Triggers による引き継ぎ）
  - **死んだスキーマの整理**: `look*Clip` ×4 / `look*Samples` ×4 の UI 非表示または削除（M-29 を取り込まない場合）
  - **Timeline / rec の追従**: `TimelineValueChannelConfig.takeoverSourceId` / `RecToTimelineExporter` の gaze 判定を新規約へ
  - **サンプルアセット 3 系統**（MultiSourceBlendDemo / OscOutputDemo / OscReceiverDemo）の Profile 更新
  - **ドキュメント**: `Documentation~/migration-guide.md:232-270` の陳腐化解消（存在しない `InputSystemGazeBinding` / `_gazeInputBindings` への参照）、`docs/mental-model.md` / quickstart への gaze 手順追記、CHANGELOG への破壊的変更記録
- **Out of scope**:
  - BlendShape gaze の runtime 配線（backlog M-29）— ただし dig で取り込み判断の余地あり（Open Question 4）
  - multi-source gaze blending（backlog M-13）と合成戦略
  - カメラ目線 procedural ソースの実装（backlog M-5）— 本 spec は入力ソース切替の受け口（G-3）を用意するまで
  - Vector3 ターゲット指定 / VRM 対応
  - OSC 広告プロトコル（`/_facialcontrol/gaze`）の変更 — Spec 1 の成果をそのまま使用（S1-3）
- **Adjacent expectations**:
  - **Spec 1（osc-gaze-auto-mapping）から受け取るもの**: `/_facialcontrol/gaze` 広告プロトコル（アドレス・flat pairs payload・chunk 分割規約。無変更で使用し、広告 id が規約定数 `"gaze"` になるだけ）、受信側 gaze 動的登録経路（`GazeAdvertisementResolver` / rebuild 機構。id 合成箇所を `GazeSourceIdConvention` へ寄せるリファクタ対象）、`GazeBindingConfigResolver.ComposeSourceId`（統合先）、`OscReceiverAdapterBinding.Configure`（リフレクション注入の置換対象リスト入り）。本 spec の実装は Spec 1 の実装完了を前提とする
  - **Spec 1 の D-1（案 c）の完結**: Spec 1 は「受信側 gaze mapping 手入力ゼロで registry まで自動反映」までを受け入れ条件とし、GazeConfig 突合を含む完全ゼロ設定は本 spec の規約 id `"gaze"` 化で成立させると約束している
  - **実行順制約**: Spec 1 は spec 文書完成済みだが**実装は未着手**（2026-08-09 時点。`GazeAdvertisementResolver` / `ComposeSourceId` / `OscReceiverAdapterBinding.Configure(IReadOnlyList<GazeBindingConfig>)` / `GazeInputReader` はコードベースに未存在）。本 spec の**実装着手は Spec 1 の実装完了後**とし、requirements / design 中の Spec 1 由来シンボルへの言及は行番号でなくシンボル名を正とする（現 HEAD の行番号は Spec 1 実装でずれる）
  - `gaze-config-promotion` spec（implemented）が確立した「GazeConfig の SO ルート昇格 + opt-in UX + 孤児自動削除」は、本 spec のデータモデル統合により部分的に置き換えられる（破壊的変更）
  - クリーンアーキテクチャ / asmdef 依存方向（core は OSC / InputSystem / iFacialMocap / Timeline を知らない）を維持する。宣言インターフェース・id 規約は core 側の契約として定義し、各拡張パッケージが実装する
  - docs/technical-spec.md §12/§17 の gaze_follow / gaze_camera「Expression テンプレート」構想は S2-3 の再解釈（入力ソース切替）で置き換える

## Open Questions and Decisions (Dig)

2026-08-09 の dig インタビューで確定した決定。各 AC からは `(see D-x)` で参照する（Spec 1 の決定は「Spec 1 の D-x」と修飾して区別）。

| ID | 論点 | 決定 | 理由 | リスク |
|----|------|------|------|--------|
| D-1 | 複数系統のデータ構造と追加系統 id 命名（旧 Open Question 1） | **チャネルリスト 1 本・先頭の既定チャネル（id 固定 `"gaze"`）は常に存在し削除・id 編集不可。追加系統はユーザー命名の自由文字列 id（InputSourceId 規約の validation + 重複禁止）** | シリアライズ / JSON 表現が単純で Inspector もリスト UI 1 つ。規約連番（gaze.2 式）は OSC で相手端末と系統を対応付ける際に意味のある名前を付けられず不採用 | 低: 「既定は削除不可」を UI / validation で担保する実装が必要 |
| D-2 | look*Clip ×4 / look*Samples ×4 の扱い（旧 Open Question 4） | **新スキーマに含めない（事実上の削除）。UI・validation からも消す。M-29（BlendShape gaze 配線）は将来の別 spec で新データモデルごと再設計する** | ランタイム消費者ゼロのフィールドが設定を促す現状の解消が本 spec の目的の一部。温存は「死んだスキーマの一掃」と矛盾し、M-29 取り込みは大型 spec のさらなる肥大化でプランの分割方針と逆行 | 中: 既存の clip 設定資産（あれば）は移行で失われる。目ボーン非搭載モデルは引き続き gaze 不可（現状と同じ） |
| D-3 | 旧データの移行方針（旧 Open Question 2） | **手動再設定（preview 破壊）。旧スキーマ（isGaze / gaze_configs[]）の自動変換は実装せず、検出時に明示的な警告 + 移行ガイドへの誘導を行う。gaze 関連以外の Profile 内容は通常どおり読み込む（全体拒否はしない）** | preview 段階の破壊的変更許容方針（S2-4 / CLAUDE.md）。変換ロジックとテストの実装コストを本体に回す。Fork 実機・サンプルの Profile は数が限られ手動再設定が現実的 | 中: 既存 Profile 資産（Fork 実機含む）は gaze 設定の作り直しが必要。警告文言と移行ガイドの質が移行体験を左右する |
| D-4 | 「自動」選択時の優先度規則（旧 Open Question 3） | **現行の「slug Ordinal 辞書順で最小を採用 + Warning」を維持。`preferredSlug` は導入しない** | 新 UI の provider 明示選択が優先度指定の役割を果たすため専用設定は冗長。設定項目を減らす本 spec の目的とも整合 | 低: 「自動」のまま優先順を変えたい要求には slug 命名での回避のみ |

## Requirements

### Requirement 1: Gaze セクションのデータモデル（規約 id "gaze" の既定チャネル）
**Objective:** As a キャラクター Profile を設定する Unity エンジニア, I want gaze の設定が Profile 直下の専用セクションに規約 id で統合されてほしい, so that expressionId の発行・手動突合をせずに gaze を構成できる。

#### Acceptance Criteria
1. The FacialCharacterProfileSO shall ルート直下に「Gaze セクション」（gaze チャネル定義の保持領域）を持ち、既定構成として規約定数 id `"gaze"` のチャネル 1 件を提供する（S2-1 / G-1）。
2. The gaze チャネル定義 shall 目ボーン path（左右）・回転軸・可動角・入力ソース選択を保持する。
3. Where 上級者向けに複数系統を構成する場合, the FacialCharacterProfileSO shall チャネルリスト 1 本の後続要素として追加の gaze チャネルを保持できる。先頭の既定チャネル（id 固定 `"gaze"`）は常に存在し削除・id 編集不可、追加系統はユーザー命名の自由文字列 id（InputSourceId 規約の validation + 重複禁止）とする（see D-1）。
4. The gaze チャネル shall 従来どおり目ボーン適用経路（`GazeBonePoseProvider` による `Transform.localRotation` 駆動）に接続され、BlendShape レイヤー合成（LayerUseCase / Aggregator）には参加しない（現行アーキテクチャの維持。eye レイヤーとの概念整理は Open Question 6）。
5. When ユーザーが既定チャネルのみを使用するとき、The FacialCharacterProfileSO shall チャネル id の入力・編集を要求しない（id はユーザーが意識しない規約定数とする）。

### Requirement 2: isGaze ダミー Expression と _gazeConfigs の廃止・スキーマ追従
**Objective:** As a Profile データの保守担当, I want isGaze Expression + GazeConfigs の二重管理が廃止されてほしい, so that JSON 直編集や UI 操作で二重管理の整合が壊れるクラスの不具合が構造的に消える。

#### Acceptance Criteria
1. The ExpressionSerializable shall `isGaze` フィールドを持たない（廃止。G-2 / S2-2）。廃止対象は `ExpressionSerializable.isGaze` の 1 フィールドのみであり、Timeline パッケージの `TimelineValueChannelConfig.isGaze` / `FacialTimelineBakeAsset.isGaze` 等は Timeline 独自のチャネル種別フラグ（別概念）として対象外とする。
2. The FacialCharacterProfileSO shall 旧 `_gazeConfigs` リスト（`GazeBindingConfig` の SO ルートリスト）を持たず、Gaze セクションのチャネル定義がこれを置き換える。
3. The profile.json スキーマ shall Gaze セクションを表す root 直下の表現を持ち、旧 `gaze_configs[]` および Expression の `isGaze` を含まない（キー名・schemaVersion の扱いは Open Question 7 で確定）。
4. The FacialCharacterProfileConverter shall 新スキーマ JSON の Gaze セクションを SO の Gaze セクションへ変換する。
5. The FacialCharacterProfileExporter shall SO の Gaze セクションを新スキーマ JSON へ出力し、When Exporter の出力を Converter で読み戻したとき、The ラウンドトリップ結果 shall 元の Gaze セクションと値等価である。
6. The SystemTextJsonParser shall 新スキーマの Gaze セクションをパースできる。
7. If 旧スキーマのデータが入力された場合, the パース・変換経路 shall 自動変換を行わず、明示的な `Debug.LogWarning`（移行ガイドへの誘導を含む）を出力した上で gaze 関連部分を読み捨て、Profile のその他の内容は通常どおり読み込む（無警告の沈黙失敗と全体拒否のどちらもしない。see D-3）。経路別の注記: JSON 経路の検出対象は root の `gaze_configs[]` キーのみとする（現行 v2 JSON スキーマの `ExpressionDto` に `isGaze` は存在せず JSON 境界を越えないため）。SO 経路は Unity デシリアライズが旧フィールドを無警告で捨てるため、警告を実現する場合は検出専用の非公開 legacy フィールド温存が必要になるが、これは AC 2.1 / 2.2 違反とはみなさない（温存の採否・検出実装点は design で確定）。

### Requirement 3: GazeSourceIdConvention による id 規約の一元化
**Objective:** As a コアパッケージの保守担当, I want gaze source id の合成・パースが単一ヘルパーに集約されてほしい, so that `.left` / `.right` リテラルの散在による 1 文字違いの無警告破棄を構造的に防げる。

#### Acceptance Criteria
1. The core（com.hidano.facialcontrol） shall `GazeSourceIdConvention` ヘルパー（`Compose(slug, channelId, side)` / `TryParse`）を新設し、gaze source id の合成・パースの唯一の実装点とする。
2. The GazeSourceIdConvention shall 規約定数 `"gaze"`（既定チャネル id）を単一定義として公開する。
3. The `.left` / `.right` リテラルが散在する 4 箇所（`GazeBindingConfigResolver.cs:33-34` / `IFacialMocapReceiverAdapterBinding.cs:61-62` / `InputSystemAdapterBinding.cs:393,398` / `OscReceiverAdapterBinding.cs:768-769`。行番号は Spec 1 実装でずれるためシンボル参照を正とする） shall すべて `GazeSourceIdConvention` 経由に置換され、side suffix の文字列直書きを残さない（`RecToTimelineExporter.CollectGazeSourceIds` にはリテラルが存在しないため置換対象から除外し、Requirement 10.2 の判定ロジック置換で扱う）。
4. The Spec 1 が局所化した `GazeBindingConfigResolver.ComposeSourceId`（および Spec 1 で追加された受信側 gaze 動的登録経路の id 合成箇所） shall `GazeSourceIdConvention` へ統合される。
5. When `Compose` で合成した id を `TryParse` に与えたとき、The GazeSourceIdConvention shall slug / channelId / side を元の値どおりに分解する（ラウンドトリップ保証）。
6. If `TryParse` に規約非準拠の文字列が与えられた場合, the GazeSourceIdConvention shall 例外を送出せず false を返す。
7. The GazeSourceIdConvention shall Unity 非依存の実装とし、Domain / Adapters いずれの配置でも asmdef 依存方向（各拡張パッケージ → core の一方向）を破らない。

### Requirement 4: binding の gaze source 宣言（IGazeSourceProvider）と入力ソース選択
**Objective:** As a 目線制御手法を選ぶ Unity エンジニア, I want 使いたい手法を Gaze セクションのドロップダウンで選ぶだけにしたい, so that binding ごとに異なる登録の流儀（4 通り併存）や id 突合を意識せずに済む。

#### Acceptance Criteria
1. The core shall gaze source を宣言するインターフェース `IGazeSourceProvider`（仮名。正式名・契約詳細は design で確定）を定義する。
2. The OSC Receiver / InputSystem / iFacialMocap / Timeline の各 binding shall `IGazeSourceProvider` を実装し、自身が提供可能な gaze source を宣言する。
3. While Gaze セクションの入力ソースドロップダウンが表示されているとき、The FacialCharacterProfileSOInspector shall 割当済み binding の宣言済み provider を列挙して選択肢として表示する。
4. The 入力ソース選択の既定値 shall 「自動」とし、While 「自動」が選択されているとき、The gaze 入力源解決 shall 従来どおり規約解決を行い、同一チャネルを複数 binding が提供する場合は slug の Ordinal 辞書順で最小を採用して `Debug.LogWarning` を出す（`preferredSlug` は導入しない。優先度を制御したい場合は AC 4.5 の明示選択を使う。see D-4）。
5. When 特定の provider が明示選択されているとき、The gaze 入力源解決 shall 当該 provider が宣言する source のみを使用する。
6. The IFacialMocapReceiverAdapterBinding shall `gaze.left` / `gaze.right` のハードコード登録に代えて、宣言インターフェース + `GazeSourceIdConvention` 経由で gaze source を公開する（追加設定なしで規約既定チャネル `"gaze"` に接続される従来の使用感は維持する）。
7. The InputSystemAdapterBinding shall 「実体 `{slug}:{actionName}` + 規約 id エイリアス後付け」の二重登録を新規約による一貫した登録に置き換える。
8. The 入力ソース選択機構 shall 将来の procedural gaze ソース（カメラ目線 backlog M-5 等）を provider 追加のみで選択肢に加えられる拡張点を備える（G-3 / S2-3。procedural ソースの実装自体は out of scope）。
9. If 選択済みの入力ソースに対応する provider が実行時に存在しない場合（binding 未割当・宣言なし）, the gaze 入力源解決 shall `Debug.LogWarning` で不足を通知し、無警告で gaze を沈黙させない。

### Requirement 5: 孤児ライフサイクルと旧導線の撤去
**Objective:** As a Inspector の保守担当, I want 旧 identity モデル前提の孤児削除ロジック・デッドパス・validation を撤去したい, so that データモデル統合後に不要となる複雑さがコードベースに残らない。

#### Acceptance Criteria
1. The FacialCharacterProfileSOInspector shall `GazeConfigDeletionTrigger` の 3 トリガ（明示削除 / Expression 削除連動 / Analog→非 Analog 遷移連動）による孤児 GazeConfig 削除ロジックを持たない（データモデル統合により孤児が構造的に発生しなくなるため）。
2. The FacialCharacterProfileSOInspector shall 隠し dropdown のデッドパス（`FacialCharacterProfileSOInspector.cs:1197-1202`）を含まない。
3. The FacialCharacterProfileSOInspector shall isGaze Toggle 連動の validation ロジックを含まない。
4. The FacialCharacterProfileSOInspector および関連 Editor コード shall 旧構造（`isGaze` / SO ルート `_gazeConfigs` / GazeConfig 生成 3 導線）を参照するコードを残さず、プロジェクト標準の Editor asmdef でコンパイル可能である。

### Requirement 6: Inspector UX — 目線タブ 1 か所完結
**Objective:** As a gaze を設定する Unity エンジニア, I want gaze に関する設定を目線タブ 1 か所で完結させたい, so that 表情ライブラリ / Adapter Bindings との 3 タブ往復と導線の分裂（挙動が異なる 3 系統の生成導線）から解放される。

#### Acceptance Criteria
1. The FacialCharacterProfileSOInspector の目線タブ shall gaze の全設定（既定チャネル・目ボーン path・軸・可動角・入力ソース選択・上級設定）を 1 か所で編集可能にし、gaze 設定のために他タブへの往復を必要としない。
2. The 目線タブ shall `useDistinctLeftRight` / `sourceIdLeft` / `sourceIdRight` 相当の上級設定を UI に露出する（現状の JSON 直編集専用フィールドの解消）。
3. Where 上級者向けの追加系統が有効な場合, the 目線タブ shall 追加チャネルの追加・編集・削除 UI を提供する（データ構造は D-1 に従う。既定チャネルは削除・id 編集不可を UI / validation で担保する）。
4. While 既定構成（規約チャネル 1 件 + 入力ソース「自動」）のままであるとき、The 目線タブ shall 上級設定を前面に出さず、目ボーン設定と入力ソース選択のみが主要操作となる表示とする。
5. When 目線タブでの編集が行われたとき、The FacialCharacterProfileSOInspector shall 他の Inspector 編集と同じ `SerializedObject` / `Undo` パイプラインで変更を記録し、Undo 可能とする。

### Requirement 7: 目ボーン自動解決の自動実行とボーン path フルパス化
**Objective:** As a モデルを差し替える Unity エンジニア, I want 参照モデルを割り当てるだけで目ボーンが自動解決されてほしい, so that 残る手動設定が「モデル依存の目ボーン設定（1 回きり）」だけになり、それすら大半のケースで自動化される。

#### Acceptance Criteria
1. When 参照モデルが Profile SO に割り当てられた（null から非 null へ、または別モデルへ変更された）とき、The FacialCharacterProfileSOInspector shall 目ボーン自動解決を自動実行する（現状の `*` マーク表示のみ（`FacialCharacterProfileSOInspector.cs:1636-1643`）の置換）。
2. While 自動解決を実行するとき、The FacialCharacterProfileSOInspector shall 手動編集済みの非空ボーン path を上書きしない。
3. The 保存される目ボーン path shall 単純名（`transform.name`）ではなく Transform 階層パスとする（`AutoAssignGazeBonesFromReferenceModel` が `transform.name` のみ保存する同名ボーン衝突問題（過去 backlog S-1 として記録。現 backlog にブロックは残っていないためクローズ操作は不要）の修正）。なお `BoneTransformResolver` は `/` 入り文字列を既に相対 path として解決できるため resolver 改修は不要だが、path の**起点定義**（ランタイム解決の root は `_animator.transform` であり、参照モデル root と Animator の位置がずれる構成では起点不一致が起きうる）を design で確定する。
4. If 参照モデルから目ボーンを解決できない場合, the FacialCharacterProfileSOInspector shall その旨を UI 上で明示し、手動設定の手掛かりを案内する。
5. The 目線タブ shall 目ボーンの再解決を明示的に実行する操作（個別または一括）を提供する。

### Requirement 8: GazeConfig 注入経路の型付き化（リフレクション解消の検討）
**Objective:** As a コアと拡張パッケージの保守担当, I want `FacialController` から binding への gaze 設定注入がコメント上の契約でなく検証可能な契約になってほしい, so that シグネチャ変更による無警告の注入漏れを防げる。

#### Acceptance Criteria
1. The design phase shall `FacialController.FindGazeConfigureMethod` のリフレクション注入契約（「末尾引数が `IReadOnlyList<GazeBindingConfig>` の Configure」）を型付きインターフェースへ置換するか現状維持とするかを、asmdef 依存方向との整合を含めて確定する（Open Question 5）。
2. Where 型付きインターフェース化を採用する場合, the 置換対象 shall `InputSystemAdapterBinding` の Configure と Spec 1 で追加された `OscReceiverAdapterBinding.Configure` の両方を含める（Spec 1 design「Revalidation Triggers」からの引き継ぎ）。
3. The 注入経路（置換後または現状維持のいずれでも） shall 新 Gaze セクションのチャネル定義を各 binding へ配布でき、旧 `GazeBindingConfig` リスト前提の契約を新データモデルに追従させる。
4. If 注入経路の変更により binding 側が設定を受け取れなくなる構成が生じた場合, the FacialController shall `Debug.LogWarning` で通知し、無警告で gaze を沈黙させない。

### Requirement 9: 死んだスキーマ（look*Clip / look*Samples）の整理
**Objective:** As a gaze を設定する Unity エンジニア, I want ランタイム消費者ゼロのフィールドが設定を促してこないでほしい, so that 実際には機能しない設定に時間を浪費しない。

#### Acceptance Criteria
1. The 新 Gaze セクションのスキーマ shall `look*Clip` ×4 / `look*Samples` ×4（BlendShape gaze 用・ランタイム消費者ゼロ = backlog M-29 未配線）を含まない（事実上の削除。M-29 は将来の別 spec で新データモデルごと再設計する。see D-2）。
2. The FacialCharacterProfileSOInspector shall `look*Clip` / `look*Samples` の入力 UI を表示せず、validation でこれらの設定を促さない。
3. The JSON スキーマ / Converter / Exporter / SystemTextJsonParser shall 当該フィールドを含まず、Requirement 2.5 のラウンドトリップ整合を保つ。

### Requirement 10: Timeline / rec の新規約追従
**Objective:** As a Timeline / rec 機能の利用者, I want gaze の記録・再生・ベイクが新 id 規約でも正しく gaze として扱われてほしい, so that identity 刷新後も rec → Timeline のワークフローが壊れない。

#### Acceptance Criteria
1. The Timeline binding の gaze チャネル構成 shall 現行の 2 要素 — `TimelineValueChannelConfig.isGaze`（Timeline 独自のチャネル種別フラグ。存廃・channel id 判定への置換可否は design で確定）と `takeoverSourceId`（乗っ取り対象 source id）— を新規約に整合させ、`takeoverSourceId` の合成・検証を `GazeSourceIdConvention` 経由にする。
2. The RecToTimelineExporter shall 現行の「GazeConfigs の `sourceIdLeft` / `sourceIdRight` 明示値を Ordinal 収集して rec 記録の source id と突合する」gaze 分類（規約合成 id を分類できない穴があり、かつ GazeConfigs 廃止で元データ自体が消える）に代えて、`GazeSourceIdConvention.TryParse` ベースの判定で gaze source id を分類・収集する。
3. When 新規約の gaze チャネルを含む rec 記録を Timeline へ書き出したとき、The RecToTimelineExporter shall 当該チャネルを gaze として正しく分類・書き出しする。
4. The Timeline binding shall 診断用連番 id（`gaze-0`, `gaze-1`…）による流儀を Requirement 4 の宣言インターフェースと新規約に整合させる。

### Requirement 11: OSC 受信ゼロ設定の成立（Spec 1 D-1 の完結）
**Objective:** As a OSC 送受信を構成する Unity エンジニア, I want 送受信とも既定構成のままで gaze が目ボーンまで反映されてほしい, so that 「OSC 受信端末で gaze だけ動かない」クラスの構成ミスが原理的に発生しなくなる。

#### Acceptance Criteria
1. The 本 spec shall Spec 1 が確立した `/_facialcontrol/gaze` 広告プロトコル（アドレス・flat pairs payload・chunk 分割規約）を変更しない（S1-3。広告に載る id が規約定数 `"gaze"`（追加系統は各チャネル id）になるのみ）。
2. When 送信側が既定構成（規約チャネル `"gaze"`）で gaze を送出しているとき、The OscSenderAdapterBinding shall 広告 id として `"gaze"` を運ぶ。
3. When 受信側が既定構成（規約チャネル `"gaze"` + 目ボーン設定済み）で広告を受信したとき、The 受信側（OscReceiverAdapterBinding + FacialController） shall expressionId の手動突合（mapping 手入力・GazeConfig id 一致作業）なしで gaze 値を目ボーンへ反映する（Spec 1 の D-1 案 (c) が先送りした「GazeConfig 設定も含めた完全な手入力ゼロ」の成立）。
4. The 本 spec shall Spec 1 が実装した受信側 gaze 動的登録経路（広告駆動 rebuild）を破壊せず、id 合成箇所の `GazeSourceIdConvention` への集約に改修を限定する。
5. The 本 spec shall 「送受信とも id 関連の手入力ゼロ（残る設定は受信側の目ボーン設定のみ）で gaze が目ボーンまで反映される」ことを検証する E2E テストを提供する。
6. The 送信側の gaze id 供給 2 経路 shall 新データモデルに追従する: (a) serialized `_gazeExpressionIds`（公開 API `GazeExpressionIds` / `ConfigureGazeExpressionIds`、options JSON の `gazeExpressionIds` キー — `OscOutputDemo/OscSenderOptions.json` で実使用）の去就（チャネル id フィルタへの読み替え or 廃止）と、(b) `ResolveGazeExpressionIds` の `CharacterSO.GazeConfigs` 直読みフォールバックの新 Gaze セクションへの読み替えを design で確定し、広告 id と GazeSnapshot 送出の両方が新セクションから供給されるようにする。OSC options JSON スキーマに変更が及ぶ場合はその追従もスコープに含める。

### Requirement 12: サンプルアセットの更新
**Objective:** As a サンプル利用者, I want 各サンプルが新データモデルでそのまま動作してほしい, so that 新規ユーザーが旧スキーマの構成を手本にしてしまわない。

#### Acceptance Criteria
1. The MultiSourceBlendDemo / OscOutputDemo / OscReceiverDemo の各サンプル（`Samples~/` 配下の Profile アセットおよび対応 profile.json） shall 新スキーマ（Gaze セクション、isGaze / gaze_configs[] なし）へ更新される。
2. When 各サンプル Scene を起動したとき、The 各サンプル shall スキーマ関連の警告・エラーを出さずに gaze を含めて動作する。
3. The 各サンプルの README shall 新しい gaze 設定手順（Gaze セクション + 入力ソース選択）を反映した記述に更新される。
4. The サンプルの gaze 構成 shall 既定チャネル `"gaze"` + 入力ソース選択のみで成立させ、上級設定（複数系統・distinct 左右 id）を使用しない。
5. The IFacialMocapReceiverDemo サンプル shall README の旧モデル手順（「Profile の `GazeBindingConfig` で `ifm:gaze.left` / `ifm:gaze.right` を目ボーンへ結線」）を新手順へ更新し、Profile アセットの stale な `_gazeConfigs: []` 行を再保存で除去する（iFM は「追加設定なしで既定チャネルに接続」（Requirement 4.6）の主役であり、旧手順の放置は新規ユーザーを誤誘導する）。lipsync 2 サンプル（MicLipSyncDemo / AnimationClipLipSyncDemo）の Profile アセットも再保存で stale キーを除去する。
6. The 本 spec shall MultiSourceBlendDemo アセット更新が pre-existing 赤 `SampleAssetsAreInSyncTests` 4 件（backlog M-28: 3 コピー同期ずれ + blink overlay snapshot 欠落）と交差することを tasks に明記し、M-28 自体は取り込まない（3 コピーを同期再生成すれば一部は解消しうるが、blink snapshot 分は gaze 無関係のため M-28 は pre-existing 赤として FAIL 判定から除外する）。

### Requirement 13: 破壊的変更の移行方針とドキュメント整備
**Objective:** As a 既存 Profile 資産の保有者 / ドキュメントの読者, I want 破壊的変更の影響範囲と移行手順が明確であってほしい, so that 既存プロジェクト（Fork 実機含む）を迷わず新スキーマへ移行できる。

#### Acceptance Criteria
1. The 移行方針 shall 手動再設定（preview 破壊）とする（see D-3 / S2-4）。自動マイグレータは実装せず、移行ガイドに旧データ（isGaze Expression + GazeConfigs）からの再設定手順を記載する。Fork 実機（`D:\Unvgi\Repositries\UnvgiFacialVerification`）の Profile も同手順の対象であることを移行ガイドで明示する。
2. The `com.hidano.facialcontrol` の CHANGELOG shall isGaze 廃止・Gaze セクション統合・スキーマ変更を Breaking changes として記録する。
3. The `Documentation~/migration-guide.md` shall 既に存在しない型 `InputSystemGazeBinding` / `_gazeInputBindings` への参照（L232-270 の陳腐化した移植手順）を除去し、本 spec の新スキーマへの移行手順に置き換える。
4. The `docs/mental-model.md` および quickstart 系ドキュメント shall 新しい gaze 設定手順（Gaze セクション・入力ソース選択・目ボーン自動解決）を反映する。
5. The ドキュメント shall gaze（目ボーン経路）と eye レイヤー（BlendShape 合成）の関係を明文化し、docs/requirements.md の「目レイヤー = まばたき・目線」記述との整合を取る（整理方針は Open Question 6）。
6. The ドキュメント shall docs/technical-spec.md §12/§17 の gaze_follow / gaze_camera「Expression テンプレート」構想を S2-3 の再解釈（procedural gaze 入力ソースの切替）に基づく記述へ更新する。

### Requirement 14: スコープ外事項の明示
**Objective:** As a レビュア / 後続 spec の担当者, I want 本 spec が触らない領域を要件レベルで固定したい, so that 大型破壊的変更のレビュー・検証単位が際限なく拡大しない。

#### Acceptance Criteria
1. The gaze-channel-redesign の成果物 shall multi-source gaze blending（backlog M-13）の合成戦略を実装しない。
2. The gaze-channel-redesign の成果物 shall カメラ目線 procedural ソース（backlog M-5）を実装せず、Requirement 4.8 の受け口（入力ソース切替の拡張点）の用意までとする。
3. The gaze-channel-redesign の成果物 shall Vector3 ターゲット指定および VRM 対応を含まない。
4. The gaze-channel-redesign の成果物 shall `/_facialcontrol/gaze` 広告プロトコル（アドレス・payload 形式）を変更しない（Requirement 11.1）。
5. The gaze-channel-redesign の成果物 shall BlendShape gaze の runtime 配線（backlog M-29）を含まない（D-2 で確定。将来の別 spec で新データモデルごと再設計する）。
6. If 本 Requirement に列挙した機能が必要になった場合, the 担当者 shall `docs/backlog.md` への記録または新 spec の起票で対処し、本 spec のスコープを拡大しない。

## Open Questions（design phase で確定する未決事項）

dig インタビューで確定済みの旧 Open Question 1 / 2 / 3 / 4 は「Open Questions and Decisions (Dig)」の D-1 / D-3 / D-4 / D-2 を参照。design phase に残る未決事項は以下の 3 件。

1. **リフレクション Configure 注入の置換方式**: 型付きインターフェース化（asmdef 依存方向との整合をどう取るか）か、現状維持か。置換対象は `InputSystemAdapterBinding` + Spec 1 で追加された `OscReceiverAdapterBinding.Configure`。→ Requirement 8.1
2. **eye レイヤーとの概念整理**: gaze はレイヤー外の別経路のままでよいか。docs/requirements.md の「目レイヤー = まばたき・目線」記述との整合をどう文書化するか。→ Requirement 1.4 / 13.5
3. **Gaze セクションの JSON スキーマ**: キー名、schemaVersion の扱い（gaze-config-promotion では "1.0" 統一の前例あり）。→ Requirement 2.3

## Dig Summary

- **実施日**: 2026-08-09 / ラウンド数: 2 / 質問数: 4 / 決定数: 4（D-1〜D-4）
- **主要な発見**:
  1. 旧データ移行は手動再設定で確定（D-3）。自動変換・一括変換ツールとも不採用。旧スキーマ検出時は「警告 + gaze 部分読み捨て + その他は通常読み込み」に確定し、Requirement 2.7 / 13.1 を具体化した
  2. look*Clip / look*Samples は新スキーマから完全に外す（D-2）。Requirement 9 の分岐（Where 節）が確定し無条件 AC になった。M-29 は将来 spec で新データモデルごと再設計
  3. チャネルはリスト 1 本・先頭既定固定（D-1）。「自動」時の優先度は現行 Ordinal 規則維持で、優先度制御は provider 明示選択に一本化（D-4）— 新設の設定項目はゼロ
- **残リスク（design へ引き継ぎ）**:
  - 旧スキーマ警告の文言・出力箇所（Converter / Parser / Inspector のどこで検出するか）の設計
  - 既定チャネル削除不可の担保方法（UI 制御 + validation + デシリアライズ時の自己修復の要否）
  - Fork 実機 Profile の再設定作業は spec 完了後のフォローアップとして別途管理（Spec 1 の D-3 と同方針）
