# Adapter Runtime Settings 調査ログ

## Summary
- **Feature**: `adapter-runtime-settings`
- **Discovery Scope**: Extension (既存 Adapter 層の責務再配置リファクタリング)
- **Key Findings**:
  - 要件 (`requirements.md`) で配置方針・実装方式 (方式 3: 親 SO + Sub-asset SO) ・フィールド分類 (Character / Environment / Machine) ・対応レベル b/c が既に確定している。設計では「方式 3 のどう実装するか」が中心議論
  - `AdapterBindingBase` は Domain 層配置で UnityEngine 参照を持たないが、具象 binding (Adapters 層) は `[SerializeField]` で SO 参照可能。`[SerializeReference]` ではなく通常の `[SerializeField]` で SO 参照を持てる (ScriptableObject は polymorphic 参照に通常の SerializeField が使える)
  - Unity の sub-asset 機構 (`AssetDatabase.AddObjectToAsset` / `RemoveObjectFromAsset`) は対象 sub-asset のみを操作するため、要件 3.3 (削除時に他の sub-asset 値を維持) を **追加実装なし** で満たせる
  - 既存 `OscReceiverOptionsDto` / `OscSenderOptionsDto` (JSON Profile 経路) には `ToJson` / `FromJson` の正規化ロジックが既にあり、`OscRuntimeSettingsSO.ToJson/FromJson` への移植元として再利用可能
  - PlayerPrefs テスト時の実書き込み回避は、業界標準で「backend interface + Fake 実装」が確立。Unity 公式に専用テスト helper は無いため、internal interface + `InternalsVisibleTo` で実現する

## Research Log

### Topic 1: Unity Sub-Asset (AddObjectToAsset) の挙動と要件 3.3 適合性
- **Context**: 要件 3.1-3.3 が「sub-asset 追加/削除でパラメータ消失が発生しないこと」を要求している。本機能の安全性が標準 API に立脚して説明できることを確認する必要があった
- **Sources Consulted**:
  - Unity ScriptingReference: `AssetDatabase.AddObjectToAsset(Object asset, Object assetObject)` — 子 Object を指定 Asset の sub として登録
  - Unity ScriptingReference: `AssetDatabase.RemoveObjectFromAsset(Object asset)` — 指定 Object を Asset から切り離す (Project 上の Asset そのものは削除しない)
  - 既存プロジェクト内の `[SerializeReference]` 使用パターン (`FacialCharacterProfileSO._adapterBindings`)
- **Findings**:
  - `RemoveObjectFromAsset` は **対象 sub-asset 1 つに対してのみ作用** する。他の sub-asset の serialize 済み値は完全に保持される
  - 親 SO の `_items` List から該当エントリを除去するのは別操作 (両方を行う必要あり)
  - sub-asset は Project ビューでフォールドアイコン付きでツリー表示される (要件 2.3 に合致)
  - sub-asset 自身は `[CreateAssetMenu]` を持たないことが推奨 (独立 Asset としても作れてしまうと sub-asset 専用設計と矛盾)
- **Implications**:
  - 設計の「Sub-asset 追加/削除フロー」は標準 Unity API のみで実装可能、独自実装によるパラメータ消失リスクを最小化できる
  - `OscRuntimeSettingsSO` は `[CreateAssetMenu]` を付けない方針で設計

### Topic 2: ScriptableObject の SerializeField による polymorphic 参照
- **Context**: `OscReceiverAdapterBinding._settings` を Collection ではなく具象 SO (`OscRuntimeSettingsSO`) で持つことの可否を確認
- **Sources Consulted**:
  - Unity ScriptingReference: Script Serialization — ScriptableObject 派生型は通常の `[SerializeField]` で参照可能
  - 既存パターン: `FacialCharacterProfileSO._referenceModel` (GameObject 参照) 等
- **Findings**:
  - ScriptableObject 派生型を `[SerializeField]` するのは Unity の常套手段。`[SerializeReference]` は **マネージドオブジェクト (`[Serializable]` plain C# class)** の polymorphic 直列化用であり、`ScriptableObject` 系には不要
  - sub-asset であっても、別 Asset であっても、参照経路は等価
- **Implications**:
  - `OscReceiverAdapterBinding._settings` は `[SerializeField] private OscRuntimeSettingsSO _settings` で実装する。`AdapterRuntimeSettingsBase` への参照 (interface 風) ではなく具象型参照のほうが IDE 補完と型安全性で勝る

### Topic 3: TypeCache による派生型探索 (Editor)
- **Context**: 要件 6.2 が「型一覧から追加対象を選択する UI」を要求。Reflection ベースの全 assembly スキャンは遅いので Unity 標準の高速代替を探した
- **Sources Consulted**:
  - Unity ScriptingReference: `UnityEditor.TypeCache.GetTypesDerivedFrom<T>()` — Domain Reload 時に事前計算された型カタログから O(1) 取得
- **Findings**:
  - `TypeCache.GetTypesDerivedFrom<AdapterRuntimeSettingsBase>()` は Domain Reload 後に最初の呼び出しでも瞬時に返る
  - abstract 型を除外するためには `.Where(t => !t.IsAbstract)` を後段でかける
  - 新規 adapter package を追加した場合、Unity の Domain Reload (script recompile) 後に自動で型が追加される
- **Implications**:
  - `AdapterRuntimeSettingsTypeRegistry` を Editor 専用 helper として導入し、TypeCache をラップする。Runtime に依存させない

### Topic 4: PlayerPrefs テスト時の実書き込み回避戦略
- **Context**: 要件 7.4 が「実 PlayerPrefs に書き込まない方式」を要求。具体的方式は「interface 抽象 / internal backend 差し替え / static reset 等、具体的方式は設計フェーズで決定」と委ねられている
- **Sources Consulted**:
  - Unity Test Framework 公式 (com.unity.test-framework 1.6): PlayerPrefs 専用テスト helper は提供されていない
  - 業界慣用パターン: PlayerPrefs ラッパー interface + Fake backend (例: `PlayerPrefsX`, `BetterPlayerPrefs` 系 OSS)
  - 既存プロジェクト: `IAsioDriverEnumerator` / `IMicrophoneDeviceEnumerator` で Default + Fake パターンが確立済み
- **Findings**:
  - 3 つの候補:
    1. **interface 抽象** (`IPlayerPrefsBackend` を `LipSyncDeviceStore` の依存性として注入): 静的 API を維持しつつテストで Fake を差し替え可能
    2. **static reset** (`PlayerPrefs.DeleteKey` をテスト前後で呼ぶ): 実書き込みは発生するため要件 7.4 に違反
    3. **テスト用 PREFIX 差し替え** (キー名に `Test_` をつける): テストフィクスチャから本番キーへの汚染リスクは下がるが、実 PlayerPrefs 書き込みは発生する
  - 候補 1 が要件 7.4 (実 PlayerPrefs に書き込まない) を満たす唯一の方式
- **Implications**:
  - `LipSyncDeviceStore` は `IPlayerPrefsBackend` を internal interface として持ち、本番では `DefaultPlayerPrefsBackend` (UnityEngine.PlayerPrefs ラッパー)、テストでは `FakePlayerPrefsBackend` を `SetBackend()` で差し替える
  - `internal` + `InternalsVisibleTo` (LipSync.Tests.EditMode / LipSync.Tests.Shared) で公開する

### Topic 5: JsonUtility と enum 文字列化
- **Context**: 要件 5.3 が `OscRuntimeSettingsSO.ToJson` / `FromJson` で全フィールドのラウンドトリップを要求。JsonUtility は enum を int で書き出すデフォルト挙動だが、既存 DTO は文字列で書き出している
- **Sources Consulted**:
  - Unity ScriptingReference: `JsonUtility.ToJson` / `FromJsonOverwrite`
  - 既存実装: `OscReceiverOptionsDto.ToFailSafeModeString` / `OscReceiverOptionsDto.ToBundleModeString`
- **Findings**:
  - `JsonUtility` は enum を **整数値** で書き出す。文字列化したい場合は intermediate string field を持ち、enum との変換を手動で行う必要がある
  - 既存 DTO は `string failSafeMode = "revertToBase"` / `string bundleMode = "atomicSwap"` といった string フィールドを `ISerializationCallbackReceiver` で正規化する設計
- **Implications**:
  - `OscRuntimeSettingsSO` は public な properties で enum を返しつつ、シリアライズ用フィールドは string で保持し、`OnAfterDeserialize` で normalize する
  - 既存 `OscReceiverOptionsDto` のロジックを共通 helper (`OscEnumNormalizer` 等) として切り出して両者で共有する案もあるが、DTO 経路 (JSON Profile) と SO 経路は依存方向が独立しているため、まずは個別実装で OK。重複が顕在化したら follow-up でリファクタ

### Topic 6: AdapterRuntimeSettingsBase の配置 asmdef (Adapters か Domain か)
- **Context**: 要件 2.1 で「`Hidano.FacialControl.Adapters` 名前空間に定義」と明記。実体 asmdef を Domain にすべきか Adapters にすべきかを確認
- **Sources Consulted**:
  - 要件 2.1: 名前空間は `Hidano.FacialControl.Adapters`
  - steering の `structure.md`: Domain は Unity 非依存 (`Unity.Collections` のみ可)
  - 既存 `AdapterBindingBase` の配置: Domain 層 (UnityEngine 不使用)
- **Findings**:
  - `AdapterRuntimeSettingsBase` は `UnityEngine.ScriptableObject` を継承する **必要がある** (要件 2.1)。これは Unity Engine 依存
  - したがって Domain (Unity 非依存) には配置不可。Adapters 層 (Unity 依存) に置く
- **Implications**:
  - `AdapterRuntimeSettingsBase` / `AdapterRuntimeSettingsCollectionSO` は `Hidano.FacialControl.Adapters` asmdef 配下に配置 (具体的には `Packages/com.hidano.facialcontrol/Runtime/Adapters/RuntimeSettings/`)
  - 名前空間は `Hidano.FacialControl.Adapters` (要件適合)。`Hidano.FacialControl.Adapters.RuntimeSettings` のサブ名前空間化は任意

### Topic 7: LipSync 既存 binding lifecycle と DeviceDescriptor 削除影響
- **Context**: `_deviceDescriptor` を削除すると `Configure(DeviceDescriptor ...)` API、`SwapDevice`、`ULipSyncAdapterBindingDrawer` の DeviceDescriptorPopup などへの影響範囲を確認
- **Sources Consulted**:
  - `ULipSyncAdapterBinding.cs` (現行 760 行)
  - `ULipSyncAdapterBindingDrawer.cs` (DeviceDescriptorPopup 結線部)
  - `DeviceResolver.cs` / `DefaultMicrophoneDeviceEnumerator.cs`
- **Findings**:
  - `Configure(DeviceDescriptor ...)` は内部で `_deviceDescriptor` に代入していた → `_deviceDescriptor` を非 serialize の `[NonSerialized]` フィールドに退化させれば API シグネチャを維持できる
  - `SwapDevice(deviceName, disambiguatorIndex)` は既に `DeviceDescriptor` を内部で構築している → DeviceStore 連動で `LipSyncDeviceStore.Save` を呼ぶように追加 (要件 4.5)
  - Drawer は `_deviceDescriptor` を `FindPropertyRelative` で参照しているため、削除に合わせて Drawer 側も DeviceStore 直読みに変更する
- **Implications**:
  - `_deviceDescriptor` 削除は `[NonSerialized]` 化 + 既定値読み出しを `LipSyncDeviceStore.Load()` に置き換えることでテスト互換性を保てる
  - Drawer は SerializedProperty 経路ではなく、Editor 起動時に `LipSyncDeviceStore.Load()` した値を VisualElement にバインドし、変更時に `Save` する設計

### Topic 8: Receiver/Sender 統合 vs 分離の比較 (要件 2.4 の根拠確認)
- **Context**: 要件 2.4 が「Receiver/Sender を 1 つの SO に統合」と明記しているが、設計者として「同一 SO 統合の利点と懸念」を理解しておく必要があった
- **Findings**:
  - 統合派の利点:
    - VRChat 互換のように Receiver/Sender 両方を有効化するユースケースで「1 つの SO を割当てれば両方設定済み」となり、operator UX が高い
    - 要件 2.7 (Receiver/Sender 両方が同じ SO で一貫した設定値を読む) を SO 1 つで自然に表現
  - 分離派の利点:
    - 役割分離が明確で SRP に近い
    - Receiver のみ運用するケース (例: モブキャラの観測用) で Sender セクションが意味不明な値で残らない
  - 統合派の懸念は要件 6.6 (同型 sub-asset 複数登録 OK) で緩和される → Sender セクションを空のままにする「Receiver 専用 SO」と「Sender 専用 SO」を別 sub-asset として並べる運用は可能
- **Implications**:
  - 要件 2.4 に従い統合する。フィールドアクセス時の懸念は `OscReceiverAdapterBinding` / `OscSenderAdapterBinding` がそれぞれ自分のセクションだけ参照するため解消
  - Drawer 側で Receiver セクション / Sender セクションのフォールドアウト分割を Inspector UX として提供

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| 方式 1: 個別 SettingsSO | binding ごとに別 Asset (`OscReceiverSettings.asset`, `OscSenderSettings.asset`, ...) | 単純、責務明快 | Project ビューに大量ファイル、git diff も拡散 | 要件で却下 |
| 方式 2: 単一巨大 SettingsSO | 全 adapter の設定を 1 Asset の inline フィールドで保持 | 1 ファイルで完結 | 型追加で既存 Asset を破壊しがち、SRP 違反 | 要件で却下 |
| **方式 3 (採用)**: 親 SO + Sub-asset 集約 | Collection SO に sub-asset として SettingsSO 群を内包 | 1 ファイル化 + 型分離 + Unity 標準 sub-asset 機構による安全性 | sub-asset の取り回しに Editor UI が必須 | 要件で採用、本 spec の中心 |
| 方式 4: JSON ファイル直編集 | StreamingAssets/JSON に運用設定を保持 | git 外管理可能 | 型安全性なし、UI なし、binding 起動が JSON 経路依存 | 別経路 (Character JSON) と統合判断必要、スコープ外 |
| 方式 5: ScriptableSingleton | Editor 全体で 1 つの設定実体を共有 | グローバル設定向き | binding 単位の差し替え不可、プロジェクト共有不可 | キャラ複数体・複数環境のユースケースに不適合 |

## Design Decisions

### Decision: Sub-asset 配置を方式 3 で実装
- **Context**: 要件 2 で方式 3 が指定されている。設計では「具象実装方針」を確定する必要がある
- **Alternatives Considered**:
  1. 方式 3 + Collection 経由参照 — binding は Collection を参照し `TryFind<OscRuntimeSettingsSO>()` 等で sub-asset を解決
  2. 方式 3 + 具象 sub-asset 直接参照 — binding は `[SerializeField] OscRuntimeSettingsSO _settings` で sub-asset を直接参照
- **Selected Approach**: 方式 3 + 具象 sub-asset 直接参照 (alternative 2)
- **Rationale**:
  - 要件 2.5/2.6 で「`OscReceiverAdapterBinding` shall `OscRuntimeSettingsSO` への参照を `[SerializeField]` で 1 つ保持」と明記されている
  - Collection 経由参照だと runtime での sub-asset lookup が必要になり、起動コスト・型誤接続リスクが増える
  - sub-asset を直接参照しても、Unity の Asset 依存解決は親 Collection を含めて Asset を読み込むため、参照経路の安全性は同等
- **Trade-offs**:
  - Pro: 起動経路が「`_settings.X`」の単純な参照のみ、型安全
  - Con: 利用者が「Collection 1 ファイル運用」を期待しても、binding 側は各 sub-asset を個別に指定する必要がある → Drawer の HelpBox で運用説明
- **Follow-up**: Drawer に「Collection から sub-asset を選択するピッカー」の UX 改善を将来検討 (本 spec ではプレーンな ObjectField)

### Decision: マイグレーション拡張点は `OnEnable` 内のフックメソッド予約
- **Context**: 要件 5.4 が「`OnEnable()` 内にマイグレーション拡張点 (空のフックメソッド or 明示的なコメントブロック) を予約」と要求
- **Alternatives Considered**:
  1. `protected virtual void MigrateOnLoad() { }` を `OnEnable` 末尾で呼ぶ
  2. 明示コメントブロック `// TODO(future-migration): _schemaVersion を読んで分岐` のみ配置
  3. `[InitializeOnLoadMethod]` で起動時に Asset Database スキャン
- **Selected Approach**: alternative 1 (`MigrateOnLoad` 仮想メソッド予約) + alternative 2 (コメントブロック併設) のハイブリッド
- **Rationale**:
  - 仮想メソッドだけだとサブクラス開発者が見落とす可能性がある → コメントで方針も明記
  - alternative 3 はパフォーマンス影響大、ユーザーが Editor 起動するたびに全 Asset スキャンになる → 採用しない
- **Trade-offs**:
  - Pro: 将来の対応レベル c 実装時に override 1 行で済む構造
  - Con: 現時点では空の仮想メソッドのため YAGNI 違反気味だが、要件 5.4 を satisfy する最低限のコスト
- **Follow-up**: 将来別 spec で対応レベル c を実装する際、`MigrateOnLoad` の override 実装と _schemaVersion 増分規約を策定

### Decision: PlayerPrefs テストは internal interface 差し替え方式
- **Context**: 要件 7.4 の「実 PlayerPrefs に書き込まない」を満たすテスト戦略を確定する必要があった
- **Alternatives Considered**:
  1. `IPlayerPrefsBackend` interface + `SetBackend/ResetBackend` 静的フック
  2. テスト前後に `PlayerPrefs.DeleteKey` で実書き込みをクリーンアップ
  3. キー名に test prefix を付与してテスト用名前空間で書き込み
- **Selected Approach**: alternative 1
- **Rationale**:
  - alternative 2/3 は要件 7.4 (実 PlayerPrefs に書き込まない) を満たさない
  - alternative 1 は既存プロジェクトの `IAsioDriverEnumerator` / Fake パターンと整合
- **Trade-offs**:
  - Pro: 完全に in-memory でテスト可能、CI ランナーの PlayerPrefs を汚染しない
  - Con: 静的 backend のため、複数テスト間で確実な reset 規約が必要
- **Follow-up**: テスト base class (`LipSyncDeviceStoreTestBase`) を用意して `[SetUp]/[TearDown]` で backend を保証

### Decision: enum はシリアライズ層で string 化
- **Context**: 要件 7.3 の JSON ラウンドトリップでバージョンセーフな表現を選択する必要があった
- **Alternatives Considered**:
  1. `JsonUtility` 既定の int シリアライズ
  2. string フィールド (`failSafeMode`, `bundleMode`) を併設し `OnAfterDeserialize` で enum に変換
- **Selected Approach**: alternative 2
- **Rationale**:
  - 既存 `OscReceiverOptionsDto` / `OscSenderOptionsDto` も同方式 → 一貫性
  - enum 順序変更があったとき int だと意味が壊れるが、string なら堅牢
- **Trade-offs**:
  - Pro: enum 値の追加/並び替えに強い
  - Con: 文字列正規化ロジック (大小文字、空白) のメンテが必要
- **Follow-up**: 既存 DTO の正規化ヘルパー (`NormalizeFailSafeMode` / `NormalizeBundleMode`) を共通化するかは follow-up で判断

## Risks & Mitigations

- **R1: 既存 Asset の手動再構築漏れによる PlayMode テスト失敗** — Mitigation: 設計の Migration Strategy 節にチェックリスト形式で記載。実装フェーズ初日に既存 `*Profile.asset` (Samples~ / テスト用 fixtures) を網羅的に列挙し、移行スプリントで一括対応
- **R2: `_settings` 未代入のキャラ SO が大量に生まれる** — Mitigation: PropertyDrawer に "未設定時は OSC Adapter は起動しない" の HelpBox を表示。binding `OnStart` でも warning を出す
- **R3: sub-asset を Collection 外に飛び出した個別 `.asset` として作ろうとする利用者** — Mitigation: `OscRuntimeSettingsSO` には `[CreateAssetMenu]` を付けない。Collection の Add ボタンを介してのみ作成可能にする
- **R4: PlayerPrefs キーがプロジェクト間で衝突** — Mitigation: キー名に `Hidano.FacialControl.LipSync.MicDevice.*` のフル修飾 prefix を採用 (要件 1.4 の通り)
- **R5: 静的 backend reset 漏れによるテスト間干渉** — Mitigation: テスト base class での `[TearDown]` 強制呼び出し + 静的 backend が `DefaultPlayerPrefsBackend` に戻ることを最初の Assert で検証
- **R6: マイグレーション拡張点が将来コードを誤認させる** — Mitigation: `MigrateOnLoad` のコメントに「本 spec 時点では未実装。対応レベル c spec で実装」と明記
- **R7: `JsonUtility` の `FromJsonOverwrite` が ScriptableObject に対して期待通り動くか** — Mitigation: 公式ドキュメント上は ScriptableObject も上書き対象。`OscRuntimeSettingsSOTests.Serialize_RoundTrip_AllFieldsPreserved` で確認

## References

- [Unity ScriptingReference: AssetDatabase.AddObjectToAsset](https://docs.unity3d.com/ScriptReference/AssetDatabase.AddObjectToAsset.html) — Sub-asset 追加 API
- [Unity ScriptingReference: AssetDatabase.RemoveObjectFromAsset](https://docs.unity3d.com/ScriptReference/AssetDatabase.RemoveObjectFromAsset.html) — Sub-asset 削除 API
- [Unity ScriptingReference: TypeCache](https://docs.unity3d.com/ScriptReference/TypeCache.html) — Editor 高速型探索
- [Unity ScriptingReference: JsonUtility](https://docs.unity3d.com/ScriptReference/JsonUtility.html) — JSON シリアライズ (steering で採用)
- [Unity ScriptingReference: PlayerPrefs](https://docs.unity3d.com/ScriptReference/PlayerPrefs.html) — マシン依存 key-value ストア
- [Unity ScriptingReference: ISerializationCallbackReceiver](https://docs.unity3d.com/ScriptReference/ISerializationCallbackReceiver.html) — シリアライズ前後フック (defaults 適用に使用)
- 内部参照: `Packages/com.hidano.facialcontrol.osc/Runtime/Adapters/Json/Dto/OscReceiverOptionsDto.cs` — enum 文字列化の既存実装パターン
- 内部参照: `Packages/com.hidano.facialcontrol.lipsync/Runtime/Adapters/Devices/DeviceDescriptor.cs` — 既存 DeviceDescriptor 構造
- 内部参照: `.kiro/steering/{product,tech,structure}.md` — プロジェクト全体設計原則
