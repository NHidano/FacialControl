# Research & Design Decisions — osc-output-binding

## Summary
- **Feature**: `osc-output-binding`
- **Discovery Scope**: Complex Integration（Domain 層の新規 bus、Adapters 層 binding 3 種、既存 binding の破壊的拡張、外部 OSC ライブラリの vendor copy + 改修、関連 Gaze データモデルの破壊変更、JSON 永続化拡張、Editor Drawer 群、サンプル 2 種、テスト網羅）
- **Key Findings**:
  - `FacialController.LateUpdate`（MonoBehaviour）と `AdapterBindingHost.LateTick`（VContainer `ILateTickable`）の dispatch 順は PlayerLoop 上で保証されないため、Adapters 側 binding が同フレーム送信を完結させるには **pull 型 Publish + binding 側 scratch buffer** の組み合わせが必要。
  - `com.hidano.uosc` は送受信ホットパス（Message / Bundle / Writer / Parser / Reader / Client / Server 全層）で毎フレーム数 KB の GC を発生させる。要件 10.1 を満たすには **vendor copy + 内部改修**が不可避（既に `uosc-modification-plan.md` でユーザー判断確定済み）。
  - uOSC Parser は bundle timetag を bundle 内 Message にコピーするため、これを bundle 識別キーに使うことで bundle アトミック適用が成立する。bundle 終端は **timestamp 変化 or N ms timeout** で確定するハイブリッド方式が現実解。
  - uOSC は受信時に送信元 IP を `UdpClient.Receive` 後に破棄するため、IP allowlist 方式は実装困難（ユーザー判断で Req から削除済み）。ゾンビ排除は送信元 UUID + 起動時刻のみで成立する。
  - `SystemTextJsonParser` は名前と裏腹に実装が `JsonUtility` ベース。要件 6.10 の文言は実装と整合する形（既存 JSON 経路を踏襲、新ライブラリ持ち込まない）に design 内で再表現する。
  - `GazeBindingConfig` を左右別 sourceId 必須に破壊変更する案は、InputSystem 側 `ExpressionBindingEntry` / 既存 Gaze テスト / JSON DTO の一斉改修を伴うが、ユーザー判断により preview 段階 migration 不要のため許容。

## Research Log

### FacialController と AdapterBindingHost.LateTick の Dispatch 順

- **Context**: Req 1.2 は「`OnLateTick` の直前」に Bus が通知すること、かつ「同フレーム送信完結」を要求している。FacialController.LateUpdate は MonoBehaviour、AdapterBindingHost.LateTick は VContainer の `ILateTickable` のため、PlayerLoop 上での相対順序が現状未制御。
- **Sources Consulted**:
  - `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Adapters/Playable/FacialController.cs` (LateUpdate 122-153)
  - `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Adapters/DependencyInjection/AdapterBindingHost.cs`
  - [VContainer Loop Documentation](https://vcontainer.hadashikick.jp/) — ILateTickable は PlayerLoop の `PostLateUpdate` カテゴリに挿入される
- **Findings**:
  - VContainer の `ILateTickable.LateTick` は `PostLateUpdate` カテゴリで dispatch される（Unity の PlayerLoop は `LateUpdate → PreLateUpdate → PostLateUpdate` の順）。MonoBehaviour の `LateUpdate` は `PreLateUpdate` カテゴリ。よって **FacialController.LateUpdate → AdapterBindingHost.LateTick の順は同フレーム内で保証される**。
  - DefaultExecutionOrder で MonoBehaviour 同士の順序を制御することは可能だが、MonoBehaviour vs VContainer の境界は PlayerLoop カテゴリ単位で決まる。
- **Implications**:
  - (案 a) pull 型 Publish: FacialController.LateUpdate 末尾で Bus.Publish → binding が observer で scratch buffer 更新 → 同フレームの AdapterBindingHost.LateTick で binding.OnLateTick → bundle 送信。この経路は同フレーム内で確実に成立する。
  - (案 b) DefaultExecutionOrder で FacialController を VContainer より前に強制配置: PlayerLoop カテゴリ越えには使えないため不要。
  - (案 c) frame counter スタンプ付き Bus + 1 フレーム遅延受容: 案 a が成立するため不要。
  - **採用**: 案 a。FacialController.LateUpdate 末尾 (`BoneWriter.Apply` の前) に `Bus.Publish` を挿入し、binding は `IFacialOutputObserver` 実装で scratch buffer を更新する。

### uOSC ライブラリの GC 発生源

- **Context**: Req 10.1 が `OnLateTick` ホットパスでヒープ確保ゼロを要求。既存 uOSC は毎フレーム数 KB の alloc を発生させる。
- **Sources Consulted**:
  - `FacialControl/Library/PackageCache/com.hidano.uosc@f7a52f0c524d/Runtime/` 配下のソース全般
  - [hecomi/uOSC GitHub](https://github.com/hecomi/uOSC) — MIT License、最終更新が古く upstream 修正期待困難
  - `uosc-modification-plan.md`（既に詳細分析済み）
- **Findings**:
  - 送信側 GC ホットスポット: `MemoryStream` alloc (uOscClient.cs:95)、`object` boxing (Queue<object>)、`params object[] values`、`string types += ...` 連結、`BitConverter.GetBytes` + `Array.Reverse`（Writer.cs:15,22,29）、UTF-8 `Encoding.GetBytes`。
  - 受信側 GC: `UdpClient.Receive` が毎回新 `byte[]` を返す（.NET BCL 仕様）、`Parser.cs:102` の `new object[n]`、各 float の boxing、`Reader.ParseString` の string alloc。
  - 60 fps + BlendShape 100 + Gaze 1 で送信側約 8〜12 KB / frame、受信側約 3〜5 KB / frame。
- **Implications**: vendor copy + Modern API（SoA `OscMessage`、`OscWriter`(Span+BinaryPrimitives)、`OscBundleBuilder`、`OscClient` ring buffer、`OscServer` Socket.ReceiveFrom + 固定 receive buffer、`OscPacketParser` ref struct）への置換が必要。詳細は `uosc-modification-plan.md`。Phase 1（送信）→ Phase 2（受信）→ Phase 3（旧 facade 撤去）の段階移行を採用。

### Bundle Accumulation Timeout（bundle 終端検出）

- **Context**: uOSC Parser は bundle 内 Message を 1 個ずつ dispatch するが、bundle 終端を明示通知しない。`OscDoubleBuffer.Swap` を bundle 単位で行うには終端検出が必要（Req 11.7）。
- **Sources Consulted**:
  - `Library/PackageCache/com.hidano.uosc@f7a52f0c524d/Runtime/Core/Parser.cs` (bundle timetag を Message.timestamp にコピーする実装)
  - LAN RTT 実測想定: typical Ethernet 1〜2 ms、Wi-Fi 5〜10 ms、Internet 30〜100 ms
- **Findings**:
  - bundle 識別キー: `Message.timestamp`（uOSC が bundle timetag をコピー、bare message はゼロ）
  - 同 bundle 内全 Message は連続 dispatch されるため、timestamp 変化検出 or N ms 経過のいずれかで終端確定可能
  - timeout を小さくすると bundle 跨ぎ Swap が早すぎて部分更新になる、大きくすると遅延が出る
- **Implications**:
  - 既定 5 ms（LAN RTT 2〜3 倍）を採用。Drawer で 1〜50 ms 範囲で設定可能。
  - 観測した最初のメッセージから時間経過カウント。次の異なる timestamp 到着で即フラッシュ。
  - bare message（bundle 外 message）は timestamp=0 として通常通り即時 publish + Swap 経由更新。

### IP Allowlist の実装可能性

- **Context**: 旧 Req 11 では IP allowlist を提案していた。
- **Sources Consulted**: `Library/PackageCache/com.hidano.uosc@f7a52f0c524d/Runtime/Core/DotNet/Udp.cs:62`
- **Findings**: `UdpClient.Receive(ref endPoint_)` で送信元 IP は取得できるが、`messageQueue_.Enqueue(buffer)` は `byte[]` のみで endpoint 情報が破棄される。uOSC fork で改修すれば IP 取得可能だが、ゾンビ排除が UUID + 起動時刻のみで成立する。
- **Implications**: ユーザー判断により Req 11 から allowlist を削除。送信元選別は本 spec で導入する `/_facialcontrol/sender_id` (UUID + UTC ms) で完結する。

### PerfectSync Eye Look 符号定義

- **Context**: Req 4.6 の ARKit プリセット Gaze 送信（PerfectSync 8 BS 形式）と Req 12.3 の左右別 Vector2 逆合成で、符号定義を Design で確定する必要あり。
- **Sources Consulted**:
  - [ARKit ARFaceAnchor.BlendShapeLocation](https://developer.apple.com/documentation/arkit/arfaceanchor/blendshapelocation) — eyeLookXxx の方向定義
  - `FacialControl/Packages/com.hidano.facialcontrol.osc/Tests/PlayMode/Adapters/InputSources/ArKitOscAnalogSourceTests.cs:53-56`
  - `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Adapters/Bone/GazeBonePoseProvider.cs`
- **Findings**:
  - ARKit 規格: `eyeLookInLeft` = 左目が「鼻に向かう」方向（キャラ右）の度合、`eyeLookOutLeft` = 左目が「鼻から離れる」方向（キャラ左）の度合
  - 「向かって右」(+x in UI / camera space) は左目視点で `eyeLookInLeft`, 右目視点で `eyeLookOutRight`
  - 既存 `GazeBonePoseProvider` は +x = 向かって右の規約
- **Implications**: 符号定義を以下で固定。
  - 左目: `x_L = eyeLookInLeft - eyeLookOutLeft`（向かって右が +x）、`y_L = eyeLookUpLeft - eyeLookDownLeft`（上が +y）
  - 右目: `x_R = eyeLookOutRight - eyeLookInRight`（向かって右が +x）、`y_R = eyeLookUpRight - eyeLookDownRight`（上が +y）
  - 送信側は `Vector2 → 8 float` を上記符号の逆変換で行い、両側合計が `|x|` / `|y|` になるよう片側だけ正値、もう片側 0 とする（例: `x_L > 0` なら `eyeLookInLeft = x_L`, `eyeLookOutLeft = 0`）。
- **Note**: design.md の Flow 3 では `x_L = eyeLookOutLeft - eyeLookInLeft` と一旦記載していたが、上記検討結果（向かって右 +x のキャラ視点 vs カメラ視点）を本 research.md で正規化する。実装時は本 research.md の符号定義（左目 `x_L = eyeLookInLeft - eyeLookOutLeft`）を真とし、テスト (`PerfectSyncEyeLookTests`) で双方向 round-trip を保証する。

### bundle MTU 上限と分割ポリシー

- **Context**: Req 3.5 は MTU 超過時の挙動を Design で確定するよう要求。
- **Sources Consulted**: RFC 791 (IPv4 MTU 1500)、UDP/IP header 28 byte、OSC 1.0 spec の bundle 構造
- **Findings**:
  - Ethernet MTU 1500 byte、IP header 20、UDP header 8、よって UDP データグラム payload 上限 1472 byte
  - 1 message は最小 16 byte（address 8 + type tag 4 + float 4）+ アラインメント。BlendShape 100 + 8 byte timetag + header = 約 1600+ byte で MTU 超過。
- **Implications**:
  - 分割ポリシー: bundle 全体サイズが 1472 byte を超える場合、`OscBundleBuilder` が **同一 timestamp を継承して複数 bundle に分割**する。各 packet は独立 UDP データグラム。
  - 受信側 `OscBundleAccumulator` は同 timestamp の複数 bundle をまとめて 1 フレーム適用する（既存 timestamp ベースの bundle 識別と整合）。
  - 警告ログを 1 回出力（同一フレーム数で頻発しないようカウンタ管理）。

### 受信側 binding 間の依存解決順序

- **Context**: Req 12 で `OscGazeReceiverAdapterBinding` が同 child scope 内の `OscReceiverAdapterBinding` の OscDoubleBuffer / OscReceiver を参照する必要がある。binding 間の `OnStart` 順序は SO 上のリスト順依存。
- **Sources Consulted**: `Packages/com.hidano.facialcontrol/Runtime/Adapters/DependencyInjection/AdapterBindingHost.cs`
- **Findings**:
  - 現状 `AdapterBindingHost.IInitializable.Initialize` は SO リスト順で binding.OnStart を同期呼出する
  - `IInputSourceRegistry` は slug-keyed lookup を提供するが、binding インスタンスそのもののハンドルは未公開
- **Implications**: 2 つの選択肢を併記し、実装時に決定する:
  1. **遅延 resolve**: `OscGazeReceiverAdapterBinding.OnStart` で受信元 binding が見つからない場合 `_pendingResolve=true` をセットし、初回 `OnFixedTick` で再 lookup（堅牢、SO リスト順非依存）
  2. **明示順序**: SO 上で `OscReceiverAdapterBinding` → `OscGazeReceiverAdapterBinding` の順を強制 + 違反時警告（単純、ただしユーザー操作ミスに脆弱）
  - design.md 内では **(1) 遅延 resolve を採用**。`AdapterBuildContext` への binding lookup 機構追加は最小限とし、Gaze receiver 側で再試行ロジックを持つ。

### SystemTextJsonParser と JsonUtility の整合性

- **Context**: Req 6.10 が「System.Text.Json ベース」と記述していたが、実装は `JsonUtility` ベース。
- **Sources Consulted**: `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Adapters/Json/SystemTextJsonParser.cs`
- **Findings**: クラス名と裏腹に実装は `JsonUtility.FromJson<T>` / `JsonUtility.ToJson(...)`。歴史的経緯で命名が残存。
- **Implications**: design.md 内で要件 6.10 を「既存 JSON 経路（`SystemTextJsonParser`、実装は `JsonUtility`）と整合する形で `[Serializable]` DTO を実装し、新規 JSON ライブラリを持ち込まない」と再表現する。requirements.md 自体は本 spec のスコープ内では修正しないが、design.md の Implementation Note でこの整合性を明示。

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| Pull 型 Publish (採用) | FacialController.LateUpdate が Bus.Publish を直接呼ぶ。binding は observer として scratch buffer 更新 + 自身の OnLateTick で送信 | dispatch 順非依存、同フレーム送信が確実に成立、Domain 純度維持 | observer 側で scratch buffer 確保が必要 | クリーンアーキテクチャと最も整合 |
| Push 型 Lifecycle | binding.OnLateTick が能動的に LayerUseCase.BlendedOutputSpan を pull | binding の責務が完結 | 全 binding が LayerUseCase を直接参照、Domain inversion 破壊 | 不採用 |
| Frame Counter Bus | Bus に frame counter スタンプ、binding 側で同フレームか判定して送るか保留 | dispatch 順制御不要 | 1 フレーム遅延を受容するケースが発生、テスト複雑化 | 不採用 |
| DefaultExecutionOrder 強制 | MonoBehaviour 同士の順序を強制（VContainer 越えには無効） | 単純 | MonoBehaviour vs VContainer の境界に対し無力、既述 PlayerLoop カテゴリ違いで不要 | 不採用 |

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| uOSC vendor fork + 内部改修 (採用) | `com.hidano.uosc` を Packages 配下に vendor copy し、Modern API（SoA / Span / ArrayPool / Socket.ReceiveFrom）を新設、旧 API は Phase 3 で撤去 | GC ゼロ達成、契約上必要な機能を完備、MIT License 維持 | upstream 追従が手動、fork 維持コスト | ユーザー判断で確定 |
| uOSC へ PR 投げる | upstream にパッチを送る | upstream 追従不要 | レビュー/マージ周期が長い、最新更新が古く反応見込み低 | 不採用 |
| 自前 OSC ライブラリ新設 | uOSC を捨てて自前実装 | 完全制御 | 開発コスト大、既存テスト資産が消失 | 不採用 |

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| `GazeBindingConfig` 破壊変更（左右別 sourceId、採用） | sourceIdLeft / sourceIdRight を必須化、後方互換なし | InputSystem / OSC の Gaze 経路が一系統に統合、左右非対称が標準サポート、設計バグ余地減 | 既存 SO / JSON / テストの一斉改修 | ユーザー判断で許容 |
| `GazeBindingConfig` optional 追加 | sourceIdLeft / Right を optional、未指定なら従来の単一 sourceId fallback | 後方互換あり | 二系統並存でテスト爆発、片側だけ指定の半端状態許容 | 不採用 |
| 別 spec で対処 | 本 spec は単一 sourceId 維持、別 spec で左右別化 | 本 spec が小さくなる | OSC Gaze 左右非対称送受信が成立しない | 不採用 |

## Design Decisions

### Decision: FacialOutputBus の Publish タイミング
- **Context**: Req 1.2 が「OnLateTick の直前」かつ「同フレーム送信完結」を要求。
- **Alternatives Considered**:
  1. FacialController.LateUpdate 末尾（BoneWriter.Apply の直後）で Publish
  2. FacialController.LateUpdate 内、layerUseCase.UpdateWeights 直後・BoneWriter.Apply の前で Publish
- **Selected Approach**: 2 を採用。`_layerUseCase.UpdateWeights` 完了直後に `BlendedOutputSpan` がフレッシュな状態で取れるため。BoneWriter.Apply はボーン更新であり post-blend 値変化を伴わない。
- **Rationale**: 送信側は BlendShape 値とともに Gaze スナップショットも必要。Gaze は `GazeBindingConfig` の入力源（IAnalogInputSource）から FacialController が直接 pull する。BoneWriter.Apply の前後どちらでも post-blend 値は変わらないが、Apply 前にすることで「post-blend = 送信値」の意味が明確。
- **Trade-offs**: Apply 前後 1 frame の意味的差分はないが、binding が SkinnedMeshRenderer の最新値ではなく `_layerUseCase.BlendedOutputSpan` を直接 observe する形を取るため、レンダラへの書き込みと送信値の整合性が保証される。
- **Follow-up**: テストで Publish 前後の値が SkinnedMeshRenderer.GetBlendShapeWeight と一致することを検証（PlayMode）。

### Decision: bundle 識別キーとして `Message.timestamp` を採用
- **Context**: Req 11.7 で bundle アトミック適用が必要だが、uOSC Parser は bundle 終端を通知しない。
- **Alternatives Considered**:
  1. uOSC fork で bundle 開始 / 終端イベントを新設
  2. `Message.timestamp`（uOSC が bundle timetag をコピー）を bundle ID として使い、変化検出 + timeout で終端確定
- **Selected Approach**: 2 を採用。
- **Rationale**: uOSC fork 改修は最小限に抑えたい（送受信ホットパスの zero-alloc 化が本筋）。timestamp ベースは Parser を改修せず既存 API のまま実現可能。
- **Trade-offs**: timeout（既定 5 ms）に依存するため遅延が増える。bare message が timestamp=0 のため bundle と区別可能だが、複数送信元が同一 timestamp を偶発的に出すと混在する。ZombieEvictionPolicy が sender_id ベースで隔離するため実害は限定的。
- **Follow-up**: PlayMode `OscBundleAtomicityTests` で同一 timestamp 内全 message が一括 Swap されることを検証。複数送信元の混在ケースは E2E テストで sender_id 隔離を検証。

### Decision: heartbeat の OSC 表現
- **Context**: Req 14.4 が BlendShape 名一覧 heartbeat を要求。OSC 1.0 spec で string[] をどう表現するか。
- **Alternatives Considered**:
  1. 1 string メッセージに区切り文字（`,` 等）で連結
  2. 複数 string 引数の単一メッセージ（OSC 1.0 で string 型を複数並べる）
  3. 名前ごとに別 OSC アドレスメッセージ
- **Selected Approach**: 2 を採用。`/_facialcontrol/blendshape_names` 1 アドレスに OSC string 型引数を順序通り並べる。
- **Rationale**: OSC 1.0 spec は単一メッセージ内の複数 type tag をサポート。bundle 内 1 メッセージで完結するため bundle accumulator との相性が良い。
- **Trade-offs**: 大量 BlendShape (>500) で 1 メッセージサイズが MTU 超過する可能性。その場合は分割 bundle として複数メッセージに展開する `HeartbeatMessageBuilder` を追加で持つ（実装時の Open Question）。
- **Follow-up**: heartbeat メッセージサイズが 1000 byte を超える場合の分割ポリシーを実装時に Phase 4 で確定。512 BlendShape × 平均名前長 16 byte + アライン = ~10 KB 程度。実用上 4〜8 分割が必要になる見込み。

### Decision: Gaze 受信の左右独立 fallback
- **Context**: Req 12.6 が leftRightIndependent OFF 時の fallback を要求。
- **Alternatives Considered**:
  1. 左右の平均を publish
  2. 左目値のみ publish（左右両方に同一）
  3. ARKit プリセット時のみ自動で左右独立、VRChat 時は左右同一
- **Selected Approach**: 3 を採用。実装は: ARKit プリセット (`PerfectSyncEyeLook.Decompose`) が左右別 Vector2 を生成するため、`leftRightIndependent=true` ならそのまま、`false` なら左右の平均 (1) を publish。VRChat プリセットは Vector2 が 1 つしか届かないので必然的に左右同一 (2)。
- **Rationale**: PerfectSync 由来の左右非対称データを fallback で潰すのは情報量を捨てるが、設定 OFF はユーザー意図のため尊重。VRChat は本来左右別が送れないので同一が自然。
- **Trade-offs**: ユーザーが ARKit プリセットを使いながら左右独立 OFF にすると寄り目が表現されない。Drawer で説明文を表示。
- **Follow-up**: Drawer の HelpBox 文言を実装時に確定。

### Decision: uOSC vendor fork のパッケージング
- **Context**: vendor copy の置き場所と version 命名。
- **Alternatives Considered**:
  1. `Packages/com.hidano.uosc/` に直接置く（local embed）
  2. 別リポジトリで fork し git URL で参照
  3. NPM scope `@hidano/uosc-facialcontrol` として npmjs.com にも公開
- **Selected Approach**: 1 を採用（preview.2 段階）。
- **Rationale**: fork 改修が active な段階では local embed が最も改修サイクルが速い。preview 卒業時に 2 or 3 へ移行検討。
- **Trade-offs**: vendor copy がリポジトリサイズを増やす（uOSC は ~50 KB なので無視できる）。upstream 追従は手動。
- **Follow-up**: `package.json` の name を `com.hidano.uosc` のまま、version を `1.0.0-fcfork.1` とする。CHANGELOG / LICENSE に fork 明示。preview.2 リリースノートで明文化。

### Decision: 受信側 binding 間依存の遅延 resolve
- **Context**: `OscGazeReceiverAdapterBinding` が `OscReceiverAdapterBinding` の OscDoubleBuffer / OscReceiver を参照する必要があるが、SO 上のリスト順に依存させたくない。
- **Alternatives Considered**:
  1. `AdapterBuildContext` に `IAdapterBindingLookup` を追加し、binding 間の slug 解決を提供
  2. `OscGazeReceiverAdapterBinding` 自身が `OnStart` で見つからない場合 `_pendingResolve=true` をセットし、`OnFixedTick` で再試行
- **Selected Approach**: 2 を採用。
- **Rationale**: AdapterBuildContext への破壊的変更を回避。binding 内に自己完結した遅延 resolve ロジックを持つことで、他 binding spec への波及をゼロにできる。
- **Trade-offs**: 最大 1 フレーム遅延（初期化時のみ）。実装複雑度が若干増える。
- **Follow-up**: 遅延 resolve が 10 フレーム以上失敗した場合は警告ログを 1 回出力。

## Risks & Mitigations

- **R1 — Unity 実行順制御**: 上記 Decision「FacialOutputBus の Publish タイミング」で pull 型を採用。PlayerLoop カテゴリの違い（MonoBehaviour LateUpdate vs VContainer LateTick）で順序が保証されることを E2E テストで検証。
- **R2 — uOSC GC**: vendor fork + 内部改修で対応。`uosc-modification-plan.md` の Phase 1〜3 計画に従う。`Profiler.GetTotalAllocatedMemoryLong` 差分テストで継続監視。
- **R3 — uOSC が送信元 IP を捨てる**: IP allowlist を Req から削除。送信元選別は UUID + 起動時刻のみで成立する。
- **R4 — Bundle-atomic 化と timestamp 依存**: `OscBundleAccumulator` の timeout を 5 ms 既定、Drawer から 1〜50 ms に設定可能。複数送信元混在は sender_id 隔離で対処。
- **R5 — SystemTextJsonParser の命名 vs 実装の乖離**: design.md 内で「既存 JSON 経路と整合、新ライブラリ持ち込まない」と再表現。本 spec で命名変更は行わない（別 spec で対処、`docs/backlog.md` に追記）。
- **R6 — GazeBindingConfig 破壊変更の波及**: preview 段階 migration 不要のため許容。`InputSystemAdapterBinding.BuildGazeProvider` / `FacialCharacterProfileConverter` / 既存 Gaze テスト一斉改修を Phase 7 で 1 PR にまとめる。
- **R7 — Sample 二重管理**: `docs/work-procedure.md` に「Samples~ と Assets/Samples の同期手順」を明文化。CLAUDE.md の Samples 二重管理ルールに従う。
- **R8 — 受信側 binding 依存解決順（解決済み）**: 2026-05-15 のレビューで Gaze Vector2 受信を別 binding に分割せず `OscReceiverAdapterBinding` 内の mode 別 entry として統合する判断に変更。`InputSystemAdapterBinding` の単一 binding 内 mode 集約パターン（`ExpressionBindingEntry.bindingMode`）を踏襲。これにより binding 間 cross-lookup（`AdapterBuildContext.OscBindingProvider` 案）は不要となり、フェイルセーフ時の状態伝播も同一 binding 内の内部呼出で完結する。`OscGazeReceiverAdapterBindingTests` は `OscReceiverAdapterBindingTests` の Gaze 系 mode シナリオに統合する。
- **R9 — MTU 超過時の bundle 分割**: 同一 timestamp を継承した複数 bundle に分割。受信側 accumulator は timestamp ベースで集約する既存設計でそのまま吸収可能。
- **R10 — heartbeat 大規模 mapping (>500 BS)**: 1 メッセージで送れない場合は複数 heartbeat メッセージに分割。実装時 Phase 4 で確定。
- **R11 — Profiler 差分テストの flakiness**: 初回 JIT / domain reload 直後の alloc を warm-up フェーズで除外。10 サンプル中下位 9 個の中央値で評価。

## References
- [OSC 1.0 Specification](https://opensoundcontrol.stanford.edu/spec-1_0.html) — bundle / message / timetag / type tag 形式の canonical
- [VRChat OSC Documentation](https://docs.vrchat.com/docs/osc-overview) — `/avatar/parameters/{name}` 規約
- [ARKit ARFaceAnchor.BlendShapeLocation](https://developer.apple.com/documentation/arkit/arfaceanchor/blendshapelocation) — eyeLookXxx 8 名の方向定義
- [hecomi/uOSC (MIT)](https://github.com/hecomi/uOSC) — 本 spec で fork 化する upstream
- [.NET BinaryPrimitives](https://learn.microsoft.com/dotnet/api/system.buffers.binary.binaryprimitives) — Big-Endian Span 書き込み API
- [.NET ArrayPool<T>](https://learn.microsoft.com/dotnet/api/system.buffers.arraypool-1) — 共有プール (`Shared`) で zero-alloc buffer 確保
- [.NET Socket.ReceiveFrom](https://learn.microsoft.com/dotnet/api/system.net.sockets.socket.receivefrom) — 固定 buffer 受信、`UdpClient.Receive` の代替
- [Unity Profiler API — GetTotalAllocatedMemoryLong](https://docs.unity3d.com/ScriptReference/Profiling.Profiler.GetTotalAllocatedMemoryLong.html) — GC アロケ差分監視
- [VContainer Lifecycle](https://vcontainer.hadashikick.jp/integrations/entrypoint) — ILateTickable / IFixedTickable / IInitializable の PlayerLoop 統合
- 関連 spec ドキュメント:
  - `D:\Personal\Repositries\FacialControl\.kiro\specs\osc-output-binding\requirements.md`
  - `D:\Personal\Repositries\FacialControl\.kiro\specs\osc-output-binding\gap-analysis.md`
  - `D:\Personal\Repositries\FacialControl\.kiro\specs\osc-output-binding\uosc-modification-plan.md`
- 関連 steering ドキュメント:
  - `D:\Personal\Repositries\FacialControl\.kiro\steering\product.md`
  - `D:\Personal\Repositries\FacialControl\.kiro\steering\tech.md`

## Decision Log（2026-05-15 設計レビュー追記）

### D1: `AdapterBuildContext` に `IFacialOutputBus` を直接追加
- **Context**: `OscSenderAdapterBinding.OnStart` で post-blend bus を取得する DI 経路が未確定。
- **Options**:
  - A. `AdapterBuildContext` 構造体に `IFacialOutputBus` フィールド追加（採用）
  - B. `AdapterBindingHost` 側で `IFacialOutputObserver` 実装 binding を pattern match で自動 Subscribe
  - C. ctx に汎用 `IBindingScopeResolver` を 1 つ追加（Service Locator）
- **Decision**: A 案を採用。ctx の最小拡張で完結し、既存 binding は当該フィールドを参照しないため動作影響なし。design.md の Revalidation Triggers L67 に「本 spec 内で 1 度実施済」と注記。
- **Rationale**: Domain 純度を保ち、boxing 無し（readonly struct + in 渡し）。VContainer の child scope register 経路に lifetime = Scoped で挿せば既存 DI フローと整合。

### D2: Gaze Vector2 受信を `OscReceiverAdapterBinding` に統合（OscGazeReceiverAdapterBinding 廃止）
- **Context**: 元設計では `OscGazeReceiverAdapterBinding`（新規）が `OscReceiverAdapterBinding` の `OscDoubleBuffer` を slug で参照する構造で、gap-analysis R8（受信側 binding 依存解決順）が未確定だった。
- **Options**:
  - α. ctx に OSC 専用 locator 追加し binding 実体を直接参照
  - β. `IInputSourceRegistry` 経由で受信元が 8 BS / X-Y を `IAnalogInputSource` として publish、Gaze receiver は slug で subscribe
  - γ. ctx に IBindingScope 列挙 interface を新設
  - **δ. `OscReceiverAdapterBinding` 自体に mode 別 entry（BlendShape / Gaze_VRChat_XY / Gaze_ARKit_8BS）を導入し、Gaze 受信を 1 binding 内に統合（採用）**
- **Decision**: δ 案を採用。`InputSystemAdapterBinding` が単一 binding 内で Trigger + Analog + Gaze（`ExpressionBindingEntry.bindingMode`）を扱う既存パターンを踏襲。R8（binding 間依存解決）自体が消滅し、フェイルセーフ時の状態伝播も内部呼出で完結する。
- **Rationale**: 既にコードベース内に「1 binding が複数 mode の入力を統合して扱う」前例（`InputSystemAdapterBinding`）が存在し、Inspector の Add ドロップダウンも 2 binding（Sender + Receiver）に統一されてユーザー設定がシンプル。要件 12 / 6.3 / 7 / 10 の文言を整合性のため修正。
- **Impact**: design.md / requirements.md / spec.json を更新。`OscGazeReceiverAdapterBinding.cs` / `OscGazeReceiverAdapterBindingDrawer.cs` / `OscGazeReceiverOptionsDto.cs` / `osc-gaze-receiver-options.md` の新設は不要となり、代わりに `OscMappingEntry` / `OscMappingMode` / `OscMappingEntryDto` を新規追加。

### D4: Gaze entry の `sourceIdLeft` / `sourceIdRight` 片方欠落の挙動（2026-05-15 再レビュー）
- **Context**: `OscMappingEntry` の `leftRightIndependent = true` 時、左右別の sourceId を 2 フィールド持つが「片方だけ空」の挙動が design.md / requirements.md / Drawer 仕様で食い違っていた。
- **Options**:
  - A1. entry 全体スキップ（両方必須）（採用）
  - A2. 解決した側だけ register、Req 13.3 fallback で他方に流用
- **Decision**: A1 を採用。`leftRightIndependent = true` の Gaze entry で `sourceIdLeft` または `sourceIdRight` のいずれかが空文字 / null なら、entry 全体をスキップ + 警告ログ。`leftRightIndependent = false`（既定）時は両 sourceId フィールドを読まないため空でも問題なし。
- **Rationale**: シンプルなデバッグ挙動。「片方だけ書く」のは意図不明な設定ミスと判定して早期に弾く。空 slug が `IInputSourceRegistry` に積まれて他 binding と衝突する事故も同時に防げる。Req 13.3 の fallback は別のシナリオ（明示指定したが register が未完了 / 未存在）で発火する責任分担に。

### D5: Gaze_ARKit_8BS mode の `addressPattern` の扱い
- **Context**: `OscMappingEntry.addressPattern` は Normal_BlendShape では完全 OSC アドレス、Gaze_VRChat_XY では base アドレス（X/Y 末尾抜き）として使う一方、Gaze_ARKit_8BS mode での意味が design.md / DTO / Drawer の各箇所で未定義だった。
- **Options**:
  - B1. プレフィクスのみ可変（例: `addressPattern = "/ARKit/"`）
  - B2. addressPattern を無視、`/ARKit/eyeLookXxx` 固定 8 個を listen（採用）
  - B3. テンプレート形式（`/ARKit/{name}` で `{name}` を 8 名置換）
- **Decision**: B2 を採用。Gaze_ARKit_8BS mode では `addressPattern` を読まず、`PerfectSyncEyeLook.Names` の固定 8 名（ASCII、`eyeLookInLeft` 等）に `/ARKit/` プレフィクスを付けた 8 アドレスを必ず listen。Drawer は ARKit mode 選択時に `addressPattern` 欄を非表示、DTO で非空の値が来た場合は Info ログ「ignored」+ 既定値扱い。
- **Rationale**: VRChat / ARKit プリセットを strict に定義し受信側も同規格を期待することで仕様の予測可能性を高める。プレフィクス可変（B1）は preview.3 以降の拡張要求として `docs/backlog.md` に逃がす。

### D6: `GazeBindingConfig` の左右独立フラグ + 既定 expressionId 自動マッチ（D3 採用）
- **Context**: 元設計（Req 13.1）は `sourceIdLeft` / `sourceIdRight` を 2 つとも必須フィールドとし、ユーザーが完全 slug 文字列を毎回手書きする UX。「ユーザーが書く必要が本当にあるのか」というレビュー指摘を受け再設計。
- **Options**:
  - D1. 完全 convention-based（sourceIdLeft / sourceIdRight 廃止、expressionId のみで自動解決）
  - D2. Drawer dropdown 入力（フィールド残すが UI 改善）
  - D3. 既定は D1 + `useDistinctLeftRight` ON 時のみ詳細フィールド出現（採用）
- **Decision**: D3 を採用。`GazeBindingConfig` は `expressionId` を必須キー、`useDistinctLeftRight` を既定 `false` の任意フィールド、`sourceIdLeft` / `sourceIdRight` を `useDistinctLeftRight = true` 時のみ意味を持つ条件付きフィールドとする。OSC 側 `OscMappingEntry.leftRightIndependent` と意味論を統一（slug 規約を Cross-binding Slug Convention で正準定義）。`InputSystemAdapterBinding.ExpressionBindingEntry` も同じ構造（`useDistinctLeftRight` + `actionName` / `actionNameLeft` / `actionNameRight`）に揃える。
- **Rationale**: シンプル運用（左右共通の Gaze 駆動）が圧倒的多数の想定。「左目 OSC / 右目 ゲームパッド」のような左右混在ユースケースは preview 段階で必須ではないが、捨てるとあとで再度破壊変更が必要になるため flag による段階的有効化で残す。Drawer UX も既定は 1 フィールド入力で完結、必要な時だけ詳細が現れる段階的開示。
- **Impact**: design.md の `GazeBindingConfig` セクション全面書き換え、`Cross-binding Slug Convention` セクション新設、Component Summary 更新。requirements.md Req 6.3 / 7 / 12 / 13 を整合修正。InputSystem 側仕様も同調。

### D7: Gaze_ARKit_8BS mode は `leftRightIndependent` フラグに関わらず常に左右別 publish（2026-05-15 第 3 回レビュー）
- **Context**: D6 (CI3) で「`leftRightIndependent = false` は 1 source として publish」と決めたが、ARKit 8 BS は本質的に左右両方の値を含むため、`false` で 1 source 化すると左右非対称 Gaze（寄り目、片目流し目）情報がサイレントに破棄される問題が第 3 回レビューで検出。
- **Options**:
  - E1. mode 別挙動: ARKit_8BS は `false` でも常に `.left` / `.right` 2 keys で publish（採用）
  - E2. 全 mode 統一で左目代表値（情報損失あり）
  - E3. 全 mode 統一で左右平均値（中心寄りバイアス）
- **Decision**: E1 を採用。`Gaze_VRChat_XY` mode は VRChat 規格自体が左右共通 Vector2 なので `false` で 1 source publish が自然だが、`Gaze_ARKit_8BS` mode は 8 BS が左右両方の情報を含むため `leftRightIndependent` フラグの値に関わらず常に左右別の 2 keys で register する。
- **Rationale**: 「自動マッチで両目駆動」のシンプル運用と「左右非対称 Gaze の忠実再現」が両立する。E2 / E3 は情報損失があり「ARKit 形式の左右独立性」という規格自体の存在意義を毀損する。
- **Impact**: design.md OscReceiverAdapterBinding Implementation Notes（Registry register ロジック）を mode 別表に書き換え。requirements.md Req 12.5 を mode × flag のマトリクス形式に修正。Req 8.5 E2E テストに「ARKit × false 既定でも左右非対称が再現される」ケースを追加。

### D8: 自動マッチで複数 binding が同 expressionId を提供時の決定性ルール（2026-05-15 第 3 回レビュー）
- **Context**: D6 で「`useDistinctLeftRight = false` 既定時は `expressionId` 自動マッチ」と決めたが、複数 binding が同じ `expressionId` を提供した場合の「先頭採用」の「先頭」が register 順 / SerializeReference 順 / dispatch 順 のどれか未確定で deterministic 性が崩れる問題が検出。
- **Options**:
  - F1. SerializeReference 配列順（Inspector 並び順）
  - F2. `binding.Slug` の Ordinal lexicographic 昇順（採用）
  - F3. 多重 binding 提供を起動時エラーで拒否
- **Decision**: F2 を採用。複数 binding が同じ `expressionId` を提供した場合は `binding.Slug` の Ordinal lexicographic 昇順で先頭採用 + 警告ログ。シーン構成 / 起動順 / Inspector 並び替えに依存せず deterministic。
- **Rationale**: F1 は Inspector 操作に依存し並び替えで意図せず優先順位が変わる隠れバグの温床。F3 は厳しすぎ、preview.3 以降で考えうる冗長構成（複数 sender 受信）に対応不能。F2 は slug 名でユーザー側が制御可能（slug 名を変えれば優先順位が変わる）かつ deterministic でテスト再現性が高い。
- **Impact**: design.md Cross-binding Slug Convention に「Ordinal lexicographic 昇順」のルールを追記。requirements.md Req 13.2 に同条件を明記。Req 8.5 E2E テストに「OSC + InputSystem 同 expressionId 衝突」ケースを追加。

### D9: Phase 順序入れ替え（Phase 7 を Phase 2 より前に実施）（2026-05-15 第 3 回レビュー）
- **Context**: 旧設計では Phase 2 (OscReceiverAdapterBinding mode 統合、`.left` / `.right` publish 開始) → Phase 7 (GazeBindingConfig 改修、`.left` / `.right` 解決対応) の順だったため、Phase 2 完了時点で OSC 受信側が新規約で publish するが GazeBindingConfig 側が旧構造で解決できない一時的な機能空白が発生する問題が検出。さらに Phase 7 自体が 6 軸（GazeBindingConfig / DTO / Converter / InputSystem / BuildGazeProvider / 既存テスト群）に及び単一 PR では巨大化する。
- **Options**:
  - G1. Phase 順序入れ替え（Phase 7 を Phase 2 より前に）（採用）
  - G2. Phase 7 を 7a / 7b / 7c の 3 PR に分割
  - G3. Phase 2 + Phase 7 を 1 PR に統合
- **Decision**: G1 を採用。実施順序を Phase 1 → Phase 7 → Phase 2 → Phase 3 → Phase 4 → Phase 5 → Phase 6 → Phase 8 → Phase 9 に変更。Phase 番号は維持（再番号付けによる外部参照リンク切れを避ける）。Phase 7 内の 3 PR 分割（7a / 7b / 7c）は実装フェーズの判断に委ねる選択肢として保持。
- **Rationale**: G1 のみで「機能空白回避」と「Phase 7 の粒度問題」の両方を解決できる（Phase 7 内分割は G2 として実装時オプション化）。G3 は PR がさらに巨大化しレビュー負荷が許容範囲を超える。Phase 7 → Phase 2 順により、Phase 7 完了時点で「GazeBindingConfig 側は新構造で待機、OSC 受信側は旧構造（OSC Gaze は一時的に動かないが InputSystem Gaze は動作）」、Phase 2 完了時点で OSC Gaze も即動作する状態へ自然に移行する。
- **Impact**: design.md Migration Strategy の Mermaid 図と Phase 列挙を `P1 → P7 → P2 → ...` に書き直し。「Phase 7 分割の選択肢」サブセクションを追加。requirements.md Req 10.5 に Phase 実施順序を明記。Req 13.7 に Phase 7 分割時のテスト緑化条件を追記。

### D3: preview milestone のスコープ分割（uOSC fork を preview.3 送り）
- **Context**: design.md の Migration Strategy が uOSC fork 3 phase + 本 spec 11 phase の合計 14 phase を一括 preview.2 に積んでおり、当初 preview milestone（2026-02 末）を超過した現状（2026-05-15）でさらに遅延リスクがあった。
- **Options**:
  - A. 最小実装サブセットで preview.2 を切る（Phase 1/3/7/8/9 + uOSC 一部のみ）
  - B. full scope 維持で preview milestone を 2026 年末まで延期
  - C. **uOSC fork（zero-alloc 化）を preview.3 送り、機能本体は preview.2 で完結（採用）**
- **Decision**: C 案を採用。本 spec の Phase 1〜9（機能本体）を preview.2、Phase 10（uOSC fork zero-alloc）+ Phase 11（uOSC 互換 facade 撤去）を preview.3 に分割。
- **Rationale**: 機能完成度を保ったまま preview.2 を確実に切ることを優先。GC スパイクは preview.2 で一時許容し、Req 10.1 の zero-alloc 完全達成は preview.3 で実現。Gaze 左右独立（Req 13）は preview.2 必須（後ろ送りすると再度破壊変更が必要なため）。
- **Impact**: design.md の Migration Strategy を 2 milestone 分割の Mermaid に書き直し。requirements.md Req 10.1 / 10.5 を milestone 分割に整合させる文言に修正。
  - `D:\Personal\Repositries\FacialControl\.kiro\steering\structure.md`
