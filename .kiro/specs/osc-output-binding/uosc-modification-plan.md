# uOSC 内部改修計画（GC ホットパス zero-alloc 化）

## 目的

`osc-output-binding` spec の Requirement 10.1（`OnLateTick` ホットパスで GC アロケーションゼロ）を満たすため、uOSC ライブラリ（`com.hidano.uosc`）を **vendor copy 化**し、送受信ホットパスの GC を完全に排除する。

現状の `com.hidano.uosc` は `Library/PackageCache/com.hidano.uosc@f7a52f0c524d/` に展開されており、Bundle / Message / Writer / Parser / uOscClient / uOscServer の **全層に毎呼出で GC を発生させる構造**になっている。本計画書は、これら GC 発生源を特定し、改修方針と新 API、影響範囲、段階的実装計画を整理する。

---

## 1. 現状の GC ホットスポット一覧

### 送信側

| # | ファイル / 行 | コード | GC 内容 |
|---|---|---|---|
| S1 | `Runtime/uOscClient.cs:95` | `using (var stream = new MemoryStream())` | 1 メッセージごとに `MemoryStream` alloc（内部の `byte[256]` 初期バッファ含む） |
| S2 | `Runtime/uOscClient.cs:124` | `messages_.Enqueue(data)` | `Message` struct を `object` として Queue に渡すため **boxing** 発生 |
| S3 | `Runtime/uOscClient.cs:133` | `public void Send(string, params object[] values)` | 呼び出し側で `object[]` alloc + 各値の boxing（float / int を入れると毎回 box） |
| S4 | `Runtime/Core/Message.cs:11` | `public object[] values;` | float などプリミティブを格納するときの boxing は不可避 |
| S5 | `Runtime/Core/Message.cs:39-49` | `string types = ","; types += Identifier.Float;` | string concat による複数 string allocation（4 byte / op） |
| S6 | `Runtime/Core/Bundle.cs:53,62` | `using (var tmpStream = new MemoryStream())` | bundle 内 element ごとに **2 重 MemoryStream alloc**（outer + inner） |
| S7 | `Runtime/Core/Bundle.cs:10` | `private List<object> elements_ = new List<object>();` | element 追加で内部配列 growth + Message struct boxing |
| S8 | `Runtime/Core/Writer.cs:15,22,29` | `var byteValue = BitConverter.GetBytes(value); Array.Reverse(byteValue);` | int / float / Timestamp を書くたびに 4 byte の `byte[]` alloc + `Array.Reverse` 呼び出し |
| S9 | `Runtime/Core/Writer.cs:36` | `var byteValue = Encoding.UTF8.GetBytes(value);` | string ごとに UTF-8 byte[] alloc |

### 受信側

| # | ファイル / 行 | コード | GC 内容 |
|---|---|---|---|
| R1 | `Runtime/Core/DotNet/Udp.cs:62` | `var buffer = udpClient_.Receive(ref endPoint_);` | `UdpClient.Receive` が**毎回新規 `byte[]` を alloc** して返す（.NET BCL 仕様） |
| R2 | `Runtime/Core/DotNet/Udp.cs:62` | `udpClient_.Receive(ref endPoint_)` | endPoint_ は更新されるが、`Queue<byte[]>` には endpoint 情報が含まれないため、上流に伝えるには **改修必須**（R3 関連） |
| R3 | `Runtime/Core/DotNet/Udp.cs:65` | `messageQueue_.Enqueue(buffer)` | `Queue<byte[]>` の内部配列 growth |
| R4 | `Runtime/Core/Parser.cs:44` | `messages_.Enqueue(new Message() { ... })` | Message struct を Queue<Message> へ enqueue（struct 自体は alloc 無いが Queue が内部配列 growth） |
| R5 | `Runtime/Core/Parser.cs:102` | `var data = new object[n];` | メッセージごとに `object[]` alloc + 各 float の boxing |
| R6 | `Runtime/Core/Reader.cs` | `Reader.ParseString(buf, ref pos)` | string alloc（毎メッセージ × 名前 1 個） |

### 1 フレームあたりの推定 GC 量（60fps、BlendShape 100 + Gaze 1）

- 送信側: Bundle 1 個 + Message 101 個 + Writer call ~400 回 + 上記 boxing
  - 概算 **8〜12 KB / frame** (現状)
  - heartbeat フレーム（5 秒に 1 回）は **+ 数 KB**
- 受信側（Process B）: byte[] buffer × N（パケット数）+ Parser 内 object[] × N
  - 概算 **3〜5 KB / frame** (現状)

合計で**毎フレーム 10 KB 超**の GC 圧。Profiler.GetTotalAllocatedMemoryLong 差分で容易に観測される。

---

## 2. 改修方針

### A. Message を SoA + Span ベースに置き換え（送信側 / 受信側両方）

現状 `Message.values: object[]` は型混合 + boxing の主因。改修後:

```csharp
public struct OscMessage  // 改修後
{
    public string address;
    public Timestamp timestamp;

    // SoA: 型ごとに別配列、index 列で順序を保持
    public TypeTag[] typeTags;   // 型タグの順序付き列 (struct enum)
    public float[] floatValues;
    public int[] intValues;
    public OscStringRef[] stringValues;  // string 参照ハンドル（後述）
    public int typeCount;        // 実際に使われている要素数（配列は再利用）
}

public enum TypeTag : byte { Int, Float, String, Blob, True, False }

public readonly struct OscStringRef
{
    public readonly int bufferStart;  // 内部 string buffer のインデックス
    public readonly int length;
}
```

これにより:
- `object[]` → 型別配列で boxing 排除
- 配列は `OscMessageBuilder` がプール / 再利用するため alloc 発生なし
- string は事前計算済みアドレス文字列（Req 4 の案 B でプリセットから組み立て）を **`byte[]` UTF-8 バッファに事前エンコード**して `OscStringRef` だけ持つ → Writer での `Encoding.UTF8.GetBytes` も排除

### B. Writer を Span + BinaryPrimitives ベースに書き換え（送信側）

```csharp
// 改修後
public static class OscWriter
{
    public static void WriteInt32(Span<byte> buffer, ref int offset, int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(offset), value);
        offset += 4;
    }

    public static void WriteFloat32(Span<byte> buffer, ref int offset, float value)
    {
        BinaryPrimitives.WriteSingleBigEndian(buffer.Slice(offset), value);
        offset += 4;
    }

    public static void WriteAlignedBytes(Span<byte> buffer, ref int offset, ReadOnlySpan<byte> data)
    {
        data.CopyTo(buffer.Slice(offset));
        offset += data.Length;
        int padding = (4 - (data.Length & 3)) & 3;
        buffer.Slice(offset, padding).Clear();
        offset += padding;
    }
}
```

- `BitConverter.GetBytes` / `Array.Reverse` を `BinaryPrimitives.Write*BigEndian` 1 行に置き換え
- 出力先は呼出側が提供する `Span<byte>`（ArrayPool から借りたバッファ）
- string は事前エンコード済み `byte[]` を `WriteAlignedBytes` で書き込み

### C. Bundle を再利用可能な struct + ring buffer 構造に置き換え（送信側）

```csharp
public struct OscBundle  // 改修後
{
    public Timestamp timestamp;
    public OscMessageRef[] messageRefs;  // 別途プールされた OscMessage[] のインデックス
    public int messageCount;

    public int CalculateSerializedSize(OscMessagePool pool) { ... }

    public void Write(Span<byte> buffer, OscMessagePool pool, out int written)
    {
        // outer header
        OscWriter.WriteAlignedBytes(buffer, ref offset, Identifier.BundleBytes);
        OscWriter.WriteUInt64(buffer, ref offset, timestamp.value);
        // each message
        for (int i = 0; i < messageCount; i++)
        {
            ref OscMessage msg = ref pool.Get(messageRefs[i]);
            int sizeStart = offset;
            offset += 4;  // size placeholder
            msg.Write(buffer, ref offset);
            int msgSize = offset - sizeStart - 4;
            BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(sizeStart), msgSize);
        }
        written = offset;
    }
}
```

- 内部 `List<object>` → 固定容量 `OscMessageRef[]`（プール参照）
- 2 重 MemoryStream → 単一 `Span<byte>` バッファに直接書き込み + サイズ placeholder の事後埋め込み

### D. uOscClient の queue を byte[] + length ペアの ring buffer に置き換え（送信側）

```csharp
public class OscClient  // 改修後
{
    private struct OutgoingPacket
    {
        public byte[] Buffer;
        public int Length;
    }

    private OutgoingPacket[] _ringBuffer;  // 固定容量、ArrayPool<byte> から借りる buffer を再利用
    private int _writeIndex, _readIndex;

    // メインスレッドから呼ぶ:
    public void Enqueue(ReadOnlySpan<byte> serializedPacket) { ... }

    // worker thread:
    private void WorkerLoop()
    {
        while (running)
        {
            if (TryDequeue(out var packet))
            {
                _udp.Send(packet.Buffer, packet.Length);
                _bufferPool.Return(packet.Buffer);
            }
        }
    }
}
```

- 呼び出し側が `OscBundleBuilder` でパケット byte[] を作成済みの状態で渡す
- queue に渡るのは `OutgoingPacket` struct（boxing なし）
- 送信完了後に `ArrayPool<byte>.Return` で返却

### E. 受信側: UdpClient を Span 経由 Receive に置き換え（受信側）

`UdpClient.Receive` は内部で毎回 `new byte[]` するため、**より低レベルの `Socket.ReceiveFrom`** に置き換える:

```csharp
public class OscServer  // 改修後
{
    private Socket _socket;
    private byte[] _receiveBuffer = new byte[65536];  // MTU 上限 + 安全余裕
    private EndPoint _remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

    private void ReceiveLoop()
    {
        while (running)
        {
            int received = _socket.ReceiveFrom(_receiveBuffer, ref _remoteEndPoint);
            // _remoteEndPoint に送信元 IP+port が入る
            ProcessPacket(_receiveBuffer.AsSpan(0, received), (IPEndPoint)_remoteEndPoint);
        }
    }
}
```

- `Socket.ReceiveFrom` は alloc free（既存バッファに書き込む）
- `_remoteEndPoint` が **送信元 IP** を保持（**ただし R3 で削除確定のため allowlist 用には使わないが、ゾンビ排除のための送信元 IP ログには使える**）

### F. Parser を SoA struct + ref struct enumerator に書き換え（受信側）

```csharp
public ref struct OscPacketParser
{
    private ReadOnlySpan<byte> _buffer;
    private int _pos;
    private ulong _bundleTimestamp;

    public bool TryParseNext(out OscMessageView message)
    {
        // bundle / message を遅延 parse、message は Span 参照を持つ ref struct
    }
}

public readonly ref struct OscMessageView
{
    public ReadOnlySpan<byte> AddressBytes { get; }   // UTF-8 string 参照
    public ulong TimestampBits { get; }
    public ReadOnlySpan<byte> TypeTags { get; }
    public ReadOnlySpan<byte> Payload { get; }

    public float ReadFloat(int valueIndex);
    public int ReadInt(int valueIndex);
}
```

- `object[] data = new object[n]` を排除し、values を `Span<byte>` で遅延読み出し
- アドレス文字列は受信側 mapping table での dictionary lookup 用に `OscAddressHash`（UTF-8 → uint64 ハッシュ）で照合し、string alloc を回避

---

## 3. 改修後の API（FacialControl 側から見た形）

### 送信側（`OscSender` から `OscClient` を呼ぶ）

```csharp
// 改修後の使い方
var builder = _bundleBuilder;  // OnStart で確保、再利用
builder.Begin(timestamp);

for (int i = 0; i < blendShapeCount; i++)
{
    builder.AddFloat(addressBytesPool[i], blendShapes[i]);
    // addressBytesPool は OnStart 時に事前 UTF-8 エンコード済み byte[][]
}

builder.End(out var packetBuffer, out var packetLength);
_oscClient.Enqueue(packetBuffer, packetLength);
// packetBuffer は OscClient 内で worker が send → ArrayPool に返却
```

### 受信側（`OscReceiver` で listener に dispatch）

```csharp
// 改修後の受信ループ
foreach (ref readonly var message in _oscServer.PendingMessages)
{
    if (_mappingTable.TryLookup(message.AddressBytes, out int blendShapeIndex))
    {
        float value = message.ReadFloat(0);
        _doubleBuffer.WriteValue(blendShapeIndex, value, message.TimestampBits);
    }
    else if (_metaListener.TryDispatch(message))  // sender_id / heartbeat 等
    {
        // メタデータ処理
    }
}
```

---

## 4. 影響範囲（FacialControl 側の波及）

| 場所 | 改修内容 |
|---|---|
| `Runtime/Adapters/OSC/OscSender.cs` | `uOSC.uOscClient.Send(string, params object[])` 呼び出しを `OscBundleBuilder` + `OscClient.Enqueue(Span<byte>)` に置き換え |
| `Runtime/Adapters/OSC/OscSender.cs` | `Configure` 内で BlendShape 名 → UTF-8 byte[] の事前エンコード、`OscBundleBuilder` 初期化 |
| `Runtime/Adapters/OSC/OscReceiver.cs` | `uOSC.uOscServer.onDataReceived` イベント → `OscMessageView` を受け取る経路に変更。`RegisterAnalogListener(string address, ...)` を内部で `OscAddressHash` ベース照合に変更 |
| `Runtime/Adapters/OSC/OscReceiverHost.cs` | uOscServer → OscServer 差し替え |
| `Runtime/Adapters/OSC/OscMappingTable.cs` | `Dictionary<string, int>` → `Dictionary<OscAddressHash, int>` 等、ホットパスを byte 比較化（または事前ハッシュ化） |
| `Runtime/Adapters/InputSources/OscInputSource.cs` | `OscDoubleBuffer.WriteTick` 連動部分は変更なし、API 内部のみ |
| `Runtime/Adapters/InputSources/ArKitOscAnalogSource.cs` | リスナー登録 API が address byte[] に変わるため改修 |
| `Tests/PlayMode/Integration/OscSendReceiveTests.cs` | `uOSC.Message` を直接使うテストヘルパーを新 API に置き換え |
| `Tests/EditMode/Adapters/OSC/OscMappingTableTests.cs` | mapping table のキー型変更に伴う改修 |

---

## 5. vendor copy 化の手順

### Step 1: パッケージを Packages/ 配下にコピー

```
Library/PackageCache/com.hidano.uosc@f7a52f0c524d/
  → Packages/com.hidano.uosc/
```

`FacialControl/Packages/manifest.json` を編集して `com.hidano.uosc` の依存指定を:
- 削除前: `"com.hidano.uosc": "git+ssh://git@github.com/hecomi/uOSC.git#xxx"` 等
- 変更後: ローカル参照（`"com.hidano.uosc": "file:Packages/com.hidano.uosc"`）または local embed として `Packages/com.hidano.uosc/package.json` を直接認識させる

### Step 2: フォーク識別（ライセンス / クレジット）

uOSC は MIT License（hecomi 氏）。vendor copy + 改修を行う場合:
- `Packages/com.hidano.uosc/package.json` の `name` を `com.hidano.uosc` のまま維持し、`version` を `1.0.0-fcfork.1` 等 fork suffix を付与
- `Packages/com.hidano.uosc/README.md` / `CHANGELOG.md` に "FacialControl fork: zero-alloc 化のため内部 API を改修" の明示
- 元 LICENSE は維持し、改修部分の著作権表記を Hidano 名義で追記

### Step 3: 既存 API の段階的置き換え

uOSC の既存 public API（`uOscClient` / `uOscServer` / `Message` / `Bundle`）は **しばらく残し**、内部実装のみ差し替える Phase 1 と、最終的に新 API に統一する Phase 2 に分ける（後述の段階的計画）。

### Step 4: `package.json` 更新

```json
{
  "name": "com.hidano.uosc",
  "version": "1.0.0-fcfork.1",
  "displayName": "uOSC (FacialControl fork)",
  "description": "Forked from hecomi/uOSC for zero-alloc hot path support.",
  "unity": "2022.3",
  "samples": []
}
```

---

## 6. 段階的実装計画

### Phase 1: 送信側ホットパス zero-alloc 化（優先）

**目標**: `OnLateTick` での bundle 送信を完全に zero-alloc 化する。

1. `OscMessage` SoA struct + `OscMessagePool` を `Packages/com.hidano.uosc/Runtime/Core/Modern/` 配下に新設
2. `OscWriter`（Span + BinaryPrimitives）を新設
3. `OscBundleBuilder`（Bundle build + パケット生成、ArrayPool バッファ）を新設
4. `OscClient`（新規、byte[] + length ring buffer ベース queue + worker thread）を新設
5. 既存 `uOscClient` / `Bundle` / `Message` / `Writer` は **互換 façade として残す**（既存テスト 通過のため）
6. FacialControl 側で `OscSender` / `OscSender` を新 API に切り替え
7. EditMode テスト: `OscWriter` の zero-alloc 検証、`OscBundleBuilder` のラウンドトリップ
8. PlayMode テスト: `Profiler.GetTotalAllocatedMemoryLong` 差分で **送信 100 フレームで 0 byte** を確認

**規模**: M+ (1〜2 週間)

### Phase 2: 受信側ホットパス zero-alloc 化

**目標**: 受信パケット parse + dispatch を zero-alloc 化する。

1. `OscServer`（新規、`Socket.ReceiveFrom` + 固定 receive buffer + ring buffer）を新設
2. `OscPacketParser`（ref struct、Span 経由遅延 parse）を新設
3. `OscAddressHash` + mapping table を改修（FacialControl 側）
4. 既存 `uOscServer` / `Parser` / `Reader` は互換 façade として残す
5. FacialControl 側で `OscReceiver` / `OscReceiverHost` / `OscMappingTable` を新 API に切り替え
6. PlayMode テスト: 受信 100 フレームで 0 byte を確認

**規模**: M+ (1〜2 週間)

### Phase 3: 旧 API 撤去 + cleanup

**目標**: 互換 façade を撤去し、uOSC fork を新 API のみに整理。

1. 旧 `uOscClient` / `Message` / `Bundle` / `Writer` / `Parser` を削除
2. `Packages/com.hidano.uosc/CHANGELOG.md` に 1.0.0-fcfork.1 breaking change を明記
3. 外部から uOSC を使っている箇所（FacialControl 内に他に無いはず）を確認

**規模**: S (1〜3 日)

---

## 7. リスクと未確定論点

### 7.1 `Bundle` 内 sub-bundle のサポート
現状 `Bundle.Add(Bundle)` で nested bundle を入れられる。osc-output-binding spec ではこれを使わないため、改修後の `OscBundleBuilder` は **flat な Message のみ受け付ける**設計で十分（nested は将来必要時に追加）。

### 7.2 既存 `Message.values: object[]` の string / blob 対応
osc-output-binding spec では送信値は **float のみ**（BlendShape weight 0〜1、Gaze x/y）+ heartbeat の string（BlendShape 名一覧）。string 送信は heartbeat のみのため、heartbeat 専用 path で扱えば常時 alloc は回避できる。

### 7.3 uOSC fork のアップストリーム追従
hecomi/uOSC が将来更新されても vendor copy なので自動追従しない。preview.2 では fork 維持、必要に応じて手動で差分取り込み（CHANGELOG に記録）。

### 7.4 `Socket.ReceiveFrom` の同期 / 非同期
現状 `UdpClient.Receive` は `udpClient_.Available > 0` をループで polling。改修後は blocking `Socket.ReceiveFrom` + 別 thread に置き換える（既存 thread 構造を維持）。タイムアウトは `Socket.ReceiveTimeout` で設定。

### 7.5 `OscAddressHash` 衝突
UTF-8 byte 列の hash 衝突は実用上ほぼ問題ない（XXHash64 等で 1/2^64 確率）。ただし VRChat 標準アドレスとの衝突を防ぐため、テストで主要 100 名の衝突有無を verify。

---

## 8. 関連ドキュメント

- 全体 spec: [requirements.md](requirements.md)
- ギャップ分析: [gap-analysis.md](gap-analysis.md)
- 既存 uOSC ソース: `D:\Personal\Repositries\FacialControl\FacialControl\Library\PackageCache\com.hidano.uosc@f7a52f0c524d\Runtime\`
- uOSC オリジナル: [hecomi/uOSC](https://github.com/hecomi/uOSC) （MIT License）

## 9. 検証指標（Phase 完了基準）

| 指標 | 目標 |
|---|---|
| 送信ホットパスの GC | `Profiler.GetTotalAllocatedMemoryLong` 差分 = 0 byte / 100 フレーム |
| 受信ホットパスの GC | 同上 |
| heartbeat フレームの GC | 5 秒に 1 回、合計 < 1 KB / 60 秒（既存 mapping table 文字列の参照渡しなら 0 byte 達成可能） |
| 既存テストの通過 | `OscIntegrationTests` / `OscSendReceiveTests` の全テスト緑 |
| パケット往復検証 | 送信 → 受信ループバックで byte-perfect 一致 |
