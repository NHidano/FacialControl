# Migration Guide — Adapter Binding アーキテクチャ移行

`com.hidano.facialcontrol` および `com.hidano.facialcontrol.osc` / `com.hidano.facialcontrol.inputsystem` の preview 段階における破壊的改修（spec [`adapter-binding-architecture`](../../../../.kiro/specs/adapter-binding-architecture/)）に伴う、既存 `FacialCharacterSO` / `*FacialControllerExtension` MonoBehaviour 経路 / reserved id を前提とする scene / asset / JSON の手動移行手順をまとめる。

> **重要**: preview 段階ポリシー（`docs/requirements.md` FR-001）に基づき、自動マイグレーションは提供しない（Req 8.1, 8.3）。本ガイド記載の手順で **手動再作成** すること。
>
> Phase 1 並走期（`_adapterBindings` と旧 `IFacialControllerExtension` の同時利用が runtime warning 付きで許容された期間）は **終了**。`FacialController.Initialize` は無条件で per-FC `LifetimeScope` を build し、両経路同時検出時の `Debug.LogWarning` および empty-list ゲートは撤去された。

`docs/migration-guide.md`（リポジトリルート、preview.1 系の `layer-input-source-blending` 移行手順）および `com.hidano.facialcontrol.inputsystem/Documentation~/MIGRATION-v0.x-to-v1.0.md`（preview.1 → preview.2 の `inspector-and-data-model-redesign` 移行手順）と併読すること。本ガイドは **preview.2 → preview.3** の差分のみ扱う。

uLipSync の A / I / U / E / O 出力を Expression Overlay 経由へ移す preview 段階の破壊的変更は、`com.hidano.facialcontrol.lipsync` 側の [`phoneme-overlay-migration.md`](../../com.hidano.facialcontrol.lipsync/Documentation~/phoneme-overlay-migration.md) を参照すること。旧 `lipsync` Layer / `ulipsync` 直書き参照は自動変換されないため、既存 profile / sample / JSON は手動で `Slots`、Expression overlay、`Layer.inputSources` を更新する必要がある。

---

## 1. 削除型一覧（compile error 必至）

### 1.1 core パッケージ (`com.hidano.facialcontrol`)

| 削除型 | 置換先 | 備考 |
|--------|--------|------|
| `Hidano.FacialControl.Adapters.Playable.IFacialControllerExtension` | `Hidano.FacialControl.Domain.Adapters.AdapterBindingBase` 派生 + `[FacialAdapterBinding]` 属性 | MonoBehaviour Extension 経路は廃止。binding 自身が VContainer LifetimeScope に register される（Req 6.8） |
| `Hidano.FacialControl.Adapters.InputSources.InputSourceFactory` | `Hidano.FacialControl.Adapters.InputSources.IInputSourceRegistry` / `InputSourceRegistry` | `(id, options)` ディスパッチ + JSON deserialize + reserved id チェックは廃止。slug-keyed の `Register(slug, source)` / `TryResolve(...)` のみ（Req 6.10） |
| `Hidano.FacialControl.Domain.Models.InputSourceId.ReservedIds` | `Hidano.FacialControl.Domain.Models.AdapterSlug` | reserved id 概念ごと撤廃。slug は binding 由来で自動採番（Req 12.5, 12.6, D-13） |
| `Hidano.FacialControl.Domain.Models.InputSourceId.IsReserved` / `IsReservedId` | （消滅） | slug の bare-string 検証は `AdapterSlug.TryParse` に移管 |

### 1.2 OSC パッケージ (`com.hidano.facialcontrol.osc`)

| 削除型 | 置換先 | 備考 |
|--------|--------|------|
| `OscFacialControllerExtension` (MonoBehaviour) | `OscReceiverAdapterBinding`（`[Serializable]` + `[FacialAdapterBinding(displayName: "OSC")]`） | scene 上の component から SO 内 `_adapterBindings` 要素に移動（Req 6.9） |
| `OscRegistration` (static helper) | （消滅） | `InputSourceRegistry` への直接 `Register(slug, source)` 呼び出しが `OscReceiverAdapterBinding.OnStart` 内で行われる（Req 6.10） |

### 1.3 InputSystem パッケージ (`com.hidano.facialcontrol.inputsystem`)

| 削除型 | 置換先 | 備考 |
|--------|--------|------|
| `FacialCharacterSO` (派生 SO) | `FacialCharacterProfileSO`（core）+ `InputSystemAdapterBinding` を `_adapterBindings` に追加 | 単一継承モデル撤廃（Req 6.4） |
| `FacialCharacterInputExtension` (MonoBehaviour) | `InputSystemAdapterBinding` の `OnStart` / `OnLateTick` / `Dispose` | 旧 576 行 MonoBehaviour ロジックを binding lifecycle に責務分割（Req 6.8） |
| `InputFacialControllerExtension` (MonoBehaviour) | `InputSystemAdapterBinding` | scene component の追加が不要な「単一 SO ファイル UX」 |
| `InputRegistration` (static helper) | （消滅） | `InputSourceRegistry.Register` を binding 内で直接呼び出し |
| `FacialCharacterSOInspector` | `InputSystemAdapterBindingDrawer`（`[CustomPropertyDrawer(typeof(InputSystemAdapterBinding))]`） | core の `PropertyField` から自動解決 |
| `FacialCharacterSOAutoExporter` | （責務移管） | `FacialCharacterProfileExporter` + UnityEditor `AssetModificationProcessor` で旧経路は不要 |

---

## 2. Step-by-Step 移行手順

### 2.1 Backup（必須）

破壊的改修のため、移行に失敗しても rollback 可能なように先に backup を取る。

1. 対象プロジェクトを git commit でクリーン状態にする
2. `Assets/**/*.asset`（`FacialCharacterSO` 派生 + 旧 `FacialCharacterProfileSO` 派生）と `StreamingAssets/FacialControl/**/*.json` を別フォルダに複製
3. preview.2 の `com.hidano.facialcontrol` / `com.hidano.facialcontrol.osc` / `com.hidano.facialcontrol.inputsystem` の `packages-lock.json` バージョンを記録（rollback 用）

### 2.2 旧 SO の再作成（自動マイグレーションなし）

旧 `FacialCharacterSO` (inputsystem 派生) は schema レベルで `_adapterBindings` を持たないため、手動で `FacialCharacterProfileSO` を再作成する。

1. Project ウィンドウで右クリック → **Create** → **FacialControl** → **Facial Character Profile** で新 `FacialCharacterProfileSO` を作成
2. 旧 SO の Layers / Expressions / Renderer Paths を新 SO の対応セクションに転記する（Inspector の表示構造は維持されている）
3. Inspector 末尾の **Adapter Bindings** セクションで **Add** ボタンを押し、必要な adapter binding を追加する
   - `Input System`（`com.hidano.facialcontrol.inputsystem` を導入している場合）
   - `OSC`（`com.hidano.facialcontrol.osc` を導入している場合）
   - `ARKit / PerfectSync`（`com.hidano.facialcontrol.osc` の ARKit binding）
4. 各 binding の inline UI（`InputActionAsset` / endpoint / port / blendshape マッピング 等）を旧 SO / 旧 Extension MonoBehaviour の値で埋める
5. 旧 SO は不要になり次第削除する（`FacialCharacterSO` 型は compile error になるため、新 SO への移行完了後に旧 `.asset` を削除）

### 2.3 Scene 上の MonoBehaviour Extension 撤去

旧 `*FacialControllerExtension` MonoBehaviour 群はすべて廃止された。以下の手順で scene を整理する。

1. scene を開き `FacialController` コンポーネントを持つ GameObject を選択
2. 同 GameObject にアタッチされた以下の component を **Remove Component** で削除する
   - `OscFacialControllerExtension`（OSC 経路）
   - `FacialCharacterInputExtension` / `InputFacialControllerExtension`（InputSystem 経路）
   - その他 `IFacialControllerExtension` 派生の自前 MonoBehaviour（撤廃。後述 2.4 で置換）
3. `FacialController.CharacterSO` フィールドを 2.2 で作成した新 `FacialCharacterProfileSO` に差し替える
4. scene を保存

> **OSC HelperHost の補足**: `OscReceiverAdapterBinding` は `OnStart` で `OscReceiverHost` を `AddComponent` する。Inspector で見える状態で残るが、これは仕様（Req 13.6）。Disable / Remove はしないこと。`Dispose` 時に `OscReceiverAdapterBinding` 自身が `Object.Destroy` する。

### 2.4 自前の `IFacialControllerExtension` 派生からの移植

ユーザー独自に `IFacialControllerExtension` を実装していた場合は、`AdapterBindingBase` 派生に置き換える。

```csharp
// Before（preview.2 以前）
public class MyCustomExtension : MonoBehaviour, IFacialControllerExtension
{
    public void Configure(InputSourceFactory factory)
    {
        factory.RegisterReserved<MyOptions>("my-source", BuildSource);
    }
}

// After（preview.3 以降）
[Serializable]
[FacialAdapterBinding(displayName: "My Custom")]
public sealed class MyCustomAdapterBinding : AdapterBindingBase
{
    [SerializeField] private MyOptions _options;
    private MyInputSource _source;

    public override void OnStart(in AdapterBuildContext ctx)
    {
        _source = new MyInputSource(_options);
        ctx.InputSourceRegistry.Register(AdapterSlug.Parse(Slug), _source);
    }

    public override void Dispose()
    {
        _source?.Dispose();
    }
}

#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(MyCustomAdapterBinding))]
internal sealed class MyCustomAdapterBindingDrawer : PropertyDrawer
{
    // UI Toolkit ベースで _options の編集 UI を提供（IMGUI 新規追加禁止）
}
#endif
```

ポイント:

- **属性駆動**: `[FacialAdapterBinding(displayName: "My Custom")]` を付けるだけで core Inspector の Add ドロップダウンに自動列挙される。core の変更は一切不要（Req 1.5, 1.6, 4）
- **slug 自動採番**: Add 時に `displayName.ToLowerInvariant()` の kebab-case（`"My Custom"` → `"my-custom"`）が `Slug` field に自動入力される。手動編集も可能。同一 SO 内 slug 重複は Inspector が error 表示 + save block する
- **lifecycle**: `OnStart` / `OnTick` / `OnLateTick` / `OnFixedTick` / `Dispose` から必要なものだけ override（virtual no-op）。例外時は core が catch + `Debug.LogError` + 以後 skip（Req 13.4, 13.5）
- **MonoBehaviour helper**: Unity Update Loop が必要な場合のみ `OnStart` 内で `ctx.HostGameObject.AddComponent<MyHelper>()` し、`Dispose` で `Object.Destroy` する（Req 13.6, 13.7）

### 2.5 `layer.inputSources[].id` の slug 化（Req 12.7）

旧 reserved id（`controller-expr` / `keyboard-expr` / `osc` / `lipsync` / `input` 等）は廃止された。プロファイル JSON および SO 内 `InputSources` 設定を slug 形式に書き換える。

#### 2.5.1 ID 対応表

| 旧 reserved id | 新 slug | 備考 |
|---------------|---------|------|
| `controller-expr` | `input-system` または `input-system:controller` | `InputSystemAdapterBinding` に統合。device 種別は `InputSystem.Action` の `bindingPath` で識別される |
| `keyboard-expr` | `input-system` または `input-system:keyboard` | 同上。1 binding に統合済みの場合は `<slug>` のみで参照可 |
| `input` | `input-system` | 旧仕様の暫定統一名。新 slug は binding displayName 由来 |
| `osc` | `osc` | `OscReceiverAdapterBinding` の slug が `displayName: "OSC"` → `"osc"` に自動採番される（変更なし、ただし binding 由来である点が新仕様） |
| `osc:secondary` 等 | `osc:secondary` | 同 SO に `OscReceiverAdapterBinding` を 2 個並置するケースは `slug:sub` 形式で識別（Req 12.4） |
| `arkit` / `arkit-perfectsync` | `arkit-perfectsync` | `ArKitOscAdapterBinding` の slug |
| `lipsync` | `lipsync` または `lip-sync` | binding を提供するパッケージの `[FacialAdapterBinding]` displayName による |
| `analog-blendshape` / `analog-bonepose` | （binding-defined） | binding が register する sub-id に依存。各 binding の README を参照 |
| `legacy` | （消滅） | preview.1 系で既に廃止済み（`docs/migration-guide.md` 参照） |
| `x-mycompany-*` | `x-mycompany-*` | 第三者拡張 prefix は継続使用可能 |

#### 2.5.2 JSON 書換手順

1. `StreamingAssets/FacialControl/**/*.json` を全て開く
2. 各 layer の `inputSources[].id` を新 slug に書き換える

   ```json
   // Before
   {
       "name": "emotion",
       "priority": 0,
       "exclusionMode": "lastWins",
       "inputSources": [
           { "id": "controller-expr", "weight": 1.0 },
           { "id": "keyboard-expr",   "weight": 1.0 }
       ]
   }

   // After（InputSystemAdapterBinding に統合済み）
   {
       "name": "emotion",
       "priority": 0,
       "exclusionMode": "lastWins",
       "inputSources": [
           { "id": "input-system", "weight": 1.0 }
       ]
   }
   ```

3. `inputSources[].id` の文字列は `[a-zA-Z0-9_.-]{1,64}` を満たすこと（既存ルール継続、Req 12.6）
4. 同一 layer 内で同じ id が複数現れた場合は最後の出現が採用され、警告ログが出力される（既存挙動継続）

#### 2.5.3 SO 上の参照書換

1. Unity Editor で `FacialCharacterProfileSO` の Inspector を開く
2. **Layers** セクションで各レイヤーの **Input Sources** を展開
3. 旧 reserved id を表示している行を選択し、`Id` を 2.5.1 の対応表に従って書き換える
4. **Adapter Bindings** セクション側の各 binding `Slug` 値と一致しているか確認する
5. 重複 slug がある場合は Inspector 上端に summary banner が出るため、binding 側 slug を重複しない値に書き換える
6. SO を保存（重複 slug が残っている場合 `AssetModificationProcessor` が save をブロックする）

### 2.6 gaze-channel-redesign に伴う Gaze セクションへの移行

この spec では `ExpressionSerializable.isGaze` と旧 `_gazeConfigs` / `gaze_configs[]` を廃止し、`FacialCharacterProfileSO` の `GazeChannels`（既定 id は常に `gaze`）へ統合した。preview 段階の破壊的変更であり、自動変換は行わない。対象は Fork 実機 `D:\Unvgi\Repositries\UnvgiFacialVerification` を含む、旧 Profile を使用するすべてのプロジェクトである。

#### 2.6.1 Profile / JSON

1. Profile をバックアップし、旧 `isGaze` Expression と旧 gaze 設定を記録する。
2. Inspector の Gaze セクションで既定チャネル `gaze` を確認し、旧設定の左右目ボーン path、初期回転、軸、可動角を手動で再入力する。追加系統はチャネルを追加し、id の重複がないことを確認する。
3. `profile.json` は root の `gaze.channels[]` にチャネルを記述する。旧 `gaze_configs[]` は削除し、Expression から `isGaze` を削除する。`gaze` が無い新規 JSON は既定チャネルが補完される。
4. 旧 JSON が検出された場合は警告後に gaze 部分だけ読み捨てられるため、警告を無視して使い続けない。

```json
// 新スキーマの最小例
{
    "schemaVersion": "1.0",
    "layers": [],
    "expressions": [],
    "rendererPaths": []
}
```

```json
// 新スキーマでの移行後
{
    "schemaVersion": "1.0",
    "layers": [],
    "expressions": [],
    "rendererPaths": [],
    "gaze": { "channels": [{
        "id": "gaze",
        "providerSlug": "",
        "useDistinctLeftRight": false,
        "sourceIdLeft": "",
        "sourceIdRight": "",
        "leftEyeBonePath": "Armature/Hips/Spine/Head/LeftEye",
        "rightEyeBonePath": "Armature/Hips/Spine/Head/RightEye"
    }]}
}
```

#### 2.6.2 Binding / source id

 - InputSystem の Gaze entry は `expressionId` ではなく、Gaze セクションのチャネル `id`（既定は `gaze`）を選択する。`DeclaredInputs` と registry の source id もチャネル起点になる。
 - OSC sender の options JSON から `gazeExpressionIds` を削除する。送信するチャネルは Profile の Gaze セクションから注入される。
 - iFacialMocap / OSC / InputSystem / Timeline の binding は `IGazeSourceProvider` / `IGazeChannelConsumer` 契約に従う。既定チャネルの左右 source id は `{slug}:gaze.left` / `{slug}:gaze.right` である。
 - `useDistinctLeftRight` を有効にする場合は `sourceIdLeft` / `sourceIdRight` を現在の binding が宣言する id に更新する。旧 actionName 由来 id は解決できない。

### 2.7 サンプル / Demo の確認

- **core 同梱 `Samples~/MultiSourceBlendBasicSample/`**（HUD なし、Mock binding 2 種、`Tools > FacialControl > Run MultiSourceBlend Basic Sample` メニュー）で MultiSourceBlend の最小動作を確認できる
- **`com.hidano.facialcontrol.inputsystem/Samples~/MultiSourceBlendDemo/`** は HUD が slug 駆動に書き換え済み。旧サンプルを Import 済みの場合は **Reimport** で最新版に差し替える
- 二重管理ミラー `Assets/Samples/com.hidano.facialcontrol/.../` を併用している場合は、両方のコピーを同期更新すること（CLAUDE.md `## Samples の二重管理ルール`）

---

## 3. 検証チェックリスト

移行完了後に以下を確認する。

- [ ] プロジェクト全体に `IFacialControllerExtension` / `InputSourceFactory` / `InputSourceId.ReservedIds` / `IsReservedId` / `IsReserved` / `OscFacialControllerExtension` / `FacialCharacterSO` / `FacialCharacterInputExtension` / `InputFacialControllerExtension` / `OscRegistration` / `InputRegistration` / `FacialCharacterSOInspector` / `FacialCharacterSOAutoExporter` の参照が残っていない（compile error がゼロ）
- [ ] scene 上の `FacialController` GameObject から `*FacialControllerExtension` MonoBehaviour 群が全て削除されている
- [ ] `FacialCharacterProfileSO` の Inspector で **Adapter Bindings** セクションに必要な binding が列挙され、slug が重複していない（summary banner なし）
- [ ] `StreamingAssets/FacialControl/**/*.json` の `inputSources[].id` がすべて新 slug 形式に書き換わっている
- [ ] Profile の Gaze セクションに既定チャネル `gaze` と必要な追加チャネルがあり、旧 `gaze_configs[]` / `isGaze` が残っていない
- [ ] InputSystem / OSC / iFacialMocap / Timeline の binding がチャネル id と宣言 source id を使用している
- [ ] Play Mode で表情遷移 / OSC 受信 / InputSystem トリガー / リップシンクが期待どおり動作する
- [ ] `Tests/PlayMode` 配下の 0-alloc perf test と統合テストが green（独自テストを保持している場合）

---

## 4. ロールバック

移行に失敗した場合は以下の手順で preview.2 に戻せる。

1. `git stash` または `git reset --hard` で 2.1 の clean state に戻す
2. `Packages/manifest.json` の以下を preview.2 系のバージョンに固定する
   - `com.hidano.facialcontrol`
   - `com.hidano.facialcontrol.osc`
   - `com.hidano.facialcontrol.inputsystem`
3. `packages-lock.json` を 2.1 で記録した内容に戻す
4. Unity Editor を再起動

> **注意**: preview.3 で作成された `FacialCharacterProfileSO`（`_adapterBindings` を含む）は preview.2 系では schema 互換性がなく、`_adapterBindings` field を持たない旧 schema にロードされた場合の挙動は未定義。preview.3 で編集した SO は preview.2 系で開かないこと。

---

## 5. 参考

- spec: [`.kiro/specs/adapter-binding-architecture/`](../../../../.kiro/specs/adapter-binding-architecture/)
  - `requirements.md` Req 6.4 / 6.7 / 6.8 / 6.9 / 6.10 / 8.1-8.5 / 12.5-12.7
  - `design.md` `## Migration Strategy > Phase 動作契約マトリクス`
- preview.1 系の移行: [`docs/migration-guide.md`](../../../../docs/migration-guide.md)
- preview.1 → preview.2 の移行: [`com.hidano.facialcontrol.inputsystem/Documentation~/MIGRATION-v0.x-to-v1.0.md`](../../com.hidano.facialcontrol.inputsystem/Documentation~/MIGRATION-v0.x-to-v1.0.md)
- uLipSync phoneme overlay 移行: [`com.hidano.facialcontrol.lipsync/Documentation~/phoneme-overlay-migration.md`](../../com.hidano.facialcontrol.lipsync/Documentation~/phoneme-overlay-migration.md)
- preview 段階の破壊的変更ポリシー: `docs/requirements.md` FR-001（`0.x.x-preview.*` では後方互換保証なし、1.0.0 以降のマイナーアップデートでは破壊的変更なし）
