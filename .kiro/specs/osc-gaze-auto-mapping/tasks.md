# Implementation Plan

> TDD 厳守 (Red-Green-Refactor)。各実装サブタスクは「失敗するテストを先に書く → 最小実装で緑 → リファクタ」の順で進める。
> テスト配置基準: mock/Fake のみ・同期実行は EditMode、MonoBehaviour ライフサイクル・実 UDP・フレーム同期が必要なものは PlayMode (CLAUDE.md「テスト配置基準」準拠)。
> テスト実行: `D:/UnityEditors/6000.3.19f1/Editor/Unity.exe` の batchmode（`-runTests -testPlatform EditMode|PlayMode`、`timeout: 600000` の同期実行、他バージョンでの実行禁止）。実行前に同一プロジェクトを開いた Editor が無いことを確認する。
>
> **pre-existing 赤（本 spec の変更起因 FAIL として扱わない）**:
> - `SampleAssetsAreInSyncTests` 4 件（M-28、MultiSourceBlendDemo サンプル同期ずれ）
> - `TenIndependentBindings_OneSwap` フレーキー 1 件（PlayMode seed 依存）
> - OSC heartbeat/auto-mapping 系 4 件（S-21: `OscHeartbeatConsistencyTests` ×1 / `OscReceiverAdapterBindingAutoMappingIntegrationTests` ×2 / `OscReceiverGCAllocationTests` ×1）— **本 spec の解消対象**。タスク 1.1 でベースライン採取、タスク 4.6 で追従確認する

## Foundation: S-21 ベースライン採取と core 共有ロジックの共通化

- [ ] 1. Foundation: S-21 ベースライン再採取・gaze 読取共通化・source id 合成 helper

- [x] 1.1 S-21 pre-existing 赤 4 件のベースライン失敗メッセージを再採取する
  - 一切の変更を加える前のベースで、S-21 の 4 件（`OscHeartbeatConsistencyTests` ×1 / `OscReceiverAdapterBindingAutoMappingIntegrationTests` ×2 / `OscReceiverGCAllocationTests` ×1）を PlayMode batchmode で再実行し失敗メッセージを採取する（design Testing Strategy の分岐手順 (0)）
  - 各件を「gaze 未設定ログの LogAssert 未追従由来」か「heartbeat ハッシュ期待値のハードコードずれ由来（`Expected: 1085723225` 系）」かに分類する
  - 4 件の失敗メッセージと原因系統の分類が、タスク 4.6 実施時に参照できる形で記録されている (観測可能な完了条件)
  - _Requirements: 2.9_

- [ ] 1.2 (P) gaze 入力読取の単一実装を新設し挙動をテストで固定する
  - Vector2 読取と scalar フォールバック（x=v, y=0）・null / invalid 時の false 返却・[-1, 1] clamp 内包を単一の共通実装に集約する
  - 挙動選択（FacialController 側に寄せる: null チェック + clamp 内包）を EditMode テスト名で明示する
  - 読取 1 回あたり managed heap 確保 0 byte を維持する
  - EditMode テストで「scalar → (v, 0)」「vector2 読取」「invalid / null → false」「clamp 境界」が緑になる (観測可能な完了条件)
  - _Requirements: 8.1, 8.3_
  - _Boundary: GazeInputReader_

- [ ] 1.3 GazeBonePoseProvider / FacialController の gaze 読取を共通実装へ委譲する
  - GazeBonePoseProvider の重複読取実装を削除して共通実装へ委譲し、caller 側の冗長 clamp を削除、「y のみ駆動」の誤解コメントを実装に一致する記述へ修正する
  - FacialController の読取も共通実装への委譲に置換する
  - 同一の入力源・入力値に対し両経路で同一の読取結果が返ることをテストで確認する
  - 既存の gaze 読取関連テストが緑のまま維持される (観測可能な完了条件)
  - _Requirements: 8.1, 8.2, 8.3_

- [ ] 1.4 (P) gaze source id 合成 helper を追加する
  - 規約 id の 3 形（shared: `{slug}:{id}` / `.left` / `.right`）を合成する helper と sub 部のみ版を追加し、`.left` / `.right` リテラルの唯一の合成点にする（Spec 2 の規約 helper 化への集約先として局所化）
  - 既存の 3 段解決アルゴリズム（distinct → side-pair → shared、slug Ordinal 優先）は無改修とする
  - EditMode テストで 3 形合成が緑になり、既存の解決テストが緑のまま維持される (観測可能な完了条件)
  - _Requirements: 2.2, 6.2_
  - _Boundary: GazeBindingConfigResolver_

## Core: 広告 parse / 正規化ハッシュ / route plan の純ロジック

- [ ] 2. Core: GazeAdvertisementResolver の純ロジック実装

- [ ] 2.1 (P) 広告 payload の parse と順序安定ハッシュを実装する
  - flat pairs `[id, format, ...]` の string 列を (id, format) ペア列として解釈する（正常 / 空 / 奇数個は末尾孤立要素を無言 skip / 空 id skip / 同一 id 重複は先勝ち）
  - 未知 format 識別子のペアは skip し、warned フラグ経由で警告を 1 度だけ通知する
  - id Ordinal 昇順の interleave 列に正規化してから既存 FNV-1a helper でハッシュ計算し、順序揺れは同一・内容 / 形式変化は相違となる変化検出を提供する
  - 状態を持たない static 実装とし、scratch / 結果リストは呼び出し側所有の再利用リストで受け渡す（GC ゼロ方針）
  - EditMode テストで上記全分岐（空 / 奇数 / 空 id / 重複 / 未知 format / 順序揺れ / 内容変化）が緑になる (観測可能な完了条件)
  - _Requirements: 1.2, 2.4, 2.5, 2.6, 9.1_
  - _Boundary: GazeAdvertisementResolver_

- [ ] 2.2 手動除外込みの auto route plan 計算を実装する
  - 広告 entry から手動 gaze entry（有効な gaze mode の entry）と同一 expressionId（Ordinal、大文字小文字差は別 id）のペアを Register 前に除外した auto plan を返す
  - EditMode テストで「手動と同一 id の除外」「大文字小文字差は別 id 扱い」「手動なしで全件 plan 化」が緑になる (観測可能な完了条件)
  - _Requirements: 3.2_

## 送信側: 広告送出経路と Custom preset 例外回収

- [ ] 3. 送信側: heartbeat 同乗の gaze 広告と Custom preset の gaze 例外処理

- [ ] 3.1 (P) heartbeat metadata を束ねる payload struct と統合送出 overload を追加する
  - heartbeat 名 / preset / gaze 広告ペアを null 表現で束ねる readonly struct を導入し、frame bundle 送出の統合 overload を 1 本追加する
  - 既存 6 overload は無改修とし、既存経路の payload が byte 単位で不変であることを後方互換テストで担保する
  - 広告アドレスの UTF-8 バイト列は static 保持する（preset アドレス前例踏襲）
  - EditMode テストで新 overload の送出内容と既存 overload の後方互換が緑になる (観測可能な完了条件)
  - _Requirements: 1.1, 1.2_
  - _Boundary: OscSender_

- [x] 3.2 bundle builder に広告 message 組み立て（ペア境界 chunk 分割）を追加する
  - `/_facialcontrol/gaze` の string message を heartbeat / preset と同一 frame bundle（同一 timestamp）に追加する
  - MTU 超過時はペア境界（2 要素単位・chunk 内は常に偶数要素）で分割し、分割時も sender identity の継続 packet 添付が働くようにする
  - ArrayPool buffer 再利用の既存機構に乗り、送出ごとの新規確保を増やさない
  - EditMode テストで「広告 message の byte 構造（address / typetag / 4 byte アライン）」「ペア境界分割」「preset + heartbeat + 広告の同一 bundle 共存」「既存 bundle payload 不変」が緑になる (観測可能な完了条件)
  - _Requirements: 1.1, 1.2, 9.5_
  - _Boundary: OscBundleBuilder_

- [x] 3.3 送信 binding の広告ペア事前構築と heartbeat payload 経路への移行を実装する
  - OnStart で endpoint slot ごとに広告ペア配列を事前構築する（preset から形式名を確定: VRChat → `VRChat_XY` / ARKit → `ARKit_8BS`、構成済み gaze id 全件を対象、gaze ゼロ構成なら広告なし）
  - Custom preset slot は形式を確定できないため広告対象外とし、gaze 構成が存在する場合のみ警告を 1 度だけ通知する
  - heartbeat 送出分岐を payload struct 経路へ移行し、事前構築配列の参照使い回しで heartbeat ごとの新規確保ゼロを維持する
  - EditMode テストで「gaze ゼロ構成 → 広告ペアなし」「複数 gaze id の全件広告」「preset 別の形式名」「Custom の広告除外 + 警告 1 回」が緑になる (観測可能な完了条件)
  - _Requirements: 1.1, 1.3, 1.4, 1.5, 5.2, 9.5_

- [ ] 3.4 Custom preset + gaze の未捕捉例外を binding 内で回収する
  - gaze address 組み立ての `NotSupportedException` を binding 内で捕捉し、警告を出して当該 endpoint の gaze 送出のみ skip する（BlendShape 側の既存 catch 挙動と統一）
  - Custom preset + gaze 構成で OnStart が例外を外へ漏らさず、他 endpoint の送出（BlendShape / heartbeat / preset）と binding の起動が継続することがテストで緑になる (観測可能な完了条件)
  - _Requirements: 5.1_

## 受信側: 広告駆動 gaze route lifecycle

- [ ] 4. 受信側: 広告受信 → 再構築 → 共存 → 突合警告 → ログ整理

- [ ] 4.1 (P) 広告受信ハンドラ（accumulate + dirty）を追加する
  - 広告アドレス定数を既存 metadata アドレス定数と同じ箇所で単一定義する
  - sender identity gating 通過後の dispatch に広告分岐を追加し、`MarkAcceptedPacket` は呼ばない（広告で staleness フェイルセーフを延命させない）
  - 受信スレッド処理は「lock + 同一 bundle timestamp accumulate + dirty フラグの Volatile 書き込み」のみとし、managed heap 確保を伴う処理を行わない（heartbeat handler と同型）
  - 広告 message 受信で scratch に accumulate され dirty が立つこと、chunk 分割された広告が同一 timestamp で復元されることがテストで確認できる (観測可能な完了条件)
  - _Requirements: 2.3, 9.2_
  - _Boundary: OscReceiverAdapterBinding_

- [ ] 4.2 メインスレッド再構築（immutable-swap）と auto source の差分適用を実装する
  - OnFixedTick で dirty 消化 → scratch copy → parse / 正規化 → hash 比較 → 変化時のみ再構築とし、hash 一致時は新規確保ゼロで即 return する
  - gaze bundle state の lazy 初期化を行う（手動 gaze なしで起動し広告駆動で初めて gaze route を持つケース。OnStart 固定の制約解消）
  - auto source の差分適用: 新規 id → 生成 + Register / 継続 id → 既存インスタンス再利用（registry 操作なし）/ 消滅 id → Unregister。source id 合成は 1.4 の helper 経由とする（ARKit_8BS は常に `.left`/`.right`、VRChat_XY は共有 1 本 — 現行手動経路と同一規約）
  - 手動 route + auto route から新しい route 辞書と runtime entry 群を完全構築してから参照を Volatile swap する（受信スレッド race 回避。既存辞書の in-place 変更禁止）
  - 広告受信後に VRChat_XY / ARKit_8BS の route が動的生成され、GazeVector2InputSource が規約 id で registry に登録されることがテストで確認できる (観測可能な完了条件)
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 9.3_
  - _Depends: 1.4, 2.1, 2.2_

- [ ] 4.3 手動 mapping との共存規則・出自診断ログ・診断 API・Dispose 解放を実装する
  - 手動 gaze entry の OnStart 構築経路は現行挙動を維持し、手動 route / source は再構築対象外として参照ごと新辞書へ引き継ぐ（広告が一切来ない環境では手動のみで従来どおり動作）
  - 同一 expressionId は手動優先（plan 段階の Register 前除外）とし、registry の重複 LogError を発生させない
  - rebuild 時の診断ログ（manual 件数 / auto 件数 / auto id 列挙）で手動由来・広告由来を識別可能にする
  - 診断 API（最終広告ハッシュ / auto source id 一覧 / auto route 有無）を公開する
  - 広告系 runtime 状態は全て非シリアライズとし、既存アセットを再設定なしで動作させる。Dispose で scratch / dirty / hash / auto source 辞書 / warned 集合を全解放する
  - 手動 entry + 広告の共存で手動構成が上書きされず、診断ログから出自が読み取れることがテストで確認できる (観測可能な完了条件)
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

- [ ] 4.4 GazeConfig 突合警告（リフレクション注入）を実装する
  - 受信 binding に GazeConfig 注入用の Configure を追加し、FacialController の既存リフレクション注入契約（末尾引数が GazeConfig リストの Configure を探す）で受信側 GazeConfigs を受け取る（core 側変更ゼロ、OnStart 前後どちらの注入も受理）
  - rebuild 時に auto id と GazeConfig の expressionId を Ordinal 突合し、不一致 id ごとに設定手順の手掛かり付き警告を 1 度だけ出す（無警告沈黙の禁止）。未注入は空集合として扱い警告する
  - 突合の自動解決（既定フォールバック・GazeConfig 自動補完）は行わない（D-1 の案 (c)）
  - 不一致 id で警告が 1 回だけ出ること、一致時は警告が出ないことがテストで緑になる (観測可能な完了条件)
  - _Requirements: 4.2, 4.3_

- [ ] 4.5 「gaze mapping 未設定」ログを削除し VRChat_XY + 左右独立の runtime 警告を追加する
  - 受信開始時の「gaze mapping が未設定のため Gaze 受信は無効です…」の診断ログを削除し、広告未受信かつ手動 gaze なしの状態は route / source 無生成・無言（LogError なし）とする
  - 手動 route 構築時、`Gaze_VRChat_XY` + `leftRightIndependent=true` の entry に対し「VRChat_XY 形式は単一 Vector2 のみを運ぶため左右には同値が配られる」旨を 1 度だけ警告する（Decision 2: 警告に留め、entry の動作は継続）
  - NoGaze 起動でログ・エラーが一切出ないこと、当該組み合わせで警告が 1 回だけ出ることがテストで緑になる (観測可能な完了条件)
  - _Requirements: 2.7, 2.9, 7.2, 7.3_

- [x] 4.6 S-21 赤 4 件の追従確認（分岐手順）を実施する
  - ログ削除後に S-21 の 4 件を PlayMode batchmode で再実行し、1.1 のベースライン分類と突合する
  - ログ由来の赤は LogAssert 追加ではなくログ削除で緑化していることを確認する
  - heartbeat ハッシュ期待値ずれ由来の赤はテスト側期待値の再計算（ハードコード値の値非依存化）で解消する。本 spec のスコープを超える場合は S-21 部分クローズ + backlog 残件追記の方針を確定する（backlog のクローズ操作自体はタスク 8.4）
  - 4 件の各件が「緑化」または「残件方針の確定」のいずれかで決着している (観測可能な完了条件)
  - 実施結果: `OnStart_EmptyMappingsAndNoHeartbeat_DoesNotRegisterOscInputSourceOrChangeRenderer` は緑化。ログ由来の `OscHeartbeatConsistencyTests` は旧 gaze 未設定ログを出力しなくなったが、別系統の `OscInputSource` 診断ログが未追従のため残存。2 件の heartbeat ハッシュ検証はそれぞれ `122830949` / `133301657` となり、ベースラインのハードコード値ずれを再現した。S-21 は部分クローズとし、残るハッシュ2件および診断ログ追従はタスク 8.4 で backlog に追記・決着する。
  - _Requirements: 2.9_
  - _Depends: 1.1_

## Core 購読: 後発登録レースの解消

- [ ] 5. (P) FacialController の候補 id 先読み Subscribe で後発登録レースを解消する
  - binding 構成時に有効な binding slug 一覧を非シリアライズ状態で保持する
  - 規約解決パスでは現時点の解決可否に関わらず、slug 一覧 × GazeConfig の expressionId から候補 id 3 形（shared / left / right）を 1.4 の合成 helper で全合成して Subscribe する（明示 id 指定の左右独立構成は現行どおり直接 Subscribe）
  - Subscribe ハンドラは現行どおり目ボーン provider の再構築のみ行う（registry 再入なし）。後発 Register / 広告駆動 Register / Unregister（null 通知）のいずれでも provider が最新状態で再構築される
  - EditMode の決定論テストで「候補 Subscribe 集合の検証」「Subscribe 実行後に `.left` / `.right` id を Register → provider 再構築が発火し目ボーン binding が構成される（iFacialMocap 型の後発登録を模擬）」「Unregister（null 通知）での provider 再構築」が緑になる (観測可能な完了条件)
  - _Requirements: 4.1, 6.1, 6.2, 6.3_
  - _Boundary: FacialController_
  - _Depends: 1.4_

## Editor: Inspector 警告

- [ ] 6. (P) Inspector Drawer に VRChat_XY + 左右独立の警告を表示する
  - 既存の mapping 警告 HelpBox 機構を拡張し、`Gaze_VRChat_XY` かつ `leftRightIndependent=true` のとき「VRChat_XY 形式は単一 Vector2 のみを運ぶため左右には同値が配られる（左右独立には ARKit_8BS を使用）」旨を表示する（entry の skip 化はしない — Decision 2）
  - 既存の sourceIdLeft/Right 欠落警告と併発する場合は両文を連結表示する
  - EditMode テストで警告表示条件（対象組み合わせで表示 / それ以外で非表示 / 既存警告との併発）が緑になる (観測可能な完了条件)
  - _Requirements: 7.1, 7.3_
  - _Boundary: OscReceiverAdapterBindingDrawer_

## サンプル・ドキュメント整備

- [ ] 7. サンプルとドキュメントを広告駆動後の姿へ更新する

- [ ] 7.1 (P) backlog の M-25 番号重複を解消する（2 段階クローズの第 1 段: 付番）
  - 「表情 active 取得の系1/系2 二重化解消」を M-25 のまま維持し、「Gaze の auto mapping 化」ブロックへ次の空き番号を付番して重複を解消する
  - 履歴行の M-25 参照に「（旧 M-25、現 M-xx）」の注記を添えて参照曖昧化を防ぐ
  - backlog 内で M-25 が一意になり、gaze ブロックが新番号で参照可能になっている (観測可能な完了条件)
  - _Requirements: 10.3_
  - _Boundary: docs/backlog.md_

- [ ] 7.2 (P) OscReceiverDemo サンプルの gaze 手入力を削除する
  - 受信サンプルのプロファイルアセットから `Gaze_VRChat_XY` 手入力 1 件を削除し、Mappings 完全空（BlendShape / gaze とも自動マッピング）で配布する
  - GazeConfigs（`eye_look`）は温存し、送信サンプルが同じ expressionId を広告することでサンプル同士の目ボーン反映が成立する構成を保つ
  - サンプル起動で gaze mapping 手入力ゼロのまま広告駆動の gaze 疎通が成立する構成になっている (観測可能な完了条件)
  - _Requirements: 10.1_
  - _Boundary: OscReceiverDemoProfile.asset_

- [ ] 7.3 (P) 両サンプル README を gaze 自動マッピング後の姿に更新する
  - 受信側 README: gaze 自動マッピングの動作条件（`/_facialcontrol/gaze` 広告が前提 / FacialControl 以外の外部 OSC ソースは手動 mapping を使用）、GazeConfig の expressionId 一致が引き続き必要なこと（D-1、完全自動化は Spec 2）、gaze のみ途絶時は最終値保持となる制限（Decision 4）を記載する
  - 受信側 README のトラブルシュート節が参照する旧ログ文言（「gaze mapping が未設定…」）の記述を削除・改稿する
  - 送信側 README: `/_facialcontrol/gaze` 広告の payload 仕様（flat pairs / 形式 2 値 / Custom preset は広告なし）を追記する
  - 両 README から広告前提・手動 mapping の使い分け・GazeConfig 一致の必要性が読み取れる (観測可能な完了条件)
  - _Requirements: 4.3, 10.2_
  - _Boundary: OscReceiverDemo/README.md, OscOutputDemo/README.md_

- [ ] 7.4 (P) mental-model の gaze 受信記述を更新する
  - gaze 受信の記述（手動 mapping 必須・OnStart 固定の前提）を「広告駆動自動生成 + 手動上書きオプション」へ更新する
  - staleness が binding 単位共有であるため「BlendShape 継続 + gaze のみ途絶」では最終値保持となる制限を明記する（Decision 4）
  - mental-model から広告駆動後の受信挙動と制限が読み取れる (観測可能な完了条件)
  - _Requirements: 10.5_
  - _Boundary: docs/mental-model.md_

## Validation: E2E・GC 退行・全体スイープとクローズ

- [ ] 8. Validation: 手入力ゼロ E2E・GC 検証・全体テストと backlog クローズ

- [x] 8.1 手入力ゼロの E2E と後方互換を検証する
  - 決定論方式は受信 handler 直接呼び出し（既存 heartbeat テストと同方式）とし、広告の実送出周期の待ち時間を発生させない。実 UDP ケースの成立根拠（初回即時 heartbeat に広告が同乗）をテストコメントに明記する
  - VRChat preset: 受信側 gaze mapping 手入力ゼロ + 実 UDP loopback（port 19341〜）で、広告受信後に VRChat_XY route 経由の gaze 値が GazeVector2InputSource へ反映される
  - ARKit preset: 同条件で ARKit_8BS（eyeLook 8 BlendShape）route 経由で `.left` / `.right` source へ反映される
  - 旧 receiver 相当の経路で `/_facialcontrol/gaze` が未知アドレスとして無警告・無エラーで読み飛ばされることを確認する
  - 上記 PlayMode テスト群が緑になり、最終受け入れ条件「手入力ゼロで registry まで自動反映」が検証される (観測可能な完了条件)
  - _Requirements: 1.6, 11.1, 11.2, 11.4, 11.5_
  - _Depends: 3.3, 4.5_

- [x] 8.2 再構築・温存・共存・突合の各シナリオを E2E で検証する
  - 同一内容の広告 2 回で route / source が再生成されない（source インスタンス同一性 assert）
  - 広告内容変化で再構築され、消滅 id の source が Unregister される
  - 広告停止後も route / source が温存され、値は既存 staleness フェイルセーフ（RevertToBase / HoldLastValue）に委ねられる
  - 手動 entry と同一 id は手動優先で LogError が出ない / 広告なし環境では手動 mapping のみで従来どおり受信する
  - 後発登録シナリオで GazeConfig 一致時に目ボーン provider が再構築され接続される / GazeConfig 不一致 id は警告 1 回
  - 広告なし + 手動なしで route 無生成・旧ログも LogError も出ない
  - 既存退行禁止: 既存の手動 mapping E2E ケース・受信統合テストの gaze 検証部・送受信テストが緑のまま維持される
  - 上記 PlayMode シナリオ群が全て緑になる (観測可能な完了条件)
  - _Requirements: 2.4, 2.5, 2.7, 2.8, 3.2, 3.3, 4.1, 4.2, 6.1, 11.3, 11.5_
  - _Depends: 5_

- [ ] 8.3 (P) 広告経路の GC 検証を追加する
  - 広告処理済み・内容不変の状態で OnFixedTick を 100 回実行し gaze 経路由来の GC allocation が 0 byte であることを検証する
  - 広告由来で生成した GazeVector2InputSource の値読取 1 回あたり 0 byte を検証する（無改修の退行確認）
  - 広告同乗 heartbeat の送出で heartbeat ごとの新規確保が発生しないこと（事前構築ペア配列 + ArrayPool 再利用）を検証する
  - 上記 PlayMode 性能テストが緑になる (観測可能な完了条件)
  - _Requirements: 9.3, 9.4, 9.5_
  - _Boundary: OscReceiverGCAllocationTests, 送信側性能テスト_
  - _Depends: 4.3_

- [ ] 8.4 EditMode / PlayMode 全体スイープと backlog クローズ（第 2 段）を実施する
  - EditMode / PlayMode の全テストを batchmode で実行し、本 spec の変更起因の赤がゼロであることを確認する（冒頭一覧の pre-existing 赤は除外判定。S-21 の 4 件は緑化または 4.6 で確定した残件方針どおりであること）
  - backlog の gaze auto mapping エントリ（7.1 で付番した新番号ブロック）を運用ルールに従いクローズする（ブロック削除 + commit message に理由記載）
  - backlog の S-21 を 4.6 の結果に従いクローズまたは部分クローズ + 残件追記する
  - 全体スイープの結果と backlog の 2 エントリの決着が確認できる (観測可能な完了条件)
  - _Requirements: 2.9, 10.4_
  - _Depends: 4.6, 7.1_
