# Implementation Plan

> 実装フェーズの進め方: design.md「Migration Strategy」に沿った 4 フェーズ構成。各 leaf は Red → Green → Refactor を 1 leaf 内で完結させる（leaf を分割しない）。各フェーズ末尾の gate（テスト pass / 目視確認）を満たしてから次フェーズへ進む。

## Phase 1: Core SO + JSON 層

- [x] 1. SO ルート `_gazeConfigs` データモデルと JSON スキーマ（preview.1 で `"1.0"` 統一）を整備する
- [x] 1.1 `FacialCharacterProfileSO` ルートに gaze configs コレクションを追加し read-only 公開する
  - SO ルート直下に `GazeBindingConfig` の serialized list を平坦に追加し、既存の `[SerializeReference]` adapter bindings 構造に副作用が出ないことを EditMode で確認する
  - `IFacialCharacterProfile` に gaze configs の read-only accessor を追加し、`FacialCharacterProfileSO` が空 list を null 化せず実装することを保証する
  - 観測条件: 既存 EditMode テスト一式が緑のまま、新たに追加した accessor 経由で空 list が取得できる
  - _Requirements: 1.1, 1.2, 1.7_

- [x] 1.2 JSON DTO とスキーマバージョン bump を実装する
  - `gaze_configs[]` を root に持つ DTO 構造を導入し、`ProfileSnapshotDto` に gaze configs フィールドを追加する
  - `SystemTextJsonParser` のスキーマバージョン定数は preview.1 リリース前段階のため `"1.0"` で固定し、`"1.0"` 以外を strict 拒否することを確認する（バージョンを別管理しないポリシー）
  - look-clip / lookXxxSamples は JSON 出力対象外（SO YAML 側 source-of-truth）の運用を DTO 設計に反映する
  - 観測条件: 新スキーマ JSON を parser に通すと DTO の gaze configs フィールドが値を保持し、旧 v2.0 JSON は `NotSupportedException` で拒否される
  - _Requirements: 2.1, 2.2_
  - _Depends: 1.1_

- [x] 1.3 (P) Converter で root → SO ルート方向の gaze configs マッピングを実装する
  - JSON DTO の root 直下 `gaze_configs[]` を SO ルートの gaze configs コレクションに変換するロジックを追加する
  - Domain `FacialProfile` には gaze を載せない方針を維持し、SO ルートのみに反映する
  - 観測条件: EditMode テストで DTO → SO 変換結果が期待 entry 数 / 各値で一致する
  - _Requirements: 2.3_
  - _Boundary: FacialCharacterProfileConverter_
  - _Depends: 1.2_

- [x] 1.4 (P) Exporter で SO ルート → root の gaze configs シリアライズを実装する
  - SO ルート gaze configs を JSON DTO の root 直下 `gaze_configs[]` に詰める出力ロジックを追加する
  - look-clip / lookXxxSamples は JSON 出力対象外の運用を Exporter にも反映する
  - 観測条件: EditMode テストで SO → DTO 変換結果が root 直下に gaze configs を出力し、binding 内部には出力されない
  - _Requirements: 2.4_
  - _Boundary: FacialCharacterProfileExporter_
  - _Depends: 1.2_

- [x] 1.5 SO ↔ JSON ラウンドトリップで gaze configs が value-equal であることを保証する
  - SO に gaze configs を持たせた状態で Exporter → Converter のラウンドトリップを行い、value-equal を assert する EditMode テストを追加する
  - 旧 v2.0 JSON 自動 migration コードが repository に存在しないことを確認し、CI が落ちる挙動を維持する
  - 観測条件: ラウンドトリップ後の SO gaze configs が元と value-equal、かつ旧スキーマに対する自動 migration コードがコードベースに存在しない
  - _Requirements: 2.5, 2.6, 11.1, 11.7_
  - _Depends: 1.3, 1.4_

**Phase 1 Gate**: EditMode round-trip / Converter / Exporter テストが全 pass。

## Phase 2: InputSystem binding

- [x] 2. InputSystem 結線レイヤを `InputSystemGazeBinding` に縮退し runtime pairing を実装する
- [x] 2.1 `InputSystemGazeBinding` を新設し `GazeExpressionConfig` 派生型を削除する
  - `expressionId` + `InputActionReference` のみを保持する薄い `[Serializable]` 値クラスを導入する
  - 旧 `GazeExpressionConfig` 派生型を削除し、依存テストおよび Sample asset の `RefIds[].type` 参照箇所を同 PR 内で書き換え／削除する
  - 観測条件: コードベース全体に `GazeExpressionConfig` 型参照が残らず、新クラスが asmdef 上で正しくシリアライズされる
  - _Requirements: 1.4, 1.8_

- [x] 2.2 `InputSystemAdapterBinding` から旧 `_gazeConfigs` フィールドを撤去し新フィールド構成を導入する
  - binding から bone path / 可動範囲 / look-clip 系フィールドを完全に撤去し、`_gazeInputBindings` の serialized list と runtime 注入用の `[NonSerialized]` injected gaze configs ハンドルを追加する
  - 観測条件: binding の serialized field 一覧に gaze 関連の bone path / look-clip フィールドが存在せず、`_gazeInputBindings` のみが残る
  - _Requirements: 1.3, 10.5_
  - _Depends: 2.1_

- [x] 2.3 binding の `Configure` シグネチャを拡張し SO ルート gaze configs を注入できるようにする
  - 既存 `Configure` に `gazeInputBindings` と `injectedGazeConfigs` を渡す引数を追加し、binding が両者を保持できるようにする
  - runtime 構築コード（FacialController 系）に「SO の gaze configs を取得して `Configure` に注入する 1 行」を追加し、Editor／PlayMode の双方から再現可能にする
  - 観測条件: runtime 構築経路を辿った後、binding の `_injectedGazeConfigs` が SO ルートの list と参照同値で保持されている
  - _Requirements: 1.5, 10.6_
  - _Depends: 2.2_

- [x] 2.4 `OnStart` で expressionId による pairing / warn / skip ロジックを実装する
  - injected gaze configs の各 entry を `_gazeInputBindings` から expressionId で lookup し、一致時に gaze provider を構築する
  - config 不在の binding は `Debug.LogWarning` で warn + skip、binding 不在の config は silent skip とし、いずれも例外を投げない
  - binding 自身に gaze 関連 field（bone path / range / look-clip）を一切読みに行かないことを実装上の不変条件として確保する
  - 観測条件: PlayMode で 一致 / config 不在 / binding 不在 の 3 ケースを実行し、それぞれ provider 構築 / silent skip / warn ログ + skip が観測される
  - _Requirements: 1.5, 1.6, 10.1, 10.2, 10.3, 10.4, 10.5_
  - _Depends: 2.3_

- [x] 2.5 PlayMode integration test `OnStart_GazePath_*` を新構造で書き換える
  - 旧 `_gazeConfigs` 直接参照を新構造（SO ルート injected configs + binding `_gazeInputBindings`）に置き換えた integration test に書き換える
  - 旧構造（`InputSystemAdapterBinding._gazeConfigs` / `GazeExpressionConfig` bone-path フィールド）を参照していた既存テストを削除または更新し、コードベースに残らないことを確認する
  - 観測条件: PlayMode integration test が新構造で全 pass し、旧構造への参照を持つテストが grep で検出されない
  - _Requirements: 11.5, 11.6, 11.7_
  - _Depends: 2.4_

**Phase 2 Gate**: PlayMode integration test `OnStart_GazePath_*` が全 pass。

## Phase 3: Editor Inspector

> 注意: Phase 3 の leaf はすべて `FacialCharacterProfileSOInspector.cs`（および同パッケージの `InputSystemAdapterBindingDrawer.cs`）への変更に集中する。同一ファイル編集のため leaf は逐次（`(P)` 不付与）で進める。

- [x] 3. Inspector セクション再編と GazeConfigs UX を実装する
- [x] 3.1 `CreateInspectorGUI` のセクション build 順を再編する
  - 「Save Status / Reference Model / Layers and Expressions / GazeConfigs（空セクションでよい） / Adapter Bindings / Debug」の順に build 順を変更する
  - 各セクションを foldout / labeled container として独立した視覚グループに分離し、Save Status Bar は最上段固定を維持する
  - 観測条件: Inspector を開いたときセクション順が指定どおりであり、Adapter Bindings は GazeConfigs の後に必ず描画される
  - _Requirements: 4.1, 4.2, 4.3, 4.4_

- [x] 3.2 GazeConfigs セクションの opt-in 追加 UX と単行 UI を実装する
  - 「+ GazeConfig を追加」ドロップダウンを Analog kind かつ未存在 expressionId の Expression のみで動的構成し、選択時に SO ルート gaze configs に append する
  - 候補 0 件のときはドロップダウンを disabled にし、その旨のラベルを表示する
  - 各 entry を「Expression 名 / bone path / range / look-clip / "参照モデルから自動設定"ボタン / remove ボタン」を備えた行として描画する
  - 観測条件: opt-in ドロップダウンから entry を追加すると SO ルート gaze configs に同 expressionId の entry が現れ、remove ボタンで該当 entry のみが消える
  - _Requirements: 5.1, 5.2, 5.3, 5.5, 5.6_

- [x] 3.3 参照モデル割当て自動補完と一括／単行再解決ボタンを実装する
  - reference model の null→non-null / 別 model 遷移を検出し、両方空の bone path のみ既存ヘルパーで自動補完する（手動値は保持）
  - 単行「参照モデルから自動設定」ボタンで該当 entry を上書き再解決し、GazeConfigs セクションに「全 GazeConfig を参照モデルから再解決」一括ボタンを設置して全 entry を上書き再解決する
  - reference model 未割当て時は単行ボタンと一括ボタンの両方を disabled にする
  - 観測条件: 両方空の entry のみ自動補完されることと、単行 / 一括ボタンが上書き再解決を実行することが Editor test で確認できる
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7_

- [x] 3.4 孤児 GazeConfig の自動削除と Undo group 集約を実装する
  - Expression 削除時 / Analog→非 Analog kind 変換時に同 expressionId の gaze config を SO ルートから削除する
  - `Undo.IncrementCurrentGroup` / `SetCurrentGroupName` / `CollapseUndoOperations` で 1 ユーザー操作 = 1 Undo step に集約する
  - 削除トリガーが (a) ユーザー明示削除 / (b) Expression 削除 / (c) Analog→Digital 変換 の 3 種に限定されることを実装上保証する
  - 観測条件: Expression 削除後に `Undo.PerformUndo` を 1 回行うと Expression と gaze config の双方が同時に巻き戻り、他のトリガーで gaze config が消えない
  - _Requirements: 7.1, 7.2, 7.3, 7.4_

- [x] 3.5 Expression 行 ID 表示を撤去し Debug セクションに ID マッピング一覧を追加する
  - Expression 行から expressionId ラベルを完全に取り除き、Layers and Expressions セクションから gaze 編集 UI が消えていることを保証する
  - Debug セクションに「Expression ID マッピング」一覧（name / expressionId / kind / layer の 4 列）を追加し、Expression 追加 / 削除 / 名前 / kind / layer 変更が次回 repaint に反映されるよう track する
  - 観測条件: Expression 行に id ラベルが存在せず、Debug セクションのマッピング一覧が SO 編集の都度更新される
  - _Requirements: 5.7, 8.1, 8.2, 8.3, 8.4_

- [x] 3.6 Inspector の dead code を整理し新名で再実装した API のみ残す
  - 旧 `_gazeConfigsProperty` / `AppendGazeConfigForExpression` / `RemoveGazeConfigByExpressionId` / `FindGazeConfigIndexByExpressionId` / `HasGazeConfigForExpression` / `BuildEyeLookFields` / `BuildGazeClipField` / `OnBuildAnalogExpressionInputSourceFields` / `ExpressionRowGazeXxx` 名前定数 / `ExpressionRowIdLabelName` 等を削除する
  - 同等動作は新 GazeConfigs セクション側で旧名を再利用しない新 API（例: append / remove-at / orphan-cleanup ヘルパー）として再実装されていることを確認する
  - Editor asmdef で警告なくコンパイルが通る状態にする
  - 観測条件: 旧名前定数 / 旧メソッドが grep で見つからず、Inspector が新セクション構成のみで Editor asmdef を通過する
  - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5_

- [x] 3.7 `InputSystemAdapterBindingDrawer` の gaze UI を新フィールドに合わせて差し替える
  - 旧 `_gazeConfigs` の inline UI 描画を削除し、`_gazeInputBindings` の薄い UI（expressionId + InputActionReference）を ListView ベースで追加する
  - 観測条件: binding drawer 上で `_gazeInputBindings` の追加 / 編集 / 削除が動作し、bone path / look-clip 系 UI が drawer から消えている
  - _Requirements: 1.4, 1.8_

- [x] 3.8 Inspector 行為の Editor test 群を追加する
  - opt-in 追加 / 単行 remove / Expression 削除での孤児削除 / Analog→Digital での孤児削除 / 単行「参照モデルから自動設定」上書き / 一括再解決上書き / reference model 新規割当て自動補完（手動値保持） / Undo 1 ステップ巻き戻し / Debug ID マッピングの追加・削除・kind 変更反映 / Expression 行に id ラベルが存在しない / 旧 dead 名前定数が query で見つからない を網羅する EditMode テストを追加する
  - 観測条件: 上記すべての観点が Editor test として緑になり、TDD の Red → Green → Refactor を 1 leaf 内で経て pass する
  - _Requirements: 11.4, 11.7_
  - _Depends: 3.2, 3.3, 3.4, 3.5, 3.6_

**Phase 3 Gate**: Editor test 群（opt-in / 単行 remove / 孤児削除 / 自動補完 / 一括再解決 / Undo / Debug ID マッピング / dead 名定数の不在）が全 pass。

## Phase 4: Samples + Docs

- [x] 4. Sample 二重管理同期と破壊変更ドキュメントを更新する
- [x] 4.1 (P) Sample asset（YAML）2 ファイルを新スキーマに同期手術する
  - `Assets/Samples/.../MultiSourceBlendDemoCharacter.asset` の YAML を直接編集し、SO ルートに `_gazeConfigs` を、binding 内部に `_gazeInputBindings` を配置する。`GazeExpressionConfig` 削除に伴い `RefIds[].type` も更新する
  - `Packages/com.hidano.facialcontrol.inputsystem/Samples~/.../MultiSourceBlendDemoCharacter.asset` を上記と同内容で同期する
  - binding 内部の `_gazeInputBindings` 各 entry の expressionId が SO ルート `_gazeConfigs` の expressionId と一致していることを目視で確認する
  - 観測条件: 双方の asset YAML が同一 diff で更新されており、SO ルート `_gazeConfigs` と binding `_gazeInputBindings` の expressionId が突合する
  - _Requirements: 3.1, 3.3, 3.5_
  - _Boundary: Sample assets (YAML)_
  - _Depends: 2.1, 2.2, 2.5_

- [x] 4.2 (P) Sample profile.json 2 ファイルを新スキーマに同期する
  - dev `Assets/Samples/.../profile.json` と UPM `Samples~/.../profile.json` の `schemaVersion` を preview.1 統一値 `"1.0"` に揃え、root 直下に `gaze_configs[]` を配置する
  - 観測条件: 双方の JSON が同一 diff で新スキーマに更新されており、parser が `NotSupportedException` を出さずに読み取れる
  - _Requirements: 3.2, 3.3_
  - _Boundary: Sample profile.json_
  - _Depends: 1.5_

- [x] 4.3 Multi Source Blend Demo scene を Editor で Play して HUD と console を目視確認する
  - Unity Editor で Multi Source Blend Demo scene を開き Play、HUD 経由で gaze が駆動することと scene load 時 / 実行中に schema-version 警告 / null-reference 警告が出ないことを確認する
  - 観測条件: HUD 操作で gaze 出力が更新され、Console に gaze 関連の warning / error が 0 件
  - _Requirements: 3.4_
  - _Depends: 4.1, 4.2_

- [x] 4.4 (P) `CHANGELOG.md` に破壊変更エントリを追加する
  - `com.hidano.facialcontrol/CHANGELOG.md` の "Breaking changes" 区画に gaze configs root 昇格と schemaVersion を preview.1 統一値 `"1.0"` に揃えた件を記録する
  - 観測条件: CHANGELOG にエントリが追加され、リリースノートとしてそのままビルド出力できる
  - _Requirements: 2.7_
  - _Boundary: com.hidano.facialcontrol/CHANGELOG.md_
  - _Depends: 1.5, 2.5_

- [x] 4.5 (P) `Documentation~/migration-guide.md` に hand-edit 手順を追記する
  - 旧構造 → 新構造（schemaVersion `"1.0"` 統一）への JSON / SO YAML hand-edit 手順を例示し、binding 内部 `_gazeConfigs` から SO ルート `_gazeConfigs` + binding `_gazeInputBindings` への移植例を記載する
  - 自動 migration コードを書かない方針も明記する
  - 観測条件: migration guide を読みながら旧スキーマ asset を手動で書き換えると、Sample と同じ新スキーマに到達できる
  - _Requirements: 2.6, 2.8_
  - _Boundary: com.hidano.facialcontrol/Documentation~/migration-guide.md_
  - _Depends: 1.5, 2.5_

**Phase 4 Gate**: HUD 目視 OK + Sample 二重管理 diff 確認 + ドキュメント更新の 3 点が完了。

## スコープ外事項の固定

- [ ] 5. スコープ外事項を実装上の不変条件として確定する
  - `ExpressionKind` に Vector2 kind を新設しないこと、OSC / ARKit native gaze 駆動を実装しないこと（`IBonePoseSource` 等の契約面のみ温存）、BlendShape-only gaze の挙動を schema 再配置以外で変更しないこと、自動まばたき / 視線追従ターゲットを導入しないこと、bone path 相対化を本 spec で行わないことを実装レビュー時に確認する
  - 上記いずれかの拡張要望が出た場合は `docs/backlog.md` または別 spec に切り出す運用を維持する
  - 観測条件: 本 spec の最終差分にスコープ外項目への変更が含まれず、レビュー時に逸脱がないことが確認される
  - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5, 12.6_
  - _Depends: 4.3, 4.4, 4.5_

## Requirements Coverage Map

| Requirement | Tasks |
|-------------|-------|
| 1.1 | 1.1 |
| 1.2 | 1.1 |
| 1.3 | 2.2 |
| 1.4 | 2.1, 3.7 |
| 1.5 | 2.3, 2.4 |
| 1.6 | 2.4 |
| 1.7 | 1.1 |
| 1.8 | 2.1, 3.7 |
| 2.1 | 1.2 |
| 2.2 | 1.2 |
| 2.3 | 1.3 |
| 2.4 | 1.4 |
| 2.5 | 1.5 |
| 2.6 | 1.5, 4.5 |
| 2.7 | 4.4 |
| 2.8 | 4.5 |
| 3.1 | 4.1 |
| 3.2 | 4.2 |
| 3.3 | 4.1, 4.2 |
| 3.4 | 4.3 |
| 3.5 | 4.1 |
| 4.1 | 3.1 |
| 4.2 | 3.1 |
| 4.3 | 3.1 |
| 4.4 | 3.1 |
| 5.1 | 3.2 |
| 5.2 | 3.2 |
| 5.3 | 3.2 |
| 5.4 | 3.2, 3.6 |
| 5.5 | 3.2 |
| 5.6 | 3.2 |
| 5.7 | 3.5 |
| 6.1 | 3.3 |
| 6.2 | 3.3 |
| 6.3 | 3.3 |
| 6.4 | 3.3 |
| 6.5 | 3.3 |
| 6.6 | 3.3 |
| 6.7 | 3.3 |
| 7.1 | 3.4 |
| 7.2 | 3.4 |
| 7.3 | 3.4 |
| 7.4 | 3.4 |
| 8.1 | 3.5 |
| 8.2 | 3.5 |
| 8.3 | 3.5 |
| 8.4 | 3.5 |
| 9.1 | 3.6 |
| 9.2 | 3.6 |
| 9.3 | 3.6 |
| 9.4 | 3.6 |
| 9.5 | 3.6 |
| 10.1 | 2.4 |
| 10.2 | 2.4 |
| 10.3 | 2.4 |
| 10.4 | 2.4 |
| 10.5 | 2.2, 2.4 |
| 10.6 | 2.3 |
| 11.1 | 1.5 |
| 11.2 | 1.3 |
| 11.3 | 1.4 |
| 11.4 | 3.8 |
| 11.5 | 2.5 |
| 11.6 | 2.5 |
| 11.7 | 1.5, 2.5, 3.8 |
| 12.1 | 5 |
| 12.2 | 5 |
| 12.3 | 5 |
| 12.4 | 5 |
| 12.5 | 5 |
| 12.6 | 5 |
