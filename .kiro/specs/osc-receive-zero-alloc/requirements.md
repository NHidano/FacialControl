# Requirements Document

## Project Description (Input)
OSC 受信経路の zero-alloc 化（osc-receive-zero-alloc）。背景: OSC 受信側で Profiler の GC Used Memory が約 7 秒周期ののこぎり歯を描く。原因はコード解析上 uOSC 本体の受信パーサ（`Udp.Receive` のデータグラムごとの byte[]、`Reader.ParseString` のアドレス string、型タグ string + `Substring(1)`、`object[]` + float boxing）で、受信 1 フレーム（ARKit 52 本 + gaze + sender_id、VRChat preset、MTU 分割 2 パケット）あたり約 14 KB、60fps で約 0.85 MB/s と見積もられる。FacialControl 側の残件（AtomicSwap の `new List<BufferedValue>`、gaze/未マッピングアドレスでの `ExtractBlendShapeName` Substring）は約 5%。送信側は既に `OscSender.SendBundle` が自前 `OscBundleBuilder` + 生 `UdpClient` で送っており uOSC Writer/Client はホットパスにない。したがって backlog M-16（uOSC vendor copy + 送受信フォーク + facade 撤去）は過剰で、受信経路だけを `com.hidano.facialcontrol.osc` 内に zero-alloc 実装する。スコープ: (1) 自前受信ループ（`Socket.ReceiveFrom` を固定 byte[] リングへ受け、受信スレッド上で解析）、(2) Span ベースの packet reader（bundle/message 走査、型タグは byte 判定、float は BinaryPrimitives）、(3) アドレス解決を UTF-8 バイト列キーの lookup に変更（マッピング登録時に UTF-8 を事前生成、送信側 `OscAddressFormatter` の UTF-8 プールを流用。string 化は sender_id / heartbeat / preset / gaze 広告の制御メッセージのみ）、(4) `OscReceiverAdapterBinding.HandleIncomingOscMessage` を struct view 受けに変更し、既存 `OscReceiver.HandleOscMessage(uOSC.Message)` はテスト互換のため残して内部で view に変換、(5) AtomicSwap の List 再確保のプール化と gaze 経路 Substring の byte 比較化、(6) `OscReceiverGCAllocationTests` を実 UDP 経由かつ全スレッド計測（`CollectOnlyOnCurrentThread` を外す）に改め 0 byte を assert する形へ昇格。既存テストが `HandleOscMessage` 直呼びで uOSC を通らず本件を検出できなかった点の是正を含む。Non-goals: uOSC の vendor copy、送信側の改修、uOSC 互換 facade の撤去（M-16 Phase 11 相当は据え置き）。既存の受信機能（heartbeat 自動マッピング、sender_id / ZombieEviction、preset、gaze 広告、AtomicSwap / IndividualMessage、staleness / FailSafe、analog listener）は挙動を変えずに維持する。受入基準: 受信側 PlayMode GC テストが実 UDP 経由で 100 フレーム 0 byte（heartbeat 到着フレームを除く）、既存 OSC EditMode/PlayMode テストが緑（pre-existing 赤 4 件を除く）、検証プロジェクト OscSend シーンからの受信でのこぎり歯が消えることを実機確認。

## Introduction

本仕様は、`com.hidano.facialcontrol.osc` パッケージの OSC 受信経路を、定常状態で毎フレームのヒープ確保ゼロ（zero-alloc）となるよう再実装するための要件を定義する。

現状の受信経路は `uOSC.uOscServer`（ワーカースレッドで `Parser.Parse`）→ メインスレッド `Update` → `OscReceiver.HandleOscMessage(uOSC.Message)` → `OscReceiverAdapterBinding.HandleIncomingOscMessage` → `OscDoubleBuffer` / `OscBundleAccumulator` → analog listener 通知という構成であり、uOSC 本体の受信パーサがデータグラムごとの `byte[]`、アドレス `string`、型タグ `string` + `Substring(1)`、`object[]` + float boxing を毎パケット確保している。この確保が Profiler 上で約 7 秒周期の GC のこぎり歯として観測される。

本仕様では、uOSC を vendor copy することなく、受信ループ・パケット解析・アドレス解決・バインディング受け口を `com.hidano.facialcontrol.osc` 内に自前実装し、既存の受信機能の挙動を一切変えずに GC 確保を排除する。あわせて、既存の GC アロケーションテストが uOSC 経路を通らず本件を検出できなかった欠陥を是正し、実 UDP 経由・全スレッド計測で 0 byte を保証するテストへ昇格させる。

## Boundary Context

- **In scope**:
  - 自前 UDP 受信ループ（`Socket.ReceiveFrom` + 固定サイズ `byte[]` リングバッファ + 受信スレッド上での解析）
  - Span ベースの OSC packet reader（bundle / message 走査、型タグの byte 判定、float の `BinaryPrimitives` 読み出し）
  - UTF-8 バイト列キーによるアドレス解決（マッピング登録時の UTF-8 事前生成、送信側 `OscAddressFormatter` の UTF-8 プール流用）
  - `OscReceiverAdapterBinding.HandleIncomingOscMessage` の struct view 受け化と、`OscReceiver.HandleOscMessage(uOSC.Message)` のテスト互換 facade としての存続
  - FacialControl 側の残存確保（AtomicSwap の `new List<BufferedValue>`、gaze / 未マッピングアドレス経路の `Substring`）の排除
  - `OscReceiverGCAllocationTests` の実 UDP loopback + 全スレッド計測への昇格
- **Out of scope**:
  - uOSC の vendor copy（backlog M-16 のフォーク方針）
  - 送信側（`OscSender.SendBundle` / `OscBundleBuilder` / 生 `UdpClient`）の改修。送信側は既に uOSC 非依存であり本仕様の対象外
  - uOSC 互換 facade（`uOSC.Message` 型を受ける公開 API）の撤去。M-16 Phase 11 相当は据え置き
  - 受信機能の仕様変更（heartbeat 自動マッピング、sender_id / ZombieEvictionPolicy、preset、gaze 広告、AtomicSwap / IndividualMessage、staleness / FailSafe、analog listener、`OscPortResolver` によるポート自動解決の意味論変更）
  - Jobs / Burst 化（通常 C# で実装する）
- **Adjacent expectations**:
  - `com.hidano.facialcontrol.ifacialmocap` など OSC 受信に依存する他パッケージは、`OscReceiverAdapterBinding` の公開挙動が変わらない前提で無改修のまま動作すること
  - 検証プロジェクトの OscSend シーン（ARKit 52 本 + gaze + sender_id、VRChat preset、MTU 分割 2 パケット / フレーム）が実機確認の基準ワークロードであること
  - pre-existing 赤テスト（`SampleAssetsAreInSyncTests` ×4、PlayMode OSC heartbeat / auto-mapping ×4、`TenIndependentBindings_OneSwap` フレーキー）は本仕様の受入判定から除外すること

## Requirements

### Requirement 1: 自前 UDP 受信ループ

**Objective:** Unity エンジニアとして、OSC 受信が uOSC の受信パーサを経由せず固定バッファ上で動作してほしい。定常受信中に GC スパイクが発生せず、フレームレートが安定するためである。

#### Acceptance Criteria
1. The OSC Receiver shall 受信開始時に固定サイズの `byte[]` リングバッファを一度だけ確保し、受信中はデータグラム受信のためのヒープ確保を行わない。
2. When 受信スレッドが `Socket.ReceiveFrom` でデータグラムを受け取った, the OSC Receiver shall そのデータグラムをリングバッファ上のスロットへ直接書き込み、コピー用の新規 `byte[]` を生成しない。
3. The OSC Receiver shall データグラムの解析（bundle / message 走査、アドレス解決、値抽出）を受信スレッド上で完了し、メインスレッドのフレーム処理に依存しない。
4. While 受信スレッドが動作中, the OSC Receiver shall 1 フレーム間に複数のデータグラム（MTU 分割された 2 パケット以上を含む）を受信・解析できる。
5. When 受信スレッドの処理がリングバッファの空きスロットを使い切った, the OSC Receiver shall ヒープ確保で拡張せず、最古または新着のデータグラムを破棄し、Unity 標準ログの Warning でその事実を一度だけ通知する。
6. When 受信停止またはコンポーネント破棄が要求された, the OSC Receiver shall 受信スレッドとソケットを確実に終了・解放し、以降のコールバックを発火しない。
7. The OSC Receiver shall 待受ポートの決定に既存の `OscPortResolver` によるポート自動解決を引き続き使用し、解決結果の意味論を変更しない。
8. If ソケットのバインドまたは受信で例外が発生した, then the OSC Receiver shall Unity 標準ログ（`Debug.LogWarning` / `Debug.LogError`）で通知し、受信スレッドを安全に終了させ、メインスレッドを停止させない。

### Requirement 2: Span ベースの OSC パケットリーダー

**Objective:** Unity エンジニアとして、OSC パケットの解析が中間オブジェクトを生成せずに行われてほしい。パケットあたり約 14 KB の確保を排除するためである。

#### Acceptance Criteria
1. The Packet Reader shall 受信バイト列を `ReadOnlySpan<byte>`（または同等のスライス表現）として走査し、解析中に `string`、`object[]`、boxing された値型、`Substring` を生成しない。
2. When 受信バイト列が `#bundle` で始まる, the Packet Reader shall bundle タイムスタンプを読み取り、内包する各要素（message またはネスト bundle）を順に列挙する。
3. When 受信バイト列が `/` で始まる, the Packet Reader shall 単一 message として解析する。
4. The Packet Reader shall 型タグ列を `string` 化せず byte 単位で判定し、`f`（float32）、`i`（int32）、`s`（string）、`b`（blob）を識別できる。
5. When 型タグが `f` の引数を読み取る, the Packet Reader shall `BinaryPrimitives` 相当のビッグエンディアン読み出しで boxing なしに `float` を返す。
6. The Packet Reader shall OSC 仕様の 4 byte アライメント（アドレス・型タグ・string・blob の末尾パディング）を正しく扱う。
7. The Packet Reader shall bundle タイムスタンプの意味論を既存の `OscBundleAccumulator.IsBundleTimestamp`（0 および 1 は bare / immediate 扱い）と一致させる。
8. If パケットが不正（長さ不足、アライメント違反、型タグ数と引数長の不一致、未知の型タグ）である, then the Packet Reader shall 例外を投げずに当該 message または bundle をスキップし、残りの有効な要素の処理を継続する。
9. The Packet Reader shall `string` 型引数および `blob` 型引数をバイトスライスとして公開し、呼び出し側が必要な場合にのみ `string` / `byte[]` へ変換できるようにする。

### Requirement 3: UTF-8 バイト列キーによるアドレス解決

**Objective:** Unity エンジニアとして、BlendShape のアドレス解決がアドレス文字列を生成せずに行われてほしい。ARKit 52 本 + gaze の毎フレーム受信でアドレス `string` の確保がゼロになるためである。

#### Acceptance Criteria
1. When BlendShape マッピングが登録または更新された, the Address Resolver shall 各アドレスの UTF-8 バイト列表現をその時点で事前生成し、受信ホットパスでは生成しない。
2. The Address Resolver shall UTF-8 バイト列の生成に送信側 `OscAddressFormatter` の UTF-8 プールを流用し、同一アドレスの UTF-8 表現を重複して保持しない。
3. When 受信 message のアドレススライスが登録済みマッピングと一致する, the Address Resolver shall 対応する BlendShape インデックスを `string` 化なしに返す。
4. When 受信 message のアドレススライスがどのマッピングにも一致しない, the Address Resolver shall ヒープ確保なしに未マッピングと判定し、当該 message を警告なしでスキップする。
5. The Address Resolver shall 2 バイト文字・特殊記号を含む BlendShape 名のアドレスを、UTF-8 バイト列の完全一致で正しく解決する。
6. The Address Resolver shall gaze 経路のアドレス（VRChat `{pattern}X` / `{pattern}Y`、ARKit 8 本の eyeLook 系 BlendShape）を `Substring` を用いず byte 比較で判定する。
7. The Address Resolver shall アドレス解決の結果（BlendShape / gaze / 制御メッセージ / 未マッピング）が既存の `string` ベース実装と同一になる。

### Requirement 4: 制御メッセージの処理

**Objective:** Unity エンジニアとして、sender_id / heartbeat / preset / gaze 広告といった制御メッセージが従来どおり動作してほしい。zero-alloc 化によって自動マッピングやゾンビ検出が壊れないためである。

#### Acceptance Criteria
1. The Receiver Binding shall `string` 化を `/_facialcontrol/sender_id`、`/_facialcontrol/blendshape_names`、`/_facialcontrol/preset`、`/_facialcontrol/gaze` の制御メッセージ処理に限定し、BlendShape 値および gaze 値の経路では `string` を生成しない。
2. When `/_facialcontrol/sender_id`（blob の uuid + string の startedAt）を受信した, the Receiver Binding shall 既存と同一の sender 識別および ZombieEvictionPolicy の判定を行う。
3. When `/_facialcontrol/blendshape_names` の heartbeat チャンク（同一 bundle タイムスタンプを持つ string 値群）を受信した, the Receiver Binding shall 既存と同一の heartbeat 自動マッピングを行い、チャンク欠落時の挙動も既存と一致させる。
4. When `/_facialcontrol/preset`（string の preset、任意のカスタムプレフィックス）を受信した, the Receiver Binding shall 既存と同一の preset 適用を行う。
5. When `/_facialcontrol/gaze` の広告（string ペア）を受信した, the Receiver Binding shall 既存と同一の gaze 広告処理を行う。
6. While heartbeat または広告など制御メッセージが到着していないフレーム, the Receiver Binding shall ヒープ確保をゼロに保つ。
7. When 制御メッセージが到着したフレーム, the Receiver Binding shall 確保を当該制御メッセージの処理に必要な最小限に留め、BlendShape 値の経路には波及させない。

### Requirement 5: バインディングの struct view 受け口とテスト互換 facade

**Objective:** Unity エンジニアとして、`OscReceiverAdapterBinding` が解析済みの軽量な view を直接受け取れ、かつ既存の `uOSC.Message` を渡す API も残ってほしい。既存テストと他パッケージを壊さずにホットパスを差し替えるためである。

#### Acceptance Criteria
1. The Receiver Binding shall `HandleIncomingOscMessage` の受け口を、アドレススライス・型タグスライス・引数スライス・bundle タイムスタンプを保持する struct view として提供する。
2. When struct view を受け取った, the Receiver Binding shall 既存の `uOSC.Message` 受け口と同一の分岐（`OscDoubleBuffer.Write` / `OscBundleAccumulator.RecordMessage` / 制御メッセージ処理 / 未マッピングスキップ）を実行する。
3. The OSC Receiver shall 既存の `OscReceiver.HandleOscMessage(uOSC.Message)` を公開 API として残し、内部で struct view へ変換して同一の処理経路へ流す。
4. When `HandleOscMessage(uOSC.Message)` 経由で message を処理した, the OSC Receiver shall 実 UDP 経由で処理した場合と同一の結果を生成する。
5. The Receiver Binding shall 受信スレッド上で解析された値をメインスレッドへ受け渡す際、既存の `OscDoubleBuffer` によるダブルバッファリングおよびロック規約（copy-forward + lock）を維持する。
6. The Receiver Binding shall analog listener への通知（`NotifyAnalogListeners`）を既存と同一のタイミング・引数で行う。
7. The Receiver Binding shall 既存の公開シグネチャ（他パッケージが参照するもの）を削除せず、追加のみで差し替えを実現する。

### Requirement 6: FacialControl 側の残存確保の排除

**Objective:** Unity エンジニアとして、uOSC 以外に残る約 5% の確保源も排除してほしい。受信経路全体で GC 確保ゼロを達成するためである。

#### Acceptance Criteria
1. When AtomicSwap モードで bundle が完了した, the Receiver Binding shall `new List<BufferedValue>` を生成せず、プール化またはダブルバッファ化された既存インスタンスを再利用する。
2. The Receiver Binding shall AtomicSwap のプール再利用時に、前 bundle の値が新 bundle に混入しないことを保証する。
3. The Receiver Binding shall gaze 経路および未マッピングアドレス経路の `ExtractBlendShapeName` による `Substring` を byte 比較に置き換え、当該経路でのヒープ確保をゼロにする。
4. The Receiver Binding shall IndividualMessage モードにおいても値の書き込み経路でヒープ確保を行わない。
5. The Receiver Binding shall staleness 判定および FailSafe の処理経路でフレームごとのヒープ確保を行わない。

### Requirement 7: 既存受信機能の挙動維持

**Objective:** Unity エンジニアとして、zero-alloc 化の前後で受信機能の観測可能な挙動が変わらないでほしい。実機で稼働中の構成を無改修で移行できるためである。

#### Acceptance Criteria
1. The OSC Receiver shall heartbeat 自動マッピングの結果（マッピング数、順序、未対応パラメータのスキップ）を既存と一致させる。
2. The OSC Receiver shall sender_id に基づく ZombieEvictionPolicy の判定タイミングと結果を既存と一致させる。
3. The OSC Receiver shall AtomicSwap / IndividualMessage の各モードにおける値の反映タイミングを既存と一致させる。
4. The OSC Receiver shall staleness 判定および FailSafe の発動条件・挙動を既存と一致させる。
5. The OSC Receiver shall analog listener に渡される値・順序・呼び出し回数を既存と一致させる。
6. The OSC Receiver shall VRChat OSC 互換の `/avatar/parameters/{name}` 形式を引き続き受信できる。
7. The OSC Receiver shall 同一プロセス内で 10 体以上の FacialController が独立した受信バインディングを持つ構成を既存と同様にサポートする。
8. When 既存の OSC 関連 EditMode / PlayMode テストを実行した, the OSC Receiver shall pre-existing 赤（`SampleAssetsAreInSyncTests` ×4、PlayMode OSC heartbeat / auto-mapping ×4、`TenIndependentBindings_OneSwap` フレーキー）を除くすべてのテストを緑に保つ。

### Requirement 8: GC アロケーションテストの昇格

**Objective:** Unity エンジニアとして、受信経路の GC 確保を実 UDP 経由・全スレッドで検証するテストがほしい。既存テストが uOSC 経路を素通りして本件を検出できなかった欠陥を再発させないためである。

#### Acceptance Criteria
1. The GC Allocation Test shall `HandleOscMessage` の直接呼び出しではなく、実 UDP loopback 経由で受信経路全体（受信ループ → パケット解析 → バインディング → analog listener）を通して計測する。
2. The GC Allocation Test shall `ProfilerRecorderOptions.CollectOnlyOnCurrentThread` を使用せず、受信スレッドを含む全スレッドの GC 確保を計測する。
3. When 定常受信状態で 100 フレームを計測した, the GC Allocation Test shall heartbeat 到着フレームを除くすべてのフレームで GC 確保が 0 byte であることを assert する。
4. The GC Allocation Test shall 計測ワークロードとして検証プロジェクト OscSend シーン相当（ARKit 52 本 + gaze + sender_id、VRChat preset、MTU 分割 2 パケット / フレーム）を送信側で再現する。
5. The GC Allocation Test shall heartbeat 到着フレームを識別して計測から除外する手段を持ち、除外したフレーム数をテスト出力に記録する。
6. The GC Allocation Test shall PlayMode テストとして `Tests/PlayMode/Performance/` 配下に配置され、既存の `OscReceiverGCAllocationTests` を置き換えまたは拡張する。
7. If 計測中に 1 フレームでも 0 byte を超える確保が検出された, then the GC Allocation Test shall 当該フレーム番号と確保バイト数を失敗メッセージに含める。
8. The GC Allocation Test shall Packet Reader の bundle / message 走査、型タグ判定、不正パケット処理について EditMode の単体テストを伴い、TDD（Red-Green-Refactor）で実装される。

### Requirement 9: 受入基準と実機確認

**Objective:** Unity エンジニアとして、本仕様の完了を客観的な基準で判定したい。Profiler 上ののこぎり歯が実際に消えたことを確認して初めて完了とみなすためである。

#### Acceptance Criteria
1. The OSC Receiver shall 受信側 PlayMode GC テストが実 UDP 経由で 100 フレーム 0 byte（heartbeat 到着フレームを除く）を満たす。
2. The OSC Receiver shall 既存 OSC 関連 EditMode / PlayMode テストが pre-existing 赤を除いて緑である。
3. When 検証プロジェクトの OscSend シーンから受信した状態で Profiler を観測した, the OSC Receiver shall GC Used Memory の約 7 秒周期ののこぎり歯が消失していることを実機確認できる。
4. The OSC Receiver shall 実機確認の結果（Profiler スクリーンショットまたは計測値）を spec の検証記録に残す。

### Requirement 10: アーキテクチャおよびスコープ制約

**Objective:** Unity エンジニアとして、本仕様の実装がプロジェクトのアーキテクチャ契約とスコープを逸脱しないでほしい。将来の M-16（uOSC facade 撤去）や他パッケージへの影響を最小化するためである。

#### Acceptance Criteria
1. The OSC Receiver shall 新規実装を `com.hidano.facialcontrol.osc` パッケージの Adapters 層（名前空間 `Hidano.FacialControl.Adapters.OSC`）内に配置し、Domain / Application 層へ `Socket` や uOSC 型を持ち込まない。
2. The OSC Receiver shall uOSC のソースコードを vendor copy せず、`uOSC.Message` 型の参照を既存の互換 facade（Requirement 5.3）に限定する。
3. The OSC Receiver shall 送信側（`OscSender.SendBundle` / `OscBundleBuilder` / 生 `UdpClient`）のコードを変更しない。
4. The OSC Receiver shall uOSC 互換 facade を撤去せず、backlog M-16 Phase 11 相当の作業を本仕様に含めない。
5. The OSC Receiver shall 受信ループおよびパケット解析を通常 C# で実装し、将来 Jobs / Burst へ差し替え可能なインターフェース境界を保つ。
6. The OSC Receiver shall エラー通知に Unity 標準ログ（`Debug.Log` / `Debug.LogWarning` / `Debug.LogError`）のみを使用し、カスタム例外型を新設しない。
7. The OSC Receiver shall 受信スレッドからの Unity API 呼び出し（`Debug.Log` を除く）を行わず、メインスレッドで処理すべき通知はダブルバッファ経由で受け渡す。
