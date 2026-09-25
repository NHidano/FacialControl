# Research & Design Decisions — osc-receive-zero-alloc

## Summary
- **Feature**: `osc-receive-zero-alloc`
- **Discovery Scope**: Complex Integration（既存受信経路のトランスポート層置換 + 性能クリティカル）→ Full discovery を実施
- **Key Findings**:
  - 現行の `uOscServer.Update()` は **メインスレッド**で `onDataReceived.Invoke` を呼んでおり、`OscReceiver.HandleOscMessage` → binding の処理は今日すでにメインスレッドで動いている。gap-analysis 案1（受信スレッドで解析、binding はメインスレッド）は既存のスレッド親和性を **そのまま維持**する設計であり、`receivedAtSeconds` の意味論（メインスレッド時刻）も変えずに済む。
  - Mono の `Socket.ReceiveFrom` は呼び出しごとに `IPEndPoint.Create` + `Serialize` で約 338 byte を確保する（where-allocation の計測）。受信機能は送信元エンドポイントを一切使わないため、受信ソケットは `Socket.Receive(byte[], int, int, SocketFlags)` を用いる（Req 1.2 の「`Socket.ReceiveFrom`」は「自前ソケット受信」の意図と解釈し、設計上の逸脱として明記）。
  - `ProfilerRecorderOptions.CollectOnlyOnCurrentThread` の公式説明は「初期化したスレッドのみ収集」であり、未指定時に**ユーザー生成スレッドの GC.Alloc が確実に集計されるかは公式に明記されていない**。GC テストは positive control（受信スレッドで意図的に確保する較正ステップ）で計測器の有効性を自己検証し、失敗時は `GC.GetAllocatedBytesForCurrentThread` を受信スレッド自身がサンプリングする方式を authoritative にする。
  - preset / gaze 広告は heartbeat フレームにのみ同梱される（`OscSenderAdapterBinding.OnLateTick`）。したがって「heartbeat 到着フレーム」を除外すれば制御メッセージ由来の string 化はすべて除外される。sender_id は毎フレーム到着するため、受信スレッドで値型（`Guid` + `long`）に解決し string 化しない。

## Research Log

### uOSC 受信経路の確保源と実行スレッド
- **Context**: 要件の前提（約 14 KB/フレーム）とスレッド親和性の確認。
- **Sources Consulted**: `Library/PackageCache/com.hidano.uosc@f7a52f0c524d/Runtime/{uOscServer.cs, Core/Parser.cs, Core/DotNet/Udp.cs}`
- **Findings**:
  - `DotNet.Udp`: 受信スレッドで `UdpClient.Receive(ref endPoint)` → データグラムごとに `byte[]` 生成 → `Queue<byte[]>`。
  - `Parser.Parse`: `Reader.ParseString`（address / typetag の string）、`.Substring(1)`、`object[]`、float/int の boxing、`new Message()`。
  - `uOscServer.UpdateMessage` は解析ワーカースレッド、`uOscServer.Update()`（メインスレッド）で `onDataReceived.Invoke(message)`。
  - uOSC は `IPAddress.IPv6Any` + `IPv6Only=0` + `ReuseAddress=1` で bind。`OscPortResolver.IsPortAvailable` は同じ dual-mode で **ReuseAddress なし**のプローブ bind を行い衝突を検知する。
- **Implications**: binding の状態変更・`Debug.Log`・registry 更新は今日もメインスレッド。案1 はこの契約を維持する。bind 方式は uOSC と同一（IPv6 dual-mode + ReuseAddress）にし、`OscPortResolver` の前提を崩さない。

### Mono `Socket.ReceiveFrom` の確保挙動
- **Context**: Req 1.1/1.2（データグラム受信でヒープ確保しない）の実現可能性。
- **Sources Consulted**: [vis2k/where-allocation](https://github.com/vis2k/where-allocation)、[dotnet/runtime #30196](https://github.com/dotnet/runtime/issues/30196)、[dotnet/runtime #30797](https://github.com/dotnet/runtime/issues/30797)
- **Findings**:
  - Mono の `ReceiveFrom` は `IPEndPoint.Create(SocketAddress)` と `EndPoint.Serialize()` を毎回呼び、計 338 byte/回を確保する。Unity 2019/2020 の古い Mono では `ReceiveFrom_Internal` がさらに 90 byte 確保するが Unity 2021.2+ の Mono では解消見込み。
  - 回避策は (a) `EndPoint` 派生クラスで `Create/Serialize` をキャッシュ返却にする（where-allocation 方式）、(b) 送信元が不要なら `Socket.Receive` を使う（endpoint 引数なし、`recv` 相当）。
  - `UdpClient.Send(byte[], int, IPEndPoint)` → `SendTo` も `Serialize()` を呼ぶ。送信側は本仕様のスコープ外だが、GC テストの**テスト側送信**は接続済み UDP ソケットの `Socket.Send` を使えば確保ゼロにできる。
- **Implications**: 受信ソケットは `Socket.Receive` を採用。将来送信元識別が必要になった場合の代替として where-allocation 方式を Open Question に記録。

### Unity Profiler の GC.Alloc とスレッド
- **Context**: Req 8.2（全スレッド計測）の計測器選定。
- **Sources Consulted**: [ProfilerRecorderOptions](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorderOptions.html)、[Tracking garbage collection allocations (6000.3)](https://docs.unity3d.com/6000.3/Documentation/Manual/performance-track-garbage-collection.html)、[GC.GetAllocatedBytesForCurrentThread](https://learn.microsoft.com/dotnet/api/system.gc.getallocatedbytesforcurrentthread)
- **Findings**:
  - `CollectOnlyOnCurrentThread`：「ProfilerRecorder を初期化したスレッドのみ収集」。未指定時の対象スレッド範囲は明記なし。
  - Manual：「GC.Alloc statistic includes allocations made across all threads」（Profiler ウィンドウの統計）。ただし `new Thread()` で作ったスレッドが `Profiler.BeginThreadProfiling` なしで ProfilerRecorder に載るかは未確認。
  - `GC.GetAllocatedBytesForCurrentThread()` は .NET Standard 2.1 API であり Unity 6 の Mono から利用可能（スレッド生涯の累積確保バイト、icall で確保なし）。
  - `GC.GetTotalAllocatedBytes(bool)` は netstandard2.1 に含まれない。リポジトリ内には `typeof(GC).GetMethod("GetTotalAllocatedBytes", new[]{typeof(bool)})` のリフレクション取得パターンが既にある（`OverlayInputSourcePerformanceTests`, `EndToEndGcAllocationTests`）。
- **Implications**: 計測器は「positive control で自己検証」し、authoritative を条件分岐で固定する（design.md「Testing Strategy」）。受信ループは**スレッド生存フック**（`OnThreadStarted` / `OnThreadStopping`）を持ち、テストから `Profiler.BeginThreadProfiling` を注入できるようにする（本番コードは受信スレッドで Unity API を呼ばない: Req 10.7）。

### OSC ワイヤ形式（送信側 `OscBundleBuilder` との互換）
- **Context**: Req 2.x / 7.x。受信リーダーが自前送信側と第三者（VRChat, iFacialMocap）両方を扱えること。
- **Sources Consulted**: `OscBundleBuilder.cs`、uOSC `Parser.cs`、OSC 1.0 仕様
- **Findings**:
  - 送信側は 1 フレーム 1 `#bundle`（8 byte timestamp）、先頭要素 sender_id（`,bs`：blob 16 byte + string）、float（`,f`）、heartbeat（`,s…` チャンク）、preset（`,s` / `,ss`）、gaze 広告（`,s…` ペア）。1472 byte で分割し、分割パケットも同一 timestamp の `#bundle` で sender_id を先頭に再掲する。
  - uOSC は bare（非 bundle）message に timestamp `0x1`（Immediate）を与える。`OscBundleAccumulator.IsBundleTimestamp` は 0 / 1 を bare 扱い。
  - uOSC は address の先頭 `/` を検証しない。未知タグ（`h`, `d`, `t` 等）はサイズを進めず後続を壊す。`T`/`F` は payload なしで bool。
  - VRChat は bool パラメータを `,T` / `,F` で送る。既存経路では bool 値は float 抽出に失敗するが、**マッピング済みアドレスであれば `MarkAcceptedPacket()`（staleness 更新）は走る**。
- **Implications**: リーダーは `i f s b h d t T F N I` を既知タグとし（`h d t` はサイズ正しくスキップ、uOSC より堅牢）、値抽出は `f` / `i` のみ float とする（既存と同一）。「既知アドレスだが float なし」を表す `HasFloat=false` レコードを流し、staleness 更新の既存挙動を維持する。bare message の timestamp は `0x1`。

### 既存 binding の制御メッセージ処理の性質
- **Context**: Req 4.x。バイト比較の fast path で string 化をスキップして良いか。
- **Sources Consulted**: `OscReceiverAdapterBinding.cs`（`HandleHeartbeatMessage`, `ProcessPendingHeartbeatMappings`, `HandleGazeAdvertisementMessage`, `ProcessPendingGazeAdvertisement`, `HandlePresetMessage`）、`HeartbeatConsistencyChecker.UpdateFromHeartbeat`、`OscSenderAdapterBinding.OnLateTick`
- **Findings**:
  - heartbeat：同一 bundle timestamp のチャンクを累積 → dirty → `OnFixedTick` で `UpdateFromHeartbeat`（入力のみから再計算する冪等処理）→ FNV-1a（string）→ 前回と同じなら return。
  - gaze 広告：同様に累積 → `Parse` → 正規化ハッシュ → 同じなら return。
  - preset：`_currentPresetName` / `_currentCustomPrefix` の単純代入。
  - 既存 PlayMode テスト `OnFixedTick_HeartbeatHashUnchanged100Frames_ZeroGCAllocation` は「同一 heartbeat が毎フレーム来ても 0 byte」を **facade 経由**で要求している。facade が uOSC.Message をワイヤ化して同じパーサに流す設計では、**バイト列レベルの unchanged 判定で string 化をスキップしないとこのテストが赤になる**。
  - `LastHeartbeatHash` / `LastGazeAdvertisementHash` はテストで「変化しないこと」「0 であること」を assert しているため、公開ハッシュの算出方法（string ベース）は変えない。
- **Implications**: 制御メッセージは「バイト列ハッシュ（内部）で unchanged 判定 → 変化時のみ string 化 → 既存の string ベース処理」の 2 段構成にする。冪等な `UpdateFromHeartbeat` を unchanged 時に省略しても観測挙動は同じ。

### 受信ホットパスで使う BCL API の確保有無
- **Context**: Req 2.1 / 2.5。
- **Sources Consulted**: .NET Standard 2.1 API 面（`System.Buffers.Binary.BinaryPrimitives`, `MemoryExtensions.SequenceEqual`, `Encoding.UTF8.GetString(ReadOnlySpan<byte>)`, `Guid.TryParse(ReadOnlySpan<char>, out Guid)`, `long.TryParse(ReadOnlySpan<char>, …)`）
- **Findings**: いずれも netstandard2.1 で利用可、確保なし（`GetString` は当然 string を確保するのでメインスレッド限定）。`ref struct` に `ReadOnlySpan<byte>` フィールドを持てる（C# 7.2+、Unity 6 は C# 9）。`allowUnsafeCode:false` のまま実装可能。
- **Implications**: `unsafe` や `NativeArray` は不要。Jobs/Burst 差し替え境界は `OscPacketReader` の入力（`ReadOnlySpan<byte>`）と `OscAddressKeyTable` の照合 API に置く。

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| A: 既存拡張 | `OscReceiver` 内に socket / parser / lookup を追加 | 変更ファイル少 | `OscReceiver` が MonoBehaviour + socket + parser + facade を兼任、zero-alloc 境界が曖昧 | 却下 |
| B: 全面新規 | `OscNativeReceiver` 等を新設し binding 受け口も全面差し替え | 責務分離が明快 | 実装量大、既存テスト互換の二重化 | 却下 |
| **C: ハイブリッド（採用）** | 新規：受信ループ / リング / リーダー / キーテーブル / 分類器。既存拡張：`OscReceiver`（facade）、`OscReceiverHost`、binding の受け口、accumulator | zero-alloc 経路と既存 API の両立、sender 無改修 | facade 経路と UDP 経路の二重化 → **facade を同一パーサへ流す**ことで解消 | ユーザー承認済み |

## Design Decisions

### Decision: スレッドモデル（案1）と apply フェーズ
- **Context**: 受信スレッドでどこまで処理するか。
- **Alternatives Considered**: 案1（解析 + 解決のみ受信スレッド、apply はメイン）／案2（binding まで受信スレッド）
- **Selected Approach**: 案1。受信スレッドは `Receive → OscPacketReader → OscMessageClassifier`（不変キーテーブル参照）→ `OscDatagramRing` へコミット。メインスレッドは `OscReceiverHost.Update()` でドレイン → `OscReceiver.Apply` → binding → `OscDoubleBuffer.Write` / `OscBundleAccumulator.RecordBundleMessage` / listener。`Swap` / `FlushDue` は従来どおり `OnFixedTick` の `Tick()`。
- **Rationale**: 現行の uOSC も `Update()` で apply しているため、値反映タイミング（7.3）、`receivedAtSeconds` の時刻源（メインスレッド `ITimeProvider` / `Time.unscaledTimeAsDouble`）、`Debug.Log` の発生スレッドがすべて既存と一致する。
- **Trade-offs**: 受信→反映に最大 1 フレームの遅延（既存と同じ）。
- **Follow-up**: `OscReceiverHost` が inactive の間はドレインされない（uOscServer と同じ）。リング容量で backlog を吸収し、あふれたら最古破棄。

### Decision: 受信ソケット API は `Socket.Receive`
- **Context**: Mono の `ReceiveFrom` は 338 byte/回確保。
- **Alternatives Considered**: (1) `ReceiveFrom` + where-allocation 方式 `EndPoint` 派生、(2) `Socket.Receive`、(3) `ReceiveFromAsync`（Unity Mono では確保あり）
- **Selected Approach**: (2)。bind は uOSC と同じ IPv6 dual-mode + ReuseAddress。停止は `Close()` でブロッキング `Receive` を解除。
- **Rationale**: 受信機能は送信元エンドポイントを使わない。追加クラス不要で最も単純。
- **Trade-offs**: 将来「送信元でフィルタ」を実装する場合は (1) が必要。
- **Follow-up**: Unity 6 Mono 上で `Socket.Receive` が確保ゼロであることは GC テストの受信スレッド計測で自動検証される。

### Decision: パケットリング = 配送リング（ロック保護 SPSC + ドレイン時コピー）
- **Context**: Req 1.1/1.2/1.5、view のライフタイム、drop-oldest の安全な実装。
- **Alternatives Considered**: (1) lock-free SPSC（drop-oldest は consumer と競合し torn read を生む）、(2) スロット pin + generation 検査、(3) ロック保護 + ドレイン時に消費側バッファへ memcpy
- **Selected Approach**: (3)。`Socket.Receive` は予約済みスロットへ直接書き込む。解析結果（レコード）はスロットに付随する固定長配列へ。コミット・ドレイン・最古破棄はすべて 1 本のロック下で行い、ドレインは `[head, tail)` をメインスレッド所有の `OscDrainBuffer` へ memcpy してから即座にスロットを解放する。
- **Rationale**: ロック取得は受信スレッドが 1 データグラムあたり 2 回（予約・コミット）、メインスレッドが 1 フレーム 1 回。保持時間は µs オーダー。drop-oldest が自明に正しく、view はドレインバッファ上で「次のドレインまで有効」という単純なライフタイム規則になる。既存の「copy-forward + lock」規約（`OscDoubleBuffer`）と同じ思想。
- **Trade-offs**: 1 フレームあたり数 KB の memcpy（無視できる）。
- **Follow-up**: `DatagramSlotBytes` × `DatagramSlotCount` × 2（リング + ドレイン）が受信 1 体あたりの固定メモリ。既定 2048 B × 32 = 64 KB × 2 + レコード領域。

### Decision: アドレス解決は「長さ + FNV-1a ハッシュ → バケット → バイト完全一致」の不変スナップショット
- **Context**: Req 3.x。gap-analysis (c) の候補。
- **Alternatives Considered**: ソート済み配列の二分探索（比較コストがアドレス長×log n）、prefix 分岐（最終的に name lookup が必要）
- **Selected Approach**: メインスレッドの `OscAddressKeyTableBuilder` が UTF-8 キーを事前生成（`OscAddressFormatter` の UTF-8 生成規則を流用、受信側 `Dictionary<string, byte[]>` プールで重複排除）し、`Build()` で不変 `OscAddressKeyTable`（version 付き）を生成、`Volatile.Write` で公開。既存の解決順序（完全一致 → VRChat/ARKit prefix + BlendShapeName フォールバック、重複は後勝ち）は**テーブル構築時にキーとして展開**して再現する。
- **Rationale**: ホットパスは hash 計算 + 1 回のバイト比較で O(アドレス長)。gaze / 制御 / listener も同じテーブルで解決できる。
- **Trade-offs**: マッピング更新のたびにテーブル再構築（低頻度、確保許容）。テーブル version 不一致のレコードはメインスレッドで破棄（最大 1 フレーム分、マッピング再構築時のみ）。

### Decision: 制御メッセージはバイト列のまま配送し、メインスレッドで unchanged 判定後に string 化
- **Context**: Req 4.x、既存 heartbeat-unchanged 0 byte テスト。
- **Alternatives Considered**: 受信スレッドで string 化（毎パケット確保 → 不可）、専用 arena へコピー（リングと二重管理）
- **Selected Approach**: 全メッセージの要素バイト列はドレインバッファ上にあるため、制御メッセージはそのバイト列から `OscMessageView` を再構成して binding の struct view 受け口へ渡す。binding は heartbeat / gaze 広告を固定 byte scratch に累積し、FNV-1a（byte）で前回と比較、変化時のみ `Encoding.UTF8.GetString` で既存の string ベース処理へ移行。sender_id は受信スレッドで `Guid` + `long` に解決（string 化なし）。preset は現在値の UTF-8 と比較し、変化時のみ string 化。
- **Rationale**: 制御メッセージ非到着フレーム 0 byte（4.6）と heartbeat 到着フレームの最小確保（4.7）を両立し、facade 経由の既存 0 byte テストも維持できる。
- **Trade-offs**: heartbeat 変化時の確保は既存と同等（string 群 + マッピング再構築）。

### Decision: `HandleOscMessage(uOSC.Message)` facade は「ワイヤ化 → 同一パーサ → 同期 apply」
- **Context**: Req 5.3/5.4、EditMode テストは `HandleOscMessage` 直後に結果を assert する（tick なし）。
- **Alternatives Considered**: uOSC.Message を直接 binding に流す分岐を残す（経路二重化 → 挙動乖離リスク）
- **Selected Approach**: `OscMessageSerializer` が `uOSC.Message` を受信側スクラッチ（一度だけ確保）へ OSC ワイヤ形式で書き出し（bundle timestamp が bundle 値なら `#bundle` で包む）、`OscDatagramRing.CommitExternal` で同じ解析・分類を通し、直後に `PumpReceived()` で同期 apply する。
- **Rationale**: 「UDP 経路と同一結果」（5.4）が構造的に保証される。既存テストの同期期待も満たす。
- **Trade-offs**: uOSC.Message → view は一方向。view → uOSC.Message は作らない（ユーザー決定）。

### Decision: `List<BufferedValue>` / `List<GazeSample>` のプール化
- **Context**: Req 6.1/6.2。
- **Selected Approach**: `OscBundleAccumulator` と binding の gaze 経路に `Stack<List<T>>` プール（初期 4 本、`Queue` は初期容量 8）を持たせ、`ApplyFrame` 後に `Clear()` して返却。`Clear()` 済みのみ再利用するため前 bundle の混入はない。
- **Trade-offs**: バースト時のみプールが拡張（確保）される。

### Decision: 警告・エラーの発生スレッドと一度きり通知
- **Selected Approach**: 受信スレッドは `OscReceiveDiagnostics` の `Interlocked` カウンタ（drop / oversized / truncated / malformed）を増やすだけ。メインスレッドのドレイン時に「0 → 非 0」遷移を検出して一度だけ `Debug.LogWarning`（`LogAssert` で検証可能）。socket の致命的例外（bind 失敗・受信ループ停止）は受信スレッドから `Debug.LogError/LogException`（Req 10.7 の例外規定）し `Faulted` を立てる。
- **Rationale**: ホットパスからログを排除しつつ、テストで決定的に検証できる。

## Risks & Mitigations
- Mono 上で `Socket.Receive` が想定外に確保する — GC テストの受信スレッド計測（`GetAllocatedBytesForCurrentThread` 差分）で検出。発生時は where-allocation 方式へ切替（`OscUdpReceiveLoop` 内部に閉じる）。
- ProfilerRecorder がワーカースレッドを集計しない — positive control で検出し、authoritative を切替（design.md Testing Strategy）。
- 第三者送信元（VRChat / iFacialMocap）のパケットが uOSC と異なる解釈になる — リーダー単体テストで uOSC の受理集合を上位互換で再現（`T/F` no-payload、未知タグはメッセージ単位スキップ）。
- キーテーブル version 不一致でマッピング更新直後の 1 フレーム分を破棄する — マッピング更新時は `OscDoubleBuffer` 自体が作り直されるため既存でも値は初期化される。差分は観測不能。
- 10 体以上で受信スレッド 10 本 — uOSC は 1 サーバー 2 スレッド（UDP + parser）だったため総スレッド数は減る。
- `_messageFilter`（uOSC.Message ベース）を外部が使っている可能性 — 公開 API は残し、UDP 経路では無視して `StartReceiving` 時に一度だけ警告。リポジトリ内利用は binding のみ（移行対象）。

## References
- [vis2k/where-allocation](https://github.com/vis2k/where-allocation) — Mono `Socket.ReceiveFrom` の確保内訳と回避策
- [dotnet/runtime #30196 Udp Socket.ReceiveFrom a lot of allocations](https://github.com/dotnet/runtime/issues/30196)
- [dotnet/runtime #30797 Zero allocation connectionless sockets](https://github.com/dotnet/runtime/issues/30797)
- [Unity ProfilerRecorderOptions](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorderOptions.html)
- [Unity Manual: Tracking garbage collection allocations (6000.3)](https://docs.unity3d.com/6000.3/Documentation/Manual/performance-track-garbage-collection.html)
- [GC.GetAllocatedBytesForCurrentThread](https://learn.microsoft.com/dotnet/api/system.gc.getallocatedbytesforcurrentthread)
- `.kiro/specs/osc-receive-zero-alloc/gap-analysis.md` — 本設計の一次入力（Option C / 案1 承認済み）
- `docs/backlog.md` M-16 — 据え置きとする uOSC vendor copy 方針
