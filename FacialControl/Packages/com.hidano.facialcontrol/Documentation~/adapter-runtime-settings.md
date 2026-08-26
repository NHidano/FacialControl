# Adapter Runtime Settings 運用ガイド

`AdapterRuntimeSettingsCollectionSO` および `OscRuntimeSettingsSO` / `LipSyncDeviceStore` は、
キャラ SO (`FacialCharacterProfileSO`) から「環境依存 / 運用依存 / マシン依存」の設定値を切り離すための
集約構造です。本ガイドでは spec [`adapter-runtime-settings`](../../../../.kiro/specs/adapter-runtime-settings/) で導入された
新構造の作成手順・運用フロー・マイグレーション手順をまとめます。

> **重要**: preview 段階のため、旧構造 (`OscReceiverAdapterBinding._endpoint` 等を持つキャラ SO) との
> 後方互換は提供しません。既存 Asset は本ガイドの「Migration Strategy」節に従って **手動再構築** してください。

---

## 1. 全体像

```
AdapterRuntimeSettingsCollection.asset (Project ビュー上は 1 ファイル)
  └─ OscRuntimeSettings (sub-asset)
       ├─ Receiver セクション (listenEndpoint / listenPort / staleness / failSafeMode / bundle 系)
       └─ Sender セクション (endpoints / heartbeatIntervalSeconds / suppressLoopback)
```

- `AdapterRuntimeSettingsBase` (abstract `ScriptableObject`) は `_label` / `_schemaVersion` と
  `ToJson()` / `FromJson()` の仮想 API を公開する共通基底です (要件 5.1-5.6)
- `AdapterRuntimeSettingsCollectionSO` は `List<AdapterRuntimeSettingsBase> _items` で
  複数の sub-asset を 1 親 Asset に束ねます (要件 2.1-2.3)
- `OscRuntimeSettingsSO` は Receiver / Sender を 1 sub-asset に統合した派生 SO です (要件 2.4)
- `OscReceiverAdapterBinding._settings` と `OscSenderAdapterBinding._settings` は同一の
  `OscRuntimeSettingsSO` を `[SerializeField]` 参照し、それぞれ Receiver / Sender セクションだけを読みます
  (要件 2.5-2.7)
- マイクデバイス指定は `LipSyncDeviceStore` 経由で PlayerPrefs に保存され、git 管理外に置かれます
  (要件 4.1-4.6)

## 2. AdapterRuntimeSettingsCollection.asset の作成手順

1. Project ウィンドウで保存先フォルダ (例: `Assets/FacialControl/RuntimeSettings/`) を選択する
2. 右クリック → **Create** → **FacialControl** → **Adapter Runtime Settings Collection** を実行
   - `CreateAssetMenu` の `menuName = "FacialControl/Adapter Runtime Settings Collection"` が登録済み
3. 生成された `AdapterRuntimeSettingsCollection.asset` を選択し、Inspector を開く
4. Inspector 上部の **Add** ボタン (`AdapterRuntimeSettingsCollectionEditor` 提供) から
   `OscRuntimeSettings` を選択して sub-asset を追加する
5. 必要に応じて `_label` フィールドに識別ラベル (例: `"production-vrchat"`、`"local-debug"`) を入力する
   - 同一型の sub-asset を 1 Collection に複数登録できる (要件 6.6)
   - 同じ `_label` を重複登録すると `Debug.LogWarning` が出る (要件 6.8)

## 3. sub-asset 追加 / 削除フロー

`AdapterRuntimeSettingsCollectionEditor` は UI Toolkit ベースの CustomEditor で、以下の不変条件を満たします
(要件 3.1-3.4, 6.1-6.8)。

### 3.1 追加 (Add)

1. Inspector の **Add** ボタンを押す
2. `AdapterRuntimeSettingsTypeRegistry` が `TypeCache.GetTypesDerivedFrom<AdapterRuntimeSettingsBase>()` で
   列挙した non-abstract 派生型の一覧がドロップダウン表示される
3. 追加対象型を選択すると以下が一括で実行される:
   - `ScriptableObject.CreateInstance(type)` で sub-asset を生成
   - `AssetDatabase.AddObjectToAsset(subAsset, collection)` で親 Asset に紐付ける
   - `_items.Add(subAsset)` で List に登録
   - `AssetDatabase.SaveAssets()` を呼ぶ
4. 同型 sub-asset を追加した際は `_label` を変えて識別性を確保する

### 3.2 削除 (Remove)

1. Inspector の対象 sub-asset 行の **Remove** ボタンを押す
2. 確認ダイアログが表示される (誤操作防止)
3. 確定後、以下が一括で実行される:
   - `AssetDatabase.RemoveObjectFromAsset(subAsset)` で sub-asset を Asset から除去
   - `_items.RemoveAt(index)` で List から外す
   - `AssetDatabase.SaveAssets()` を呼ぶ
4. 削除対象 sub-asset 以外の `_label` / `_schemaVersion` および各派生 SO の serialize 済み値は保持される
   (パラメータ消失防止保証、対応レベル b)

> **HelpBox 警告**: sub-asset を削除した場合、それを `[SerializeField]` で参照していた
> AdapterBinding (`OscReceiverAdapterBinding._settings` 等) は参照欠落になり、Inspector 側 Drawer が
> HelpBox で「未設定」警告を表示します。Binding の `OnStart` は `_settings == null` を検出すると
> warning を出して起動を skip するため、scene 起動時に silent な無動作にはなりません (要件 2.7)。
> 削除前に逆引き確認ダイアログを出す機能は本 spec のスコープ外で、`docs/backlog.md` に登録済みです。

## 4. `_receiverEnabled` / `_senderEnabled` の使い分け

`OscRuntimeSettingsSO` は Receiver / Sender 2 セクションを 1 sub-asset に統合しています。
セクション単位で機能を停止したい場合に `_receiverEnabled` / `_senderEnabled` の bool トグルを使います
(要件 2.7, validate-design Critical Issue #3 の暫定対応)。

| フィールド | 既定値 | 効果 |
|---|---|---|
| `_receiverEnabled` | `true` | `false` の場合、`OscReceiverAdapterBinding._settings` がこの SO を参照していても `OnStart` は warning を出して `OscReceiverHost` を起動せず skip する |
| `_senderEnabled` | `true` | `false` の場合、`OscSenderAdapterBinding._settings` がこの SO を参照していても `OnStart` は warning を出して送信側 Host を起動せず skip する |

典型的な運用パターン:

- **本番 / 配信時**: `_receiverEnabled = true`, `_senderEnabled = true`
  - 同じ SO を `OscReceiverAdapterBinding._settings` と `OscSenderAdapterBinding._settings` の双方に
    アサインすることで Receiver / Sender 両系統で一貫した設定値を読み出せる (要件 2.7)
- **受信のみ検証 (例: VRChat OSC 受信デバッグ)**: `_receiverEnabled = true`, `_senderEnabled = false`
- **送信のみ検証 (例: PerfectSync 送信単体テスト)**: `_receiverEnabled = false`, `_senderEnabled = true`
- **AdapterBindings から binding を完全に外す方が「停止」の意味として正しい場合**: そちらを優先
  - enabled トグルは「同じ SO の構成のまま一時的にセクションを止める」用途であり、運用上の
    enabled / disabled フラグ全般としては設計していない (`enabled` フラグによる動作停止機構は
    本 spec のスコープ外、要件 8.3)

### 4.1 トグル切替時の挙動

- `_receiverEnabled` / `_senderEnabled` は `OnAfterDeserialize` の正規化対象ではない
  (true / false の二値のためそのまま反映)
- ランタイム実行中にトグルを切り替えても、binding は `OnStart` でのみ参照を読むため、
  scene 再起動 (PlayMode 再入) で反映される
- ランタイム動的設定変更 UI は本 spec のスコープ外 (要件 8.4)

## 5. PlayerPrefs キー名一覧 (LipSync マイクデバイス)

`Hidano.FacialControl.LipSync.Adapters.Devices.LipSyncDeviceStore` が以下のキーを管理します
(要件 1.4, 4.2)。

| キー | 型 | 既定値 | 説明 |
|---|---|---|---|
| `Hidano.FacialControl.LipSync.MicDevice.Name` | `string` | `""` (空文字) | 使用するマイク / ASIO デバイス名。`UnityEngine.Microphone.devices` 一覧、または ASIO ドライバ名と完全一致する文字列を期待する |
| `Hidano.FacialControl.LipSync.MicDevice.Disambiguator` | `int` | `0` | 同名デバイスが複数存在する環境用の 0 始まり列挙インデックス。通常は `0` のままで問題ない |

### 5.1 公開 API

```csharp
// 読み出し (キー欠落時は DeviceName="" / DisambiguatorIndex=0 を返し、例外を投げない)
DeviceDescriptor descriptor = LipSyncDeviceStore.Load();

// 書き込み (DeviceName が null の場合は空文字に正規化される)
LipSyncDeviceStore.Save(new DeviceDescriptor
{
    DeviceName = "Realtek High Definition Audio",
    DisambiguatorIndex = 0,
});
```

- `LipSyncDeviceStore` は `static` クラス。インスタンス化不要
- `IPlayerPrefsBackend` interface で内部実装が抽象化されており、テスト時は
  `Tests/Shared` 配下の `FakePlayerPrefsBackend` を `internal` `SetBackend` / `ResetBackend` で差し替える
  ことで、実 PlayerPrefs に書き込まずにラウンドトリップ検証ができる (要件 7.4)
- `ULipSyncAdapterBinding._deviceDescriptor` は `[SerializeField]` から削除済み。
  Editor 側 Drawer の DeviceDescriptorPopup が `Load()` / `Save(descriptor)` を呼び、
  ランタイムは `OnStart` で `LipSyncDeviceStore.Load()` を実行してマイク初期化フローへ渡す

### 5.2 PlayerPrefs 保存先と git 管理

- Windows の場合、PlayerPrefs はレジストリ
  `HKCU\Software\<CompanyName>\<ProductName>` (Player Settings 由来) に保存される
- マシン依存値のため `Assets/` 配下に Asset として保存せず、git 管理対象に入らない
- 別マシンで開発する際は、各人が一度 Inspector でデバイスを再選択すれば PlayerPrefs に独立した値が
  記録される (git diff は発生しない)

---

## 6. Migration Strategy (旧構造からの手動再構築)

旧構造ではキャラ SO 直下の `OscReceiverAdapterBinding` / `OscSenderAdapterBinding` / `ULipSyncAdapterBinding` に
環境依存フィールドが inline されていました。preview 段階のポリシー (`docs/requirements.md` FR-001) に基づき、
自動マイグレーションは提供しません。以下の手順で **手動で再構築** してください (要件 1.5, 8.5)。

### 6.1 backup (必須)

- 対象プロジェクトを git commit で clean state にする
- `FacialCharacterProfileSO` Asset と `StreamingAssets/FacialControl/**/*.json` を別フォルダに複製しておく

### 6.2 旧フィールド値の控え

新構造に転記するため、旧 SO Inspector で以下の値を控えておく:

- `OscReceiverAdapterBinding._endpoint`, `_port`, `_stalenessSeconds`, `_failSafeMode`,
  `_consistencyCheckWarnLog`, `_bundleMode`, `_bundleAccumulationTimeoutMs`
- `OscSenderAdapterBinding._endpoints` (各 endpoint の `ip` / `port` / `preset` / `enabled`),
  `_heartbeatIntervalSeconds`, `_suppressLoopback`
- `ULipSyncAdapterBinding._deviceDescriptor.DeviceName`, `DisambiguatorIndex`

### 6.3 AdapterRuntimeSettingsCollection の新規作成

- 2 節「AdapterRuntimeSettingsCollection.asset の作成手順」に従って新規 Asset を作成
- `OscRuntimeSettings` sub-asset を追加し、6.2 で控えた OSC 系の値を Receiver / Sender 各セクションに転記
- `_receiverEnabled` / `_senderEnabled` は既定 `true` のまま (両系統を有効化)

### 6.4 AdapterBinding 参照の差し替え

- 対象キャラ SO (`FacialCharacterProfileSO`) を Inspector で開く
- **Adapter Bindings** セクションで `OscReceiverAdapterBinding` を展開し、`_settings` フィールドに
  6.3 で作成した `OscRuntimeSettings` sub-asset をアサインする
- `OscSenderAdapterBinding._settings` にも同じ sub-asset (または別構成が必要なら別 sub-asset) をアサインする
- 旧 `_endpoint` / `_port` / `_endpoints` / `_heartbeatIntervalSeconds` 等の inline フィールドは
  PropertyDrawer から既に削除されているため、Inspector 上には表示されない (要件 1.1)

### 6.5 LipSync マイクデバイスの PlayerPrefs 移行

- `ULipSyncAdapterBinding._deviceDescriptor` は `[SerializeField]` から削除済み
- 旧 `DeviceName` / `DisambiguatorIndex` を、Editor 上の DeviceDescriptorPopup から再選択するか、
  `LipSyncDeviceStore.Save(new DeviceDescriptor { DeviceName = "...", DisambiguatorIndex = 0 })` を
  一度ランタイムから呼んで PlayerPrefs に書き込む
- 開発メンバー各自のマシンで一度ずつ実施が必要 (git で共有されない)

### 6.6 検証チェックリスト

- [ ] `OscReceiverAdapterBinding` / `OscSenderAdapterBinding` の `_settings` が全て `null` ではない
      (Project 内検索で binding を列挙し、Inspector 上で確認)
- [ ] 旧 `_endpoint` / `_port` / `_endpoints` / `_deviceDescriptor` を直接参照していた
      テストコードが新構造 (`OscRuntimeSettingsSO` インスタンス生成 + `Configure` API 経路、または
      `LipSyncDeviceStore.Save` 経路) に書き換わっている
- [ ] PlayMode で Scene を起動し、OSC 受信 / OSC 送信 / リップシンクが期待通り動作する
- [ ] 0-alloc perf test (`OscReceiverGCAllocationTests` / `OscSenderGCAllocationTests` /
      `EndToEndGcAllocationTests`) が緑

---

## 7. 将来マイグレーション拡張点 (`_schemaVersion` / `ToJson` / `FromJson`)

本 spec 時点では拡張点を予約するのみで、本実装はありません (要件 5.4-5.5)。将来 spec で対応レベル c
(C# 型削除 / リネーム時の自動マイグレーション) を導入する際は、以下のフックを使います。

- `AdapterRuntimeSettingsBase._schemaVersion: int` — 既定値 `1`。バージョン差分判定用
- `AdapterRuntimeSettingsBase.ToJson()` / `FromJson(string)` — サブクラスが override 可能
- `OscRuntimeSettingsSO` は両 API を override 済み。`JsonUtility` ベースで全フィールドを camelCase
  キー名で書き出し、enum (`FailSafeMode` / `BundleInterpretationMode`) は文字列 (`"revertToBase"` 等)
  として保存される (要件 5.3, 7.3)
- `AdapterRuntimeSettingsCollectionSO.MigrateOnLoad()` — `OnEnable` から呼ばれる空メソッド。
  将来同クラス内に直接マイグレーション処理を追加する

詳細は `docs/backlog.md` の「`MigrateOnLoad` の本実装、`_schemaVersion` 増分規約策定」エントリを参照してください。

---

## 8. 参考

- spec: [`.kiro/specs/adapter-runtime-settings/`](../../../../.kiro/specs/adapter-runtime-settings/)
  - `requirements.md` Req 1-8
  - `design.md` `## Architecture` / `## Components and Interfaces`
- OSC JSON スキーマ:
  - [`com.hidano.facialcontrol.osc/Documentation~/osc-receiver-options.md`](../../com.hidano.facialcontrol.osc/Documentation~/osc-receiver-options.md)
  - [`com.hidano.facialcontrol.osc/Documentation~/osc-sender-options.md`](../../com.hidano.facialcontrol.osc/Documentation~/osc-sender-options.md)
- LipSync 使用手順:
  - [`com.hidano.facialcontrol.lipsync/Documentation~/usage.md`](../../com.hidano.facialcontrol.lipsync/Documentation~/usage.md)
- Adapter Binding アーキテクチャ全体の preview.2 → preview.3 移行手順:
  [`migration-guide.md`](./migration-guide.md)
