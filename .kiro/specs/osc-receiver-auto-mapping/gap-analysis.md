# OSC Receiver Auto Mapping ギャップ分析

`/kiro:validate-gap osc-receiver-auto-mapping` のコンテキストで実施した、`requirements.md` と既存コードベースの実装ギャップ分析の結果。

## 分析サマリ（要点）

- **`OscReceiverAdapterBinding.OnStart` は「mappings がゼロなら登録放棄」する短絡パスを持つ**ため、Req 1.1 / 1.4（heartbeat 待ちで起動する経路）と直接矛盾する。`runtimeMappings` 解決と `OscInputSource` の `IInputSourceRegistry.Register` を**遅延フェーズ**に分割するリファクタが本 spec の中心改修ポイント。
- **`OscInputSource` / `OscDoubleBuffer` / `HeartbeatConsistencyChecker` / `mappingIndexToMeshIndex` は全て constructor 渡し**で、runtime 中の mapping 件数変動を想定していない（`OscDoubleBuffer.Resize` は存在するが、`OscInputSource._mappingIndexToMeshIndex` / `_skipMask` / `_contributeMask` は読み取り専用 field）。Req 4 を満たすには「新 `OscInputSource` を構築して `IInputSourceRegistry` に再登録（後勝ち上書きは既存仕様）」する**置換戦略**が現実的で、`Resize` 経路を握る必要はあるが、in-place 拡張より破壊リスクが少ない。
- **ARKit 標準名は Domain 層に既に存在**: `Hidano.FacialControl.Domain.Services.ARKitDetector.ARKit52Names` (52 名) + `PerfectSyncNames` (13 名)。`PerfectSyncEyeLook.Names` は 8 名のみで eyeLook 系に特化。Req 3.7「単一情報源」は `ARKitDetector` を Adapters から参照すれば達成可能（Domain 公開済み API）。
- **heartbeat payload は現状 `string[]` のみ**（`OscBundleBuilder.AddHeartbeatMessages` が `string` 型タグだけを詰める）。Req 5 の「preset 識別子追加」拡張は、(a) `string[]` 末尾に sentinel + preset 文字列を追加する案、(b) 末尾に 1 件追加し OSC 型タグで識別する案、(c) heartbeat を分割せず維持して**受信側で名前一致率推定のみ**を行う案、の 3 案で評価可能。既存 receiver は `string` 以外を skip するため (a)(b) は後方互換。
- **Mappings が空での受け入れ条件 (Req 7) は現状未達**: `OscReceiverDemoProfile.asset` は 4 件の手入力 `Normal_BlendShape` + 1 件の `Gaze_VRChat_XY` を含み、これを空にすると `OnStart` 内の `!hasBlendShapeMappings && !hasGazeMappings` 分岐で warning 1 行を出して registry 未登録のまま終わる。空 Mappings 経路の確立が機能ゴール。
- **`OscMappingEntry` の SerializeField は破壊しなくて済む**: 6 フィールド (`mode/expressionId/addressPattern/sourceIdLeft/sourceIdRight/leftRightIndependent`) を温存し、heartbeat 由来の runtime mapping は `[NonSerialized]` の別コレクション (`_runtimeMappings` の延長線) に保持すれば Req 2.6 / 6.1 を満たせる。Inspector の `OscReceiverAdapterBindingDrawer.MappingsFieldName` (`_mappings`) も互換維持。
- **送信側拡張は本 spec のスコープ外**（Boundary Context 明記）だが、Req 5.3 の preset 拡張が選択される場合は `OscSenderAdapterBinding` と `OscBundleBuilder.AddHeartbeatMessages` への追加 spec が連鎖発生する点を design phase で明記すべき。

## Document Status

- 形式: `gap-analysis.md` 既存 spec 互換（`osc-output-binding/gap-analysis.md` の構成に準拠）
- 言語: ja（`spec.json.language=ja`）
- spec 進捗: `requirements-generated` / requirements 未承認 — 本分析は requirements の改修にもフィードバック可能

---

## 要件別 ギャップ分析

### Requirement 1: 空 Mappings での自動 BlendShape mapping 生成

**既存資産（再利用 / 改修対象）**
- `OscReceiverAdapterBinding.OnStart` (`OscReceiverAdapterBinding.cs:331-436`): `_mappings` 空 + `_runtimeMappings` 未指定の場合は `runtimeMappings = CreateNormalBlendShapeMappings(_mappings)` で空配列、`!hasBlendShapeMappings && !hasGazeMappings` 分岐 (line 366) で warning + return する。**ここが Req 1.1 / 1.4 と矛盾する主改修対象**。
- `OscReceiverAdapterBinding.HandleHeartbeatMessage` (line 778-795): heartbeat 文字列を `_heartbeatScratch` に集約して `_heartbeatChecker.UpdateFromHeartbeat` に渡す既存経路があり、Req 1.2 の「heartbeat 到着時に mapping 生成」をフックする位置はここ。**ただし現状は `_heartbeatChecker == null` のとき早期 return** するため、Mappings が空のときは heartbeat が完全に無視される（改修必須）。
- `OscReceiverAdapterBinding.HandleIncomingOscMessage` (line 679-705): `BlendShapeNamesAddress` 判定経路は既にあるため、Req 1.2 の hook 点として正当。
- `AdapterBuildContext.BlendShapeNames` (`AdapterBuildContext.cs:34`): 受信側 mesh 名一覧。Req 1.2 / 1.5 の積集合計算の左辺として使える。
- `OscReceiverHost.Configure` (line 65-95): 現状 `OscMapping[]` を必須引数として受け取り `OscReceiver.Initialize` まで委譲。**heartbeat 受信のみで起動するには `Configure` を `mappings: null` 許容に拡張するか、`Configure` を 2 段階呼出 (receiver socket open → 後追い mappings 注入) に分けるか**を design phase で決定する必要。
- `OscReceiver` の dispatcher (`OscReceiver.cs:160-220`): mappings 未注入でも `_messageFilter` 経由で binding 側 `HandleIncomingOscMessage` を通過させる経路は既にある（Filter 通過後の解釈は binding 側）。socket open と mapping 注入を分離可能なポテンシャルあり。

**新規 / 拡張コンポーネント案**
- `AutoMappingState`（仮）: heartbeat 由来の (BlendShape 名, 推定 address, 由来=Auto/Manual) を保持する struct/class。
- `OscReceiverAdapterBinding` 内に「mapping 確定→ `OscInputSource` 構築→ `IInputSourceRegistry.Register`」のメソッドを抽出し、`OnStart` と heartbeat callback の両方から呼べる形にリファクタ。

**ギャップ**: Missing（heartbeat 待ち起動経路）/ Constraint（OscReceiverHost.Configure が mappings 必須）。

**複雑度**: M、**リスク**: Medium（lifecycle 分割 + ロック設計）

---

### Requirement 2: 手入力 / 部分入力 / 完全自動の統一フロー

**既存資産**
- `_mappings` (`OscReceiverAdapterBinding.cs:48`): Inspector でシリアライズされる手入力リスト。`Normal_BlendShape` / `Gaze_VRChat_XY` / `Gaze_ARKit_8BS` の 3 mode を保持。
- `CreateNormalBlendShapeMappings` (line 516-541): 手入力 `Normal_BlendShape` から `OscMapping[]` に変換する既存ロジック。**Req 2.1 の「手入力優先」はここで成立済み**。
- `BuildNormalLookup` (line 1014-1032): `_normalBlendShapeNames` HashSet を構築。手入力 entry の expressionId 集合は既に取得可能 → Req 2.2 の「heartbeat 由来差分のみ追加」の判定に再利用できる。
- `_runtimeMappings` (line 66): runtime 専用配列。手入力分 + heartbeat 由来分を merge する場として使える。
- `HasGazeMappings` (line 1111-1128): Gaze entry のみ存在ケースを判定する既存 helper。Req 2.4 の入口判定として再利用可。

**新規 / 拡張**
- `OscMapping` (Domain) は 3 フィールド (`OscAddress`, `BlendShapeName`, `Layer`) の readonly struct で**由来情報 (Manual/Auto) を持たない**。Req 2.5 の「出自を区別」は (a) parallel array で `bool[] isAutoMapping` を持つ、(b) Adapters 専用 wrapper struct を新設、のいずれか。Domain 契約に手を入れず Adapters 内で完結させるのが望ましい。
- merge 関数: 手入力の `expressionId` 集合と heartbeat 名集合の集合演算で「手入力 entry 全保持 + heartbeat 由来の差分のみ追加」を実装。

**ギャップ**: Missing（merge / 出自タグ）/ Constraint（既存 SerializeField 構造は維持必須 = Req 2.6）。

**複雑度**: S、**リスク**: Low（既存パターンの素直な拡張）

---

### Requirement 3: アドレスプリセット推定 (VRChat / ARKit / カスタム)

**既存資産**
- `AddressPresetKind` enum (`AddressPresetKind.cs`): 現状 `VRChat`, `ARKit` の 2 値。**Custom 値は未定義** → 本 spec で第 3 値追加が必要（送信側 SerializedField に影響するため後方互換確認は必要だが、enum の追加は破壊変更にはならない）。
- `OscAddressFormatter` (`OscAddressFormatter.cs`): `VRChatParameterPrefix = "/avatar/parameters/"` / `ARKitParameterPrefix = "/ARKit/"` の定数 + `FormatBlendShapeAddress(preset, name)` / `FormatBlendShapeAddressUtf8` / `GetOrAddBlendShapeAddressUtf8`（pool 化）。**Req 3.3 / 3.4 のアドレス組み立てはそのまま再利用可能**（VRChat / ARKit 両方サポート済み）。Custom 形式は `NotSupportedException` を投げるので拡張が必要。
- `OscReceiver.VRChatAddressPrefix` / `ARKitAddressPrefix` / `ExtractBlendShapeName` (`OscReceiver.cs:334-352`): 受信側の prefix 識別ロジック。**逆方向の「名前一覧からプリセット推定」には流用できないが定数源としては有効**。
- `ARKitDetector.ARKit52Names` / `PerfectSyncNames` (Domain): Req 3.4 の「ARKit 標準名」判定の単一情報源として活用可能。Adapters 層から Domain を参照する依存方向は問題なし。
- `PerfectSyncEyeLook.Names` (8 名): Gaze 専用。**ARKit 全名一覧との混同に注意**。本 spec は Gaze 対象外のため EyeLook 単独で見る必要はないが、Req 3.4 の `/ARKit/{name}` 推定で eyeLook 系も含むことが望ましい（`ARKitDetector.ARKit52Names` に含まれているため、それを参照すれば包含される）。

**新規 / 拡張**
- `AddressPresetEstimator`（仮、Adapters.OSC）: heartbeat 名一覧 → `AddressPresetKind`（または `string` 形式の custom 識別子）。判定戦略は (a) ARKit 名との一致率 (`ARKitDetector.DetectARKit`)、(b) heartbeat payload の preset hint（Req 5）、(c) fallback = VRChat。
- `AddressPresetKind.Custom` 追加 + `OscAddressFormatter` に custom prefix 受け取り API 追加（既存呼び出しは `enum` 値のみで完結するため、custom 用 overload を分けるのが安全）。

**ギャップ**: Missing（推定器 / Custom enum 値）/ Constraint（`OscAddressFormatter` の `GetBlendShapePrefix` switch が default で例外）。

**複雑度**: S–M、**リスク**: Low

---

### Requirement 4: Runtime 中の mapping 拡張と GC スパイク回避

**既存資産（拡張対象）**
- `OscDoubleBuffer.Resize(int)` (`OscDoubleBuffer.cs:102-128`): 既に存在。新サイズで `NativeArray<float>` を再確保し `_writeIndex = 0` にリセット。**受信スレッドが書き込み中の値が消える可能性**があるため、Req 4.5「再確保中の受信値を取りこぼさない」の保証は新規メカニズムが必要。
- `OscInputSource._mappingIndexToMeshIndex` (`OscInputSource.cs:43`): `readonly int[]`。**runtime 拡張不可**。`OscInputSource` を新規構築して `IInputSourceRegistry.Register` で**後勝ち上書き**するのが現実解（既存 spec で「同 id 重複登録は LogError + 後勝ち上書き」と明記、`IInputSourceRegistry.cs:28-32`）。後勝ち上書きの LogError を抑止する path を本 spec で別途検討する必要あり。
- `OscInputSource.TryWriteValues` (line 132-177): hot path での managed heap 確保は無し（`NativeArray<float>.ReadOnly` 取得 + ループのみ）。Req 4.4 の 0 byte 維持は構造的に達成可能。
- `HeartbeatConsistencyChecker._skipMask` / `_contributeMask` (`HeartbeatConsistencyChecker.cs:18-19`): `readonly BitArray`。constructor 渡し。**長さ変更は不可** → Req 4.6「`SkipMask` / `ContributeMask` の長さを再構築できる経路を提供」のために新メソッド（例: `RebuildForMappings(meshNames, receiverNames)`) もしくは Checker 自体の差し替えが必要。
- `OscBundleAccumulator` (`OscBundleAccumulator.cs`): `_buffer` を constructor で受け取り保持。OscDoubleBuffer 再確保時は accumulator も新インスタンスにする or `_buffer` 参照を nullable に変更する必要。
- `OscReceiverAdapterBinding.OnFixedTick` (line 439-454): メインスレッド呼び出し点。Req 4.3 の「再構築をメインスレッドで」のフックとして再利用可。

**新規 / 拡張**
- `HeartbeatConsistencyChecker.RebuildForMappings(meshNames, receiverNames)` 公開メソッド: 内部 `_skipMask` / `_contributeMask` を新サイズで再確保し、既存 `Clear()` と同じ初期化を行う。または Checker を毎回 new し直す（GC 1 回だがフレーム外なので許容範囲）。
- `OscDoubleBuffer` の atomic resize 機構: pending message を一時 list（メインスレッド処理）に貯めて、resize 後に新 buffer に再投入するパターン。あるいは「heartbeat 到着 → 受信スレッド lock → resize → unlock」のグローバルロック方式（短時間ロック）。design phase で取捨選択。
- `OscReceiverAdapterBinding` 内に「heartbeat 内容ハッシュ」を持ち、Req 4.1 / 4.2 の「変化検出時のみ再構築」を判定。

**ギャップ**: Missing（atomic resize / mask rebuild）/ Constraint（複数 field が `readonly`）。

**複雑度**: L、**リスク**: Medium（受信スレッドとメインスレッドの同期設計、Req 4.5 の「取りこぼさない」保証）

---

### Requirement 5: Heartbeat メッセージフォーマット拡張可否

**既存資産**
- `OscReceiverAdapterBinding.HandleHeartbeatMessage` (line 778-795): `message.values[i] is string` のみを `_heartbeatScratch.Add` で集約。**非 string 値は無視する仕組みが既にあるため、preset 識別子を別 OSC タイプタグ (`int` / `byte[]`) で送ってきても旧 receiver は安全に skip する**。Req 5.3 後方互換の前提が成立。
- `OscBundleBuilder.AddHeartbeatMessages` (`OscBundleBuilder.cs:353-`): 現状 `string[]` のみを書き出す。Req 5.3 の送信側拡張はここに preset payload を追加する形になるが、**送信側改修は本 spec 範囲外**であり、本 spec では「拡張形式の受信解釈契約」までを規定する。
- `OscRuntimeSettingsSO`: 受信側 preset 設定を持たない（Req 5.6 のシリアライズ非破壊条件と整合）。runtime 内部状態として保持すべき。

**新規 / 拡張**
- heartbeat payload スキーマ案（3 つを並列に評価する形で design phase に持ち込み）:
  - **Option A**: 現状維持 = `string[]` のみ → Req 3.2(b) の名前ベース推定で全ケースを処理（implement 最小、判定精度のみ課題）
  - **Option B**: `string[]` 末尾に sentinel `"__preset__"` + preset 名 `"vrchat"|"arkit"|"custom"` を追加 → 旧 receiver は普通の BlendShape 名として扱うので mesh と一致しなければ無視（warning 1 件は出る可能性）
  - **Option C**: `string[]` の後ろに別 OSC タイプタグ（`string` 1 件、`int` 1 件 など）を追記 → 旧 receiver の `is string` フィルタで暗黙 skip
- preset 識別子辞書: `"vrchat"` / `"arkit"` / `"custom"` の 3 値解釈 + 未知文字列の warning + fallback。

**ギャップ**: Decision Required（design phase で payload 方針確定）/ Constraint（送信側改修との連動）。

**複雑度**: S（受信側のみ）、**リスク**: Low

---

### Requirement 6: 既存実装との後方互換 / Gaze 結線維持

**既存資産（保護対象）**
- `OscReceiverDemoProfile.asset`: 5 件 mapping（4 件 `Normal_BlendShape` + 1 件 `Gaze_VRChat_XY`）を SerializeField で保持。Req 6.1 の「無改修で動作」のための非破壊性確認対象。
- `RegisterGazeSources` / `RegisterGazeRoutes` (line 543-648): Gaze 結線。Req 6.2 で「改変しない」と明記された経路 → 本 spec の改修は `Normal_BlendShape` mapping 解決経路に閉じる。
- `_zombiePolicy` / `_bundleSenderDecisions` / `SenderIdentity` 経路 (line 707-776): Req 6.3 で退行禁止対象。`OnStart` 内の初期化順序（`_zombiePolicy = new ZombieEvictionPolicy()` → `_heartbeatChecker` 構築 → `_buffer = new OscDoubleBuffer(...)`）を runtime mapping 拡張時にも同じ順序で再実行できる構造に整える必要。
- `_helperHost.Receiver.SetMessageFilter` (line 419): heartbeat 含む全 OSC を binding の `HandleIncomingOscMessage` 経由でフィルタする経路。`OscInputSource` 未登録でも message filter 自体は機能するため、Req 1.4 の「heartbeat 待ち」期間は filter のみ動作させればよい。
- `OscRuntimeSettingsSO.ReceiverEnabled` (line 73): Req 6.5「false 時は何も起動しない」既存挙動を保護対象。OnStart 早期 return 経路 (line 351) を改修後も維持する。
- `Dispose` (line 457-514): `_buffer.Dispose()` / `_helperHost` Destroy / 全 `[NonSerialized]` field の null 化。Req 6.6 の leak 防止のため、heartbeat 由来 mapping の追加 field も同様に Dispose で解放するチェックリストを追加。

**ギャップ**: Constraint（多数の既存挙動を退行させない）。

**複雑度**: 既存リファクタの一部として吸収（独立 effort なし）、**リスク**: Medium（既存 PlayMode 統合テスト群への影響範囲が広い）

---

### Requirement 7: 受け入れ条件 — OscReceiverDemo の空 Mappings 疎通

**既存資産**
- `OscReceiverDemoProfile.asset`: 現状 5 件 mapping を含む。Req 7.1 検証では `_adapterBindings.._mappings` を空にした variant（または手順）が必要。
- `OscOutputDemo.unity` + `OscOutputDemoProfile.asset` + `OscSenderOptions.json`: 送信側は `BlendShape Names` 空 = 全 BlendShape 自動送信 + heartbeat 5 秒間隔 + `/avatar/parameters/...` (VRChat) + `/ARKit/...`（別 endpoint）を既に出している。**送信側は本 spec の改修不要**。
- `OscReceiverDemoBootstrap.cs`: `Application.runInBackground = true` のみ。改修不要。
- `OscReceiverDemo/README.md`: 既に「Mappings 自動生成は別 spec で計画」と明記。本 spec で README を「空 Mappings 運用」に更新する。
- `OscSendReceiveTests` / `OscIntegrationTests` / `OscHeartbeatConsistencyTests`: 既存 PlayMode 統合テストパターン。`OscReceiverAdapterBindingAutoMappingIntegrationTests`（Req 7.5 で予定の test 名）を design phase で確定し、これらと並列配置する。

**新規 / 拡張**
- `OscReceiverDemoProfile.asset`（または並列 variant）: Mappings を空にした版を sample 用に提供するか、既存 asset を空に変更しドキュメントで「サンプル BlendShape 名は heartbeat 駆動で自動生成」と説明するかの方針確定。
- PlayMode テスト: 実 UDP loopback で `OscOutputAdapterBinding` → `OscReceiverAdapterBinding` (空 Mappings) → mesh BlendShape weight 反映を検証。

**ギャップ**: Missing（受け入れテスト / 空 Mappings sample 形態）。

**複雑度**: M、**リスク**: Medium（PlayMode flakiness、heartbeat タイミング待ちの test wait）

---

## 実装アプローチオプション

### Option A: 既存 `OscReceiverAdapterBinding.OnStart` 内のフェーズ分割

`OnStart` を以下 3 フェーズに再構成し、heartbeat 受信時に Phase 2 / 3 を再実行可能にする:
- Phase 1: socket / `OscReceiverHost` 起動 + `OscInputSourceRegistry` 関連の事前準備（mapping 0 件でも実行）
- Phase 2: mapping 確定（手入力 ∪ heartbeat 由来）
- Phase 3: `OscInputSource` 構築 + `IInputSourceRegistry.Register`（mapping ≥1 件で実行）

heartbeat 受信時は Phase 2 → Phase 3 を再実行（後勝ち上書きで `OscInputSource` を差し替え）。

**Trade-offs**: 既存ファイル 1 つに改修集約、lifecycle が直感的、Req 6 後方互換確保が容易 / `OscReceiverAdapterBinding.cs` が既に約 1400 行で更に膨らむ、phase 間状態管理が複雑化。

---

### Option B: 新規 `AutoMappingCoordinator` クラス抽出

`OscReceiverAdapterBinding` から「mapping 解決 + heartbeat 駆動拡張 + preset 推定」を切り出した新クラス（Adapters.OSC）を新設し、binding は Coordinator を持つだけにする。

**Trade-offs**: 単一責任原則の達成、テスト容易性（Coordinator 単体テスト）、`OscReceiverAdapterBinding` のサイズ抑制 / 新クラス間のデータ受け渡し設計が必要、`OscDoubleBuffer` / `HeartbeatConsistencyChecker` の所有関係がより不明瞭になる懸念。

---

### Option C: Option A + B のハイブリッド

- mapping 解決 / preset 推定のみを `AddressPresetEstimator` + `AutoMappingResolver`（小粒度 helper）として抽出
- lifecycle 制御は `OscReceiverAdapterBinding` に残す（Option A の phase 分割）
- `HeartbeatConsistencyChecker` 拡張は同クラスに `RebuildForMappings` を追加

**Trade-offs**: 改修の重心が binding 内に残るが、preset 推定など testable な単位は独立 / 影響範囲が広め、レビュー粒度が大きくなる。

---

## 横断的リスク / Research Needed

- **R1: 受信スレッド ↔ メインスレッド同期**（Req 4.3 / 4.5）— heartbeat callback (受信スレッド) と `OnFixedTick` (メインスレッド) の間で `OscInputSource` 差し替え / `OscDoubleBuffer.Resize` を行う際の lock 戦略。`AtomicSwap` bundle mode との相互作用を design で確定する必要。
- **R2: heartbeat payload 拡張可否**（Req 5）— Option A/B/C の選択は送信側改修発生の有無を決める。送信側改修が必要なら別 spec / 追加 task が連鎖。
- **R3: `IInputSourceRegistry` の後勝ち上書き LogError 抑止**（Req 4.3）— 既存仕様は重複登録で LogError を出す。heartbeat 駆動の差し替えで毎回 LogError が出ると Req 6.3 の退行扱いになりかねない。design phase で「予期された再登録」用の API（`ReplaceQuiet` / `Unregister + Register`）を整える。
- **R4: heartbeat 名と mesh 名の積集合空ケース**（Req 1.5）— `Debug.LogWarning` を 1 度だけ出す仕組みは `HeartbeatConsistencyChecker._loggedMismatchHashes` パターンを再利用できるが、auto mapping 側に転用するか別 instance に分離するか確定が必要。
- **R5: PlayMode 統合テストの heartbeat 待ち**（Req 7.5）— `ManualTimeProvider` + 強制 heartbeat 注入で決定論化するか、実 5 秒 wait（CI 不向き）で受け入れ条件を再現するかの判断。`OscHeartbeatConsistencyTests` の `HandleHeartbeat` 直接呼び出しパターンが参考になる。
- **R6: `OscReceiverAdapterBindingDrawer` (Inspector) の出自表示**（Req 2.5）— 「手入力 / 自動」表示は現状 UI に無い。preview 段階のため「diagnostic ログのみで OK」とするか、ListView 行に Badge を出すかの方針を design で決定。

## 実装複雑度 / リスク評価

| 項目 | Effort | Risk | 根拠 |
|------|--------|------|------|
| OnStart phase 分割 | M | Medium | 既存 lifecycle の構造変更、Req 6 全条件への影響波及 |
| HeartbeatConsistencyChecker 拡張 | S | Low | 既存 `Clear()` パターンの素直な拡張 |
| OscDoubleBuffer atomic resize | M | Medium | Spin / lock の選択、Req 4.5 取りこぼし防止 |
| `OscInputSource` 差し替え経路 | S | Low | 既存「後勝ち上書き」契約の活用 |
| AddressPresetEstimator 新設 | S | Low | `ARKitDetector` を Domain から参照する単純な判定 |
| heartbeat payload 拡張対応 | S | Low | 受信側 skip ロジックは既存、送信側拡張は別 spec |
| PlayMode 受け入れテスト | M | Medium | UDP loopback + heartbeat 注入の決定論化 |
| Sample / README 更新 | S | Low | 既存 Samples~ ファイル小規模修正 |
| **全体合計** | **L (1–2 週)** | **Medium** | lifecycle + 同期設計の重さが支配的 |

## design phase へ持ち込む論点

1. heartbeat payload 拡張方針（Req 5 の Option A/B/C 確定）
2. `OscInputSource` runtime 差し替え API（new + Register 後勝ち / 専用 ReplaceQuiet メソッド導入 のいずれか）
3. `OscDoubleBuffer.Resize` 中の受信値取りこぼし防止メカニズム
4. `HeartbeatConsistencyChecker` 拡張形態（`RebuildForMappings` メソッド追加 vs Checker 自体差し替え）
5. heartbeat 内容変化検出のハッシュ戦略（毎フレーム計算回避）
6. `AddressPresetKind.Custom` 追加と `OscAddressFormatter` の custom prefix サポート
7. Inspector の出自表示要否（Req 2.5、preview 段階での妥協点）
8. 受け入れテストの heartbeat 注入方式（実 UDP / direct invoke / TimeProvider 操作）
9. `OscReceiverDemoProfile.asset` を空 Mappings に書き換えるか variant 追加かの方針
10. `IInputSourceRegistry` 重複登録時の LogError 抑止経路（再登録を「予期される操作」として扱う API 検討）

## Next Steps

- **要件承認**: requirements.md を確定 → spec.json を `approvals.requirements.approved=true` に更新
- **design phase 着手**: `/kiro:spec-design osc-receiver-auto-mapping` で本ギャップ分析の論点 1〜10 を解決する設計を生成
- **本 gap-analysis の活用**: design.md の "Existing Code Analysis" / "Design Decisions" セクションへ転記してトレーサビリティ確保

---

## 主要参照ファイル

### 既存資産（再利用 / 改修対象）
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/AdapterBindings/OscReceiverAdapterBinding.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/InputSources/OscInputSource.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/HeartbeatConsistencyChecker.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/OscDoubleBuffer.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/OscBundleAccumulator.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/OscReceiverHost.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/OscReceiver.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/OscAddressFormatter.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/AddressPresetKind.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/OscMappingEntry.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/OscMappingMode.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/PerfectSyncEyeLook.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/SenderIdentity.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/ZombieEvictionPolicy.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/FailSafeMode.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/OscBundleBuilder.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/OscSender.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/AdapterBindings/OscSenderAdapterBinding.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/RuntimeSettings/OscRuntimeSettingsSO.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/Json/Dto/OscMappingEntryDto.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Editor/AdapterBindings/OscReceiverAdapterBindingDrawer.cs`
- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Adapters/AdapterBuildContext.cs`
- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Adapters/IInputSourceRegistry.cs`
- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Models/OscMapping.cs`
- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Services/ARKitDetector.cs`
- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Adapters/ScriptableObject/FacialCharacterProfileSO.cs`

### Sample / テスト
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Samples~/OscReceiverDemo/OscReceiverDemo.unity`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Samples~/OscReceiverDemo/OscReceiverDemoProfile.asset`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Samples~/OscReceiverDemo/OscReceiverDemoSettings.asset`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Samples~/OscReceiverDemo/OscReceiverOptions.json`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Samples~/OscReceiverDemo/README.md`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Samples~/OscOutputDemo/OscOutputDemo.unity`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Samples~/OscOutputDemo/README.md`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Tests/PlayMode/Integration/OscHeartbeatConsistencyTests.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Tests/PlayMode/Integration/OscReceiverAdapterBindingIntegrationTests.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Tests/PlayMode/Integration/OscSendReceiveTests.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/HeartbeatConsistencyCheckerTests.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/InputSources/OscInputSourceTests.cs`
- `FacialControl/Packages/com.hidano.facialcontrol.osc/Tests/EditMode/Adapters/OSC/OscDoubleBufferTests.cs`

### spec / 関連分析
- `.kiro/specs/osc-receiver-auto-mapping/requirements.md`
- `.kiro/specs/osc-output-binding/gap-analysis.md`（フォーマット参考 + 隣接 spec の文脈）
