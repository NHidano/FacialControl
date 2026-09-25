# Migration Guide v0.x → v1.0

`com.hidano.facialcontrol` および `com.hidano.facialcontrol.inputsystem` の preview 段階における破壊的改修（`inspector-and-data-model-redesign`）に伴う、既存 `FacialCharacterSO` / JSON / Scene / Prefab の手動移行手順をまとめます。

> **重要**: v1.0（schema v2.0）は v0.x（schema v1.0）の SO / JSON を一切ロードできません。`SystemTextJsonParser` は `schemaVersion != "2.0"` を `Debug.LogError` + `InvalidOperationException` で拒否します。アップグレード前に必ず本ドキュメントの手順で資産を変換してください（Req 10.1）。

---

## 1. Schema 差分一覧表

### 1.1 Domain モデルの変更

| 区分 | v0.x（旧） | v1.0（新） | 備考 |
|------|-----------|-----------|------|
| 撤去 | `Hidano.FacialControl.Domain.Models.LayerSlot`（layer 名 + per-layer params） | `LayerOverrideMask`（`[Flags] enum : int`、32 bit） | bit position と layer 名の対応表は Adapters / Editor 側ヘルパが保持（Domain は関知しない）|
| 撤去 | `Expression.TransitionDuration` / `Expression.TransitionCurve` / `Expression.BlendShapeValues` / `Expression.RendererPath` / `Expression.LayerSlots`（独立 field） | `Expression.SnapshotId` への参照に集約。値は AnimationClip からサンプリングして `ExpressionSnapshot` に格納 | AnimationClip が真実の源 |
| 撤去 | `FacialProfile.BonePoses`、`Domain.Models.BonePose` / `BonePoseEntry` | （消滅）`ExpressionSnapshot.Bones`（`ReadOnlyMemory<BoneSnapshot>`）に統合 | bone pose は AnimationClip Transform curve から派生 |
| 撤去 | `Domain.Models.AnalogMappingFunction` / `Domain.Services.AnalogMappingEvaluator` | （消滅）InputAction Asset の processors チェーンに置換 | `AnalogBindingEntry` は `inputActionRef + targetIdentifier + targetAxis` のみに縮退 |
| 新設 | — | `BlendShapeSnapshot`（`RendererPath / Name / Value`）、`BoneSnapshot`（`BonePath / Position(X,Y,Z) / Euler(X,Y,Z) / Scale(X,Y,Z)`） | `readonly struct`、AnimationClip 由来の不変スナップショット |
| 新設 | — | `ExpressionSnapshot`（`Id / TransitionDuration / TransitionCurvePreset / BlendShapes / Bones / RendererPaths`） | コンストラクタで防御コピー、`ReadOnlyMemory<T>` で公開 |
| 新設 | — | `TransitionCurvePreset` enum（`Linear=0 / EaseIn=1 / EaseOut=2 / EaseInOut=3`） | `TransitionCurve` enum は撤去 |
| 新設 | — | `Domain.Services.ExpressionResolver`（`TryResolve(snapshotId, Span<float>, Span<BoneSnapshot>)`） | 0-alloc な preallocated 解決経路 |

### 1.2 InputSystem 連携の変更

| 区分 | v0.x（旧） | v1.0（新） | 備考 |
|------|-----------|-----------|------|
| 撤去 | `KeyboardExpressionInputSource` / `ControllerExpressionInputSource` MonoBehaviour | `ExpressionInputSourceAdapter` 1 個に統合 | `[DisallowMultipleComponent]`。device は `InputBinding.path` から自動推定 |
| 撤去 | `ExpressionBindingEntry.Category`（`Controller` / `Keyboard` enum） | （消滅）`InputDeviceCategorizer.Categorize(bindingPath)` で自動分類 | 既存 SO の category field は読み込み時に無視（schema v2.0 に load 不可なので実質的には移行で消滅）|
| 撤去 | `InputSourceCategory` enum | （消滅、または OSC 別 spec で再導入） | grep で参照ゼロを確認済み |
| 撤去 | `FacialCharacterSO.GetExpressionBindings(InputSourceCategory category)` | `FacialCharacterSO.GetExpressionBindings()`（引数なし） | adapter 側が dispatch を吸収 |
| 撤去 | `AnalogBindingEntry.Mapping`（`AnalogMappingFunction` 埋め込み） | （消滅）`*.inputactions` 内の processors 文字列に移行 | 後述「2.4 AnalogMappingFunction → InputAction processors」を参照 |
| 新設 | — | 6 種カスタム InputProcessor: `analogDeadZone` / `analogScale` / `analogOffset` / `analogClamp` / `analogCurve` / `analogInvert` | `AnalogProcessorRegistration` 静的クラスが Editor / Runtime 双方で `InputSystem.RegisterProcessor` 一括登録 |
| 新設 | — | `Adapters.Input.InputDeviceCategorizer.Categorize(string bindingPath, out bool wasFallback)` | `<Keyboard>` prefix → Keyboard、`<Gamepad>` / `<Joystick>` / `<XRController>` / `<Pen>` / `<Touchscreen>` → Controller、未認識は Controller fallback + warning（Req 7.5） |

### 1.3 中間 JSON schema の変更

| field | v0.x (`schemaVersion: "1.0"`) | v1.0 (`schemaVersion: "2.0"`) |
|-------|------------------------------|------------------------------|
| `schemaVersion` | `"1.0"` | `"2.0"`（strict、不一致は `InvalidOperationException`） |
| `expressions[].transitionDuration` | あり | 撤去（`expressions[].snapshot.transitionDuration` に移動） |
| `expressions[].transitionCurve` | あり | 撤去（`expressions[].snapshot.transitionCurvePreset: int`（0..3）に移動） |
| `expressions[].blendShapeValues[]` | あり | 撤去（`expressions[].snapshot.blendShapes[]` に移動）|
| `expressions[].layerSlots[]` | layer 名 + per-layer params | 撤去 → `expressions[].layerOverrideMask: ["layer-a", "layer-b"]`（layer 名配列、bit に展開）|
| `expressions[].snapshot.blendShapes[]` | — | 新設（`{ rendererPath, name, value }`）|
| `expressions[].snapshot.bones[]` | — | 新設（`{ bonePath, position:{x,y,z}, euler:{x,y,z}, scale:{x,y,z} }`）|
| `expressions[].snapshot.rendererPaths[]` | — | 新設（top-level `rendererPaths[]` の subset）|
| top-level `rendererPaths[]` | — | 新設（全 Expression を通じて出現する renderer path の和集合）|
| `bonePoses[]`（profile 直下） | あり | 撤去（Expression snapshot に統合）|
| `analogBindings[].mapping` | `AnalogMappingFunction` インライン | 撤去 → InputAction Asset の processors 文字列で表現 |
| `analogBindings[].inputActionRef` | あり | 維持 |
| `analogBindings[].targetIdentifier` | あり | 維持 |
| `analogBindings[].targetAxis` | あり | 維持 |
| `expressionBindings[].category` | あり（`"Controller"` / `"Keyboard"`）| 撤去 |

---

## 2. Step-by-Step 移行手順

### 2.1 Backup（必須）

破壊的改修のため、移行に失敗しても rollback 可能なように先に backup を取ります。

1. 対象プロジェクトを git commit でクリーン状態にする
2. `Assets/**/*.asset`（FacialCharacterSO 派生）と `StreamingAssets/FacialControl/**/*.json` を別フォルダに複製
3. v0.x の `com.hidano.facialcontrol` / `com.hidano.facialcontrol.inputsystem` の packages-lock.json バージョンを記録（rollback 用）

> **Tip**: v0.x の SO / JSON のスナップショットを含む git tag を切っておくと、移行中の差分参照が容易です。

### 2.2 LayerSlot.blendShapeValues[] → AnimationClip 結合

旧 `Expression.BlendShapeValues` / `LayerSlot.blendShapeValues` 配列を Unity の AnimationClip に焼き直します。

1. Project ウィンドウで右クリック → **Create** → **Animation** で空の `AnimationClip` を作成（推奨命名: `expr_<expression-id>.anim`）
2. Animation ウィンドウで対象キャラクター GameObject を選択し、`SkinnedMeshRenderer` の `blendShape.<BlendShapeName>` を curve に追加
3. v0.x の `BlendShapeValues[i].Value`（`float`、0..100 が一般的）を **時刻 0 のキーフレーム値** として登録（`AnimationCurve.Constant(0f, 0f, value)` 相当）
4. すべての BlendShape 値を 1 つの AnimationClip にまとめる（複数レイヤーに分けない。レイヤー所属は後述 `LayerOverrideMask` で表現する）
5. 後述 [3. AnimationClip メタデータ運搬規約](#3-animationclip-メタデータ運搬規約-facialcontrolmeta_set-予約名) に従い `transitionDuration` / `transitionCurvePreset` を AnimationEvent で埋める

> **注**: Editor 保存時に `IExpressionAnimationClipSampler` が `AnimationUtility.GetEditorCurve(clip, binding).Evaluate(0f)` で時刻 0 の値を抽出します。中間値や複数キーフレームは **使用されません**（snapshot 形式）。アニメーション中の補間カーブを使いたい場合は将来の Timeline 統合を待つか、別 Expression として分割してください。

### 2.3 FacialProfile.BonePoses[] → AnimationClip Transform curves

v0.x の `BonePoseEntry`（`bonePath / position / euler / scale`）を AnimationClip の Transform curve に焼き直します。

1. Animation ウィンドウで該当ボーン（`Hips/Spine/...` など Animator path で指定）を選択
2. 以下 9 軸のキーフレームを時刻 0 に登録:
   - `m_LocalPosition.x` / `m_LocalPosition.y` / `m_LocalPosition.z`
   - `m_LocalRotation.x` / `m_LocalRotation.y` / `m_LocalRotation.z`（Euler。Quaternion を使う場合は `m_LocalEulerAnglesHint.{x,y,z}` も併用）
   - `m_LocalScale.x` / `m_LocalScale.y` / `m_LocalScale.z`
3. `Expression` あたり 1 つの AnimationClip に BlendShape curve と Transform curve を共存させる（Expression 単位で 1 clip）

サンプラ判別ロジック（Editor 専用）:
- BlendShape: `binding.propertyName.StartsWith("blendShape.")`
- Transform: `m_LocalPosition.{x,y,z}` / `m_LocalRotation`系 / `m_LocalScale.{x,y,z}`
- それ以外: `Debug.LogWarning` + skip（Req 2.7）

> **Tip**: 旧 BonePose を Inspector でチクチク手入力していた場合、Animation ウィンドウの **Record モード** + 実機ポージングのほうが圧倒的に速いです。

### 2.4 AnalogMappingFunction → InputAction processors 文字列対応表

旧 `AnalogMappingFunction(deadZone, scale, offset, curve, invert, min, max)` を、対象 `*.inputactions` の対象 binding に **処理順** で processor を連結します。適用順は v0.x の `dead-zone(re-center) → scale → offset → curve → invert → clamp(min, max)` を維持してください。

processors 文字列フォーマット: `name(param=value),name(param=value),...`（Unity InputSystem 標準）

#### 対応表

| v0.x（`AnalogMappingFunction` の field）| v1.0（processors 文字列） | パラメータ対応 |
|--------------------------------------|--------------------------|---------------|
| `DeadZone = d`（>0） | `analogDeadZone(min=d,max=1)` | abs <= min は 0、(abs - min) / (max - min) で正規化（再センタ） |
| `Scale = s` | `analogScale(factor=s)` | `value * factor` |
| `Offset = o` | `analogOffset(offset=o)` | `value + offset` |
| `Curve = Linear` | （省略可、preset=0） | identity |
| `Curve = EaseIn` | `analogCurve(preset=1)` | `v * v` |
| `Curve = EaseOut` | `analogCurve(preset=2)` | `1 - (1 - v)^2` |
| `Curve = EaseInOut` | `analogCurve(preset=3)` | `v^2 * (3 - 2v)`（SmoothStep） |
| `Curve = Custom (Hermite)` | **直接対応なし**（v2.1 以降の検討事項、OQ3） | 暫定対応として preset 4 種からの近似を選ぶか、複数 processor 直列で代替 |
| `Invert = true` | `analogInvert()` | `-value` |
| `Min = lo, Max = hi` | `analogClamp(min=lo,max=hi)` | `Mathf.Clamp(value, min, max)` |

#### 変換例

##### 例 1: deadzone のみ

- v0.x: `new AnalogMappingFunction(deadZone: 0.15f, scale: 1f, offset: 0f, curve: Linear, invert: false, min: 0f, max: 1f)`
- v1.0 processors 文字列: `analogDeadZone(min=0.15,max=1)`
- 用途: スティック中央のドリフト除去

##### 例 2: scale + clamp

- v0.x: `new AnalogMappingFunction(deadZone: 0f, scale: 2f, offset: 0f, curve: Linear, invert: false, min: 0f, max: 1f)`
- v1.0 processors 文字列: `analogScale(factor=2),analogClamp(min=0,max=1)`
- 用途: 半押しでフルスケール、超過分はクランプ

##### 例 3: offset + curve preset

- v0.x: `new AnalogMappingFunction(deadZone: 0f, scale: 1f, offset: 0.1f, curve: EaseIn, invert: false, min: 0f, max: 1f)`
- v1.0 processors 文字列: `analogOffset(offset=0.1),analogCurve(preset=1),analogClamp(min=0,max=1)`
- 用途: 弱入力時の感度を抑えつつ最低 0.1 を保証

##### 例 4: deadzone + scale + invert + clamp（フル適用）

- v0.x: `new AnalogMappingFunction(deadZone: 0.1f, scale: 1.5f, offset: 0f, curve: EaseOut, invert: true, min: -1f, max: 1f)`
- v1.0 processors 文字列: `analogDeadZone(min=0.1,max=1),analogScale(factor=1.5),analogCurve(preset=2),analogInvert(),analogClamp(min=-1,max=1)`
- 用途: ジョイスティック上下反転 + 立ち上がり緩和

> **検証手順**: Unity Editor で `*.inputactions` を開き、対象 binding の **Processors** 欄に上記文字列を入力。Properties パネルで各 processor の field 値が正しく表示されるか確認してください。`analogScale` 等が **Unknown processor** と表示される場合は、Editor をドメインリロード（スクリプトの再コンパイル）して `AnalogProcessorRegistration` の `[InitializeOnLoad]` フックを発火させてください。

### 2.5 ExpressionBindingEntry.category 削除

v0.x で `category: "Controller"` / `"Keyboard"` を持っていた `ExpressionBindingEntry` は、v1.0 では category field 自体が消滅します。

1. v0.x の SO に存在する `category` field は v1.0 ロード時に schema v2.0 拒否で再ロード不可
2. v2.0 schema で SO を作り直す際、category 指定は不要（`InputDeviceCategorizer` が `InputAction.bindings[0].path` から自動分類）
3. 既存コードで `GetExpressionBindings(InputSourceCategory.Keyboard)` のように category を引数で渡している箇所は、`GetExpressionBindings()`（引数なし）に書き換える

```csharp
// v0.x
foreach (var entry in soInputs.GetExpressionBindings(InputSourceCategory.Keyboard))
{
    // keyboard 専用処理
}

// v1.0
foreach (var entry in soInputs.GetExpressionBindings())
{
    // adapter が device 推定で dispatch するため category 分岐は不要
}
```

### 2.6 KeyboardExpressionInputSource / ControllerExpressionInputSource → ExpressionInputSourceAdapter 差し替え

Scene / Prefab に配置済みの旧コンポーネントを `ExpressionInputSourceAdapter` 1 個に置換します。

#### 既存 Scene / Prefab に対する差し替え手順

1. 対象 Scene / Prefab を開く
2. `KeyboardExpressionInputSource` および `ControllerExpressionInputSource` が attach されている GameObject を選択
3. Inspector 上で両 Component の `Reset` 状態（依存先 SO 参照など）を控える
4. 両 Component を **Remove Component** で削除
5. **Add Component** で `ExpressionInputSourceAdapter` を追加（`[DisallowMultipleComponent]` のため 1 GameObject に 1 つだけ）
6. adapter の SO 参照欄に旧 Component と同じ `FacialCharacterSO` を割り当て
7. Save Scene / Apply Prefab

> **注**: adapter は内部に keyboard 用 / controller 用の `ExpressionTriggerInputSourceBase` インスタンスを composition で保持しています（D-12 既存挙動）。InputAction の `bindings[0].path` を `InputDeviceCategorizer` で分類して dispatch するため、外部から見た挙動は v0.x の 2 Component 配置と等価です。

#### コード経路の差し替え

```csharp
// v0.x: 個別 InputSource を直接 InputSourceFactory に登録
factory.RegisterReserved(new KeyboardExpressionInputSource(...));
factory.RegisterReserved(new ControllerExpressionInputSource(...));

// v1.0: adapter が内部で両 sink を保持。MonoBehaviour 配置のみで完結
// (CodePath からの直接登録経路は撤去)
```

`OnEnable` / `OnDisable` の subscription leak を避けるため、adapter を有効/無効に切り替えるテストでは `MultipleEnableDisable_DoesNotLeakSubscriptions` 相当の挙動を保証しています（Req 8.4）。

---

## 3. AnimationClip メタデータ運搬規約（`FacialControlMeta_Set` 予約名）

`TransitionDuration` / `TransitionCurvePreset` は AnimationClip の curve として表現できないため、**予約 AnimationEvent** で運搬します。

### 規約

- **functionName**: `FacialControlMeta_Set`（変更すると runtime / editor 双方で抽出に失敗します）
- **stringParameter**: メタデータキー
  - `"transitionDuration"` — `floatParameter` を秒数として採用（Req 2.5）
  - `"transitionCurvePreset"` — `floatParameter` を `(int)` キャストして preset 番号として採用（0=Linear / 1=EaseIn / 2=EaseOut / 3=EaseInOut、Req 2.6）
- **floatParameter**: 値運搬
- **time**: 0（推奨）。サンプラは時刻 0 値のみを参照
- **重複時の挙動**: 同一 stringParameter を持つ AnimationEvent が複数存在する場合、**最初の 1 個のみ採用**（Req 2.4）し `Debug.LogWarning` を 1 回ログ
- **不在時の fallback**: `transitionDuration = 0.25f` / `transitionCurvePreset = Linear (0)`（Req 2.5, 2.6）

### Editor で AnimationEvent を埋める手順

1. AnimationClip を Project ウィンドウで選択
2. Animation ウィンドウの上部タイムラインで時刻 0 にカーソルを合わせる
3. **Add Event** ボタン（旗アイコン）でイベントを追加
4. Inspector で:
   - **Function** に `FacialControlMeta_Set` を入力
   - **String** に `"transitionDuration"` を入力
   - **Float** に秒数（例: `0.35`）を入力
5. もう 1 つイベントを追加し、同様に `transitionCurvePreset` + preset 番号を埋める

> **Tip**: `Editor/Tools/ExpressionCreatorWindow` を使うと、専用 UI（foldout）から TransitionDuration / Preset を編集して AnimationClip にベイクできます（タスク 5.2）。

### コード経路（参考、Editor 専用）

```csharp
// 例: ExpressionClipBakery 相当の処理
var events = new List<AnimationEvent>(AnimationUtility.GetAnimationEvents(clip));
events.Add(new AnimationEvent
{
    time = 0f,
    functionName = "FacialControlMeta_Set",
    stringParameter = "transitionDuration",
    floatParameter = 0.35f
});
events.Add(new AnimationEvent
{
    time = 0f,
    functionName = "FacialControlMeta_Set",
    stringParameter = "transitionCurvePreset",
    floatParameter = (int)TransitionCurvePreset.EaseInOut, // 3
});
AnimationUtility.SetAnimationEvents(clip, events.ToArray());
```

---

## 4. 既知の制限

v1.0 の preview 段階で承知の上で導入している制約をまとめます。

1. **schema v1.0 ロード不可**: `SystemTextJsonParser` は `schemaVersion != "2.0"` を拒否します。v0.x JSON を強制的に流し込みたい場合でも自動互換は提供しません（Req 10.1）。
2. **Custom Hermite Curve 非対応**: `AnalogCurveProcessor` は preset 4 種（Linear / EaseIn / EaseOut / EaseInOut）のみサポートします。v0.x の `TransitionCurve = Custom`（Hermite 補間）は v2.1 以降のスコープです（Decision 3、OQ3）。preview 利用者からのフィードバック次第で複数 processor 直列または独自拡張を再評価します。
3. **AnimationClip の中間値は無視**: サンプラは時刻 0 の値のみを参照します。アニメーション中の補間カーブを Expression として扱いたい場合は別 Expression として分割するか、Timeline 統合（将来マイルストーン）を待ってください。
4. **AnimationClip サンプリングは Editor 専用**: Runtime には AnimationUtility 系 API を呼ばせません（Req 9.4, 11.2）。Runtime は Editor 保存時に書き出された中間 JSON v2.0 のみを読みます。
5. **InputProcessor の AnimationCurve 非対応**: `InputProcessor` の serializer 制約により AnimationCurve / string は serialize 不可です。`analogCurve` は `int preset` field のみで運搬します（design.md Topic 3）。
6. **`Domain.Models.LayerInputSources` の round-trip**: v0.x で `layers[i].inputSources` を `FacialProfile.LayerInputSources` で再現していた仕組みは、v2.0 schema での扱いを Phase 2 着手時点で再確定（OQ6）。preview ユーザー側でカスタム拡張していた場合は別途相談してください。
7. **`InputSourceCategory` の OSC 別 spec 取り扱い**: v1.0 では `com.hidano.facialcontrol.inputsystem` 配下から `InputSourceCategory` enum を撤去しました。OSC 連携（`com.hidano.facialcontrol.osc`、別 spec）で再導入される可能性があります（OQ1）。
8. **未認識 device は Controller fallback**: `InputDeviceCategorizer` が `<Keyboard>` 以外の prefix（`<Gamepad>` / `<Joystick>` / `<XRController>` / `<Pen>` / `<Touchscreen>` 含む）を Controller に分類します。それ以外の prefix（例: 独自の HID 拡張）は Controller fallback + `Debug.LogWarning` で続行します（Req 7.5）。

---

## 5. 自動移行 menu の不在に関する説明（Req 10.5）

v1.0 では **自動移行 Editor menu command を提供しません**。理由と対応方針を以下に明記します。

### 提供しない理由

- **preview 段階のため**: v0.x preview は対象ユーザーが限定的（Hidano + Junki Hiroi の 2 名体制 + 早期 preview ユーザー数名）であり、自動移行ロジックを実装・検証・保守するコストが破壊的改修の本質的価値（schema 簡素化）を上回ると判断しました。
- **AnimationClip 結合は半自動化が困難**: `BlendShapeValues[]` から AnimationClip への結合は、対象 SkinnedMeshRenderer の path / BlendShape 名の組み合わせが資産ごとに異なるため、Inspector 上で目視確認しながら手動移行するほうが安全です。
- **Hermite Curve 非対応の妥協が必須**: `Curve = Custom` を含む AnalogMappingFunction は preset 4 種への近似が必要で、人間の判断を介さない自動変換は精度を保証できません。

### 対応方針

- **本ドキュメントの手順を正典とする**: §2.2 〜 §2.6 の Step-by-Step に従い手動移行してください。手順は MIGRATION-v0.x-to-v1.0.md（本ファイル）でメンテナンスします。
- **ユーザー独自の移行スクリプトは歓迎**: 上記 §2 の対応表があれば preview ユーザー側で `EditorWindow` ベースの自動変換ツールを実装することは可能です。共有可能な実装ができた場合は Issue / PR で還元してください。
- **将来的な再検討**: 1.0 release 後に preview ユーザーから "破壊的改修ごとに自動移行を欲しい" というフィードバックが集まれば、`schema v2.x → v3.x` 等の次回破壊改修時に自動移行 menu の提供を再検討します（OQ3 と並列）。
- **Rollback 経路**: 万一 v1.0 移行で問題が発生した場合、§2.1 の backup から git revert で v0.x preview に戻してください。v0.x preview の packages-lock.json と SO / JSON があれば再現可能です。

---

## 関連ドキュメント

- `Packages/com.hidano.facialcontrol/CHANGELOG.md` — Breaking Change 表記と本ガイドへのリンク（Req 10.6）
- `Packages/com.hidano.facialcontrol.inputsystem/CHANGELOG.md` — InputSystem 連携側の変更履歴
- `Packages/com.hidano.facialcontrol/Documentation~/json-schema.md` — schema v2.0 の field 定義と JSON 例
- `.kiro/specs/inspector-and-data-model-redesign/requirements.md` — 本改修の要件定義（Req 1〜13）
- `.kiro/specs/inspector-and-data-model-redesign/design.md` — Phase 分割と Migration Guide 構造の正典
