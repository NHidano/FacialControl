# Requirements Document

## Project Description (Input)
osc-gaze-auto-mapping: OSC 送信側が /_facialcontrol/gaze 専用アドレスで gaze の id と形式（VRChat_XY / ARKit_8BS）を広告し、受信側が gaze route / GazeVector2InputSource を自動生成する（backlog M-25 の実行）。あわせて調査で発覚した gaze 経路の潜在バグ 4 件（Custom preset + gaze の未捕捉例外 / gaze source 後発登録レース / Gaze_VRChat_XY + leftRightIndependent の見かけ倒し / gaze 読取ロジック重複）を回収し、実機の未解決不具合「OSC 受信端末で gaze だけ動かない」を根治する。詳細な背景・決定済み事項・スコープ・未決事項は .kiro/multi-spec/gaze-control-overhaul.md の「全体コンテクスト」「共通の決定済み事項」「Spec: osc-gaze-auto-mapping」の各節に記載されており、requirements 生成・dig インタビュー・design の前提として必ず読み込むこと。

## Introduction

本 spec は、OSC 送信側（`OscSenderAdapterBinding` / `OscBundleBuilder`）が専用アドレス `/_facialcontrol/gaze` で gaze の id（expressionId 文字列）と形式（`VRChat_XY` / `ARKit_8BS`）を広告し、受信側（`OscReceiverAdapterBinding`）が gaze route と `GazeVector2InputSource` を自動生成・再構築する経路を定義する。`osc-receiver-auto-mapping` spec（BlendShape 自動マッピング本体）が Non-Goal として `docs/backlog.md` M-25 に先送りした gaze 自動マッピングの実行であり、multi-spec プラン `gaze-control-overhaul` の Spec 1 に当たる。

現状の gaze 受信は `OnStart` 固定・手動 `OscMappingEntry` 必須（`HasGazeMappings` が SerializeField `_mappings` のみを参照）であり、送受信マシン間で expressionId を Ordinal 完全一致させる手動突合が「OSC 受信端末で BlendShape は動くが gaze だけ動かない」実機不具合の構造的原因になっている。本 spec は広告駆動の自動生成でこれを根治するとともに、調査で発覚した gaze 経路の潜在バグ 4 件（送信側 Custom preset の未捕捉例外 / gaze source 後発登録レース / `Gaze_VRChat_XY` + `leftRightIndependent` の見かけ倒し / gaze 読取ロジック重複）を回収する。

プランの決定済み事項 G-4 / S1-1〜S1-3 を前提とし、identity モデルの刷新（規約 id `"gaze"` 化）は Spec 2（gaze-channel-redesign）に委ねる。広告は id を文字列で運ぶため、Spec 2 適用後もプロトコル無変更で本 spec の成果が生きる。プランで未決とされた事項は本ドキュメント末尾の「Open Questions」に列挙し、後続の dig インタビュー / design phase で確定する。

## Boundary Context

- **In scope**:
  - 送信側（`com.hidano.facialcontrol.osc/Runtime/Adapters/AdapterBindings/OscSenderAdapterBinding.cs` / `OscBundleBuilder.cs`）: heartbeat 送出タイミングに合わせた `/_facialcontrol/gaze` 広告メッセージの追加（gaze expressionId 文字列 + endpoint preset から確定した形式。複数 gaze id 対応）
  - 受信側（`OscReceiverAdapterBinding.cs`）: 広告受信を起点とする gaze route / `GazeVector2InputSource` の動的生成・再構築経路の新設（BlendShape の `PublishRuntimeMappings` に相当する gaze 版）。手動 `OscMappingEntry`（`Gaze_VRChat_XY` / `Gaze_ARKit_8BS`）は上書き用オプションとして存続
  - gaze 経路の潜在バグ 4 件の回収（Requirement 5〜8）
  - `OscReceiverDemo` サンプルのハイブリッド構成解消（手入力 gaze mapping 1 件の削除、README 更新）
  - ドキュメント修正: `docs/backlog.md` の M-25 番号重複解消と gaze M-25 のクローズ、`docs/mental-model.md` の gaze 受信記述更新
  - テスト: 広告→自動生成の E2E（`OscGazeE2ETests` 拡張）、後発登録・staleness・手動上書き共存の検証
- **Out of scope**:
  - identity モデルの変更（規約 id `"gaze"` 化、isGaze ダミー Expression の廃止）→ Spec 2（gaze-channel-redesign）
  - 受信側 GazeConfig（目ボーン path）の自動生成 UX 改善 → Spec 2
  - BlendShape gaze の runtime 配線（backlog M-29）
  - multi-source gaze blending（backlog M-13）
  - uOsc 差し替え等のプロトコル基盤変更（backlog M-16）
  - Inspector への Manual/Auto 出自 badge UI（backlog M-26。本 spec は診断ログでの出自識別まで）
- **Adjacent expectations**:
  - `osc-receiver-auto-mapping` spec が確立した heartbeat（`/_facialcontrol/blendshape_names`）/ preset（`/_facialcontrol/preset`）経路は**一切変更しない**。gaze 広告は独立した新設アドレスとして共存する
  - Spec 2 へ引き渡すもの: `/_facialcontrol/gaze` 広告プロトコル（アドレス・ペイロード仕様）。Spec 2 では広告される id が規約定数 `"gaze"` になるだけでプロトコルは変わらない（S1-3）
  - 受信側の gaze source id 合成（`{slug}:{expressionId}` / `.left` / `.right`）は、Spec 2 の `GazeSourceIdConvention` ヘルパー化を見越して実装箇所を局所化する
  - `IInputSourceRegistry` の登録 slug `osc` は予約のまま維持し、新 slug を導入しない
  - Domain 層の Unity 非依存契約を維持し、広告駆動の自動生成ロジックは Adapters 層に閉じ込める

## Open Questions and Decisions (Dig)

2026-08-08 の dig インタビューで確定した決定。各 AC からは `(see D-x)` で参照する。

| ID | 論点 | 決定 | 理由 | リスク |
|----|------|------|------|--------|
| D-1 | 広告 id と受信側 GazeConfig の突合範囲（旧 Open Question 1） | **案 (c): Spec 1 は route / source の自動生成まで。GazeConfig 突合の自動解決（既定フォールバック・自動補完）は行わず、不一致は警告で通知する** | 変更最小・設計が単純。既定フォールバック（案 a）の暫定ルールを作っても Spec 2 の規約 id `"gaze"` 化で不要になる。完全な「手入力ゼロ→目ボーン反映」は Spec 2 完了時に成立させる | 中: Spec 1 完了時点では受信側 GazeConfig の expressionId を広告 id に手動で一致させる作業が残る（警告 + README で案内） |
| D-2 | 広告の送出周期と staleness（旧 Open Question 3） | **heartbeat と同周期（既定 5 秒）で周期送出。広告が途絶えても生成済み route / source は破棄せず温存し、値の staleness は既存の `stalenessSeconds` フェイルセーフ（RevertToBase / HoldLastValue）に委ねる** | UDP・コネクションレスのため受信側後起動でも次の広告で疎通する周期送出が必須。route 破棄→再購読の連鎖を避け、送信側消失の実害は既存フェイルセーフが吸収する | 中: 送信側の gaze 構成が消えた場合に古い route が残る。staleness は binding 単位共有のため「BlendShape 継続 + gaze のみ途絶」ではフェイルセーフが発火せず最終値保持となる（per-route staleness の要否は Open Question 4 で design 判断） |
| D-3 | Fork への反映を完了条件に含めるか（旧 Open Question 5） | **含めない。spec は本リポジトリ内の実装 + テストで完結し、Fork 反映・version bump・publish・実機検証は従来どおり別途の運用フローで行う** | 別リポジトリ・レジストリ操作が spec タスクに混ざるとバッチ実行（spec-run）で完結できない | 低: 実機での根治確認は spec 外のフォローアップとして明示的に管理する |

## Requirements

### Requirement 1: 送信側 gaze 広告の送出（/_facialcontrol/gaze）
**Objective:** As a OSC 送信側を構成する Unity エンジニア, I want 送信側が gaze の id と形式を専用アドレスで自動的に広告してほしい, so that 受信側が expressionId の手動突合なしに gaze 受信を構成できる。

#### Acceptance Criteria
1. When `OscSenderAdapterBinding` が heartbeat（`/_facialcontrol/blendshape_names`）を送出するタイミングになったとき、The OscSenderAdapterBinding shall 専用アドレス `/_facialcontrol/gaze` へ gaze 広告メッセージを併せて送出する（heartbeat と同周期の周期送出。see D-2）。
2. The OscSenderAdapterBinding shall 広告 payload に gaze の expressionId（文字列）と形式識別子（`VRChat_XY` / `ARKit_8BS`）を含める。id は文字列として運び、Spec 2 で id が規約定数 `"gaze"` に変わってもプロトコル変更を要しない構造とする（S1-3）。
3. When 広告に載せる形式識別子を決定するとき、The OscSenderAdapterBinding shall 送信側の endpoint preset から確定した値（VRChat preset → `VRChat_XY`、ARKit preset → `ARKit_8BS`）を使用し、受信側での形式推定を前提としない（S1-1: `/ARKit/eyeLook*` は通常 BlendShape としても正当なため受信側単独では判別不能）。
4. While 送信側に複数の gaze expressionId が構成されているとき、The OscSenderAdapterBinding shall すべての gaze id を広告対象に含める。
5. If 送信側に gaze の送出構成が 1 件も存在しない場合, the OscSenderAdapterBinding shall `/_facialcontrol/gaze` メッセージを送出しない。
6. When 本 spec 改修前の旧 receiver が `/_facialcontrol/gaze` メッセージを受信したとき、The 旧 receiver shall ハンドラ未登録の未知アドレスとして読み飛ばし、警告も致命エラーも発生させない（`/_facialcontrol/preset` 追加時と同じ後方互換方式）。

### Requirement 2: 受信側の広告駆動 gaze route / GazeVector2InputSource 自動生成
**Objective:** As a OSC 受信側を構成する Unity エンジニア, I want 広告の受信を起点に gaze route と `GazeVector2InputSource` が自動生成・再構築されてほしい, so that 受信側で `OscMappingEntry` を手入力しなくても gaze データを受信できる。

#### Acceptance Criteria
1. When `OscReceiverAdapterBinding` が `/_facialcontrol/gaze` 広告を受信したとき、The OscReceiverAdapterBinding shall 広告に含まれる id と形式識別子に基づき gaze route（`VRChat_XY`: Vector2 相当 2 メッセージ / `ARKit_8BS`: eyeLook 8 BlendShape）を動的生成する。
2. When 広告起点で gaze route を生成したとき、The OscReceiverAdapterBinding shall `GazeVector2InputSource` を生成し、現行規約 id `{slug}:{expressionId}`（左右独立構成時は `.left` / `.right`）で `IInputSourceRegistry` に登録する。
3. The OscReceiverAdapterBinding shall gaze route の構成を `OnStart` 時点の SerializeField `_mappings` の内容に固定せず、起動後の広告受信によって route / source を追加・再構築できる（現行 `HasGazeMappings(_mappings)` 判定による OnStart 固定の制約を解消する）。
4. When 後続の広告で内容（id 集合または形式）の変化を検出したとき、The OscReceiverAdapterBinding shall gaze route / source を最新の広告内容に合わせて再構築する。
5. While 広告内容が前回受信時と同一であるとき、The OscReceiverAdapterBinding shall gaze route / source を再生成せず既存インスタンスを再利用する。
6. If 広告に未知の形式識別子が含まれる場合, the OscReceiverAdapterBinding shall 当該 id の route 生成をスキップし、`Debug.LogWarning` で未知形式を 1 度だけ通知する。
7. The OscReceiverAdapterBinding shall 広告未受信かつ手動 gaze mapping も無い状態では gaze route / source を生成せず、`Debug.LogError` を出力しない。
8. While 広告の受信が途絶えたとき（送信側の停止・ネットワーク断など）、The OscReceiverAdapterBinding shall 生成済みの gaze route / source を破棄せず温存し、値のフェイルセーフは既存の `stalenessSeconds` 機構（RevertToBase / HoldLastValue）に委ねる（see D-2）。なお送信側の gaze 構成が N 件→0 件になった場合は広告自体が送出されなくなり（AC 1.5）「内容変化」（AC 2.4）として検出されないため、既存 route は本 AC の温存方針に従い残る。また staleness は binding 単位の共有時刻（gaze / BlendShape どちらの受理でも更新）であるため、BlendShape が流れ続けたまま gaze だけ途絶えた場合はフェイルセーフが発火せず最終値保持となる（per-route staleness の要否は Open Question 4 として design で判断）。
9. When 広告駆動生成の導入により現行の診断ログ「gaze mapping が未設定のため Gaze 受信は無効です（heartbeat auto-map は gaze route を生成しません）」（`StartReceiverPhase`）の前提が虚偽になるため、The OscReceiverAdapterBinding shall 同ログを削除または実態に合わせて改稿する。この変更に伴い backlog S-21（同ログの LogAssert 未追従による pre-existing 赤 4 件: `OscHeartbeatConsistencyTests` ×1 / `OscReceiverAdapterBindingAutoMappingIntegrationTests` ×2 / `OscReceiverGCAllocationTests` ×1）の解消確認と backlog 追従をスコープに含める。

### Requirement 3: 手動 gaze mapping との共存（上書きオプション・後方互換）
**Objective:** As a 既存 OSC 構成 / 外部 OSC ソース利用者, I want 手動 `OscMappingEntry` による gaze 設定が上書き用オプションとして引き続き機能してほしい, so that 広告が来ない外部 OSC ソース（VRChat 本体等）からの受信や既存プロジェクトの運用が壊れない。

#### Acceptance Criteria
1. When `OscReceiverAdapterBinding.OnStart` 時点で手動の gaze entry（`Gaze_VRChat_XY` / `Gaze_ARKit_8BS`）が `Mappings` に存在するとき、The OscReceiverAdapterBinding shall 現行バージョンと同様に手動 entry から gaze route を構成する（既存挙動の維持）。
2. While 手動 gaze entry と広告由来 route が共存しているとき、The OscReceiverAdapterBinding shall 同一 expressionId について手動 entry の構成を優先し、広告由来の値で上書きしない（S1-2: 手動 mapping は上書き用オプション）。
3. If 送信側が FacialControl でなく `/_facialcontrol/gaze` 広告が一切受信されない場合, the OscReceiverAdapterBinding shall 手動 gaze mapping のみで従来どおり gaze を受信する。
4. The OscReceiverAdapterBinding shall `OscMappingEntry` のシリアライズ済みフィールドを破壊的変更せず、既存 `FacialAdapterBindingCollectionSO` アセットを再設定なしで動作させる（広告由来 route は非シリアライズの runtime 状態として保持する）。
5. The OscReceiverAdapterBinding shall 手動由来 / 広告由来の gaze route を診断ログ（`Debug.Log` / `Debug.LogWarning`）から識別可能にする（Inspector への Manual/Auto badge 表示は backlog M-26 のスコープとし、本 spec ではログまで。Open Question 4 参照）。

### Requirement 4: 広告 id と受信側 GazeConfig の突合（無警告沈黙の禁止）
**Objective:** As a 受信側で目ボーンを動かしたい Unity エンジニア, I want 広告由来の gaze source が受信側 GazeConfig と突合できたかを確実に把握したい, so that 「無警告で gaze だけ動かない」実機不具合のクラスを再発させない。

#### Acceptance Criteria
1. When 広告 id と Ordinal 一致する expressionId を持つ GazeConfig が受信側に存在するとき、The FacialController shall 追加の手動設定なしで当該 gaze source を目ボーン適用（`GazeBonePoseProvider`）へ接続する。
2. If 広告由来で生成した gaze source の expressionId が受信側のどの GazeConfig とも一致しない場合, the 受信側（OscReceiverAdapterBinding または FacialController） shall 不一致 id と設定手順の手掛かりを `Debug.LogWarning` で 1 度だけ通知する（現行の無警告破棄を禁止する）。
3. The 本 spec shall GazeConfig 突合の自動解決（既定フォールバック・GazeConfig 自動補完）を行わない（D-1 で案 (c) に確定）。受信側で目ボーンまで反映させるには GazeConfig の expressionId を広告 id と一致させる手動設定が引き続き必要であり、その旨を AC 4.2 の警告および README（Requirement 10.2）で案内する。完全自動化は Spec 2（gaze-channel-redesign）の規約 id `"gaze"` 化で成立させる。

### Requirement 5: 潜在バグ回収 (1) — Custom preset + gaze の送信側未捕捉例外
**Objective:** As a Custom preset を含むマルチ endpoint 構成の送信側構成者, I want Custom preset の gaze 構成が binding 全体を無効化しないでほしい, so that 他の endpoint（VRChat / ARKit preset）への送出と heartbeat が継続稼働する。

#### Acceptance Criteria
1. If endpoint preset が Custom かつ gaze の送出構成が存在する場合, the OscSenderAdapterBinding shall `OscAddressFormatter.GetGazePrefix` の `NotSupportedException` を binding 内で捕捉し（現状は gaze 経路のみ catch が無く、ホスト側 `AdapterBindingHost.InvokeOnStartOnce` の catch に到達して `Debug.LogError` + **binding 全体が skip され、他 endpoint・heartbeat 含め全停止**する）、`Debug.LogWarning` を出力して当該 endpoint の gaze 送出のみをスキップし、他の endpoint の送出処理（BlendShape / heartbeat / preset）と binding の起動を継続する（BlendShape 側の既存 catch 挙動と統一）。
2. While endpoint preset が Custom であるとき、The OscSenderAdapterBinding shall 形式識別子を確定できないため `/_facialcontrol/gaze` 広告を送出せず、その旨を `Debug.LogWarning` で 1 度だけ通知する。

### Requirement 6: 潜在バグ回収 (2) — gaze source 後発登録レースの解消
**Objective:** As a iFacialMocap 等の後発登録 binding の利用者, I want gaze source が接続確立後に登録されても目ボーンが動いてほしい, so that binding の初期化順序に依存せず gaze が安定して反映される。

#### Acceptance Criteria
1. When `FacialController` の gaze 購読処理（`SubscribeGazeInputSources`）実行後に gaze source が `IInputSourceRegistry` へ登録されたとき、The FacialController shall 当該 source を購読対象に加え、目ボーン適用へ反映する。
2. The FacialController shall GazeConfig から解決される gaze source id について、購読処理時点で未解決（未登録）の id も購読予約できる仕組み（先読み Subscribe または登録通知の購読）を備える。
3. The 本 spec shall 「接続確立後に source 登録される binding（iFacialMocap 等）で目ボーンが動く」ことを検証するテスト（決定論的な後発登録シナリオ）を提供する。

### Requirement 7: 潜在バグ回収 (3) — Gaze_VRChat_XY + leftRightIndependent の見かけ倒し解消
**Objective:** As a 受信側 mapping を設定する Unity エンジニア, I want `Gaze_VRChat_XY` で左右独立が成立しないことを設定時点で知りたい, so that 「UI 上は左右独立に見えるが実際は左右同値」という誤解を防げる。

#### Acceptance Criteria
1. While `OscMappingEntry` が `Gaze_VRChat_XY` かつ `leftRightIndependent=true` の組み合わせで構成されているとき、The OscReceiverAdapterBinding の Inspector Drawer shall 「VRChat_XY 形式は単一 Vector2 のみを運ぶため左右には同値が配られる」旨の警告を表示する。
2. If 当該組み合わせのまま runtime が起動した場合, the OscReceiverAdapterBinding shall `Debug.LogWarning` で 1 度だけ同旨を通知する。
3. The design phase shall 警告表示に留めるか組み合わせ自体を禁止（validation エラー化）するかを確定する（プラン上は両案とも許容）。

### Requirement 8: 潜在バグ回収 (4) — gaze 読取ロジックの共通化
**Objective:** As a コアパッケージの保守担当, I want gaze 入力の読取ロジックを単一実装に共通化したい, so that `GazeBonePoseProvider` と `FacialController` で scalar 時挙動が食い違う不整合を解消できる。

#### Acceptance Criteria
1. The core（com.hidano.facialcontrol） shall gaze 入力読取ロジック（Vector2 読取および scalar フォールバックの解釈）を単一の共通実装に集約し、`GazeBonePoseProvider.TryReadInputXY` と `FacialController.TryReadGazeInput` の重複実装を解消する。
2. When 同一の入力源・同一の入力値を `GazeBonePoseProvider` 経由と `FacialController` 経由で読み取ったとき、The core shall 同一の読取結果を返す（scalar 時の挙動差異の解消）。
3. The 共通化 shall 既存の gaze 読取テストを緑のまま維持し、挙動差異があった箇所は「どちらの挙動に寄せたか」をテストで明示する。

### Requirement 9: Runtime 中の再構築と GC スパイク回避
**Objective:** As a パフォーマンス担当, I want 広告駆動の gaze route 再構築が毎フレームのヒープ確保ゼロ目標を破らないでほしい, so that BlendShape auto mapping で確立した GC ゼロ運用を gaze でも維持できる。

#### Acceptance Criteria
1. The OscReceiverAdapterBinding shall gaze 広告の内容変化検出を GC ゼロの順序依存安定ハッシュ（FNV-1a 等、BlendShape auto mapping と同方式）で行い、変化検出時のみ再構築を実行する。
2. When 受信スレッドのコールバックで gaze 広告を受け取ったとき、The OscReceiverAdapterBinding shall hot path で managed heap 確保を伴う処理を行わず、到着の記録のみ行い、route / source の再構築はメインスレッド（`OnFixedTick` 等）で実行する。
3. While 広告内容が変化していないとき、The OscReceiverAdapterBinding shall 毎フレーム処理で gaze 経路由来の新規アロケーションを発生させない。
4. The GazeVector2InputSource shall 広告由来生成後も値読取 1 回あたり managed heap 確保 0 byte を維持する。
5. The OscSenderAdapterBinding shall gaze 広告の組み立て・送出を「毎フレームのヒープ確保ゼロ目標」に反しない頻度・実装（バッファ再利用等）で行う。

### Requirement 10: サンプルとドキュメントの整備
**Objective:** As a サンプル利用者 / プロジェクトドキュメントの読者, I want サンプルとドキュメントが gaze 自動マッピング後の姿に更新されてほしい, so that ハイブリッド構成（手入力 gaze 1 件残し）や旧記述に惑わされない。

#### Acceptance Criteria
1. The OscReceiverDemo サンプル shall `OscReceiverDemoProfile.asset` の手入力 gaze mapping（`Gaze_VRChat_XY` 1 件）を削除し、BlendShape / gaze とも自動マッピングで疎通する構成で配布する（ハイブリッド構成の解消）。
2. The OscReceiverDemo / OscOutputDemo の README shall gaze 自動マッピングの動作条件（`/_facialcontrol/gaze` 広告が前提であること、FacialControl 以外の外部 OSC ソースから受信する場合は手動 mapping を使うこと）を説明する内容に更新する。あわせて `Samples~/OscReceiverDemo/README.md` のトラブルシュート節が参照している「gaze mapping 未設定」ログ文言（Requirement 2.9 で削除・改稿対象）の記述も追従させる。
3. The docs/backlog.md shall M-25 の番号重複（「表情 active 取得の系1/系2 二重化解消」と「Gaze の auto mapping 化」の 2 件が同番号）を解消し、一方に新番号を付番する。
4. When 本 spec の実装が完了したとき、The docs/backlog.md shall gaze auto mapping のエントリ（現 M-25）を backlog 運用ルールに従いクローズ（ブロック削除 + commit message に理由記載）する。
5. The docs/mental-model.md shall gaze 受信の記述（手動 mapping 必須・OnStart 固定の前提）を広告駆動自動生成後の挙動へ更新する。

### Requirement 11: 受け入れ条件 — 受信側 gaze mapping 手入力ゼロでの E2E 疎通
**Objective:** As a 実機検証担当, I want 受信側の gaze `OscMappingEntry` を手入力ゼロにしたまま送信側からの gaze が反映されることを再現確認したい, so that 実機の未解決不具合「OSC 受信端末で gaze だけ動かない」が根治されたことを誰でも検証できる。

#### Acceptance Criteria
1. When 受信側を gaze の `OscMappingEntry` 手入力ゼロで起動し、送信側（VRChat preset）が gaze を送信したとき、The 受信側 shall `/_facialcontrol/gaze` 広告の受信後に `VRChat_XY` route 経由で gaze 値を受信し `GazeVector2InputSource` へ反映する。
2. When 受信側を同条件で起動し、送信側（ARKit preset）が gaze を送信したとき、The 受信側 shall `ARKit_8BS`（eyeLook 8 BlendShape）route 経由で gaze 値を受信・反映する。
3. The 本 spec shall 広告→自動生成→値反映の E2E を検証するテスト（`OscGazeE2ETests` 拡張）を提供し、後発登録（Requirement 6）・広告途絶時の route 温存（Requirement 2.8, D-2）・手動上書き共存（Requirement 3）の各シナリオを含める。
4. The E2E テスト shall 広告の実送出周期の待ち時間を発生させない決定論的方式（ハンドラ直接呼び出し等、既存 heartbeat テストと同方式）を採用する。
5. The 本 spec shall 「受信側の gaze `OscMappingEntry` 手入力ゼロで、広告駆動により gaze 値が `GazeVector2InputSource`（registry）まで自動反映される」ことを最終受け入れ条件とする（D-1 の案 (c) に基づく）。目ボーンへの反映は受信側 GazeConfig の expressionId が広告 id と一致している場合に成立し（Requirement 4.1）、一致していない場合は警告で案内する（Requirement 4.2）。「GazeConfig 設定も含めた完全な手入力ゼロ」は Spec 2 完了時の受け入れ条件とする。

## Open Questions（design phase で確定する未決事項）

dig インタビューで確定済みの旧 Open Question 1 / 3 / 5 は「Open Questions and Decisions (Dig)」の D-1 / D-2 / D-3 を参照。design phase に残る未決事項は以下の 4 件（3 / 4 は validate-gap で追加）。

1. **広告ペイロードの詳細形式**: 左右独立性の情報を広告に載せるか（`ARKit_8BS` は形式自体が左右を運ぶため不要の可能性が高い）/ 1 メッセージに複数 id を詰めるか id ごとに 1 メッセージか / MTU との関係。heartbeat の chunk 分割方式（`GetFittingStringChunkCount`）を前例として design で確定する。
2. **`Gaze_VRChat_XY` + `leftRightIndependent` の扱い**: 警告表示に留めるか組み合わせ自体を禁止（validation エラー化）するか（Requirement 7.3）。
3. **先読み Subscribe の実現方式**（Requirement 6.2）: `IInputSourceRegistry.Subscribe` は未登録 id にも張れる（後発 Register で通知される契約）が per-id 購読のみで、全登録イベントの購読 API は存在しない。規約解決の候補 id `{slug}:{expressionId}[.left/.right]` は提供 slug が事前に分からないと合成できないため、(a) `ctx.AdapterBindings` の slug 一覧から候補 id を全合成して購読する / (b) registry に登録通知 API を追加する（Domain 契約変更を伴う）のいずれかを design で確定する。
4. **per-route staleness の要否**（Requirement 2.8 注記）: staleness は binding 単位の共有時刻のため、BlendShape 継続 + gaze のみ途絶ではフェイルセーフが発火せず最終値保持となる。gaze route 単位の staleness を導入するかを design で判断する。

## Dig Summary

- **実施日**: 2026-08-08 / ラウンド数: 1 / 質問数: 3 / 決定数: 3（D-1〜D-3）
- **主要な発見**:
  1. GazeConfig 突合は自動化せず案 (c) を採用（D-1）。これにより本 spec の最終受け入れ条件は「mapping 手入力ゼロで registry まで自動疎通」に確定し、「GazeConfig 含む完全手入力ゼロ」は Spec 2 の受け入れ条件へ移った（Requirement 4.3 / 11.5 を更新）
  2. 広告は周期送出 + route 温存が確定（D-2）。受信側の広告 staleness による route 破棄は行わず、既存 `stalenessSeconds` フェイルセーフに委ねる（Requirement 2.8 を追加）
  3. Fork 反映・実機検証は spec 完了条件外（D-3）。実機での根治確認は spec 完了後のフォローアップとして別途管理する
- **残リスク（design へ引き継ぎ）**:
  - 広告ペイロードの MTU 超過時の分割方式（Open Question 1）
  - 手動 entry と広告由来 route の優先規則の実装詳細（同一 expressionId の判定タイミング）
  - Spec 1 完了時点では受信側 GazeConfig の id 一致が手動のまま残ること（ユーザー向け案内の質が実機での混乱を左右する）
