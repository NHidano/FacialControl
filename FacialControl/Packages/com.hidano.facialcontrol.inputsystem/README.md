# FacialControl InputSystem

## preview.1 破壊的変更

- `InputSystemAdapterBindingDrawer` の表示順を InputActionAsset / Trigger bindings / Analog bindings / Gaze settings の設定フローに合わせて変更しました。preview.1 polish 以降は旧 preview の Inspector 並び順と一致しません。
- これは見た目互換を維持しない破壊的変更です。既存データの自動マイグレーションはありません。既存の `FacialCharacterProfileSO` / `InputSystemAdapterBinding` は Inspector で内容を確認し、手順書やスクリーンショットは新しい並び順に合わせて更新してください。

`com.hidano.facialcontrol` の Unity InputSystem 連携アダプタ。

## 概要

このパッケージは FacialControl コアに **InputActionAsset 1 個 + `FacialCharacterProfileSO` 1 個** で完結する入力結線を提供します。長文の README を読ませず、SO Inspector 末尾の **Adapter Bindings** セクションだけで設定が完結する UX を目指しています。

- **`InputSystemAdapterBinding`** (`[FacialAdapterBinding(displayName: "Input System")]`): `AdapterBindingBase` 派生の `[Serializable]` クラス。`FacialCharacterProfileSO._adapterBindings` に追加するだけで、InputActionAsset / キーバインディング (Action ↔ Expression) / アナログバインディング (連続値 → BlendShape / BonePose) / 視線設定を一括保持する。scene 上に追加 MonoBehaviour を置く必要はない
- **`OnStart` / `OnLateTick` / `Dispose` lifecycle**: `OnStart(in AdapterBuildContext ctx)` で `InputActionAsset.Instantiate` + `ActionMap.Enable` + Trigger / Analog / Gaze の各 `IInputSource` 構築 + `ctx.InputSourceRegistry.Register(slug, source)` を実行し、`OnLateTick` でアナログ Tick / BonePose の `BuildAndPush` を行い、`Dispose` で ActionMap.Disable + Asset destroy + provider dispose を順次実施
- **`InputSystemAdapterBindingDrawer`**: `[CustomPropertyDrawer(typeof(InputSystemAdapterBinding))]` 付きの UI Toolkit ベース PropertyDrawer。core の `PropertyField` から自動解決され Inspector 内に inline UI を提供
- **slug 駆動**: binding の `Slug` field（デフォルトは `displayName` 由来の kebab-case `input-system`）が core の `IInputSourceRegistry` キーとなり、`layer.inputSources[].id` から `<slug>` または `<slug>:<sub>` 形式で参照できる。reserved id 概念は廃止
- **JSON 自動エクスポート (Editor 専用)**: SO 保存時に `StreamingAssets/FacialControl/{SO 名}/profile.json` および `analog_bindings.json` を AssetModificationProcessor 経由で自動更新

## 依存パッケージ

| パッケージ | バージョン | 用途 |
|---|---|---|
| `com.hidano.facialcontrol` | 0.1.0-preview.1 以降 | コア機能 |
| `com.unity.inputsystem` | 1.17.0 | 入力デバイスの動的切り替え |

## 使い方

1. `com.hidano.facialcontrol` と本パッケージを `Packages/manifest.json` に追加
2. Project ウィンドウで **Create** → **FacialControl** → **Facial Character Profile** から `FacialCharacterProfileSO` を作成
3. SO Inspector の Layers / Expressions / Renderer Paths を埋める
4. Inspector 末尾の **Adapter Bindings** セクションで **Add** ドロップダウンから **`Input System`** を選択し、inline UI で InputActionAsset / キーバインディング / アナログ / 視線設定を埋める
5. キャラクターの GameObject に `FacialController` を追加し、SO を結線

binding 経路は `FacialController.Initialize` 時に per-FC `LifetimeScope` に自動 register される。scene 上に追加 MonoBehaviour を置く必要はない。

```csharp
// 自前 binding を実装する場合の最小例
[Serializable]
[FacialAdapterBinding(displayName: "My Custom Input")]
public sealed class MyCustomAdapterBinding : AdapterBindingBase
{
    [SerializeField] private InputActionAsset _asset;
    private MyInputSource _source;

    public override void OnStart(in AdapterBuildContext ctx)
    {
        _source = new MyInputSource(_asset);
        ctx.InputSourceRegistry.Register(AdapterSlug.Parse(Slug), _source);
    }

    public override void Dispose() => _source?.Dispose();
}
```

## サンプル

- **Multi Source Blend Demo** — 同一レイヤーに `input-system` (Trigger Action 経由) と `osc` の 2 系統入力源を並置して加重和ブレンドを目視確認するサンプル。Scene / `FacialCharacterProfileSO` (`InputSystemAdapterBinding` + `OscReceiverAdapterBinding` 同時保持) / InputActionAsset / HUD を同梱、ユーザーはモデルを Scene の Character の子に配置するだけで動作。Package Manager から Import 可能
- **Analog Binding Demo** — 右スティック等の連続値を BlendShape / BonePose 軸に写像するアナログ結線サンプル

各サンプルの詳細は `Samples~/<sample-name>/README.md` を参照。

## ライセンス

[MIT License](../com.hidano.facialcontrol/LICENSE.md)
