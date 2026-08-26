# Requirements Document

## Project Description (Input)

## 目的
Adapter 層の設定値のうち「環境依存」「運用依存」「マシン依存」の項目を、現在の `FacialCharacterProfileSO` (キャラ SO) から切り離し、適切な置き場所に再配置する。キャラ SO は「キャラクター固有の表情データ」のみを保持する純粋な存在に戻す。

## 背景
現在、OSC ポート・IP・マイクデバイス名などの環境/マシン依存設定が `OscReceiverAdapterBinding` / `OscSenderAdapterBinding` / `ULipSyncAdapterBinding` の `[SerializeField]` としてキャラ SO に埋め込まれている。これにより以下の問題が発生している:
- 開発環境ごとに値を変更すると git diff が発生し、merge conflict が増える
- 本番環境とテスト環境の IP 混在リスク
- 同じキャラ SO を複数シーン/複数環境で使い回せない
- マイクデバイス名が個人マシン依存なのに git 管理下に置かれる

## 配置方針(合意済み)

### キャラ SO に残す (キャラ依存)
- `OscReceiverAdapterBinding._mappings` (OSC アドレス ↔ BlendShape)
- `OscSenderAdapterBinding._blendShapeNames` (キャラの BlendShape 名リスト override)
- `OscSenderAdapterBinding._gazeExpressionIds` (キャラの Gaze 用 Expression ID リスト)
- `ULipSyncAdapterBinding._analyzerProfile` (uLipSync.Profile)
- `ULipSyncAdapterBinding._phonemeEntries` (音素 ↔ BlendShape/Clip)
- `ULipSyncAdapterBinding._targetMeshHint`
- `ULipSyncAdapterBinding._maxWeightScale`

### 新規 Settings SO に切り出す (環境/運用依存)
- OSC Receiver: `_endpoint`, `_port`, `_stalenessSeconds`, `_failSafeMode`, `_bundleMode`, `_bundleAccumulationTimeoutMs`
- OSC Sender: `_endpoints` (リスト), `_heartbeatIntervalSeconds`, `_suppressLoopback`

### PlayerPrefs に移す (マシン依存)
- `ULipSyncAdapterBinding._deviceDescriptor` (DeviceName + DisambiguatorIndex)
- キー: `Hidano.FacialControl.LipSync.MicDevice.Name` / `Hidano.FacialControl.LipSync.MicDevice.Disambiguator`

## 採用する実装方式: 親 SO + Sub-asset SO 集約 (方式3)

```
AdapterRuntimeSettingsCollection.asset (Project ビューでは 1 ファイル)
  └─ OscRuntimeSettings (sub-asset)
       ├─ Receiver セクション
       └─ Sender セクション
```

- `AdapterRuntimeSettingsBase` (abstract ScriptableObject) を基底とする
- `AdapterRuntimeSettingsCollectionSO` が `List<AdapterRuntimeSettingsBase>` で sub-asset を束ねる
- `OscRuntimeSettingsSO` は Receiver/Sender を 1 つの SO に統合 (両 AdapterBinding が同じ SettingsSO を参照し、それぞれのセクションだけ読む)
- AdapterBinding (`OscReceiverAdapterBinding` / `OscSenderAdapterBinding`) は子 SO を直接 SerializeField で参照
- Collection SO の CustomEditor で sub-asset の追加/削除 UI を提供

## Adapter 追加/削除の想定レベル
- **対応レベル b**: Inspector でエントリ追加/削除、または新規 Adapter 種別を C# 型として追加することは安全に行える(既存パラメータが消失しない)
- **対応レベル c (型削除/リネーム)**: 今回はスコープ外。破壊的変更としてアナウンスする前提
- ただし将来の c 対応に向けて以下の拡張点を v0 から仕込んでおく:
  - `AdapterRuntimeSettingsBase._schemaVersion: int` フィールド
  - `ToJson()` / `FromJson()` API (各 RuntimeSettings サブクラスで override 可能)
  - `AdapterRuntimeSettingsCollectionSO.OnEnable()` でマイグレーション拡張点を予約(実装は空)

## 後方互換
- 既存 SO データとの後方互換性は **考慮不要**(プレリリース前、preview 段階)
- 既存テストは新構造に合わせて全面修正

## スコープ外
- Adapter 種別の C# 型削除/リネーム時の自動マイグレーション (将来別 spec)
- AdapterBinding (`OscReceiverAdapterBinding` / `OscSenderAdapterBinding`) の統合 (現状の 2 Binding 構造は維持)
- enabled フラグによる動作停止機構 (AdapterBindings リストからの除外で代替)
- ランタイムでの動的設定変更 UI

## 影響範囲
- `Packages/com.hidano.facialcontrol/Runtime/Adapters/AdapterBindings/` (Base, Collection の新規追加)
- `Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/AdapterBindings/OscReceiverAdapterBinding.cs` (フィールド削除 + Settings 参照追加)
- `Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/AdapterBindings/OscSenderAdapterBinding.cs` (同上)
- `Packages/com.hidano.facialcontrol.osc/Runtime/Settings/OscRuntimeSettingsSO.cs` (新規)
- `Packages/com.hidano.facialcontrol.lipsync/Runtime/Adapters/ULipSyncAdapterBinding.cs` (_deviceDescriptor 削除)
- `Packages/com.hidano.facialcontrol.lipsync/Runtime/Settings/LipSyncDeviceStore.cs` (新規, PlayerPrefs ラッパー)
- Editor: `AdapterRuntimeSettingsCollectionSO` の CustomEditor
- Tests: EditMode/PlayMode 両方で既存テストの修正と新規 SO 用テストの追加

## Requirements

## Introduction

本仕様は、`FacialCharacterProfileSO` (キャラ SO) に埋め込まれている「環境/運用/マシン依存」の設定値を切り離し、適切な格納先へ再配置するリファクタリングを定義する。新たに `AdapterRuntimeSettingsCollectionSO` を親 ScriptableObject、`OscRuntimeSettingsSO` 等を sub-asset として集約する方式 (方式3) を採用し、AdapterBinding (`OscReceiverAdapterBinding` / `OscSenderAdapterBinding`) は子 SO を直接 SerializeField で参照する。マシン依存項目 (マイクデバイス) は PlayerPrefs に格納し、git 管理外とする。本仕様は preview 段階のため既存 Asset との後方互換は考慮しないが、将来の C# 型削除/リネーム時のマイグレーション拡張点 (`_schemaVersion` / `ToJson` / `FromJson`) は v0 から仕込んでおく。

## Boundary Context

- **In scope**:
  - キャラ SO からの環境/運用/マシン依存項目の切り出し
  - 親 SO + Sub-asset SO 集約構造 (`AdapterRuntimeSettingsCollectionSO` + `AdapterRuntimeSettingsBase`) の新規追加
  - `OscRuntimeSettingsSO` (Receiver/Sender セクション統合) の新規追加
  - `OscReceiverAdapterBinding` / `OscSenderAdapterBinding` のフィールド削除と Settings 参照差し替え
  - `ULipSyncAdapterBinding._deviceDescriptor` の PlayerPrefs 移行 (`LipSyncDeviceStore` ラッパー新規追加)
  - `AdapterRuntimeSettingsCollectionSO` の CustomEditor (sub-asset 追加/削除 UI)
  - 将来マイグレーション用の `_schemaVersion` フィールド / `ToJson` / `FromJson` API 雛形
  - EditMode/PlayMode 両方のテスト整備
- **Out of scope**:
  - Adapter 種別の C# 型削除/リネーム時の自動マイグレーション (将来別 spec)
  - `OscReceiverAdapterBinding` と `OscSenderAdapterBinding` の統合 (現行 2 Binding 構造は維持)
  - `enabled` フラグによる動作停止機構 (AdapterBindings リストからの除外で代替)
  - ランタイムでの動的設定変更 UI
  - 既存 SO Asset データとの後方互換 (preview 段階のため不要)
- **Adjacent expectations**:
  - キャラ SO (`FacialCharacterProfileSO`) はキャラ依存項目のみを保持するという責務に戻る
  - `com.hidano.facialcontrol` (Core) には Base/Collection を、`com.hidano.facialcontrol.osc` には OSC 固有 SettingsSO を、`com.hidano.facialcontrol.lipsync` (今後分離予定の領域) には `LipSyncDeviceStore` を配置し、パッケージ境界を超えた依存を作らない
  - `Hidano.FacialControl.{Domain|Application|Adapters|Editor}` の asmdef 依存方向 (Adapters → Application → Domain) を維持する

## Requirements

### Requirement 1: 関心事の分離 (Character / Environment / Machine の分割)
**Objective:** As a Unity エンジニア (パッケージ利用者), I want キャラ SO から環境/運用/マシン依存項目が分離されていること, so that 同じキャラ SO を環境/マシンを越えて git diff を発生させずに再利用できる

#### Acceptance Criteria
1. The FacialCharacterProfileSO shall NOT serialize OSC receiver endpoint, OSC port, OSC staleness/failsafe/bundle 系設定, OSC sender endpoints list, heartbeat interval, suppress loopback, microphone device descriptor のいずれをも直接フィールドとして保持しない
2. The FacialCharacterProfileSO shall キャラ依存項目 (`OscReceiverAdapterBinding._mappings`, `OscSenderAdapterBinding._blendShapeNames`, `OscSenderAdapterBinding._gazeExpressionIds`, `ULipSyncAdapterBinding._analyzerProfile`, `_phonemeEntries`, `_targetMeshHint`, `_maxWeightScale`) を引き続き保持する
3. The AdapterRuntimeSettingsCollectionSO shall 環境/運用依存項目 (OSC Receiver/Sender セクション) を `OscRuntimeSettingsSO` sub-asset 経由で保持する
4. The LipSyncDeviceStore shall マイクデバイス名と Disambiguator Index を PlayerPrefs キー `Hidano.FacialControl.LipSync.MicDevice.Name` / `Hidano.FacialControl.LipSync.MicDevice.Disambiguator` に格納する
5. When 利用者が同一キャラ SO を異なる環境 (異なる IP/ポート/マイク) で使用する, the FacialControl パッケージ shall キャラ SO 側に git diff を発生させずに設定差し替えを可能にする

### Requirement 2: 親 SO + Sub-asset SO 集約構造
**Objective:** As a Unity エンジニア, I want Adapter 用設定を 1 つの Project Asset ファイルに sub-asset として束ねたい, so that プロジェクトビューが煩雑にならず、設定群を 1 ファイルとして取り回せる

#### Acceptance Criteria
1. The AdapterRuntimeSettingsBase shall abstract ScriptableObject として `Hidano.FacialControl.Adapters` 名前空間に定義される
2. The AdapterRuntimeSettingsCollectionSO shall `List<AdapterRuntimeSettingsBase>` フィールドを持ち、複数の sub-asset 型を束ねる
3. When 利用者が `AdapterRuntimeSettingsCollectionSO` の Asset を Project ビューで表示する, the Unity Editor shall 親 Asset 1 ファイルの下に sub-asset がフォールドツリーで表示される構造を提供する
4. The OscRuntimeSettingsSO shall `AdapterRuntimeSettingsBase` を継承し、Receiver セクション (`_endpoint`, `_port`, `_stalenessSeconds`, `_failSafeMode`, `_bundleMode`, `_bundleAccumulationTimeoutMs`) と Sender セクション (`_endpoints`, `_heartbeatIntervalSeconds`, `_suppressLoopback`) を 1 つの SO に統合する
5. The OscReceiverAdapterBinding shall `OscRuntimeSettingsSO` への参照を `[SerializeField]` で 1 つ保持し、その Receiver セクションのみを読む
6. The OscSenderAdapterBinding shall `OscRuntimeSettingsSO` への参照を `[SerializeField]` で 1 つ保持し、その Sender セクションのみを読む
7. When `OscReceiverAdapterBinding` と `OscSenderAdapterBinding` が同一の `OscRuntimeSettingsSO` を参照する, the OSC 拡張パッケージ shall Receiver/Sender 両系統で一貫した設定値を読み出す

### Requirement 3: パラメータ消失防止保証 (対応レベル b)
**Objective:** As a Unity エンジニア, I want sub-asset の追加/削除や新規 Adapter 種別の C# 型追加を Inspector で安全に行いたい, so that 既存の設定パラメータがリファクタリング操作で意図せず消失することがない

#### Acceptance Criteria
1. When 利用者が CustomEditor で sub-asset を新規追加する, the AdapterRuntimeSettingsCollectionSO shall 既存 sub-asset の serialize 済み値を保持したまま新規 sub-asset を List に追加する
2. When 利用者が新規 Adapter 種別を C# 型として追加し既存 Collection に組み込む, the AdapterRuntimeSettingsCollectionSO shall 既存 sub-asset の値を消失させない
3. If 利用者が CustomEditor で sub-asset を削除する, then the AdapterRuntimeSettingsCollectionSO shall 削除対象 sub-asset のみを `AssetDatabase.RemoveObjectFromAsset` で除去し、他の sub-asset の serialize 済み値に影響を与えない
4. The AdapterRuntimeSettingsCollectionSO shall 対応レベル b (Inspector エントリ追加/削除 + 新規 C# 型追加) の操作に対してパラメータ消失防止保証を満たす
5. The AdapterRuntimeSettingsCollectionSO shall 対応レベル c (C# 型削除/リネーム時の自動マイグレーション) を本仕様のスコープ外として扱う

### Requirement 4: マシン依存値の PlayerPrefs 統合 (マイクデバイス)
**Objective:** As a Unity エンジニア, I want マシン依存のマイクデバイス指定が git 管理外に保存されること, so that 個人マシン固有値がチームの git 履歴を汚染しない

#### Acceptance Criteria
1. The ULipSyncAdapterBinding shall `_deviceDescriptor` を `[SerializeField]` フィールドとして持たない
2. The LipSyncDeviceStore shall PlayerPrefs キー `Hidano.FacialControl.LipSync.MicDevice.Name` (string) および `Hidano.FacialControl.LipSync.MicDevice.Disambiguator` (int) の読み書きラッパー API を提供する
3. When ULipSyncAdapterBinding が初期化される, the LipSync Adapter shall `LipSyncDeviceStore` 経由で PlayerPrefs から DeviceName + DisambiguatorIndex を読み出す
4. If PlayerPrefs に該当キーが存在しない, then the LipSyncDeviceStore shall 既定値 (DeviceName=空文字, Disambiguator=0) を返し、例外を投げない
5. When 利用者が UI 上でマイクデバイスを選択する, the LipSync Adapter shall `LipSyncDeviceStore` 経由で PlayerPrefs に値を保存する
6. The LipSyncDeviceStore shall PlayerPrefs 以外の永続化機構 (ScriptableObject / JSON ファイル) を併用しない

### Requirement 5: 将来マイグレーション用拡張点
**Objective:** As a パッケージ保守担当 (将来の自分), I want C# 型削除/リネーム時のマイグレーションを実装できる拡張点が v0 から仕込まれていること, so that 将来別 spec で対応レベル c を導入する際に破壊的変更を最小化できる

#### Acceptance Criteria
1. The AdapterRuntimeSettingsBase shall `_schemaVersion` (int) フィールドを `[SerializeField]` として保持し、規定値は 1 とする
2. The AdapterRuntimeSettingsBase shall `ToJson()` / `FromJson(string json)` の仮想 API を公開し、サブクラスが override 可能にする
3. The OscRuntimeSettingsSO shall `ToJson()` / `FromJson()` を override し、Receiver/Sender 両セクションを JSON にラウンドトリップ可能にする
4. The AdapterRuntimeSettingsCollectionSO shall `OnEnable()` 内にマイグレーション拡張点 (空のフックメソッド or 明示的なコメントブロック) を予約する
5. The AdapterRuntimeSettingsCollectionSO shall 本仕様時点ではマイグレーション処理本体を実装しない (拡張点予約のみ)
6. Where 将来マイグレーション処理が追加される, the AdapterRuntimeSettingsBase shall `_schemaVersion` を読んでバージョン差分を判定できる契約を満たす

### Requirement 6: CustomEditor による sub-asset 追加/削除 UI
**Objective:** As a Unity エンジニア, I want Inspector 上で sub-asset の追加/削除を安全に行いたい, so that AssetDatabase API を直接叩かずに Collection を編集できる

#### Acceptance Criteria
1. The AdapterRuntimeSettingsCollectionSO Editor shall `CustomEditor` 属性で `AdapterRuntimeSettingsCollectionSO` 用 Inspector を提供する
2. When 利用者が Inspector の Add ボタンを押す, the AdapterRuntimeSettingsCollectionSO Editor shall `AdapterRuntimeSettingsBase` を継承する型の一覧から追加対象を選択する UI を表示する
3. When 利用者が追加対象型を確定する, the AdapterRuntimeSettingsCollectionSO Editor shall `ScriptableObject.CreateInstance` で sub-asset を生成し、`AssetDatabase.AddObjectToAsset` で親 Asset に追加し、`List<AdapterRuntimeSettingsBase>` に登録する
4. When 利用者が Inspector の Remove ボタンを押す, the AdapterRuntimeSettingsCollectionSO Editor shall 対象 sub-asset を `AssetDatabase.RemoveObjectFromAsset` で除去し、`List<AdapterRuntimeSettingsBase>` から外し、`AssetDatabase.SaveAssets` を呼び出す
5. The AdapterRuntimeSettingsCollectionSO Editor shall UI Toolkit で実装される (IMGUI を新規 UI に使わない)
6. The AdapterRuntimeSettingsCollectionSO Editor shall 同一型の sub-asset を 1 つの Collection に複数登録することを許可する (例: VRChat 用と ARKit 用の `OscRuntimeSettingsSO` を並べる、複数受信ポートのために `OscRuntimeSettingsSO` を 2 つ持つ等のユースケース)
7. The AdapterRuntimeSettingsBase shall `_label` (string) フィールドを `[SerializeField]` として保持し、同型 sub-asset が複数登録された際の識別に用いる
8. When 利用者が新規 sub-asset 追加時に既存と同一の `_label` を入力する, the AdapterRuntimeSettingsCollectionSO Editor shall Unity 標準ログで警告を出し、識別困難になる可能性を通知する (追加自体は許可する)

### Requirement 7: テストカバレッジ (EditMode / PlayMode)
**Objective:** As a パッケージ保守担当, I want 新構造に対する EditMode/PlayMode テストが整備されていること, so that TDD (Red-Green-Refactor) サイクルで回帰を検出できる

#### Acceptance Criteria
1. The Tests/EditMode shall `AdapterRuntimeSettingsCollectionSO` の sub-asset 追加/削除でパラメータ消失が発生しないことを検証するテストを含む
2. The Tests/EditMode shall `OscRuntimeSettingsSO` の Receiver/Sender 両セクションが正しく serialize/deserialize されることを検証するテストを含む
3. The Tests/EditMode shall `OscRuntimeSettingsSO.ToJson()` / `FromJson()` のラウンドトリップで全フィールドが保持されることを検証するテストを含む
4. The Tests/EditMode shall `LipSyncDeviceStore` の Load/Save (既定値返却含む) を検証するテストを実 PlayerPrefs に書き込まない方式で実施する (interface 抽象 / internal backend 差し替え / static reset 等、具体的方式は設計フェーズで決定)
5. The Tests/PlayMode shall `OscReceiverAdapterBinding` / `OscSenderAdapterBinding` が `OscRuntimeSettingsSO` を参照して実 UDP 経由で OSC 送受信できることを検証するテストを含む
6. The Tests/PlayMode shall `ULipSyncAdapterBinding` が `LipSyncDeviceStore` 経由で取得した DeviceName を使用してマイク初期化フローを開始することを検証するテストを含む
7. The FacialControl パッケージ shall 既存テストを新構造に合わせて全面修正し、旧フィールド (`_endpoint`, `_port` 等を キャラ SO 直下で参照していたテスト) への参照を全て除去する
8. The Tests/EditMode and Tests/PlayMode shall 命名規則 `{Target}Tests` / `{Method}_{Condition}_{Expected}` に従う

### Requirement 8: スコープ外項目の明示
**Objective:** As a パッケージ保守担当, I want 本仕様のスコープ外項目が明示されていること, so that 将来別 spec で扱う事項を混入させず、本仕様の完了判定を曖昧にしない

#### Acceptance Criteria
1. The 本仕様 shall Adapter 種別の C# 型削除/リネーム時の自動マイグレーションをスコープ外と明示する (将来別 spec で対応)
2. The 本仕様 shall `OscReceiverAdapterBinding` と `OscSenderAdapterBinding` の統合をスコープ外と明示する (現行 2 Binding 構造を維持)
3. The 本仕様 shall `enabled` フラグによる動作停止機構をスコープ外と明示する (AdapterBindings リストからの除外で代替)
4. The 本仕様 shall ランタイムでの動的設定変更 UI をスコープ外と明示する
5. The 本仕様 shall 既存 SO Asset データとの後方互換をスコープ外と明示する (preview 段階のため不要、既存 Asset は手動で再構築する前提)
6. If 実装中にスコープ外項目への踏み込みが必要と判明する, then the 開発者 shall `docs/backlog.md` または別 spec として切り出し、本仕様の完了条件には含めない
