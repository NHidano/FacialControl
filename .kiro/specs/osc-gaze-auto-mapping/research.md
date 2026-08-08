# Research & Design Decisions — osc-gaze-auto-mapping

## Summary
- **Feature**: `osc-gaze-auto-mapping`
- **Discovery Scope**: Extension（既存 OSC auto-mapping アーキテクチャの gaze 拡張 + 潜在バグ 4 件の回収）
- **Key Findings**:
  - 送信側の gaze 広告は既存の heartbeat frame bundle（`SendBundle` → `OscBundleBuilder.BuildFrameBundleCore`）に preset message と同じ方式で同乗でき、sender identity gating・MTU 分割・GC ゼロ運用の全てを既存機構で継承できる
  - 受信側は `osc-receiver-auto-mapping` で確立した「受信スレッド accumulate + dirty flag → `OnFixedTick` で FNV-1a 変化検出 → 再構築」パターンがそのまま gaze route に適用可能。route 辞書 `_gazeRoutes` は受信スレッドから参照されるため、再構築は immutable-swap（参照差し替え）が必要
  - `FacialController.SubscribeGazeInputSources` は「今解決できた id」しか Subscribe しないが、`IInputSourceRegistry.Subscribe` は未登録 id にも張れる契約のため、候補 id を先読み合成して Subscribe すれば Domain 契約変更なしで後発登録レースを解消できる
  - `OscReceiverAdapterBinding` は受信側 GazeConfig を知らないが、`FacialController.FindGazeConfigureMethod` のリフレクション注入契約（末尾引数 `IReadOnlyList<GazeBindingConfig>` の `Configure`）に乗れば core 側変更ゼロで GazeConfig 突合警告を receiver 側に置ける

## Research Log

### 送信側: 広告の送出タイミングと組み立て経路
- **Context**: Req 1（heartbeat と同周期の広告送出）の実装ポイント確定。
- **Sources Consulted**: `OscSenderAdapterBinding.cs`（OnLateTick L376-448 / ShouldSendHeartbeat L959-975 / TryBuildMappings L740-810 / AppendGazeMappings L812-864 / SendSlot L1064-1114）、`OscSender.cs`（SendBundle 6 overload 群 L239-421）、`OscBundleBuilder.cs`（BuildFrameBundleCore L270-379 / AddPresetMessage L620-631 / GetFittingStringChunkCount L758-790）。
- **Findings**:
  - heartbeat 判定は `ShouldSendHeartbeat`（既定 5 秒、`_sendHeartbeatOnNextTick` で初回即時）。preset message は heartbeat 送出時のみ frame bundle に同乗する。gaze 広告も同じ分岐に同乗させるのが最小変更。
  - `SendSlot.Preset` が endpoint ごとの形式を保持（VRChat → `VRChat_XY` / ARKit → `ARKit_8BS`）。gaze expressionId は `ResolveGazeExpressionIds`（明示指定 or FacialController.CharacterSO.GazeConfigs フォールバック）で OnStart 時に確定済み → 広告 payload（`[id, format]` ペア配列）は OnStart で 1 回だけ構築して SendSlot に保持すれば毎 heartbeat の GC ゼロ。
  - `OscSender.SendBundle` は既に 6 overload。preset 引数追加時の 7 引数 overload が既に上限感 → 追加引数はまとめて readonly struct（options 構造体）で渡す方式に切り替えるべき。
  - `OscBundleBuilder` の MTU 分割は `GetFittingStringChunkCount`（string message 単位）+ `BeginMessage` の packet 溢れ時分割（sender identity を継続 packet に再添付）。広告 message も `AddStringMessage` と同型で追加でき、ペア境界（2 要素単位）で chunk すれば受信側は heartbeat と同じ timestamp accumulate で復元できる。
- **Implications**: 送信側は「SendSlot に事前構築した広告ペア配列 + BuildFrameBundleCore の広告 message 追加 + options struct overload」の 3 点改修に収まる。

### 送信側: Custom preset + gaze の未捕捉例外（Req 5）
- **Context**: 潜在バグ (1) の実証。
- **Sources Consulted**: `OscAddressFormatter.cs`（GetGazePrefix L185-194: VRChat 以外 `NotSupportedException`）、`OscSenderAdapterBinding.cs`（TryBuildMappings 内 BlendShape 側は L783-790 で catch あり、AppendGazeMappings L839-846 の `FormatGazeAddress` / `GetOrAddGazeAddressUtf8` 呼び出しは catch なし）、`OnStart` 全体（L255-374: TryBuildMappings 呼び出しは try 外）。
- **Findings**: Custom preset endpoint + gaze 構成で `AppendGazeMappings` → `FormatGazeAddress(Custom, ...)` が throw → `OnStart` を突き抜けてホスト（`AdapterBindingHost.InvokeOnStartOnce`）の catch に到達 → binding 全体 skip（他 endpoint・heartbeat 含め全停止）。ARKit preset は `AppendArKitGazeMappings`（FormatBlendShapeAddress(ARKit)）経路のため throw しない。
- **Implications**: BlendShape 側と同じ「catch → LogWarning → 当該 gaze のみ skip」に統一。Custom slot は広告ペア配列も null（広告対象外）+ 1 度だけ警告。

### 受信側: gaze route の OnStart 固定と動的化の障害
- **Context**: Req 2（広告駆動の動的生成・再構築）の統合ポイント確定。
- **Sources Consulted**: `OscReceiverAdapterBinding.cs`（OnStart L425-489 / StartReceiverPhase L609-661 / RegisterGazeSources L729-803 / InitializeGazeBundleState L836-845 / HandleIncomingOscMessage L865-897 / HandleHeartbeatMessage + ProcessPendingHeartbeatMappings L986-1077 / TryHandleGazeMessage L1216-1241 / PublishGazeForCurrentLifecycleState L1396-1431）。
- **Findings**:
  - `SetMessageFilter` は mapping の有無に関わらず常時アタッチ（L657-660）→ 広告 message は既存 dispatch に分岐追加するだけで受信可能。
  - gaze bundle state（`_gazeBundleSync` 等）は `hasGazeMappings` 時のみ OnStart で初期化（L629-631）→ 広告駆動パスでは lazy 初期化が必要。初期化完了後に route 辞書を publish する順序制御で受信スレッドとの race を回避できる。
  - `_gazeRoutes`（Dictionary）は受信スレッド `TryHandleGazeMessage` から読まれる → メインスレッド再構築は新辞書を構築して参照を Volatile swap する immutable-swap が必須（要素の in-place 変更は禁止）。
  - `RegisterGazeSources` は `Gaze_ARKit_8BS` のとき `leftRightIndependent` に関係なく常に `.left` / `.right` を登録（L766-770）。広告駆動の ARKit_8BS も同じ規約に合わせる。`Gaze_VRChat_XY`（非独立）は共有 1 本 `{slug}:{expressionId}`。
  - `registry.Register` は重複時 LogError + 後勝ち → 手動 entry と同一 expressionId の広告 id は Register 前に除外しないと手動 route が破壊される（Req 3.2 の実装制約）。再広告での同一 id は source インスタンスを再利用して Register 自体を呼ばない。
  - `StartReceiverPhase` L636-641 の「gaze mapping が未設定のため Gaze 受信は無効です」ログは広告駆動導入で前提虚偽になる。backlog S-21 の pre-existing 赤 4 件（`OscHeartbeatConsistencyTests` ×1 / `OscReceiverAdapterBindingAutoMappingIntegrationTests` ×2 / `OscReceiverGCAllocationTests` ×1）はこのログの LogAssert 未追従が原因のため、ログ削除で同時解消する。
  - 決定論テスト経路は `binding.HelperHost.Receiver.HandleOscMessage(new uOSC.Message(...))`（`OscReceiverAdapterBindingAutoMappingIntegrationTests` L284-298 の既存方式）が広告にもそのまま使える。
- **Implications**: 受信側は「広告 accumulate + dirty + FNV-1a + 再構築」を新 helper `GazeAdvertisementResolver` に切り出し（`RuntimeMappingResolver` 前例踏襲）、binding は accumulate / dirty / rebuild 配線のみ担う（Option C）。

### FacialController: gaze 後発登録レースと読取重複（Req 6 / 8）
- **Context**: 潜在バグ (2)(4) の実証と修正方式の確定。
- **Sources Consulted**: `FacialController.cs`（SubscribeGazeInputSources L852-880 / AddGazeSubscriptionId L882-891 / TryReadGazeInput L566-592 / SetupGazeBoneProvider L701-751 / ConfigureAdapterBindingsWithGazeConfigs L311-402）、`GazeBindingConfigResolver.cs`（全体: distinct → side-pair → shared の 3 段解決、`.left`/`.right` 定数 L33-34）、`GazeBonePoseProvider.cs`（TryReadInputXY L215-246 / Apply の caller 側 clamp L129-130）、`IInputSourceRegistry.cs`（Subscribe L125-131: 未登録 id にも張れる / Register・Replace 時に同期通知）。
- **Findings**:
  - 規約解決パス（`useDistinctLeftRight=false`）では `TryResolve` が**今**成功した場合のみ `resolved.LeftSourceId/RightSourceId` を Subscribe する。解決失敗時は Subscribe ゼロ → 後発登録（iFacialMocap 等の接続確立後 Register、広告駆動の遅延 Register）が `SetupGazeBoneProvider` を再トリガしない。
  - `Subscribe` は per-id のみで registry 全体の登録イベント API は無い。ただし規約 id は `{slug}:{expressionId}` / `.left` / `.right` の 3 形のみで、slug は `ctx.AdapterBindings`（`AdapterBuildContext` L50）の `AdapterBindingBase.Slug` から全列挙できる → 候補 id 全合成 + Subscribe が Domain 契約変更なしで成立する。
  - 読取重複: `GazeBonePoseProvider.TryReadInputXY` は null チェックなし・clamp は caller（Apply L129-130）・scalar は x=v, y=0（コメント「y のみ駆動」は実装と不一致）。`FacialController.TryReadGazeInput` は null チェックあり・clamp 内包・scalar は x=v, y=0。**scalar の実質セマンティクスは両者一致**（x に代入）。差分は null チェックと clamp 位置のみ。
- **Implications**: 共通化は「FacialController 側挙動（null チェック + clamp 内包）+ scalar → x=v, y=0」に寄せた static helper 1 本で挙動互換にできる。誤解コメントは helper 側で正しい記述に修正。

### 広告 payload 形式と MTU（Open Question 1）
- **Context**: 1 message 複数ペア vs per-id message、左右独立フラグの要否。
- **Sources Consulted**: `OscBundleBuilder.cs`（heartbeat chunk 分割 / preset message の string 引数方式）、`HandleHeartbeatMessage` の timestamp accumulate、`PerfectSyncEyeLook`（ARKit_8BS が形式自体で左右を運ぶ）。
- **Findings**:
  - preset message 前例: 1 address + string 引数列。gaze 広告も `[id, format, id, format, ...]` の flat pairs で自然に表現できる。
  - 典型構成は gaze id 1〜2 件 → 1 message で常に MTU 内。id 多数の異常系のみ heartbeat 同様の chunk 分割が必要で、ペア境界（2 要素単位）で分割すれば受信側は同一 bundle timestamp の accumulate で無損失復元できる（heartbeat chunk と同じ機構）。
  - 左右独立性フラグは不要: `ARKit_8BS` は形式自体が左右を運び、`VRChat_XY` は単一 Vector2 しか運ばないため独立フラグを載せても実現不能（Req 7 と同根）。載せないことでプロトコルが Spec 2 まで安定する。
- **Implications**: design.md の Decision 1 参照。

### 変化検出ハッシュの正規化順序（Req 9.1）
- **Context**: FNV-1a は順序依存のため、送信側の広告順序揺れ（構成同一・順序違い）を「変化」と誤検出しない正規化が必要。
- **Sources Consulted**: `HeartbeatHashHelper.cs`（ComputeFnv1a: `IReadOnlyList<string>` を順に連続ハッシュ、名前間 0x00 区切り）。
- **Findings**: (id, format) ペアを **id の Ordinal 昇順** に整列した interleaved リスト `[id0, fmt0, id1, fmt1, ...]` に正規化してから既存 `HeartbeatHashHelper.ComputeFnv1a` に渡せば、helper 新設なしで順序安定ハッシュになる。整列は再利用 scratch リスト + 挿入ソート（件数 ≤ 数件）で GC ゼロ化可能だが、実行は広告受信時の `OnFixedTick`（毎フレームではない）のため厳密なゼロ化は必須でない（Req 9.3 は「変化していないとき」のみ縛る。ただし hash 計算自体は dirty 消化のたびに走るため scratch 再利用は行う）。
- **Implications**: `GazeAdvertisementResolver` に正規化を実装し、ハッシュ本体は `HeartbeatHashHelper` を再利用。

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| A: binding 内に直書き | `OscReceiverAdapterBinding` に広告 parse / diff / rebuild を全部実装 | ファイル数最小 | binding が 1830 行 → 2200 行超に肥大。単体テスト不能（PlayMode 必須） | 却下 |
| B: 汎用 auto-mapping resolver 拡張 | `RuntimeMappingResolver` に gaze 経路を統合 | 入口 1 本化 | BlendShape mapping（`OscMapping[]` / DoubleBuffer 経路）と gaze route（`GazeRoute` / ValueProvider 経路）は生成物が完全に別物。統合すると両者の責務が混濁 | 却下（backlog M-25 設計判断項目 (b) の指摘どおり生成経路が別） |
| C: gaze 専用 helper 新設 | `GazeAdvertisementResolver`（static helper）に parse / 正規化ハッシュ / route plan 計算を切り出し、binding は accumulate / dirty / rebuild 配線のみ | `RuntimeMappingResolver` / `AddressPresetEstimator` 前例と一貫。EditMode 単体テスト可能。binding 側差分最小 | helper と binding の状態受け渡し設計が必要 | **採用** |

## Design Decisions

### Decision 1: 広告 payload は 1 message の flat pairs `[id, format, ...]`（Open Question 1）
- **Context**: 広告に載せる情報の形と MTU 対策。
- **Alternatives Considered**:
  1. 1 message に interleaved pairs — preset message 前例の拡張。
  2. id ごとに 1 message（`[id, format]` 2 引数）— message 数が id 数に比例。
  3. 左右独立フラグを 3 要素目として追加 — 情報量最大。
- **Selected Approach**: 案 1。`/_facialcontrol/gaze` 1 address に string 引数を `[id0, format0, id1, format1, ...]` で詰める。format 識別子は `"VRChat_XY"` / `"ARKit_8BS"` の 2 値。MTU 超過時はペア境界で chunk 分割し、同一 bundle timestamp で accumulate（heartbeat と同機構）。左右独立フラグは載せない。
- **Rationale**: 典型 1〜2 ペアは常に 1 message で完結し、受信側 parse が「引数を 2 個ずつ読む」だけで済む。per-id message 案は空集合と「1 message 欠落」の区別など受信側の完了判定が曖昧になる（flat pairs + timestamp accumulate なら heartbeat と同一の完了モデル）。左右独立性は ARKit_8BS では形式自体が運び、VRChat_XY では実現不能なため載せる意味がない（載せないことが S1-3 のプロトコル安定にも寄与）。
- **Trade-offs**: 将来第 3 の形式が「形式名 + 追加パラメータ」を要する場合はペア→トリプル拡張が必要になるが、未知形式 skip 規約（Req 2.6）で前方互換の逃げ道は確保済み。
- **Follow-up**: E2E テストで「2 id 広告」「未知 format 混在」を検証。

### Decision 2: `Gaze_VRChat_XY` + `leftRightIndependent` は警告に留める（Open Question 2 / Req 7.3）
- **Context**: 見かけ倒し組み合わせを警告か禁止か。
- **Alternatives Considered**:
  1. 警告（Inspector HelpBox + runtime LogWarning 1 回）。
  2. 禁止（validation エラー化・entry skip）。
- **Selected Approach**: 案 1（警告）。Drawer の既存 warning HelpBox（`MappingWarningName`）を拡張し、runtime は route 構築時に 1 度だけ LogWarning。entry 自体は従来どおり動作させる（左右同値配布）。
- **Rationale**: 禁止（skip）にすると現在動いている既存アセット（左右同値でも `.left`/`.right` 登録により side-pair 規約解決が成立している構成）が無警告時代の資産ごと突然沈黙し、Req 3.4（既存アセット無再設定動作）と衝突する。挙動自体は定義済み（同値配布）で害は「誤解」のみのため、誤解の解消は警告で足りる。
- **Trade-offs**: 警告を見ないユーザーには誤解が残り得る。Spec 2 の Inspector 刷新で UI 構造ごと解消される見込み。

### Decision 3: 先読み Subscribe は候補 id 全合成方式（Open Question 3 / Req 6.2）
- **Context**: 未解決 gaze source id の購読予約方式。
- **Alternatives Considered**:
  1. `ctx.AdapterBindings` の slug 一覧 × GazeConfigs から候補 id `{slug}:{expressionId}` / `.left` / `.right` を全合成して Subscribe。
  2. `IInputSourceRegistry` に registry 全体の登録通知 API を追加（Domain 契約変更）。
- **Selected Approach**: 案 1。`FacialController` が binding 構成時に slug 一覧を保持し、`SubscribeGazeInputSources` で全候補 id（slug 数 × GazeConfig 数 × 3）を Subscribe する。id 合成は `GazeBindingConfigResolver` に新設する合成 helper に局所化する（Spec 2 の `GazeSourceIdConvention` への集約先）。
- **Rationale**: Domain 契約変更（案 2）は全実装 + テスト Fake への波及と「全登録イベント」の通知コスト設計が必要で、Spec 2 で identity 規約自体が `"gaze"` 定数化される前提では過剰投資。候補集合は有界（数十件オーダー）で、`Subscribe` は未登録 id に対して no-cost（後発 Register 時のみ発火）。`SetupGazeBoneProvider` は通知のたびに全 config を再解決するため、slug Ordinal 優先規則も自然に再評価される。
- **Trade-offs**: 規約外 id（Timeline の `gaze-0` 等）で登録される source は捕捉しないが、それらは現行の規約解決でも解決不能であり退行ではない。`useDistinctLeftRight=true` の明示 id は現行どおり直接 Subscribe。
- **Follow-up**: Req 6.3 の決定論テスト（Subscribe 後に Register する後発シナリオ）で検証。

### Decision 4: per-route staleness は導入しない（Open Question 4 / Req 2.8 注記）
- **Context**: staleness は binding 単位共有時刻のため「BlendShape 継続 + gaze のみ途絶」でフェイルセーフが発火しない。
- **Alternatives Considered**:
  1. 導入しない（現行の binding 単位 staleness を維持、gaze のみ途絶は最終値保持）。
  2. gaze route 単位の最終受信時刻を持ち per-route でフェイルセーフ発火。
- **Selected Approach**: 案 1（導入しない）。制限は mental-model / README に明記する。
- **Rationale**: 「送信側が生きたまま gaze だけ送出停止する」のは送信側 gaze 構成がランタイムで消えたケースに限られ、その場合広告も止まる（AC 1.5）が D-2 で route 温存が確定済み — つまり per-route staleness を入れても「温存した route が最終値を保持する」という D-2 の帰結と矛盾する動作（勝手にゼロ復帰）になり得る。また per-route 時刻管理は受信スレッド hot path（`TryHandleGazeMessage`）への書き込み追加であり、Req 9.2 の hot path 最小化方針に逆行する。実害が確認されたら backlog 化して実機データで判断する。
- **Trade-offs**: gaze のみ途絶時に目線が最終値で固まる（RevertToBase を選んでいても）。発生条件が限定的で、送信側全停止なら共有 staleness が従来どおり発火する。

### Decision 5: Req 4.2 警告は OscReceiverAdapterBinding に配置し、GazeConfig は既存リフレクション注入で受け取る
- **Context**: 「広告 id がどの GazeConfig とも一致しない」警告の配置。receiver は GazeConfigs を知らず、FacialController は広告内容を知らない。
- **Alternatives Considered**:
  1. FacialController 側 — receiver へ「広告由来 id 一覧」を問い合わせる新 API が必要（core → osc の逆向き知識）。
  2. Receiver 側 — `Configure(IReadOnlyList<GazeBindingConfig>)` を binding に追加し、`FacialController.FindGazeConfigureMethod` の既存契約（末尾引数型で探すリフレクション注入）で受信側 GazeConfigs を受け取る。
- **Selected Approach**: 案 2。receiver は広告再構築時に auto id と注入済み GazeConfig expressionId を Ordinal 突合し、不一致 id を 1 度だけ LogWarning（設定手順の手掛かり付き）。注入が無い（FacialController 不在・GazeConfigs 空）場合は空集合として扱い警告を出す（無警告沈黙の禁止）。
- **Rationale**: 広告内容と route 出自（manual/auto）を知るのは receiver のみ。注入機構は `OscSenderAdapterBinding` 同様に既存の `ConfigureAdapterBindingsWithGazeConfigs` が自動で拾うため core 側変更ゼロ。警告タイミングも再構築時（メインスレッド・低頻度）に自然に収まる。
- **Trade-offs**: リフレクション注入契約への依存が 1 箇所増える。Spec 2 のリフレクション解消（型付きインターフェース化）の置換対象リストに本 binding を追加する。

### Decision 6: `OscSender.SendBundle` は overload 追加ではなく heartbeat payload struct を導入
- **Context**: 既存 6 overload に広告引数 2 個を足すと 7 個目の 9 引数 overload になる。
- **Selected Approach**: `OscHeartbeatPayload` readonly struct（heartbeat 名 / preset / 広告ペアをまとめる）を新設し、`SendBundle(..., in OscHeartbeatPayload)` を 1 本追加。既存 overload は無改修で温存（後方互換）。`OscSenderAdapterBinding` の heartbeat 分岐は struct 経路へ移行。
- **Rationale**: 引数増殖の打ち止めと、heartbeat 系 metadata（names / preset / 広告）の凝集。readonly struct のため GC ゼロ。
- **Trade-offs**: overload が一時的に併存する。既存テストは無改修で緑を維持できる。

### Decision 7: gaze 読取共通化は FacialController 挙動へ寄せる（Req 8）
- **Context**: `GazeBonePoseProvider.TryReadInputXY` と `FacialController.TryReadGazeInput` の差分（null チェック有無 / clamp 位置。scalar → x=v, y=0 は両者一致）。
- **Selected Approach**: core に static helper `GazeInputReader.TryReadXY` を新設し、null チェックあり + clamp 内包 + scalar は x=v, y=0（コメントも実装に一致させる）。両呼び出し元を helper へ置換し、`GazeBonePoseProvider.Apply` の caller 側 clamp は冗長化するため削除。
- **Rationale**: null チェックは防御的に常に安全（現行 Provider は constructor で null source を弾いているため挙動変化なし）。clamp 内包は Provider 側で「clamp 後に読める」保証となり呼び出し側の重複を消せる。scalar セマンティクスは両者一致のため挙動選択の争点なし。
- **Follow-up**: Req 8.3 のテストで「scalar 入力 → (x=v, y=0)」「invalid → false + rest 復帰（Provider）/ snapshot 非生成（Controller）」の等価性を明示。

## Risks & Mitigations
- **手動 route と広告 route の Register 衝突**（LogError + 後勝ちで手動破壊）— 広告 plan 計算時に手動 gaze entry の expressionId を Ordinal 除外してから Register する（Req 3.2）。EditMode で resolver の除外を、PlayMode で共存を検証。
- **受信スレッドと route 再構築の race** — `_gazeRoutes` は immutable-swap（新辞書構築 → Volatile 参照差し替え）。gaze bundle state の lazy 初期化は swap より前に完了させる。
- **広告順序揺れによる誤再構築** — (id, format) ペアを id Ordinal 昇順に正規化してからハッシュ。
- **S-21 赤の解消確認漏れ** — ログ削除タスクに PlayMode 4 件の緑化確認を含める。
- **Spec 1 完了時点で GazeConfig の id 一致が手動のまま残る**（D-1 受容済み）— Req 4.2 警告文に設定手順の手掛かりを含め、README / mental-model を更新する。

## References
- `.kiro/multi-spec/gaze-control-overhaul.md` — G-1〜G-5 / S1-1〜S1-3 の決定と現状調査
- `.kiro/specs/osc-receiver-auto-mapping/design.md` — heartbeat auto-mapping の前例アーキテクチャ（accumulate + dirty + FNV-1a + Replace）
- `docs/backlog.md` M-25（gaze auto mapping、番号重複あり）/ S-21（gaze ログ由来 pre-existing 赤 4 件）
- OSC 1.0 仕様（bundle / string 引数の 4 byte アライン）— `OscBundleBuilder` 実装が準拠済みのため追加調査不要と判断
