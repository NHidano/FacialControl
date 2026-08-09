# OscReceiverDemo

`OscReceiverAdapterBinding` の受信専用サンプルです。`OscReceiverDemo.unity` を開き、お手持ちのキャラモデルを Scene に置いた状態で Play すると、`127.0.0.1:9000` で受信した `/avatar/parameters/{BlendShape 名}` 系 OSC メッセージをモデルの BlendShape に反映します。

## 同梱されているもの

| ファイル | 役割 |
|---|---|
| `OscReceiverDemo.unity` | `FacialController` と `OscReceiverDemoProfile` を結線済みの最小 Scene |
| `OscReceiverDemoProfile.asset` | `OscReceiverAdapterBinding`（OSC 受信）を結線済みの `FacialCharacterProfileSO`。Layer は 1 つだけ（OSC 入力を素通し） |
| `OscReceiverDemoBootstrap.cs` | `Application.runInBackground = true` を有効化する最小 helper |
| `OscReceiverOptions.json` | Scene 内設定と同等の `OscReceiverOptionsDto` サンプル（参考用） |

> キャラモデル (FBX / VRM / prefab) は同梱していません。お手持ちのものを用意してください。

## 受信内容

- listen endpoint: `127.0.0.1:9000`
- BlendShape: 送信側 heartbeat `/_facialcontrol/blendshape_names` と受信側モデルの BlendShape 名一覧から自動生成される mapping
- Gaze: `/_facialcontrol/gaze` の広告を受信すると、広告内容に対応する `Gaze_VRChat_XY` または `Gaze_ARKit_8BS` の runtime mapping が自動生成されます。受信した Gaze を目ボーンに反映するには、profile の GazeConfigs に広告の `expressionId`（既定では `eye_look`）と、お手持ちのモデルの目ボーン path が一致している必要があります（手順 4）。目ボーンへの適用は `FacialController` が行います。
- staleness: 1 秒受信が途絶えると base 表情へ復帰
- bundle mode: atomic swap

## 手順

1. **シーンを開く**: Project ウィンドウで `OscReceiverDemo.unity` をダブルクリック。Hierarchy に `Character / Main Camera / Directional Light` が並びます。
2. **モデルを置く**: お手持ちのキャラモデルの prefab を Hierarchy の **`Character` の子**にドラッグして配置します。
3. **Gaze の広告経路を確認**: `OscReceiverDemoProfile.asset` の **`OSC` → `Mappings`** に Gaze の手入力 mapping を追加する必要はありません。`/_facialcontrol/gaze` を広告する FacialControl 送信側を同じ endpoint に接続してください。FacialControl 以外の外部 OSC ソースを使う場合は広告がないため、`Gaze_VRChat_XY` または `Gaze_ARKit_8BS` の mapping を手動設定します。
4. **目ボーンを設定（Gaze を反映する場合）**: 同 Inspector 上部の **「参照モデル」** にお手持ちのモデル（prefab / Scene 上の GameObject）を割り当て、**目線タブ**の GazeConfig 行（`eye_look`）で **「参照モデルから自動設定」** を押します。目ボーン名・初期回転・yaw/pitch 軸・可動角が自動入力されます（Humanoid の Eye ボーンマッピング優先、無ければ `LeftEye` / `RightEye` の名前検索）。自動解決できないモデルは「左目ボーン / 右目ボーン」フィールドにボーン名（例 `Eye_L`）または相対 path（例 `Hips/Spine/Head/Eye_L`）を手入力してください。bone path が空のままだと Gaze は受信されても目は動きません。
5. **listen port を必要に応じて変更**: 別 port で受けたいとき、`OscReceiverDemoProfile.asset` の `OSC` → `_endpoint` / `_port` を変更します。
6. **Play**: 送信側（`OscOutputDemo` 等）から `127.0.0.1:9000` に向けて OSC を送ると、モデルの BlendShape と目ボーンが更新されます。

## Auto Mapping 運用

Normal_BlendShape は heartbeat 駆動の auto mapping が既定経路です。受信側は `/_facialcontrol/blendshape_names` を受け取った時点で、送信側が持つ BlendShape 名と受信側モデルの BlendShape 名の積集合から runtime mapping を生成します。そのため `OscReceiverDemoProfile.asset` には Normal_BlendShape の手入力 mapping を置かず、モデル差し替え時も BlendShape 名が一致していれば Inspector で 1 件ずつ追加する必要はありません。

auto mapping は heartbeat 受信が前提です。送信側の `/_facialcontrol/preset` は受信側にアドレスプリセットを知らせる制御 address で、payload の 1 番目に `vrchat` または `arkit` を送ると、それぞれ `/avatar/parameters/{name}` または `/ARKit/{name}` の BlendShape address として解釈されます。custom prefix を使う送信側は payload を `custom`, `{prefix}` の 2 文字列にし、`{prefix}` は `/custom/blendshape/` のように先頭 `/` と末尾区切りを含めた完全な prefix として指定してください。`/_facialcontrol/preset` が届かない場合は、受信した BlendShape 名から VRChat / ARKit を推定します。

Gaze の auto mapping は `/_facialcontrol/gaze` 広告が前提です。広告を受信したときだけ、広告の route / format と一致する Gaze mapping を生成します。GazeConfig の `expressionId` は引き続き広告の id と一致させてください。id の完全自動生成・推測は Spec 2 の範囲であり、このサンプルの auto mapping は完全自動化ではありません。Gaze だけが途絶えた場合は staleness により無効化せず、最後に受信した Gaze 値を保持します。FacialControl 以外の外部 OSC ソースには広告がないため、Gaze mapping を手動で設定してください。

## トラブルシューティング

- **何も動かない**: Hierarchy の `Character` 配下にモデルの `SkinnedMeshRenderer` が居るか確認。送信側から `/_facialcontrol/blendshape_names` heartbeat が届いているか、送信側と受信側の BlendShape 名が一致しているか確認（不一致のときは heartbeat 整合性検査が警告ログを出します）。
- **目線だけ動かない**: `/_facialcontrol/gaze` の広告が受信できているか、広告の `expressionId` と GazeConfig（既定は `eye_look`）が一致しているか、目ボーン path が設定されているかを確認してください。外部 OSC ソースの場合は Gaze mapping を手動設定します。Gaze だけの途絶時は最後に受信した値が保持されます。
- **送信側と同居して動かしたい**: `OscOutputDemo` 側の `OSC Sender` で `Suppress Loopback` を ✗ OFF にする必要があります。
