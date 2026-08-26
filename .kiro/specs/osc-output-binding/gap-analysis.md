# OSC Output Binding ギャップ分析

`/kiro:validate-gap osc-output-binding`（2026-05-14）で実施した、requirements.md と既存コードベースの実装ギャップ分析の結果。

## 分析サマリー（要点）

- **新規 Domain バス**: `IFacialOutputObserver` / `FacialOutputBus` を新設し、`FacialController.LateUpdate` の `_layerUseCase.UpdateWeights → BlendedOutputSpan` 後・`_boneWriter.Apply` 前で発火させるのが Req 1.2 の「`OnLateTick` 直前 + 同フレーム送信完結」を満たす唯一の整合点。ただし `AdapterBindingHost.LateTick` は VContainer 経由で呼ばれ、Unity の MonoBehaviour 実行順との相対順序が現状無制御 → 設計フェーズで `DefaultExecutionOrder` か `OnLateTick` を **bus 通知から同期 dispatch** する pull 型に切替える検討が必須。
- **送信側は新規 Binding 1 件 + 既存 OscSender の拡張**: 既存 `OscSender.SendAll` は単発 Message 送信のみで bundle 非対応。`uOSC.Bundle` 型は `uOscClient.Send(Bundle)` で利用可能なため、`OscSender` 拡張または新規 `OscBundleSender` を介して bundle 化する。Send パスは uOSC 内部で **GC アロケーションが多い**（毎送信 `new Message`、`MemoryStream`、`object[]`）ため Req 10.1（GC ゼロ）と既存 uOSC 実装が衝突 → ユーザー判断により **uOSC vendor copy + 内部修正**で対処（詳細は `uosc-modification-plan.md` 参照）。
- **受信側は既存 `OscReceiverAdapterBinding` の破壊的拡張**: uOSC の `Parser` は bundle を平坦化して 1 Message ずつ dispatch する一方、**`Message.timestamp` に bundle timetag をコピー**するため、これが bundle-atomic Swap の自然な相関キー。Req 11.8 はこれを利用して "同一 timetag のメッセージ群が揃ったら Swap" 機構を新設する設計が有力。
- **`GazeBindingConfig` 破壊変更**: ユーザー判断により後方互換不要・構造拡張案を採用。左目用 / 右目用 sourceId を `GazeBindingConfig` 自体に持たせ、JSON DTO・`InputSystemAdapterBinding`・既存テストの一斉改修を許容する。
- **IP allowlist は要件から削除**: uOSC が受信時に送信元 IP を捨てる実装上の制約と、ゾンビ排除が送信元 UUID + 起動時刻だけで成立することから、ユーザー判断により Req 11 から allowlist を削除。
- **JSON 経路の固有制約**: コアの `SystemTextJsonParser` は名前と裏腹に実装が `JsonUtility` で書かれている（Req 6.10 の "System.Text.Json ベース" 記述は実装と乖離）。新規 DTO `OscSenderOptionsDto` / `OscReceiverOptionsDto` は `[Serializable]` で `JsonUtility` 互換とするのが既存経路と整合する選択肢で、要件 6.10 の文言は設計フェーズで再表現が必要（R5）。

---

## 要件別 ギャップ分析

### Requirement 1: Domain 層の表情出力バス（BlendShape 値 + Gaze Vector2）

**既存資産（再利用可）**
- `LayerUseCase.BlendedOutputSpan` (`Application/UseCases/LayerUseCase.cs`): `_finalOutput` を `ReadOnlySpan<float>` で zero-alloc 公開済み。Bus が `ReadOnlySpan<float>` で BlendShape スナップショットを通知する経路は **そのまま転送するだけ** で実現できる（Req 1.4 の GC-free を最短達成）。
- `Domain.Adapters.AdapterBuildContext`: `IInputSourceRegistry` を介して Gaze 入力源（`IAnalogInputSource`）を解決済み。
- `IAnalogInputSource.TryReadVector2`: Domain 純度を保ったまま Gaze の `(float x, float y)` 値を取り出せる既存 API。
- `IInputSource` / `InputSourceRegistry`: 既に同じ DI コンテナ内で IInputSource を解決する経路があるため、Bus を `Lifetime.Scoped` で同じ child scope に登録できる。

**新規コンポーネント（提案名前空間/レイヤー）**
- `Hidano.FacialControl.Domain.Adapters.IFacialOutputObserver` (Domain)
- `Hidano.FacialControl.Domain.Adapters.IFacialOutputBus` (Domain)
- `Hidano.FacialControl.Domain.Services.FacialOutputBus` (Domain)
- `Hidano.FacialControl.Domain.Models.GazeSnapshot` (Domain) — `(string ExpressionId, float X, float Y)` の readonly struct
- `FacialController` の `LateUpdate` 終端で `_facialOutputBus.Publish(blendShapeSpan, gazeBuffer)` を呼ぶ薄いフック

**複雑度**: M (3–7 日)、**リスク**: Medium（R1: Unity 実行順制御）、**Breaking Change**: なし

---

### Requirement 2: OscSenderAdapterBinding の AdapterBinding lifecycle 結線

**既存資産**: `OscReceiverAdapterBinding` の lifecycle 構造（OnStart で AddComponent + Configure → OnFixedTick → Dispose）が **完全に対称な雛形**として再利用可能。`OscSender.Configure(endpoint, port, OscMapping[])` も既存。

**新規コンポーネント**: `Hidano.FacialControl.Adapters.AdapterBindings.OscSenderAdapterBinding` (Adapters)、`IFacialOutputObserver` 実装。

**複雑度**: L (1–2 週)、**リスク**: Medium（R2: uOSC GC）

**横断的論点**: `OscSender.Configure` の signature が **VRChat 形式固定アドレス**の `OscMapping` を要求するため、Req 4（プリセット案 B = 名前のみ保持）と矛盾。設計フェーズで Configure を string[] + preset enum を受ける overload に拡張する必要あり。

---

### Requirement 3: 複数送信先（multi-endpoint unicast）と OSC bundle アトミック性

**既存資産**: `uOSC.Bundle.Add(Message)` + `uOscClient.Send(Bundle)` で bundle 化 UDP 送信は API としては存在。

**新規コンポーネント**: `OscSenderEndpointConfig`、`AddressPresetKind` enum、endpoint ごとの `OscSender` 配列、bundle 構築用 reusable buffer。

**複雑度**: M、**リスク**: Medium（R2: uOSC GC、UDP MTU 1500 制約での bundle 分割）

**横断的論点**: bundle 内データグラム上限を超える場合の分割ポリシー（同一 timetag を継承して複数 bundle に分割）を設計で確定。

---

### Requirement 4: アドレス形式プリセット（VRChat / ARKit + PerfectSync 互換 Gaze）

**既存資産**
- `OscReceiver.VRChatAddressPrefix` = `/avatar/parameters/`、`OscReceiver.ARKitAddressPrefix` = `/ARKit/` の定数。
- `ArKitOscAnalogSourceTests.BuildArKit52ParameterNames()` に PerfectSync 52 名のリスト（eyeLookXxx 8 名は別途 const 化が必要）。

**新規コンポーネント**: `OscAddressFormatter`（zero-alloc アドレス生成）、`PerfectSyncEyeLook`（8 名 const + Vector2 ⇔ 8 float の双方向変換関数）。

**複雑度**: S (1–3 日)、**リスク**: Low

**横断的論点**: 符号定義（左目 x = `eyeLookInLeft - eyeLookOutLeft` か `eyeLookOutLeft - eyeLookInLeft` か）を Apple ARKit doc と `GazeBonePoseProvider` の符号反転を一覧表で固定する必要あり。

---

### Requirement 5: ループバック抑制

**既存資産**: `OscReceiverAdapterBinding.Endpoint` / `.Port` getter で listen endpoint 取得可能。VContainer child scope 内に send/recv binding が同居する経路あり。

**新規コンポーネント**: `LoopbackSuppressionPolicy`（IP+port 集合差の判定器）。

**複雑度**: S、**リスク**: Low

---

### Requirement 6: JSON 永続化（送信 / 受信 DTO）

**既存資産**
- `Hidano.FacialControl.Adapters.Json.Dto.OscOptionsDto` — `stalenessSeconds` のみ持つ薄い DTO。
- `SystemTextJsonParser.cs` — **名前と裏腹に `JsonUtility` ベース実装**。`[Serializable]` DTO を `JsonUtility.FromJson<T>` で読む、unknown key skip、必須欠落時の警告 + defaulting の挙動が既存テストで担保。

**新規コンポーネント**: `OscSenderOptionsDto`、`OscReceiverOptionsDto`（allowlist 削除済み、mode 別 `OscMappingEntryDto[]` を内包）、`OscMappingEntryDto`、`OscSenderEndpointDto`。Gaze Vector2 受信は OscReceiverAdapterBinding に統合（D2）されたため `OscGazeReceiverOptionsDto` は新設しない。

**複雑度**: M、**リスク**: Low–Medium（既存 inline serialize 構造との適合性）

**横断的論点（R5）**: 要件 6.10「`System.Text.Json` ベースと整合 / `JsonUtility` を新規に持ち込まない」は実装と矛盾。**現実の `SystemTextJsonParser` は `JsonUtility` で実装されている**ため、要件記述を「既存 JSON パーサと整合する」に丸める文言修正が必要。

---

### Requirement 7: Editor 拡張（UI Toolkit Drawer）

**既存資産**: `OscReceiverAdapterBindingDrawer`（UI Toolkit 雛形）、`AdapterBindingsListView`（親 UI）、`ArKitOscAdapterBindingDrawer`（string[] フィールド UI 例）。

**新規コンポーネント**: `OscSenderAdapterBindingDrawer`、拡張版 `OscReceiverAdapterBindingDrawer`（mode 別 fold-out で BlendShape / Gaze 統合表示）。Gaze Vector2 受信専用 Drawer は新設しない（D2 で binding 統合済み）。

**複雑度**: M、**リスク**: Low

---

### Requirement 8: テスト

**既存資産**: `OscSendReceiveTests` / `OscIntegrationTests`（実 UDP loopback テストの雛形）、`ArKitOscAnalogSourceTests`（subscribe 経路雛形）、`ManualTimeProvider`（staleness テストで再利用）。

**新規テスト**: EditMode 4 件 + PlayMode 8 件（bundle アトミック / フェイルセーフ / ゾンビ排除 / heartbeat 整合 / Gaze 左右非対称 / GC ホットパス監視 等）。

**複雑度**: L、**リスク**: Medium（Profiler 差分テストの flakiness）

---

### Requirement 9: サンプル + ドキュメント

**既存資産**: `Multi Source Blend Demo` の dev mirror パターン、`StreamingAssets/FacialControl/{name}/profile.json` 配置規約。

**新規構成**: `Samples~/OscOutputDemo` + `Samples~/OscReceiverDemo` の 2 サンプル + Assets/Samples 二重管理（OSC パッケージは現状 `package.json` samples 配列が未存在 → 新規追加）。

**複雑度**: M、**リスク**: Low

---

### Requirement 10: パフォーマンス・アーキテクチャ制約

**既存資産**: `OscDoubleBuffer`、`OscInputSource.TryWriteValues`、`LayerUseCase.BlendedOutputSpan` はすべて zero-alloc 設計済み。

**横断リスク（R2）**: uOSC が GC を発生させるため、`Req 10.1` の完全達成には uOSC vendor copy + 内部修正が必要（ユーザー判断確定）。詳細は `uosc-modification-plan.md`。

---

### Requirement 11: OscReceiverAdapterBinding（受信側）の破壊的拡張

**既存資産（拡張対象）**
- `OscReceiverAdapterBinding` — endpoint / port / stalenessSeconds の SerializeField + Configure + OnStart で `OscReceiverHost` 起動 + `OscInputSource` 登録のフロー。
- `OscInputSource.TryWriteValues` (`OscInputSource.cs:81-104`) — staleness 超過時 false 返却の挙動が Req 11.3 のフェイルセーフ復帰のフックとして使える。
- `OscDoubleBuffer.Swap` — bundle 境界に揃える改修対象。
- `OscReceiver.HandleOscMessage` — 受信スレッドコールバック。bundle メッセージは uOSC Parser によって個別 dispatch されるが **全 Message が同じ `timestamp`** を持つため bundle 識別キーとして利用可能。
- `OscReceiver.RegisterAnalogListener` — 任意アドレスへの float リスナー登録。送信元識別ヘッダ / heartbeat のメタアドレス（例 `/_facialcontrol/sender_id`、`/_facialcontrol/heartbeat`）にも流用可能。

**新規コンポーネント**
- `OscBundleAccumulator` — `Message.timestamp` をキーに同 bundle メッセージを一時蓄積し、bundle 終端検出（次の異なる timestamp / N ms 経過）で `OscDoubleBuffer.Swap` を呼ぶ機構
- `SenderIdentity`（UUID + 起動時刻の値型 + パース）
- `ZombieEvictionPolicy`（観測中 UUID 群から「最新起動時刻」を選択）
- `HeartbeatConsistencyChecker`（送信側 BlendShape 名一覧 vs 受信側 mapping の差分検出 + 部分反映マスク生成）
- `FailSafeMode` (enum: RevertToBase / HoldLastValue)

**注（IP allowlist は削除）**: 元 R3 の課題（uOSC が送信元 IP を捨てる）はユーザー判断により allowlist 機能を要件から外すことで解消。ゾンビ排除は送信元 UUID + 起動時刻のみで行う。

**複雑度**: L (1–2 週)、**リスク**: Medium（R4: Bundle-atomic timeout 設計）、**Breaking Change**: 大（OscReceiverAdapterBinding の SerializeField 構造変更、テスト群への波及）

---

### Requirement 12: Gaze Vector2 受信（OscReceiverAdapterBinding 内 mode 統合）

**既存資産**: `IAnalogInputSource` / `InputSourceRegistry.Register(slug, sub, source)` で `slug:sub` 形式の合成キー登録が可能。`ArKitOscAnalogSource` の subscribe + `pendingValues` 蓄積パターンを 8 名に限定して流用可能。`InputSystemAdapterBinding` の `ExpressionBindingEntry.bindingMode` パターンが「1 binding 内で複数 mode の入力を扱う」先行事例。

**新規コンポーネント**: `OscMappingEntry`、`OscMappingMode`（enum: Normal_BlendShape / Gaze_VRChat_XY / Gaze_ARKit_8BS）、`GazeVector2InputSource`（OscReceiverAdapterBinding 内で構築・registry に slug 登録）、`PerfectSyncEyeLook`。Gaze 専用 binding（`OscGazeReceiverAdapterBinding`）は **新設しない**（D2）。

**複雑度**: M、**リスク**: Low（R8 解消、binding 間依存無し）

---

### Requirement 13: `GazeBindingConfig` の左右別 Vector2 入力対応（構造拡張、後方互換不要）

**既存資産（破壊変更対象）**
- `GazeBindingConfig.cs` — 現状は `expressionId` のみ持ち、sidecar（`ExpressionBindingEntry` for InputSystem）が action 名で入力源を解決する設計。
- `GazeBonePoseProvider.Apply()` — `EyeBinding` 内に左右別 `IAnalogInputSource` を持つ構造を既にサポート（左右別 source 注入は既存 API で実現可能）。
- `InputSystemAdapterBinding.BuildGazeProvider` — 現状 `expressionId` 1 個 → action 1 個 → analog source 1 個 → `new GazeBoneBinding(config, source)` 1 個。**改修対象**。

**新規 / 改修コンポーネント**
- `GazeBindingConfig` に `sourceIdLeft` / `sourceIdRight` フィールドを追加（**後方互換不要、必須化**）
- `ExpressionBindingEntry`（InputSystem 経路）も左右別 actionName に対応する破壊的変更
- `GazeBindingConfigDto` および `FacialCharacterProfileConverter` の JSON 経路を改修
- 既存テスト（`FacialCharacterProfileSO_GazeConfigsRoundTripTests`、`FacialCharacterProfileExporter_GazeConfigsTests` 等）の一斉改修

**複雑度**: M、**リスク**: Medium（InputSystem 経路への波及、テスト改修ボリューム）

**Breaking Change スコープ**: **大**
- `GazeBindingConfig` の必須フィールド追加 → 既存 SO アセットの構造変更
- `InputSystemAdapterBinding` の Gaze 解決ロジック改修
- `FacialController.ConfigureAdapterBindingsWithGazeConfigs` の reflection injection 確認
- 既存 Gaze 関連テスト全件改修
- ユーザー方針: OSC binding が preview 段階のため migration 不要、`InputSystem` 側の SO ファイル / JSON も破壊的変更を許容

---

### Requirement 14: 送信元識別ヘッダ / heartbeat の送信仕様

**既存資産**: なし（純粋新規）。

**新規コンポーネント**: `SenderIdentityGenerator`（UUID + Unix ms 生成）、`/_facialcontrol/sender_id` / `/_facialcontrol/blendshape_names` 等のメタ OSC アドレス、heartbeat タイマー。

**複雑度**: S、**リスク**: Low

---

## 横断的リスクサマリ（Cross-cutting Risks / Constraints）

### R1: Unity 実行順制御（Req 1.2 中核） — Design で必ず確定
`FacialController.LateUpdate`（MonoBehaviour）と `AdapterBindingHost.LateTick`（VContainer ILateTickable）の相対順序が現状 PlayerLoop 上で未保証。次のいずれかを設計で確定:
- (a) `FacialController.LateUpdate` 内で `FacialOutputBus.Publish` を呼んだ直後に **同期的に** `OscSenderAdapterBinding.SendBundle` を駆動する pull 型設計（OnLateTick lifecycle を使わない）
- (b) `DefaultExecutionOrder` で `FacialController` の LateUpdate を VContainer よりも前に強制配置
- (c) Bus を **frame counter スタンプ付き**にし、binding 側 OnLateTick で「同フレームのスタンプが届いていなければ skip して次フレームに送る」設計（フレーム遅延 1 を受容）

### R2: uOSC ライブラリの GC アロケーション（Req 10.1 中核） — 確定済み
ユーザー判断により **uOSC vendor copy + 内部修正**を採用。詳細は `uosc-modification-plan.md` を参照。

### R3: uOSC が受信時に Remote IP を捨てる（Req 11.5 中核） — 確定済み
ユーザー判断により **IP allowlist 機能を Req 11 から削除**。ゾンビ排除は送信元 UUID + 起動時刻のみで行う。要件側を本判断に合わせて更新する。

### R4: Bundle-atomic 化と `Message.timestamp` 依存（Req 11.8 中核） — Design で確定
uOSC Parser は bundle timetag を bundle 内 Message にそのまま継承するため、これを bundle 識別キーとして使える。bundle 終端は明示通知されないため **N ms タイムアウトベース確定**が必須。設計フェーズでタイムアウト値（5 ms / 10 ms など、実機 RTT 考慮）、フレーム境界との関係、bundle 跨ぎの扱いを確定。

### R5: `SystemTextJsonParser` 名と実装の乖離（Req 6.10 中核） — 要件文言修正
クラス名と裏腹に実装は `JsonUtility`。要件 6.10 を「既存 JSON パーサ経路（`SystemTextJsonParser`）と整合する形で実装し、新たな JSON ライブラリを持ち込まない」に文言整理して、現実と要件を一致させる。

### R6: `GazeBindingConfig` 破壊変更の波及（Req 13） — 確定済み
ユーザー判断により **後方互換不要・構造拡張案**を採用。`InputSystemAdapterBinding` / `GazeBindingConfigDto` / 既存 Gaze テスト群への一斉改修を許容する。影響範囲は `InputSystemAdapterBinding.BuildGazeProvider`、`FacialCharacterProfileConverter`、`FacialCharacterProfileExporter_GazeConfigsTests`、`FacialCharacterProfileSO_GazeConfigsRoundTripTests`、`FacialCharacterProfileSOInspectorGazeConfigsTests`。

### R7: Sample 二重管理（Req 9.4）
`Samples~/` と `Assets/Samples/` の同期手順を `docs/work-procedure.md` に明文化（CLAUDE.md の Samples 二重管理ルールに従う）。OSC 用 dev mirror は現状未存在で、新規ディレクトリ構造を切る必要あり。Low risk。

### R8: 受信側依存解決順（Req 12） — 解決済み（2026-05-15 設計レビュー）
**当初の懸念**: `OscGazeReceiverAdapterBinding` が `OscReceiverAdapterBinding` の listen endpoint / `OscReceiver` を参照したいが、binding 間の `OnStart` 順序は SO 上のリスト順依存。

**解決方針**: Gaze Vector2 受信を別 binding に切り分けず、`OscReceiverAdapterBinding` 内の mode 別 entry（`OscMappingEntry.bindingMode = Gaze_VRChat_XY / Gaze_ARKit_8BS`）として統合する判断に変更。`InputSystemAdapterBinding` の単一 binding 内 mode 集約パターン（`ExpressionBindingEntry.bindingMode`）を踏襲。

**影響**: cross-binding lookup 機構（`AdapterBuildContext.OscBindingProvider` 案 / `IInputSourceRegistry` 経由案 / `IBindingScope` 列挙案）はすべて不要となり、Domain 契約への追加は CI1 で確定した `IFacialOutputBus` の 1 フィールドのみで済む。フェイルセーフ時の状態伝播も同一 binding 内の内部呼出で完結し、binding 順序依存は消滅。

---

## 推奨実装順序（タスク分割の参考）

1. **R2 解決: uOSC vendor copy + 内部修正**（`uosc-modification-plan.md` 参照） — 他のすべての前提
2. `FacialOutputBus`（Req 1） — 他の binding の前提
3. `OscBundleAccumulator` + `OscReceiverAdapterBinding` 拡張（Req 11） — 受信側基盤
4. `OscSenderAdapterBinding` 骨格（Req 2） + 単一 endpoint 送信
5. multi-endpoint + bundle 送信（Req 3, 14） — heartbeat / 送信元 ID
6. アドレスプリセット + Gaze 送信（Req 4） — VRChat と ARKit (PerfectSync) 互換
7. ループバック抑制（Req 5）
8. `GazeBindingConfig` 構造拡張（Req 13） — 後方互換不要、テスト一斉改修
9. Gaze 受信 binding（Req 12）
10. JSON DTO（Req 6） + Editor Drawer（Req 7） + サンプル（Req 9） — 並列可
11. E2E / 性能テスト（Req 8） — 各機能完成後に追加

---

## 主要参照ファイル（絶対パス）

### 既存資産（再利用 / 改修対象）
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.osc\Runtime\Adapters\OSC\OscSender.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.osc\Runtime\Adapters\OSC\OscSender.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.osc\Runtime\Adapters\OSC\OscReceiver.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.osc\Runtime\Adapters\OSC\OscReceiverHost.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.osc\Runtime\Adapters\OSC\OscDoubleBuffer.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.osc\Runtime\Adapters\AdapterBindings\OscReceiverAdapterBinding.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.osc\Runtime\Adapters\AdapterBindings\ARKit\ArKitOscAdapterBinding.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.osc\Runtime\Adapters\InputSources\OscInputSource.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.osc\Runtime\Adapters\InputSources\ArKitOscAnalogSource.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.osc\Runtime\Adapters\Json\Dto\OscOptionsDto.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.osc\Editor\AdapterBindings\OscReceiverAdapterBindingDrawer.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.osc\package.json`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Domain\Adapters\AdapterBindingBase.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Domain\Adapters\AdapterBuildContext.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Adapters\DependencyInjection\AdapterBindingHost.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Adapters\Playable\FacialController.cs` (LateUpdate 122–153, Initialize 159–189)
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Application\UseCases\LayerUseCase.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Adapters\ScriptableObject\GazeBindingConfig.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Adapters\Bone\GazeBoneBinding.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Adapters\Bone\GazeBonePoseProvider.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Adapters\Json\SystemTextJsonParser.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Adapters\InputSources\InputSourceRegistry.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol\Runtime\Domain\Interfaces\IAnalogInputSource.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Packages\com.hidano.facialcontrol.inputsystem\Runtime\Adapters\AdapterBindings\InputSystemAdapterBinding.cs` (BuildGazeProvider 520–559)

### uOSC ライブラリ（vendor copy + 改修対象）
- `D:\Personal\Repositries\FacialControl\FacialControl\Library\PackageCache\com.hidano.uosc@f7a52f0c524d\Runtime\Core\Bundle.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Library\PackageCache\com.hidano.uosc@f7a52f0c524d\Runtime\Core\Parser.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Library\PackageCache\com.hidano.uosc@f7a52f0c524d\Runtime\uOscClient.cs`
- `D:\Personal\Repositries\FacialControl\FacialControl\Library\PackageCache\com.hidano.uosc@f7a52f0c524d\Runtime\uOscServer.cs`

---

## Document Status

- 生成: 2026-05-14（`/kiro:validate-gap osc-output-binding` 実行時の validate-gap-agent 報告内容 + 後続のユーザー判断を統合）
- spec.json: 本ファイルは spec の補助ドキュメントであり、`approvals` には影響しない
- 関連: [`uosc-modification-plan.md`](uosc-modification-plan.md) — R2 解決のための uOSC 内部改修詳細
