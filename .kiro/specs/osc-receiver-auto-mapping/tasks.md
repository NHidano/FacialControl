# Implementation Plan

> TDD 厳守 (Red-Green-Refactor)。各実装サブタスクは「失敗するテストを先に書く → 最小実装で緑 → リファクタ」の順で進める。
> テスト配置基準: mock/Fake のみ・同期実行は EditMode、MonoBehaviour ライフサイクル・実 UDP・フレーム同期が必要なものは PlayMode (CLAUDE.md「テスト配置基準」準拠)。

## Foundation: 共有プリミティブと差し替え API

- [ ] 1. Foundation: enum 拡張・Registry 差し替え API・heartbeat ハッシュ helper の整備

- [ ] 1.1 AddressPresetKind に Custom 値を追加する
  - 既存シリアライズ値を破壊しないよう `VRChat = 0` / `ARKit = 1` / `Custom = 2` の数値を明示する
  - Custom 値追加後、既存アセットの再 import なしで `VRChat` / `ARKit` がそれぞれ `0` / `1` のまま解釈されることを確認する (観測可能な完了条件)
  - _Requirements: 3.1_
  - _Boundary: AddressPresetKind_

- [ ] 1.2 IInputSourceRegistry に Replace API を追加し InputSourceRegistry で実装する
  - Domain interface に slug 版と複合 id (slug + sub) 版の Replace シグネチャを追加する
  - 既存登録があれば差し替え、なければ新規登録し、挿入順を保持する実装を追加する
  - 差し替え時は `Debug.Log` で「id 差し替え (旧型 / 新型)」を出力し、`Register` 重複時の `LogError` は発生させない
  - EditMode テストで「既存 id への Replace で挿入順保持」「未登録 id への Replace で新規登録」「null source で `ArgumentNullException`」「LogError が出ない」ことが緑になる (観測可能な完了条件)
  - _Requirements: 4.7_
  - _Boundary: IInputSourceRegistry, InputSourceRegistry_

- [ ] 1.3 HeartbeatHashHelper で FNV-1a 32-bit を GC ゼロで計算する
  - BlendShape 名配列を順序依存で連続ハッシュし、名前間に区切りを挟んで連続名衝突を回避する
  - 全体版と範囲指定版 (startIndex, count) の 2 経路を提供し、null / 空配列を安全に扱う
  - EditMode テストで「決定論性」「順序依存性」「同一入力の再現性」「null / 空安全」が緑になり、計算経路が 0 byte 確保であることを確認する (観測可能な完了条件)
  - _Requirements: 4.1_
  - _Boundary: HeartbeatHashHelper_

## Core: アドレス推定・mapping マージ・buffer/checker の改修

- [ ] 2. Core: アドレスプリセット推定と address 組み立て

- [ ] 2.1 (P) OscAddressFormatter に custom prefix overload を追加する
  - 任意 prefix + `{name}` で address を組み立てる文字列版 / UTF8 版 / Pool 再利用版の overload を追加する
  - Pool key を `(name, customPrefix)` 形式に拡張し、既存の `(name, AddressPresetKind)` Pool と衝突させない
  - `AddressPresetKind.Custom` を既存の preset 版 API に渡した場合は従来通り `NotSupportedException` を維持し、Custom は overload 経由で処理する契約とする
  - `customPrefix` が null/empty のとき `ArgumentException`、先頭 `/` 自動付加なし (呼び出し側責務)
  - EditMode テストで「`/myapp/` 等での組み立て」「空 prefix での例外」「Pool 再利用で同一 byte[] 返却」が緑になる (観測可能な完了条件)
  - _Requirements: 3.3, 3.5_
  - _Boundary: OscAddressFormatter_

- [ ] 2.2 (P) AddressPresetEstimator で preset 種別と custom prefix を確定する
  - preset address 由来の preset 名 (`vrchat` / `arkit` / `custom`) を最優先で解釈する
  - preset 名が null (preset address 未受信) のとき、BlendShape 名と `ARKitDetector.ARKit52Names` の一致率 ≥ 50% で ARKit、未満で VRChat と推定する
  - custom のとき続く custom prefix を採用し、prefix 欠落時は VRChat にフォールバックして warning を 1 度だけ通知する
  - 未知 preset 文字列は warning を 1 度だけ通知し名前ベース推定にフォールバックする
  - ARKit52 名の一致率計算は `ARKit52Names` を HashSet 化して 1 走査で count し GC ゼロを保つ
  - EditMode テストで preset 各分岐・名前ベース推定・一致率 50% 境界・ARKit52 名 0/26/52 件・warning 1 回が緑になる (観測可能な完了条件)
  - _Requirements: 3.1, 3.2, 3.7, 5.3, 5.4, 5.5_
  - _Boundary: AddressPresetEstimator_
  - _Depends: 1.1_

- [ ] 2.3 RuntimeMappingResolver で手入力と heartbeat 差分をマージする
  - 手入力 `Normal_BlendShape` entry のみで構築する初期解決経路と、手入力 ∪ heartbeat 差分のマージ経路を提供する
  - 結果配列の先頭に手入力分、続いて heartbeat 差分を並べ、各要素の出自タグ (Manual / HeartbeatAuto) を同順で返す
  - 同一 expressionId は手入力 entry の addressPattern を優先し heartbeat 由来推定で上書きしない
  - ARKit 推定では標準名に `/ARKit/{name}`、非標準名に VRChat フォールバックを割り当て、Custom は overload 経由で組み立てる
  - heartbeat と mesh の積集合が空のときは warning を 1 度だけ通知し、結果は手入力件数のみとする
  - 同名 BlendShape の address 衝突時は preset address 由来を最優先・無ければ VRChat を採用し warning を 1 度だけ通知する
  - mode が Normal_BlendShape 以外 (Gaze 等) や空文字列の entry は無視する
  - EditMode テストで「手入力 only / 空 only / 部分入力 + heartbeat / 重複時優先順 / 非標準名 VRChat fallback / 空 intersection warning 1 回」が緑になる (観測可能な完了条件)
  - _Requirements: 1.2, 1.5, 2.1, 2.2, 2.3, 3.4, 3.6_
  - _Boundary: RuntimeMappingResolver, OscAddressFormatter_
  - _Depends: 2.1, 2.2_

- [ ] 3. Core: 受信バッファと consistency checker の改修

- [ ] 3.1 (P) OscDoubleBuffer を Write/Resize 同一 lock 方式の動的 Resize に改修する
  - `Write` (受信スレッド) と `Resize` (メインスレッド) を同一 lock オブジェクト (`Monitor`) で保護する
  - Resize は旧 buffer を確保・コピー・Dispose し、Resize 中に来た範囲外 index の Write を lock 内で silently drop する
  - `GetReadBuffer` / `Resize` はメインスレッド同一スレッドのため lock 不要、`TryWriteValues` 読み取り経路に lock を入れず 0 byte 確保を維持する
  - EditMode テストで「Resize と Write を別スレッドから並行 1000 回実行しても native crash しない」「Resize 完了後の Write が新 buffer に書かれる」「範囲外 index Write が drop される」が緑になる (観測可能な完了条件)
  - _Requirements: 4.5_
  - _Boundary: OscDoubleBuffer_

- [ ] 3.2 (P) HeartbeatConsistencyChecker を mismatch warning 専用にスリム化する
  - `SkipMask` / `ContributeMask` / `_mappedMeshBlendShapeMask` をクラスから完全削除する
  - `UpdateFromHeartbeat` で senderOnly / receiverOnly 差分を計算し、mismatch hash ベースで `Debug.LogWarning` を 1 度だけ出力する経路と `HasMismatch` / `Clear` を維持する
  - 既存 `HeartbeatConsistencyCheckerTests` から SkipMask / ContributeMask の assertion を削除し SenderOnly / ReceiverOnly / HasMismatch のみ検証する形に再構成する
  - 再構成後の EditMode テストが緑になり、mismatch warning が 1 度だけ出ることを確認する (観測可能な完了条件)
  - _Requirements: 6.4_
  - _Boundary: HeartbeatConsistencyChecker_

- [ ] 3.3 OscInputSource から SkipMask 経路を削除し ContributeMask を引数受け取りに整理する
  - constructor の `skipMask` 引数・`_skipMask` フィールド・`TryWriteValues` 内の `_skipMask` 参照を削除する
  - ContributeMask は呼び出し側 (binding) から受け取る形に整理し、`TryWriteValues` 1 回あたり 0 byte 確保を維持する
  - 既存 `OscInputSourceMaskTests` の `skipMask:` 引数付き constructor 呼び出しを削除し ContributeMask のみで構築する形に再構成する
  - 再構成後の EditMode テストが緑になり、SkipMask なしで出力範囲が ContributeMask のみで決まることを確認する (観測可能な完了条件)
  - _Requirements: 4.4, 4.6_
  - _Boundary: OscInputSource_
  - _Depends: 3.2_

## Integration: 受信 binding の lifecycle 再構成と送信側 preset 送出

- [ ] 4. Integration: OscReceiverAdapterBinding の OnStart 3 Phase 分割と heartbeat 駆動再構築

- [ ] 4.1 OnStart を 3 Phase に分割し空 Mappings で socket だけ起動できるようにする
  - Phase 1 で OSC socket + message filter + Gaze entry の即時登録 + SenderIdentity / ZombieEvictionPolicy / OscBundleAccumulator / FailSafeMode 初期化を行う
  - 手入力 Normal_BlendShape entry が 1 件以上ある場合は Phase 2/3 を即実行し OscInputSource を即時登録する (ScenarioManual / ScenarioPartial)
  - 手入力が空の場合は Phase 3 を skip し heartbeat 待機状態とし OscInputSource を未登録のまま保持する (ScenarioEmpty)
  - ContributeMask は runtime mapping 配列から binding 自身で生成し OscInputSource constructor に渡す (SkipMask は渡さない)
  - `ReceiverEnabled` が false のときは heartbeat 受信も auto mapping 生成も行わず現行の warning を維持する
  - 空 Mappings 起動時に OscInputSource が未登録のまま socket が open し、Aggregator に値を一切出力しないことが確認できる (観測可能な完了条件)
  - _Requirements: 1.1, 1.4, 2.1, 4.6, 6.1, 6.3, 6.5_
  - _Boundary: OscReceiverAdapterBinding_
  - _Depends: 1.2, 2.3, 3.1, 3.3_

- [ ] 4.2 PresetAddress ハンドラを追加し preset 状態を runtime 内部に保持する
  - `PresetAddress` (`/_facialcontrol/preset`) を `BlendShapeNamesAddress` と同じ箇所で address 定数として単一定義する
  - `/_facialcontrol/preset` 受信時に preset 名 / custom prefix を runtime 内部状態に保持し、mapping 生成はトリガしない (Req 5.8 疎結合)
  - preset 状態は `OscRuntimeSettingsSO` を介さず `[NonSerialized]` runtime 状態として保持しシリアライズ済みアセットを破壊しない
  - preset address を一度も受信していない場合は名前ベース推定にフォールバックする
  - preset 受信が BlendShape 名 heartbeat より前でも後でも、次回 mapping 再構築時に最新 preset 状態が参照されることが確認できる (観測可能な完了条件)
  - _Requirements: 5.3, 5.6, 5.8_
  - _Boundary: OscReceiverAdapterBinding_
  - _Depends: 4.1_

- [ ] 4.3 heartbeat 駆動の mapping 再構築を OnFixedTick に実装する
  - 受信スレッドの BlendShape 名 heartbeat 受信は dirty フラグ立てのみ行い、FNV-1a 計算と再構築をメインスレッド OnFixedTick で実行する
  - heartbeat hash が前回と一致するときは mapping 配列 / OscInputSource / OscDoubleBuffer を新規確保せず既存インスタンスを再利用する
  - hash 変化時は AddressPresetEstimator で preset を確定し RuntimeMappingResolver でマージ、OscDoubleBuffer を Resize し新 OscInputSource を Replace 経由で差し替える
  - heartbeat 由来 runtime mapping と出自タグを `[NonSerialized]` 別コレクションに保持し後続 heartbeat で再評価できる構造にする (`OscMappingEntry` の SerializedField は無改修)
  - HeartbeatConsistencyChecker を runtime mapping 由来の名前一覧で再構成し mismatch warning を継続提供する
  - heartbeat 受信後に積集合分の Normal_BlendShape auto mapping が生成・登録され、hash 不変時は再構築されないことが確認できる (観測可能な完了条件)
  - _Requirements: 1.2, 1.3, 1.6, 2.2, 2.3, 2.4, 4.2, 4.3, 6.4_
  - _Boundary: OscReceiverAdapterBinding_
  - _Depends: 1.3, 4.2_

- [ ] 4.4 診断 API と Dispose の runtime 状態解放を実装する
  - RuntimeMappings / GetMappingOrigin / CurrentPreset / CurrentCustomPrefix / LastHeartbeatHash の診断 API を公開し出自を runtime + 診断ログから識別可能にする
  - Dispose 時に heartbeat 由来 runtime mapping / 拡張済み mask / 再確保した buffer を全て解放し leak を残さない
  - Dispose 後に heartbeat 由来 runtime 状態が解放され診断 API で出自が識別できることが確認できる (観測可能な完了条件)
  - _Requirements: 2.5, 2.6, 6.6_
  - _Boundary: OscReceiverAdapterBinding_
  - _Depends: 4.3_

- [ ] 5. Integration: 送信側 preset 専用 address の送出

- [ ] 5.1 OscBundleBuilder に preset 専用 address 送出経路を追加する
  - `/_facialcontrol/preset` address に preset 名 string を packing し、custom のときのみ custom prefix string を続けて送る message 構築経路を追加する
  - `/_facialcontrol/blendshape_names` の `string[]` payload と chunk 分割ロジックには一切触れない
  - preset message が BlendShape 名 heartbeat とは独立した OSC message として bundle に同梱されることが確認できる (観測可能な完了条件)
  - _Requirements: 5.1_
  - _Boundary: OscBundleBuilder_

- [ ] 5.2 OscSenderAdapterBinding に preset 出力 ON/OFF オプションを追加する
  - preset 出力 ON/OFF の SerializeField (デフォルト ON) と対応する JSON DTO フィールドを追加する (既存スキーマ破壊なし)
  - ON のとき heartbeat 送信タイミングで現在の AddressPresetKind を文字列化し preset 専用 address を同梱、OFF のとき送出しない
  - OFF 時は `/_facialcontrol/blendshape_names` のみ送出され受信側が名前ベース推定にフォールバックすることが確認できる (観測可能な完了条件)
  - _Requirements: 5.1, 5.7_
  - _Boundary: OscSenderAdapterBinding_
  - _Depends: 5.1_

## Validation: 統合テスト・退行確認・性能・サンプル更新

- [ ] 6. Validation: PlayMode 統合テストと退行確認

- [ ] 6.1 auto mapping の PlayMode 統合テストを追加する
  - `HandleHeartbeat` 直接呼び出し方式で決定論的に検証し heartbeat 5 秒間隔の待ち時間を発生させない
  - 空 Mappings + heartbeat 受信で VRChat 形式 / ARKit 形式の auto mapping が受信側 SkinnedMeshRenderer の weight を更新する
  - 空 Mappings + heartbeat 未受信で OscInputSource 未登録・BlendShape weight 初期値維持・LogError なしを検証する
  - 部分入力 + heartbeat で手入力 addressPattern を保持したまま heartbeat 差分を append する
  - custom preset + prefix で custom prefix 付き address が生成される / heartbeat hash 不変で再構築されない / 空 intersection で warning 1 回 / Dispose で runtime 状態解放を検証する
  - 上記 PlayMode テスト群が全て緑になることが確認できる (観測可能な完了条件)
  - _Requirements: 1.3, 7.1, 7.2, 7.3, 7.4, 7.5_
  - _Boundary: OscReceiverAdapterBindingAutoMappingIntegrationTests_
  - _Depends: 4.4_

- [ ] 6.2 既存 PlayMode 統合テストの退行禁止確認と SkipMask 検証の置換を行う
  - 手入力 mapping only の起動シーケンス・登録順序・mask 構成が現行と同一であることを維持する
  - Gaze entry (`GazeVector2InputSource` 登録・bundle accumulator 経路) が SkipMask 廃止・`skipMask` 引数削除の影響を受けず緑のまま維持されることを確認する
  - `OscHeartbeatConsistencyTests` の SkipMask 検証部を ContributeMask 検証 + mismatch warning 1 度のみ検証に置換する
  - `OscReceiverAdapterBindingTests` の SkipMask assertion を `OscInputSource.ContributeMask` 検証に置換する
  - SenderIdentity / ZombieEvictionPolicy / OscBundleAccumulator / FailSafeMode が退行しないことを既存テストの緑維持で確認する
  - `/_facialcontrol/preset` と `/_facialcontrol/blendshape_names` の実 UDP 送受信が緑のまま維持されることを確認する
  - 上記既存テスト群が緑のまま維持され SkipMask 検証が ContributeMask 検証へ置換完了することが確認できる (観測可能な完了条件)
  - _Requirements: 5.2, 6.1, 6.2, 6.3, 6.4_
  - _Boundary: OscHeartbeatConsistencyTests, OscReceiverAdapterBindingIntegrationTests, OscReceiverAdapterBindingTests, OscSendReceiveTests_
  - _Depends: 6.1_

- [ ] 6.3* heartbeat hash 不変時の毎フレーム GC ゼロ性能テストを追加する
  - heartbeat 内容が同一のとき `OnFixedTick` を 100 回呼び出して GC allocation が 0 byte であることを検証する
  - OscDoubleBuffer の Resize と Write を別スレッドから並行 1000 回実行しても native crash しないことを性能テストとして検証する
  - 性能テストが緑になり毎フレーム 0 byte / native crash なしが確認できる (観測可能な完了条件)
  - _Requirements: 4.1, 4.2, 4.4, 4.5_
  - _Boundary: Performance Tests_
  - _Depends: 6.1_

- [ ] 7. Validation: サンプルアセット・README 更新と follow-up 登録

- [ ] 7.1 OscReceiverDemoProfile.asset をハイブリッド構成に書き換える
  - `_mappings` から手入力 `Normal_BlendShape` 4 件を削除し `Gaze_VRChat_XY` 1 件のみ残す (BlendShape=auto / Gaze=手入力)
  - 既存 `Slug: osc` / `_settings` 参照を保持する
  - 受信開始時の Mappings が Gaze 1 件のみで、heartbeat 受信後に Normal_BlendShape auto mapping が生成され Gaze と並存することが確認できる (観測可能な完了条件)
  - _Requirements: 7.6_
  - _Boundary: OscReceiverDemoProfile.asset_
  - _Depends: 6.1_

- [ ] 7.2 (P) サンプル README に auto mapping 運用説明を追加する
  - OscReceiverDemo README で「Normal_BlendShape は heartbeat 駆動 auto mapping がデフォルト経路、Gaze は heartbeat に id を含まないため手入力 mapping を残す」を 1 段落以上で説明する
  - 両 README に auto mapping 動作条件 (heartbeat 受信前提)、`/_facialcontrol/preset` での preset 指定、custom prefix 指定方法、Gaze は別 spec で auto 化予定の節を追加する
  - OscOutputDemo README に `/_facialcontrol/preset` address payload の説明を追加する
  - 両 README に上記節が記載され auto mapping のデフォルト運用と preset 指定方法が読み取れることが確認できる (観測可能な完了条件)
  - _Requirements: 7.7_
  - _Boundary: OscReceiverDemo/README.md, OscOutputDemo/README.md_

- [ ] 7.3 (P) Gaze auto mapping follow-up を backlog に登録する
  - `docs/backlog.md` に M-25 (Gaze auto mapping: heartbeat / preset address への Gaze id 追加) が follow-up spec として登録済みであることを確認・整備する
  - 本 spec では Gaze を手入力 mapping のまま維持する方針が backlog に明記されていることが確認できる (観測可能な完了条件)
  - _Requirements: 7.8_
  - _Boundary: docs/backlog.md_
