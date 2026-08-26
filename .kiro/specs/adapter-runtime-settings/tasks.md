# Implementation Plan

> 本タスクリストは `requirements.md` / `design.md` / `research.md` を前提とする。各サブタスクは TDD (Red → Green → Refactor) サイクルで進めることを想定し、テスト追加と実装をペアで含む。`(P)` マーカーは並列実行可能なタスクに付与する。

## 0. Pre-Implementation 整合性修正

- [x] 0. design.md と backlog の整合性修正
- [x] 0.1 design.md の `AdapterRuntimeSettingsBase` 配置記述を Adapters 層に統一する
  - `Allowed Dependencies` 節と `Components and Interfaces` 表で `Hidano.FacialControl.Domain` と `Hidano.FacialControl.Adapters (core)` の二重記述になっている箇所を `Hidano.FacialControl.Adapters.RuntimeSettings` に単一化する
  - `ScriptableObject` 継承クラスは Adapters 層配置である根拠 (要件 2.1) を本文に明記する
  - 修正後の design.md を再読し、Base/Collection の配置に関する記述が矛盾なく Adapters 層に揃っていることを観測可能な完了条件として確認する
  - _Requirements: 2.1_
- [x] 0.2 docs/backlog.md に「sub-asset 削除時の逆引き確認ダイアログ」項目を追加し、本 spec 内では Drawer HelpBox 警告で代替する方針を明文化する
  - validate-design Critical Issue #2 (Sub-asset 削除時の binding 参照欠落) を「(b) Drawer 側 HelpBox による警告で本 spec 内対応」「(a) Editor の逆引き確認ダイアログは後続 spec で対応」と整理する
  - docs/backlog.md に該当エントリが追加されており、参照欠落のリスクと暫定対応を観測できる状態にする
  - _Requirements: 3.3, 6.4_

## 1. Foundation: パッケージ構造とテスト基盤

- [x] 1. Adapter Runtime Settings のパッケージ配置と asmdef を準備する
- [x] 1.1 Core パッケージに RuntimeSettings 用ディレクトリと asmdef 参照を整える
  - `Packages/com.hidano.facialcontrol/Runtime/Adapters/RuntimeSettings/` ディレクトリと `.meta` を作成する
  - 既存の Adapters asmdef がそのまま新規ディレクトリのコードを取り込むことを Editor 起動でコンパイル成功として確認する
  - 観測可能な完了条件: 空の placeholder クラス (削除予定) を置いた状態で Unity の Project ビューに新規ディレクトリが表示され、コンパイルエラーがない
  - _Requirements: 2.1, 2.2_
- [x] 1.2 OSC / LipSync パッケージ側に RuntimeSettings / Devices ディレクトリを整える
  - `Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/RuntimeSettings/`、`Packages/com.hidano.facialcontrol.lipsync/Runtime/Adapters/Devices/` のディレクトリ・`.meta` を作成
  - 既存 asmdef を変更なしで取り込めることをコンパイル成功として確認する
  - 観測可能な完了条件: Unity Project ビューに各ディレクトリが表示され、`.meta` GUID がランダム生成済みで重複しない
  - _Requirements: 2.4, 4.2_
  - _Boundary: com.hidano.facialcontrol.osc, com.hidano.facialcontrol.lipsync_
- [x] 1.3 LipSync テスト用 InternalsVisibleTo と PlayerPrefs Fake 用基盤を整える
  - LipSync ランタイム asmdef に `InternalsVisibleTo` で EditMode / Shared テスト asmdef を公開する
  - `IPlayerPrefsBackend` interface 雛形と Default / Fake backend の骨格を `Tests/Shared` 配下に置く (中身の挙動は 4.x で実装)
  - 観測可能な完了条件: テストアセンブリから internal 型が参照可能で、ビルド警告/エラーなし
  - _Requirements: 4.2, 7.4_
  - _Boundary: com.hidano.facialcontrol.lipsync_

## 2. Core: AdapterRuntimeSettingsBase / Collection

- [x] 2. 親 SO + Sub-asset 共通基底の構築
- [x] 2.1 AdapterRuntimeSettingsBase に `_label` / `_schemaVersion` と ToJson/FromJson 仮想 API を実装する
  - abstract ScriptableObject として Adapters 層に定義し、`_label` (string) と `_schemaVersion` (int、既定値 1) を `[SerializeField]` で保持する
  - 仮想 `ToJson()` / `FromJson(string)` の no-op 既定実装 (warning ログのみ) を実装する
  - 観測可能な完了条件: Base 派生のテスト用サブクラスを生成し、`_schemaVersion == 1`・`Label` getter が動作し、未 override の `ToJson()` が warning を出すことを EditMode テストで確認できる
  - _Requirements: 2.1, 5.1, 5.2, 5.6, 6.7_
  - _Boundary: AdapterRuntimeSettingsBase_
- [x] 2.2 AdapterRuntimeSettingsCollectionSO に List<Base> と TryFind API を実装する
  - `[CreateAssetMenu]` を付与し、`List<AdapterRuntimeSettingsBase> _items` と `IReadOnlyList<>` 公開、`TryFind<T>()` / `TryFind<T>(label)` を提供する
  - `OnEnable()` 内に空の `MigrateOnLoad()` 仮想フックメソッドとマイグレーション拡張点コメントを配置する (要件 5.4-5.5: 実装本体は未着手)
  - 観測可能な完了条件: Collection asset を `ScriptableObject.CreateInstance` で生成し、空 List 状態で `TryFind<T>()` が `null` を返すことを EditMode テストで確認できる
  - _Requirements: 1.3, 2.2, 2.3, 5.4, 5.5_
  - _Boundary: AdapterRuntimeSettingsCollectionSO_
  - _Depends: 2.1_
- [x] 2.3 Collection の sub-asset 操作不変条件と null 要素警告を実装する
  - `OnEnable` で `_items` 中の null 要素を検出して `Debug.LogWarning` を出す
  - 既存 sub-asset の serialize 済み値が `_items` 更新時に影響を受けないことをサポートする補助 API (例: `IndexOf` ヘルパー) を必要に応じて追加する
  - 観測可能な完了条件: null 要素を意図的に含む Collection 生成 → `OnEnable` 経由で `LogAssert.Expect(Warning, ...)` が観測できる EditMode テストが緑になる
  - _Requirements: 3.1, 3.2, 3.3, 3.4_
  - _Boundary: AdapterRuntimeSettingsCollectionSO_
  - _Depends: 2.2_

## 3. Core: OscRuntimeSettingsSO (Receiver/Sender 統合)

- [x] 3. OscRuntimeSettingsSO の実装とラウンドトリップ対応
- [x] 3.1 (P) OscRuntimeSettingsSO に Receiver / Sender セクション両方のフィールドと getter を実装する
  - Receiver セクション (`ListenEndpoint`, `ListenPort`, `StalenessSeconds`, `FailSafeMode`, `ConsistencyCheckWarnLog`, `BundleMode`, `BundleAccumulationTimeoutMs`)、Sender セクション (`Endpoints`, `HeartbeatIntervalSeconds`, `SuppressLoopback`) を `[SerializeField]` + public getter で公開する
  - `[CreateAssetMenu]` は付与しない (sub-asset 専用)
  - 観測可能な完了条件: SO の Inspector で Receiver/Sender 両セクションのフィールドが表示され、EditMode テストで `JsonUtility.ToJson` が両セクションの値を含むことを確認できる
  - _Requirements: 1.3, 2.4, 7.2_
  - _Boundary: OscRuntimeSettingsSO_
  - _Depends: 2.1_
- [x] 3.2 (P) `_receiverEnabled` / `_senderEnabled` トグルと OnAfterDeserialize 正規化を実装する
  - `[SerializeField] private bool _receiverEnabled` / `_senderEnabled` と公開 getter を追加 (要件 2.7 の運用負荷緩和、validate-design Critical Issue #3)
  - `ISerializationCallbackReceiver.OnAfterDeserialize` で `ListenPort` 範囲・`BundleAccumulationTimeoutMs` 正値・`HeartbeatIntervalSeconds` 正値・enum string 正規化を適用する
  - 観測可能な完了条件: 不正値 (port 0, timeout -1 等) を持つ SO を `FromJsonOverwrite` で読み込んだ後、getter が既定値に補正された値を返すことを EditMode テストで確認できる
  - _Requirements: 2.7, 5.3_
  - _Boundary: OscRuntimeSettingsSO_
  - _Depends: 2.1_
- [x] 3.3 OscRuntimeSettingsSO の ToJson/FromJson を override し JSON ラウンドトリップを実装する
  - `JsonUtility.ToJson(this, true)` をベースに、enum (`FailSafeMode` / `BundleInterpretationMode`) を文字列フィールドとして書き出す既存 DTO ロジックを移植する
  - `FromJson` は `JsonUtility.FromJsonOverwrite` + `OnAfterDeserialize` で defaults を再適用する
  - 観測可能な完了条件: 全フィールドを設定した SO を `ToJson` → 新規 SO に `FromJson` で復元したとき全フィールドが一致する EditMode テストが緑になる
  - _Requirements: 5.3, 7.3_
  - _Boundary: OscRuntimeSettingsSO_
  - _Depends: 3.1, 3.2_

## 4. Core: LipSyncDeviceStore (PlayerPrefs ラッパー)

- [x] 4. PlayerPrefs ベースの DeviceStore を実装する
- [x] 4.1 (P) IPlayerPrefsBackend と DefaultPlayerPrefsBackend を実装する
  - `IPlayerPrefsBackend` を internal interface として定義し、`GetString` / `GetInt` / `SetString` / `SetInt` / `Save` を契約化する
  - `DefaultPlayerPrefsBackend` で `UnityEngine.PlayerPrefs` をラップする
  - 観測可能な完了条件: テストから `DefaultPlayerPrefsBackend` のメソッドを通じて PlayerPrefs アクセスがプロキシされていることを EditMode テストで確認できる (実書込み回避のため Fake と切替検証する形)
  - _Requirements: 4.6, 7.4_
  - _Boundary: LipSyncDeviceStore_
  - _Depends: 1.3_
- [x] 4.2 (P) FakePlayerPrefsBackend を Shared テスト基盤に実装する
  - in-memory dictionary で `IPlayerPrefsBackend` を実装し、`Tests/Shared` 配下に置く
  - `[SetUp]` / `[TearDown]` で backend を差し替え/リセットする基底クラス雛形を提供する
  - 観測可能な完了条件: Fake backend に対する Get/Set のラウンドトリップ EditMode テストが緑で、実 PlayerPrefs に書き込まれないことを LogAssert / PlayerPrefs キー存在チェックで確認できる
  - _Requirements: 7.4_
  - _Boundary: LipSyncDeviceStore Tests_
  - _Depends: 1.3_
- [x] 4.3 LipSyncDeviceStore の静的 Load/Save API と Backend 差し替えフックを実装する
  - キー定数 (`Hidano.FacialControl.LipSync.MicDevice.Name` / `...Disambiguator`) を const として公開
  - `Load()` でキー欠落時に `DeviceDescriptor` の既定値 (DeviceName="", Disambiguator=0) を返し例外を投げない
  - `Save(DeviceDescriptor)` で `DeviceName` null を空文字に正規化し、内部で `backend.Save()` を呼ぶ
  - `SetBackend` / `ResetBackend` を internal で公開
  - 観測可能な完了条件: Fake backend 差替え後の Save→Load ラウンドトリップ、キー欠落時の既定値返却、null DeviceName 正規化のいずれも EditMode テストで緑になる
  - _Requirements: 1.4, 4.2, 4.4, 4.5, 4.6, 7.4_
  - _Boundary: LipSyncDeviceStore_
  - _Depends: 4.1, 4.2_

## 5. Core: AdapterBinding 改修 (OSC / ULipSync)

- [x] 5. AdapterBinding を SettingsSO / DeviceStore 参照経路に切り替える
- [x] 5.1 (P) OscReceiverAdapterBinding から環境依存フィールドを削除し OscRuntimeSettingsSO 参照に切り替える
  - 削除: `_endpoint`, `_port`, `_stalenessSeconds`, `_failSafeMode`, `_bundleMode`, `_bundleAccumulationTimeoutMs`, `_consistencyCheckWarnLog`
  - 追加: `[SerializeField] private OscRuntimeSettingsSO _settings`
  - `OnStart` で `_settings == null` or `_settings.ReceiverEnabled == false` の場合は warning + skip、それ以外は `_settings` の Receiver セクションを `OscReceiverHost` に流し込む
  - `_mappings` (キャラ依存) は引き続き保持する
  - 観測可能な完了条件: `_settings` 未代入時に warning が出て binding 起動がスキップされる EditMode テスト、`_settings` 経由で Host が configure される PlayMode テストの両方が緑
  - _Requirements: 1.1, 1.2, 2.5, 2.7_
  - _Boundary: OscAdapterBinding_
  - _Depends: 3.3_
- [x] 5.2 (P) OscSenderAdapterBinding から環境依存フィールドを削除し OscRuntimeSettingsSO 参照に切り替える
  - 削除: `_endpoints`, `_heartbeatIntervalSeconds`, `_suppressLoopback`
  - 追加: `[SerializeField] private OscRuntimeSettingsSO _settings`
  - `OnStart` で `_settings == null` or `_settings.SenderEnabled == false` の場合 warning + skip、それ以外は Sender セクションを Sender Host に流し込む
  - `_blendShapeNames` / `_gazeExpressionIds` は据え置き
  - 観測可能な完了条件: Sender 系の起動分岐 (skip / configure) が EditMode + PlayMode で検証可能
  - _Requirements: 1.1, 1.2, 2.6, 2.7_
  - _Boundary: OscSenderAdapterBinding_
  - _Depends: 3.3_
- [x] 5.3 (P) ULipSyncAdapterBinding から `_deviceDescriptor` SerializeField を削除し DeviceStore 経由読み込みに切り替える
  - 削除: `[SerializeField] DeviceDescriptor _deviceDescriptor`
  - 追加: `[NonSerialized]` runtime descriptor キャッシュ
  - `OnStart` 冒頭で `LipSyncDeviceStore.Load()` を呼び、`Configure(DeviceDescriptor)` で外部から渡された値があればそれを優先する診断パスを保持する
  - `_analyzerProfile` / `_phonemeEntries` / `_targetMeshHint` / `_maxWeightScale` は据え置き
  - 観測可能な完了条件: DeviceStore に保存された DeviceName が `OnStart` で読み出され、`uLipSyncMicrophone` 初期化に伝播することを PlayMode テストで観測できる
  - _Requirements: 1.2, 4.1, 4.3_
  - _Boundary: ULipSyncAdapterBinding_
  - _Depends: 4.3_

## 6. Editor: PropertyDrawer 改修と CollectionEditor 新規実装

- [x] 6. Inspector / Editor 拡張の整備
- [x] 6.1 (P) OscReceiverAdapterBindingDrawer / OscSenderAdapterBindingDrawer を改修して SettingsSO ObjectField に差し替える
  - 削除した環境依存フィールドの inline 編集 UI を取り除き、`OscRuntimeSettingsSO` 用 ObjectField を追加する
  - `_settings == null` の場合に「未設定時は OSC Adapter は起動しない」HelpBox を表示する
  - validate-design Critical Issue #2 暫定対応として「sub-asset 削除でこの参照が null になる可能性あり」の HelpBox 警告を `_settings == null` 表示と統合する
  - 観測可能な完了条件: Drawer を含む Inspector を起動し、SettingsSO 割当て有/無で HelpBox 表示が切り替わることを EditMode (Editor) テスト or 手動確認で観測できる
  - _Requirements: 2.5, 2.6, 3.3, 6.4_
  - _Boundary: OscReceiverAdapterBindingDrawer, OscSenderAdapterBindingDrawer_
  - _Depends: 5.1, 5.2_
- [x] 6.2 (P) ULipSyncAdapterBindingDrawer から `_deviceDescriptor` UI を取り除き DeviceStore 連動の DeviceDescriptorPopup を結線する
  - SerializedProperty 経路をやめて、Editor 起動時に `LipSyncDeviceStore.Load()` した値を VisualElement にバインドし、変更時に `Save` を呼ぶ設計に置き換える
  - 未選択時の挙動を HelpBox で表示する
  - 観測可能な完了条件: Inspector で DeviceDescriptorPopup を操作すると PlayerPrefs (テスト時は Fake backend) に値が反映されることを観測できる
  - _Requirements: 4.1, 4.5_
  - _Boundary: ULipSyncAdapterBindingDrawer_
  - _Depends: 5.3_
- [x] 6.3 AdapterRuntimeSettingsTypeRegistry を Editor 専用 helper として実装する
  - `TypeCache.GetTypesDerivedFrom<AdapterRuntimeSettingsBase>().Where(t => !t.IsAbstract)` を helper としてラップ
  - displayName は `[CreateAssetMenu]` の menuName か型名から導出する
  - 観測可能な完了条件: EditMode (Editor) テストで Registry が `OscRuntimeSettingsSO` を列挙し、abstract Base は含まないことを確認できる
  - _Requirements: 6.2_
  - _Boundary: AdapterRuntimeSettingsTypeRegistry_
  - _Depends: 2.1, 3.1_
- [x] 6.4 AdapterRuntimeSettingsCollectionEditor (UI Toolkit) を実装し sub-asset 追加/削除 UI を提供する
  - `[CustomEditor(typeof(AdapterRuntimeSettingsCollectionSO))]` で `CreateInspectorGUI()` をオーバーライドし、UI Toolkit で構築
  - Add ボタン: `AdvancedDropdown` / `GenericMenu` で TypeRegistry の型一覧を表示し、選択時に `CreateInstance` → `AddObjectToAsset` → `_items.InsertArrayElementAtIndex` → `SaveAssets`
  - Remove ボタン: 確認ダイアログ → `RemoveObjectFromAsset` → `_items.RemoveAt` → `SaveAssets`
  - 同型 sub-asset の複数登録を許可しつつ、`_label` 重複時は `Debug.LogWarning` を出す
  - `Undo.RecordObject` で操作を undo スタックに積む
  - 観測可能な完了条件: Editor テストで Add/Remove ボタン経由で sub-asset の登録・解除と他 sub-asset 値保持を観測でき、重複 _label で warning が出ることを LogAssert で検証できる
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.8_
  - _Boundary: AdapterRuntimeSettingsCollectionEditor_
  - _Depends: 2.3, 6.3_

## 7. Migration: 既存 Asset / テスト Fixtures 再構築

- [x] 7. 既存 Asset とテストフィクスチャの新構造移行
- [x] 7.1 既存 `*Profile.asset` と Samples~ 内の fixture を新構造に再構築する
  - 各 `FacialCharacterProfileSO` Asset を開き、新規 `AdapterRuntimeSettingsCollection.asset` を作成
  - `OscRuntimeSettingsSO` sub-asset を Collection に Add し、旧 `_endpoint` / `_port` / 各種値を転記
  - `OscReceiverAdapterBinding._settings` / `OscSenderAdapterBinding._settings` に新 SO をアサイン
  - `_deviceDescriptor` 旧値はテスト初期化スクリプト経由で `LipSyncDeviceStore.Save` 相当に変換し、PlayerPrefs (Fake) シナリオに移す
  - 観測可能な完了条件: 移行後の Project を Unity で開き、既存 PlayMode 起動シーンが新構造で正常に起動し、`_settings` 未設定の binding が無いことを Project 内検索で確認できる
  - _Requirements: 1.5, 8.5_
  - _Depends: 5.1, 5.2, 5.3, 6.4_

## 8. Tests: EditMode / PlayMode カバレッジ整備

- [x] 8. テストカバレッジを新構造に揃える
- [x] 8.1 (P) AdapterRuntimeSettingsCollectionSO の sub-asset 追加/削除でパラメータ消失が無いことを検証する EditMode テストを追加する
  - 既存 sub-asset の `_label` / `_schemaVersion` を読み、追加/削除後に値が保持される
  - 中央の 1 つを Remove して残りに影響しないことを確認する
  - 観測可能な完了条件: 該当 EditMode テスト 2 件以上が緑、`{Target}Tests` / `{Method}_{Condition}_{Expected}` 命名規則に準拠
  - _Requirements: 3.1, 3.3, 7.1, 7.8_
  - _Boundary: AdapterRuntimeSettingsCollectionSOTests_
  - _Depends: 2.3_
- [x] 8.2 (P) OscRuntimeSettingsSO の Serialize/Deserialize と JSON ラウンドトリップ EditMode テストを追加する
  - `JsonUtility.ToJson` ↔ `FromJsonOverwrite` で全フィールドが保持されること
  - enum 文字列正規化 (FailSafeMode / BundleInterpretationMode) と OnAfterDeserialize による defaults 適用
  - 観測可能な完了条件: ラウンドトリップ EditMode テストが緑、enum 文字列出力が固定文字列であることをアサート
  - _Requirements: 5.3, 7.2, 7.3, 7.8_
  - _Boundary: OscRuntimeSettingsSOTests, OscRuntimeSettingsJsonRoundTripTests_
  - _Depends: 3.3_
- [x] 8.3 (P) LipSyncDeviceStore の Fake backend 経由 EditMode テストを追加する
  - キー欠落時に既定値返却 (DeviceName="", Disambiguator=0)
  - Save→Load のラウンドトリップで同値返却
  - null DeviceName の正規化、`SetBackend` / `ResetBackend` の差替え動作
  - 観測可能な完了条件: 全テストが緑で、実 PlayerPrefs に書き込みが発生しないことを PlayerPrefs キー直接照会で確認できる
  - _Requirements: 4.2, 4.4, 4.5, 7.4, 7.8_
  - _Boundary: LipSyncDeviceStoreTests_
  - _Depends: 4.3_
- [x] 8.4 (P) AdapterRuntimeSettingsCollectionEditor の Add/Remove と _label 重複 warning を EditMode (Editor) テストで検証する
  - Add ボタン経由で `AssetDatabase` 上に sub-asset が生成される
  - Remove ボタン経由で対象 sub-asset のみが除去される
  - 同 _label の sub-asset を追加すると `Debug.LogWarning` が観測される (LogAssert)
  - 観測可能な完了条件: 3 ケース以上の Editor テストが緑
  - _Requirements: 3.1, 3.3, 6.3, 6.4, 6.8, 7.1_
  - _Boundary: AdapterRuntimeSettingsCollectionEditorTests_
  - _Depends: 6.4_
- [x] 8.5 (P) OscReceiverAdapterBinding / OscSenderAdapterBinding が SettingsSO 経由で UDP 送受信できることを PlayMode で検証する
  - Receiver: 実 UDP で OSC メッセージを受信し、`_settings.ListenPort` 経由で port が反映される
  - Sender: `_settings.Endpoints` 経由で UDP 送信が行われる
  - Receiver/Sender が同一 SO を参照したとき設定が一貫することを別ケースで検証 (要件 2.7)
  - 観測可能な完了条件: 3 ケース以上の PlayMode テストが緑
  - _Requirements: 2.5, 2.6, 2.7, 7.5, 7.8_
  - _Boundary: OscReceiverAdapterBindingSettingsReferenceTests_
  - _Depends: 5.1, 5.2_
- [x] 8.6 (P) ULipSyncAdapterBinding が DeviceStore 経由 DeviceName でマイク初期化フローを開始することを PlayMode で検証する
  - `LipSyncDeviceStore.Save` で DeviceName を投入後、binding `OnStart` で `uLipSyncMicrophone` が当該 DeviceName で初期化される
  - Fake backend をテストフィクスチャに適用し、テスト後の状態リセットを `[TearDown]` で行う
  - 観測可能な完了条件: PlayMode テストが緑で、Fake backend 経由で実 PlayerPrefs に書き込みが発生しないこと
  - _Requirements: 4.3, 7.6, 7.8_
  - _Boundary: ULipSyncAdapterBindingDeviceStoreTests_
  - _Depends: 5.3_
- [x] 8.7 既存テスト群の旧フィールド参照を全て新構造に書き換える
  - キャラ SO 直下の `_endpoint` / `_port` / `_deviceDescriptor` 等を参照していた既存テストを洗い出し、`OscRuntimeSettingsSO` インスタンス生成 + `Configure` API 経路、または `LipSyncDeviceStore.Save` 経路に書き換える
  - 旧フィールド名の参照が grep でゼロになることを観測可能な完了条件とする
  - 既存の GC アロケーションテスト (`OscReceiverGCAllocationTests` / `OscSenderGCAllocationTests` / `EndToEndGcAllocationTests`) を新経路で再実行し緑であることを確認する
  - _Requirements: 7.7, 7.8_
  - _Depends: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6_

## 9. Documentation & Backlog 更新

- [x] 9. ドキュメントと backlog を最終化する
- [x] 9.1 Documentation~ にマイグレーションと運用ガイドを追記する
  - `Packages/com.hidano.facialcontrol/Documentation~/` または該当パッケージ Documentation~ に、`AdapterRuntimeSettingsCollection.asset` の作成手順・sub-asset 追加/削除フロー・`_receiverEnabled`/`_senderEnabled` の使い分け・PlayerPrefs キー名一覧を Markdown で記述する
  - 既存 Asset を手動再構築する手順 (Migration Strategy) を箇条書きで残す
  - 観測可能な完了条件: Documentation~ に新規/更新 Markdown が存在し、`_receiverEnabled` / `_senderEnabled` / PlayerPrefs キーが文書化されていることを目視確認できる
  - _Requirements: 2.7, 8.5_
- [x] 9.2 docs/backlog.md にスコープ外項目と将来マイグレーションを記録する
  - 対応レベル c (型削除/リネーム時の自動マイグレーション) を別 spec として backlog に登録 (要件 8.1)
  - Receiver/Sender 統合解消の検討、Sub-asset 削除時の逆引き確認ダイアログ (validate-design Critical Issue #2 の (a) 案) を backlog に追記
  - `MigrateOnLoad` の本実装、`_schemaVersion` 増分規約策定を backlog に明記
  - 観測可能な完了条件: docs/backlog.md に該当 3 項目以上が追記されていることを diff で確認できる
  - _Requirements: 5.5, 8.1, 8.2, 8.3, 8.4, 8.6_

## Requirements Coverage Matrix

| 要件 ID | カバータスク |
|---------|--------------|
| 1.1 | 5.1, 5.2 |
| 1.2 | 5.1, 5.2, 5.3 |
| 1.3 | 2.2, 3.1 |
| 1.4 | 4.3 |
| 1.5 | 7.1 |
| 2.1 | 0.1, 1.1, 2.1 |
| 2.2 | 1.1, 2.2 |
| 2.3 | 2.2 |
| 2.4 | 1.2, 3.1 |
| 2.5 | 5.1, 6.1, 8.5 |
| 2.6 | 5.2, 6.1, 8.5 |
| 2.7 | 3.2, 5.1, 5.2, 8.5, 9.1 |
| 3.1 | 2.3, 6.4, 8.1, 8.4 |
| 3.2 | 2.3, 6.4 |
| 3.3 | 0.2, 2.3, 6.1, 6.4, 8.1, 8.4 |
| 3.4 | 2.3, 6.4 |
| 3.5 | 9.2 |
| 4.1 | 5.3, 6.2 |
| 4.2 | 1.2, 4.3, 8.3 |
| 4.3 | 5.3, 8.6 |
| 4.4 | 4.3, 8.3 |
| 4.5 | 4.3, 6.2, 8.3 |
| 4.6 | 4.1, 4.3 |
| 5.1 | 2.1 |
| 5.2 | 2.1 |
| 5.3 | 3.2, 3.3, 8.2 |
| 5.4 | 2.2 |
| 5.5 | 2.2, 9.2 |
| 5.6 | 2.1 |
| 6.1 | 6.4 |
| 6.2 | 6.3, 6.4 |
| 6.3 | 6.4, 8.4 |
| 6.4 | 0.2, 6.1, 6.4, 8.4 |
| 6.5 | 6.4 |
| 6.6 | 6.4 |
| 6.7 | 2.1 |
| 6.8 | 6.4, 8.4 |
| 7.1 | 8.1, 8.4 |
| 7.2 | 8.2 |
| 7.3 | 3.3, 8.2 |
| 7.4 | 1.3, 4.1, 4.2, 4.3, 8.3 |
| 7.5 | 5.1, 5.2, 8.5 |
| 7.6 | 5.3, 8.6 |
| 7.7 | 8.7 |
| 7.8 | 8.1, 8.2, 8.3, 8.5, 8.6 |
| 8.1 | 9.2 |
| 8.2 | 9.2 |
| 8.3 | 9.2 |
| 8.4 | 9.2 |
| 8.5 | 7.1, 9.1 |
| 8.6 | 9.2 |
