# Changelog

すべての変更は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従います。

## 初回リリース

本パッケージはこれが初回リリースです。

### Changed

- `InputSystemAdapterBindingDrawer` のキーバインディング一覧に交互背景（`AlternatingRowBackground.ContentOnly`）を付け、複数フィールドで構成される各行の境界を視認しやすくした。あわせて一覧ヘッダー Foldout の開閉状態を `SessionState` に保存し、Inspector 再構築（domain reload / asset 再読み込み）後も直前の展開状態を復元するようにした（Editor 再起動時はリセット）。
- `InputSystemAdapterBindingDrawer` のキーバインディング一覧 `ListView` を固定行高（228px）から `DynamicHeight` 仮想化に変更し、BindingMode によって非表示になるフィールド分の空白で行が縦に間延びする問題を解消した。あわせて `minHeight`（120px）を撤去し、一覧を折りたたんだ際に下部へ無駄な空白が残る問題も解消した。

### Breaking Changes

- **自前の gaze 目ボーン適用を撤去し core `FacialController` の集約適用へ移行**: `InputSystemAdapterBinding` は `GazeBonePoseProvider` を構築・駆動しなくなった（`BuildGazeProvider` / `OnLateTick` での `Apply` / `Dispose` での破棄を撤去）。公開プロパティ **`HasGazeProvider` は削除**（参照コードはコンパイルエラー）。gaze 入力源の registry 登録（`{slug}:{expressionId}` / `.left` / `.right`）と、対応 `GazeBindingConfig` 欠落時の warning は従来どおり本 binding が担う。目ボーンへの適用は core の `FacialController` が registry 経由で行うため、**ユーザー設定（`GazeBindingConfig` / `ExpressionBindingEntry`）の変更は不要**で実行時挙動は同等。
- **`InputSystemAdapterBindingDrawer` の表示順を変更**: Input Drawer は InputActionAsset / Trigger bindings / Analog bindings / Gaze settings の順で、実際の設定フローに合わせて並び替えました。既存の Inspector 表示順との見た目互換は維持しません。
- **自動マイグレーション無し**: 過去のスクリーンショット、手順書、Inspector 操作順を前提にした説明は更新が必要です。既存データの自動変換は行いません。必要に応じて `InputSystemAdapterBinding` の内容を Inspector 上で確認し直してください。
- 根拠: spec `preview1-polish-pack` Req 6.5 / task 8.2。

### ⚠ BREAKING CHANGES

- **ExpressionTrigger 系の予約 ID を `controller-expr` / `keyboard-expr` から単一の `input` に統合**。`ControllerExpressionInputSource` / `KeyboardExpressionInputSource` の二系統クラス分離は撤廃され、単一の `ExpressionTriggerInputSource` (予約 ID `input`) に統一された。`ExpressionTriggerInputSource.InputReservedId = "input"` を新設し、旧 `ControllerReservedId` / `KeyboardReservedId` 定数は削除。`InputRegistration` も `RegisterReservedId(InputReservedId, ...)` の単一登録に変更。InputSystem の Action 名で device 種別が抽象化されるため、コア側で device 別 ID を分ける必要がなくなった旨の設計判断による。**後方互換は持たない**: 既存 SO / profile.json で `inputSources[].id` が `controller-expr` / `keyboard-expr` のままだと parse 時に warning + skip されるため、`input` (1 件) に書き換える必要がある。同梱サンプル (`Multi Source Blend Demo` の SO / profile.json / HUD) も新 ID に更新済み。
- **Gaze サブシステムを core パッケージへ移管**。`GazeExpressionConfig` の汎用フィールド (両目ボーン path / 初期回転 / yaw・pitch 軸 / 可動範囲 / Look 4 系統 clip / sample 配列) を core の新設 `Hidano.FacialControl.Adapters.ScriptableObject.GazeBindingConfig` に集約し、`GazeExpressionConfig` は `: GazeBindingConfig` 派生クラスとして `InputActionReference inputAction` のみを保持する形に縮小。フィールドアクセス (`cfg.leftEyeBonePath` 等) は継承により互換維持されるが、**namespace 由来の using 文や型直参照は破壊**: `Hidano.FacialControl.InputSystem.Adapters.ScriptableObject.GazeBlendShapeSampleEntry` → `Hidano.FacialControl.Adapters.ScriptableObject.GazeBlendShapeSampleEntry` へ移動。
- **`GazeBonePoseProvider` を core (`Hidano.FacialControl.Adapters.Bone`) へ移管 + コンストラクタ署名変更**。旧 `(BoneTransformResolver, IReadOnlyDictionary<string, IAnalogInputSource>, IReadOnlyList<GazeExpressionConfig>)` から、新 `(BoneTransformResolver, IReadOnlyList<GazeBoneBinding>)` へ。`GazeBoneBinding` は `(GazeBindingConfig, IAnalogInputSource)` のペア readonly struct。sourceId 解決の責務は呼出側 (`FacialCharacterInputExtension`) に移動。これにより本 provider は Unity InputSystem に依存せず、OSC や ARKit 経路から目線ボーン制御を再利用できる。
- **`GazeClipBlendShapeSampler` を core (`Hidano.FacialControl.Editor.Sampling`) へ移管**。旧 `Hidano.FacialControl.InputSystem.Editor.Sampling.GazeClipBlendShapeSampler` を参照していた外部コードは破壊。
- **`FacialCharacterSOInspector` を派生 inspector へ縮小**。汎用 UI 部分 (Layers / Expressions / Gaze (bone+clip) / Reference Model / Debug / 自動保存) は core の新設 `FacialCharacterProfileSOInspector` 基底に集約された。本 inspector は `: FacialCharacterProfileSOInspector` を継承し、InputSystem 固有部分 (InputActionAsset 選択 / ExpressionBindings / Gaze の `InputActionReference` フィールド / analog_bindings.json 出力 / `.inputactions` 編集追従) のみを担う 439 行構成に縮小（旧 約 2067 行 → 約 79% 削減）。`[CustomEditor(typeof(FacialCharacterSO))]` 属性と完全修飾型名 `Hidano.FacialControl.InputSystem.Editor.Inspector.FacialCharacterSOInspector` は維持されているため reflection 経由のテストは互換。
- `GazeExpressionConfig` の BlendShape 経路を 4 string field (`leftEyeXBlendShape` / `leftEyeYBlendShape` / `rightEyeXBlendShape` / `rightEyeYBlendShape`) から 4 AnimationClip 参照 (`lookLeftClip` / `lookRightClip` / `lookUpClip` / `lookDownClip`) に置換。Vector2 入力の +X / -X / +Y / -Y がそれぞれ LookRight / LookLeft / LookUp / LookDown clip に対応する。既存 SO の旧 BS string 値はロード時に黙って無視される。
- 目線ボーンの runtime 反映経路を `AnalogBonePoseProvider` (Euler 加算) から専用 `GazeBonePoseProvider` (quaternion 合成 + 可動範囲制限) に切替えた。`FacialCharacterSO.BuildAnalogProfileFromGazeConfigs` は `BonePose` の `AnalogBindingEntry` を生成しなくなり、目線ボーンの軸変換と角度クランプは `GazeBonePoseProvider` が担う。**既存 SO は 目線ボーン foldout の「参照モデルから自動設定」を再実行**して新しい yaw/pitch 軸と可動角を再構築する必要がある。
- `ExpressionBindingEntry` の既定動作モードが `Toggle` から `Hold` に変更。`triggerMode` field を追加し、未指定時は押している間だけ表情が ON、リリースで OFF になる。**既存 SO の挙動が変化する**ため、これまで通りトグル動作を維持したいバインディングは Inspector で動作モードを `Toggle` に戻す。

### Added

- `Hidano.FacialControl.Domain.Models.TriggerMode` (`Hold` / `Toggle`) と `Hidano.FacialControl.Domain.Models.InputBinding.TriggerMode` フィールドを新設し、ボタン押下時の挙動を選択可能にした。
- `ExpressionBindingEntry` に `triggerMode` フィールドを追加し、`FacialCharacterSOInspector` のキーバインディング行に動作モード選択 UI (EnumField) を追加した。
- `GazeExpressionConfig` に左右目それぞれの yaw/pitch local 軸 (`leftEyeYawAxisLocal` / `leftEyePitchAxisLocal` / `rightEyeYawAxisLocal` / `rightEyePitchAxisLocal`) と、可動範囲制限フィールド (`lookUpAngle` / `lookDownAngle` / `outerYawAngle` / `innerYawAngle`) を追加した。
- `Hidano.FacialControl.InputSystem.Adapters.Bone.GazeBonePoseProvider` を新設し、quaternion 合成で世界の上下/左右軸基準に目線ボーン rotation を計算するようにした。`FacialCharacterInputExtension` が GazeConfig から構築・Tick 駆動する。
- `GazeExpressionConfig` に 4 系統 AnimationClip フィールドと、Editor で焼き付けた sample 配列 `lookLeftSamples` / `lookRightSamples` / `lookUpSamples` / `lookDownSamples` (`List<GazeBlendShapeSampleEntry>`) を追加。runtime はこの sample 配列から `AnalogBindingEntry` を構築する。
- `GazeBlendShapeSampleEntry` 型 (`blendShapeName: string` / `weight: float`) を新設。
- `Hidano.FacialControl.InputSystem.Editor.Sampling.GazeClipBlendShapeSampler` (Editor 専用) — 4 系統 clip の time=0 における BlendShape weight を抽出する `AnimationUtility` ベースのヘルパ。
- `FacialCharacterSOAutoExporter.SampleGazeClipsIntoConfigs` — SO 保存時に 4 系統 clip を sample して上記 sample 配列に永続化する経路を追加。
- `FacialCharacterSOInspector` の Gaze BS セクションを 4 ObjectField (AnimationClip) に置換。同セクションの `BuildGazeClipField` ヘルパと、UIElements name 定数 (`ExpressionRowGazeLookLeftClipName` 等) を新設。
- `FacialCharacterSOInspector` の目線ボーン foldout に「参照モデルから自動設定」ボタンを追加。`_referenceModel` の Animator から Humanoid Avatar マッピング優先 (LeftEye / RightEye)、不在時は名前検索 (`*eye*`) で目ボーンを解決し、`Transform.name` と現在の `localEulerAngles` を初期回転として書き込む。同ボタンは新たに、参照モデル時点の親 Transform を介して「世界の上下/左右軸が当該目ボーンの local 空間でどの方向に対応するか」を算出し yaw/pitch 軸として保存する。
- `FacialCharacterSOInspector` のアナログ表情行に「可動範囲 (角度制限)」 foldout を追加。上方向/下方向/外側 (yaw)/内側 (yaw) の 4 角度を Slider で編集できる。

### Changed

- `FacialCharacterSOAutoExporter` を core の `Hidano.FacialControl.Editor.AutoExport.FacialCharacterProfileExporter` に delegate する構成に再構成。`SampleAnimationClipsIntoCachedSnapshots` と profile.json 出力 (`ExportProfileJson` 相当) は core 側で実装され、本クラスは AssetModificationProcessor フック / 進捗 / abort パイプライン / Gaze clip サンプリング (`SampleGazeClipsIntoConfigs`) / `analog_bindings.json` 追加出力 / test seam (`SamplerOverride` 等) に責務を絞った。公開 API (`ExportToStreamingAssets`, `SampleAnimationClipsIntoCachedSnapshots`, `ProcessAssetSavePaths`, `IElapsedStopwatch` 等) のシグネチャは互換維持。
- `FacialCharacterInputExtension` を新 `GazeBonePoseProvider` API に対応。`so.GazeConfigs` から `(cfg, _activeSources[cfg.inputAction.action.name])` ペアを構築して `List<GazeBoneBinding>` に詰め、`GazeBonePoseProvider(resolver, bindings)` へ渡す形に変更。未解決 sourceId はここで warning + skip され、provider はもはや辞書経路の lookup を持たない。
- `Adapters.Input.InputSystemAdapter.BindExpression` に `TriggerMode` を受ける overload を追加し、Hold モードでは `started` / `canceled`、Toggle モードでは `performed` を購読する分岐を実装。既存の 2-arg overload は後方互換のため `Toggle` で固定。
- `FacialCharacterInputExtension.BindAllExpressions` がバインディングごとの `TriggerMode` を `InputSystemAdapter` に伝播するようになった。
- `FacialCharacterInputExtension` が GazeConfig がある場合に `GazeBonePoseProvider` を構築・LateUpdate で Apply / Teardown 時に Dispose するようになった。`AnalogBonePoseProvider` は引き続き非目線ボーンや legacy `_analogBindings` 経路で使用される。
- `FacialCharacterSO.BuildAnalogProfileFromGazeConfigs` を 4 sample list 経路に変更し、各 BS の keyframe weight を `AnalogBindingEntry.Scale` に、+X/-X/+Y/-Y 振り分けを `AnalogBindingDirection` (Positive / Negative) に設定して emit する。目線ボーン制御は `GazeBonePoseProvider` に移管したため `BonePose` 系の entry は emit しなくなった。
- `Adapters.Json.Dto.AnalogBindingEntryDto` の scale / direction 追加に伴い JSON round-trip も更新（core パッケージ側）。
- `GazeExpressionConfig` の可動角デフォルト値を Sample 想定値に揃えた: `lookDownAngle` 12° → 9°、`outerYawAngle` 30° → 15°（`lookUpAngle` 15° / `innerYawAngle` 18° は据え置き）。既存 SO で値を明示保存していなければ次回保存時に新デフォルトが書き込まれる。

### Fixed

- `InputSystemAdapterBindingDrawer` の Overlay slot 候補を定期リフレッシュするタイマーが、Inspector を別オブジェクトへ切り替えた後も破棄済み `SerializedObject` へ `Update()` を呼び続け、`NullReferenceException` がティックごとに大量発生する問題を修正。破棄を検出した時点でタイマーを停止するようにした。
- `GazeBonePoseProvider` で目線入力 (Vector2) が上下左右とも反転して目ボーンに反映されていた問題を修正。Unity の `Quaternion.AngleAxis` 左手系規約と参照モデル自動取得の yaw/pitch 軸 (+Y / +X) の組み合わせで生じていた符号反転をコード側で吸収し、InputActionAsset の Invert processor に頼らずとも入力 +X = 視線右、+Y = 視線上 で動作するようにした。

### Removed

- `FacialCharacterSO.AppendBoneBinding` private ヘルパを撤去 (目線ボーンは `GazeBonePoseProvider` で扱うため)。
- `FacialCharacterSOInspector.BuildGazeBlendShapeField` (TextField 版) を削除。
- 旧定数 `ExpressionRowGazeLeftXName` / `ExpressionRowGazeLeftYName` / `ExpressionRowGazeRightXName` / `ExpressionRowGazeRightYName` を削除。

### ⚠ BREAKING CHANGES

> 本リリースは spec `inspector-and-data-model-redesign` に基づく InputSystem 連携の全面改修を含みます。**旧バージョンの `FacialCharacterSO` / `*.inputactions` / Scene / Prefab はそのままでは動作しません**。アップグレード前に必ず [`Migration Guide v0.x → v1.0`](Documentation~/MIGRATION-v0.x-to-v1.0.md) の手順で資産を変換してください（Req 10.1, 10.6）。

#### MonoBehaviour / Inspector の撤去

- `KeyboardExpressionInputSource` / `ControllerExpressionInputSource` MonoBehaviour — `ExpressionInputSourceAdapter` 1 個（`[DisallowMultipleComponent]`）に統合
- `ExpressionBindingEntry.Category` field — `InputDeviceCategorizer.Categorize(InputBinding.path)` で自動分類
- `InputSourceCategory` enum — 参照ゼロ確認のうえ撤去（OSC 別 spec で再導入の余地）
- `FacialCharacterSO.GetExpressionBindings(InputSourceCategory category)` — 引数なし版に縮退（adapter 側が dispatch を吸収）
- `FacialCharacterInputExtension.BindCategory` の二重ループ — 1 ループに統合

#### アナログバインディングの撤去

- `AnalogBindingEntry.Mapping`（`AnalogMappingFunction` 埋め込み） — `*.inputactions` 内の processors 文字列に移行
- `AnalogBindingEntrySerializable` を `inputActionRef + targetIdentifier + targetAxis` の 3 field に縮退
- `AnalogMappingFunctionSerializable` を物理削除

### Added

- `ExpressionInputSourceAdapter` MonoBehaviour — Keyboard / Controller を統合した入力源 adapter。内部に keyboard / controller 用の 2 つの sink を composition で保持し、`InputAction.bindings[0].path` を `InputDeviceCategorizer` で分類して dispatch。未認識 device は Controller 側 + `Debug.LogWarning` 1 回（Req 7.5）、unsupported processor 検知時は raw value 続行 + warning（Req 6.6）。per-frame 0-alloc 維持（Req 11.3）
- `Adapters.Input.InputDeviceCategorizer` — `Categorize(string bindingPath, out bool wasFallback)` 静的メソッド。`<Keyboard>` prefix → Keyboard、`<Gamepad>` / `<Joystick>` / `<XRController>` / `<Pen>` / `<Touchscreen>` → Controller、未認識は Controller fallback。`string.StartsWith(prefix, StringComparison.Ordinal)` で 0-alloc
- 6 種カスタム InputProcessor:
  - `analogDeadZone`（`min` / `max` の `float` field、abs ≤ min は 0、それ以外は `Sign(value) * Clamp01(normalized)`）
  - `analogScale`（`factor: float`、`value * factor`）
  - `analogOffset`（`offset: float`、`value + offset`）
  - `analogClamp`（`min` / `max: float`、`Mathf.Clamp(value, min, max)`）
  - `analogInvert`（`-value`）
  - `analogCurve`（`preset: int`、0=Linear, 1=EaseIn, 2=EaseOut, 3=EaseInOut。Custom Hermite は v2.1 以降）
- `Adapters.Processors.AnalogProcessorRegistration` — `[InitializeOnLoad]` + `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` で 6 processor を `InputSystem.RegisterProcessor` 一括登録
- `Documentation~/MIGRATION-v0.x-to-v1.0.md` — v0.x → v1.0 手動移行ガイド（Schema 差分一覧 / Step-by-Step 手順 / AnimationClip メタデータ運搬規約 / 既知の制限）

### Changed

- `InputSystemAdapter.BindExpression(action, expression)` の入口で `InputDeviceCategorizer.Categorize(action.bindings[0].path)` を呼んで分岐
- 同梱サンプル（`Multi Source Blend Demo` / `Analog Binding Demo`）を schema v2.0 / processors 文字列ベースで再生成

### Removed

- `Adapters.InputSources.KeyboardExpressionInputSource` / `ControllerExpressionInputSource` MonoBehaviour（`.meta` 含む）
- `Adapters.ScriptableObject.ExpressionBindingEntry.Category` field
- `Adapters.ScriptableObject.Serializable.AnalogMappingFunctionSerializable`

初回リリースで `com.hidano.facialcontrol` の Unity InputSystem 連携アダプタとして提供します。

### Added

- `InputSystemAdapter` — InputAction Asset との連携（Button / Value 両対応）
- `KeyboardExpressionInputSource` / `ControllerExpressionInputSource`（予約 id `keyboard-expr` / `controller-expr`）
- `InputBindingProfileSO`（旧形式）— InputActionAsset / ActionMap / バインディングペアの永続化
- `FacialInputBinder`（MonoBehaviour）— `InputBindingProfileSO` 読み込み + Action ↔ Expression バインド
- `InputFacialControllerExtension` — `InputSourceFactory.RegisterReserved` 経由でトリガー入力源を登録
- `InputBindingProfileSOEditor`（UI Toolkit Inspector）
- `Runtime/Adapters/Input/FacialControlDefaultActions.inputactions`（Xbox コントローラ LT/RT バインディング含む）
- `FacialCharacterSO`（統合 SO）と `FacialCharacterInputExtension`
- 同梱サンプル `Multi Source Blend Demo` / `Analog Binding Demo`
