# Multi-Spec Plan: gaze-control-overhaul

- 生成日: 2026-08-08
- 元セッション: 目線（gaze）機能の設定煩雑問題の根本原因調査と仕様刷新方針の確定
- Status 欄は /kiro:spec-init-batch が更新する。手で編集して並び替え・追加・削除してもよい。

## 全体コンテクスト

### 背景と最終ゴール

FacialControl の gaze（目線）機能は現状「開発者が LLM に聞きながらでないと設定できない」レベルに設定が煩雑で、実機でも「OSC 受信端末で BlendShape は動くが gaze だけ動かない」不具合が未解決のまま残っている（HANDOVER.md 2026-08-07/08 参照。原因は受信側の手動 gaze mapping 未設定 + GazeConfig 突合の失敗で、構造上の必然）。

2026-08-08 のセッションでコードベース全体（Domain / Inspector / OSC 送受信 / iFacialMocap / InputSystem / Timeline / rec）を調査し、複雑さの根本原因を特定した上で刷新方針をユーザーと確定した。

**最終ゴール**: ユーザーは使用する目線制御手法（コントローラ操作、iFacialMocap、OSC 受信など）を選択するだけで、それ以上の細かい設定なしにデータが送信され、受信側でも正しく解釈されて目線が動く。残る手動設定はモデル依存の目ボーン設定（1 回きり、参照モデルからの自動解決あり）のみ。

### 現状アーキテクチャの要点（調査結果、2026-08-08 HEAD 時点）

セッションを知らない読者向けに、両 spec の前提となる現状を列挙する。パッケージルートは `FacialControl/Packages/`。

1. **gaze の識別子はユーザー任意の `expressionId`**。次の 4 箇所以上を Ordinal 完全一致で手動突合させる設計になっている:
   - `isGaze=true` の Expression（`com.hidano.facialcontrol/Runtime/Adapters/ScriptableObject/Serializable/ExpressionSerializable.cs:36`。GUID 自動採番。レイヤー集約・clip 補間から除外される事実上のダミー行）
   - `GazeBindingConfig.expressionId`（`.../ScriptableObject/GazeBindingConfig.cs:43`。Profile SO 直下の `_gazeConfigs` リスト）
   - binding 側設定（InputSystem の `ExpressionBindingEntry.expressionId`、OSC 受信の `OscMappingEntry.expressionId`）
   - registry 登録 id `{slug}:{expressionId}` / `{slug}:{expressionId}.left` / `.right`（文字列連結規約）
   - OSC 構成ではさらに送信側 Profile の expressionId と受信側 mapping / GazeConfig をマシンをまたいで一致させる必要がある。1 文字違いで無警告破棄
2. **入力源解決**は `GazeBindingConfigResolver`（`.../ScriptableObject/GazeBindingConfigResolver.cs:31-378`）。distinct 明示 → `.left/.right` ペア規約 → 共有 1 本の 3 段フォールバック。複数 slug が同 expressionId を提供すると **slug の Ordinal 辞書順で最小を採用 + Warning**（L361-368）
3. **目ボーン適用**は `GazeBonePoseProvider`（`.../Adapters/Bone/GazeBonePoseProvider.cs:101-165`）が `Transform.localRotation` を直書きする。BlendShape レイヤー系（LayerUseCase / Aggregator）とは完全に別経路。`GazeSnapshot`（Domain 型）は OSC 送信専用で目ボーン経路には使われない
4. **OSC の非対称性**: BlendShape は heartbeat（`/_facialcontrol/blendshape_names`）∩ 受信側 mesh の積集合で自動マッピングされるが、**gaze の expressionId は送信側 Profile にしか存在しない**ため積集合が取れず、受信側は手動 mapping 必須。gaze route は `OnStart` 固定で heartbeat 再構築（`PublishRuntimeMappings`）の対象外
5. **アダプタごとに gaze source 登録の流儀が 4 通り併存**:
   - OSC Receiver: `{slug}:{expressionId}[.left/.right]`（規約準拠。`OscReceiverAdapterBinding.cs:729-803`）
   - InputSystem: 実体は `{slug}:{actionName}`、規約 id はエイリアス後付け（`InputSystemAdapterBinding.cs:372-420`）
   - iFacialMocap: **`gaze.left` / `gaze.right` にハードコード**（`IFacialMocapReceiverAdapterBinding.cs:61-62`）。GazeConfig の expressionId を文字通り `"gaze"` にしないと規約解決が通らない
   - Timeline: 診断用連番 `gaze-0`, `gaze-1`…（実効は takeoverSourceId で別指定）
6. **Inspector の穴**: `GazeBindingConfig.useDistinctLeftRight` / `sourceIdLeft` / `sourceIdRight` は Inspector に UI が無く JSON 直編集のみ。同名フィールドが `OscMappingEntry`（受信 binding 側）にも存在するが、そちらは**入力チェックにだけ使われ値自体は無視される**という意味の違いがある
7. **死んだスキーマ**: `GazeBindingConfig.look*Clip` ×4 / `look*Samples` ×4 はランタイム消費者ゼロ（BlendShape gaze 経路未配線 = backlog M-29）なのに Inspector に ObjectField が並び、validation が設定を促す
8. **設定導線の分裂**: 最短でも 3 タブ往復（表情ライブラリ / 目線 / Adapter Bindings）。GazeConfig 生成導線が 3 系統（隠し dropdown / 一括再生成 / 表情行の条件付きボタン）あり挙動が全部違う
9. **`FacialCharacterProfileSO` → binding への GazeConfig 注入はリフレクション**（`FacialController.FindGazeConfigureMethod`、「末尾引数が `IReadOnlyList<GazeBindingConfig>` の Configure」というコメント上の契約）
10. **関連 spec**: `.kiro/specs/gaze-config-promotion`（implemented。GazeConfig の SO ルート昇格）と `.kiro/specs/osc-receiver-auto-mapping`（BlendShape 自動マッピング本体。gaze は明示的 Non-Goal で backlog M-25 に先送り）。`docs/backlog.md` の M-25 が本プラン Spec 1 の出典（なお backlog は M-25 番号が 2 件重複しており要修正）

### なぜ複数 Spec に分割するのか

- Spec 1（OSC 自動マッピング）は現行の identity モデルのまま実装でき、実機の未解決不具合（受信端末 gaze 不動）を最短で根治する。osc パッケージ + 送信 binding に閉じ、影響範囲が管理可能
- Spec 2（identity/データモデル刷新）は SO/JSON スキーマ・Inspector・全アダプタに及ぶ大型破壊的変更で、要件も独立にレビュー可能
- 広告プロトコルを「id 文字列を運ぶ」設計にしておけば、Spec 2 適用後は広告される id が規約定数 `"gaze"` になるだけで Spec 1 の成果はそのまま生きる（手戻りなし）

## 共通の決定済み事項

| ID  | 決定 | 理由 |
|-----|------|------|
| G-1 | gaze チャネルの既定 id を規約定数 `"gaze"` 1 本に固定。複数系統は上級者向けオプションに格下げ | VTuber ユースケースの大半は gaze 1 系統で足りる。expressionId の手動突合（送受信マシン間 Ordinal 一致含む）が複雑さの最大の源泉であり、既定を規約化すれば突合作業自体が消える。iFacialMocap は既に `"gaze"` 前提のハードコードで運用実績がある |
| G-2 | isGaze ダミー Expression + GazeConfigs の二重管理を廃止し、Profile 直下の「Gaze セクション」へ統合（案 X） | isGaze Expression の実行時の役割は「expressionId の発行元」だけ（レイヤー集約・遷移・Activate いずれにも不参加）。二重管理の整合を守る孤児削除ロジックが複雑さを生み、JSON 直編集で壊れる。G-1 の規約 id は専用セクションの定数として置くのが自然 |
| G-3 | 将来の「カメラ目線」（backlog M-5 / docs/technical-spec.md §12 の gaze_camera）は Expression 切替ではなく **procedural な gaze 入力ソースの一種**として入力ソース切替で表現する | 案 X で Expression の Activate 機構による gaze モード切替の道が閉じるため。入力ソース選択 UI に「カメラ目線ソース」を追加する形が G-2 と一貫する |
| G-4 | OSC の gaze 広告は heartbeat 混載ではなく `/_facialcontrol/gaze` **専用アドレス**を新設。id 文字列 + 形式（VRChat_XY / ARKit_8BS）を運ぶ | 形式情報（メッセージ数・セマンティクスが preset で変わる）も運ぶ必要があり、BlendShape 名一覧への混載では表現できない。id を文字列で運べば Spec 2 前後で互換 |
| G-5 | 実行順は Spec 1（osc-gaze-auto-mapping）→ Spec 2（gaze-channel-redesign） | 実機の未解決不具合の根治を優先。Spec 1 は現行 identity モデルで成立し、Spec 2 の破壊的変更を待たない |

## 検討して捨てた選択肢

| 選択肢 | 捨てた理由 |
|--------|-----------|
| 案 Y: データモデル維持で Inspector 統合のみ | 二重管理・孤児削除ロジック・JSON 直編集で壊れる経路が温存され、「UI で隠したが構造は複雑なまま」になる。G-1 の実装が「gaze 用 Expression だけ固定 id」という特例パッチになり歪む |
| 現行の複数 expressionId 体系を維持して広告・自動化だけ整備 | 設定項目の総量が減らない。突合ミス（無警告沈黙）のクラスが残る |
| gaze 広告を heartbeat の BlendShape 名一覧に混載 | 形式（VRChat_XY / ARKit_8BS）を運べない。BlendShape 名と gaze id の名前空間衝突リスク |
| 柱 1〜3 を 1 spec でまとめて実施 | 破壊的変更の面が広すぎてレビュー・検証単位として不適。実機不具合の根治が遅れる |
| M-14 (a)「gaze も Expression の 1 種」の Domain 抽象化路線 | 案 X 採用により放棄（ユーザー確認済み）。gaze モード切替は入力ソース切替で表現する（G-3） |
| Aggregator 側の防御的 mask 長ガード | 本プランとは別件（HANDOVER 2026-08-08 クローズ済み案件のスコープ外決定を踏襲） |

## Spec 一覧（推奨実行順）

| # | Spec | Status  | 依存 |
|---|------|---------|------|
| 1 | osc-gaze-auto-mapping | PENDING | -    |
| 2 | gaze-channel-redesign | PENDING | #1   |

---

## Spec: osc-gaze-auto-mapping

- Status: PENDING
- Feature dir: (spec-init-batch が記入)
- 依存: なし

### 概要

OSC 送信側が `/_facialcontrol/gaze` 専用アドレスで gaze の id と形式（VRChat_XY / ARKit_8BS）を広告し、受信側が gaze route / `GazeVector2InputSource` を自動生成する。backlog M-25 の実行。あわせて調査で発覚した gaze 経路の潜在バグ 4 件を回収し、実機の未解決不具合「OSC 受信端末で gaze だけ動かない」を根治する。

### スコープ (in)

- 送信側（`com.hidano.facialcontrol.osc/Runtime/Adapters/AdapterBindings/OscSenderAdapterBinding.cs` / `OscBundleBuilder.cs`）: heartbeat 送出タイミングに合わせて `/_facialcontrol/gaze` メッセージを追加。ペイロードは gaze expressionId（文字列）+ 形式（endpoint preset から確定: VRChat=XY 2 メッセージ / ARKit=eyeLook 8 BlendShape）。複数 gaze id 対応
- 受信側（`OscReceiverAdapterBinding.cs`）: 広告受信で gaze route / source を動的生成・再構築する経路を新設（BlendShape の `PublishRuntimeMappings` L1079-1118 に相当する gaze 版。現状 gaze は `OnStart` 固定 = L468 `HasGazeMappings(_mappings)` が SerializeField のみ参照）。手動 `OscMappingEntry`（`Gaze_VRChat_XY` / `Gaze_ARKit_8BS`）は上書き用オプションとして残す
- 潜在バグ回収:
  1. **Custom preset + gaze で送信側 `OnStart` が未捕捉例外で落ちる**（`OscAddressFormatter.cs:185-194` の `GetGazePrefix` throw を gaze 経路だけ catch していない。BlendShape 側 L782-790 は catch あり）→ 警告 + スキップに統一
  2. **gaze source 後発登録レース**: 規約解決では「今解決できた id」しか Subscribe しない（`FacialController.cs:852-880`）ため、接続確立後に source 登録される binding（iFM 等）で目ボーンが動かない可能性 → 未解決 id も先読み Subscribe できる形に修正
  3. **`Gaze_VRChat_XY` + `leftRightIndependent=true` の見かけ倒し**: UI 上「左右独立」に見えるが実際は左右同値が配られる（`GazeRuntimeEntry.PublishVrChat`、`OscReceiverAdapterBinding.cs:1801-1817`。VRChat 形式は単一 Vector2 しか運べないため）→ Drawer で警告表示または組み合わせ禁止
  4. **gaze 読取ロジック重複**: `GazeBonePoseProvider.TryReadInputXY`（L215-246）と `FacialController.TryReadGazeInput`（L566-592）が別実装で scalar 時挙動が微妙に違う → core の 1 箇所に共通化
- `OscReceiverDemo` サンプルのハイブリッド構成解消（手入力 gaze mapping 1 件の削除、README 更新）
- ドキュメント修正: `docs/backlog.md` の M-25 番号重複解消、M-25 のクローズ、`docs/mental-model.md` の gaze 受信記述更新
- テスト: 広告→自動生成の E2E（`OscGazeE2ETests.cs` 拡張）、後発登録・staleness・手動上書き共存

### スコープ (out)

- identity モデルの変更（規約 id "gaze" 化、isGaze 廃止）→ Spec 2
- 受信側 GazeConfig（目ボーン path）の自動生成 UX 改善 → Spec 2
- BlendShape gaze の runtime 配線（backlog M-29）
- multi-source gaze blending（backlog M-13）
- uOsc 差し替え等のプロトコル基盤変更

### この Spec 固有の決定済み事項

| ID  | 決定 | 理由 |
|-----|------|------|
| S1-1 | 広告アドレスは `/_facialcontrol/gaze` 専用（G-4 再掲）。形式は送信側 endpoint preset から確定した値を載せ、受信側での形式推定はしない | `/ARKit/eyeLook*` は通常 BlendShape としても正当なため受信側単独では VRChat/ARKit を判別不能（M-25 設計判断項目 (c) の解決） |
| S1-2 | 手動 mapping は削除せず「上書き用オプション」に格下げ | 送信側が FacialControl でない外部 OSC ソース（VRChat 本体等）からの受信では広告が来ないため、手動経路は残す必要がある |
| S1-3 | 広告は id を文字列で運ぶ | Spec 2 適用後は広告 id が規約定数 `"gaze"` になるだけでプロトコル無変更（手戻り防止） |

### 未決事項（dig で確認すべき候補）

- **広告 id と受信側 GazeConfig の突合**: 現行 identity モデルでは送信側の expressionId（GUID 等）と受信側 GazeConfig の expressionId が一致しないのが通常。route/source の自動生成だけでは目ボーンまで届かない。候補: (a) 受信側 GazeConfig が 1 件だけなら広告 id を自動でそこへルーティング（既定 gaze フォールバック）、(b) 広告 id で GazeConfig エントリを自動補完し目ボーン path のみユーザー設定、(c) Spec 1 では mapping 自動生成まで（GazeConfig 突合は従来どおり手動）とし Spec 2 の "gaze" 規約化で完結させる。どこまでを Spec 1 で解決するか
- 広告のペイロード形式（`leftRightIndependent` 相当の左右独立性フラグを載せるか / 1 メッセージに複数 id を詰めるか id ごとに 1 メッセージか / MTU との関係）
- 広告の送出周期（heartbeat と同periodic か、起動時 + 変化時のみか）と受信側の staleness 扱い
- 自動生成された gaze mapping の Inspector 表示（backlog M-26 の Manual/Auto badge との整合）
- Fork（jp.co.unvgi.*、npm.unvgi.com）への反映・publish をこの spec の完了条件に含めるか

### 他 Spec とのインターフェース

- Spec 2 へ引き渡すもの: `/_facialcontrol/gaze` 広告プロトコル（アドレス・ペイロード仕様）。Spec 2 は広告される id が `"gaze"` 定数になるだけでプロトコルを変更しない
- gaze source の動的登録/再構築コード（受信側）は、Spec 2 で `GazeSourceIdConvention` ヘルパーに寄せることを見越し、id 合成箇所を局所化して実装する

---

## Spec: gaze-channel-redesign

- Status: PENDING
- Feature dir: (spec-init-batch が記入)
- 依存: osc-gaze-auto-mapping（広告プロトコルと gaze 動的登録経路を前提とする。逆順に実施すると Spec 1 の設計が旧 identity モデルに縛られ手戻りする）

### 概要

gaze の識別を規約 id `"gaze"` 1 本の既定チャネルに再設計し、isGaze ダミー Expression + GazeConfigs の二重管理を廃止して Profile 直下の「Gaze セクション」へ統合する。各 binding は gaze source を宣言インターフェースで公開し、ユーザーは Gaze セクションの入力ソース選択（ドロップダウン）だけで手法を切り替えられる。目ボーン設定は参照モデル割当時に自動解決する。

### スコープ (in)

- **データモデル**: `FacialCharacterProfileSO` 直下に Gaze セクション（規約 id `"gaze"` の既定 1 件 + 上級者向け追加系統。目ボーン path・軸・可動角・入力ソース選択を保持）。`ExpressionSerializable.isGaze`（L36）と `_gazeConfigs` の廃止、JSON スキーマ・`FacialCharacterProfileConverter` / Exporter / `SystemTextJsonParser` の追従、サンプルアセット 3 系統（MultiSourceBlendDemo / OscOutputDemo / OscReceiverDemo）の Profile 更新
- **id 規約の一元化**: `GazeSourceIdConvention` ヘルパー（`Compose(slug, channelId, side)` / `TryParse`）を core に新設。現在 `.left`/`.right` 文字列が散っている最低 5 箇所（`GazeBindingConfigResolver.cs:33-34` / `IFacialMocapReceiverAdapterBinding.cs:61-62` / `InputSystemAdapterBinding.cs:393,398` / `OscReceiverAdapterBinding.cs:768-769` / `RecToTimelineExporter.CollectGazeSourceIds`）を全て置換
- **binding の gaze source 宣言**: `IGazeSourceProvider`（仮名）インターフェースを各 binding（OSC Receiver / InputSystem / iFacialMocap / Timeline）に実装させ、Gaze セクションの「入力ソース」ドロップダウンが宣言済み provider を列挙する。既定は「自動」（従来の規約解決 + 明示優先度）。iFM の `gaze.left/right` ハードコードは宣言で吸収
- **孤児ライフサイクルの撤去**: Inspector の孤児削除ロジック（`GazeConfigDeletionTrigger` 3 トリガ）、隠し dropdown デッドパス（`FacialCharacterProfileSOInspector.cs:1197-1202`）、isGaze Toggle 連動 validation の削除
- **Inspector UX**: 目線タブ 1 か所で完結（3 タブ往復の解消）。`useDistinctLeftRight` / `sourceIdLeft/Right` 相当の上級設定も同タブに露出。参照モデル割当時に目ボーン自動解決を自動実行（現状は `*` マーク表示のみ、`FacialCharacterProfileSOInspector.cs:1636-1643`）
- **ボーン path のフルパス化**: `AutoAssignGazeBonesFromReferenceModel`（L2992/L3004）が `transform.name` しか保存しない問題を修正（backlog S-1 同時解消）
- **リフレクション注入の解消**: `FacialController.FindGazeConfigureMethod`（L377-402）の「末尾引数型で探す Configure」契約を型付きインターフェースへ置換（検討の上）
- **死んだスキーマの整理**: `look*Clip` / `look*Samples` の UI を隠すか削除（M-29 を取り込まない場合）
- **ドキュメント**: `Documentation~/migration-guide.md:232-270` が既に存在しない型 `InputSystemGazeBinding` / `_gazeInputBindings` の移植手順を案内している陳腐化の解消、`docs/mental-model.md` / quickstart への gaze 手順追記
- **Timeline / rec の追従**: `TimelineValueChannelConfig.takeoverSourceId` / `RecToTimelineExporter` の gaze 判定を新規約に追従

### スコープ (out)

- BlendShape gaze の runtime 配線（backlog M-29）— ただし dig で取り込み判断の余地あり
- multi-source gaze blending（backlog M-13）と合成戦略
- カメラ目線 procedural ソースの実装（backlog M-5）— 本 spec は入力ソース切替の受け口（G-3）を用意するまで
- Vector3 ターゲット指定 / VRM 対応
- OSC 広告プロトコルの変更（Spec 1 の成果をそのまま使用）

### この Spec 固有の決定済み事項

| ID  | 決定 | 理由 |
|-----|------|------|
| S2-1 | 既定チャネル id は規約定数 `"gaze"`（G-1 再掲）。複数系統は上級オプションとして残す | 全アダプタ・送受信の突合を定数化して消すため。iFM の既存ハードコードと自然に整合 |
| S2-2 | isGaze Expression は廃止（G-2 = 案 X。ユーザー確認済み） | 実行時の役割が id 発行のみで、二重管理コストに見合わない |
| S2-3 | カメラ目線・視線追従の切替は gaze 入力ソースの切替で表現（G-3） | Expression Activate 機構への依存を断つ。docs/technical-spec.md §12/§17 の gaze_follow / gaze_camera「Expression テンプレート」構想はこの再解釈で置き換える |
| S2-4 | preview 段階の破壊的変更として実施（CLAUDE.md の方針どおり）。Fork 実機 Profile への影響は移行手順または移行コンバータで吸収 | JSON ファースト永続化の方針上、スキーマ変更は preview 中に済ませるべき |

### 未決事項（dig で確認すべき候補）

- **複数系統（上級オプション）のデータ構造**: 既定 1 件 + 追加ボタンのリストか、既定は単数フィールドで上級はリスト併設か。追加系統の id 命名規則（自由文字列か `gaze.2` 的な規約か）
- **旧データの移行**: 既存 profile.json / SO（isGaze Expression + GazeConfigs）の自動マイグレータを書くか、preview 破壊として手動再設定を README 案内で済ませるか。Fork 実機（`D:\Unvgi\Repositries\UnvgiFacialVerification`）の Profile への影響範囲
- **「自動」ソース選択時の優先度規則**: 現行の「slug Ordinal 辞書順 + Warning」を維持するか、明示的な優先度リスト（`preferredSlug`）を導入するか
- **look*Clip（BlendShape gaze）の扱い**: UI から隠すだけか、スキーマごと削除か、M-29 を本 spec に取り込んで配線するか（目ボーン非搭載モデルのユーザーへの影響）
- **リフレクション Configure 注入の置換方式**: 型付きインターフェース化（asmdef 依存方向との整合をどう取るか）か、現状維持か
- **eye レイヤーとの概念整理**: gaze はレイヤー外の別経路のままでよいか。docs/requirements.md の「目レイヤー = まばたき・目線」記述との整合をどう文書化するか
- **Gaze セクションの JSON スキーマ**（キー名、schemaVersion の扱い — gaze-config-promotion では "1.0" 統一の前例あり）

### 他 Spec とのインターフェース

- Spec 1 から受け取るもの: `/_facialcontrol/gaze` 広告プロトコル（無変更で使用。広告 id が `"gaze"` 定数になる）、受信側 gaze 動的登録経路（`GazeSourceIdConvention` へ寄せるリファクタ対象）
- Spec 1 の未決事項「広告 id と受信側 GazeConfig の突合」で (c)（Spec 2 で完結）が選ばれた場合、本 spec の完了をもって受信側ゼロ設定が成立する
