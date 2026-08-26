# Implementation Plan

> 本 tasks.md は design.md「Migration Strategy」に基づき、preview.2 スコープを Phase 1 → Phase 7 → Phase 2 → Phase 3 → Phase 4 → Phase 5 → Phase 6 → Phase 8 → Phase 9 の順序（D9 / CI3）で主タスク化する。Phase 10 / Phase 11 は preview.3 マイルストーンとして末尾の Deferred セクションに記載する。
>
> TDD（Red-Green-Refactor）厳守。EditMode テストは原則として該当機能の実装サブタスクと同じ phase 内で先行配置（Red）し、実装サブタスクで Green に到達させる。PlayMode E2E / 性能テストは Phase 9 に集約する。
>
> `(P)` は同一 phase 内で先行サブタスクに依存せず並行実行可能なものに限り付与する。Foundation 系 / 破壊的変更系 / 統合系のタスクには付けない。

## preview.2 milestone

### Phase 1: Domain 出力バス基盤と FacialController 結線

- [x] 1. Domain 層 FacialOutputBus と `AdapterBuildContext` 拡張で post-blend BlendShape + Gaze 観察基盤を構築する

- [x] 1.1 Domain 純度を保った Gaze スナップショット値型と観察契約を定義する
  - `Hidano.FacialControl.Domain.Models` 名前空間に `GazeSnapshot`（`expressionId` / x / y を保持する不変構造体）を新設し、`UnityEngine.Vector2` を Domain 層に持ち込まない構造とする
  - `Hidano.FacialControl.Domain.Adapters` 名前空間に `IFacialOutputObserver`（post-blend Span + Gaze Span を受け取る単一通知メソッド）と `IFacialOutputBus`（Subscribe / Unsubscribe / HasObservers / Publish を提供）の 2 インターフェースを定義する
  - Span 引数は呼出スコープ内のみ有効である旨と、observer 側は値をコピーすべき旨を XML doc コメントに明示する
  - 観察完了状態: Domain アセンブリのみで型がコンパイル可能で、`UnityEngine` / `uOsc` 参照を持たないことを asmdef で確認できる
  - _Requirements: 1.1, 1.3, 1.9_
  - _Boundary: Domain.Models, Domain.Adapters_

- [x] 1.2 FacialOutputBus 実装（subscribe/unsubscribe 並行安全 + 例外吸収 + 空オブザーバ skip）
  - 内部に `_observers` の主リストと `_pendingAdds` / `_pendingRemoves` の遅延適用リストを持ち、`Publish` 列挙中の `Subscribe` / `Unsubscribe` を列挙完了後に反映する
  - `HasObservers` が false の場合は `Publish` を即時 return し、Span コピーや列挙コストを発生させない
  - observer.OnFacialOutputPublished が例外を投げた場合は Unity 標準ログ（`Debug.LogException`）にログ出力し、他 observer への通知を継続する
  - subscribe/unsubscribe 列挙中変更 / 空 observer skip / 例外伝播ログ化 / BlendShape + Gaze 同時通知 / 未接続 Gaze 除外を EditMode テストで Red → Green に到達させる
  - 観察完了状態: `FacialOutputBusTests` が全ケースで緑になる
  - _Requirements: 1.1, 1.4, 1.5, 1.6, 1.7, 1.8, 1.10_
  - _Boundary: Domain.Services_

- [x] 1.3 `AdapterBuildContext` への `IFacialOutputBus` 必須フィールド追加（破壊的拡張）
  - 既存 `AdapterBuildContext` のコンストラクタ引数に `IFacialOutputBus FacialOutputBus` を追加し、null 時は `ArgumentNullException` を投げる
  - 既存 binding（InputSystem / Bone / ARKit 等）が当該フィールドを参照しないことを確認し、追加によるロジック変化が無いことを単体テストで検証する
  - 観察完了状態: `AdapterBuildContext` の構築経路を通る既存 binding テストがすべて緑のまま、`FacialOutputBus` フィールドが非 null であることを確認するテストが追加されている
  - _Requirements: 1.1, 1.2, 2.4, 10.2_
  - _Boundary: Domain.Adapters_

- [x] 1.4 FacialController の LateUpdate に pull 型 Publish フックを追加し VContainer child scope に Bus を登録する
  - `BuildAdapterBindingsChildScope` で `IFacialOutputBus` を Scoped で register し、`AdapterBuildContext` コンストラクタへ渡す
  - `LateUpdate` 末尾、`_layerUseCase.UpdateWeights` 直後・`BoneWriter.Apply` 前後の規定位置（design 採用案 (a)）で `_bus.Publish(blendShapeSpan, gazeSnapshotBuffer.AsSpan())` を呼ぶフックを追加する
  - `_gazeSnapshotBuffer` を `OnEnable` で `_characterSO.GazeConfigs.Count` ぶん確保し、Gaze 入力源が `IsValid=false` の `expressionId` は通知から除外する
  - Publish 時点で観察した binding 側 scratch buffer は同フレームの `OnLateTick` で UDP 送出される、という不変条件を E2E テスト（Phase 9）から逆引き可能な hook 構造にする
  - 観察完了状態: ダミー observer を登録すると 1 フレームに 1 回だけ post-blend Span + Gaze Span を受け取れることを EditMode/PlayMode どちらかで検証可能になる
  - _Requirements: 1.2, 1.4, 1.8, 1.10, 10.2_
  - _Boundary: Adapters.Playable.FacialController, AdapterBindingHost wiring_

### Phase 7: GazeBindingConfig 破壊的改修 + InputSystem 同調 + Gaze テスト群一斉改修

> 本 phase は範囲が広いため 7a / 7b / 7c の 3 sub-task 単位で 3 PR に分割する選択肢を保持する（7a 完了で SO/Dto Round-trip 緑、7b 完了で InputSystem Gaze 緑、7c 完了で残る Gaze テストすべて緑）。Phase 2 より前に実施することで OSC 受信側 `.left` / `.right` publish 規約と GazeBindingConfig の解決経路の機能空白を回避する。

- [x] 2. `GazeBindingConfig` を `expressionId` 必須 + `useDistinctLeftRight` flag + 条件付き sourceIdLeft/Right の構造へ破壊的改修し、Cross-binding Slug Convention を導入する

- [x] 2.1 (7a) `GazeBindingConfig` / DTO / Converter の構造改修と Round-trip テスト
  - `GazeBindingConfig` から旧単一 `sourceId` フィールドを削除し、`expressionId`（必須 string） / `useDistinctLeftRight`（既定 false） / `sourceIdLeft` / `sourceIdRight`（true 時のみ意味を持つ）を追加する
  - 角度制限フィールド（`outerYawAngle` / `innerYawAngle` / `lookUpAngle` / `lookDownAngle`）は左目 / 右目に対して左右別に適用する既存ロジックを維持する
  - `GazeBindingConfigDto` および `FacialCharacterProfileConverter` の双方向変換を新構造に合わせて改修し、`useDistinctLeftRight = false` 既定パスと `true` パスの両方を JSON Round-trip テストで網羅する
  - 観察完了状態: `FacialCharacterProfileSO_GazeConfigsRoundTripTests`（改修済み）が新構造で緑になり、SO アセットを再構築した dev project でロード/セーブが安定する
  - _Requirements: 13.1, 13.4, 13.6_
  - _Boundary: Adapters.ScriptableObject.GazeBindingConfig, Adapters.Json.Dto.GazeBindingConfigDto, FacialCharacterProfileConverter_

- [x] 2.2 (7b) `InputSystemAdapterBinding.ExpressionBindingEntry`（bindingMode=Gaze）を `useDistinctLeftRight` + 左右別 actionName へ同調
  - `ExpressionBindingEntry` の Gaze 用フィールドを `useDistinctLeftRight` flag + `actionNameLeft` / `actionNameRight` に置き換え、`useDistinctLeftRight = false` 時は既存単一 `actionName` を両目共通で使う動作と等価にする
  - `BuildGazeProvider` の InputAction 解決を左右別 actionName 解決に変更し、片方欠落時は解決済み側を両目 fallback、両方未解決時はベース姿勢維持とする
  - 観察完了状態: InputSystem 経由の Gaze 入力が `useDistinctLeftRight = false` / `true` 双方のパスで E2E 動作し、新構造で関連テストが緑になる
  - _Requirements: 13.5, 13.6_
  - _Boundary: com.hidano.facialcontrol.inputsystem InputSystemAdapterBinding_
  - _Depends: 2.1_

- [x] 2.3 (7c) Cross-binding Slug Convention 実装 + 既存 Gaze テスト群一斉改修
  - `GazeBindingConfig` の解決ロジックに Cross-binding Slug Convention（`useDistinctLeftRight=false` 時の `{any}:{expressionId}.left` / `.right` / `{expressionId}` 自動マッチ、`useDistinctLeftRight=true` 時の完全 slug 文字列 lookup）を実装する
  - 複数 binding が同一 `expressionId` を提供する場合、`binding.Slug` の Ordinal lexicographic 昇順で deterministic に先頭採用し、その旨を警告ログ出力する（D8 / CI2）
  - `expressionId` 内に予約接尾辞 `.left` / `.right` を含む値はパース時に警告 + 拒否する
  - 既存 Gaze 関連テスト（`FacialCharacterProfileSO_GazeConfigsRoundTripTests`、`FacialCharacterProfileExporter_GazeConfigsTests`、`FacialCharacterProfileSOInspectorGazeConfigsTests` 等）を新構造に合わせて一斉改修し、すべて緑にする
  - 観察完了状態: 既存 Gaze 関連テスト群がすべて緑、deterministic 採用と警告ログが EditMode テストで再現される
  - _Requirements: 13.2, 13.3, 13.7_
  - _Boundary: Adapters.ScriptableObject.GazeBindingConfig 解決経路, 既存 Gaze テスト群_
  - _Depends: 2.1, 2.2_

### Phase 2: OscReceiverAdapterBinding（受信）破壊的拡張 — mode 別 entry / bundle アトミック / フェイルセーフ / ゾンビ排除 / heartbeat 整合性

- [x] 3. 受信側 `OscReceiverAdapterBinding` を mode 別 `OscMappingEntry` 統合 + bundle アトミック + フェイルセーフ + ゾンビ排除 + heartbeat 整合性検査の構造に破壊的拡張する

- [x] 3.1 受信側 mapping 型と enum の Serializable 定義
  - `OscMappingMode` enum（`Normal_BlendShape` / `Gaze_VRChat_XY` / `Gaze_ARKit_8BS`）を新設する
  - `OscMappingEntry` を `[Serializable]` クラスとして定義し、`mode` / `expressionId` / `addressPattern` / `sourceIdLeft` / `sourceIdRight` / `leftRightIndependent` を保持する
  - `FailSafeMode` enum（`RevertToBase` / `HoldLastValue`） / `BundleInterpretationMode` enum（`AtomicSwap` / `IndividualMessage`） / `AddressPresetKind` enum（`VRChat` / `ARKit`）を新設する
  - 観察完了状態: Inspector で `OscReceiverAdapterBinding` を Add した際に mode / フェイルセーフ / bundle 解釈モードの enum が表示可能な状態になる
  - _Requirements: 11.1, 12.1, 12.2_
  - _Boundary: Adapters.OSC enums/state types_

- [x] 3.2 `PerfectSyncEyeLook` 双方向変換 helper（Compose / Decompose）と符号定義テスト
  - 8 名（`eyeLookInLeft` / `eyeLookOutLeft` / `eyeLookUpLeft` / `eyeLookDownLeft` / `eyeLookInRight` / `eyeLookOutRight` / `eyeLookUpRight` / `eyeLookDownRight`）を ASCII 固定で静的配列に保持し、UTF-8 byte 化済みアドレスバッファを事前計算する
  - `Decompose`: 受信 8 float の `ReadOnlySpan<float>` から左目 / 右目の Vector2（`x_L = eyeLookOutLeft - eyeLookInLeft`, `y_L = eyeLookUpLeft - eyeLookDownLeft`, `x_R = eyeLookOutRight - eyeLookInRight`, `y_R = eyeLookUpRight - eyeLookDownRight`）を out 引数で返す
  - `Compose`: 左目 / 右目 Vector2 を 8 float の `Span<float>` に書き出す（GC ゼロ）
  - 左右非対称入力（寄り目、片目流し目）を含む代表ケースで Compose → Decompose が誤差 < 1e-6 で復元することを EditMode テストで Red → Green に到達させる
  - 観察完了状態: `PerfectSyncEyeLookTests` が緑
  - _Requirements: 4.6, 4.7, 12.3, 8.3_
  - _Boundary: Adapters.OSC.PerfectSyncEyeLook_

- [x] 3.3 (P) `OscBundleAccumulator`（timestamp キー集積 + Swap タイミング決定）
  - 同一 `Message.timestamp` を蓄積し、異なる timestamp 到着 or `bundleAccumulationTimeoutMs`（既定 5 ms）超過で `OscDoubleBuffer.Swap` を呼ぶ集積器を新設する
  - bundle 外の bare メッセージは受信時刻順にバッファへ書き込み、次の `OnFixedTick.Swap` で公開するパスを併設する
  - 受信スレッドで蓄積し、メインスレッド `OnFixedTick` 内で Swap を確定するスレッドモデルを実装する
  - 観察完了状態: 同 timestamp の複数メッセージが 1 つの Swap にまとまり、異なる timestamp が新しい Swap を発火することを EditMode テストで再現できる
  - _Requirements: 3.4, 11.7_
  - _Boundary: Adapters.OSC.OscBundleAccumulator_

- [x] 3.4 (P) `ZombieEvictionPolicy`（最新起動時刻 UUID 採用 + 切替時 Info ログ）
  - 観測された `SenderIdentity`（Guid + UTC ms）群を内部 dictionary に蓄積し、最も新しい `StartedAtUnixMs` の UUID のみを採用する
  - 採用送信元の切替が発生した時に切替前後の UUID / 起動時刻を Unity 標準 Info ログに出力する
  - 内部 dictionary に上限 16 件を保持し、FIFO eviction で古いエントリを破棄する
  - 観察完了状態: 複数 UUID 観測列を流す EditMode テスト（`ZombieEvictionPolicyTests`）が緑になり、最新採用と切替ログが検証される
  - _Requirements: 11.5, 11.6_
  - _Boundary: Adapters.OSC.ZombieEvictionPolicy_

- [x] 3.5 (P) `HeartbeatConsistencyChecker`（名前一覧差分検出 + 部分反映マスク + 重複ログ抑制）
  - heartbeat 受信時に送信側 BlendShape 名一覧と自身の mapping を比較し、不一致 BlendShape のみ反映を停止する `_skipMask : BitArray` を更新する
  - 不一致内容（送信側にあるが受信側に無い名前 / 受信側にあるが送信側に無い名前）を Unity 標準 Warning ログに出力する
  - 同一不一致セットに対するログ抑制を hash で重複判定し、同じ不一致では 1 回のみログを出す
  - 観察完了状態: `HeartbeatConsistencyCheckerTests` が緑になり、部分反映マスクと重複ログ抑制が検証される
  - _Requirements: 11.8, 11.9, 11.10, 11.11_
  - _Boundary: Adapters.OSC.HeartbeatConsistencyChecker_

- [x] 3.6 `GazeVector2InputSource`（Vector2 を IAnalogInputSource として登録 + GC ゼロ publish）
  - `IAnalogInputSource` を実装し、Vector2 値を読み取りホットパスで GC アロケーションを発生させずに publish する
  - `OscReceiverAdapterBinding` 内部で Gaze 系 entry から構築され、`IInputSourceRegistry` に slug 登録される構造とする
  - フェイルセーフ時に `(0, 0)` へ publish 切替できるエントリポイントを持つ
  - 観察完了状態: Vector2 入力源として `IInputSourceRegistry` に登録し、`GazeBonePoseProvider` から lookup できる状態を EditMode テストで確認できる
  - _Requirements: 12.5, 12.8, 12.9_
  - _Boundary: Adapters.InputSources.GazeVector2InputSource_

- [x] 3.7 `OscReceiverAdapterBinding` 本体の破壊的拡張（mode 別 entry 統合 + lifecycle + Gaze 受信 + bundle アトミック + ゾンビ排除 + heartbeat 連携）
  - 既存 SerializeField（endpoint / port / stalenessSeconds）を維持しつつ、`_mappings : List<OscMappingEntry>` / `_failSafeMode` / `_consistencyCheckWarnLog` / `_bundleMode` / `_bundleAccumulationTimeoutMs` を追加する
  - `OnStart` で `_mappings` を mode 別に分類し、`Normal_BlendShape` は OscInputSource / OscDoubleBuffer 経路へ、`Gaze_VRChat_XY` / `Gaze_ARKit_8BS` は `GazeVector2InputSource` 経路へ振り分ける
  - `Gaze_VRChat_XY` の `{addressPattern}X` / `{addressPattern}Y` を 1 つの `GazeVector2InputSource` に集約、`Gaze_ARKit_8BS` は `addressPattern` 無視で `/ARKit/eyeLookXxx` 固定 8 アドレスを listen し `PerfectSyncEyeLook.Decompose` で左右別 Vector2 を構築する
  - Registry register ロジック（D4 / D6 / D7）: `Normal_BlendShape` は既存経路、`Gaze_VRChat_XY` は `leftRightIndependent = false` で `(Slug, expressionId)` 単一キー、`true` で `.left` / `.right` 2 キー、`Gaze_ARKit_8BS` は `leftRightIndependent` の値に関わらず常に `.left` / `.right` 2 キーで publish する
  - `leftRightIndependent = true` の Gaze entry で `sourceIdLeft` / `sourceIdRight` のいずれかが空文字 / null なら entry 全体スキップ + 警告ログ（D4 / CI1）
  - `/_facialcontrol/sender_id` / `/_facialcontrol/blendshape_names` の analog/string listener を登録し、`OscBundleAccumulator` / `ZombieEvictionPolicy` / `HeartbeatConsistencyChecker` を orchestration する
  - `TryWriteValues` が staleness 超過で false を返した時、`FailSafeMode = RevertToBase` なら全要素 0.0 を publish、`HoldLastValue` ならスナップショット保持。新規パケット受信でフェイルセーフ解除。Gaze 系 entry も同タイミングで `(0, 0)` 切替する
  - 観察完了状態: `OscReceiverAdapterBinding` 単体で BlendShape / Gaze_VRChat_XY / Gaze_ARKit_8BS の 3 mode を受信し、フェイルセーフ / ゾンビ排除 / heartbeat 整合性検査が EditMode/PlayMode のスモークテストで成立する
  - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7, 11.8, 11.9, 11.10, 11.11, 12.1, 12.2, 12.3, 12.4, 12.5, 12.6, 12.7, 12.8, 12.9_
  - _Boundary: Adapters.AdapterBindings.OscAdapterBinding_
  - _Depends: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 2.3_

### Phase 3: OscSenderAdapterBinding 骨格と単一 endpoint 送信

- [x] 4. `OscSenderAdapterBinding` の AdapterBinding lifecycle 結線骨格を構築し、単一 endpoint への BlendShape 送信を成立させる

- [x] 4.1 (P) `OscSenderEndpointConfig`（[Serializable] endpoint 設定）と `OscAddressFormatter`（zero-alloc アドレス組立）
  - `OscSenderEndpointConfig` を `[Serializable]` クラスとして定義し、IP/host 文字列 / UDP port / 有効/無効 flag / `AddressPresetKind` を保持する
  - `OscAddressFormatter` を新設し、プリセットと BlendShape 名 / `expressionId` から完全 OSC アドレス文字列を組み立てる API を提供する（Phase 5 でプリセット切替対応を完成、ここでは VRChat 形式の base 実装のみ）
  - BlendShape 名にマルチバイト文字 / 記号が含まれてもアドレス文字列を正しく構築できることを EditMode テストで確認する
  - 観察完了状態: 単一 endpoint + VRChat 形式アドレスでメッセージを組み立てられる
  - _Requirements: 3.1, 3.2, 4.1, 4.8_
  - _Boundary: Adapters.OSC.OscSenderEndpointConfig, Adapters.OSC.OscAddressFormatter_

- [x] 4.2 (P) `SenderIdentity` / `SenderIdentityGenerator`（UUID + UTC ms）
  - `SenderIdentity` を `readonly struct`（`Guid Uuid` + `long StartedAtUnixMs`）として定義する
  - `SenderIdentityGenerator.Generate()` で `Guid.NewGuid()` + `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` を返す
  - OSC 表現として `/_facialcontrol/sender_id` に 2 引数（`Guid.ToByteArray()` の 16 byte blob, `long` の UTC ms）を送る規約を定数化する
  - 観察完了状態: プロセス起動ごとに UUID + UTC ms が一意になり、bundle に同梱される文字列形式が `OscReceiverAdapterBinding` 側のパーサと一致する
  - _Requirements: 14.1, 14.2_
  - _Boundary: Adapters.OSC.SenderIdentity, Adapters.OSC.SenderIdentityGenerator_

- [x] 4.3 `OscSenderAdapterBinding` 骨格（`[FacialAdapterBinding(displayName: "OSC Sender")]` + lifecycle + FacialOutputBus 購読 + 単一 endpoint 送信）
  - `[Serializable]` + `[FacialAdapterBinding(displayName: "OSC Sender")]` 属性付き `AdapterBindingBase` sealed 具象として実装し、パラメータレスコンストラクタで Inspector の Add ドロップダウンから生成可能にする
  - `OnStart`: `ctx.HostGameObject` に `OscSender` を `AddComponent` + Configure、`ctx.FacialOutputBus.Subscribe(this)` で post-blend 観察開始、`SenderIdentityGenerator.Generate()` で Identity 発行
  - `OnFacialOutputPublished`: `postBlendValues` / `gazeSnapshots` を scratch buffer にコピー（zero-alloc 設計、Phase 10 で uOSC fork 化により完全達成）
  - `OnLateTick`: 単一 endpoint へ post-blend BlendShape 値 + `/_facialcontrol/sender_id` を 1 つの bundle として送信する（multi-endpoint / Gaze / heartbeat / プリセット切替は Phase 4-5 で完成）
  - `Dispose`: FacialOutputBus 購読解除、`OscSender` を `Object.Destroy`、Slug 規約 (`AdapterBindingBase.Slug`) チェックと違反時警告ログ
  - 起動失敗ケース（HostGameObject null / mapping 全空）の no-op + 警告ログ
  - 観察完了状態: Inspector の Add ドロップダウンから `OSC Sender` を選択 → 1 endpoint を設定 → Play すると同一プロセス内の `OscReceiverAdapterBinding`（拡張版）が BlendShape 値を受信できる
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8_
  - _Boundary: Adapters.AdapterBindings.OscSenderAdapterBinding_
  - _Depends: 1.4, 3.7, 4.1, 4.2_

### Phase 4: multi-endpoint + OSC bundle 送信 + heartbeat

- [x] 5. multi-endpoint 同報送信、OSC bundle フレームアトミック送信、heartbeat（BlendShape 名一覧）送信を `OscSenderAdapterBinding` に追加する

- [x] 5.1 (P) `OscBundleBuilder`（zero-alloc bundle 構築 + MTU 超過分割）
  - timestamp + 事前 UTF-8 化済みアドレス byte[] + float 値の列から bundle byte 列を `Span<byte>`（ArrayPool 貸出）に書き出す再利用可能 builder を新設する
  - heartbeat 用 string 列を含む bundle 構築 API を提供する
  - UDP MTU（1472 byte）超過時は複数 bundle に同 timestamp で分割し、警告ログを出力する
  - 観察完了状態: 1000 BlendShape などの大規模 mapping で分割が動作し、出力 byte 列が OSC 1.0 仕様準拠のため受信側 `OscReceiverAdapterBinding` でパース可能
  - _Requirements: 3.4, 3.5_
  - _Boundary: Adapters.OSC.OscBundleBuilder（uOSC fork 配下相当、preview.2 では既存 uOSC 上に実装）_

- [x] 5.2 multi-endpoint 同報送信と loopback 抑制以外の endpoint 正規化
  - `_endpoints : List<OscSenderEndpointConfig>` を保持し、`OnStart` で endpoint ごとに独立した送信スロット（`OscSender` インスタンスまたは uOSC client）を確保する
  - 同一 (IP, port) 重複を 1 件に正規化し、初回検出時に 1 回の警告ログを出す
  - `enabled = false` の endpoint は送信 skip、`enabled = true` のみ送信。endpoint 件数 0 件は `OnStart` no-op + 警告ログ
  - `OnLateTick` で各 endpoint に対し同一フレームの bundle を unicast 送信する
  - 1 endpoint の起動失敗が他 endpoint の送信を阻害しない
  - 観察完了状態: 1 sender → 2 receiver への同報送信が PlayMode E2E（Phase 9）で受信可能になる
  - _Requirements: 3.1, 3.2, 3.3, 3.6, 3.7, 3.8, 3.9, 3.10_
  - _Boundary: Adapters.AdapterBindings.OscSenderAdapterBinding multi-endpoint state_
  - _Depends: 4.3, 5.1_

- [x] 5.3 OSC bundle フレームアトミック化（1 フレームの全メッセージを単一 bundle に集約）
  - 1 フレーム分の全 OSC メッセージ（sender_id ヘッダ + N float BlendShape + M Gaze メッセージ + 必要なら heartbeat）を単一の OSC bundle にまとめる
  - bundle 単位で UDP 送信し、受信側 `OscBundleAccumulator` の timestamp キー集積と整合する
  - 観察完了状態: 受信側 `OscReceiverAdapterBinding` で 1 フレーム分の全メッセージが同じ Swap 周期に反映される（Phase 9 PlayMode で bundle atomicity test 緑）
  - _Requirements: 3.4, 3.5_
  - _Boundary: Adapters.AdapterBindings.OscSenderAdapterBinding bundle path_
  - _Depends: 5.1, 5.2_

- [x] 5.4 heartbeat（BlendShape 名一覧）周期送信と sender_id 同梱
  - heartbeat 周期を `[Serializable]` フィールド `heartbeatIntervalSeconds`（既定 5 秒）として保持し、Drawer から 0.5〜60 秒範囲で設定可能にする
  - 0.5 秒未満は警告ログ + 最小値（0.5 秒）にクランプ
  - 起動時 1 回 + 周期で `/_facialcontrol/blendshape_names`（string 配列メッセージ）を当該 binding の送信対象 BlendShape 名リスト（順序保持）として送出する
  - heartbeat は通常の毎フレーム bundle と同じ送信先 endpoint 群へ送られる
  - ループバック抑制が有効で送信先が全 endpoint 抑制対象の場合は heartbeat も送信しない（Phase 6 と整合）
  - sender_id（`/_facialcontrol/sender_id`）を毎フレーム bundle 先頭に同梱する
  - heartbeat 名前リストは `OnStart` 時に確保し再利用、`OnLateTick` ホットパス GC ゼロを維持する（preview.2 では uOSC 側 GC は許容、本 spec ロジック側は GC ゼロ）
  - 観察完了状態: 受信側 `HeartbeatConsistencyChecker` が起動後 5 秒以内に名前一覧を受け取り、整合性検査がトリガされることを PlayMode E2E（Phase 9）で確認できる
  - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5, 14.6, 14.7, 14.8_
  - _Boundary: Adapters.AdapterBindings.OscSenderAdapterBinding heartbeat path_
  - _Depends: 5.3, 4.2_

### Phase 5: アドレスプリセット（VRChat / ARKit）+ Gaze 送信（PerfectSync 互換）

- [x] 6. endpoint ごとのアドレスプリセット切替（VRChat / ARKit）と Gaze Vector2 送信（VRChat 形式 X/Y + ARKit PerfectSync 互換 8 BlendShape）を実装する

- [x] 6.1 `OscAddressFormatter` プリセット切替（VRChat / ARKit）+ 事前 UTF-8 byte 化キャッシュ
  - endpoint のプリセットが `AddressPresetKind.VRChat` の時 BlendShape を `/avatar/parameters/{BlendShape 名}` 形式で構築する
  - endpoint のプリセットが `AddressPresetKind.ARKit` の時 BlendShape を `/ARKit/{BlendShape 名}` 形式で構築する
  - プリセット切替時に BlendShape / Gaze 両系統でアドレス文字列を再構築し送信テーブルに反映する
  - 完全 OSC アドレスは保存せず、BlendShape 名 / `expressionId` のみを保存して送信時組立とする（案 B）
  - `_addressBytesPool : Dictionary<(string name, AddressPresetKind), byte[]>` で `OnStart` 時に UTF-8 byte 化を事前計算する
  - 将来プリセット追加時は enum 値の追加 + 分岐 1 か所の修正で済む構造を維持する
  - 観察完了状態: 1 binding に VRChat endpoint + ARKit endpoint を混在させると、それぞれ正しいアドレス形式で送信される
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.8, 4.9, 4.10_
  - _Boundary: Adapters.OSC.OscAddressFormatter preset switching_
  - _Depends: 4.1_

- [x] 6.2 Gaze 送信パス（VRChat 形式 X/Y 2 メッセージ）
  - VRChat 形式 endpoint で Gaze 送信が有効な時、Gaze Vector2 を `/avatar/parameters/{expressionId}X` および `/avatar/parameters/{expressionId}Y` の 2 メッセージとして送信する
  - 送信対象 `gazeExpressionIds : List<string>` を `[Serializable]` フィールドとして保持する
  - `OnFacialOutputPublished` から受け取った Gaze スナップショットを scratch に格納し、`OnLateTick` で bundle に同梱する
  - 観察完了状態: VRChat 形式 endpoint に対して `{expressionId}X` / `{expressionId}Y` の 2 メッセージが 1 bundle 内で送信され、受信側 `Gaze_VRChat_XY` mode entry で Vector2 として再構築される
  - _Requirements: 4.5_
  - _Boundary: Adapters.AdapterBindings.OscSenderAdapterBinding gaze sender_
  - _Depends: 5.3, 6.1_

- [x] 6.3 Gaze 送信パス（ARKit PerfectSync 互換 8 BlendShape 分解）
  - ARKit 形式 endpoint で Gaze 送信が有効な時、Gaze Vector2 を PerfectSync 互換 8 BlendShape（`eyeLookInLeft` / `eyeLookOutLeft` / `eyeLookUpLeft` / `eyeLookDownLeft` / `eyeLookInRight` / `eyeLookOutRight` / `eyeLookUpRight` / `eyeLookDownRight`）に `PerfectSyncEyeLook.Compose` で分解し、各 BlendShape を `/ARKit/{eyeLook 名}` 形式で送信する
  - 角度制限（`outerYawAngle` / `innerYawAngle` / `lookUpAngle` / `lookDownAngle`）を送信側では適用せず、正規化済み Vector2 をそのまま分解する（受信側責務）
  - PerfectSync 互換の eyeLookXxx 名は ARKit 規格の固定 ASCII 名を使用する
  - 観察完了状態: ARKit 形式 endpoint に対して 8 BlendShape が 1 bundle 内で送信され、受信側 `Gaze_ARKit_8BS` mode entry で `PerfectSyncEyeLook.Decompose` により左右別 Vector2 として再構築される
  - _Requirements: 4.6, 4.7_
  - _Boundary: Adapters.AdapterBindings.OscSenderAdapterBinding gaze sender ARKit path_
  - _Depends: 3.2, 5.3, 6.1_

### Phase 6: ループバック抑制ポリシー

- [x] 7. 同一プロセス内 OSC 受信 binding と送信 binding の loopback 抑制ポリシーを実装する
  - `LoopbackSuppressionPolicy` を新設し、同一 child scope の `OscReceiverAdapterBinding` を `OnStart` 時に列挙、`(Endpoint, Port)` を抑制集合に追加する
  - `_suppressLoopback : bool`（既定 ON）を `OscSenderAdapterBinding` の `[Serializable]` フィールドとして追加
  - 抑制 ON 時に各送信 endpoint が受信 binding の listen endpoint (IP+port) と一致するかを `OnStart` および構成変更時のみ検査し、`OnLateTick` ホットパスに比較処理を入れない
  - 一致した endpoint への送信を抑制 + 警告ログ出力。全 endpoint が抑制対象になった場合は送信せず警告ログを出すが binding 自体は live 維持
  - 明示的に OFF に切り替えるオプションを Drawer UI とフィールドで提供し、E2E テストや意図的なローカル配信検証で抑制解除可能にする
  - heartbeat も同抑制に従う（全 endpoint 抑制対象なら heartbeat 送信もしない、Phase 4 と整合）
  - 観察完了状態: 同一プロセス内に sender + receiver 同居 + 抑制 ON で送信されない、OFF で送信される、を PlayMode E2E（Phase 9）で再現できる
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7_
  - _Boundary: Adapters.OSC.LoopbackSuppressionPolicy, Adapters.AdapterBindings.OscSenderAdapterBinding suppress field_

### Phase 8: JSON DTO + Editor Drawer + サンプル

- [x] 8. JSON 永続化 DTO 群、UI Toolkit ベース PropertyDrawer 2 種類、`OscOutputDemo` / `OscReceiverDemo` サンプル一式を整備する

- [x] 8.1 (P) `OscSenderOptionsDto` / `OscSenderEndpointDto`（送信側 JSON DTO）
  - `OscSenderOptionsDto` を `[Serializable]` 型として定義し、endpoint 配列 / BlendShape mapping / Gaze expressionId / `suppressLoopback` / `heartbeatIntervalSeconds` を保持する
  - `OscSenderEndpointDto` を `[Serializable]` 型として定義し、ip / port / preset（"vrchat" / "arkit"）/ enabled を保持する
  - `SystemTextJsonParser`（`JsonUtility` ベース、新規 JSON ライブラリ持ち込み禁止）で JSON 往復、未知キー無視、必須欠落時の警告 + 既定値補完を実装する
  - human-readable / diff フレンドリーな JSON 出力（インデント / 安定キー順）にする
  - 観察完了状態: `OscSenderOptionsDtoTests` が JSON 往復 / 未知キー / 必須欠落の 3 ケースで緑になる
  - _Requirements: 6.1, 6.4, 6.5, 6.6, 6.7, 6.9, 6.10_
  - _Boundary: Adapters.Json.Dto.OscSenderOptionsDto, Adapters.Json.Dto.OscSenderEndpointDto_

- [x] 8.2 (P) `OscReceiverOptionsDto` / `OscMappingEntryDto`（受信側 JSON DTO + mode 別 entry）
  - `OscReceiverOptionsDto` を `[Serializable]` 型として定義し、listenEndpoint / listenPort / mappings（`OscMappingEntryDto[]`）/ stalenessSeconds / failSafeMode / consistencyCheckWarnLog / bundleMode / bundleAccumulationTimeoutMs を保持する
  - `OscMappingEntryDto` を `[Serializable]` 型として定義し、mode（"blendShape" / "gazeVrchatXy" / "gazeArkit8Bs"）/ expressionId / addressPattern / sourceIdLeft / sourceIdRight / leftRightIndependent を保持する
  - mode 別意味論を実装: `Normal_BlendShape` は完全 OSC アドレス、`Gaze_VRChat_XY` は base アドレス（末尾 X/Y 抜き）、`Gaze_ARKit_8BS` は addressPattern 無視（Info ログ「ignored」+ 既定値扱い）
  - `leftRightIndependent = true` の Gaze entry で `sourceIdLeft` / `sourceIdRight` のいずれかが空ならパース時に entry 全体スキップ + 警告ログ（D4 / CI1）
  - 観察完了状態: `OscReceiverOptionsDtoTests` / `OscMappingEntryDtoTests` が 3 mode 分の JSON 往復、Gaze 片方欠落スキップ、ARKit `addressPattern` ignored の各ケースで緑になる
  - _Requirements: 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 6.9, 6.10_
  - _Boundary: Adapters.Json.Dto.OscReceiverOptionsDto, Adapters.Json.Dto.OscMappingEntryDto_

- [x] 8.3 JSON スキーマドキュメント（`Documentation~/osc-sender-options.md` / `osc-receiver-options.md`）
  - 送信側スキーマドキュメントに endpoint 一覧 / プリセット enum / heartbeat 周期 / ループバック抑制 / Gaze 送信構成のフィールド一覧、型、既定値、サンプル JSON を Markdown で記載する
  - 受信側スキーマドキュメントに mode 別 entry 構造（`Normal_BlendShape` / `Gaze_VRChat_XY` / `Gaze_ARKit_8BS`）、フェイルセーフモード、整合性検査ポリシー、bundle 解釈モードのフィールド一覧、型、既定値、サンプル JSON を Markdown で記載する（3 mode 分のサンプル）
  - ARKit mode の `addressPattern` 省略形を例示し、`leftRightIndependent = false` 既定の Gaze 例も含める
  - 観察完了状態: `com.hidano.facialcontrol.osc/Documentation~/` 配下に 2 つの Markdown が配置され、サンプル JSON がパース可能
  - _Requirements: 6.8_
  - _Boundary: com.hidano.facialcontrol.osc Documentation~_
  - _Depends: 8.1, 8.2_

- [x] 8.4 (P) `OscSenderAdapterBindingDrawer`（UI Toolkit）
  - UI Toolkit ベース PropertyDrawer として実装（IMGUI 新規利用禁止）し、Editor 専用 asmdef 配下に配置する
  - endpoint 一覧を ReorderableList 相当の UI で表示し、追加 / 削除 / 並べ替え、各 endpoint の IP/host・ポート・プリセット（VRChat / ARKit）・有効/無効を編集する
  - ループバック抑制ポリシー（既定: 有効）、BlendShape mapping 名一覧、Gaze 送信対象 `expressionId` 一覧の編集 UI を提供する
  - heartbeat 周期（秒、既定: 5）、送信元識別子の読み取り専用表示（起動時 UUID / 起動時刻）を提供する
  - IP 空文字 / port 範囲外 / heartbeat 範囲外をインライン警告 HelpBox 表示し、保存時にスキップ対象とマークする
  - 観察完了状態: Inspector で `OscSenderAdapterBinding` を Add すると Drawer が表示され、endpoint の追加/削除/並べ替えと各種設定編集が可能
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.7, 7.8_
  - _Boundary: Editor.AdapterBindings.OscSenderAdapterBindingDrawer_

- [x] 8.5 (P) `OscReceiverAdapterBindingDrawer`（UI Toolkit、mode 別 fold-out で BlendShape / Gaze 統合表示）
  - UI Toolkit ベース PropertyDrawer として実装（IMGUI 新規利用禁止）し、Editor 専用 asmdef 配下に配置する
  - listen endpoint 編集、`OscMappingEntry[]` の ReorderableList 編集、staleness 秒数編集、`FailSafeMode` 切替（既定: ベース表情復帰）、整合性検査の警告ログ ON/OFF、bundle 解釈モード、bundleAccumulationTimeoutMs を提供する
  - 各 entry の mode 選択（`Normal_BlendShape` / `Gaze_VRChat_XY` / `Gaze_ARKit_8BS`）に応じた fold-out UI:
    - `Normal_BlendShape`: `expressionId` + `addressPattern` のみ
    - `Gaze_VRChat_XY`: `expressionId` + `addressPattern`（X/Y 末尾抜きの base）+ `leftRightIndependent` トグル。ON 時のみ `sourceIdLeft` / `sourceIdRight` を表示
    - `Gaze_ARKit_8BS`: `expressionId` + `leftRightIndependent` トグル。ON 時のみ `sourceIdLeft` / `sourceIdRight` 表示。`addressPattern` 非表示（D5 / CI2）
  - `leftRightIndependent = true` の Gaze entry で `sourceIdLeft` / `sourceIdRight` のいずれかが空な場合はインライン警告 HelpBox 表示 + 保存時スキップ対象マーク（D4 / CI1）
  - 観察完了状態: Inspector で `OscReceiverAdapterBinding` を Add すると Drawer が表示され、3 mode の fold-out UI が mode セレクタで切り替わる
  - _Requirements: 7.1, 7.5, 7.6, 7.7, 7.8_
  - _Boundary: Editor.AdapterBindings.OscReceiverAdapterBindingDrawer_

- [x] 8.6 (P) `Samples~/OscOutputDemo`（送信側サンプル、Samples~ + Assets/Samples 二重管理）
  - `com.hidano.facialcontrol.osc/Samples~/OscOutputDemo` に Scene / FacialProfileSO / `OscSenderOptions` JSON / README をまとめて配置する
  - BlendShape 送信、Gaze Vector2 送信（VRChat 形式と ARKit 形式の両方）、複数 endpoint 同報、ループバック抑制設定例を含める
  - `FacialControl/Assets/Samples/` 配下に同名サンプルを二重管理として配置し、dev project で Scene 結線して動作確認できる状態にする
  - 観察完了状態: Package Manager から `OscOutputDemo` を Import 可能、dev project で同サンプルを Play すると BlendShape + Gaze が送信される
  - _Requirements: 9.1, 9.2, 9.4_
  - _Boundary: com.hidano.facialcontrol.osc Samples~/OscOutputDemo, Assets/Samples mirror_

- [x] 8.7 (P) `Samples~/OscReceiverDemo`（受信専用サンプル、Samples~ + Assets/Samples 二重管理）
  - `com.hidano.facialcontrol.osc/Samples~/OscReceiverDemo` に Scene / 受信専用最小 `FacialProfileSO`（OSC 入力源を素通しする 1 Layer のみ、Expression 定義不要）/ `OscReceiverOptions` JSON / Gaze 受信 binding 設定 / README をまとめて配置する
  - `FacialControl/Assets/Samples/` 配下に同名サンプルを二重管理として配置する
  - 観察完了状態: Package Manager から `OscReceiverDemo` を Import 可能、dev project で同サンプルを Play して送信側からの OSC を受信し描画できる
  - _Requirements: 9.3, 9.4_
  - _Boundary: com.hidano.facialcontrol.osc Samples~/OscReceiverDemo, Assets/Samples mirror_

- [x] 8.8 `package.json` samples 配列登録 + CHANGELOG / README / work-procedure / backlog 更新
  - `com.hidano.facialcontrol.osc/package.json` の `samples` 配列に `OscOutputDemo` / `OscReceiverDemo` を登録する
  - `com.hidano.facialcontrol.osc/CHANGELOG.md` および `README.md` に OSC 送信 / 受信拡張 / Gaze Vector2 受信機能の追加と既存 `OscReceiverAdapterBinding` への破壊的変更を記載する
  - `docs/work-procedure.md` に本 spec の作業手順 + Samples~ と Assets/Samples 同期コピー手順を追記し、`docs/backlog.md` から OSC 送信関連の積み残しを引き上げて整理する
  - 観察完了状態: Package Manager の Import Sample に 2 サンプルが表示され、CHANGELOG / README / docs が最新状態
  - _Requirements: 9.5, 9.6, 9.7, 9.8_
  - _Boundary: com.hidano.facialcontrol.osc package metadata, repository docs_
  - _Depends: 8.6, 8.7_

### Phase 9: E2E + 性能テスト（PlayMode）

> 本 phase は preview.2 milestone の検証層。Phase 1〜8 が緑になった時点で実行する。GC スパイク完全達成は preview.3 (Phase 10) で行うため、ここでは「機能正しさ」と「処理遅延が許容範囲内」を確認し、GC 計測は計測のみ（baseline 取得）に留める。

- [x] 9. PlayMode E2E と性能テスト（GC 計測）を実装する

- [x] 9.1 (P) `OscSendReceiveE2ETests`（BlendShape 経路 E2E）
  - `FacialController` → `FacialOutputBus` → `OscSenderAdapterBinding` → 実 UDP → `OscReceiverAdapterBinding`（拡張版）→ `LayerUseCase` への post-blend 到達を検証する
  - テスト命名 `{Target}Tests` / `{Method}_{Condition}_{Expected}` 規約に従う
  - 観察完了状態: PlayMode テストランナーで全ケース緑
  - _Requirements: 8.4, 8.13_
  - _Boundary: Tests/PlayMode/Integration_

- [x] 9.2 (P) `OscGazeE2ETests`（Gaze VRChat / ARKit 双方の経路 + ARKit 左右非対称情報損失なし + deterministic 採用）
  - 左右非対称 Gaze Vector2（寄り目、片目流し目）が VRChat 形式（X/Y 2 メッセージ）/ ARKit 形式（PerfectSync 互換 8 BlendShape）双方の経路で受信側に到達し、誤差範囲で再現されることを検証する
  - ARKit mode かつ `leftRightIndependent = false` 時にも左右非対称 Gaze が情報損失なく `useDistinctLeftRight = false` の `GazeBindingConfig` 経路で両目別駆動として再現されるケースを 1 件追加する（D7 / CI1）
  - OSC 受信 binding と InputSystem 受信 binding が同一 `expressionId` を提供した時に `binding.Slug` の Ordinal lexicographic 昇順で deterministic に先頭採用 + 警告ログが出るケースを E2E で確認する（D8 / CI2）
  - 観察完了状態: PlayMode テストランナーで全ケース緑
  - _Requirements: 8.5, 8.13_
  - _Boundary: Tests/PlayMode/Integration_

- [x] 9.3 (P) `OscMultiEndpointTests` + `OscBundleAtomicityTests`
  - 1 sender → 2 receiver への unicast 同報送信が BlendShape と Gaze の両方について両系統で受信されることを検証する
  - OSC bundle のフレームアトミック性: 1 フレーム分の全メッセージが受信側で同一フレーム（同一 Swap）に反映されることを検証する
  - 観察完了状態: PlayMode テストランナーで両ケース緑
  - _Requirements: 8.6, 8.7, 8.13_
  - _Boundary: Tests/PlayMode/Integration_

- [x] 9.4 (P) `OscLoopbackSuppressionTests` + `OscFailSafeRevertTests`
  - 同一プロセス内のループバック抑制ポリシー（既定 ON 時は送信されず、明示 OFF 時は送信される）を検証する
  - staleness 超過時のフェイルセーフ復帰: 送信側を停止して staleness 秒数経過後に受信側がベース表情へ復帰すること、送信再開後に表情が復元することを検証する
  - 観察完了状態: PlayMode テストランナーで両ケース緑
  - _Requirements: 8.8, 8.9, 8.13_
  - _Boundary: Tests/PlayMode/Integration_

- [x] 9.5 (P) `OscZombieEvictionTests` + `OscHeartbeatConsistencyTests`
  - ゾンビ送信元排除: 複数の送信元 UUID が同時に観測される場合、最も新しい起動時刻の送信元のみが採用され、古い起動時刻の送信元から到着する値は無視されることを検証する
  - BlendShape 名一覧整合性検査: 送信側 heartbeat 受信後、不一致 BlendShape のみ反映されず警告ログが出ることを検証する
  - 観察完了状態: PlayMode テストランナーで両ケース緑
  - _Requirements: 8.10, 8.11, 8.13_
  - _Boundary: Tests/PlayMode/Integration_

- [x] 9.6* (P) `OscSenderGCAllocationTests` / `OscReceiverGCAllocationTests` / `OscBundleMtuTests`（preview.2 では baseline 計測のみ）
  - `OnLateTick` のホットパスに GC アロケーションが発生しないことを `Profiler.GetTotalAllocatedMemoryLong` 差分で監視する（BlendShape のみ送信ケース、Gaze 同送ケース、bundle 化ケース、heartbeat 含むケース）
  - preview.2 段階では uOSC 側の string / object[] アロケーションは許容するため、本 spec ロジック側（mode 別 entry 分別 / Gaze 逆合成 / bundle accumulator）の GC ゼロのみを確認する
  - 大規模 mapping（1000 BlendShape）で MTU 超過時の分割動作と GC ゼロ維持を計測する
  - 完全達成は preview.3 (Phase 10) で実現するため、preview.2 ではテスト存在 + baseline 数値の記録に留め、`*` 印で deferred test として扱う
  - 観察完了状態: GC baseline 数値が CI ログに記録され、本 spec ロジック側の GC ゼロが確認できる
  - _Requirements: 8.12, 8.13, 10.1_
  - _Boundary: Tests/PlayMode/Performance_

## preview.3 milestone（Deferred）

> 以下 Phase 10 / Phase 11 は preview.3 マイルストーンの対象。preview.2 内ではテスト存在のみで実施しない。本 spec が preview.2 で完了した時点で `docs/backlog.md` 経由で preview.3 spec に引き継ぐ。

### Phase 10: uOSC vendor copy + zero-alloc fork 化

- [ ] 10. uOSC を vendor copy 化し、送受信ホットパスを zero-alloc に改修する（preview.3）
  - `com.hidano.uosc` を `Library/PackageCache` から `Packages/com.hidano.uosc/` へ vendor copy 化し、`manifest.json` の参照を local file path へ変更、`package.json` の version を `1.0.0-fcfork.1` とする
  - `Runtime/Core/Modern/` に新 API（`OscMessage` SoA struct / `OscMessagePool` / `OscWriter`（Span + BinaryPrimitives）/ `OscBundleBuilder` / `OscClient`（ring buffer 送信 worker）/ `OscServer`（Socket.ReceiveFrom 受信 worker）/ `OscPacketParser`（ref struct, Span 経由 parse）/ `OscMessageView`（受信 ref readonly struct）/ `OscAddressHash`（UTF-8 → uint64 ハッシュ））を実装する
  - 既存 `Runtime/Core/{Bundle.cs, Message.cs, Parser.cs, Reader.cs, Writer.cs}` および `Runtime/{uOscClient.cs, uOscServer.cs, ...}` は互換 facade として残す（Phase 11 で撤去）
  - 送受信ホットパスを Span / ArrayPool ベースへ改修し、`OnLateTick` および受信スレッドで毎フレーム 0 byte GC を達成する（Req 10.1 完全達成）
  - `Profiler.GetTotalAllocatedMemoryLong` 差分 = 0 byte を 100 フレーム送信 / 受信で確認する PlayMode 性能テストを緑にする
  - 観察完了状態: `OscSenderGCAllocationTests` / `OscReceiverGCAllocationTests` が 0 byte で緑、`uosc-modification-plan.md` のチェックリストがすべて完了
  - _Requirements: 8.12, 10.1, 10.6_
  - _Boundary: com.hidano.uosc fork（vendor copy 化、新 API 実装）_

### Phase 11: uOSC 互換 facade 撤去

- [ ] 11. uOSC 互換 facade（旧 `uOscClient` / `Bundle` / `Message`）を撤去し、新 `OscClient` / `OscServer` / `OscMessage` SoA に統一する（preview.3）
  - 旧 facade に依存している既存 PlayMode テストを新 API 経路へ段階的に移行する
  - 互換 facade ファイル群を削除し、`com.hidano.uosc` を新 API のみの構造に整理する
  - 観察完了状態: 旧 facade 参照がコードベースから消え、すべての OSC 送受信パスが新 API 経由になる
  - _Requirements: 10.1_
  - _Boundary: com.hidano.uosc facade removal_
  - _Depends: 10_

---

## Requirements Coverage Summary

| 要件群 | カバーする主要タスク |
|--------|------------------|
| Req 1（Domain Facial Output Bus） | 1.1, 1.2, 1.3, 1.4 |
| Req 2（Sender lifecycle） | 4.3 |
| Req 3（multi-endpoint + bundle） | 5.1, 5.2, 5.3 |
| Req 4（アドレスプリセット + Gaze 送信） | 4.1, 6.1, 6.2, 6.3 |
| Req 5（ループバック抑制） | 7 |
| Req 6（JSON DTO + スキーマ） | 8.1, 8.2, 8.3 |
| Req 7（Editor Drawer） | 8.4, 8.5 |
| Req 8（テスト） | 1.2, 2.x, 3.2, 9.1〜9.6 |
| Req 9（サンプル + ドキュメント） | 8.6, 8.7, 8.8 |
| Req 10（性能 + アーキテクチャ制約） | 横断、preview.2 は 8.x / 9.6、preview.3 完全達成は 10, 11 |
| Req 11（受信側破壊拡張） | 3.1〜3.7 |
| Req 12（Gaze Vector2 受信） | 3.2, 3.6, 3.7 |
| Req 13（GazeBindingConfig 改修） | 2.1, 2.2, 2.3 |
| Req 14（送信元識別 + heartbeat） | 4.2, 5.4 |
