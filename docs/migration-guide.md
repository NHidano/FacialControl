# FacialControl 移行ガイド

本ドキュメントは preview 段階の破壊的変更に対するユーザー向け移行手順をまとめる。破壊的変更は `docs/requirements.md` FR-001（表情プロファイル管理）の「後方互換性: preview 段階では破壊的変更を許容。1.0.0 以降で互換性を担保」ポリシーに基づき許容されている。

各エントリは `FacialControl/Packages/com.hidano.facialcontrol/CHANGELOG.md` の「Breaking Changes」節と対応する。

---

## preview.1 系: layer-input-source-blending 導入に伴う破壊的変更

spec: [`.kiro/specs/layer-input-source-blending/`](../.kiro/specs/layer-input-source-blending/)

### 1. FacialProfile JSON `layers[].inputSources` の必須化

#### 影響

- `inputSources` を持たない既存プロファイル JSON はロード時に `FormatException` となり、`FacialController` が起動しない。
- 旧実装の「`inputSources` 欠落時に Expression パイプラインへ暗黙フォールバック (`legacy`)」は廃止された（spec D-5 / R3.2 / R7.3）。

#### 手順

1. `StreamingAssets/FacialControl/` 以下の全 `*_profile.json` を開く。
2. 各レイヤー（`layers[].{…}`）に `inputSources` 配列を追記する。
3. 典型的な初期値は以下のとおり。目的に応じて `id` と `weight` を選択する。

   ```json
   {
       "name": "emotion",
       "priority": 0,
       "exclusionMode": "lastWins",
       "inputSources": [
           { "id": "controller-expr", "weight": 1.0 }
       ]
   }
   ```

4. 予約 ID は以下の 5 種類（spec D-6 / R1.7）。
    - `controller-expr` — ゲームコントローラ由来の Expression トリガー
    - `keyboard-expr` — キーボード由来の Expression トリガー
    - `osc` — OSC 受信 BlendShape 値（`options.stalenessSeconds` で staleness タイムアウトをオプトイン指定可）
    - `lipsync` — `ILipSyncProvider` 由来のリップシンク
    - `input` — 将来拡張用プレースホルダ（現状未実装）
5. サードパーティ拡張アダプタの ID は `x-` プレフィックスで付与する（例: `x-mycompany-arm-sensor`）。識別子は `[a-zA-Z0-9_.-]{1,64}` を満たすこと。
6. `inputSources` 内で同じ `id` が複数回現れた場合は最後の出現が採用され、警告ログが出力される（spec R3.4）。
7. 旧 ID `legacy` は予約から外されている。`legacy` を使用しているエントリは `controller-expr` 等の具体的なアダプタ ID に置き換える。

#### 同梱サンプル

- `FacialControl/Assets/StreamingAssets/FacialControl/sample_profile.json`
- `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Templates/default_profile.json`

上記 2 ファイルは task 10.7 で移行済みであり、新スキーマの参考実装として参照できる。

### 2. 予約 ID `legacy` の廃止

#### 影響

- プロファイル JSON または拡張登録で `legacy` を使用していたコードはパーサーから警告を受け、当該エントリはスキップされる。
- 既存のランタイム API 経由で `SetInputSourceWeight("legacy", …)` のような呼び出しは対象 ID の不在ログで警告され no-op となる。

#### 手順

1. コードベースおよびプロファイル JSON 内の `"legacy"` 参照を検索する。
2. 「コントローラ操作を受け付けるレイヤー」に対応する参照は `controller-expr` に置換する。
3. 「キーボード操作を受け付けるレイヤー」に対応する参照は `keyboard-expr` に置換する。
4. Expression トリガーとして両デバイスを同時に受け付ける場合は 2 エントリ並記する:

   ```json
   "inputSources": [
       { "id": "controller-expr", "weight": 1.0 },
       { "id": "keyboard-expr",   "weight": 1.0 }
   ]
   ```

   同一レイヤーの両アダプタは独立した Expression スタック / TransitionCalculator を持ち、BlendShape 値レベルで加算される（spec D-1 / D-12 / D-13 / R1.6 / R1.8）。

### 3. `InputBindingProfileSO` への `InputSourceCategory` フィールド追加（既定値 Controller）

#### 影響

- `InputBindingProfileSO` アセットには新たに `_inputSourceCategory` フィールドが追加された。
- 既存 Asset には本フィールドが存在しないため、Unity の SerializedObject 機構によって初回ロード時に既定値 `InputSourceCategory.Controller` が暗黙的に付与される。
- 結果として、**キーボード専用バインディング** を持つ既存 Asset は意図せず `ControllerExpressionInputSource` 側へトリガーを流し始める挙動変更となる。

#### 手順

1. Unity Editor を起動し、Project ビューで検索欄に `t:InputBindingProfileSO` と入力して全ての `InputBindingProfileSO` Asset を列挙する。
2. 各 Asset を選択し、Inspector で `Input Source Category` の値を確認する。
3. 当該 Asset が参照している `InputActionAsset` / ActionMap のバインディングがどのデバイスを参照しているか確認する。
    - コントローラ（Gamepad / XboxController / PS4Controller など）のみ参照している場合: `Controller` のまま。
    - Keyboard のみ参照している場合: `Keyboard` に変更する。
    - 両方が混在している場合: 現状のスキーマでは 1 Asset = 1 カテゴリとしているため、Asset を複製してデバイスごとに分割することを推奨する。
4. `Controller` のまま保存された Asset のうち、ActionMap のバインディングが全て `<Keyboard>` を参照している場合は `OnValidate()` で警告ログが出力される（best-effort 検出）。警告が出た Asset は手順 3 に従って Category を再設定する。
5. 変更後の Asset はプロジェクトへコミットする。

#### チェックリスト

- [ ] 全 `InputBindingProfileSO` Asset の Category を一度ずつ目視確認した
- [ ] キーボード専用 Asset は `Keyboard` に変更済み
- [ ] Play Mode で意図したアダプタ（`ControllerExpressionInputSource` / `KeyboardExpressionInputSource`）のみが発火することを確認した
- [ ] OnValidate 警告ログが残っていない

#### 参考

- 実装: `FacialControl/Packages/com.hidano.facialcontrol/Runtime/Adapters/ScriptableObject/InputBindingProfileSO.cs`
- spec: R5.1 / R5.7 / R7.4（`.kiro/specs/layer-input-source-blending/requirements.md`）

---

## 参考: 破壊的変更ポリシー

- preview 系列（`0.x.x-preview.*`）では後方互換性の保証を行わない（`docs/requirements.md` FR-001）。
- 1.0.0 以降のマイナーバージョンアップでは破壊的変更を行わない。破壊的変更はメジャーバージョンアップ時にのみ発生し、本ドキュメントに追記される。
