# Requirements Document

## Project Description (Input)
FacialControl の OSC 送信機能を AdapterBinding として正式統合する spec。

## 背景
現状 `com.hidano.facialcontrol.osc` には `OscSender` / `OscSender` クラスが実装済みで、uOSC クライアントをラップした実 UDP 送信機構（`SendAll` / `SendSingle`）と PlayMode ループバックテストは存在する。一方、これらは AdapterBinding として結線されておらず、Inspector の Add ドロップダウンから選択できず、FacialController の合成後 BlendShape 値や Gaze 入力 Vector2 を吸い出す経路も無いため、プロダクション統合は未完了の状態にある。

加えて、受信側 (`OscReceiverAdapterBinding`) には通信瞬断時の "値据え置き" 挙動はあるが「ベース表情への自動復帰」機構や、複数送信元 / ゾンビプロセス排除、BlendShape 名一覧の整合性検査、OSC bundle によるフレームアトミック性、Gaze Vector2 受信といった構造化された相互運用機能が無い。本 spec で受信側 binding にもこれらを破壊的変更として導入する（OSC binding はまだプロダクション運用に乗っていないため preview.2 内での breaking change を許容する）。

## ユースケース
- VTuber 配信などで「ひとつの表情システム → 複数のキャラ描画システム」へリアルタイム配信し、描画側を冗長構成にする
- Unity でキャプチャ／編集した表情を UnrealEngine など Unity 外プラットフォームへ転送し、クロスエンジンでの表情同期を実現する
- 送信元アプリの瞬断・落ち時にも受信側がベース表情に自動復帰し、配信全体の破綻を防ぐ
- ゾンビプロセスから古い値が流れ込んでも、現役送信元の値だけを採用して安定描画する

## スコープ（実装すべきもの）
1. **Domain 層**: FacialController の合成後 BlendShape 値、および各 `GazeBindingConfig` に対応する Gaze 入力 Vector2 を観察できる仕組み（`IFacialOutputObserver` / `FacialOutputBus` 的なもの）を Domain 純度を保ったまま新設。GC-free（`ReadOnlySpan<float>` および軽量構造体ベース受け渡し）。通知タイミングは `OnLateTick` の **直前**、同フレーム送信完結を保証。
2. **OscSenderAdapterBinding**（送信側 binding 新規）: `[Serializable]` + `[FacialAdapterBinding(displayName: "OSC Sender")]` 具象。OnStart で `OscSender` を AddComponent + Configure、Domain bus を購読し OnLateTick で BlendShape 配列と Gaze Vector2 群を **OSC bundle として 1 フレームアトミックに**送出。
3. **OscReceiverAdapterBinding 拡張**（受信側 binding 破壊的変更、mode 別 entry 統合）: `OscMappingEntry.bindingMode`（`Normal_BlendShape` / `Gaze_VRChat_XY` / `Gaze_ARKit_8BS`）で BlendShape と Gaze Vector2 受信を 1 binding 内に統合（`InputSystemAdapterBinding` の単一 binding 内 mode 集約パターンを踏襲）。同 binding 内で staleness 超過時の **ベース表情への自動フェイルセーフ復帰**、**送信元 UUID + 起動時刻による正規送信元選別**（ゾンビ排除）、**OSC bundle のフレーム単位アトミック処理**、送信側 heartbeat に基づく **BlendShape 名一覧整合性検査**、ARKit/VRChat 各プリセットでの Gaze Vector2 逆合成と `IAnalogInputSource` publish を実装。なお、IP allowlist 方式は uOSC 既存 API の制約（受信時に送信元 IP が破棄される）と機能要件の縮小判断により採用しない。
4. **Gaze Vector2 受信**（OscReceiverAdapterBinding に統合）: ARKit プリセット時の eyeLookXxx 系 8 BlendShape を **左目用 / 右目用 の 2 つの Vector2** に逆合成し、左右非対称を許容する形で `IAnalogInputSource` として publish。VRChat プリセット時は `{expressionId}X` / `{expressionId}Y` の 2 メッセージを Vector2 として受け取り、`leftRightIndependent=false` 時は左右共通として publish。Gaze 専用の別 binding は作らず、上記 OscReceiverAdapterBinding の `Gaze_VRChat_XY` / `Gaze_ARKit_8BS` mode の entry が同責務を担う。
5. **`GazeBindingConfig` 拡張**（破壊的変更、後方互換なし）: 入力源解決を「単一 Vector2 入力」から **「左目用 sourceId + 右目用 sourceId」必須**に変更し、`InputSystemAdapterBinding` の Gaze 解決経路、`GazeBindingConfigDto` / `FacialCharacterProfileConverter` 経由の JSON 表現、既存 Gaze 関連テスト群を一斉改修する。**角度制限は受信側で適用**（送信側は正規化 Vector2 を分解した 8 BlendShape を制限なしで送る）。
6. **複数送信先**: 1 binding で複数 endpoint への同報送信に対応（`List<EndpointConfig>` 形式）。BlendShape も Gaze も同じ endpoint 群へ流す。
7. **アドレス形式プリセット（案 B）**: mapping エントリは BlendShape 名 / Gaze `expressionId` のみを保持し、送信時に endpoint のプリセット enum と組み合わせて完全な OSC アドレスを組み立てる。
   - VRChat 形式: BlendShape `/avatar/parameters/{name}`、Gaze `/avatar/parameters/{expressionId}X` `/avatar/parameters/{expressionId}Y`
   - ARKit 形式: BlendShape `/ARKit/{name}`、Gaze は **PerfectSync 互換**として eyeLookXxx 系 8 BlendShape に分解して送信（案 II）
8. **ループ防止**: 同一プロセス内で OSC 受信 binding と送信 binding が同居した場合に無限ループしないための loopback 抑制ポリシー（**既定 ON / 明示無効化可能**）。
9. **JSON 永続化**: `OscSenderOptionsDto` / `OscReceiverOptionsDto`（mode 別 `OscMappingEntryDto[]` を内包）+ 既存 JSON パーサ経路（`SystemTextJsonParser`、実装は `JsonUtility` ベース）への統合、JSON スキーマドキュメント追記。heartbeat 周期、フェイルセーフモード、整合性検査ポリシーも DTO に含める。
10. **Editor**: 送信 / 受信の 2 種類の Drawer（UI Toolkit / PropertyDrawer）。受信 Drawer 内は mode 別 fold-out で BlendShape mapping / Gaze Vector2 設定（左目用 / 右目用 sourceId、左右独立トグル）を統合表示。整合性検査ポリシー、heartbeat 周期、フェイルセーフモード設定を含む。
11. **テスト**: EditMode（DTO 往復、Domain bus 単体、Vector2 ⇔ 8 BlendShape 双方向変換、Gaze 左右別 sourceId のラウンドトリップ）、PlayMode（FacialController → OscSender → OscReceiver E2E、複数 endpoint 同報、ループバック抑制、bundle アトミック性、フェイルセーフ復帰、ゾンビ排除、heartbeat 整合性検査、Gaze 左右非対称受信）。既存 Gaze 関連テスト（`FacialCharacterProfileSO_GazeConfigsRoundTripTests` 等）も新構造に合わせて改修。
12. **サンプル**: `Samples~/OscOutputDemo`（送信側）+ `Samples~/OscReceiverDemo`（受信専用、最小 SO 構成）を `Assets/Samples` 二重管理で配置。README / CHANGELOG / `docs/work-procedure.md` 更新。

## Non-Goals
- VRM 対応（別マイルストーン）
- UDP multicast / broadcast の独自実装（unicast 複数送信先で代替）
- 音声ストリーム / リップシンク音声の OSC 送出（リップシンク値のみ対象）
- OSC timetag による未来時刻スケジューリング（bundle は採用するが timetag の time-shift 機能は使わない）
- ボーンの世界回転 / 位置値そのものの OSC 送出（Gaze は Vector2 / 8 BlendShape 経由でのみ送る）

## 制約
- 既存パッケージ `com.hidano.facialcontrol.osc` に追加実装する形を取る（新パッケージは切らない）
- クリーンアーキテクチャ（Domain / Application / Adapters）の依存方向を維持。Domain は Unity / uOSC 非依存
- 毎フレームのヒープ確保ゼロを目標
- preview.2 以降の機能として位置付ける（preview.1 ターゲットには含めない）
- OSC binding はまだプロダクション運用に乗っていないため、本 spec 内の受信側 / `GazeBindingConfig` への破壊的変更を許容する（migration 不要）

## 参考ファイル
- 既存送信機構: `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/OSC/OscSender.cs`, `OscSender.cs`
- 受信 binding（拡張対象）: `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/AdapterBindings/OscReceiverAdapterBinding.cs`
- 既存 staleness 機構: `FacialControl/Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/InputSources/OscInputSource.cs:81-104`
- AdapterBinding 基底: `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Domain/Adapters/AdapterBindingBase.cs`
- Gaze 仕様: `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Adapters/ScriptableObject/GazeBindingConfig.cs`, `Runtime/Adapters/Bone/GazeBoneBinding.cs`
- FacialController 初期化: `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Adapters/Playable/FacialController.cs:108-247`
- PerfectSync 8 BlendShape リスト: `FacialControl/Packages/com.hidano.facialcontrol.osc/Tests/PlayMode/Adapters/InputSources/ArKitOscAnalogSourceTests.cs:53-56`

## Requirements

### Requirement 1: Domain 層の表情出力バス（BlendShape 値 + Gaze Vector2）
**Objective:** As a FacialControl コアの利用者（OSC 送信 binding 実装者）, I want FacialController の合成後 BlendShape 値および各 `GazeBindingConfig` に対応する Vector2 入力値を Domain 純度を保ったまま観察できる仕組み, so that Adapters 層から Unity / uOSC 非依存な契約で表情の全状態（BlendShape + 視線）を毎フレーム読み取れる

#### Acceptance Criteria
1. The Facial Output Bus shall be defined in `Hidano.FacialControl.Domain` namespace and shall not reference any UnityEngine, uOSC, or Adapters-layer type.
2. When FacialController が 1 フレームのレイヤー合成および Gaze 入力評価を完了した直後、かつ `AdapterBindingHost.OnLateTick` が dispatch される直前, the Facial Output Bus shall 全 BlendShape の合成後値（`ReadOnlySpan<float>`）と Gaze 入力 Vector2 スナップショットを登録済みオブザーバへ通知し、観察→送信→UDP 発射が同一フレーム内に完結することを保証する。
3. The Facial Output Bus shall Gaze Vector2 を Domain 表現の軽量構造体（例: `(float x, float y)` ペア or Domain 専用 `GazeSnapshot` 構造体）として表現し、`UnityEngine.Vector2` を Domain 層へ持ち込まない。
4. The Facial Output Bus shall BlendShape 値配列および Gaze スナップショットコレクションを内部バッファとして再利用し、毎フレームのヒープ確保をゼロにする。
5. When 利用者がオブザーバ（`IFacialOutputObserver` 相当）を登録/解除する API を呼び出した時, the Facial Output Bus shall 当該フレームの通知タイミング前後で安全に追加/削除を反映し、列挙中の並行変更例外を発生させない。
6. While オブザーバが 1 件も登録されていない, the Facial Output Bus shall 通知処理を skip し、合成後値配列のコピーや Gaze スナップショット構築、列挙コストを発生させない。
7. If 通知対象のオブザーバが例外をスローした場合, the Facial Output Bus shall その例外を Unity 標準ログ（`Debug.LogException` 相当の Adapters 層フック）に渡したうえで他オブザーバへの通知を継続する。
8. The Facial Output Bus shall BlendShape インデックスの並び順を FacialController の内部順序（`_blendShapeNames`）と一致させ、`OscMapping` 配列とインデックス対応で参照可能にする。
9. The Facial Output Bus shall Gaze Vector2 スナップショットを `expressionId`（`GazeBindingConfig.expressionId` と同値）でキー付けし、Adapters 層が任意の `GazeBindingConfig` 集合と join できるよう構造化する。
10. The Facial Output Bus shall Gaze 入力源が未接続のフレームでは当該 `expressionId` のスナップショットを通知対象から除外する（送信側は何も送らない方針、フェイルセーフは受信側の責務）。

### Requirement 2: OscSenderAdapterBinding の AdapterBinding lifecycle 結線
**Objective:** As a FacialControl 利用者, I want OSC 送信機能を Inspector の Add ドロップダウンから他の binding と同じ手順で追加・除去できるよう, so that 既存 `OscReceiverAdapterBinding`（受信側）と対称な UX で OSC 送信を有効化できる

#### Acceptance Criteria
1. The OscSenderAdapterBinding shall `[Serializable]` 属性および `[FacialAdapterBinding(displayName: "OSC Sender")]` 属性を付与した `AdapterBindingBase` の sealed 具象として実装される。
2. The OscSenderAdapterBinding shall パラメータレスコンストラクタを公開し、Inspector の Add ドロップダウンから `Activator.CreateInstance` 経由で生成可能にする。
3. When `OnStart(in AdapterBuildContext ctx)` が呼び出された時, the OscSenderAdapterBinding shall `ctx.HostGameObject` に `OscSender` を `AddComponent` し、`Configure(endpoint, port, mappings)` を呼び出して送信機構を起動する。
4. When `OnStart` 後に Domain 層の Facial Output Bus が利用可能な状態である時, the OscSenderAdapterBinding shall 自身を `IFacialOutputObserver` として登録し、合成後 BlendShape 値および Gaze Vector2 の双方の購読を開始する。
5. If `ctx.HostGameObject` が null、または BlendShape mapping と Gaze 送信構成が両方とも空の場合, the OscSenderAdapterBinding shall 送信を開始せず Unity 標準ログに警告を出力し、`OnStart` を no-op として安全に終了する。
6. When `OnLateTick(deltaTime)` が呼び出された時, the OscSenderAdapterBinding shall 直近フレームで受信した post-blend BlendShape 値配列、Gaze 関連メッセージ群、および送信元識別ヘッダを **1 つの OSC bundle にまとめて** `OscSender` 経由で全送信先に同報送信する。
7. When `Dispose()` が呼び出された時, the OscSenderAdapterBinding shall Facial Output Bus からのオブザーバ登録を解除し、`OscSender` を `Object.Destroy` で破棄したうえで内部状態を初期化済みフラグごとリセットする。
8. The OscSenderAdapterBinding shall `Slug` を `AdapterBindingBase.Slug` 規約に従って保持し、空 / 規約違反の場合は警告ログを出力したうえで起動を断念する。

### Requirement 3: 複数送信先（multi-endpoint unicast）の同報送信と OSC bundle アトミック性
**Objective:** As a VTuber 配信向け冗長構成を構築する Unity エンジニア, I want 1 つの OSC 送信 binding に複数の送信先（IP/port）を登録して BlendShape 値と Gaze Vector2 を同時に配り、各フレームの値が受信側で部分更新（フレーム跨ぎ）にならない仕組み, so that マルチキャスト / ブロードキャスト機構を使わずに複数描画システムへリアルタイム配信でき、Gaze の x / y が片方だけ届いて視線が斜めに歪むような事故を防げる

#### Acceptance Criteria
1. The OscSenderAdapterBinding shall 送信先を `List<EndpointConfig>`（または同等の `[Serializable]` コレクション）として保持し、任意件数の unicast endpoint を構成可能にする。
2. The EndpointConfig shall 少なくとも IP/host 文字列、UDP ポート番号、有効/無効フラグ、アドレス形式プリセット（VRChat 形式 / ARKit 形式）を保持する。
3. When 利用者が複数の endpoint を有効化した状態で `OnLateTick` が呼び出された時, the OscSenderAdapterBinding shall 各 endpoint に対して同一フレームの合成後 BlendShape 値および Gaze Vector2 を unicast で送信する。
4. The OscSenderAdapterBinding shall 1 フレーム分の全 OSC メッセージ（BlendShape 値、Gaze 関連、送信元識別ヘッダを含む）を **単一の OSC bundle** にまとめ、bundle 単位で UDP 送信する。
5. The OSC bundle shall 1 つの UDP データグラムに収まる範囲を上限とし、収まらない場合は Design で確定する分割ポリシー（複数 bundle に分割、または warning 出力）に従って動作する。
6. The OscSenderAdapterBinding shall endpoint ごとに独立した `OscSender` インスタンス（または同等の送信スロット）を確保し、1 endpoint の起動失敗が他 endpoint の送信を阻害しないようにする。
7. If 同一 endpoint（IP+port の組）が複数登録された場合, the OscSenderAdapterBinding shall 1 回の警告ログを出力したうえで重複を 1 つに正規化して送信する。
8. Where endpoint が無効化フラグで disabled に設定されている場合, the OscSenderAdapterBinding shall その endpoint への送信を skip し、他 endpoint への送信は継続する。
9. The OscSenderAdapterBinding shall 送信先件数が 0 件の場合に Unity 標準ログで警告を出力し、`OnStart` を no-op として終了する。
10. The OscSenderAdapterBinding shall UDP multicast / broadcast アドレスを独自に解決・送信する処理を実装しない（unicast での複数 endpoint 構成のみで要求を満たす）。

### Requirement 4: アドレス形式プリセット（案 B + 案 II: VRChat / ARKit + PerfectSync 互換 Gaze）
**Objective:** As a 既存 VRChat / ARKit / PerfectSync エコシステムと相互運用する Unity エンジニア, I want endpoint ごとに OSC アドレス形式をプリセットで切り替えると BlendShape も Gaze も全自動で対応するアドレス文字列が組み立てられる仕組み, so that 「endpoint A は VRChat 互換、endpoint B は ARKit / PerfectSync 互換」を 1 クリックで切り替えられる

#### Acceptance Criteria
1. The OscSenderAdapterBinding shall mapping 1 件あたり完全な OSC アドレスを保存せず、BlendShape 名（および Gaze の場合は `expressionId`）のみを保存し、送信時に endpoint のプリセット enum と組み合わせて完全なアドレス文字列を組み立てる（案 B）。
2. The OscSenderAdapterBinding shall endpoint ごとに「VRChat 形式」「ARKit 形式」を選択可能なプリセット enum を提供する。
3. When endpoint のプリセットが VRChat 形式に設定されている時, the OscSenderAdapterBinding shall その endpoint への BlendShape 送信アドレスを `/avatar/parameters/{BlendShape 名}` の形式で構築する。
4. When endpoint のプリセットが ARKit 形式に設定されている時, the OscSenderAdapterBinding shall その endpoint への BlendShape 送信アドレスを `/ARKit/{BlendShape 名}` の形式で構築する。
5. When endpoint のプリセットが VRChat 形式に設定され、Gaze 送信が有効な時, the OscSenderAdapterBinding shall Gaze Vector2 を `/avatar/parameters/{expressionId}X` および `/avatar/parameters/{expressionId}Y` の 2 メッセージとして送信する。
6. When endpoint のプリセットが ARKit 形式に設定され、Gaze 送信が有効な時, the OscSenderAdapterBinding shall Gaze Vector2 を **PerfectSync 互換の 8 BlendShape**（`eyeLookInLeft`, `eyeLookOutLeft`, `eyeLookUpLeft`, `eyeLookDownLeft`, `eyeLookInRight`, `eyeLookOutRight`, `eyeLookUpRight`, `eyeLookDownRight`）に分解し、各 BlendShape を `/ARKit/{eyeLook 名}` 形式で送信する（案 II）。
7. The ARKit プリセットでの Vector2 → 8 BlendShape 分解 shall **正規化済み Vector2 をそのまま** 分解し、`GazeBindingConfig` の角度制限（`outerYawAngle` / `innerYawAngle` / `lookUpAngle` / `lookDownAngle`）を送信側では適用しない。角度制限の適用は受信側の責務とする。
8. The OscSenderAdapterBinding shall BlendShape 名および `expressionId` にマルチバイト文字（日本語等）および記号（`_`, `.` 等）が含まれてもアドレス文字列を正しく構築できる（ASCII 限定の正規化を強制しない）。ただし PerfectSync 互換の `eyeLookXxx` 名は ARKit 規格の固定 ASCII 名を使用する。
9. The OscSenderAdapterBinding shall プリセット切り替え時に BlendShape および Gaze の両系統でアドレス文字列を再構築し、`OscSender.Configure(...)` へ渡す送信テーブルへ反映する。
10. Where 将来的に追加プリセットが必要になった場合, the OscSenderAdapterBinding shall 新規プリセットを enum 値の追加と分岐 1 か所の修正（BlendShape 用 + Gaze 用）で拡張可能な構造を維持する。

### Requirement 5: 同一プロセス内の OSC 受信 → 送信ループバック抑制
**Objective:** As a 同じ Unity プロセスで OSC 受信 binding と送信 binding を同居させる利用者, I want 受信した値をそのまま同じ endpoint に送り返して無限ループする事故を防ぐ仕組み, so that ローカル配信検証やループバックテストでも暴走を起こさず安心して結線できる

#### Acceptance Criteria
1. The OscSenderAdapterBinding shall ループバック抑制ポリシーを送信側のみで実装する（受信側 `OscReceiverAdapterBinding` 拡張とは独立した責務）。
2. The OscSenderAdapterBinding shall ループバック抑制ポリシーを `[Serializable]` フィールドとして保持し、その既定値を「有効（抑制 ON）」とする。
3. While 同一プロセス内に `OscReceiverAdapterBinding`（受信）と `OscSenderAdapterBinding`（送信）が同時に有効、かつ抑制が ON, the OscSenderAdapterBinding shall 各送信 endpoint が受信側 binding の listen endpoint（IP+port）と一致するかを起動時に検査する。
4. If 送信先 endpoint が受信 binding の listen endpoint と一致した場合（抑制 ON 時）, the OscSenderAdapterBinding shall その endpoint への送信を抑制し、Unity 標準ログで警告を出力する。
5. The OscSenderAdapterBinding shall 抑制ポリシーを明示的に無効化（OFF）するオプションを `[Serializable]` フィールドおよび Drawer UI で提供し、E2E テストや意図的なローカル配信検証で抑制を解除できるようにする。
6. When ループバック抑制が有効な状態で全 endpoint が抑制対象になった場合, the OscSenderAdapterBinding shall 送信を行わずに Unity 標準ログで警告を出力し、binding 自体は live 状態を維持する。
7. The OscSenderAdapterBinding shall 抑制判定を毎フレームではなく `OnStart` および endpoint 構成変更時にのみ行い、`OnLateTick` のホットパスに比較処理を入れない。

### Requirement 6: JSON 永続化（送信 / 受信 DTO）とスキーマドキュメント
**Objective:** As a JSON ファースト永続化を採用するプロジェクトの利用者, I want OSC 送信 / 受信 binding の設定（endpoint 一覧 / BlendShape mapping / Gaze 送信構成 / 抑制ポリシー / allowlist / heartbeat 周期 / 整合性検査ポリシー）を JSON で往復シリアライズできる仕組み, so that ビルド後でも JSON で送受信設定を差し替え、スキーマドキュメントで仕様を共有できる

#### Acceptance Criteria
1. The OscSenderOptionsDto shall `OscSenderAdapterBinding` が保持する設定（endpoint 一覧、各 endpoint の IP/port/プリセット/有効フラグ、ループバック抑制ポリシー、BlendShape mapping、Gaze 送信構成、送信元識別子 = 起動時生成 UUID / 起動時刻、BlendShape 名一覧 heartbeat 周期 = 既定 5 秒）を漏れなく表現する `[Serializable]` 型として定義される。
2. The OscReceiverOptionsDto shall `OscReceiverAdapterBinding`（受信側）が保持する設定（listen endpoint、mode 別 `OscMappingEntryDto[]`（BlendShape / Gaze_VRChat_XY / Gaze_ARKit_8BS）、staleness 超過時のフェイルセーフモード = ベース表情復帰 / 値据え置き選択、送信元 ID 検査ポリシー、整合性検査ポリシー = 警告ログ ON/OFF、bundle 解釈モード）を漏れなく表現する `[Serializable]` 型として定義される。
3. The OscMappingEntryDto shall mode（"blendShape" / "gazeVrchatXy" / "gazeArkit8Bs"）、expressionId、addressPattern、Gaze 用の sourceIdLeft / sourceIdRight / leftRightIndependent フィールドを保持する `[Serializable]` 型として定義され、`OscReceiverOptionsDto.mappings` 配列内に含められる。各フィールドの mode 別意味論は以下:
   - **Normal_BlendShape**: `addressPattern` は完全 OSC アドレス。`sourceIdLeft` / `sourceIdRight` / `leftRightIndependent` は無視
   - **Gaze_VRChat_XY**: `addressPattern` は base アドレス（末尾 X/Y 抜き、内部で `X` / `Y` 末尾付加）
   - **Gaze_ARKit_8BS**: `addressPattern` は **無視**（`/ARKit/eyeLookInLeft` 等 8 個固定で listen、D5）。DTO で非空の値が来た場合は Info ログ「ignored」+ 既定値扱い
   - **Gaze 系共通**: `leftRightIndependent` 既定 `false`。`true` 時のみ `sourceIdLeft` / `sourceIdRight` を参照し、いずれかが空文字 / null なら entry 全体スキップ + 警告（D4）
Gaze Vector2 受信は OscReceiverAdapterBinding に統合されたため、別 DTO は設けない。
4. When 各 DTO が JSON へシリアライズされた時, the JSON 出力 shall human-readable で diff フレンドリーな形式（インデント / 安定キー順）であること。
5. When 同等内容の JSON が DTO に往復デシリアライズされた時, the round-trip 結果 shall 元の DTO と値レベルで等価である。
6. If JSON 中に未知のキーが含まれている場合, the DTO デシリアライザ shall 当該キーを無視し、既知キーのみで DTO を構築する。
7. If JSON 中に必須キーの欠落 / 型不一致がある場合, the DTO デシリアライザ shall Unity 標準ログで警告を出力し、当該フィールドは既定値で補ったうえで構築を継続する。
8. The 各 DTO shall パッケージ内 `Documentation~/` 配下に JSON スキーマ仕様（フィールド一覧、型、既定値、サンプル JSON）を Markdown で提供し、Gaze 送信構成 / フェイルセーフモード / heartbeat 周期 / 整合性検査ポリシーも例示する。
9. The 各 DTO shall 既存 `com.hidano.facialcontrol.osc` パッケージ内に追加実装され、新パッケージを切らずに JSON ロード／セーブ経路へ統合する。
10. The 各 DTO shall 既存 JSON 経路で採用されている `SystemTextJsonParser`（実装は `JsonUtility` ベース、クラス名は歴史的経緯）と整合する形で `[Serializable]` 型で実装され、JSON ライブラリを新規に持ち込まない。

### Requirement 7: Editor 拡張（送信 / 受信の 2 種類 Drawer / UI Toolkit、受信は mode 別 fold-out）
**Objective:** As a Inspector から OSC 送受信を構成する Unity エンジニア, I want endpoint 一覧、mode 別 OscMappingEntry（BlendShape / Gaze Vector2 受信）、フェイルセーフモード、heartbeat 周期、整合性検査ポリシーを視覚的に編集できる UI Toolkit ベースの PropertyDrawer, so that 設定ファイルを直接編集せずに送信先 / 受信構成を統一 UX で設定できる

#### Acceptance Criteria
1. The 各 AdapterBindingDrawer shall UI Toolkit ベースで実装され、IMGUI を新規 UI コードに混入させない。
2. The OscSenderAdapterBindingDrawer shall endpoint 一覧を ReorderableList 相当の UI で表示し、追加 / 削除 / 並べ替え、各 endpoint の IP/host・ポート・プリセット（VRChat / ARKit）・有効/無効を編集できるフォーム要素を提供する。
3. The OscSenderAdapterBindingDrawer shall ループバック抑制ポリシー（既定: 有効）、BlendShape mapping 名一覧、Gaze 送信対象 `expressionId` 一覧の編集 UI を提供する。送信時のアドレス文字列はプリセットから自動生成されるためインライン編集は不要とする。
4. The OscSenderAdapterBindingDrawer shall BlendShape 名一覧 heartbeat 周期（秒、既定: 5）と、送信元識別子の表示（読み取り専用: 起動時 UUID、起動時刻）を提供する。
5. The OscReceiverAdapterBindingDrawer（受信側、破壊的変更）shall listen endpoint 編集、`OscMappingEntry[]` の ReorderableList 編集（各 entry は mode selector で BlendShape / Gaze_VRChat_XY / Gaze_ARKit_8BS を切替可能）、staleness 秒数編集、staleness 超過時のフェイルセーフモード切替（**ベース表情復帰** / 値据え置き、既定: ベース表情復帰）、整合性検査の警告ログ ON/OFF、bundle 解釈モードを提供する。
6. The OscReceiverAdapterBindingDrawer shall 各 OscMappingEntry の mode 選択に応じた fold-out UI を提供する:
   - **Normal_BlendShape**: expressionId + addressPattern のみ
   - **Gaze_VRChat_XY**: expressionId + addressPattern（X/Y 末尾抜きの base）+ leftRightIndependent トグル。トグル ON 時のみ sourceIdLeft / sourceIdRight フィールドを表示
   - **Gaze_ARKit_8BS**: expressionId + leftRightIndependent トグル。トグル ON 時のみ sourceIdLeft / sourceIdRight 表示。**addressPattern フィールドは非表示**（D5 / CI2、固定 8 アドレス listen）
   - Gaze Vector2 受信専用の別 Drawer は設けない
7. If 入力された IP / ポート / heartbeat 周期が明らかに不正（空文字 / 範囲外）な場合、または **leftRightIndependent = true の Gaze entry で sourceIdLeft / sourceIdRight のいずれかが空**な場合（D4 / CI1）, the 各 Drawer shall インラインで警告メッセージを表示し、当該 entry を保存時にスキップ対象とマークする。
8. The 各 Drawer shall Editor 専用 asmdef 配下に実装され、Runtime asmdef へ Editor 参照を漏らさない。

### Requirement 8: テスト（EditMode 単体 + PlayMode E2E）
**Objective:** As a TDD で実装する開発者, I want EditMode / PlayMode の役割分担と検証観点を仕様として明文化した状態, so that Red-Green-Refactor サイクルで安全に OSC 送信 / 受信 binding および Gaze 経路を構築できる

#### Acceptance Criteria
1. The osc-output-binding spec shall EditMode テストで `OscSenderOptionsDto` / `OscReceiverOptionsDto`（mode 別 `OscMappingEntryDto[]` を含む）の JSON 往復、未知キー無視、必須キー欠落時の既定値補完、Gaze entry の sourceIdLeft / sourceIdRight 両方欠落時のスキップ挙動を検証する。
2. The osc-output-binding spec shall EditMode テストで Domain 層 Facial Output Bus のオブザーバ登録 / 解除、列挙中変更、空オブザーバ時 skip、例外伝播のログ化を BlendShape 通知と Gaze 通知の両方で検証する。
3. The osc-output-binding spec shall EditMode テストで Vector2 → 8 BlendShape 分解および 8 BlendShape → 左右別 Vector2 逆合成の双方向変換が、左右非対称入力（例: 寄り目、片目流し目）を含む代表ケースで誤差範囲内に再現できることを検証する。
4. The osc-output-binding spec shall PlayMode テストで `FacialController` → Facial Output Bus → `OscSenderAdapterBinding` → 実 UDP → `OscReceiverAdapterBinding`（拡張版）の E2E 経路で post-blend BlendShape 値が到達することを検証する。
5. The osc-output-binding spec shall PlayMode テストで Gaze Vector2（左右非対称）が ARKit 形式（8 BlendShape）/ VRChat 形式（X/Y 2 メッセージ）双方の経路で受信側に到達し、誤差範囲で再現されることを検証する。さらに、D7 / CI1 を検証するため **ARKit mode かつ `leftRightIndependent = false` 時にも左右非対称 Gaze が情報損失なく `useDistinctLeftRight = false` の `GazeBindingConfig` 経路で両目別駆動として再現される**ケースを 1 件追加する。同様に D8 / CI2 を検証するため **OSC 受信 binding と InputSystem 受信 binding が同一 `expressionId` を提供した時に `binding.Slug` の lexicographic 昇順で deterministic に先頭採用 + 警告ログ**が出ることを E2E で確認する。
6. The osc-output-binding spec shall PlayMode テストで 1 つの `OscSenderAdapterBinding` から複数 endpoint へ unicast 同報送信した結果が 2 系統の受信側で BlendShape と Gaze の両方について受信されることを検証する。
7. The osc-output-binding spec shall PlayMode テストで **OSC bundle のフレームアトミック性**（1 フレーム分の全メッセージが受信側で同一フレームに反映されること）を検証する。
8. The osc-output-binding spec shall PlayMode テストで 同一プロセス内のループバック抑制ポリシー（既定 ON 時は送信されず、明示 OFF 時は送信される）を検証する。
9. The osc-output-binding spec shall PlayMode テストで **staleness 超過時のフェイルセーフ復帰**（送信側を停止して staleness 秒数経過後に受信側がベース表情へ復帰すること、送信再開後に表情が復元すること）を検証する。
10. The osc-output-binding spec shall PlayMode テストで **ゾンビ送信元排除**（複数の送信元 UUID が同時に観測される場合、最も新しい起動時刻の送信元のみが採用され、古い起動時刻の送信元から到着する値は無視されること）を検証する。
11. The osc-output-binding spec shall PlayMode テストで **BlendShape 名一覧整合性検査**（送信側 heartbeat 受信後、不一致 BlendShape のみ反映されず警告ログが出ること）を検証する。
12. The osc-output-binding spec shall PlayMode テストで `OnLateTick` のホットパスに GC アロケーションが発生しないことを `Profiler.GetTotalAllocatedMemoryLong` 差分等で監視する（BlendShape のみ送信ケース、Gaze 同送ケース、bundle 化ケース、heartbeat 含むケース）。
13. The osc-output-binding spec shall テスト命名を `{Target}Tests` / `{Method}_{Condition}_{Expected}` 規約に従って配置する。

### Requirement 9: サンプル（送信 + 受信専用最小構成）とドキュメント更新
**Objective:** As a UPM 経由でパッケージを Import する利用者, I want OSC 送信 binding と受信専用最小構成を分けて動かせる完結したサンプル, so that Inspector 設定例と JSON 例を見ながら配信側 / 描画側それぞれの導入経路を理解できる

#### Acceptance Criteria
1. The osc-output-binding spec shall `com.hidano.facialcontrol.osc` の `Samples~/OscOutputDemo`（送信側）に Scene / FacialProfileSO / OscSenderOptions JSON / README をまとめて配置する。
2. The OscOutputDemo サンプル shall BlendShape 送信、Gaze Vector2 送信（VRChat 形式と ARKit 形式の両方）、複数 endpoint 同報、ループバック抑制設定例を含む。
3. The osc-output-binding spec shall `com.hidano.facialcontrol.osc` の `Samples~/OscReceiverDemo`（受信専用、最小 SO 構成）に Scene / 受信専用最小 FacialProfileSO（OSC 入力源を素通しする 1 Layer のみ、Expression 定義不要）/ OscReceiverOptions JSON / Gaze 受信 binding 設定 / README をまとめて配置する。
4. The osc-output-binding spec shall 上記 2 サンプル一式を `FacialControl/Assets/Samples/` 配下にも二重管理として配置し、dev プロジェクトで Scene 結線して動作確認できる状態にする。
5. When `Samples~` または `Assets/Samples` のいずれかが編集された時, the osc-output-binding spec の作業手順 shall もう一方を同期コピーする手順を `docs/work-procedure.md` に明記する。
6. The osc-output-binding spec shall `com.hidano.facialcontrol.osc` の `package.json` の `samples` 配列に OscOutputDemo / OscReceiverDemo の両方を登録する。
7. The osc-output-binding spec shall `com.hidano.facialcontrol.osc` の `CHANGELOG.md` および `README.md` に OSC 送信 / 受信拡張 / Gaze Vector2 受信機能の追加と既存 `OscReceiverAdapterBinding` への破壊的変更を記載する。
8. The osc-output-binding spec shall `docs/work-procedure.md` にこの spec の作業手順を追加し、`docs/backlog.md` から OSC 送信関連の積み残しを引き上げて整理する。

### Requirement 10: パフォーマンス・アーキテクチャ制約
**Objective:** As a 性能設計指針（毎フレーム GC ゼロ、クリーンアーキテクチャ、preview.2 以降）に従う必要のあるプロジェクト, I want OSC 送信 / 受信 binding が全レイヤーの規約を破らないこと, so that 既存 contract に違反せず preview.2 以降にマージできる

#### Acceptance Criteria
1. The OscSenderAdapterBinding および拡張後の OscReceiverAdapterBinding shall `OnLateTick` / `OnFixedTick` のホットパス（毎フレーム実行経路、BlendShape と Gaze の両系統、bundle 構築 / 解釈含む）でヒープ確保を行わず、配列・バッファは `OnStart` 時点で確保し再利用する。本要件の **完全達成は preview.3 milestone（uOSC vendor copy + zero-alloc fork 化、Phase 10）** で実現する。preview.2 milestone では既存 uOSC の string / object[] アロケーションを一時許容し、本 spec 側のロジック（mode 別 entry 分別、Gaze 逆合成、bundle accumulator 等）は preview.2 時点でも GC ゼロを満たすこと。
2. The 各 binding shall Adapters 層（`Hidano.FacialControl.Adapters.OSC` 名前空間相当）に配置され、Domain / Application 層へ Unity / uOSC 参照を漏らさない。
3. The Facial Output Bus shall `Hidano.FacialControl.Domain` 配下に配置され、`Unity.Collections` 以外の Engine 参照を持たない。Gaze Vector2 の通知データ型も Domain 純度を保つ（`UnityEngine.Vector2` を Domain に持ち込まず、`(float x, float y)` などの Domain 表現を使う）。
4. The osc-output-binding 実装 shall 新規パッケージを切らず、既存 `com.hidano.facialcontrol.osc` パッケージへの追加実装として完結する。
5. The osc-output-binding 実装 shall 機能本体を **preview.2 milestone**（Phase 1〜9）として位置付け、preview.1 ターゲットの破壊的変更を発生させない。**uOSC vendor copy + zero-alloc fork 化（Phase 10）と uOSC 互換 facade 撤去（Phase 11）は preview.3 milestone** に送る。Gaze 左右独立（Req 13）は preview.2 必須（後ろ送りすると後に再度破壊変更が必要なため）。Phase 実施順序は D9 / CI3 により **Phase 1 → Phase 7 → Phase 2 → Phase 3 → Phase 4 → Phase 5 → Phase 6 → Phase 8 → Phase 9** とし（GazeBindingConfig 改修を OscReceiverAdapterBinding mode 統合より前に実施することで Gaze 機能の一時破綻を回避）、必要に応じて Phase 7 を 7a / 7b / 7c の 3 PR に分割する選択肢を保持する。
6. The osc-output-binding 実装 shall Windows PC 環境を主たる検証対象とし、モバイル / WebGL / VR 固有のコードパスを必須にしない（将来拡張の余地は契約として残す）。
7. The osc-output-binding 実装 shall エラーハンドリングを Unity 標準ログ（`Debug.Log/Warning/Error/LogException`）のみで行い、カスタム例外型を新規追加しない。

### Requirement 11: OscReceiverAdapterBinding（受信側）の破壊的拡張 — フェイルセーフ / ゾンビ排除 / bundle 解釈 / 整合性検査
**Objective:** As a 配信を受信して描画する Unity プロセスの運用者, I want 送信元の瞬断 / 落ちで受信側がベース表情へ復帰し、複数の送信元（ゾンビ含む）から流入する値の中から正規の 1 つだけを採用し、フレーム単位の値をアトミックに反映する受信 binding, so that 配信全体の安定性と表情破綻の防止が両立できる

#### Acceptance Criteria
1. The 拡張後 `OscReceiverAdapterBinding` shall 既存 binding と互換 API（slug、endpoint、staleness 等）を維持しつつ、本要件で追加する設定（フェイルセーフモード、整合性検査ポリシー、bundle 解釈モード）を `[Serializable]` フィールドとして追加する。OSC binding はまだプロダクション運用に乗っていないため、既存利用箇所への migration は不要とし、API 形状の breaking change を許容する。
2. When 受信が継続している間, the 拡張後 `OscReceiverAdapterBinding` shall 既存と同じく `OscDoubleBuffer` 経由で BlendShape 値を `IInputSourceRegistry` に publish する。
3. When `OscInputSource.TryWriteValues` が staleness 超過で false を返した時、かつフェイルセーフモードが「ベース表情復帰」に設定されている場合, the 拡張後 `OscReceiverAdapterBinding` shall 当該入力源の BlendShape 値配列を **全要素 0.0** で publish し、レイヤー合成がベース表情の状態へ自然遷移するよう経路を切り替える。
4. When staleness 超過後に新規パケットを受信した時, the 拡張後 `OscReceiverAdapterBinding` shall フェイルセーフ状態を解除し、通常の受信値 publish に復帰する。
5. The 拡張後 `OscReceiverAdapterBinding` shall 送信側が bundle 先頭に同梱する **送信元識別ヘッダ**（送信元 UUID + 起動時刻、Design で具体的 OSC アドレスを確定）を読み取り、複数の送信元 UUID が同時に観測された場合は **最も新しい起動時刻の送信元** のみを採用し、古い起動時刻の送信元から到着する値は廃棄する（ゾンビ排除）。
6. The 拡張後 `OscReceiverAdapterBinding` shall 採用送信元の切替が発生した時に Unity 標準ログで情報ログ（採用前後の UUID / 起動時刻）を出力する。
7. The 拡張後 `OscReceiverAdapterBinding` shall 受信した OSC bundle を **1 フレーム単位のアトミック更新**として扱い、bundle 内全メッセージの書き込みが完了してから `OscDoubleBuffer.Swap` を実行する。bundle 外の bare メッセージは受信時刻順にバッファへ書き込み、次の `OnFixedTick.Swap` で公開する。
8. The 拡張後 `OscReceiverAdapterBinding` shall 送信側 heartbeat（既定 5 秒間隔の BlendShape 名一覧）を別 OSC アドレスで受信し、自身の BlendShape mapping と照合する。
9. If heartbeat の BlendShape 名一覧と自身の mapping が部分不一致である場合, the 拡張後 `OscReceiverAdapterBinding` shall **不一致 BlendShape のみ反映を停止**し、一致分の更新は継続する（解釈 C: 部分不一致時は部分反映）。
10. When heartbeat 不一致を検出した時, the 拡張後 `OscReceiverAdapterBinding` shall 不一致内容（送信側にあるが受信側に無い名前 / 受信側にあるが送信側に無い名前）を Unity 標準ログで警告出力する。同一不一致セットに対するログ抑制は Design で確定する。
11. The 拡張後 `OscReceiverAdapterBinding` shall 整合性検査およびゾンビ排除判定を `OnFixedTick.Swap` の準備処理で行い、`OscInputSource.TryWriteValues` のホットパス（GC ゼロ目標）に重い文字列比較を入れない。

### Requirement 12: Gaze Vector2 受信（OscReceiverAdapterBinding 内に mode 別 entry として統合）
**Objective:** As a 受信側で送信元の視線を再現したい Unity エンジニア, I want OSC で届く eyeLookXxx 系 8 BlendShape または VRChat 形式の Vector2 を左右別 Vector2 として `GazeBindingConfig` の入力源に流し込める受信機能, so that 寄り目や片目だけの流し目など左右非対称の視線も含めて忠実に再現でき、角度制限は受信側で自由に調整できる。受信元 binding と Gaze 受信を別 binding に切り分けず、`InputSystemAdapterBinding` のように 1 binding 内で BlendShape と Gaze を同居させる

#### Acceptance Criteria
1. The OscReceiverAdapterBinding shall `OscMappingEntry.bindingMode` 列挙値 `Gaze_VRChat_XY` および `Gaze_ARKit_8BS` を受け付け、当該 entry の Gaze Vector2 受信を同 binding 内で担当する。Gaze 専用の別 binding（`OscGazeReceiverAdapterBinding` 等）は新設しない。
2. The OscReceiverAdapterBinding shall Gaze 系 mode entry について `expressionId`、`addressPattern`（mode 別意味論は Req 6.3 参照）、`leftRightIndependent` フラグ、`sourceIdLeft` / `sourceIdRight`（条件付き必須）を `OscMappingEntry` の `[Serializable]` フィールドで構成可能にする。
3. When ARKit 形式の eyeLookXxx 系 8 BlendShape が **`/ARKit/eyeLookXxx` の固定 8 アドレス**（D5 / CI2、`addressPattern` は無視）で到着した時, the OscReceiverAdapterBinding shall 当該 Gaze_ARKit_8BS entry に対して **左目用 Vector2** `(x_L, y_L) = (eyeLookInLeft - eyeLookOutLeft, eyeLookUpLeft - eyeLookDownLeft)` および **右目用 Vector2** `(x_R, y_R) = (eyeLookOutRight - eyeLookInRight, eyeLookUpRight - eyeLookDownRight)` として逆合成する（符号定義は Design で確定、ここでは規約例として記載）。
4. When VRChat 形式の `{addressPattern}X` / `{addressPattern}Y` が到着した時, the OscReceiverAdapterBinding shall 当該 Gaze_VRChat_XY entry の Vector2 を組み立てる。
5. The OscReceiverAdapterBinding shall 各 Gaze entry を mode と `leftRightIndependent` フラグの組合せに応じて以下のように `IInputSourceRegistry` に publish する（D6 / D7 / Cross-binding Slug Convention）:
   - **Gaze_VRChat_XY mode**:
     - `leftRightIndependent = false`（既定）: `(binding.Slug, expressionId)` 単一キーで 1 つの `IAnalogInputSource` を register（受信 Vector2 をそのまま両目共通駆動）
     - `leftRightIndependent = true`: `(binding.Slug, "{expressionId}.left")` / `(binding.Slug, "{expressionId}.right")` 2 キーで register（両 source とも受信 Vector2 と同値を publish、左右別 lookup 規約への適合のみが目的）
   - **Gaze_ARKit_8BS mode**（D7 / CI1）: `leftRightIndependent` フラグの値に関わらず、**常に `(binding.Slug, "{expressionId}.left")` / `(binding.Slug, "{expressionId}.right")` の 2 キーで左右別の `IAnalogInputSource` を register**。ARKit 8 BS が本質的に左右非対称情報を含むため代表値選択による情報損失を避ける。`useDistinctLeftRight = false` の `GazeBindingConfig` は自動マッチで両 keys を解決し両目別駆動になる。
6. The OscReceiverAdapterBinding shall `leftRightIndependent = true` の Gaze entry で `sourceIdLeft` または `sourceIdRight` のいずれかが空文字 / null である場合（D4 / CI1）、entry 全体をスキップし警告ログを Unity 標準で出力する。`leftRightIndependent = false` の場合は `sourceIdLeft` / `sourceIdRight` フィールドを読まないため空でも警告は出さない。
7. The OscReceiverAdapterBinding shall Gaze 系 entry に対して **角度制限を適用しない**。`GazeBindingConfig.outerYawAngle` / `innerYawAngle` / `lookUpAngle` / `lookDownAngle` は `GazeBonePoseProvider`（描画段）が適用する責務とし、本 binding は正規化 Vector2 を素通しする。
8. The OscReceiverAdapterBinding shall Gaze 系 entry の Vector2 を OscDoubleBuffer から読み取るホットパスで GC アロケーションを発生させない。
9. The OscReceiverAdapterBinding shall 自身が staleness 超過 / フェイルセーフ復帰状態にある時、Gaze 系 entry に対しても左右 Vector2 とも `(0, 0)` を publish し、視線がセンターへ自然遷移する（受信側 `GazeBindingConfig` が中央復帰）。binding 内部での状態伝播のため、別 binding 間の依存解決機構（旧設計の cross-binding lookup）は不要。

### Requirement 13: `GazeBindingConfig` の左右独立 Vector2 入力対応（破壊的変更、後方互換なし、D6 で D3 設計を採用）
**Objective:** As a Gaze の左右独立駆動を可能にしたいモデル制作者, I want `GazeBindingConfig` が **既定では expressionId 1 個だけで両目を自動解決**し、左右で異なる入力源を混ぜたい高度ケースでは flag を立てて左右別 sourceId を明示できる構造, so that シンプル運用時のユーザー入力が最小化され、左右独立採用時のみ詳細フィールドが現れて段階的に複雑度を上げられる

#### Acceptance Criteria
1. The 拡張後 `GazeBindingConfig` shall 必須フィールドとして `expressionId`（string）を保持し、既存の単一 `sourceId` フィールドは削除する（破壊変更、後方互換なし）。任意フィールドとして `useDistinctLeftRight: bool`（既定 `false`）、および `useDistinctLeftRight = true` 時のみ意味を持つ `sourceIdLeft` / `sourceIdRight` を保持する。
2. When `useDistinctLeftRight = false`（既定）の時, the GazeBindingConfig 解決経路 shall `expressionId` を `IInputSourceRegistry` のあらゆる binding slug 配下で自動探索する（D6 / Cross-binding Slug Convention）。`{any}:{expressionId}.left` および `{any}:{expressionId}.right` の両方が見つかれば左右別 publish 経路で接続、片方のみ見つかれば fallback で両目に流用、両方無ければ `{any}:{expressionId}` を 1 source として試す。**複数 binding が同じ `expressionId` を提供した場合は D8 / CI2 に従い `binding.Slug` の Ordinal lexicographic 昇順で先頭採用 + 警告ログを出力する**（deterministic、シーン構成や起動順に依存しない）。
3. When `useDistinctLeftRight = true` の時, the GazeBindingConfig 解決経路 shall `sourceIdLeft` / `sourceIdRight` の完全 slug 文字列で `IInputSourceRegistry` を直接 lookup する。片方のみ解決時は警告 + 解決済み側を両目 fallback、両方未解決時はベース姿勢を維持する。
4. The 拡張後 `GazeBindingConfig` shall 角度制限フィールド（`outerYawAngle` / `innerYawAngle` / `lookUpAngle` / `lookDownAngle`）を **左目 / 右目に対して左右別に適用** する責務を保持する。「向かって左に視線」と「向かって右に視線」で左目・右目それぞれの内側・外側角度が選択される既存ロジック（`GazeBindingConfig.cs` のコメント参照）を本拡張でも維持する。
5. The 拡張後 `InputSystemAdapterBinding.ExpressionBindingEntry`（bindingMode = Gaze）shall 同じく `useDistinctLeftRight` フラグを持ち、`false` 既定時は単一 `actionName` を左右共通として両目駆動、`true` 時のみ `actionNameLeft` / `actionNameRight` を持ち左右別 InputAction を解決する。
6. The 拡張後 `GazeBindingConfigDto` および `FacialCharacterProfileConverter` の JSON 表現 shall `expressionId` を必須フィールド、`useDistinctLeftRight` を任意（既定 `false`）、`sourceIdLeft` / `sourceIdRight` を `useDistinctLeftRight = true` 時のみ意味を持つフィールドとして表現する。`InputSystemAdapterBinding.ExpressionBindingEntry` も同じ構造の破壊的変更を行う。
7. The osc-output-binding 実装 shall 既存 Gaze 関連テスト（`FacialCharacterProfileSO_GazeConfigsRoundTripTests`、`FacialCharacterProfileExporter_GazeConfigsTests`、`FacialCharacterProfileSOInspectorGazeConfigsTests` 等）を新構造に合わせて改修し、`useDistinctLeftRight = false` 既定パスと `true` パスの両方を網羅したうえですべて緑にする。Phase 7 を 7a / 7b / 7c に分割する場合は、7a 完了時点で `GazeBindingConfig` Round-trip テストが緑、7b 完了時点で InputSystem Gaze E2E が緑、7c 完了時点で残る既存 Gaze テスト全部が緑となる構造とする。

### Requirement 14: 送信元識別ヘッダ / heartbeat の送信仕様
**Objective:** As a 複数送信元 / ゾンビ排除と BlendShape 名整合性検査を成立させたい運用者, I want 送信側が起動時生成 UUID / 起動時刻、および BlendShape 名一覧 heartbeat を一定周期で送出する仕様, so that 受信側が allowlist と heartbeat に基づいて正規の送信元を選別し、不一致 BlendShape を検出できる

#### Acceptance Criteria
1. The OscSenderAdapterBinding shall `OnStart` 時に **起動時生成 UUID**（プロセスごとにユニーク、再起動で変化）と **起動時刻**（UTC、Unix 秒 or ISO 8601）を生成し、内部状態として保持する。
2. The OscSenderAdapterBinding shall 各フレームの OSC bundle 先頭に **送信元識別ヘッダ**（UUID + 起動時刻、Design で具体的 OSC アドレスを確定）を 1 メッセージとして同梱する。
3. The OscSenderAdapterBinding shall **BlendShape 名一覧 heartbeat** を **起動時 1 回 + 5 秒間隔（既定、Drawer から変更可）** で送信する（方式 β-2）。
4. The BlendShape 名一覧 heartbeat shall 当該 binding が送信対象としている BlendShape 名のリスト（順序保持）を 1 つの OSC bundle 内で送出する。
5. The BlendShape 名一覧 heartbeat shall 通常の毎フレーム bundle と同じ送信先 endpoint 群へ送られる。
6. The OscSenderAdapterBinding shall heartbeat 周期が極端に短く設定された場合（例: 0.1 秒未満）に警告ログを出力し、最小値（Design で確定）にクランプする。
7. When ループバック抑制が有効で送信先が全 endpoint 抑制対象の場合, the OscSenderAdapterBinding shall heartbeat も送信しない。
8. The OscSenderAdapterBinding shall 送信元識別ヘッダ / heartbeat の構築・送信処理でも `OnLateTick` のホットパス GC ゼロを維持する（heartbeat の名前リストは `OnStart` 時に確保し再利用）。
