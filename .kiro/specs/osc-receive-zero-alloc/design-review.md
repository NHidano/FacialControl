# Design Review (codex, read-only, 2026-09-15)

> レビュー時点の design.md に対する指摘。必須修正 1〜4 と推奨修正 2〜6・8 は同日に design.md へ反映済み（推奨 1 は Open Questions に追記、推奨 7 はコード確認の結果 定常確保なしと判定）。

## 総評

設計は、固定リング・Span パーサ・UTF-8 キーテーブル・struct view という主要方針を明確にしており、Req 1.1–10.7 の大半をトレーサビリティ表でカバーしています。

ただし、現行の `SetMessageFilter` と新しい `IOscResolvedMessageHandler` の併用、および `Thread.Join(500ms)` タイムアウト後の資源処理に、実装時に機能重複・データ競合・use-after-stop を起こし得る blocking issue があります。

## 必須修正（blocking）

1. **重要 — facade 経由でメッセージが二重処理される**

   - 場所: `design.md:652–654, 717–724`、`OscReceiverAdapterBinding.cs:1104–1141`
   - 証拠: 設計は `HandleOscMessage(uOSC.Message)` 内で `_messageFilter` を呼び、その後 `CommitExternal` → `PumpReceived` → `HandleIncomingOscMessage(in view, in resolved)` を実行する。
   - 現行 filter は通常の BlendShape で `true` を返しつつ `MarkAcceptedPacket()` を実行する。新 handler でも同じ処理を行うため、通常値・gaze・sender 判定・staleness 更新が二重化する。制御メッセージは filter が `false` を返すため新経路で処理されず、通常メッセージだけ二重処理になる。
   - 修正案: facade 経路を「旧 filter を呼ぶ互換分岐」と「新 handler に委譲する分岐」に明確に分離する。少なくとも、filter が `true` を返した後に新 handlerを再実行しない契約を設ける。既存直接呼び出しテストを維持するため、facade 専用の legacy apply または一回限りの filter bypass を明示する。

2. **重要 — `Join(500ms)` 失敗後のリング再利用・破棄が安全でない**

   - 場所: `design.md:237, 382`、`OscReceiverHost.OnDestroy`、`OscReceiver.StopReceiving` の新設計
   - 証拠: 設計は `Socket.Close()` → `Thread.Join(500ms)` の後にリングを `Clear()` するとしている。しかし `Join` がタイムアウトした場合、受信スレッドがまだ予約済みスロットへ書き込み中である可能性がある。
   - その状態で `Clear()`、再 `StartReceiving()`、`Dispose()`、GameObject 破棄を行うと、受信スレッドとメインスレッドの lost update、古いスレッドの残存、リングへの同時書き込みが発生する。
   - 修正案: `Join` が失敗した場合はリングを再利用・破棄せず、`Faulted/Stopping` のまま再起動を禁止する。受信スレッドの終了確認後だけ `Clear`・再構成・Dispose を許可する。Windows/Mono で `Close` が確実に解除できないケースをテスト契約に含める。

3. **重要 — 溢れ処理と reserved slot の不変条件を実装レベルで定義不足**

   - 場所: `design.md:395–436`、スレッド／ロック契約表
   - 証拠: `TryReserveSlot` は満杯時に `head` を進め、常に 1 slot を reserved とする設計だが、`Drain` は `[head, tail)` を処理すると記載されている。reserved slot の状態、`head/tail` の意味、未 commit slot を consumer が絶対に参照しない条件が形式化されていない。
   - 特に、producer が slot を予約した直後にブロックしている間、consumer がどのインデックスまで drain 可能かが曖昧である。
   - 修正案: `committedTail` と `reservedTail` を分離するか、slot state (`Free/Reserved/Committed`) を定義する。満杯時の head advance、Abort、drop-oldest、wrap-around を状態遷移表と不変条件で明記し、並行テストを追加する。

4. **重要 — `ReconfigureMappings` とテーブル版・buffer resize の原子性が不足**

   - 場所: `design.md:212–213, 331, 754`、`OscReceiverAdapterBinding.cs:1624–1644`
   - 証拠: 現行コードは `BuildNormalLookup` → `_buffer.Resize` → 新 accumulator 作成 → `ReconfigureMappings` の順で処理する。設計は table version 不一致で古い record を破棄するが、buffer、mapping table、accumulator、binding の runtime mapping の公開順序を一つの原子操作として定義していない。
   - heartbeat 到着中にこの処理が走ると、古い record を破棄できても、新 buffer と新 table の組み合わせが一時的に不整合になる。
   - 修正案: `ReconfigureMappings` に buffer・mapping table・accumulator をまとめて渡し、単一の main-thread commit point で交換する。`TableVersion` の採番、公開順序、旧リング record の扱いを明記する。

## 推奨修正（non-blocking）

1. **重要 — `Socket.Receive` の oversized datagram 契約を実機検証する**

   - 場所: `design.md:317, 369–382`
   - 証拠: 設計は `SocketException(MessageSize)` を前提に超過 datagram を検出するが、Windows/Mono の UDP `Receive(byte[], int, int, SocketFlags)` では、実装やソケット設定によって切り詰め値が返る場合がある。
   - 修正案: `Receive` の戻り値・例外・切り詰め判定を Unity 6000.3.19f1 で確認し、`OversizedDatagramCount` の判定方法をテストで固定する。

2. **重要 — heartbeat 到着フレームの除外方法が非決定的**

   - 場所: `design.md:802–815`
   - 証拠: `HeartbeatArrivalCount` を各フレーム末に読む方式では、UDP 到着、`Update`、`FixedUpdate`、`yield return null` のタイミングがずれ、heartbeat が隣接フレームに分類される可能性がある。
   - 修正案: heartbeat の送信停止・到着確認・drain 完了を明示的に待つ。フレーム番号ではなく、テスト用 sequence または適用済み datagram count で測定区間を確定する。`yield return null` だけに依存せず、明示的な `Update`/drain 完了条件を使う。

3. **重要 — `ProfilerRecorder` positive control の自己判定を受入基準から分離する**

   - 場所: `design.md:806–812`
   - 証拠: positive control の結果に応じて authoritative 計測器を切り替える設計は、計測器が壊れている場合でもテストを通す余地がある。
   - 修正案: `ProfilerRecorder` が worker thread を観測できない場合は「性能テスト PASS」ではなく「環境制限として inconclusive」と記録する。M2 は補助測定ではなく、受信スレッド allocation の必須ゲートとして独立 assert する。

4. **中 — `OscMessageSerializer` の公開範囲を縮小する**

   - 場所: `design.md:688–704`
   - 証拠: `GetRequiredLength` と `TryWrite` を public static API として追加しているが、Req 5.3/5.4 は facade 互換を要求するだけで、外部公開 API の追加までは要求していない。
   - 修正案: serializer を internal または `OscReceiver` 内部実装にする。外部利用が必要なら、その理由と API 安定性を明記する。

5. **中 — OSC type tag の拡張を削減または明示する**

   - 場所: `design.md:484–487, 756`
   - 証拠: `h d t N I` を既知タグとして追加しているが、requirements 2.4 の必須型は `i f s b` であり、現行 uOSC は `i f s b T F` を処理する。新たな既知タグ化は、従来 unknown としてスキップされた入力の可観測挙動を変更する。
   - 修正案: sender identity の `long` に必要な `h` だけを要件根拠付きで追加し、それ以外は backlog に移す。`T` の staleness semantics は明示的に維持する。

6. **中 — `Guid` の byte order と string parse の同値性をテストする**

   - 場所: `design.md:626`
   - 証拠: 現行コードは `new Guid(byte[])` を使用しており、OSC blob の 16 byte 表現と textual GUID 表現で byte order の解釈差が起こり得る。
   - 修正案: blob/string の同一 sender が同じ `SenderIdentity` になることを、固定バイト列で明示的に検証する。

7. **中 — `ZombieEvictionPolicy.Observe` を定常経路の性能要件から分離する**

   - 場所: `design.md:849`
   - 証拠: 現行 `Observe` は sender 更新時に Dictionary 全体を走査し、sender 切替時にはログ文字列も構築する。通常の float だけの経路では呼ばれないが、sender_id を毎フレーム送る workload では zero-alloc・CPU 要件の対象になり得る。
   - 修正案: sender control を性能 workload に含めるか、「通常経路」の定義から明示的に除外する。

8. **軽微 — 設定フィールドと Inspector 変更を backlog 候補にする**

   - 場所: `design.md:124–173, 756, 841`
   - 証拠: `receiveDatagramSlotBytes`、`receiveDatagramSlotCount`、`receiveSocketBufferBytes` の追加と drawer 表示変更は、固定容量で zero-alloc を達成する本体要件を超える。
   - 修正案: 今回は内部既定値に固定し、設定公開・JSON・Inspector は別仕様へ分離する。設定化する場合は容量変更時の再確保・runtime reconfiguration 契約を追加する。

## 確認済み（verified OK）

- **要件トレーサビリティ**: `design.md:241–275` に Req 1.1–10.7 の対応表があり、Req 1.2 も `Socket.Receive` として新しい許容表現に整合している。
- **現行 uOSC 経路の理解**: `uOscServer.Update()` が parser queue を drain し、`onDataReceived` から `HandleOscMessage` を呼ぶ構造を正しく把握している。
- **メッセージ順序**: 設計上、datagram 順序および datagram 内 element 順序を保存するため、sender_id → float の順序は維持可能。ただし上記 ring 不変条件の明文化が必要。
- **timestamp による sender decision cache**: `Dictionary<ulong,bool>` と bounded `Queue<ulong>` の方針は、同一 bundle timestamp 内の sender 判定を一貫させる設計として妥当。
- **`,T` メッセージ**: `design.md:724` で `HasFloat` に依存せず `MarkAcceptedPacket` する方針が明示され、現行実装の挙動を意識している。
- **analog listener の順序**: `Apply` 内で binding 処理後に listener を呼ぶ方針は、現行 `HandleOscMessage` の mapping 処理後 listener 呼び出しに整合する。
- **heartbeat / gaze の timestamp 累積**: 同一 timestamp で追記し、timestamp 変更で reset する現行 semantics を維持する方針は妥当。
- **view lifetime**: `design.md:328–331` で `OscMessageView` を handler 呼び出し中だけ有効と定義しており、binding が必要なデータを scratch/string に移す契約は明確。
- **facade の既存 API 維持方針**: `HandleOscMessage`、`SetMessageFilter`、analog listener、host の主要 API を残す方針は、既存 EditMode/PlayMode テスト互換の観点で正しい。
- **zero-alloc 候補 API**: `Socket.Receive(byte[], int, int, SocketFlags)`、`BinaryPrimitives`、`Guid(ReadOnlySpan<byte>)`、`long.TryParse(ReadOnlySpan<char>)`、`stackalloc`、FNV-1a span 処理はいずれも Unity 6 / .NET Standard 2.1 の検証対象として妥当。
- **送信側・uOSC vendor copy の非改修**: scope boundary と整合している。

## タスク分割の示唆

1. `OscPacketReader`、type tag、argument reader を最初に独立実装し、EditMode で hand-built packet と builder 出力を比較する。
2. `OscAddressKeyTable` と classifier を次に実装し、UTF-8 完全一致、prefix fallback、gaze route、sender identity を単独検証する。
3. ring は parser/classifier と分離して、`Reserved/Committed/Free`、drop-oldest、Abort、wrap-around、Drain を EditMode で検証する。
4. UDP receive loop は ring 完成後に実装し、bind、停止、socket close、join timeout、Faulted を PlayMode で検証する。
5. `OscReceiver.Apply` と table-version commit を実装し、`OscDoubleBuffer.Resize`・accumulator・mapping table の交換順序を統合テストする。
6. facade serializer は新経路の通常処理と分離して実装し、既存の `HandleOscMessage` 直接呼び出しテストを先に通す。
7. binding の struct handler は、sender decision → heartbeat/preset/gaze control → float routing → `MarkAcceptedPacket` の順序を一つの統合タスクとして実装する。ここは責務が大きく、単一 TDD タスクには大きすぎる。
8. gaze route snapshot、heartbeat chunk accumulation、AtomicSwap frame pool は別タスクに分割する。
9. 最後に既存機能回帰、UDP loopback、全スレッド GC 計測を実施する。GC テストは instrumentation correctness、worker-thread allocation、main-thread allocation、heartbeat 除外判定を別テストに分ける。

VALIDATION_ISSUES