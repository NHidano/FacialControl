# `osc-receive-zero-alloc` 実装ギャップ分析

## 1. 現状調査

### 1.1 現在の受信経路

現在の経路は次のとおりです。

```text
UDP socket
  ↓
uOSC.DotNet.Udp
  ├─ UDP Receive スレッド: UdpClient.Receive → Queue<byte[]>
  ↓
uOSC.DotNet.Thread
  ├─ 解析ワーカースレッド: Parser.Parse(byte[])
  │    ├─ Reader.ParseString → string
  │    ├─ Reader.ParseFloat/Int
  │    ├─ ParseData → string.Substring(1)
  │    ├─ object[] の生成
  │    └─ uOSC.Message を Queue
  ↓
uOscServer.Update（Unity メインスレッド）
  ├─ Parser.Dequeue()
  ├─ onDataReceived.Invoke(message)
  ↓
OscReceiver.HandleOscMessage(uOSC.Message)
  ↓
OscReceiverAdapterBinding.HandleIncomingOscMessage
  ├─ sender_id
  ├─ heartbeat
  ├─ preset
  ├─ gaze advertisement
  ├─ gaze routing
  └─ normal blendshape routing
  ↓
OscDoubleBuffer / OscBundleAccumulator
  ↓
OnFixedTick
  ├─ buffer Swap / bundle Flush
  └─ OscInputSource / GazeVector2InputSource
```

根拠となる実装は以下です。

- `uOscServer.UpdateMessage()` が解析ワーカースレッド上で `Parser.Parse` を呼ぶ。
- `uOscServer.Update()` がメインスレッド上で `Parser.Dequeue()` と `onDataReceived.Invoke()` を呼ぶ。
- `Udp.cs` は UDP 受信で `byte[]` を毎回作成し、`Queue<byte[]>` に格納する。
- `Parser.cs` は `Message`、`object[]`、アドレス文字列、型タグ文字列を生成する。
- `OscReceiver` は現在 `uOSC.Message` を受け取り、`Dictionary<string, int>` でアドレスを解決する。
- `OscReceiverAdapterBinding` の制御メッセージ処理も、現在は `uOscServer.Update()` から呼ばれるメインスレッド上で実行される。

### 1.2 既存の再利用可能部品

| 部品 | 再利用可能な機能 | ギャップ |
|---|---|---|
| `OscDoubleBuffer` | `NativeArray<float>` の二重バッファ、write tick、メインスレッド Swap | `Write` ごとに `_resizeLock` を取得。受信リングバッファそのものではない |
| `OscBundleAccumulator` | OSC bundle timestamp 単位の atomic swap、bare/immediate 処理、timeout | `List<BufferedValue>` を bundle 完了ごとに生成する |
| `OscAddressFormatter` | OSC アドレス形式、VRChat/ARKit prefix、UTF-8 pool | 現状は送信側向け。受信キー lookup には未対応 |
| `OscBundleBuilder` | OSC packet layout、4-byte alignment、big-endian、UTF-8 書き込み、`ArrayPool<byte>` | sender-side 専用。受信 parser としては再利用できない |
| `OscPortResolver` | port 探索と既存仕様 | 自前 receiver から引き続き利用可能 |
| `HeartbeatConsistencyChecker` | heartbeat の一貫性検証 | string/list ベースの現行制御処理との統合が必要 |
| `RuntimeMappingResolver` | heartbeat から runtime mapping を構築 | 解決結果自体は再利用可能。入力を struct/byte view に変更する必要 |
| `GazeAdvertisementResolver` | gaze advertisement の parse、正規化、hash | string 化をどのスレッドで行うか要設計 |
| `ZombieEvictionPolicy` / `SenderIdentity` | sender identity と zombie 判定 | struct view への変換が必要 |
| `PerfectSyncEyeLook` | ARKit gaze の値変換 | 受信解析とは独立して再利用可能 |
| `OscReceiverHost` | helper MonoBehaviour、start/stop、Tick、Dispose | uOSC server の代替 lifecycle を組み込む必要 |
| `OscInputSource` | `NativeArray` から output Span へのコピー、staleness/failsafe | 受信後の値消費はそのまま利用可能 |
| `GazeVector2InputSource` | gaze 値の保持と読み出し | 受信スレッドから直接 Unity/入力 source を触らない設計が必要 |

### 1.3 binding の処理構造

`OscReceiverAdapterBinding` はすでに多くの再利用領域を持っています。

- `OnStart` で `OscReceiverHost`、buffer、mapping、input source を構築。
- `OnFixedTick` で `OscReceiverHost.Tick()`、pending heartbeat/gaze advertisement 処理、gaze frame flush、publish を行う。
- `HandleIncomingOscMessage` は以下を判定する。

  - `sender_id`
  - sender decision
  - `/_facialcontrol/blendshape_names`
  - `/_facialcontrol/preset`
  - `/_facialcontrol/gaze`
  - gaze route
  - normal blendshape route

- heartbeat と gaze advertisement は現状、scratch list と lock を用いて蓄積し、`OnFixedTick` で dirty flag を確認して処理する。
- gaze bundle は `_gazeBundleSync` と `Queue<List<GazeSample>>` を使用する。
- `GetCurrentTimeSeconds()` は `ITimeProvider` がない場合に `Time.unscaledTimeAsDouble` を呼ぶ。

### 1.4 テスト配置と uOSC 依存

主なテストは以下です。

- EditMode
  - `Tests/EditMode/Adapters/AdapterBindings/OscReceiverAdapterBindingTests.cs`
  - `Tests/EditMode/Adapters/InputSources/*`
  - `Tests/EditMode/Adapters/OSC/*`
  - `uOSC.Message` を生成し、`HandleOscMessage` を直接呼ぶテストが多数存在。
- PlayMode Integration
  - `OscSendReceiveTests.cs`
  - `OscSendReceiveE2ETests.cs`
  - `OscReceiverAdapterBindingIntegrationTests.cs`
  - bundle atomicity、heartbeat、gaze、zombie eviction、failsafe 等。
- PlayMode Performance
  - `OscReceiverGCAllocationTests.cs`
  - `OscSenderGCAllocationTests.cs`

`OscReceiverGCAllocationTests` は uOSC の UDP/Parser 経路を通らず、`HandleOscMessage` を直接呼び出します。また、`ProfilerRecorderOptions.CollectOnlyOnCurrentThread` を使っているため、現在の受信スレッド解析・全スレッド GC は測定していません。

asmdef 依存は以下です。

- `Hidano.FacialControl.Osc`
  - `uOSC.Runtime` を直接参照。
- Osc EditMode/PlayMode test asmdef
  - `Hidano.FacialControl.Osc`
  - `uOSC.Runtime`
  - Unity Test Runner 関連を参照。
- `uOSC.Runtime`
  - 外部 assembly 依存なし。

他パッケージの実装参照については、`com.hidano.facialcontrol.ifacialmocap` は `.osc` パッケージに依存しますが、`uOSC` や `OscReceiver` を直接利用していません。公開 surface として維持すべき主なものは、`OscReceiverAdapterBinding`、`OscReceiverHost`、`OscReceiver`、`HandleOscMessage(uOSC.Message)`、`RegisterAnalogListener`、`OscDoubleBuffer` です。

---

## 2. Requirement-to-Asset Map

| Req | 既存資産 | Missing / Unknown | Constraint |
|---|---|---|---|
| 1. 自前 UDP 受信ループ | `[Existing]` `OscReceiverHost`、`OscPortResolver`、`OscDoubleBuffer`、既存 lifecycle | `[Missing]` 固定サイズ byte[] ring buffer、`Socket.ReceiveFrom` ループ、最古データ破棄、停止同期 | `[Constraint]` socket/解析スレッドは Unity API を呼べない。port 解決仕様は維持 |
| 2. Span ベース packet reader | `[Existing]` `OscBundleBuilder` に layout、alignment、big-endian の知識 | `[Missing]` receive-side `ReadOnlySpan<byte>` parser、bundle/message iterator、validation、typed value view | `[Constraint]` string、object[]、boxing、Substring を hot path で生成不可 |
| 3. UTF-8 byte key address resolver | `[Existing]` `OscAddressFormatter` の UTF-8 生成と送信 pool、既存 mapping table | `[Missing]` UTF-8 byte sequence lookup、prefix除去なしの blendshape解決、gaze suffix 判定 | `[Constraint]` mapping 更新時に key を事前生成する必要。UTF-8 bytes の所有期間を保証する必要 |
| 4. 制御メッセージ処理 | `[Existing]` sender identity、heartbeat、preset、gaze advertisement、各 resolver/checker、dirty flag | `[Missing]` struct view から既存制御処理へ渡す API、string 化の境界、byte payload の一時保持戦略 | `[Constraint]` Unity logging、SO/registry 更新、source生成はメインスレッド限定に寄せる必要 |
| 5. struct view と互換 facade | `[Existing]` `HandleOscMessage(uOSC.Message)`、EditMode/PlayMode の直接呼び出しテスト | `[Missing]` `OscMessageView` 等の型、facade から struct view への変換規則、uOSC を使わないテスト受け口 | `[Constraint]` 既存公開 API とテストの互換性を壊せない |
| 6. FacialControl 側残存確保排除 | `[Existing]` `NativeArray` buffer、各種 scratch list、hash、pool の考え方 | `[Missing]` `new List<BufferedValue>`、gaze frame queue、control payload の hot path allocation 排除 | `[Constraint]` bundle境界・timeout・既存 atomicity を維持する必要 |
| 7. 既存挙動維持 | `[Existing]` integration tests、bundle、heartbeat、gaze、failsafe、zombie、auto mapping | `[Unknown]` 新 parser が全 OSC edge case で uOSC と同一挙動になるか | `[Constraint]` sender path は変更対象外。既存 `OscSender.SendBundle` との wire compatibility が必要 |
| 8. GC test 昇格 | `[Existing]` `OscReceiverGCAllocationTests` のシナリオと baseline、`ProfilerRecorder` | `[Missing]` 実 UDP loopback を通す receiver allocation test、受信/解析スレッド測定方法 | `[Unknown]` `ProfilerRecorder` が全スレッドを正確に捕捉できるか |
| 9. 受入基準・実機確認 | `[Existing]` 100 frame テスト、send/receive E2E、MTU/bundle tests | `[Missing]` 受信スレッドを含むゼロ allocation の受入テスト、負荷・packet drop 可視化 | `[Constraint]` Unity Test Runner の sanctioned 実行方法を遵守 |
| 10. architecture/scope | `[Existing]` OSC package 内に責務を閉じる構成、asmdef、sender-side separation | `[Missing]` uOSC 依存を runtime hot path から除去する設計、移行・互換ポリシー | `[Constraint]` uOSC vendor copy、sender、Jobs/Burst、他 package の API 契約は変更対象外 |

---

## 3. 実装アプローチ

### Option A: 既存拡張

主な変更先：

- `OscReceiver.cs`
- `OscReceiverHost.cs`
- `OscBundleAccumulator.cs`
- `OscDoubleBuffer.cs`
- `OscReceiverAdapterBinding.cs`
- `OscAddressFormatter.cs`

自前 socket、parser、struct view を既存クラスに追加し、`HandleOscMessage(uOSC.Message)` は互換 facade として残します。

利点：

- 公開 surface を維持しやすい。
- 既存の bundle、mapping、gaze、control 処理を直接利用できる。
- 変更ファイルが少なく、移行経路が単純。

欠点：

- `OscReceiver` が MonoBehaviour、socket lifecycle、parser、address lookup、互換 facade を兼任する。
- 現在の `uOSC.Message` と新しい view の分岐で複雑化しやすい。
- thread safety の責務が既存クラスに集中する。
- 既存 `OscBundleAccumulator` の allocation を残しやすい。

評価：短期実装は可能だが、zero-allocation の境界が曖昧になりやすい。

### Option B: 新規コンポーネント

新規に次の責務を分離します。

- `OscNativeReceiver`
  - socket、固定 byte[] ring、受信スレッド。
- `OscPacketReader`
  - `ReadOnlySpan<byte>` による packet parse。
- `OscMessageView`
  - address/type/value/timestamp の struct view。
- `OscAddressByteResolver`
  - UTF-8 bytes の lookup。
- `OscReceiveQueue` または `OscMessageRingBuffer`
  - parsed view の main thread 配送。
- `OscReceiver`
  - 既存の MonoBehaviour facade と lifecycle。

利点：

- socket、parser、binding dispatch の責務を分離できる。
- parser を Unity 非依存・uOSC 非依存としてテストできる。
- allocation boundary と thread boundary を明確にできる。
- `HandleOscMessage(uOSC.Message)` を compatibility adapter に限定できる。

欠点：

- 新規 API とデータモデルが増える。
- struct view の lifetime、blob/string payload の所有方式を慎重に設計する必要がある。
- 既存 binding の全面的な受け口変更が必要。
- 互換 facade と新経路の二重テストが必要。

評価：設計としては最も明快だが、初期実装量は大きい。

### Option C: ハイブリッド

推奨候補です。

- 新規：
  - socket receiver
  - packet reader
  - `OscMessageView`
  - byte-key resolver
  - parsed message ring buffer
- 既存拡張：
  - `OscReceiverHost` の lifecycle
  - `OscReceiver` の公開 facade
  - `OscReceiverAdapterBinding` の struct view 受け口
  - `OscDoubleBuffer` / bundle semantics
  - control resolver、gaze、heartbeat、failsafe
- 互換：
  - `HandleOscMessage(uOSC.Message)` はテスト・外部利用向けに残し、内部で view へ変換。

利点：

- zero-allocation の新経路と既存 API の互換性を両立できる。
- parser/transport の検証を独立させられる。
- 既存の成熟した binding ロジックを段階的に移行できる。
- sender path は変更せず、受信側だけを置換できる。

欠点：

- 新経路と facade 経路で挙動が二重化する可能性がある。
- struct view と uOSC.Message の両方向変換方針を固定しないと設計がぶれる。
- 移行期間中は parser、binding、legacy test の三層を維持する必要がある。

---

## 4. 設計で決めるべき論点

### (a) 解析スレッドと binding 処理スレッド

#### 案1: 受信スレッドで解析、解析済み struct をメインスレッドへ配送

```text
UDP Receive thread
  → packet reader
  → address lookup
  → typed struct view
  → bounded parsed-message ring
  → Unity main thread
  → binding / buffer / input source
```

受信スレッドで実行可能な処理：

- socket receive
- packet bounds validation
- OSC bundle/message traversal
- byte tag 判定
- endian conversion
- float/int の読み出し
- UTF-8 byte key による事前構築済み mapping lookup
- timestamp の保持
- 固定容量 ring への struct 書き込み

メインスレッドに残すべき処理：

- `OscReceiverAdapterBinding` の状態変更
- `InputSourceRegistry` 登録・更新
- gaze source の生成・再構成
- runtime mapping の再構築
- `ScriptableObject` や `MonoBehaviour` へのアクセス
- `Debug.Log*`
- Unity `Time` の直接参照
- `OscDoubleBuffer.Swap`
- `OscInputSource` の publish
- failsafe の状態変更

必要な同期：

- ring の producer/consumer index は lock-free または最小限の atomic 操作。
- mapping table は immutable snapshot とし、更新時に新しい resolver を構築して atomic swap。
- binding の既存 `_heartbeatSync`、`_gazeAdSync`、`_gazeBundleSync` は原則メインスレッド処理へ集約できれば削減可能。
- stop/dispose では socket close、受信スレッド終了、ring drain/破棄の順序を明確化する。

この方式が最も安全です。Unity API と binding の複雑な状態遷移をメインスレッドに閉じ込められます。

#### 案2: binding まで受信スレッドで処理

利点は main-thread queue の遅延を減らせることです。

ただし、以下の問題があります。

- `Time.unscaledTimeAsDouble` は Unity API であり、現状の fallback は binding 内に存在する。
- `Debug.Log*` を受信スレッドから呼ぶ既存コードがある。
- gaze source、input registry、runtime mapping の変更が Unity object lifecycle と競合する。
- `_gazeRuntimeEntries`、heartbeat、preset、sender identity、fail-safe の状態が main thread と競合する。
- `OscDoubleBuffer.Write` は lock を取るが、読み出し・swap は main thread 前提。
- callback/analog listener が任意コードを呼ぶため、スレッド契約が変わる。

したがって、binding 全体を受信スレッドで実行する案は、追加の dispatcher、Unity API の分離、全状態の同期化が必要で、リスクが高いです。

### (b) `HandleOscMessage(uOSC.Message)` facade と struct view

推奨方向は以下です。

```text
uOSC.Message
  → compatibility converter
  → OscMessageView
  → 共通 binding 処理

raw packet
  → OscPacketReader
  → OscMessageView
  → 共通 binding 処理
```

つまり、binding の正規入口を `OscMessageView` にし、`uOSC.Message` から view へ一方向変換します。

逆方向の struct view → `uOSC.Message` 変換は避けるべきです。

理由：

- `uOSC.Message` は `string address`、`object[] values` を必須とし、zero-allocation 条件に反する。
- compatibility facade はテスト・既存外部利用向けであり、hot path ではない。
- `byte[]` blob の所有権と string の lifetime が曖昧になる。

ただし、既存 EditMode tests は `uOSC.Message` を構築して直接 `HandleOscMessage` を呼ぶため、public facade は維持する必要があります。

### (c) UTF-8 バイト列キー lookup

候補は次のとおりです。

1. 長さ + ハッシュの custom Dictionary
   - 平均 lookup は高速。
   - collision 時に bytes 比較が必要。
   - hash 実装、登録時の事前計算、collision 処理が必要。

2. ソート済み byte key 配列の二分探索
   - allocation-free で実装しやすい。
   - mapping 数が少ない場合は十分。
   - 比較コストがアドレス長に比例する。

3. 先頭バイト・prefix 分岐
   - `/avatar/parameters/`、`/ARKit/`、制御 prefix 等を高速に分類可能。
   - 最終的には suffix/name lookup が必要。
   - gaze の `X/Y`、ARKit eye-look の分岐に向く。

推奨は、登録時に UTF-8 bytes と hash/length を生成し、hot path では「prefix分類 → hash/length lookup → byte比較」の構成です。

`OscAddressFormatter` の `GetOrAdd...Utf8` は送信側の `(string, preset)` pool であり、受信 mapping の key storage としてそのまま流用はできません。ただし、同じ UTF-8 生成ルールと pool の考え方は再利用できます。受信側では mapping の lifetime と immutable snapshot を管理する別 pool が必要です。

### (d) 制御メッセージの string 化

推奨はメッセージ種別ごとに分けることです。

- `sender_id`
  - UUID blob と startedAt を struct/byte view のまま解析。
  - `Guid` 生成や string fallback は、必要な場合のみ main thread。
- heartbeat
  - 通常の blendshape name は mapping 更新時など低頻度のため、main thread で string 化可能。
  - 受信スレッドでは byte slice の参照または固定 scratch に蓄積。
- preset
  - 低頻度。main thread で string 化して `_currentPresetName` 等を更新。
- gaze advertisement
  - 低頻度。解析スレッドでは raw byte view を配送し、main thread で既存 resolver に渡すのが安全。
- blendshape/gaze の通常値
  - string 化しない。UTF-8 key lookup と float value のみで処理。

制御メッセージを受信スレッドで string 化する場合、`string` の割当は通常経路から排除できません。zero-allocation の対象を「通常の blendshape/gaze 値」と「制御メッセージも含む全受信」に分けて spec/design で明確化すべきです。

### (e) リングバッファサイズと最古破棄

Req 1.5 の最古破棄は、bounded queue として仕様化が必要です。

決めるべき値：

- packet ring の容量
- parsed message ring の容量
- 1 frame あたりの最大 message 数
- 受信速度が処理速度を超えた場合の drop 単位
- bundle の途中で容量不足になった場合の扱い
- warning の一度きり条件
- drop counter の公開方法

推奨方針：

- packet ring は MTU 上限または設定可能な固定 byte buffer。
- parsed message ring は、通常 frame の最大 message 数に burst margin を加えた固定容量。
- full 時は最古 packet/message を破棄して最新を優先。
- bundle は timestamp 単位で一貫性を保つため、途中破棄時は bundle 全体を drop するか、bundle incomplete flag を配送する。
- drop warning は `Interlocked.Exchange` による一度きり通知にする。

### (f) 全スレッド GC 計測

現在の `OscReceiverGCAllocationTests` は `CollectOnlyOnCurrentThread` を使い、直接 `HandleOscMessage` を呼んでいます。そのため、uOSC の UDP receive/parse worker の allocation を測定していません。

`ProfilerRecorder` が全スレッドの `GC.Alloc` を正確に捕捉できるかは、Unity 6000.3.19f1 と使用する recorder counter の実動確認が必要です。

これは **Research Needed** として設計フェーズに持ち越すべきです。

代替検証候補：

- Unity Profiler の `GC.Alloc` counter を全スレッド設定で記録。
- 実 UDP loopback で受信数と allocation delta を比較。
- 受信スレッド内部の allocation-sensitive instrumentation。
- packet count、drop count、frame count と GC counter の相関確認。

---

## 5. Effort と Risk

### 総合評価

- Effort: **XL**
- Risk: **High**

根拠：

- uOSC の UDP、解析、queue、main-thread dispatch を受信側だけ置換するアーキテクチャ変更である。
- packet parser、bundle semantics、UTF-8 lookup、control message、gaze、heartbeat、sender identity を同時に維持する必要がある。
- public `uOSC.Message` facade と大量の既存テストを壊せない。
- 現行 GC test は実際の receive/parse worker を測定しておらず、達成判定の方法自体に Research Needed がある。
- `OscReceiverAdapterBinding` は大規模で、thread affinity と lock の設計を誤ると deadlock、race、入力値欠落が起こり得る。

部分評価：

| 領域 | Effort | Risk | 根拠 |
|---|---:|---:|---|
| 固定 byte[] UDP ring | L | Medium | socket lifecycle と overflow semantics が必要 |
| Span parser | L | High | OSC alignment、bundle recursion、型、異常 packet の互換性 |
| UTF-8 resolver | M/L | Medium | mapping snapshot と collision 処理が必要 |
| binding struct view 移行 | L | High | control/gaze/heartbeat と既存 public facade の統合 |
| allocation test 昇格 | M/L | High | 全スレッド計測方法が未確定 |
| 既存挙動維持 | L | High | E2E と既存 direct-call test の両方を維持 |

---

## 6. 設計フェーズへの推奨事項

### 推奨アプローチ

Option C のハイブリッドを推奨します。

1. `OscNativeReceiver`、`OscPacketReader`、`OscMessageView`、byte-key resolver を新規責務として分離。
2. 解析済み struct view を bounded queue でメインスレッドへ配送。
3. `OscReceiverAdapterBinding` はメインスレッドで binding 処理を継続。
4. `OscDoubleBuffer`、bundle semantics、input source、既存 control resolver は段階的に再利用。
5. `HandleOscMessage(uOSC.Message)` は public compatibility facade として維持し、view へ一方向変換。
6. sender path と `OscBundleBuilder` の送信処理は変更しない。
7. parser 単体、UDP integration、binding compatibility、GC/performance を別テスト群に分離。

### 設計で先に固定すべき契約

- `OscMessageView` の address representation と lifetime。
- float/int/string/blob の typed representation。
- blob/string の byte slice が packet ring 上に存在できる期間。
- parser が bundle 内 message をどの順序で配送するか。
- malformed packet、unknown tag、truncated packet の処理。
- ring full 時の drop 単位と警告仕様。
- mapping table の immutable snapshot 更新方式。
- control message の string 化境界。
- main-thread dispatch 前の timestamp と receive time の定義。
- `HandleOscMessage(uOSC.Message)` の例外・null・互換挙動。
- 全スレッド GC の受入測定方法。

### Research Needed

1. Unity 6000.3.19f1 の `ProfilerRecorder` が worker thread allocation をどの範囲で集計するか。
2. `Socket.ReceiveFrom` の Unity/Mono/.NET runtime 上の allocation 挙動。
3. `ReadOnlySpan<byte>`、`BinaryPrimitives`、`Encoding.UTF8` の対象 Unity runtime における allocation と Burst/IL2CPP 互換性。
4. raw byte slice を packet ring 上で保持したまま main thread に配送する場合の overwrite 防止設計。
5. bundle途中drop時に既存 atomicity を維持する最適な drop semantics。
6. `uOSC.Message` の既存 direct-call test と新 struct view test の共存方法。
7. `Debug.LogWarning` の thread affinity と受信スレッドからの安全な通知方式。
8. `Time.unscaledTimeAsDouble` を受信時刻に使わず、`ITimeProvider` または monotonic clock に統一する方式。
9. 既存の `OscBundleAccumulator` と新しい parsed message queue のどちらを bundle accumulation の責務とするか。
10. 受信側 UTF-8 resolver の候補実装について、実際の mapping 数と benchmark 条件を決めること。

