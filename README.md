# FacialControl

## preview.1 破壊的変更

- `com.hidano.facialcontrol`: AnimationClip 上の `AnimationEvent` 由来遷移メタデータ (`FacialControlMeta_Set` など) は撤去され、Expression の `transitionDuration` / `transitionCurvePreset` は `FacialCharacterProfileSO` Inspector の Expression 行で管理します。過去 preview の AnimationClip からの自動マイグレーションはありません。
- `com.hidano.facialcontrol.inputsystem`: `InputSystemAdapterBindingDrawer` の表示順を InputActionAsset / Trigger bindings / Analog bindings / Gaze settings の設定フローに合わせて変更しました。旧 preview の Inspector 並び順との見た目互換は維持せず、自動マイグレーションもありません。

3D キャラクターの表情をリアルタイムに制御する Unity 向けライブラリ（開発者向けアセット）のモノレポ。VTuber 配信用フェイシャルキャプチャ連動や GUI エディタでの Expression 作成支援をユースケースとし、コアに加えて OSC 送受信・InputSystem 入力の機能別サブパッケージを別途提供する。

## パッケージ構成

機能別に 3 パッケージに分割されている。コアパッケージは表情制御本体のみを含み、OSC 配信・InputSystem 入力は用途に応じて任意で追加する。

| パッケージ | 役割 | 必須依存 | 場所 |
|---|---|---|---|
| `com.hidano.facialcontrol` | コア（表情遷移・レイヤーブレンド・JSON プロファイル・Editor 拡張・LipSync I/F） | Unity 6000.3+ | `FacialControl/Packages/com.hidano.facialcontrol/` |
| `com.hidano.facialcontrol.osc` | OSC 送受信アダプタ（VRChat / ARKit 互換） | コア + `com.hidano.uosc` | `FacialControl/Packages/com.hidano.facialcontrol.osc/` |
| `com.hidano.facialcontrol.inputsystem` | InputSystem 経由のキーバインド・コントローラ入力 | コア + `com.unity.inputsystem` | `FacialControl/Packages/com.hidano.facialcontrol.inputsystem/` |

サブパッケージは同 GameObject 上の `OscFacialControllerExtension` / `InputFacialControllerExtension` MonoBehaviour を経由して `FacialController` に接続される（コアは `IFacialControllerExtension` I/F 経由で拡張を受け取る。具体実装は知らない）。

## 動作要件

- Unity 6000.3 以降
- Animator コンポーネントが設定された 3D キャラクターモデル（FBX）
- BlendShape を持つ SkinnedMeshRenderer

## インストール

### npm レジストリ経由（推奨）

`Packages/manifest.json` に以下を追加する（必要なサブパッケージのみ追加 OK）。

```json
{
    "scopedRegistries": [
        {
            "name": "npmjs",
            "url": "https://registry.npmjs.org",
            "scopes": [
                "com.hidano"
            ]
        }
    ],
    "dependencies": {
        "com.hidano.facialcontrol": "0.1.0-preview.1",
        "com.hidano.facialcontrol.osc": "0.1.0-preview.1",
        "com.hidano.facialcontrol.inputsystem": "0.1.0-preview.1"
    }
}
```

### Git URL 経由

npm レジストリを経由せず GitHub から直接インポートする場合、`Packages/manifest.json` の `dependencies` に Git URL を指定する。モノレポ構成のためサブディレクトリを `?path=` クエリで指定する。

```json
{
    "dependencies": {
        "com.hidano.facialcontrol": "https://github.com/NHidano/FacialControl.git?path=FacialControl/Packages/com.hidano.facialcontrol",
        "com.hidano.facialcontrol.osc": "https://github.com/NHidano/FacialControl.git?path=FacialControl/Packages/com.hidano.facialcontrol.osc",
        "com.hidano.facialcontrol.inputsystem": "https://github.com/NHidano/FacialControl.git?path=FacialControl/Packages/com.hidano.facialcontrol.inputsystem"
    }
}
```

特定のバージョン（タグ）やブランチを指定する場合は URL の末尾に `#<tag-or-branch>` を付加する。

```json
"com.hidano.facialcontrol": "https://github.com/NHidano/FacialControl.git?path=FacialControl/Packages/com.hidano.facialcontrol#v0.1.0-preview.1"
```

## クイックスタート

### 最速動作確認: InputSystem サンプル

`com.hidano.facialcontrol.inputsystem` の **Multi Source Blend Demo** サンプルには、モデル以外すべて結線済みの Scene・FacialProfileSO・InputBindingProfileSO・HUD・JSON が同梱されている。

1. Package Manager で `Multi Source Blend Demo` をインポート
2. `Assets/Samples/FacialControl InputSystem/<version>/Multi Source Blend Demo/MultiSourceBlendDemo.unity` を開く
3. Hierarchy の **Character** GameObject の子として任意の FBX / VRM モデルをドラッグ
4. Play（キーボード `1`〜`4` / HUD ボタンで smile / angry / surprise / troubled が発動）

詳細は [`Packages/com.hidano.facialcontrol.inputsystem/Samples~/MultiSourceBlendDemo/README.md`](FacialControl/Packages/com.hidano.facialcontrol.inputsystem/Samples~/MultiSourceBlendDemo/README.md) 参照。

### GUI ベースでゼロから作る

1. メニュー **FacialControl** → **新規プロファイル作成** からダイアログを開き、命名規則プリセット（VRM / ARKit）を選んで作成（`FacialProfileSO` + プロファイル JSON が自動生成され、`smile` / `angry` / `blink` の雛形 Expression が含まれる）
2. キャラクターの GameObject に **FacialController** を追加し、生成された `FacialProfileSO` を割り当て
3. キーボード／コントローラ入力を使うなら `com.hidano.facialcontrol.inputsystem` を追加し、同 GameObject に **Input Facial Extension** を追加。`InputBindingProfileSO` を作成して `FacialInputBinder` に割り当て
4. OSC 送受信を使うなら `com.hidano.facialcontrol.osc` を追加し、同 GameObject に **OSC Facial Extension** と `OscReceiver` / `OscSender` を追加
5. Play して動作確認。BlendShape 名の微調整は `FacialProfileSO` Inspector か **Expression 作成** ウィンドウから（ドロップダウン候補あり）

コア機能のみを深く扱う場合は [コアパッケージのクイックスタートガイド](FacialControl/Packages/com.hidano.facialcontrol/Documentation~/quickstart.md) も参照。

```csharp
// スクリプトから Expression を切り替える場合
_facialController.Activate(expression);
_facialController.Deactivate(expression);
```

## アーキテクチャ

コアパッケージはレイヤード設計（Domain / Application / Adapters）で Assembly Definition により依存方向を強制する。Unity ランタイム依存（PlayableAPI / InputSystem / uOSC 等）は Adapters 層に集約し、Domain 層は `Unity.Collections`（NativeArray）にのみ依存する（`UnityEngine.dll` には依存しない）。

```
com.hidano.facialcontrol/
└── Runtime/
    ├── Domain/         # 値オブジェクト・抽象 I/F・純ドメインサービス
    ├── Application/    # ユースケース
    └── Adapters/       # Unity API / PlayableGraph 実装
com.hidano.facialcontrol.osc/          # uOsc ベースの OSC 実装（拡張）
com.hidano.facialcontrol.inputsystem/  # InputSystem ベースの入力実装（拡張）
```

サブパッケージは `IFacialControllerExtension` を実装する MonoBehaviour を通じて `InputSourceFactory.RegisterReserved` でアダプタを登録する。コア API は OSC / InputSystem を直接知らない。表情制御は PlayableGraph + AnimationStream API ベースで、既存の Animator と共存可能。

## パフォーマンス

- 定常状態（レイヤー数・BlendShape 数固定時）で毎フレーム GC アロケーションゼロを目標に設計（構成変更時のみ内部バッファを再確保）
- 遷移割り込み時も `LayerPlayable` の `_snapshotBuffer` / `_targetBuffer`（Persistent NativeArray）を再利用し追加アロケーションなし
- OSC 受信は `Interlocked.Exchange` ベースの非ブロッキングダブルバッファで lock 不使用
- OSC 送信は uOsc クライアント内部のワーカースレッドで非同期化（本ライブラリ側はメインスレッド非ブロック）
- PlayMode テスト `MultiCharacterPerformanceTests`（10 キャラクター × ARKit 52 BlendShape）で同時制御の動作を検証

## 対応フォーマット

| フォーマット | 対応状況 |
|-------------|----------|
| FBX | 対応 |
| VRM | 今後対応予定 |

## 既知の制限とロードマップ

### Addressables（AssetBundle）対応

現状 preview.1 では、プロファイル JSON は `StreamingAssets/FacialControl/` 配下のファイルシステムから直接読み込む設計。これにより以下の制約がある。

- キャラクター Prefab と表情プロファイルを 1 セットで Addressables 配信しにくい
  - `FacialController` / `FacialProfileSO` 自体は Addressables 対応可能だが、SO が指す JSON が StreamingAssets 前提のためキャラクター配信とセットにできない
- プラットフォーム別の読み込み経路差
  - Windows PC を主ターゲットとする。Android / WebGL 等では StreamingAssets アクセスに専用 API が必要で現状未対応

### preview.2 以降の予定

`IProfileJsonLoader` 抽象化の導入によりプロファイル JSON のロード経路を差し替え可能にし、以下をサポート予定。

- StreamingAssets（現状、デフォルト）
- TextAsset 参照（Addressables グループに同梱可能）
- Addressables アドレス直接指定（リモート配信・追加表情パック）

既存のデフォルト経路（StreamingAssets）は維持するため、現在の設定から破壊的変更は発生しない。

## ドキュメント

- コアパッケージ: [`Packages/com.hidano.facialcontrol/README.md`](FacialControl/Packages/com.hidano.facialcontrol/README.md)
- OSC: [`Packages/com.hidano.facialcontrol.osc/README.md`](FacialControl/Packages/com.hidano.facialcontrol.osc/README.md)
- InputSystem: [`Packages/com.hidano.facialcontrol.inputsystem/README.md`](FacialControl/Packages/com.hidano.facialcontrol.inputsystem/README.md)
- クイックスタートガイド: [`Documentation~/quickstart.md`](FacialControl/Packages/com.hidano.facialcontrol/Documentation~/quickstart.md)
- JSON スキーマリファレンス: [`Documentation~/json-schema.md`](FacialControl/Packages/com.hidano.facialcontrol/Documentation~/json-schema.md)
- 移行ガイド: [`docs/migration-guide.md`](docs/migration-guide.md)

## ライセンス

[MIT License](FacialControl/Packages/com.hidano.facialcontrol/LICENSE.md)
