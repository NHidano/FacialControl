# クイックスタートガイド

FacialControl で 3D キャラクターの表情をリアルタイム制御するまでの最短手順です。**InputActionAsset 1 個 + キャラクター SO 1 個** だけ用意すれば動作します。JSON は人間が触る必要はありません。

> **最速確認**: `com.hidano.facialcontrol.inputsystem` をインストール後、Package Manager から `Multi Source Blend Demo` サンプルを Import し、Scene の `Character` GameObject にモデルを子として配置すれば即動作します。詳細は `Samples~/MultiSourceBlendDemo/README.md`。

## 動作要件

- Unity 6000.3 以降
- Animator を持つ 3D キャラクターモデル (FBX / VRM 等)
- BlendShape を持つ SkinnedMeshRenderer

## 1. パッケージのインストール

| パッケージ | 役割 | 必須依存 |
|---|---|---|
| `com.hidano.facialcontrol` | コア (表情遷移・PlayableGraph・Editor 拡張) | Unity 6000.3+ |
| `com.hidano.facialcontrol.inputsystem` | InputSystem 経由の入力結線 (推奨) | コア + `com.unity.inputsystem` |
| `com.hidano.facialcontrol.osc` | OSC 送受信 (任意) | コア + `com.hidano.uosc` |

`Packages/manifest.json` に scopedRegistries (`com.hidano` を `https://registry.npmjs.org` に向ける) を追加した上で `dependencies` に追記してください。Git URL 経由のインストールも可能です (詳細は本リポジトリ README)。

## 2. SO とコンポーネントのセットアップ

このリファクタ後は **FacialCharacterSO 1 個** をシーン上のキャラクターに結線するだけで完結します。

1. Project ウィンドウで右クリック → **Create** → **FacialControl** → **Facial Character** で `FacialCharacterSO` を作成
2. 作成された SO の Inspector を開き、各 Foldout セクションを上から埋める:
   - **入力 (Input)**: 自前の `.inputactions` または同梱の `FacialControlDefaultActions.inputactions` を割り当て、`Action Map Name` を設定 (既定 `Expression`)
   - **キーバインディング**: Action 名 ↔ Expression ID を編集 (Action 名はドロップダウンで InputActionAsset から候補列挙)
   - **アナログバインディング**: 必要に応じて右スティック等を BlendShape / BonePose 軸に写像
   - **レイヤー** / **Expression** / **BonePose**: 表情データ本体を編集 (BlendShape 名は参照モデルから候補ドロップダウン)
3. シーンにキャラクターを配置し、Animator を含むルート GameObject を選択
4. **Add Component** → **FacialControl** → **Facial Controller**
5. **Character SO** フィールドに 1 で作成した SO をドラッグ&ドロップ
6. (InputSystem 連携時) 同 GameObject に **Add Component** → **FacialControl** → **Facial Character Input Extension**

これだけで Play 時に自動初期化されます。`OnEnable` で SO から表情データと入力結線を読み込み、`OnDisable` でクリーンアップします。

> **JSON について**: SO 編集時に Editor が裏で `StreamingAssets/FacialControl/{SO 名}/profile.json` を自動エクスポートします。**ユーザーが JSON のパスを書いたり中身を編集したりする必要はありません**。ビルド後にコンテンツ差し替えが必要な場合のみ、StreamingAssets 配下の JSON を直接置き換えれば反映されます。

## 3. 動作確認

Unity Editor で Play モードに入り、SO の **キーバインディング** で割り当てた Action のキーを押すと Expression が発火します。Inspector の **デバッグ情報** Foldout で `schemaVersion` / レイヤー数 / Expression 数 / 自動エクスポート先パスを確認できます。

## 4. スクリプトから表情を切り替える

```csharp
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

public class MyExpressionController : MonoBehaviour
{
    [SerializeField] private FacialController _facialController;

    private void TriggerSmile()
    {
        if (!_facialController.IsInitialized || !_facialController.CurrentProfile.HasValue)
            return;

        var profile = _facialController.CurrentProfile.Value;
        var smile = profile.FindExpressionById("smile");
        if (smile.HasValue)
            _facialController.Activate(smile.Value);
    }
}
```

### 主要 API

| メソッド | 説明 |
|---|---|
| `Activate(Expression)` | Expression をアクティブ化 (レイヤーの排他モードに従う) |
| `Deactivate(Expression)` | Expression を非アクティブ化 |
| `LoadCharacter(FacialCharacterProfileSO)` | キャラクター SO を切り替え (PlayableGraph 再構築) |
| `ReloadProfile()` | 現在のプロファイルを再読み込み |
| `GetActiveExpressions()` | 現在アクティブな Expression のリストを取得 |
| `SetActiveBonePose(in BonePose)` | アナログ等から BonePose を上書き |

### 主要プロパティ

| プロパティ | 型 | 説明 |
|---|---|---|
| `IsInitialized` | `bool` | 初期化済みか |
| `CurrentProfile` | `FacialProfile?` | 現在のプロファイル |
| `CharacterSO` | `FacialCharacterProfileSO` | 結線中の統合 SO |

## 5. ARKit / PerfectSync の自動検出 (オプション)

メニュー **FacialControl** → **ARKit 検出ツール** からモデルの BlendShape 命名を自動検出して Expression を生成できます。出力先に `FacialCharacterSO` の StreamingAssets 規約パスを指定すれば、対応する JSON が直接更新されます。

## トラブルシューティング

### 表情が変化しない

- **キャラクター SO 未結線**: `FacialController.CharacterSO` を Inspector で確認
- **BlendShape 名がモデルと不一致**: SO Inspector の `_referenceModel` (Editor 専用) に対象モデルを指定すれば BlendShape ドロップダウンが正しい候補を表示
- **SkinnedMeshRenderer 自動検索失敗**: `FacialController.SkinnedMeshRenderers` に明示的に割り当て
- **Animator が無い**: ルートに Animator を必ず付与

### キー入力で切り替わらない

- **InputActionAsset 未結線**: SO Inspector の **入力** セクションで .inputactions を割り当て
- **Action Map Name 不一致**: 既定は `Expression`、独自命名する場合は両側で揃える
- **FacialCharacterInputExtension 未追加**: 同 GameObject に追加してあるか確認

### JSON を手で書き換えたい (上級者向け)

ビルド後のコンテンツ差し替え用途のみ想定しています。スキーマは [json-schema.md](json-schema.md) を参照。Editor 操作中の SO がマスターであり、Editor で SO を保存すると JSON が上書きされる点に注意してください。

## 次のステップ

- **JSON スキーマの詳細 (上級者向け)**: [json-schema.md](json-schema.md)
- **サンプル**: `Multi Source Blend Demo` / `Analog Binding Demo`
