# Requirements Document

## Project Description (Input)
OscReceiverAdapterBinding の Mappings 列挙を、送信側 heartbeat (/_facialcontrol/blendshape_names) と受信側モデルの BlendShape 一覧から自動生成する。現状 Mappings は Inspector で手入力必須（mode=blendShape, expressionId, addressPattern を 1 件ずつ）で、200 個の BlendShape を持つモデルでは現実的に運用できない。本 spec では (a) heartbeat 受信時に runtime mapping を動的拡張する経路を追加し、(b) Mappings が空 / 部分入力 / 完全手入力の 3 ケースを統一的に扱い、(c) アドレスプリセット（VRChat 形式 /avatar/parameters/{name} と ARKit 形式 /ARKit/{name}）の推定方法を確立する。スコープ: OscReceiverAdapterBinding、OscInputSource、HeartbeatConsistencyChecker、heartbeat メッセージフォーマット（preset 情報の追加可否含む）、Gaze は対象外（heartbeat に Gaze id を含まないため別途検討）。非機能: runtime 中の mapping 拡張で GC スパイクを起こさない、既存の手動 Mappings との後方互換維持。受け入れ条件は OscReceiverDemo の Mappings を空にしても OscOutputDemo からの送信が受信側モデルの BlendShape へ反映されること。

## Introduction
本 spec は `OscReceiverAdapterBinding` の `Mappings` 列挙を、送信側 heartbeat (`/_facialcontrol/blendshape_names`) と受信側モデルの BlendShape 名一覧から自動生成する仕組みを定義する。現状の手入力運用は 200 シェイプ規模のモデルでは破綻するため、heartbeat 受信を起点として runtime に mapping を動的拡張する経路を追加する。同時にアドレスプリセット (VRChat 形式 `/avatar/parameters/{name}` と ARKit 形式 `/ARKit/{name}`) の推定方法を確立し、`Mappings` が空 / 部分入力 / 完全手入力の 3 ケースを統一フローで扱う。Gaze は heartbeat に id を含まないため対象外とし、既存の手動 mapping との後方互換を維持する。

## Boundary Context

- **In scope**:
  - `OscReceiverAdapterBinding` の Normal_BlendShape mapping 自動生成 (heartbeat 駆動)
  - `HeartbeatConsistencyChecker` の責務スリム化 (mismatch warning 専用化、SkipMask 廃止)
  - `OscInputSource` の mapping 拡張対応 (runtime 中の mapping 件数増加と内部バッファ再構築、`OscDoubleBuffer.Resize` の pending list 化)
  - アドレスプリセット推定 (VRChat `/avatar/parameters/{name}` / ARKit `/ARKit/{name}` / カスタム任意 prefix)
  - heartbeat とは別の独立 OSC address `/_facialcontrol/preset` での preset 情報送出 (preset 名 + custom prefix)。BlendShape 名 heartbeat (`/_facialcontrol/blendshape_names`) は一切変更しない
  - `IInputSourceRegistry` への `Replace(id, source)` API 追加 (LogDebug 出力)
  - `AddressPresetKind.Custom` enum 値追加 + `OscAddressFormatter` の custom prefix overload
  - `OscReceiverDemo` / `OscOutputDemo` サンプルの auto mapping 運用確認 (`OscReceiverDemoProfile.asset` から手入力 `Normal_BlendShape` を削除し、`Gaze_VRChat_XY` 1 件のみ残すハイブリッド構成にする)
- **Out of scope**:
  - Gaze (`Gaze_VRChat_XY` / `Gaze_ARKit_8BS`) の自動 mapping (heartbeat / preset address に Gaze id を含まないため別 spec)。本 spec 完了後の follow-up spec として「heartbeat または preset address に Gaze id を追加し Gaze も auto mapping 化する」提言を `docs/backlog.md` に登録する
  - 送信側 (`OscOutputAdapterBinding`) の自動 mapping 生成 (本 spec は受信側に閉じる)
  - heartbeat 以外の経路 (例: シーン外 JSON プリセット配信) からの mapping 投入
  - 表情 (Expression) id 体系の改廃
  - `OscRuntimeSettingsSO` のスキーマ破壊的変更
  - Inspector への「Manual / Auto」出自 badge UI (preview 段階は runtime 内部状態 + 診断ログのみ。Inspector UI 拡張は backlog)
- **Adjacent expectations / 連動改修**:
  - **送信側の preset address 送出改修 (本 spec スコープに含む)**: `OscBundleBuilder` に preset 専用 address (`/_facialcontrol/preset`) の送出経路を追加し、`OscSenderAdapterBinding` で preset 名 + (custom 時) custom prefix を送る。既存 `/_facialcontrol/blendshape_names` の `string[]` payload は無改修。旧 receiver は未知 address `/_facialcontrol/preset` を message filter / dispatcher が振り分けず単純に無視するため、警告も発生しない (sentinel 方式より後方互換がクリーン)。
  - `IInputSourceRegistry` への登録 slug `osc` は予約のまま維持し、本 spec で新 slug を導入しない
  - Domain 層は Unity 非依存契約を維持し、自動 mapping ロジックは Adapters 層に閉じ込める
  - 既存の手動 `OscMappingEntry` (mode=`Normal_BlendShape`) を含む `FacialAdapterBindingCollectionSO` の構成は無改修で動作する (後方互換)
  - 既存の Checker `SkipMask` 依存テスト (`HeartbeatConsistencyCheckerTests` / `OscInputSourceMaskTests` / `OscHeartbeatConsistencyTests` の SkipMask 検証部) は廃止に伴って再構成する

## Requirements

### Requirement 1: 空 Mappings での自動 BlendShape mapping 生成
**Objective:** As a Unity エンジニア (受信側統合担当), I want `OscReceiverAdapterBinding.Mappings` を空にしたまま受信を開始できるようにしたい, so that 200 シェイプ規模のモデルでも Inspector への手入力を避けて素早く疎通させられる。

#### Acceptance Criteria
1. When `OscReceiverAdapterBinding.OnStart` が実行されたとき、`Mappings` が空かつ `Mappings` 由来の Gaze entry も無い場合、The OscReceiverAdapterBinding shall heartbeat 待ちで OSC 受信を起動し、`OscInputSource` 登録を heartbeat 到着まで延期する。
2. When `OscReceiverAdapterBinding` が `BlendShapeNamesAddress` heartbeat メッセージを最初に受信したとき、The OscReceiverAdapterBinding shall heartbeat の BlendShape 名と `AdapterBuildContext.BlendShapeNames` (受信側モデル) の積集合から `Normal_BlendShape` runtime mapping を生成し、`OscInputSource` を `IInputSourceRegistry` に登録する。
3. When heartbeat 起点で runtime mapping が生成されたとき、The OscReceiverAdapterBinding shall 各 mapping の `addressPattern` を推定アドレスプリセット (Requirement 3) に従って組み立て、`expressionId` を BlendShape 名に一致させる。
4. While Mappings が空のまま heartbeat 未受信であるとき、The OscReceiverAdapterBinding shall `OscInputSource` を未登録のまま保持し、`Aggregator` に対して BlendShape 値を一切出力しない。
5. If 受信した heartbeat と受信側 mesh の BlendShape 名に共通要素が一つも存在しない場合, the OscReceiverAdapterBinding shall mapping を生成せず、`Debug.LogWarning` で「heartbeat と mesh BlendShape が一致しません」を 1 度だけ出力する。
6. The OscReceiverAdapterBinding shall heartbeat 駆動で生成された runtime mapping を、後続 heartbeat 受信時に再評価できる構造で保持する (Requirement 4 の差分更新前提)。

### Requirement 2: 手入力 Mappings / 部分入力 Mappings との統一フロー
**Objective:** As a Unity エンジニア, I want Mappings が空 / 部分入力 / 完全手入力のいずれでも同じ起動シーケンスで処理されるようにしたい, so that 既存プロジェクトを破壊せず段階的に自動化に移行できる。

#### Acceptance Criteria
1. When `OscReceiverAdapterBinding.OnStart` 時点で `Mappings` に `Normal_BlendShape` entry が 1 件以上含まれているとき、The OscReceiverAdapterBinding shall 手入力 entry を runtime mapping として優先採用し、`OscInputSource` を即時登録する (既存挙動を維持)。
2. When 手入力 `Normal_BlendShape` mapping が存在し、かつ後続で heartbeat が受信されたとき、The OscReceiverAdapterBinding shall heartbeat に含まれる BlendShape 名のうち手入力 entry でカバーされていないものを追加 runtime mapping として登録する (部分入力ケース)。
3. While 手入力 mapping と heartbeat 由来 mapping が共存しているとき、The OscReceiverAdapterBinding shall 同一 `expressionId` に対して手入力 entry の `addressPattern` を優先し、heartbeat 由来の推定値で上書きしない。
4. If `Mappings` に `Gaze_VRChat_XY` / `Gaze_ARKit_8BS` entry のみが含まれ `Normal_BlendShape` entry が含まれない場合, the OscReceiverAdapterBinding shall 既存の Gaze 結線を維持しつつ、heartbeat 受信時に自動 BlendShape mapping 生成 (Requirement 1) を実行する。
5. The OscReceiverAdapterBinding shall 手入力 entry / heartbeat 由来 entry を区別できる runtime 内部状態を保持し、診断ログ (`Debug.Log` / `Debug.LogWarning`) から出自を識別可能にする。Inspector UI への出自表示 (Manual/Auto badge 等) は preview 段階ではスコープ外とし、backlog に登録する。
6. The OscReceiverAdapterBinding shall `OscMappingEntry` のシリアライズ済みフィールドを破壊的変更せず、既存 `FacialAdapterBindingCollectionSO` アセットの再 import なしで起動可能とする (heartbeat 由来 mapping は `[NonSerialized]` の別コレクションに保持する)。

### Requirement 3: アドレスプリセット推定 (VRChat / ARKit / カスタム)
**Objective:** As a Unity エンジニア, I want heartbeat 起点の mapping 生成時に VRChat / ARKit / カスタム形式のいずれかから OSC アドレスを推定したい, so that VRChat 互換クライアントや ARKit 系トラッカからの送信を Inspector 手入力なしで受信できる。

#### Acceptance Criteria
1. The OscReceiverAdapterBinding shall アドレスプリセットとして以下の 3 種を識別する: VRChat 形式 (`/avatar/parameters/{name}`), ARKit 形式 (`/ARKit/{name}`), カスタム形式 (任意プレフィックス + `{name}`)。`AddressPresetKind` enum に `Custom` 値を追加する。
2. When プリセット種別を判定するとき、The OscReceiverAdapterBinding shall (a) `/_facialcontrol/preset` address で preset 名を受信していればそれを採用し (Requirement 5)、(b) 受信していなければ `/_facialcontrol/blendshape_names` の BlendShape 名と `ARKitDetector.ARKit52Names` (Domain) との一致率から推定する (一致率 ≥ 50% で ARKit、未満で VRChat)。
3. When プリセット種別が VRChat と判定されたとき、The OscReceiverAdapterBinding shall `OscAddressFormatter.FormatBlendShapeAddress(AddressPresetKind.VRChat, name)` を使い `/avatar/parameters/{name}` を `addressPattern` として割り当てる。
4. When プリセット種別が ARKit と判定されたとき、The OscReceiverAdapterBinding shall `ARKitDetector.ARKit52Names` に一致する BlendShape へ `/ARKit/{name}` を割り当て、一致しない名前は `/avatar/parameters/{name}` (VRChat フォールバック) を割り当てる。
5. When プリセット種別がカスタムと判定されたとき、The OscReceiverAdapterBinding shall `/_facialcontrol/preset` address で受信した custom prefix 文字列 (Requirement 5.4) を使い `OscAddressFormatter` の新 overload (`FormatBlendShapeAddress(string customPrefix, string name)`) でアドレスを組み立てる。custom prefix が欠落していれば VRChat 形式へフォールバックし `Debug.LogWarning` で fallback 採用を 1 度だけ通知する。
6. If 推定結果として同一 BlendShape 名に対して複数候補アドレスが生成された場合, the OscReceiverAdapterBinding shall (a) heartbeat payload に明示された preset を最優先し、(b) 明示が無い場合は VRChat 形式を採用し、衝突発生を `Debug.LogWarning` で 1 度だけ通知する。
7. The OscReceiverAdapterBinding shall ARKit 標準名一覧として `Hidano.FacialControl.Domain.Services.ARKitDetector.ARKit52Names` (52 名) を単一情報源として参照する (Adapters → Domain 依存は方向順)。

### Requirement 4: Runtime 中の mapping 拡張と GC スパイク回避
**Objective:** As a Unity エンジニア (パフォーマンス担当), I want heartbeat 受信時の mapping 動的拡張で毎フレーム GC アロケーションを増やさないようにしたい, so that FacialControl の「毎フレームのヒープ確保ゼロ目標」を破らずに自動 mapping を導入できる。

#### Acceptance Criteria
1. The OscReceiverAdapterBinding shall heartbeat 由来 mapping の生成・拡張・更新を毎フレームでは行わず、heartbeat 受信イベントもしくは内容変化を検出した時点でのみ実行する。heartbeat 内容変化検出は FNV-1a (または同等の順序依存安定ハッシュ) を用いて GC ゼロで判定する (`string.Join` / GetHashCode の安易な利用を禁止)。
2. While heartbeat 内容が前回と同一 (ハッシュ一致) であるとき、The OscReceiverAdapterBinding shall mapping 配列・`OscInputSource`・`OscDoubleBuffer` を新規確保せず、既存インスタンスを再利用する。
3. When heartbeat 内容が変化し runtime mapping の再構築が必要になったとき、The OscReceiverAdapterBinding shall mapping 配列確保・`OscDoubleBuffer` リサイズ・`OscInputSource` 差し替えをメインスレッドで実行し、受信スレッドの hot path でアロケーションを発生させない。受信スレッドのコールバックは「heartbeat 到着フラグを立てる」のみ行い、再構築は `OnFixedTick` で実行する。
4. The OscInputSource shall `TryWriteValues` 1 回あたりの managed heap 確保 0 byte を維持する (mapping 拡張後も `_mappingIndexToMeshIndex` 再構築はメインスレッドの再構築タイミングで完結させる)。
5. If heartbeat の BlendShape 名件数が `OscDoubleBuffer` の現在サイズを超える場合, the OscDoubleBuffer shall `Write` と `Resize` を同一 lock (`lock` / `Monitor`) で保護し、Resize 中の受信スレッド書き込みが旧 buffer の Dispose と競合しないことを保証する。lock は受信スレッド hot path に入るが、`Monitor` ベースのため managed heap 確保は発生しない (Req 4.4 の「0 byte 確保」を維持)。Resize は heartbeat 内容変化時のみ発火するため lock 競合による latency スパイクは実用上無視できる。
6. The OscReceiverAdapterBinding shall ContributeMask を runtime mapping 配列から binding 自身で生成し、`OscInputSource` の constructor に渡す。SkipMask は本 spec で廃止し、heartbeat 駆動で常に最新化される mapping のみが OscInputSource の出力範囲を決める。`HeartbeatConsistencyChecker` は sender/receiver 名前差分の mismatch warning 専用にスリム化する。
7. The IInputSourceRegistry shall `Replace(string id, IInputSource source)` API を提供する。`Replace` は既存登録を差し替え、ログレベルは `Debug.Log` (LogDebug 相当) とし、`Register` 重複時の `LogError` を発生させない。本 API は heartbeat 駆動の `OscInputSource` 差し替えで使用される。

### Requirement 5: Preset 情報の独立 address (`/_facialcontrol/preset`) 送出
**Objective:** As a プロトコル設計者, I want preset 識別子を BlendShape 名 heartbeat とは別の独立 OSC address で送りたい, so that 既存 `/_facialcontrol/blendshape_names` の payload を一切変更せず、受信側のプリセット推定を確定的かつ後方互換に行える。

#### Acceptance Criteria (採用方針: 別 address `/_facialcontrol/preset`)
1. The 送信側 (`OscBundleBuilder` および `OscSenderAdapterBinding`) shall preset 専用 address `/_facialcontrol/preset` に対し、preset 名 (`"vrchat"` / `"arkit"` / `"custom"`) を string で送出し、preset が `custom` の場合のみ続けて custom prefix 文字列 (例: `/myapp/`) を送る。`/_facialcontrol/blendshape_names` の `string[]` payload は一切変更しない。
2. When 既存の旧 receiver (本 spec 改修前) が `/_facialcontrol/preset` メッセージを受信したとき、The 旧 receiver shall message filter / dispatcher が当該 address にハンドラを持たないため単純に読み飛ばし、警告も致命エラーも発生させない。
3. When 本 spec 改修後の OscReceiverAdapterBinding が `/_facialcontrol/preset` メッセージを受信したとき、The OscReceiverAdapterBinding shall preset 名と (存在すれば) custom prefix を runtime 内部状態に保持し、次回 mapping 再構築時に Requirement 3 のアドレス推定へ反映する。`/_facialcontrol/preset` を一度も受信していなければ Requirement 3.2(b) の名前ベース推定にフォールバックする。
4. The OscReceiverAdapterBinding shall preset 名として `vrchat` / `arkit` / `custom` の 3 値を解釈できる。`custom` のときは続く string 要素を custom prefix 文字列として扱う。
5. If preset 名として未知の文字列を受信した場合, the OscReceiverAdapterBinding shall `Debug.LogWarning` で未知 preset を 1 度だけ通知し、Requirement 3.2(b) の名前ベース推定にフォールバックする。
6. The OscReceiverAdapterBinding shall preset address の受信結果を `OscRuntimeSettingsSO` を介さず runtime 内部状態として保持し、シリアライズ済みアセットの破壊的変更を伴わない。`/_facialcontrol/preset` は `BlendShapeNamesAddress` と同様に address 定数として単一箇所で定義する。
7. The OscSenderAdapterBinding shall preset address 送出を Inspector / JSON 設定で ON/OFF できるオプションを提供する (デフォルト ON)。OFF 時は `/_facialcontrol/preset` を送出せず、受信側は名前ベース推定にフォールバックする。
8. The OscReceiverAdapterBinding shall `/_facialcontrol/preset` の受信タイミングが BlendShape 名 heartbeat より前後どちらでも正しく動作するよう、preset 状態と mapping 生成を疎結合に保つ (preset 受信のみで mapping は生成せず、BlendShape 名 heartbeat 受信時に最新 preset 状態を参照する)。

### Requirement 6: 既存実装との後方互換と Gaze 結線維持
**Objective:** As a 既存 OSC サンプル / ユーザープロジェクトの保守担当, I want 自動 mapping 追加によって既存の手入力 mapping / Gaze 結線 / sender identity / staleness が壊れないことを保証したい, so that 段階的にバージョンアップしても運用が止まらない。

#### Acceptance Criteria
1. The OscReceiverAdapterBinding shall 既存の `OscMappingEntry` (mode=`Normal_BlendShape`) のみで構成された `Mappings` を持つ binding を、現行バージョンと同一の起動シーケンス・登録順序・mask 構成で動作させる。
2. The OscReceiverAdapterBinding shall 既存の Gaze entry (`Gaze_VRChat_XY` / `Gaze_ARKit_8BS`) の解釈・`GazeVector2InputSource` 登録・bundle accumulator 経路を改変しない。`GazeVector2InputSource` は `BlendShapeCount=0` / `ContributeMask=BitArray(0)` の ValueProvider 型であり、SkipMask 廃止 (Requirement 4.6) の影響を受けないことを design / テストで確認する。
3. The OscReceiverAdapterBinding shall `SenderIdentity` / `ZombieEvictionPolicy` / `OscBundleAccumulator` / `FailSafeMode` の挙動を本 spec の変更で退行させない (既存 PlayMode 統合テストが緑のまま維持されること)。
4. While heartbeat 由来の auto mapping が登録された状態であるとき、The HeartbeatConsistencyChecker shall sender / receiver の名前差分検出と `Debug.LogWarning` の 1 度だけログ機構を継続的に提供する (SkipMask は廃止するが mismatch 検出と warning は維持)。
5. If `OscRuntimeSettingsSO.ReceiverEnabled` が false の場合, the OscReceiverAdapterBinding shall heartbeat 受信も auto mapping 生成も実行せず、現行の「OSC Adapter は起動しません」warning を維持する。
6. The OscReceiverAdapterBinding shall `Dispose` 時に heartbeat 由来 runtime mapping / 拡張済み mask / 再確保したバッファを全て解放し、leak を残さない。

### Requirement 7: 受け入れ条件 — OscReceiverDemo の空 Mappings 疎通
**Objective:** As a プレリリース利用者 (サンプル動作確認担当), I want `OscReceiverDemo` の `Mappings` を空にしたまま `OscOutputDemo` からの送信が受信側モデルの BlendShape に反映されることを確認したい, so that 本 spec の主目的が達成されたことを誰でも再現確認できる。

#### Acceptance Criteria
1. When `OscReceiverDemo` Scene を `Mappings` 空の状態で起動し、`OscOutputDemo` Scene から VRChat 形式 (`/avatar/parameters/{name}`) で送信したとき、The OscReceiverDemo shall heartbeat 受信後に該当 BlendShape 名を持つ受信側 `SkinnedMeshRenderer` の weight を送信値で更新する。
2. When `OscReceiverDemo` Scene を `Mappings` 空の状態で起動し、`OscOutputDemo` 相当の ARKit 形式 (`/ARKit/{name}`) 送信を行ったとき、The OscReceiverDemo shall ARKit 標準名に一致する BlendShape weight を送信値で更新する。
3. While `OscReceiverDemo` が空 `Mappings` で受信中であるとき、The OscReceiverDemo shall PlayMode 内で `Debug.LogError` を出力せず、heartbeat 未受信フェーズでは BlendShape weight を初期値のまま保持する。
4. If 送信側が heartbeat を一度も送出しない場合, the OscReceiverDemo shall BlendShape weight を一切変更せず、`OscInputSource` を未登録のまま保持する。
5. The 本 spec shall 上記受け入れ条件を検証する PlayMode 統合テスト (例: `OscReceiverAdapterBindingAutoMappingIntegrationTests` 名称は design phase で確定) を提供する。テストは `HandleHeartbeat` を直接呼び出す決定論的パターン (既存 `OscHeartbeatConsistencyTests` と同方式) を採用し、heartbeat 5 秒間隔の待ち時間を発生させない。
6. The `OscReceiverDemoProfile.asset` shall 既存の手入力 `Normal_BlendShape` mapping (4 件) を全削除し、`Gaze_VRChat_XY` (1 件) のみ残すハイブリッド構成で配布する。これにより BlendShape は auto mapping、Gaze は手入力 mapping という preview 段階のデフォルト運用を示す。README は「Normal_BlendShape は heartbeat 駆動の auto mapping がデフォルト経路であり、Gaze は heartbeat に id を含まないため手入力 mapping を残している」旨を 1 段落以上で説明する。
7. The OscReceiverDemo / OscOutputDemo サンプル README shall auto mapping の動作条件 (heartbeat 受信が前提、`/_facialcontrol/preset` address での preset 指定の説明、custom prefix の指定方法、Gaze は別 spec で auto 化予定) を含む節を追加する。
8. The 本 spec shall Gaze の auto mapping 化 (heartbeat / preset address への Gaze id 追加) を follow-up spec として `docs/backlog.md` に登録し、本 spec では Gaze を手入力 mapping のまま維持する。
